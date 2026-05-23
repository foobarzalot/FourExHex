using System;
using System.Collections.Generic;

/// <summary>
/// Return value from a single tick of the chunked instant driver
/// (<c>GameController.RunInstantTick</c>). Used by both the live-AI
/// instant step (<c>AiInstantStep</c>) and the replay instant step
/// (<see cref="ReplayRecorder.ReplayInstantStep"/>) to signal whether
/// the loop should continue draining within budget, yield at a turn
/// boundary, or stop entirely.
/// </summary>
public enum InstantStep { Continued, TurnBoundary, Exhausted }

/// <summary>
/// Replay subsystem extracted from <see cref="GameController"/>. Owns
/// the recorded <see cref="ReplayBeat"/> log (the parallel-to-undo
/// action history), the per-game initial snapshot used to rewind
/// playback, the parallel undo/redo beat-stack bookkeeping, and both
/// playback step machines (paced + instant). Every mutation goes
/// through <see cref="GameOperations"/>; the recorder does not
/// reference <see cref="GameController"/> directly. The
/// <see cref="GameController.BeginReplay"/> and
/// <see cref="GameController.RecordTutorialOnlyBeat"/> public methods
/// are thin forwarders over <see cref="BeginReplay"/> and
/// <see cref="RecordTutorialOnlyBeat"/>.
/// </summary>
public class ReplayRecorder
{
    private readonly GameState _state;
    private readonly SessionState _session;
    private readonly IHexMapView _map;
    private readonly GameOperations _ops;
    private readonly IAiPacer _aiPacer;
    private readonly Func<bool>? _replayIsInstantMode;
    private readonly Action _instantTickEntry;
    private readonly bool _previewMode;

    // Pacing constants for the paced replay step machine. Same values
    // as GameController's AI step machine — replay matches AI cadence
    // so the two are visually equivalent to a viewer.
    private const int AiPreviewDelayMs = 350;
    private const int AiActionDelayMs = 300;
    private const int AiBetweenPlayersDelayMs = 600;
    // Mirrors GameController.InstantTurnDelayMs (the per-turn cadence of
    // the chunked instant driver). Duplicated here the same way the
    // paced delays above are, so the recorder can re-dispatch the
    // instant track without reaching into GameController.
    private const int InstantTurnDelayMs = 200;

    // The replay log lives parallel to the per-turn undo stack: every
    // state-mutating action by every player (human and AI) appends a
    // ReplayBeat here. The list is never cleared by EndTurn or by load;
    // it grows monotonically for the lifetime of the game. BeginReplay
    // restores InitialSnapshot, then steps through the beats via
    // IAiPacer.
    private readonly List<ReplayBeat> _replayBeats = new();
    private GameStateSnapshot? _initialSnapshot;
    private int _initialTurnNumber;
    private int _initialCurrentPlayerIndex;
    private bool _replayDataIsCompleteFromStart;

    private bool _replayMode;
    private int _replayIndex;
    private bool _replayInstantActive;

    // Parallel bookkeeping for undo/redo: track the beat-list size at
    // the moment each UndoEntry was pushed, so undo can trim
    // _replayBeats back to that size and stash the popped beats for
    // redo to restore. Synchronized externally with _session.Undo via
    // every push/pop site on GameController.
    private readonly Stack<int> _undoBeatCounts = new();
    private readonly Stack<List<ReplayBeat>> _redoBeatLists = new();

    public ReplayRecorder(
        GameState state,
        SessionState session,
        IHexMapView map,
        GameOperations ops,
        IAiPacer aiPacer,
        bool previewMode,
        Func<bool>? replayIsInstantMode,
        Action instantTickEntry,
        Replay? loadedReplay)
    {
        _state = state;
        _session = session;
        _map = map;
        _ops = ops;
        _aiPacer = aiPacer;
        _previewMode = previewMode;
        _replayIsInstantMode = replayIsInstantMode;
        _instantTickEntry = instantTickEntry;

        if (loadedReplay != null)
        {
            _initialSnapshot = loadedReplay.InitialSnapshot;
            _initialTurnNumber = loadedReplay.InitialTurnNumber;
            _initialCurrentPlayerIndex = loadedReplay.InitialCurrentPlayerIndex;
            _replayBeats.AddRange(loadedReplay.Beats);
            _replayDataIsCompleteFromStart = true;
        }
    }

    // --- Public read surface --------------------------------------------

    public IReadOnlyList<ReplayBeat> Beats => _replayBeats;
    public int BeatsCount => _replayBeats.Count;
    public GameStateSnapshot? InitialSnapshot => _initialSnapshot;
    public int InitialTurnNumber => _initialTurnNumber;
    public int InitialCurrentPlayerIndex => _initialCurrentPlayerIndex;
    public bool IsCompleteFromStart => _replayDataIsCompleteFromStart;
    public bool HasInitialSnapshot => _initialSnapshot != null;
    public bool IsReplaying => _replayMode;
    public bool IsInstantModeActive => _replayInstantActive;

    // --- Initial snapshot capture (Phase 2 will own BeginReplay too) ---

    /// <summary>
    /// Capture the initial snapshot for a fresh game (StartGame) or a
    /// loaded save without prior replay data (Resume on a v3 save).
    /// <paramref name="markCompleteFromStart"/> is true only on fresh
    /// StartGame; v3-save resumes set it false so the Replay button
    /// stays disabled for that session.
    /// </summary>
    public void CaptureInitialSnapshot(
        GameStateSnapshot snapshot,
        int turnNumber,
        int currentPlayerIndex,
        bool markCompleteFromStart)
    {
        _initialSnapshot = snapshot;
        _initialTurnNumber = turnNumber;
        _initialCurrentPlayerIndex = currentPlayerIndex;
        if (markCompleteFromStart) _replayDataIsCompleteFromStart = true;
    }

    // --- Recording ------------------------------------------------------

    /// <summary>
    /// Append a typed beat to the replay log, stamping
    /// <see cref="ReplayBeat.Index"/>, <see cref="ReplayBeat.Turn"/>,
    /// and <see cref="ReplayBeat.Actor"/> from the current turn state.
    /// Called from every state-mutation site; gated on
    /// <see cref="IsReplaying"/> by each caller so playback doesn't
    /// re-record the beats it's replaying. Preview-mode no-ops here so
    /// the gate doesn't have to live at every call site.
    /// </summary>
    public void RecordBeat(ReplayBeat beat)
    {
        if (_previewMode) return;
        ReplayBeat stamped = beat with
        {
            Index = _replayBeats.Count,
            Turn = _state.Turns.TurnNumber,
            Actor = _state.Turns.CurrentPlayerIndex,
        };
        _replayBeats.Add(stamped);
    }

    /// <summary>
    /// Append an authored tutorial-only beat to the recording log. Used
    /// by RecordPane's "+ Text" authoring path via the public forwarder
    /// on <see cref="GameController"/>. Stamps Index + Turn from current
    /// state and forces <see cref="ReplayBeat.Actor"/> = -1 (no player
    /// owns these). Gated on replay/preview mode: silently no-ops outside
    /// of an active recording.
    /// </summary>
    public void RecordTutorialOnlyBeat(TutorialOnlyBeat beat)
    {
        if (_replayMode || _previewMode) return;
        ReplayBeat stamped = beat with
        {
            Index = _replayBeats.Count,
            Turn = _state.Turns.TurnNumber,
            Actor = -1,
        };
        _replayBeats.Add(stamped);
    }

    // --- Undo/redo beat-stack bookkeeping ------------------------------

    /// <summary>
    /// Single sync point called by <c>GameController.TrackHandler</c>
    /// right after pushing onto <c>_session.Undo</c>: stamps the
    /// pre-handler beat-list size so undo can later trim back to it,
    /// and clears the redo stash because new forward history invalidates
    /// the previous forward branch.
    /// </summary>
    public void OnHumanHandlerCommitted(int beatsBefore)
    {
        _undoBeatCounts.Push(beatsBefore);
        _redoBeatLists.Clear();
    }

    /// <summary>
    /// Pop one entry from <see cref="_undoBeatCounts"/> and stash the
    /// corresponding tail of the beat log onto <see cref="_redoBeatLists"/>.
    /// Mirrors a single <see cref="UndoStack{T}.UndoLast"/> pop on the
    /// replay side.
    /// </summary>
    public void PopOneBeatBatchForUndo()
    {
        int targetCount = _undoBeatCounts.Pop();
        var popped = new List<ReplayBeat>();
        for (int i = targetCount; i < _replayBeats.Count; i++)
        {
            popped.Add(_replayBeats[i]);
        }
        _replayBeats.RemoveRange(targetCount, _replayBeats.Count - targetCount);
        _redoBeatLists.Push(popped);
    }

    /// <summary>
    /// Pop one stashed batch from <see cref="_redoBeatLists"/> and
    /// re-append to <see cref="_replayBeats"/>, recording the new
    /// pre-batch count on <see cref="_undoBeatCounts"/>. Mirrors a
    /// single <see cref="UndoStack{T}.RedoLast"/>.
    /// </summary>
    public void PushOneBeatBatchForRedo()
    {
        List<ReplayBeat> toRestore = _redoBeatLists.Pop();
        _undoBeatCounts.Push(_replayBeats.Count);
        _replayBeats.AddRange(toRestore);
    }

    /// <summary>
    /// Clear the parallel beat-tracking stacks. Called by
    /// <c>GameController.ClearUndoAndReplayBookkeeping</c> in lockstep
    /// with <c>_session.Undo.Clear()</c> at the three "no more undo"
    /// commit sites (end of turn, mid-turn domination, claim-victory
    /// win). Does NOT touch the beat log itself — those beats are
    /// committed history.
    /// </summary>
    public void ClearBookkeeping()
    {
        _undoBeatCounts.Clear();
        _redoBeatLists.Clear();
    }

    // --- Playback ------------------------------------------------------

    /// <summary>
    /// Rewind the game to <see cref="InitialSnapshot"/> and begin
    /// paced playback of <see cref="Beats"/>. While playing,
    /// <see cref="IsReplaying"/> is true: every input handler on
    /// <c>GameController</c> early-returns and the <c>HumanTurnStarted</c>
    /// autosave hook is suppressed. The view's victory overlay is hidden
    /// (Winner is reset) until the recorded game-ending beat re-fires it.
    /// </summary>
    public void BeginReplay()
    {
        if (_initialSnapshot == null) return;
        _aiPacer.Cancel();
        _replayMode = true;
        _replayIndex = 0;
        _ops.GameEndedFired = false;
        _ops.HumanTurnFiredForCurrentTurn = false;

        _state.Territories = _initialSnapshot.ApplyTo(_state.Grid, _state.Treasury);
        _state.Turns.Reset(_initialCurrentPlayerIndex, _initialTurnNumber);
        _session.Winner = null;
        _session.PendingDefeatScreen = null;
        _session.PendingClaimVictory = null;
        _session.ClaimVictoryPromptedHighestThreshold.Clear();
        _session.ClearPendingAction();
        _session.SelectedTerritory = null;
        // Note: the parallel undo stack + beat-stack bookkeeping is
        // cleared by the caller (GameController.BeginReplay forwarder
        // via ClearUndoAndReplayBookkeeping) BEFORE this method runs,
        // so the three stacks stay in lockstep.

        _replayInstantActive = _replayIsInstantMode?.Invoke() == true;

        _map.RebuildAfterTerritoryChange();
        _map.ShowHighlight(null);
        _map.ClearAllOverlays();
        // Set silent BEFORE the setup refresh so even the rewind paint
        // skips tweens. Instant playback never shows per-beat highlights.
        if (_replayInstantActive) _map.SetSilentMode(true);
        _ops.RefreshViews();

        // First dispatch is a turn boundary; the track was just seeded
        // above so this won't register a spurious transition.
        ScheduleNextReplayBeat(turnBoundary: true);
    }

    /// <summary>
    /// Single re-dispatching decision point for advancing replay
    /// playback. Called by <see cref="StepReplayExecute"/> after each
    /// beat and by the chunked instant driver's reschedule, so a
    /// mid-replay Replay-Speed change switches tracks at the next beat:
    /// Instant routes to the chunked driver (unscaled), otherwise the
    /// paced preview/execute machine. Re-reads
    /// <see cref="_replayIsInstantMode"/> each call, syncs silent mode,
    /// and on an instant→paced transition forces the structural rebuild
    /// the instant track's suppressed per-capture rebuilds skipped.
    /// </summary>
    public void ScheduleNextReplayBeat(bool turnBoundary)
    {
        if (!_replayMode) return;
        bool nowInstant = _replayIsInstantMode?.Invoke() == true;
        if (_replayInstantActive && !nowInstant)
        {
            Log.Info(Log.LogCategory.Turn,
                $"[speed] replay track instant→paced at beat {_replayIndex}");
            // Instant suppressed per-capture rebuilds; refresh borders
            // before the first paced render.
            _map.RebuildAfterTerritoryChange();
        }
        else if (!_replayInstantActive && nowInstant)
        {
            Log.Info(Log.LogCategory.Turn,
                $"[speed] replay track paced→instant at beat {_replayIndex}");
            // Instant playback shows no per-beat highlight; clear the
            // acting-territory outline the paced track last drew so it
            // doesn't linger through the fast-forward.
            _map.ShowHighlight(null);
        }
        _replayInstantActive = nowInstant;
        // Replay's silent flag is driven directly by the instant flag
        // (no AI "Opponents…" overlay), mirroring BeginReplay/EndReplay.
        _map.SetSilentMode(nowInstant);
        if (nowInstant)
            _aiPacer.ScheduleUnscaled(_instantTickEntry, turnBoundary ? InstantTurnDelayMs : 0);
        else
            _aiPacer.Schedule(StepReplayPreview, turnBoundary ? AiBetweenPlayersDelayMs : AiActionDelayMs);
    }

    private void StepReplayPreview()
    {
        if (!_replayMode) return;
        if (_replayIndex >= _replayBeats.Count) { EndReplay(); return; }

        ReplayBeat beat = _replayBeats[_replayIndex];
        _map.ShowHighlight(ResolveReplayActingTerritory(beat));
        _ops.RefreshViews();

        int delay = beat is ReplayEndTurnBeat ? AiActionDelayMs : AiPreviewDelayMs;
        _aiPacer.Schedule(StepReplayExecute, delay);
    }

    private void StepReplayExecute()
    {
        if (!_replayMode) return;
        if (_replayIndex >= _replayBeats.Count) { EndReplay(); return; }

        ReplayBeat beat = _replayBeats[_replayIndex++];
        bool crossesTurn = beat is ReplayEndTurnBeat;

        ExecuteReplayBeat(beat);

        _ops.CheckGameEndConditions();
        _ops.RefreshViews();

        if (_session.IsGameOver) { EndReplay(); return; }

        // Beat boundary: re-dispatch so a mid-replay Replay-Speed change
        // switches tracks for the next beat.
        ScheduleNextReplayBeat(turnBoundary: crossesTurn);
    }

    /// <summary>
    /// Dispatch a single replay beat onto the same mutation paths the
    /// live game uses. Shared by the paced step machine
    /// (<see cref="StepReplayExecute"/>) and the chunked
    /// <see cref="ReplayInstantStep"/> so the two playback modes can't
    /// drift. Does NOT advance <see cref="_replayIndex"/>, run the
    /// game-end check, or refresh views — callers own pacing.
    /// </summary>
    private void ExecuteReplayBeat(ReplayBeat beat)
    {
        switch (beat)
        {
            case ReplayMoveBeat mv:
                _ops.ExecuteAiMove(mv.From, mv.To);
                break;
            case ReplayBuyBeat bu:
                _ops.ExecuteAiBuyUnit(bu.Capital, bu.To, bu.Level);
                break;
            case ReplayBuildTowerBeat bt:
                _ops.ExecuteAiBuildTower(bt.Capital, bt.To);
                break;
            case ReplayEndTurnBeat _:
                ReplayApplyEndTurn();
                break;
            case ReplayClaimVictoryBeat cv:
                PlayerId cvColor = _state.Turns.CurrentPlayer.Id;
                _session.ClaimVictoryPromptedHighestThreshold[cvColor] = cv.ThresholdPercent;
                _ops.DeclareWinner(cvColor);
                break;
            case ReplayDismissClaimBeat dcv:
                PlayerId dcColor = _state.Turns.CurrentPlayer.Id;
                _session.ClaimVictoryPromptedHighestThreshold[dcColor] = dcv.ThresholdPercent;
                break;
            case ReplayDismissDefeatBeat _:
                _session.PendingDefeatScreen = null;
                break;
            case ReplayLongPressRallyBeat rally:
                _ops.ApplyLongPressRally(rally.Target);
                break;
            case TutorialOnlyBeat _:
                // Tutorial-only beats (e.g., narration text) are
                // authoring-only — the in-game Replay button silently
                // skips them. Tutorial Preview consumes them through
                // TutorialNarrationDriver instead.
                break;
        }
    }

    /// <summary>
    /// Replay's EndTurn dispatch. Runs the same end-of-turn bookkeeping
    /// as the live <c>EndTurnNow</c> (win check, advance player, start
    /// player turn) but does NOT trigger the AI loop — in replay, every
    /// action is explicit in the beat log.
    /// </summary>
    private void ReplayApplyEndTurn()
    {
        _ops.EndOfTurnProcessing();
        if (_session.IsGameOver) return;
        _ops.AdvanceToNextActivePlayer();
        _ops.StartPlayerTurn();
    }

    /// <summary>
    /// Step function for the chunked instant-replay driver. Returns
    /// <see cref="GameController.InstantStep.Exhausted"/> on beat-list
    /// end or game-over; <c>TurnBoundary</c> on end-of-turn beats;
    /// <c>Continued</c> otherwise.
    /// </summary>
    public InstantStep ReplayInstantStep()
    {
        if (_replayIndex >= _replayBeats.Count) return InstantStep.Exhausted;
        ReplayBeat beat = _replayBeats[_replayIndex++];
        bool isEndTurn = beat is ReplayEndTurnBeat;
        ExecuteReplayBeat(beat);
        _ops.CheckGameEndConditions();
        if (_session.IsGameOver) return InstantStep.Exhausted;
        return isEndTurn ? InstantStep.TurnBoundary : InstantStep.Continued;
    }

    /// <summary>
    /// Finish action for both the paced and instant playback drivers.
    /// Lifts silent mode, does one final rebuild for instant (which
    /// suppressed per-capture rebuilds), then a closing refresh.
    /// </summary>
    public void EndReplay()
    {
        bool wasInstant = _replayInstantActive;
        _replayMode = false;
        _replayInstantActive = false;
        _ops.SuppressMapRebuild = false;
        _aiPacer.Cancel();
        // Lift silent mode so the final game-over board (winner overlay,
        // last-move state) renders with normal audio/VFX. No-op for
        // non-instant replay, which never silenced the view.
        _map.SetSilentMode(false);
        // Instant replay suppressed every per-capture rebuild, so the
        // border layer is stale; do one final structural rebuild before
        // the closing refresh. Non-instant replay already rebuilt per
        // capture, so this is instant-only.
        if (wasInstant) _map.RebuildAfterTerritoryChange();
        _map.ShowHighlight(null);
        _ops.RefreshViews();
    }

    /// <summary>
    /// Acting-territory resolver for replay preview highlights. Mirrors
    /// the AI-side resolver but covers the broader beat-kind family
    /// (human-only beats too).
    /// </summary>
    private Territory? ResolveReplayActingTerritory(ReplayBeat beat)
    {
        PlayerId owner = _state.Turns.CurrentPlayer.Id;
        return beat switch
        {
            ReplayMoveBeat mv => TerritoryLookup.FindOwnedContaining(_state.Territories, owner, mv.From),
            ReplayBuyBeat bu => TerritoryLookup.FindOwnedContaining(_state.Territories, owner, bu.Capital),
            ReplayBuildTowerBeat bt => TerritoryLookup.FindOwnedContaining(_state.Territories, owner, bt.Capital),
            ReplayLongPressRallyBeat rally => TerritoryLookup.FindOwnedContaining(_state.Territories, owner, rally.Target),
            _ => null,
        };
    }
}
