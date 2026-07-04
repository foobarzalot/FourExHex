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

    // True while the driver is running the viking pseudo-turn (Viking
    // Raiders): the rotation has already advanced to the round's first
    // player, their StartPlayerTurn is deferred, and the chooser is
    // VikingAi.ChooseNext instead of the injected player chooser. Uses
    // the same _aiVisited/_aiStepsThisPlayer/_pendingAiAction scratch.
    private bool _vikingPhase;

    // Which track the live AI run is currently on (true = chunked
    // Instant, false = paced). Re-read from _aiSilentMode() at every
    // continuation point so a mid-turn Ai-Speed change switches tracks;
    // the previous value lets Schedule detect an instant→paced
    // transition and force the structural rebuild that instant's
    // suppressed per-capture rebuilds skipped.
    private bool _aiTrackInstant;

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
    // the session says it's over, or control rests on a human — unless
    // the viking pseudo-turn still has to run (or is mid-flight), which
    // proceeds even when the waiting player is human.
    private bool RunHalted =>
        _ops.GameEndedFired
        || _session.IsGameOver
        || (!_state.Turns.CurrentPlayer.IsAi && !_vikingPhase && !_ops.VikingTurnPending);

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
        // The syncSilentMode hop refreshes the view's silent flag + the
        // "Opponents…" overlay to the live setting before scheduling, so
        // the next beat renders correctly.
        StepPacing.Redispatch(
            wasInstant: _aiTrackInstant,
            nowInstant: _aiSilentMode(),
            turnBoundary: turnBoundary,
            map: _map,
            pacer: _aiPacer,
            instantTick: InstantAiTick,
            pacedStep: StepAiPreview,
            setTrack: v => _aiTrackInstant = v,
            syncSilentMode: _ops.RefreshSilentMode,
            logInstantToPaced: () => Log.Debug(Log.LogCategory.Ai,
                $"[speed] AI track instant→paced mid-turn (player={_state.Turns.CurrentPlayer.Id})"),
            logPacedToInstant: () => Log.Debug(Log.LogCategory.Ai,
                $"[speed] AI track paced→instant mid-turn (player={_state.Turns.CurrentPlayer.Id})"));
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
        if (!_state.Turns.CurrentPlayer.IsAi && !_vikingPhase && !_ops.VikingTurnPending)
        {
            // Control changed out from under a scheduled callback
            // (scene reload, test teardown). Just stop. (The viking
            // pseudo-turn proceeds even when the waiting player is human.)
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
        MaybeBeginVikingPhase();
        if (!_vikingPhase && !_state.Turns.CurrentPlayer.IsAi) return;
        PlayerId color = _state.Turns.CurrentPlayer.Id;
        StepAiPreviewAfterChoose(ChooseNextForCurrentActor(), color);
    }

    /// <summary>
    /// Enter the viking pseudo-turn if it's due at this dispatch
    /// boundary: fresh scratch state, then <see cref="GameOperations.BeginVikingTurn"/>
    /// (RNG reseed onto the vikings' own stream + move-flag reset +
    /// input lock).
    /// </summary>
    private void MaybeBeginVikingPhase()
    {
        if (_vikingPhase || !_ops.VikingTurnPending) return;
        _vikingPhase = true;
        _aiVisited.Clear();
        _aiStepsThisPlayer = 0;
        _pendingAiAction = null;
        _ops.BeginVikingTurn();
    }

    /// <summary>The next action for whoever is actually acting: the
    /// viking sequencer during the viking phase, else the injected
    /// player chooser for the current (AI) player.</summary>
    private AiAction? ChooseNextForCurrentActor() =>
        _vikingPhase
            ? VikingAi.ChooseNext(_state, _aiVisited, _ops.Rng)
            : _aiChooser(_state, _state.Turns.CurrentPlayer.Id, _aiVisited, _ops.Rng);

    /// <summary>
    /// Mutation half of the viking phase's end: complete the phase
    /// (round marked done, wipeout check), then run the deferred
    /// <see cref="GameOperations.StartPlayerTurn"/> for the waiting
    /// player — skipping them first if the raiders eliminated them
    /// mid-phase. Mirrors <see cref="EndCurrentAiPlayerTurnCore"/>;
    /// callers own pacing/refresh.
    /// </summary>
    private void EndVikingPhaseCore(AiAction? action)
    {
        Log.Info(Log.LogCategory.Turn,
            $"[T{_state.Turns.TurnNumber}] Vikings end phase after " +
            $"{_aiStepsThisPlayer} actions " +
            $"({(action == null ? "no actions left" : "step cap reached")})");
        // Record the phase's terminator beat (live only) — replay's
        // ReplayVikingTurnEndBeat handler runs the same completion below.
        if (!_recorder.IsReplaying) _recorder.RecordBeat(new ReplayVikingTurnEndBeat());
        _vikingPhase = false;
        _ops.CompleteVikingTurn();
        if (!_session.IsGameOver && !_ops.GameEndedFired)
        {
            _ops.SkipEliminatedCurrentPlayers();
            _ops.StartPlayerTurn();
        }
        _aiVisited.Clear();
        _aiStepsThisPlayer = 0;
        _pendingAiAction = null;
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
            // Current actor is done. Run the shared end-of-turn
            // mutation, clear the lingering highlight, then either stop
            // (human next) or schedule the next preview beat.
            if (_vikingPhase) EndVikingPhaseCore(action);
            else EndCurrentAiPlayerTurnCore(action);
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
        Territory? acting = ResolveAiActingTerritory(action);
        if (_vikingPhase)
        {
            Log.Debug(Log.LogCategory.Viking,
                $"[viking] preview beat {action.GetType().Name} → highlight " +
                $"{(acting == null ? "null (clears)" : $"size={acting.Size}")}");
        }
        _ops.ShowHighlightAndRefresh(acting);
        // Preview→execute hop stays a direct schedule (NOT a re-dispatch):
        // _pendingAiAction is already chosen, so switching tracks here
        // would re-draw RNG for it. The track switch lands at the next
        // action boundary in StepAiExecute instead.
        _aiPacer.Schedule(StepAiExecute, StepPacing.AiPreviewDelayMs);
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
        // actor's territory now containing the result coord and
        // re-highlight so the outline matches the post-action board.
        // During the viking phase the actor is the neutral owner, not
        // the (waiting) current player.
        PlayerId actor = _vikingPhase ? PlayerId.None : _state.Turns.CurrentPlayer.Id;
        Territory? result = TerritoryLookup.FindOwnedContaining(
            _state.Territories, actor, resultCoord);
        if (_vikingPhase)
        {
            Log.Debug(Log.LogCategory.Viking,
                $"[viking] execute beat {action.GetType().Name} @{resultCoord} → highlight " +
                $"{(result == null ? "null (clears)" : $"size={result.Size}")}");
        }
        _ops.ShowHighlightAndRefresh(result);

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
    /// Shared by the paced step machine (<see cref="StepAiExecute"/>),
    /// the chunked <see cref="InstantAiTick"/>, and the controller's
    /// human-turn Automate loop so the pacing modes can't drift.
    /// Returns the action's result coord (for the
    /// paced post-action highlight) or null for an unrecognised action
    /// kind. Does NOT run
    /// the game-end check or refresh views — callers own pacing.
    /// </summary>
    internal HexCoord? ApplyAiActionCore(AiAction action)
    {
        _aiStepsThisPlayer++;
        LogAction(action);
        switch (action)
        {
            case AiMoveAction mv:
                // Viking land moves execute through the owner-aware core and
                // record the viking-specific beat kind (a ReplayMoveBeat
                // would replay as the current player's move).
                if (_vikingPhase)
                {
                    if (!_recorder.IsReplaying)
                    {
                        _recorder.RecordBeat(new ReplayVikingMoveBeat
                        {
                            From = mv.Source,
                            To = mv.Destination,
                        });
                    }
                    _ops.ExecuteVikingMove(mv.Source, mv.Destination);
                    return mv.Destination;
                }
                if (!_recorder.IsReplaying)
                {
                    _recorder.RecordBeat(new ReplayMoveBeat { From = mv.Source, To = mv.Destination });
                }
                _ops.ExecuteAiMove(mv.Source, mv.Destination);
                return mv.Destination;
            case VikingDisembarkAction vd:
                if (!_recorder.IsReplaying)
                {
                    _recorder.RecordBeat(new ReplayVikingDisembarkBeat { Sea = vd.Sea, Land = vd.Land });
                }
                _ops.ExecuteVikingDisembark(vd.Sea, vd.Land);
                return vd.Land;
            case VikingPerishAtSeaAction vp:
                if (!_recorder.IsReplaying)
                {
                    _recorder.RecordBeat(new ReplayVikingPerishBeat { Sea = vp.Sea });
                }
                _ops.ExecuteVikingPerish(vp.Sea);
                return vp.Sea;
            case VikingSpawnWaveAction vs:
                if (!_recorder.IsReplaying)
                {
                    _recorder.RecordBeat(new ReplayVikingSpawnBeat
                    {
                        WaveIndex = vs.WaveIndex,
                        Spawns = vs.Spawns,
                    });
                }
                _ops.ExecuteVikingSpawnWave(vs);
                return vs.Spawns.Count > 0 ? vs.Spawns[0].Coord : new HexCoord(0, 0);
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
            // Round boundary in Viking Raiders: the raiders act before
            // the new player's turn starts. The driver's next dispatch
            // enters the viking phase; StartPlayerTurn runs when it ends
            // (EndVikingPhaseCore).
            if (!_ops.VikingTurnPending) _ops.StartPlayerTurn();
        }
        _aiVisited.Clear();
        _aiStepsThisPlayer = 0;
        _pendingAiAction = null;
    }

    [Conditional("DEBUG")]
    private void LogAction(AiAction action)
    {
        string actor = _vikingPhase ? "Vikings" : _state.Turns.CurrentPlayer.Name;
        string desc = action switch
        {
            AiMoveAction mv => $"Move {mv.Source}→{mv.Destination}",
            AiBuyUnitAction bu => $"Buy {bu.Level}@{bu.Capital} → {bu.Destination}",
            AiBuildTowerAction bt => $"Tower@{bt.Capital} → {bt.Destination}",
            AiBuyCombineAction bc => $"BuyCombine {bc.BuyLevel}@{bc.Capital} → {bc.CombineTarget}",
            VikingDisembarkAction vd => $"Disembark {vd.Sea}→{vd.Land}",
            VikingPerishAtSeaAction vp => $"Perish at sea {vp.Sea}",
            VikingSpawnWaveAction vs => $"Spawn wave {vs.WaveIndex} ({vs.Spawns.Count} raiders)",
            _ => "?",
        };
        Log.Info(Log.LogCategory.Ai, $"[T{_state.Turns.TurnNumber}]   {actor}: {desc}");
    }

    /// <summary>
    /// Resolve the AI's acting territory for the preview highlight:
    /// the attacker territory for a move, the buying territory for a
    /// buy, the building territory for a tower build. Returns null if
    /// the lookup fails — the preview is purely cosmetic, so missing
    /// the highlight is preferable to throwing out of a scheduled
    /// callback.
    /// </summary>
    internal Territory? ResolveAiActingTerritory(AiAction action)
    {
        // During the viking phase the actor is the neutral owner; sea
        // actions (disembark/perish/spawn) have no acting territory —
        // the preview highlight is simply skipped for them.
        PlayerId owner = _vikingPhase ? PlayerId.None : _state.Turns.CurrentPlayer.Id;
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

        MaybeBeginVikingPhase();
        AiAction? action = ChooseNextForCurrentActor();
        if (action == null || _aiStepsThisPlayer >= MaxAiStepsPerPlayer)
        {
            if (_vikingPhase) EndVikingPhaseCore(action);
            else EndCurrentAiPlayerTurnCore(action);
            // Game over or next player is human → hand control back;
            // else this actor's turn just completed → repaint it and
            // pace the next.
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
