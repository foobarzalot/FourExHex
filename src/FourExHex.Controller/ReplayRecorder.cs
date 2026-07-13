using System;
using System.Collections.Generic;

/// <summary>
/// Return value from a single tick of the chunked instant driver
/// (<see cref="GameOperations.RunInstantTick"/>). Used by both the
/// live-AI instant step (<c>AiTurnDriver.AiInstantStep</c>) and the
/// replay instant step (<see cref="ReplayRecorder.ReplayInstantStep"/>)
/// to signal whether the loop should continue draining within budget,
/// yield at a turn boundary, or stop entirely.
/// </summary>
public enum InstantStep { Continued, TurnBoundary, Exhausted }

/// <summary>
/// Result of the replay divergence check: set when a loaded
/// replay, re-executed under the current rules, lands on a board whose
/// <see cref="GameStateChecksum"/> differs from the recorded final board.
/// Both checksums are computed by the current binary, so a divergence
/// reflects a genuine board difference (e.g. a gameplay-rule change since
/// the replay was recorded), not a checksum-format change.
/// </summary>
public sealed record ReplayDivergence(string Expected, string Actual);

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
    /// <summary>Raised at the end of <see cref="EndReplay"/> — playback
    /// finished (beat log exhausted, game-over, or aborted). Forwarded by
    /// <c>GameController.ReplayEnded</c>; the Instructions demo player
    /// uses it to loop playback.</summary>
    public event Action? ReplayEnded;

    private readonly GameState _state;
    private readonly SessionState _session;
    private readonly IHexMapView _map;
    private readonly GameOperations _ops;
    private readonly IAiPacer _aiPacer;
    private readonly Func<bool>? _replayIsInstantMode;
    private readonly bool _previewMode;

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

    // Divergence detection. Captured once at the first BeginReplay
    // (before the rewind) from the recorded end board — loaded.State for
    // a save, or the finished live board. EndReplay recomputes the
    // replayed board and compares. Capturing once (and only here) keeps
    // the baseline stable across repeated replays of the same game. The
    // canonical string is kept alongside the hash purely for the Debug
    // first-difference diagnostic.
    private string? _expectedEndChecksum;
    private string? _expectedEndCanonical;
    /// <summary>Set by <see cref="EndReplay"/> when the replayed end board
    /// does not match the recorded one; null after a faithful replay.</summary>
    public ReplayDivergence? LastDivergence { get; private set; }

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
        Replay? loadedReplay)
    {
        _state = state;
        _session = session;
        _map = map;
        _ops = ops;
        _aiPacer = aiPacer;
        _previewMode = previewMode;
        _replayIsInstantMode = replayIsInstantMode;

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
    // Depths of the parallel beat-tracking stacks. Invariant: equal to
    // the session undo stack's UndoCount / RedoCount at every quiescent
    // point — tests pin this (UndoReplayBeatSyncTests).
    public int UndoBatchDepth => _undoBeatCounts.Count;
    public int RedoBatchDepth => _redoBeatLists.Count;
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
    //
    // The session undo stack and the two beat stacks below must move in
    // lockstep: one beat batch per undo entry. The public coordinator
    // methods (CommitHumanHandler / UndoOneStep / RedoOneStep /
    // ClearUndoAndBookkeeping) each perform BOTH sides as one atomic
    // operation and validate the invariant afterward, so a caller cannot
    // half-do the dance. The single-side steps are private.

    /// <summary>
    /// Atomic commit of a state-changing human handler: push the
    /// pre-action snapshot onto the session undo stack AND stamp the
    /// pre-handler beat-list size so undo can later trim back to it
    /// (clearing the redo stash — new forward history invalidates the
    /// previous forward branch). Called by <c>GameController.TrackHandler</c>.
    /// </summary>
    public void CommitHumanHandler(UndoEntry pre, int beatsBefore)
    {
        _session.Undo.PushBefore(pre);
        _undoBeatCounts.Push(beatsBefore);
        _redoBeatLists.Clear();
        ValidateBeatStacksInSync("CommitHumanHandler");
        Log.Trace(Log.LogCategory.Undo,
            $"commit: undo depth {_undoBeatCounts.Count}, beats {_replayBeats.Count}");
    }

    /// <summary>
    /// Atomic single-step undo: pop one beat batch (trimming the beat
    /// log back to the entry's pre-handler size, stashing the tail for
    /// redo) AND pop the session undo stack. Returns the restored entry
    /// for the caller to apply.
    /// </summary>
    public UndoEntry UndoOneStep(UndoEntry current)
    {
        int targetCount = _undoBeatCounts.Pop();
        var popped = new List<ReplayBeat>();
        for (int i = targetCount; i < _replayBeats.Count; i++)
        {
            popped.Add(_replayBeats[i]);
        }
        _replayBeats.RemoveRange(targetCount, _replayBeats.Count - targetCount);
        _redoBeatLists.Push(popped);
        UndoEntry restored = _session.Undo.UndoLast(current);
        ValidateBeatStacksInSync("UndoOneStep");
        Log.Debug(Log.LogCategory.Undo,
            $"undo step: popped {popped.Count} beat(s), undo depth {_undoBeatCounts.Count}, " +
            $"redo depth {_redoBeatLists.Count}, beats {_replayBeats.Count}");
        return restored;
    }

    /// <summary>
    /// Atomic single-step redo: re-append one stashed beat batch
    /// (recording the new pre-batch count) AND pop the session redo
    /// stack. Returns the restored entry for the caller to apply.
    /// </summary>
    public UndoEntry RedoOneStep(UndoEntry current)
    {
        List<ReplayBeat> toRestore = _redoBeatLists.Pop();
        _undoBeatCounts.Push(_replayBeats.Count);
        _replayBeats.AddRange(toRestore);
        UndoEntry restored = _session.Undo.RedoLast(current);
        ValidateBeatStacksInSync("RedoOneStep");
        Log.Debug(Log.LogCategory.Undo,
            $"redo step: restored {toRestore.Count} beat(s), undo depth {_undoBeatCounts.Count}, " +
            $"redo depth {_redoBeatLists.Count}, beats {_replayBeats.Count}");
        return restored;
    }

    /// <summary>
    /// Atomic clear of the session undo stack and the parallel
    /// beat-tracking stacks. Called (via the controller's
    /// <c>ClearUndoAndReplayBookkeeping</c>) at the three "no more undo"
    /// commit sites (end of turn, mid-turn domination, claim-victory
    /// win). Does NOT touch the beat log itself — those beats are
    /// committed history.
    /// </summary>
    public void ClearUndoAndBookkeeping()
    {
        _session.Undo.Clear();
        _undoBeatCounts.Clear();
        _redoBeatLists.Clear();
        Log.Debug(Log.LogCategory.Undo,
            $"clear: bookkeeping dropped, beats {_replayBeats.Count} committed");
    }

    /// <summary>
    /// The lockstep invariant: one beat batch per session undo entry,
    /// one stashed batch per session redo entry. A divergence means the
    /// next undo would trim the wrong tail of the beat log — crash
    /// loudly at the cause instead of corrupting the replay silently.
    /// </summary>
    private void ValidateBeatStacksInSync(string operation)
    {
        if (_undoBeatCounts.Count != _session.Undo.UndoCount
            || _redoBeatLists.Count != _session.Undo.RedoCount)
        {
            throw new InvalidOperationException(
                $"Undo/replay-beat bookkeeping desync after {operation}: " +
                $"session undo={_session.Undo.UndoCount} redo={_session.Undo.RedoCount}, " +
                $"beat stacks undo={_undoBeatCounts.Count} redo={_redoBeatLists.Count}.");
        }
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

        // Snapshot the recorded end board's checksum BEFORE the rewind
        // below overwrites it. Captured once (??=) so a re-replay still
        // compares against the original recording, not the prior playback.
        // Skipped in preview mode (authored tutorials have no played-out
        // end state to reproduce).
        if (!_previewMode && _expectedEndChecksum == null)
        {
            _expectedEndChecksum = GameStateChecksum.Compute(_state);
            _expectedEndCanonical = GameStateChecksum.Stringify(_state);
        }

        _state.Territories = _initialSnapshot.ApplyTo(_state.Grid, _state.Treasury);
        // Rising Tides: the snapshot's ApplyTo re-grew the grid with
        // every tile that submerged during the recording. Those coords are land
        // again, so drop them from the water set — otherwise the rewound board
        // keeps the recorded sinks marked as water and the replay diverges (e.g.
        // tree growth, which reads WaterCoords). No-op for freeform (no sinks).
        foreach (HexTile tile in _state.Grid.Tiles) _state.RemoveWater(tile.Coord);
        // Fog Of War: forget the recorded game's accumulated exploration so the
        // replay re-animates fog from scratch (the setup refresh below re-marks
        // only the initial sight). No-op outside Fog Of War.
        _state.ClearSeen();
        // Viking Raiders: the undo snapshot deliberately excludes viking
        // state, so rewind it explicitly to game-start defaults (empty sea,
        // wave cursors at 0); the recorded VikingSpawn/Disembark/... beats
        // re-drive it from there. No-op outside Viking Raiders.
        _state.Vikings.Reset(
            System.Array.Empty<SeaViking>(),
            nextWaveIndex: 0, lastCompletedRound: 0, lastSpawnRound: 0);
        _state.Turns.Reset(_initialCurrentPlayerIndex, _initialTurnNumber);
        _session.Winner = null;
        _session.PendingDefeatScreen = null;
        _session.PendingClaimVictory = null;
        _session.ClaimVictoryPromptedHighestThreshold.Clear();
        _session.ClearPendingAction();
        _session.SelectedTerritory = null;
        // Rising Tides: a live fresh start seeds the first player's
        // turn-1 tide forecast in GameController.Resume(freshStart:true). The
        // replay rewind IS that same fresh start, so seed the same forecast
        // here — otherwise the first end-of-turn tide erodes different tiles
        // than the recording, the board diverges, and a later recorded action
        // lands on a now-submerged tile (illegal placement -> exception).
        // No-op outside Rising Tides. Mirrors Resume's reseed-then-forecast.
        _ops.ReseedRngForCurrentTurn();
        _ops.ForecastTideForCurrentPlayer();
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
        // syncSilentMode: replay's silent flag is driven directly by the
        // instant flag (no AI "Opponents…" overlay), mirroring
        // BeginReplay/EndReplay.
        StepPacing.Redispatch(
            wasInstant: _replayInstantActive,
            nowInstant: nowInstant,
            turnBoundary: turnBoundary,
            map: _map,
            pacer: _aiPacer,
            instantTick: InstantReplayTick,
            pacedStep: StepReplayPreview,
            setTrack: v => _replayInstantActive = v,
            syncSilentMode: () => _map.SetSilentMode(nowInstant),
            logInstantToPaced: () => Log.Info(Log.LogCategory.Turn,
                $"[speed] replay track instant→paced at beat {_replayIndex}"),
            logPacedToInstant: () => Log.Info(Log.LogCategory.Turn,
                $"[speed] replay track paced→instant at beat {_replayIndex}"));
    }

    /// <summary>Instant-replay driver: a thin wrapper over the shared
    /// chunked loop <see cref="GameOperations.RunInstantTick"/>.</summary>
    private void InstantReplayTick() => _ops.RunInstantTick(
        active: () => _replayMode,
        step: ReplayInstantStep,
        onExhausted: EndReplay,
        reschedule: ScheduleNextReplayBeat);

    private void StepReplayPreview()
    {
        if (!_replayMode) return;
        if (_replayIndex >= _replayBeats.Count) { EndReplay(); return; }

        ReplayBeat beat = _replayBeats[_replayIndex];
        _map.ShowHighlight(ResolveReplayActingTerritory(beat));
        // A move's preview shows the unit being picked up — the same
        // pickup pulse a live player sees when selecting a unit to move —
        // so playback reads "select, then move" instead of teleporting.
        if (beat is ReplayMoveBeat mv) _map.ShowMoveSource(mv.From);
        _ops.RefreshViews();

        // Select beats pace like EndTurn (a beat with no board mutation
        // to watch); everything else holds the standard preview pause.
        int delay = beat is ReplayEndTurnBeat or ReplaySelectTerritoryBeat
            ? StepPacing.AiActionDelayMs
            : StepPacing.AiPreviewDelayMs;
        _aiPacer.Schedule(StepReplayExecute, delay);
    }

    private void StepReplayExecute()
    {
        if (!_replayMode) return;
        if (_replayIndex >= _replayBeats.Count) { EndReplay(); return; }

        ReplayBeat beat = _replayBeats[_replayIndex++];
        bool crossesTurn = beat is ReplayEndTurnBeat or ReplayVikingTurnEndBeat;

        ExecuteReplayBeat(beat);
        // The pickup pulse shown by this beat's preview is done — the
        // move landed.
        if (beat is ReplayMoveBeat) _map.ShowMoveSource(null);

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
            case ReplayVikingMoveBeat vm:
                _ops.ExecuteVikingMove(vm.From, vm.To);
                break;
            case ReplayVikingDisembarkBeat vd:
                _ops.ExecuteVikingDisembark(vd.Sea, vd.Land);
                break;
            case ReplayVikingPerishBeat vp:
                _ops.ExecuteVikingPerish(vp.Sea);
                break;
            case ReplayVikingSpawnBeat vs:
                _ops.ExecuteVikingSpawnWave(new VikingSpawnWaveAction(vs.WaveIndex, vs.Spawns));
                break;
            case ReplayVikingTurnEndBeat _:
                // Same completion the live driver's EndVikingPhaseCore runs:
                // close the phase, then the deferred StartPlayerTurn for the
                // waiting (non-eliminated) player.
                _ops.CompleteVikingTurn();
                if (!_session.IsGameOver)
                {
                    _ops.SkipEliminatedCurrentPlayers();
                    _ops.StartPlayerTurn();
                }
                break;
            case ReplaySelectTerritoryBeat sel:
                // Authored selection (Instructions demos): anchor → the
                // territory containing it, applied like a live selection
                // click. Resolved against current state — territory
                // objects aren't stable across the rewind.
                Territory? selected = TerritoryLookup.FindContaining(
                    _state.Territories, sel.Anchor);
                _session.SelectedTerritory = selected;
                _map.ShowHighlight(selected);
                break;
            case TutorialOnlyBeat _:
                // Remaining tutorial-only beats (e.g., narration text) are
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
        if (_ops.VikingTurnPending)
        {
            // Viking round boundary — the log carries explicit viking beats
            // next. Enter the phase exactly like the live driver (RNG
            // stream + move-flag reset) and defer StartPlayerTurn to the
            // ReplayVikingTurnEndBeat.
            _ops.BeginVikingTurn();
            return;
        }
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
        bool isEndTurn = beat is ReplayEndTurnBeat or ReplayVikingTurnEndBeat;
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
        _map.ShowMoveSource(null);
        _ops.RefreshViews();

        // Compare the replayed board against the recorded end board.
        // Only on a clean finish (all beats consumed, or a beat ended the
        // game) so an aborted mid-replay can't falsely diverge.
        bool cleanFinish = _replayIndex >= _replayBeats.Count || _session.IsGameOver;
        if (_expectedEndChecksum != null && cleanFinish)
        {
            string actual = GameStateChecksum.Compute(_state);
            if (actual != _expectedEndChecksum)
            {
                LastDivergence = new ReplayDivergence(_expectedEndChecksum, actual);
                Log.Warn(Log.LogCategory.Replay,
                    $"Replay diverged from recording: expected {_expectedEndChecksum}, " +
                    $"got {actual}. Recorded under different gameplay rules?");
                Log.Debug(Log.LogCategory.Replay,
                    "First diff " + FirstDifference(
                        _expectedEndCanonical ?? "", GameStateChecksum.Stringify(_state)));
            }
            else
            {
                LastDivergence = null;
                Log.Debug(Log.LogCategory.Replay,
                    $"Replay end board matches recording (no divergence): {actual}");
            }
        }

        ReplayEnded?.Invoke();
    }

    /// <summary>
    /// First differing line between the recorded and replayed canonical
    /// state strings, for the divergence Debug log. Mirrors the diagnostic
    /// in AiSimulatorDriftTests.
    /// </summary>
    private static string FirstDifference(string recorded, string replayed)
    {
        string[] a = recorded.Split('\n');
        string[] b = replayed.Split('\n');
        int n = Math.Max(a.Length, b.Length);
        for (int i = 0; i < n; i++)
        {
            string ra = i < a.Length ? a[i] : "<missing>";
            string rb = i < b.Length ? b[i] : "<missing>";
            if (ra != rb) return $"at line {i}: recorded [{ra}] vs replayed [{rb}]";
        }
        return "<identical>";
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
            ReplayVikingMoveBeat vm => TerritoryLookup.FindOwnedContaining(
                _state.Territories, PlayerId.None, vm.From),
            // Authored select beats highlight their target during the
            // preview phase too, so back-to-back selects don't flicker
            // through a no-highlight gap.
            ReplaySelectTerritoryBeat sel => TerritoryLookup.FindContaining(
                _state.Territories, sel.Anchor),
            _ => null,
        };
    }
}
