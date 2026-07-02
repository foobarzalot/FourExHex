using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

/// <summary>
/// AI-turn driver extracted from <see cref="GameController"/>. Owns the
/// paced preview/execute step machine, the chunked instant driver, and
/// the per-AI-turn scratch state. Every mutation goes through
/// <see cref="GameOperations"/> and every replay beat through
/// <see cref="ReplayRecorder"/>; the driver does not reference
/// <see cref="GameController"/> directly. The controller keeps only the
/// entry points: <see cref="RunUntilHumanOrDone"/> (game start / end of
/// a human turn), <see cref="Schedule"/> (defeat-dismiss resume), and
/// <see cref="ResumeAfterReplayPause"/> (Tutorial-Preview narration
/// dismissed).
/// </summary>
public class AiTurnDriver
{
    private readonly GameState _state;
    private readonly SessionState _session;
    private readonly IHexMapView _map;
    private readonly GameOperations _ops;
    private readonly ReplayRecorder _recorder;
    private readonly IAiPacer _aiPacer;
    private readonly Func<GameState, PlayerId, HashSet<HexCoord>, Random, AiAction?> _aiChooser;
    private readonly Func<bool> _aiSilentMode;

    // True while the replay must hold (a blocking Tutorial-Preview
    // narration beat is on screen awaiting the player's tap). The paced
    // AI step machine consults this so it doesn't drain opponents' turns
    // while the shared replay cursor is parked on the narration — see
    // ResumeAfterReplayPause. Always false outside Preview.
    private readonly Func<bool> _isReplayPaused;

    // Per-AI-turn scratch state for the step machine. Persists across
    // paced StepAi invocations and resets whenever control advances
    // to a new player.
    private readonly HashSet<HexCoord> _aiVisited = new();
    private int _aiStepsThisPlayer;

    // The action chosen during the "preview" beat and carried into
    // the "execute" beat that follows. Lets us highlight the acting
    // territory first, pause, then actually run the action.
    private AiAction? _pendingAiAction;

    // Which track the live AI run is currently on (true = chunked
    // Instant, false = paced). Re-read from _aiSilentMode() at every
    // continuation point so a mid-turn Ai-Speed change switches tracks;
    // the previous value lets Schedule detect an instant→paced
    // transition and force the structural rebuild that instant's
    // suppressed per-capture rebuilds skipped.
    private bool _aiTrackInstant;

    // Delay (milliseconds) between AI step beats. Each AI action is
    // split into a preview (highlight the acting territory) and an
    // execute (run the action, re-highlight the resulting territory)
    // so the player can see who is doing what.
    //   AiPreviewDelayMs      — pause BEFORE executing a previewed action
    //   AiActionDelayMs       — pause AFTER executing, before the next preview
    //   AiBetweenPlayersDelayMs — longer pause on player change
    private const int AiPreviewDelayMs = 350;
    private const int AiActionDelayMs = 300;
    private const int AiBetweenPlayersDelayMs = 600;

    // Delay between a per-turn repaint and the next instant tick, so each
    // player-turn's board lingers long enough to follow (≈5 turns/sec)
    // instead of flipping past at frame rate. Still far faster than
    // Fast (~325ms/beat). Mid-turn budget yields (no repaint) use 0 —
    // an in-progress turn shouldn't be paced, only completed ones.
    private const int InstantTurnDelayMs = 200;

    // Safety cap on AI actions per player turn — the visited set
    // guarantees termination in practice, but this keeps a buggy
    // chooser from pacing forever.
    private const int MaxAiStepsPerPlayer = 64;

    public AiTurnDriver(
        GameState state,
        SessionState session,
        IHexMapView map,
        GameOperations ops,
        ReplayRecorder recorder,
        IAiPacer aiPacer,
        Func<GameState, PlayerId, HashSet<HexCoord>, Random, AiAction?> aiChooser,
        Func<bool> aiSilentMode,
        Func<bool> isReplayPaused)
    {
        _state = state;
        _session = session;
        _map = map;
        _ops = ops;
        _recorder = recorder;
        _aiPacer = aiPacer;
        _aiChooser = aiChooser;
        _aiSilentMode = aiSilentMode;
        _isReplayPaused = isReplayPaused;
    }

    // The single game-over / human-turn gate every step-machine entry
    // consults: the run halts when the game has been announced over,
    // the session says it's over, or control rests on a human.
    private bool RunHalted =>
        _ops.GameEndedFired
        || _session.IsGameOver
        || !_state.Turns.CurrentPlayer.IsAi;

    /// <summary>
    /// Re-kick the paced AI run after an external replay pause clears
    /// (the Tutorial-Preview narration beat was tapped away). No-op if
    /// it's the human's turn, the game ended, or the pause is still
    /// active. Unlike <see cref="RunUntilHumanOrDone"/> this does
    /// NOT reset the per-turn step bookkeeping — it resumes the same AI
    /// player's turn mid-stream so its remaining scripted beats (which
    /// sat behind the narration in the shared cursor) execute in order.
    /// </summary>
    public void ResumeAfterReplayPause()
    {
        if (RunHalted) return;
        if (_isReplayPaused()) return;
        Schedule(turnBoundary: false);
    }

    /// <summary>
    /// Kick off a fresh AI run from a turn boundary: reset the per-turn
    /// scratch state, seed the pacing track from the live setting, and
    /// dispatch the first beat. No-op when control already rests on a
    /// human or the game is over.
    /// </summary>
    public void RunUntilHumanOrDone()
    {
        if (RunHalted) return;

        _aiVisited.Clear();
        _aiStepsThisPlayer = 0;
        _pendingAiAction = null;
        // Seed the track from the live setting so the first dispatch
        // doesn't register a spurious instant→paced transition.
        _aiTrackInstant = _aiSilentMode();
        Schedule(turnBoundary: true);
    }

    /// <summary>
    /// Single re-dispatching decision point for kicking off, resuming,
    /// or continuing a live AI run. Called at every safe continuation
    /// boundary (between players, after an executed action, the instant
    /// driver's self-reschedule, and the defeat-dismiss resume) — never
    /// between a paced preview and its execute, so a track switch can't
    /// re-draw RNG for an already-chosen action. Re-reads
    /// <see cref="_aiSilentMode"/> each call so a mid-turn Ai-Speed change
    /// switches tracks: Instant routes to the chunked
    /// <see cref="InstantAiTick"/> (unscaled), otherwise the
    /// multiplier-scaled paced step machine. Syncs silent mode / the
    /// "Opponents…" overlay first, and on an instant→paced transition
    /// forces the structural rebuild instant's suppressed per-capture
    /// rebuilds skipped.
    /// </summary>
    public void Schedule(bool turnBoundary)
    {
        bool nowInstant = _aiSilentMode();
        if (_aiTrackInstant && !nowInstant)
        {
            Log.Debug(Log.LogCategory.Ai,
                $"[speed] AI track instant→paced mid-turn (player={_state.Turns.CurrentPlayer.Id})");
            // Instant suppressed per-capture rebuilds; the border layer
            // is stale before the first paced render.
            _map.RebuildAfterTerritoryChange();
        }
        else if (!_aiTrackInstant && nowInstant)
        {
            Log.Debug(Log.LogCategory.Ai,
                $"[speed] AI track paced→instant mid-turn (player={_state.Turns.CurrentPlayer.Id})");
            // The instant track shows no per-action highlight, so clear
            // the acting-territory outline the paced track last drew —
            // otherwise it lingers through the fast-forward.
            _map.ShowHighlight(null);
        }
        _aiTrackInstant = nowInstant;
        // Sync the view's silent flag + "Opponents…" overlay to the live
        // setting before scheduling, so the next beat renders correctly.
        _ops.RefreshSilentMode();
        // Delay belongs to whichever track we land on: instant runs at
        // its own cadence (0 mid-turn, InstantTurnDelayMs at a boundary,
        // unscaled); paced uses the multiplier-scaled step delays.
        if (nowInstant)
            _aiPacer.ScheduleUnscaled(InstantAiTick, turnBoundary ? InstantTurnDelayMs : 0);
        else
            _aiPacer.Schedule(StepAiPreview, turnBoundary ? AiBetweenPlayersDelayMs : AiActionDelayMs);
    }

    /// <summary>
    /// Preview beat: pick the next AI action, highlight the territory
    /// that will perform it, and schedule <see cref="StepAiExecute"/>
    /// to run that action after a short pause. If the chooser has
    /// nothing left, instead transition to the next player.
    /// </summary>
    private void StepAiPreview()
    {
        if (_ops.GameEndedFired) return;
        if (_session.IsGameOver)
        {
            _ops.ShowHighlightAndRefresh(null);
            return;
        }
        if (!_state.Turns.CurrentPlayer.IsAi)
        {
            // Control changed out from under a scheduled callback
            // (scene reload, test teardown). Just stop.
            return;
        }

        // A blocking Tutorial-Preview narration beat is on screen: the
        // current AI player's own scripted beat may sit behind it in the
        // shared replay cursor. Bail WITHOUT scheduling or ending the
        // turn — ResumeAfterReplayPause re-kicks this step once the
        // player taps the narration away. (Always false outside Preview.)
        if (_isReplayPaused())
        {
            Log.Debug(Log.LogCategory.Ai,
                $"[replay] AI step held: narration blocking (player={_state.Turns.CurrentPlayer.Id})");
            return;
        }

        // Paced path only — Instant routes to InstantAiTick via
        // Schedule and never enters this step machine.
        PlayerId color = _state.Turns.CurrentPlayer.Id;
        StepAiPreviewAfterChoose(_aiChooser(_state, color, _aiVisited, _ops.Rng), color);
    }

    private void StepAiPreviewAfterChoose(AiAction? action, PlayerId color)
    {
        // Defensive re-checks: the game may have ended or the player
        // changed (BeginReplay, AbandonGame mid-await) between the
        // chooser dispatch and this continuation. Mirrors the gates
        // at the top of StepAiPreview.
        if (RunHalted) return;
        if (_state.Turns.CurrentPlayer.Id != color) return;

        if (action == null || _aiStepsThisPlayer >= MaxAiStepsPerPlayer)
        {
            // Current AI player is done. Run the shared end-of-turn
            // mutation, clear the lingering highlight, then either stop
            // (human next) or schedule the next preview beat.
            EndCurrentAiPlayerTurnCore(action);
            // End-of-turn win (sole capital-bearer) declared inside
            // EndCurrentAiPlayerTurnCore: no next StartPlayerTurn fires to
            // hide the "Opponents…" overlay, so reconcile it here before
            // the victory paint so it doesn't draw on top. Mirrors
            // the domination branch in StepAiExecute.
            if (_ops.GameEndedFired || _session.IsGameOver) _ops.RefreshSilentMode();
            // Human-next: StartPlayerTurn auto-selected their first
            // territory — re-show it rather than clearing. AI-next leaves
            // the selection null, so this clears the highlight as before.
            _ops.ShowHighlightAndRefresh(_session.SelectedTerritory);

            if (!RunHalted)
            {
                // Crossing to the next AI player: re-dispatch so a
                // mid-run Ai-Speed change can switch tracks here.
                Schedule(turnBoundary: true);
            }
            return;
        }

        _pendingAiAction = action;
        _ops.ShowHighlightAndRefresh(ResolveAiActingTerritory(action));
        // Preview→execute hop stays a direct schedule (NOT a re-dispatch):
        // _pendingAiAction is already chosen, so switching tracks here
        // would re-draw RNG for it. The track switch lands at the next
        // action boundary in StepAiExecute instead.
        _aiPacer.Schedule(StepAiExecute, AiPreviewDelayMs);
    }

    /// <summary>
    /// Execute beat: run the previewed action, re-highlight the
    /// (possibly expanded) resulting territory so the player can see
    /// the outcome, then schedule the next preview beat.
    /// </summary>
    private void StepAiExecute()
    {
        if (_ops.GameEndedFired) return;
        if (_session.IsGameOver)
        {
            _ops.ShowHighlightAndRefresh(null);
            return;
        }
        AiAction? action = _pendingAiAction;
        _pendingAiAction = null;
        if (action == null) return; // defensive; shouldn't happen

        HexCoord? rc = ApplyAiActionCore(action);
        if (rc == null) return; // defensive; unrecognised action kind
        HexCoord resultCoord = rc.Value;

        _ops.CheckGameEndConditions();
        if (_ops.GameEndedFired)
        {
            // Domination fired inside the action we just executed
            // (HandleCapture set _session.Winner). The HUD's victory
            // overlay is gated on session.Winner inside RefreshViews,
            // so without this final refresh the dialog never appears
            // and the game looks frozen mid-board.
            //
            // RefreshSilentMode first: the paced step machine has no
            // StartPlayerTurn hand-off on game-over, so this is where we
            // hide the "Opponents…" overlay (aiActing is now false) before
            // painting the victory screen — otherwise it draws on top.
            _ops.RefreshSilentMode();
            _ops.ShowHighlightAndRefresh(null);
            return;
        }

        // After a capture the old territory object is stale; find the
        // AI's territory now containing the result coord and
        // re-highlight so the outline matches the post-action board.
        _ops.ShowHighlightAndRefresh(TerritoryLookup.FindOwnedContaining(
            _state.Territories, _state.Turns.CurrentPlayer.Id, resultCoord));

        if (_session.IsGameOver)
        {
            _map.ShowHighlight(null);
            return;
        }
        // Paused: defeat-overlay handler will re-schedule when the
        // human dismisses it. Without this, the AI keeps running
        // behind the modal scrim and the user clicks Continue into a
        // game state several turns past their elimination.
        if (_session.PendingDefeatScreen.HasValue)
        {
            _ops.RefreshSilentMode();
            _ops.RefreshViews();
            return;
        }
        // Action boundary: re-dispatch so a mid-turn Ai-Speed change
        // switches tracks for the next action.
        Schedule(turnBoundary: false);
    }

    /// <summary>
    /// Apply one chosen AI action: record its replay beat (live play
    /// only) and run the same Execute* mutation the live game uses.
    /// Shared by the paced step machine (<see cref="StepAiExecute"/>)
    /// and the chunked <see cref="InstantAiTick"/> so the two pacing
    /// modes can't drift. Returns the action's result coord (for the
    /// paced post-action highlight) or null for an unrecognised action
    /// kind. Does NOT run
    /// the game-end check or refresh views — callers own pacing.
    /// </summary>
    private HexCoord? ApplyAiActionCore(AiAction action)
    {
        _aiStepsThisPlayer++;
        LogAction(action);
        switch (action)
        {
            case AiMoveAction mv:
                if (!_recorder.IsReplaying)
                {
                    _recorder.RecordBeat(new ReplayMoveBeat { From = mv.Source, To = mv.Destination });
                }
                _ops.ExecuteAiMove(mv.Source, mv.Destination);
                return mv.Destination;
            case AiBuyUnitAction bu:
                if (!_recorder.IsReplaying)
                {
                    _recorder.RecordBeat(new ReplayBuyBeat
                    {
                        Capital = bu.Capital,
                        To = bu.Destination,
                        Level = bu.Level,
                    });
                }
                _ops.ExecuteAiBuyUnit(bu.Capital, bu.Destination, bu.Level);
                return bu.Destination;
            case AiBuildTowerAction bt:
                if (!_recorder.IsReplaying)
                {
                    _recorder.RecordBeat(new ReplayBuildTowerBeat
                    {
                        Capital = bt.Capital,
                        To = bt.Destination,
                    });
                }
                _ops.ExecuteAiBuildTower(bt.Capital, bt.Destination);
                return bt.Destination;
            case AiBuyCombineAction bc:
                if (!_recorder.IsReplaying)
                {
                    // Recorded as a buy beat; replay drives ExecuteAiBuyUnit
                    // which handles combines naturally via PlaceNew.
                    _recorder.RecordBeat(new ReplayBuyBeat
                    {
                        Capital = bc.Capital,
                        To = bc.CombineTarget,
                        Level = bc.BuyLevel,
                    });
                }
                _ops.ExecuteAiBuyCombine(bc.Capital, bc.CombineTarget, bc.BuyLevel);
                return bc.CombineTarget;
            case AiLongPressRallyAction rl:
                if (!_recorder.IsReplaying)
                {
                    _recorder.RecordBeat(new ReplayLongPressRallyBeat { Target = rl.Target });
                }
                _ops.ApplyLongPressRally(rl.Target);
                return rl.Target;
            case AiClaimVictoryAction cv:
            {
                PlayerId cvColor = _state.Turns.CurrentPlayer.Id;
                if (!_recorder.IsReplaying)
                {
                    _recorder.RecordBeat(new ReplayClaimVictoryBeat { ThresholdPercent = cv.ThresholdPercent });
                }
                _session.ClaimVictoryPromptedHighestThreshold[cvColor] = cv.ThresholdPercent;
                _ops.DeclareWinner(cvColor);
                _recorder.ClearUndoAndBookkeeping();
                return TerritoryLookup
                    .OwnedCapitalBearing(_state.Territories, cvColor)
                    .FirstOrDefault()?.Capital
                    ?? new HexCoord(0, 0);
            }
            case AiDismissClaimAction dc:
            {
                PlayerId dcColor = _state.Turns.CurrentPlayer.Id;
                if (!_recorder.IsReplaying)
                {
                    _recorder.RecordBeat(new ReplayDismissClaimBeat { ThresholdPercent = dc.ThresholdPercent });
                }
                _session.ClaimVictoryPromptedHighestThreshold[dcColor] = dc.ThresholdPercent;
                return TerritoryLookup
                    .OwnedCapitalBearing(_state.Territories, dcColor)
                    .FirstOrDefault()?.Capital
                    ?? new HexCoord(0, 0);
            }
            case AiDismissDefeatAction _:
                if (!_recorder.IsReplaying)
                {
                    _recorder.RecordBeat(new ReplayDismissDefeatBeat());
                }
                _session.PendingDefeatScreen = null;
                return new HexCoord(0, 0);
            default:
                return null;
        }
    }

    /// <summary>
    /// Mutation half of an AI player's implicit end-of-turn: log,
    /// record the EndTurn beat (live only), run end-of-turn
    /// processing, then announce game-over or advance to the next
    /// player and reset the per-player AI bookkeeping. Shared by the
    /// paced step machine and the chunked <see cref="InstantAiTick"/>.
    /// Does NOT refresh views or schedule the next beat — callers own
    /// pacing. <paramref name="action"/> (the null/step-capped chooser
    /// result) is used only for the AI-log reason string.
    /// </summary>
    private void EndCurrentAiPlayerTurnCore(AiAction? action)
    {
        Log.Info(Log.LogCategory.Turn,
            $"[T{_state.Turns.TurnNumber}] {_state.Turns.CurrentPlayer.Name} ends turn after " +
            $"{_aiStepsThisPlayer} actions " +
            $"({(action == null ? "no positive-delta actions" : "step cap reached")})");
        // Record AI's implicit end-of-turn for the replay log. The
        // beat captures the *ending* AI player's actor/turn.
        if (!_recorder.IsReplaying) _recorder.RecordBeat(new ReplayEndTurnBeat());
        _ops.EndOfTurnProcessing();
        if (_session.IsGameOver)
        {
            _ops.CheckGameEndConditions();
        }
        else
        {
            _ops.AdvanceToNextActivePlayer();
            _ops.StartPlayerTurn();
        }
        _aiVisited.Clear();
        _aiStepsThisPlayer = 0;
        _pendingAiAction = null;
    }

    [Conditional("DEBUG")]
    private void LogAction(AiAction action)
    {
        Player p = _state.Turns.CurrentPlayer;
        string desc = action switch
        {
            AiMoveAction mv => $"Move {mv.Source}→{mv.Destination}",
            AiBuyUnitAction bu => $"Buy {bu.Level}@{bu.Capital} → {bu.Destination}",
            AiBuildTowerAction bt => $"Tower@{bt.Capital} → {bt.Destination}",
            AiBuyCombineAction bc => $"BuyCombine {bc.BuyLevel}@{bc.Capital} → {bc.CombineTarget}",
            _ => "?",
        };
        Log.Info(Log.LogCategory.Ai, $"[T{_state.Turns.TurnNumber}]   {p.Name}: {desc}");
    }

    /// <summary>
    /// Resolve the AI's acting territory for the preview highlight:
    /// the attacker territory for a move, the buying territory for a
    /// buy, the building territory for a tower build. Returns null if
    /// the lookup fails — the preview is purely cosmetic, so missing
    /// the highlight is preferable to throwing out of a scheduled
    /// callback.
    /// </summary>
    private Territory? ResolveAiActingTerritory(AiAction action)
    {
        PlayerId owner = _state.Turns.CurrentPlayer.Id;
        return action switch
        {
            AiMoveAction mv => TerritoryLookup.FindOwnedContaining(_state.Territories, owner, mv.Source),
            AiBuyUnitAction bu => TerritoryLookup.FindOwnedContaining(_state.Territories, owner, bu.Capital),
            AiBuildTowerAction bt => TerritoryLookup.FindOwnedContaining(_state.Territories, owner, bt.Capital),
            AiBuyCombineAction bc => TerritoryLookup.FindOwnedContaining(_state.Territories, owner, bc.Capital),
            _ => null
        };
    }

    /// <summary>
    /// Live-AI instant driver — the user-visible 1:1 of instant replay
    /// for AI opponents' turns. Same chunked cadence, silence and
    /// per-turn sampling; the only deliberate difference is that the
    /// "Opponents are taking their turns…" overlay stays (driven by
    /// <see cref="GameOperations.RefreshSilentMode"/>, which replay leaves off).
    /// </summary>
    private void InstantAiTick() => _ops.RunInstantTick(
        active: () => !RunHalted,
        step: AiInstantStep,
        onExhausted: EndInstantAiBatch,
        reschedule: Schedule);

    private InstantStep AiInstantStep()
    {
        if (RunHalted) return InstantStep.Exhausted;
        // A human-dismissable overlay raised mid-batch: stop so it can
        // paint; the dismiss handler reschedules InstantAiTick.
        if (_session.PendingDefeatScreen.HasValue
            || _session.PendingClaimVictory.HasValue) return InstantStep.Exhausted;

        PlayerId color = _state.Turns.CurrentPlayer.Id;
        AiAction? action = _aiChooser(_state, color, _aiVisited, _ops.Rng);
        if (action == null || _aiStepsThisPlayer >= MaxAiStepsPerPlayer)
        {
            EndCurrentAiPlayerTurnCore(action);
            // Game over or next player is human → hand control back;
            // else this AI turn just completed → repaint it and pace
            // the next.
            if (RunHalted) return InstantStep.Exhausted;
            return InstantStep.TurnBoundary;
        }

        HexCoord? rc = ApplyAiActionCore(action);
        if (rc == null) return InstantStep.Continued; // defensive
        _ops.CheckGameEndConditions();
        if (_ops.GameEndedFired || _session.IsGameOver) return InstantStep.Exhausted;
        if (_session.PendingDefeatScreen.HasValue) return InstantStep.Exhausted;
        return InstantStep.Continued;
    }

    /// <summary>
    /// Finish action for <see cref="InstantAiTick"/>. Mirrors
    /// <see cref="ReplayRecorder.EndReplay"/>'s shape (final rebuild + lift silent +
    /// single paint) plus the live-only mid-batch pause: if a defeat /
    /// claim-victory overlay is up, lift silent and paint it but DON'T
    /// rebuild/advance — the dismiss handler resumes the batch.
    /// </summary>
    private void EndInstantAiBatch()
    {
        if (_session.PendingDefeatScreen.HasValue
            || _session.PendingClaimVictory.HasValue)
        {
            _ops.RefreshSilentMode();
            _ops.RefreshViews();
            return;
        }
        // Per-capture rebuilds were suppressed during the batch; do one
        // final structural rebuild so borders match the post-AI board.
        _map.RebuildAfterTerritoryChange();
        // Hands control back to a human (or the game ended): lift silent
        // + hide the "Opponents…" overlay, then the single end-of-batch
        // paint the human sees (winner overlay if the game just ended).
        // Re-show any auto-selection StartPlayerTurn made for the human
        // now in control (null when the game just ended).
        _ops.RefreshSilentMode();
        _ops.ShowHighlightAndRefresh(_session.SelectedTerritory);
    }
}
