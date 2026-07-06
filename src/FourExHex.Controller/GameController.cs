using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

/// <summary>
/// The controller in the MVC split. Owns <see cref="GameState"/> and
/// <see cref="SessionState"/> and orchestrates every interaction: click
/// policy, buy/move/capture flows, undo/redo, turn transitions, view
/// refreshes. Pure C# (no Godot Node lifecycle) — Main is the scene
/// root that constructs and wires everything; the controller just
/// receives events from the views and applies rules.
/// </summary>
public class GameController
{
    private readonly GameState _state;

    /// <summary>Purchase costs depend on the buyer; in every controller
    /// path the buyer is the current player.</summary>
    private Difficulty CurrentDifficulty => _state.Turns.CurrentPlayer.Difficulty;
    private readonly SessionState _session;
    private readonly IHexMapView _map;
    private readonly IHudView _hud;
    private readonly GameOperations _ops;
    private readonly ReplayRecorder _recorder;

    // A saved master seed plus the (turn, player) tuple determines the
    // per-turn RNG sequence. The reseed happens at the top of
    // StartPlayerTurn, so a save need only capture the seed (no
    // RNG-consumption count) to replay identically on load.
    private readonly int _masterSeed;
    public int MasterSeed => _masterSeed;

    private readonly IAiPacer _aiPacer;

    // The AI-turn driver owns the paced preview/execute step machine,
    // the chunked instant driver, and the per-AI-turn scratch state.
    // The controller only enters it at turn hand-offs
    // (RunUntilHumanOrDone), the defeat-dismiss resume (Schedule), and
    // the Tutorial-Preview narration resume (ResumeAfterReplayPause).
    private readonly AiTurnDriver _aiDriver;


    /// <summary>
    /// Fired exactly once when the game ends — either naturally
    /// (<see cref="SessionState.IsGameOver"/> becomes true) or by
    /// hitting the turn cap passed to the constructor. The
    /// diagnostic launch path subscribes to this so headless runs
    /// can exit on completion.
    /// </summary>
    public event Action? GameEnded;

    /// <summary>
    /// Fired at the start of every human player's turn, after start-of-turn
    /// bookkeeping (tree growth, income, upkeep) and after
    /// <see cref="GameOperations.RefreshViews"/>. Save/load wires the autosave path to
    /// this event — the saved state matches what the player sees.
    /// Never fires for AI turns.
    /// </summary>
    public event Action? HumanTurnStarted;

    // When true (the real-game default), a human turn opens with their
    // first territory auto-selected (#94). Tests that exercise the
    // no-selection / cycle-from-scratch mechanics construct with it off so
    // the board starts unselected, the same control as seed / aiPacer.
    private readonly bool _autoSelectFirstTerritory;

    public GameController(
        GameState state,
        SessionState session,
        IHexMapView map,
        IHudView hud,
        int? seed = null,
        Func<GameState, PlayerId, HashSet<HexCoord>, Random, AiAction?>? aiChooser = null,
        IAiPacer? aiPacer = null,
        int maxTurnNumber = int.MaxValue,
        Replay? loadedReplay = null,
        Func<ReplayBeat, bool>? humanActionValidator = null,
        Func<UnitLevel, bool>? buyLevelValidator = null,
        bool previewMode = false,
        bool recordingMode = false,
        Action? onAfterRefresh = null,
        Func<bool>? aiSilentMode = null,
        Func<bool>? replayIsInstantMode = null,
        Func<bool>? isReplayPaused = null,
        bool autoSelectFirstTerritory = true,
        Func<GameState, PlayerId, HashSet<HexCoord>, Random, AiAction?>? automateChooser = null)
    {
        _autoSelectFirstTerritory = autoSelectFirstTerritory;
        _automateChooser = automateChooser ?? ComputerAi.ChooseNextAction;
        Func<bool> aiSilent = aiSilentMode ?? (() => false);
        _humanActionValidator = humanActionValidator;
        _buyLevelValidator = buyLevelValidator;
        _previewMode = previewMode;
        _recordingMode = recordingMode;
        _state = state;
        _session = session;
        _map = map;
        _hud = hud;
        _masterSeed = seed ?? SeedFormat.NextSeed(Random.Shared);
        _ops = new GameOperations(
            state,
            session,
            map,
            hud,
            recordingMode: recordingMode,
            previewMode: previewMode,
            // Closures over _recorder: the field is null at GameOperations
            // construction but the recorder is created immediately below;
            // these predicates aren't invoked until runtime paths fire.
            // Null-coalesce makes the timing window explicit and silences
            // the static-analysis warning.
            isReplayMode: () => _recorder?.IsReplaying ?? false,
            aiSilentMode: aiSilent,
            isReplayInstantActive: () => _recorder?.IsInstantModeActive ?? false,
            clearUndoAndReplayBookkeeping: ClearUndoAndReplayBookkeeping,
            onGameEnded: () => GameEnded?.Invoke(),
            onHumanTurnStarted: RaiseHumanTurnStarted,
            maxTurnNumber: maxTurnNumber,
            masterSeed: _masterSeed,
            onAfterRefresh: onAfterRefresh,
            isAutomating: () => _automating,
            isAutomateExhausted: () => _automateExhausted);
        _aiPacer = aiPacer ?? new SynchronousAiPacer();
        _recorder = new ReplayRecorder(
            state: state,
            session: session,
            map: map,
            ops: _ops,
            aiPacer: _aiPacer,
            previewMode: previewMode,
            replayIsInstantMode: replayIsInstantMode,
            loadedReplay: loadedReplay);
        _aiDriver = new AiTurnDriver(
            state: state,
            session: session,
            map: map,
            ops: _ops,
            recorder: _recorder,
            aiPacer: _aiPacer,
            aiChooser: aiChooser ?? ComputerAi.ChooseNextAction,
            aiSilentMode: aiSilent,
            isReplayPaused: isReplayPaused ?? (() => false));

        _map.TileClicked += OnTileClicked;
        _map.TileLongClicked += OnTileLongClicked;
        _map.OffGridClicked += OnOffGridClicked;
        _hud.BuyRecruitClicked += OnBuyPressed;
        _hud.BuyUnitClicked += OnBuyUnitPressed;
        _hud.BuildTowerClicked += OnBuildTowerPressed;
        _hud.UndoLastClicked += OnUndoLastPressed;
        _hud.UndoTurnClicked += OnUndoTurnPressed;
        _hud.RedoLastClicked += OnRedoLastPressed;
        _hud.RedoAllClicked += OnRedoAllPressed;
        _hud.EndTurnClicked += OnEndTurnPressed;
        _hud.NextTerritoryClicked += OnNextTerritoryPressed;
        _hud.PreviousTerritoryClicked += OnPreviousTerritoryPressed;
        _hud.NextUnitClicked += OnNextUnitPressed;
        _hud.PreviousUnitClicked += OnPreviousUnitPressed;
        _hud.CancelActionPressed += OnCancelActionPressed;
        _hud.AutomateClicked += OnAutomatePressed;
        _hud.DefeatContinueClicked += OnDefeatContinuePressed;
        _hud.ClaimVictoryWinNowClicked += OnClaimVictoryWinNowPressed;
        _hud.ClaimVictoryContinueClicked += OnClaimVictoryContinuePressed;

        // Tutorial Preview / Record use the bottom-center tutorial
        // message panel for game-over signaling; the click-blocking
        // victory modal would otherwise freeze further author input.
        _hud.SetVictoryOverlaySuppressed(_previewMode || _recordingMode);

        // Difficulty lever instrumentation: one-shot at game
        // construction, only when some slot is non-Soldier (non-default), so a
        // FOUREXHEX_DIFFICULTY run proves which player got which level. Lands
        // in the FOUREXHEX_6AI stdout (Turn:Info is pinned there).
        if (System.Linq.Enumerable.Any(_state.Players, p => p.Difficulty != Difficulty.Soldier))
        {
            Log.Info(Log.LogCategory.Turn,
                "difficulties: " + string.Join(", ",
                    System.Linq.Enumerable.Select(_state.Players,
                        p => $"{p.Name}={p.Difficulty}")));
        }
    }

    /// <summary>
    /// Finish initial game setup: seed starting gold and do the first
    /// view refresh. Main calls this once after constructing the
    /// controller and adding the views to the scene tree.
    /// </summary>
    /// <summary>
    /// Abandon the current game: drop any pending AI step callbacks
    /// the pacer has queued. Called from <c>Main</c> before swapping
    /// back to the menu scene, so a timer that was already in flight
    /// can't fire an <see cref="AiTurnDriver"/> step after the scene's
    /// tile polygons have been disposed (which would throw
    /// <c>ObjectDisposedException</c> from the <c>HexTile.Owner</c>
    /// setter mid-capture).
    /// </summary>
    public void AbandonGame()
    {
        // Flag only (no StopAutomation): the views may be mid-teardown,
        // so no refresh — the cancelled pacer drops any in-flight beat.
        _automating = false;
        _pendingAutomateAction = null;
        _aiPacer.Cancel();
        // Unsubscribe from view events so a downstream click can't
        // re-enter this stale controller's handlers — relevant when
        // the view is shared between sessions (TutorialBuilder's
        // Record ↔ Preview transitions reuse the same HexMapView).
        // Without this, the stale handler's RefreshViews call hits
        // a disposed HudView and throws ObjectDisposedException.
        _map.TileClicked -= OnTileClicked;
        _map.TileLongClicked -= OnTileLongClicked;
        _map.OffGridClicked -= OnOffGridClicked;
        _hud.BuyRecruitClicked -= OnBuyPressed;
        _hud.BuyUnitClicked -= OnBuyUnitPressed;
        _hud.BuildTowerClicked -= OnBuildTowerPressed;
        _hud.UndoLastClicked -= OnUndoLastPressed;
        _hud.UndoTurnClicked -= OnUndoTurnPressed;
        _hud.RedoLastClicked -= OnRedoLastPressed;
        _hud.RedoAllClicked -= OnRedoAllPressed;
        _hud.EndTurnClicked -= OnEndTurnPressed;
        _hud.NextTerritoryClicked -= OnNextTerritoryPressed;
        _hud.PreviousTerritoryClicked -= OnPreviousTerritoryPressed;
        _hud.NextUnitClicked -= OnNextUnitPressed;
        _hud.PreviousUnitClicked -= OnPreviousUnitPressed;
        _hud.CancelActionPressed -= OnCancelActionPressed;
        _hud.AutomateClicked -= OnAutomatePressed;
        _hud.DefeatContinueClicked -= OnDefeatContinuePressed;
        _hud.ClaimVictoryWinNowClicked -= OnClaimVictoryWinNowPressed;
        _hud.ClaimVictoryContinueClicked -= OnClaimVictoryContinuePressed;
        // Clear any tap-summoned notice so a shared HudView doesn't
        // carry stale view state into the next session.
        _hud.DismissCapitalAlertNotice();
    }

    public void StartGame()
    {
        SeedStartingGold();
        // Replay-recording anchor: capture starting state after seeding
        // gold so replay's restore matches the seed the live game saw on
        // round 1. Skipped when the controller was constructed with a
        // loaded replay (the snapshot is already populated from disk).
        if (!_recorder.HasInitialSnapshot)
        {
            _recorder.CaptureInitialSnapshot(
                GameStateSnapshot.Capture(_state.Grid, _state.Treasury, _state.Territories),
                _state.Turns.TurnNumber,
                _state.Turns.CurrentPlayerIndex,
                markCompleteFromStart: true);
        }
        // No start-of-game income collection: the seed already equals
        // 5 × tree-free cells per territory, which is exactly what each
        // player sees on their first turn. Subsequent turns credit
        // income at the END of the turn (see OnEndTurnPressed and the
        // AI turn-end path).
        Resume(freshStart: true);
    }

    /// <summary>
    /// Pick up where a previously running game left off. Used by both
    /// fresh-game startup (after <see cref="SeedStartingGold"/>) and
    /// the load-game path, where <see cref="GameState"/> already holds
    /// the saved gold and turn state — re-seeding starting gold would
    /// overwrite the saved economy.
    ///
    /// Reseeds the per-turn RNG for the current (turn, player), runs
    /// any leading AI turns until control reaches a human (or game
    /// ends), pushes the latest state into the views, then fires
    /// <see cref="HumanTurnStarted"/> if the resumed player is human.
    ///
    /// <paramref name="freshStart"/> is true only for a brand-new game
    /// (via <see cref="StartGame"/>): StartPlayerTurn isn't called for the
    /// initial player, so Rising Tides' turn-1 forecast is
    /// seeded here. A load passes false — the saved <see cref="GameState.PendingTide"/>
    /// is restored from disk and must NOT be recomputed (re-picking would
    /// drift the locked forecast).
    /// </summary>
    public void Resume(bool freshStart = false)
    {
        // Resume reached without an initial snapshot means we loaded a
        // pre-replay save (v3). Capture at load time so future replays
        // of *this* game from save-then-load have something to anchor
        // on, but leave _replayDataIsCompleteFromStart false: the UI
        // disables the Replay button for this game because we have no
        // history before the load point.
        if (!_recorder.HasInitialSnapshot)
        {
            _recorder.CaptureInitialSnapshot(
                GameStateSnapshot.Capture(_state.Grid, _state.Treasury, _state.Territories),
                _state.Turns.TurnNumber,
                _state.Turns.CurrentPlayerIndex,
                markCompleteFromStart: false);
        }
        _ops.ReseedRngForCurrentTurn();
        // Rising Tides: seed the initial player's turn-1 forecast.
        // Only on a fresh game — a load restores PendingTide from the save.
        if (freshStart) _ops.ForecastTideForCurrentPlayer();
        _ops.RefreshSilentMode();
        _aiDriver.RunUntilHumanOrDone();
        _ops.RefreshViews();
        // Initial player is human → StartPlayerTurn is never called
        // for them (it only runs at transitions), so fire the autosave
        // hook here. If a human turn was reached via AI hand-off
        // inside the driver's RunUntilHumanOrDone, the event already
        // fired from inside StartPlayerTurn.
        MaybeFireHumanTurnStartedFromStartGame();
    }

    // Tracks whether HumanTurnStarted has fired for the current player's
    // turn so StartGame doesn't double-fire when the AI hand-off path
    // already raised it from inside StartPlayerTurn.
    private void MaybeFireHumanTurnStartedFromStartGame()
    {
        if (_ops.HumanTurnFiredForCurrentTurn) return;
        if (_session.IsGameOver || _ops.GameEndedFired) return;
        if (_state.Turns.CurrentPlayer.IsAi) return;
        // Viking Raiders: a paced viking phase is in flight (or about to
        // run) — the waiting human's StartPlayerTurn fires the event when
        // the phase completes; firing here would autosave and auto-select
        // a pre-viking board.
        if (_ops.VikingPhaseActive || _ops.VikingTurnPending) return;
        _ops.HumanTurnFiredForCurrentTurn = true;
        RaiseHumanTurnStarted();
    }

    /// <summary>
    /// Single hand-off point for the start of a human player's turn:
    /// auto-select their first territory (so they can act without a
    /// manual click) and then raise <see cref="HumanTurnStarted"/> for
    /// the autosave hook. Reached from every human turn-start path —
    /// <see cref="GameOperations.StartPlayerTurn"/>'s hook and the
    /// initial-player seam in <see cref="MaybeFireHumanTurnStartedFromStartGame"/>.
    /// </summary>
    private void RaiseHumanTurnStarted()
    {
        AutoSelectFirstTerritoryForHuman();
        // Viking Raiders: the wave countdown / arrival banner, at every
        // human turn start (null outside the mode or once nothing is left
        // to announce). Suppressed in tutorial Record/Preview, whose
        // scripted messaging owns the screen. On turn 1 this naturally
        // sequences after the one-time mode intro — Main defers game start
        // (and therefore this event) until the intro is tapped away.
        string? waveBanner = VikingWaveBannerContent.For(_state);
        if (waveBanner != null && !_previewMode && !_recordingMode)
        {
            _hud.ShowTransientBanner(waveBanner);
        }
        HumanTurnStarted?.Invoke();
    }

    /// <summary>
    /// Open a human turn with their "first" territory already selected,
    /// reusing the Next-Territory cycle picker so auto-select and the Tab
    /// button share one ordering/actionable/visited rule. From a clean
    /// turn this lands on the largest actionable territory (capital-coord
    /// tie-break); a no-op leaving the selection null when the player has
    /// nothing to act on. Skipped for AI, replay, and game-over.
    /// </summary>
    private void AutoSelectFirstTerritoryForHuman()
    {
        if (!_autoSelectFirstTerritory) return;
        if (_recorder.IsReplaying) return;
        if (_session.IsGameOver || _ops.GameEndedFired) return;
        if (_state.Turns.CurrentPlayer.IsAi) return;
        Log.Debug(Log.LogCategory.Input,
            $"[autoselect] human turn start, player={_state.Turns.CurrentPlayer.Id}");
        // Drop any stale selection from the prior player's turn first, so a
        // player who starts with nothing actionable (StepTerritorySelection
        // no-ops) is left cleanly unselected rather than showing the
        // previous player's territory. Then walk to the first actionable.
        SetSelection(null);
        if (TryFocusPendingTide()) return;
        StepTerritorySelection(forward: true);
    }

    /// <summary>
    /// Rising Tides turn-start focus: center the camera on the current
    /// player's telegraphed doomed tile and select the territory containing
    /// it, so the per-turn erosion is never off-screen. Replaces the
    /// largest-actionable auto-select whenever a forecast exists — the
    /// doomed territory is selected even when it has nothing actionable
    /// (the point is to look at it); a capital-less singleton leaves
    /// nothing selected, camera pan only. Mountain-demote steps focus the
    /// same way — the reprieve is the same tide event. False outside
    /// Rising Tides or with no pending forecast (normal auto-select runs).
    /// </summary>
    private bool TryFocusPendingTide()
    {
        if (_state.Mode != GameMode.RisingTides) return false;
        if (_state.PendingTide.Count == 0) return false;
        HexCoord doomed = _state.PendingTide[0].Coord;
        Territory? territory = TerritoryLookup.FindOwnedContaining(
            _state.Territories, _state.Turns.CurrentPlayer.Id, doomed);
        if (territory is { HasCapital: true }) SetSelection(territory);
        _map.CenterOnCoord(doomed);
        Log.Debug(Log.LogCategory.Tide,
            $"[tide] turn-start focus {doomed} territory=" +
            $"{(territory?.Capital?.ToString() ?? "none")}");
        return true;
    }

    /// <summary>
    /// Seed every territory's treasury to 5 × its gold-earning-cell
    /// count. Tree-occupied cells don't earn gold, so they don't
    /// contribute to the seed.
    /// </summary>
    private void SeedStartingGold()
    {
        const int startingGoldPerEarningCell = 5;
        foreach (Territory territory in _state.Territories)
        {
            if (!territory.HasCapital) continue;
            int earningCells = TreeRules.CountIncomeProducingTiles(territory, _state.Grid);
            _state.Treasury.SetGold(
                territory.Capital!.Value, earningCells * startingGoldPerEarningCell);
        }
    }

    private UndoEntry CaptureCurrentSnapshot() => new UndoEntry(
        GameStateSnapshot.Capture(_state.Grid, _state.Treasury, _state.Territories),
        SessionStateSnapshot.Capture(_session));

    // Set inside Execute* helpers right before the first game-state
    // mutation. Read by TrackHandler at handler exit to decide whether
    // to push an UndoEntry. The wrapping handler captures and pushes once,
    // covering both game and session state changes in a single entry.
    private bool _handlerMutatedGame;

    // --- Automate (AI finishes the human's turn) --------------------------
    // The chooser the Automate loop asks for the current human's next
    // action. Unlike the AI driver's chooser (production:
    // AiDispatcher.ChooseForCurrentPlayer, which returns null for a
    // Human slot), this one is invoked FOR a human slot, so it defaults
    // to ComputerAi.ChooseNextAction directly. Tests inject scripts.
    private readonly Func<GameState, PlayerId, HashSet<HexCoord>, Random, AiAction?> _automateChooser;
    private bool _automating;
    // Latched when automation stops because the chooser ran dry
    // ("exhausted") — re-pressing Automate would no-op, so the button
    // greys out. Cleared by anything that invalidates the "nothing left
    // to automate" verdict: undo/redo (ApplySnapshot), a manual
    // state-changing move (TrackHandler push), or ending the turn.
    private bool _automateExhausted;

    /// <summary>True while the Automate loop is playing the current
    /// human's remaining moves. Read by Main's pacer-speed closure to
    /// select the Automate speed setting, and by the HUD state push.</summary>
    public bool IsAutomating => _automating;

    // Tutorial Preview hooks. _humanActionValidator (set via the
    // constructor's humanActionValidator param) gates every
    // state-mutating human input: input handlers build the proposed
    // ReplayBeat and call this BEFORE mutating; false → abort.
    // _previewMode (set via the previewMode param) parallels
    // _replayMode for the RecordBeat gate but does NOT block input
    // handlers — Preview wants player-0 clicks to flow through.
    private readonly Func<ReplayBeat, bool>? _humanActionValidator;

    // Tutorial Preview pre-placement guard for the Buy radio row. When
    // set, returns true iff the next expected player-0 beat is a buy
    // at exactly this level. The buy handlers consult it before
    // changing session.Mode so the dev can't pre-select a stronger
    // unit than the script asks for. Non-mutating — the cursor doesn't
    // advance until the placement click runs through
    // _humanActionValidator.
    private readonly Func<UnitLevel, bool>? _buyLevelValidator;
    private readonly bool _previewMode;
    public bool IsPreviewMode => _previewMode;

    // Latched true once a Tutorial-Preview script runs out of beats and
    // the session graduates to ordinary free play (see
    // GraduateFromTutorialScripting). While false in preview, scripted
    // suppressions hold; once true, ordinary game-end rules resume.
    private bool _previewScriptingComplete;

    // _recordingMode (set via the recordingMode param) is true when the
    // controller is hosting the Tutorial Builder's Record session. All
    // six slots are forced Human in that mode so the dev can play every
    // color, which would otherwise fire the defeat overlay (and similar
    // human-only UI) for non-player-0 eliminations even though those
    // slots will be AI in the eventual Preview playback. Used by
    // HandleCapture to gate PendingDefeatScreen to player 0 only.
    private readonly bool _recordingMode;
    public bool IsRecordingMode => _recordingMode;

    // --- Replay recording / playback ------------------------------------
    // The replay log + playback step machines live on _recorder
    // (ReplayRecorder.cs). The only local bookkeeping that stays on
    // GameController is _pendingHumanBeat: a per-handler buffer that
    // handler bodies set when they mutate state; TrackHandler reads it
    // post-body and forwards to _recorder.RecordBeat. The buffer lives
    // here because the handlers themselves live here.
    private ReplayBeat? _pendingHumanBeat;

    /// <summary>Read-only view of the recorded beat log.</summary>
    public System.Collections.Generic.IReadOnlyList<ReplayBeat> ReplayBeats => _recorder.Beats;
    /// <summary>The captured game-start snapshot, or null if recording
    /// hasn't started yet.</summary>
    public GameStateSnapshot? InitialReplaySnapshot => _recorder.InitialSnapshot;
    /// <summary><see cref="TurnState.TurnNumber"/> at recording start.</summary>
    public int InitialReplayTurnNumber => _recorder.InitialTurnNumber;
    /// <summary><see cref="TurnState.CurrentPlayerIndex"/> at recording start.</summary>
    public int InitialReplayCurrentPlayerIndex => _recorder.InitialCurrentPlayerIndex;
    /// <summary>Whether <see cref="BeginReplay"/> would produce a
    /// faithful from-start playback. False after loading a save that
    /// pre-dates the replay feature.</summary>
    public bool ReplayDataIsCompleteFromStart => _recorder.IsCompleteFromStart;
    /// <summary>True while <see cref="BeginReplay"/> is driving playback.
    /// Input handlers early-return when this is set; autosave is
    /// suppressed.</summary>
    public bool IsReplayMode => _recorder.IsReplaying;
    /// <summary>Set when the most recent completed replay landed on a board
    /// that differs from the recorded final board — e.g. a
    /// gameplay rule changed since the replay was recorded; null after a
    /// faithful replay. Developer-facing detection (also logged at
    /// <see cref="Log.LogCategory.Replay"/>); no user-facing UI.</summary>
    public ReplayDivergence? LastReplayDivergence => _recorder.LastDivergence;
    /// <summary>Depth of the recorder's undo beat-count stack. Invariant:
    /// equals <c>SessionState.Undo.UndoCount</c> at every quiescent point
    /// (pinned by UndoReplayBeatSyncTests).</summary>
    public int UndoBeatBatchDepth => _recorder.UndoBatchDepth;
    /// <summary>Depth of the recorder's redo beat-stash stack. Invariant:
    /// equals <c>SessionState.Undo.RedoCount</c> at every quiescent point
    /// (pinned by UndoReplayBeatSyncTests).</summary>
    public int RedoBeatBatchDepth => _recorder.RedoBatchDepth;

    /// <summary>
    /// Per-event-handler push policy. Captures pre-handler state, runs
    /// <paramref name="work"/>, and pushes that pre-state onto the undo
    /// stack iff the handler actually changed something — either game
    /// state (signaled by <see cref="_handlerMutatedGame"/>) or session
    /// state (selection / mode / move source). De-dup is automatic: a
    /// no-op handler (e.g. Buy Recruit when already in BuyingRecruit
    /// and only recruit is affordable) leaves both signals false and
    /// no entry is pushed.
    ///
    /// Exceptions thrown by <paramref name="work"/> propagate; the push
    /// code below is intentionally skipped. An exception in a handler
    /// means the controller's invariants are broken — we want the
    /// application to crash, not to leave a fake "press Undo to
    /// recover" path that would mask the bug.
    /// </summary>
    private void TrackHandler(System.Action work)
    {
        if (_recorder.IsReplaying) return;
        // Drop the input outright while an AI player is acting under
        // Instant — the main thread is free between worker yields, so
        // a tile click or Tab press could otherwise race the chooser
        // and mutate SessionState behind it. The gate also covers the
        // mid-mutation window inside the synchronous trampoline (a
        // belt-and-braces guarantee: even if Godot's input ordering
        // ever changed, the controller's invariants are protected) and
        // the viking pseudo-turn (the human's turn hasn't started yet).
        if (_ops.HumanInputLocked) return;
        // Any real human input interrupts a running automation before the
        // handler body runs — the loop's own execute step is exempt
        // (_inAutomateStep), it IS the automation.
        if (_automating && !_inAutomateStep) StopAutomation("input");
        int beatsBefore = _recorder.BeatsCount;
        UndoEntry pre = CaptureCurrentSnapshot();
        _handlerMutatedGame = false;
        _pendingHumanBeat = null;
        // Default-dismiss the tap-summoned capital-alert notice on every
        // human action. OnTileClicked's outer wrapper captures the prior
        // coord before TrackHandler runs, so OnTileClickedBody can still
        // implement toggle-off-on-re-tap; every other handler just clears
        // the notice and that's the desired behavior.
        _hud.DismissCapitalAlertNotice();
        work();
        // If the handler triggered a game-over (e.g., a winning capture
        // calls Undo.Clear()), don't push — there's nothing to undo past
        // game-end, and the pre-state would otherwise resurrect the
        // just-cleared stack. But still record the beat: the action that
        // ended the game must appear in the replay log.
        if (_session.IsGameOver)
        {
            if (_pendingHumanBeat != null) _recorder.RecordBeat(_pendingHumanBeat);
            return;
        }
        // A manual game-mutating action (not the automate loop's own
        // step) invalidates the exhausted-automation verdict. The body's
        // own RefreshViews already ran with the latch still set, so an
        // actual un-latch needs one more refresh to re-enable the button.
        if (_handlerMutatedGame && !_inAutomateStep && _automateExhausted)
        {
            _automateExhausted = false;
            _ops.RefreshViews();
        }
        SessionStateSnapshot postSession = SessionStateSnapshot.Capture(_session);
        bool sessionChanged = !pre.Session.Equals(postSession);
        if (_handlerMutatedGame || sessionChanged)
        {
            // Atomic: pushes the session undo entry AND the parallel
            // beat bookkeeping (this entry corresponds to the beat list
            // at size beatsBefore; new action invalidates forward
            // history on both sides) in one call.
            _recorder.CommitHumanHandler(pre, beatsBefore);
        }
        if (_pendingHumanBeat != null) _recorder.RecordBeat(_pendingHumanBeat);
        // Tutorial Preview cue update: handler bodies sometimes paint
        // ShowMoveTargets / ShowTowerTargets AFTER their mid-body
        // RefreshViews call (e.g., OnTileClickedBody enters MovingUnit
        // mode and paints all valid targets AFTER SetSelection
        // refreshed). Re-fire onAfterRefresh at the tail so the cue
        // paints last and wins over the body's full-target sets.
        // Re-entrancy in TutorialPreviewCues.Apply is guarded
        // separately, so the extra invocation is safe.
        _ops.InvokeAfterRefresh();
    }

    // --- Click handling ---------------------------------------------------

    private void OnTileClicked(HexTile? tile)
    {
        // Capture BEFORE TrackHandler runs, since TrackHandler's default-
        // dismiss will clear _hud.SummonedCapitalAlertCoord. The body uses
        // priorAlert to implement toggle-off-on-re-tap of the same capital.
        HexCoord? priorAlert = _hud.SummonedCapitalAlertCoord;
        TrackHandler(() => OnTileClickedBody(tile, priorAlert));
    }

    private void OnOffGridClicked(HexCoord coord) =>
        TrackHandler(() => OnOffGridClickedBody(coord));

    /// <summary>
    /// Handle a click whose coord is outside the land grid (water, etc.).
    /// In a pending placement mode (buy/move/tower) it's a rejected click
    /// just like a far in-grid click: flash + sound, then cancel the mode
    /// (like Escape) and deselect — off-grid "re-selection" is the
    /// long-standing "click off-grid to deselect" UX. Outside of a
    /// placement mode the click simply clears selection.
    /// </summary>
    private void OnOffGridClickedBody(HexCoord coord)
    {
        if (_session.IsGameOver) return;

        UnitLevel? buyLevel = SessionState.BuyModeLevel(_session.Mode);
        if (buyLevel.HasValue && _session.SelectedTerritory != null)
        {
            EmitRejection(buyLevel.Value, coord);
            Log.Debug(Log.LogCategory.Input,
                $"[Click] off-grid buy click at {coord} → flash + cancel mode, deselecting");
            CancelPendingAction();
        }
        else if (_session.Mode == SessionState.ActionMode.BuildingTower && _session.SelectedTerritory != null)
        {
            _map.FlashRejection(coord, RejectionShape.Tower, System.Array.Empty<HexCoord>());
            Log.Debug(Log.LogCategory.Input,
                $"[Click] off-grid build-tower click at {coord} → flash + cancel mode, deselecting");
            CancelPendingAction();
        }
        else if (_session.Mode == SessionState.ActionMode.MovingUnit && _session.MoveSource.HasValue)
        {
            Unit? sourceUnit = _state.Grid.Get(_session.MoveSource.Value)?.Unit;
            if (sourceUnit != null)
            {
                EmitRejection(sourceUnit.Level, coord);
            }
            Log.Debug(Log.LogCategory.Input,
                $"[Click] off-grid move click at {coord} → flash + cancel mode, deselecting");
            CancelPendingAction();
        }

        SetSelection(null);
    }

    private void OnTileClickedBody(HexTile? tile, HexCoord? priorAlertCoord)
    {
        if (_session.IsGameOver) return;

        // Handle any pending action mode first. Rejected clicks split
        // into two cases: "in range but blocked" (own-territory occupant,
        // adjacent over-defended enemy) flashes + stays in mode so the
        // user can adjust without re-pressing the button; "out of range"
        // (no shared border with the selected territory; for tower,
        // outside the selected territory entirely) cancels the mode and
        // falls through to the normal selection branch.
        UnitLevel? buyLevel = SessionState.BuyModeLevel(_session.Mode);
        if (buyLevel.HasValue && tile != null && _session.SelectedTerritory != null)
        {
            if (IsValidTarget(buyLevel.Value, tile.Coord))
            {
                ExecuteBuyAndPlace(buyLevel.Value, tile.Coord);
                return;
            }
            EmitRejection(buyLevel.Value, tile.Coord);
            if (IsCoordReachableForUnitAction(tile.Coord, _session.SelectedTerritory))
            {
                Log.Debug(Log.LogCategory.Input,
                    $"[Click] in-range invalid buy target at {tile.Coord} → flash + stay in buy mode");
                return;
            }
            Log.Debug(Log.LogCategory.Input,
                $"[Click] out-of-range invalid buy target at {tile.Coord} → flash + cancel mode, re-processing as selection");
            CancelPendingAction();
        }
        else if (_session.Mode == SessionState.ActionMode.BuildingTower && tile != null && _session.SelectedTerritory != null)
        {
            if (IsValidTowerTarget(tile.Coord))
            {
                ExecuteBuildTower(tile.Coord);
                return;
            }
            _map.FlashRejection(tile.Coord, RejectionShape.Tower, System.Array.Empty<HexCoord>());
            if (IsCoordReachableForTowerAction(tile.Coord, _session.SelectedTerritory))
            {
                Log.Debug(Log.LogCategory.Input,
                    $"[Click] in-territory invalid build-tower target at {tile.Coord} ({DescribeInvalidTowerReason(tile.Coord)}) → flash + stay in build mode");
                return;
            }
            Log.Debug(Log.LogCategory.Input,
                $"[Click] out-of-territory invalid build-tower target at {tile.Coord} ({DescribeInvalidTowerReason(tile.Coord)}) → flash + cancel mode, re-processing as selection");
            CancelPendingAction();
        }
        else if (_session.Mode == SessionState.ActionMode.MovingUnit && tile != null && _session.SelectedTerritory != null && _session.MoveSource.HasValue)
        {
            Unit? sourceUnit = _state.Grid.Get(_session.MoveSource.Value)?.Unit;
            if (sourceUnit != null && IsValidTarget(sourceUnit.Level, tile.Coord))
            {
                ExecuteMove(_session.MoveSource.Value, tile.Coord);
                return;
            }
            if (sourceUnit != null)
            {
                EmitRejection(sourceUnit.Level, tile.Coord);
            }
            if (IsCoordReachableForUnitAction(tile.Coord, _session.SelectedTerritory))
            {
                Log.Debug(Log.LogCategory.Input,
                    $"[Click] in-range invalid move target at {tile.Coord} → flash + stay in move mode");
                return;
            }
            Log.Debug(Log.LogCategory.Input,
                $"[Click] out-of-range invalid move target at {tile.Coord} → flash + cancel mode, re-processing as selection");
            CancelPendingAction();
        }

        // Tap-summoned capital-alert notice. TrackHandler has already
        // dismissed any previously-summoned notice; re-summon iff this
        // tap landed on the current human player's own capital whose
        // economy outlook would draw the warning badge AND the prior
        // tap wasn't already on that same coord (toggle-off semantics).
        if (tile != null && !_state.Turns.CurrentPlayer.IsAi)
        {
            Territory? alertTerritory = TerritoryLookup.FindContaining(_state.Territories, tile.Coord);
            if (alertTerritory != null
                && alertTerritory.Owner == _state.Turns.CurrentPlayer.Id
                && alertTerritory.Capital == tile.Coord
                && priorAlertCoord != tile.Coord)
            {
                EconomyOutlook outlook = UpkeepRules.Classify(
                    alertTerritory, _state.Grid, _state.Treasury,
                    _state.Turns.CurrentPlayer.Difficulty);
                if (outlook != EconomyOutlook.Healthy)
                {
                    _hud.SummonCapitalAlertNotice(tile.Coord, outlook);
                }
            }
        }

        // Normal click handling.
        if (tile == null)
        {
            SetSelection(null);
            return;
        }

        Territory? territory = TerritoryLookup.FindContaining(_state.Territories, tile.Coord);
        if (territory == null || territory.Owner != _state.Turns.CurrentPlayer.Id)
        {
            SetSelection(null);
            return;
        }

        // A user click that lands on a *different* territory exits
        // repeated-movement (the user redirected their attention). Same-
        // territory clicks preserve the flag — those are either picking
        // up a unit (handled below) or were already routed through the
        // MovingUnit branch above (which cleared via CancelPendingAction
        // on invalid targets). Capture-rebound selection changes happen
        // via RebindSelectionToContaining, which doesn't pass through
        // this code path.
        if (_session.RepeatedMovement
            && !ReferenceEquals(_session.SelectedTerritory, territory))
        {
            Log.Debug(Log.LogCategory.Input,
                "[RepeatedMovement] cleared (user-clicked different territory)");
            _session.RepeatedMovement = false;
        }

        // Select the territory; if the clicked tile has one of our own
        // unused units, also pick it up for movement.
        SetSelection(territory);

        if (tile.Unit != null
            && tile.Unit.Owner == _state.Turns.CurrentPlayer.Id
            && !tile.Unit.HasMovedThisTurn)
        {
            _session.Mode = SessionState.ActionMode.MovingUnit;
            _session.MoveSource = tile.Coord;
            _map.ShowMoveTargets(ActionConsumingTargets(tile.Unit.Level, territory), tile.Unit.Level);
            _map.ShowMoveSource(tile.Coord);
            // Re-refresh after entering MovingUnit so HudView's cached
            // _hasPendingAction sees the new mode — otherwise Escape
            // routes to the pause menu instead of cancelling the move.
            _ops.RefreshViews();
        }
    }

    private void OnTileLongClicked(HexTile? tile) =>
        TrackHandler(() => OnTileLongClickedBody(tile));

    /// <summary>
    /// Long-press rally: move every still-unmoved unit in the territory
    /// containing <paramref name="tile"/> as close as possible to the
    /// target, using only legal free-reposition destinations (empty
    /// friendly hexes in the territory). Ignored when a buy / build /
    /// move action is pending — the player would otherwise lose their
    /// in-progress context. The whole rally is a single undo step.
    ///
    /// The target hex itself doesn't have to be a legal destination
    /// (e.g., a friendly tower is fine) — units rally to the closest
    /// empty cell to it. Greedy by distance: the unit currently closest
    /// to the target gets first pick of the closest empty cell, so a
    /// far unit can't leapfrog a near one.
    /// </summary>
    private void OnTileLongClickedBody(HexTile? tile)
    {
        if (_session.IsGameOver) return;
        if (tile == null) return;
        // Repeated-movement is a passive sticky intent — a deliberate
        // long-press overrides it: cancel the pending pick (clears Mode +
        // MoveSource + flag) and proceed with rally. Buy / Build / non-
        // chained MovingUnit pending intents stay protected by the guard
        // below.
        if (_session.RepeatedMovement)
        {
            CancelPendingAction("long-press rally");
        }
        if (_session.Mode != SessionState.ActionMode.None) return;

        PlayerId currentColor = _state.Turns.CurrentPlayer.Id;
        if (tile.Owner != currentColor) return;

        if (_humanActionValidator != null && !_humanActionValidator(
            new ReplayLongPressRallyBeat { Target = tile.Coord }))
        {
            return;
        }

        Territory? territory = TerritoryLookup.FindContaining(_state.Territories, tile.Coord);
        if (territory == null || territory.Owner != currentColor) return;

        HexCoord target = tile.Coord;
        bool anyMoved = RallyRules.ResolveRally(_state.Grid, territory, target, currentColor);

        if (anyMoved)
        {
            _handlerMutatedGame = true;
            _pendingHumanBeat = new ReplayLongPressRallyBeat { Target = target };
            _ops.EmitSound(SoundEffect.Rally);
            SetSelection(territory);
        }
        _ops.RefreshViews();
    }

    /// <summary>
    /// Update <see cref="SessionState.SelectedTerritory"/>, redraw the
    /// view's highlight outline, and refresh the HUD.
    /// </summary>
    private void SetSelection(Territory? territory)
    {
        MarkSelectedVisited(territory);
        _session.SelectedTerritory = territory;
        _ops.ShowHighlightAndRefresh(territory);
    }

    /// <summary>
    /// Record <paramref name="territory"/> as visited this turn for
    /// Tab-cycle ordering. Any selection counts — Tab, Shift+Tab,
    /// or a direct click — so "untouched" means the player hasn't been
    /// there at all. Keyed by capital coord; capital-less territories
    /// (singletons) never participate in the cycle and are skipped.
    /// </summary>
    private void MarkSelectedVisited(Territory? territory)
    {
        if (territory?.Capital is not HexCoord capital) return;
        if (_session.VisitedTerritoryCapitals.Add(capital))
        {
            Log.Debug(Log.LogCategory.Input,
                $"[visited] += capital {capital} ({_session.VisitedTerritoryCapitals.Count} this turn)");
        }
    }

    /// <summary>
    /// Public selection entry point for tutorial Preview orchestration —
    /// drives the same path a tile click would (private SetSelection +
    /// view highlight + RefreshViews) without going through TrackHandler
    /// (no undo entry: tutorial Preview isn't undoable). Ordinary play
    /// reaches selection via OnTileClicked / OnNextTerritoryPressed and
    /// shouldn't call this.
    /// </summary>
    public void SelectTerritoryForTutorial(Territory? territory) => SetSelection(territory);

    /// <summary>
    /// Public cancel-pending-action entry point for tutorial Preview
    /// orchestration. Clears <see cref="SessionState.Mode"/> / MoveSource
    /// and the associated map overlays, then refreshes views. Bypasses
    /// TrackHandler so no undo entry is pushed (tutorial Preview isn't
    /// undoable). Ordinary play reaches this path via the Cancel button
    /// / Escape key.
    /// </summary>
    public void CancelActionForTutorial()
    {
        CancelPendingAction();
        _ops.RefreshViews();
    }

    /// <summary>
    /// Public refresh-views passthrough for tutorial Preview wiring —
    /// invoked by <c>TutorialNarrationDriver</c> after the player taps
    /// to dismiss a tutorial-only beat, so <c>TutorialPreviewCues</c>
    /// re-paints the next action's cue (the cues run at the tail of
    /// every RefreshViews via onAfterRefresh). Ordinary play and tests
    /// drive refreshes through the normal handler paths and shouldn't
    /// call this.
    /// </summary>
    public void RefreshViewsForTutorial() => _ops.RefreshViews();

    /// <summary>
    /// Graduate a Tutorial-Preview session to ordinary free play once the
    /// scripted beats run out. Called by <c>PreviewPane</c> when
    /// <c>TutorialPreview.TutorialFinished</c> fires. Lifts the preview's
    /// scripted suppressions so normal game-end rules resume: the full-win
    /// overlay un-hides and the End-Turn claim-victory prompt fires again.
    /// (The validators / AI chooser become permissive on their own once
    /// the script is complete; this only flips the controller-side gates.)
    /// </summary>
    public void GraduateFromTutorialScripting()
    {
        _previewScriptingComplete = true;
        _hud.SetVictoryOverlaySuppressed(false);
        // Clear any pinned scripted cue instruction (e.g. "Press End
        // Turn.") so it doesn't linger into free play or the auto-replay;
        // hands the message panel back to the normal HUD action-hint pass.
        _hud.HideTutorialMessage();
        Log.Info(Log.LogCategory.Tutorial,
            "[GameController] tutorial script exhausted; graduated to ordinary free play");
    }

    /// <summary>
    /// Append an authored tutorial-only beat to the recording log. Used
    /// by RecordPane's "+ Text" authoring path. Stamps Index + Turn
    /// from current state and forces <see cref="ReplayBeat.Actor"/> =
    /// -1 (no player owns these). Gated on recording mode: silently
    /// no-ops outside of an active recording (replay playback, preview
    /// mode, or before StartGame). Crashes if a non-<see cref="TutorialOnlyBeat"/>
    /// subclass is passed — only tutorial-only beats are authorable.
    /// </summary>
    public void RecordTutorialOnlyBeat(TutorialOnlyBeat beat) =>
        _recorder.RecordTutorialOnlyBeat(beat);

    /// <summary>
    /// Returns the action-consuming targets for a would-be attacker of
    /// level <paramref name="attackerLevel"/>: enemy tiles we can capture,
    /// plus own-territory tiles whose tree the unit would clear. Empty
    /// own-territory repositions and friendly combines are legal but not
    /// highlighted — they don't consume the unit's action.
    /// </summary>
    private IEnumerable<HexCoord> ActionConsumingTargets(UnitLevel attackerLevel, Territory territory)
    {
        foreach (HexCoord coord in MovementRules.ValidTargets(attackerLevel, territory, _state.Grid, _state.Territories))
        {
            HexTile? tile = _state.Grid.Get(coord);
            if (tile == null) continue;
            if (MovementRules.ArrivalConsumesAction(tile, territory))
            {
                yield return coord;
            }
        }
    }

    private bool IsValidTarget(UnitLevel attackerLevel, HexCoord coord)
    {
        if (_session.SelectedTerritory == null) return false;
        var targets = MovementRules.ValidTargets(
            attackerLevel, _session.SelectedTerritory, _state.Grid, _state.Territories);
        return targets.Contains(coord);
    }

    /// <summary>
    /// Tell the view to red-flash the rejected target. Only enemy
    /// territory clicks compute defenders — clicks on water / own
    /// territory / non-adjacent tiles pass an empty defender set so the
    /// view plays the generic-rejection sound instead of the defended one.
    /// </summary>
    private void EmitRejection(UnitLevel attackerLevel, HexCoord coord)
    {
        Territory? targetTerritory = TerritoryLookup.FindContaining(_state.Territories, coord);
        PlayerId currentColor = _state.Turns.CurrentPlayer.Id;

        // Only surface defenders when the click was actually reachable —
        // i.e. the target is in the selected territory or touches it.
        // A non-adjacent click is a "too far" rejection regardless of
        // what's defending the far hex.
        bool inFrontier = _session.SelectedTerritory != null
            && (_session.SelectedTerritory.Coords.Contains(coord)
                || coord.Neighbors().Any(n => _session.SelectedTerritory.Coords.Contains(n)));

        System.Collections.Generic.IEnumerable<HexCoord> defenders =
            inFrontier && targetTerritory != null && targetTerritory.Owner != currentColor
                ? DefenseRules.BlockingDefenders(coord, attackerLevel, _state.Grid, targetTerritory)
                : System.Array.Empty<HexCoord>();
        _map.FlashRejection(coord, RejectionShapeExtensions.FromUnitLevel(attackerLevel), defenders);
    }

    /// <summary>
    /// Tower placement target: an empty tile inside the currently
    /// selected territory, at least
    /// <see cref="PurchaseRules.MinTowerSpacing"/> hexes from any
    /// existing same-territory tower. Delegates to
    /// <see cref="PurchaseRules.IsValidTowerLocation"/>.
    /// </summary>
    private bool IsValidTowerTarget(HexCoord coord)
    {
        if (_session.SelectedTerritory == null) return false;
        HexTile? tile = _state.Grid.Get(coord);
        if (tile == null) return false;
        return PurchaseRules.IsValidTowerLocation(tile, _session.SelectedTerritory, _state.Grid);
    }

    /// <summary>
    /// Every coord inside <paramref name="territory"/> on which a tower
    /// can legally be placed right now. Drives the tower-target preview
    /// shown in BuildingTower mode.
    /// </summary>
    private IEnumerable<HexCoord> ValidTowerTargets(Territory territory)
    {
        foreach (HexCoord coord in territory.Coords)
        {
            HexTile? tile = _state.Grid.Get(coord);
            if (tile == null) continue;
            if (PurchaseRules.IsValidTowerLocation(tile, territory, _state.Grid))
            {
                yield return coord;
            }
        }
    }

    /// <summary>
    /// Coords inside <paramref name="territory"/> that are currently
    /// covered by a same-territory tower (the tower's own tile and any
    /// of its neighbors that also belong to the territory). Drives the
    /// subtle "already defended" tint shown in BuildingTower mode so the
    /// player can plan placements without doubling up coverage.
    /// </summary>
    private IEnumerable<HexCoord> TowerCoverageCoords(Territory territory)
    {
        var covered = new HashSet<HexCoord>();
        var inTerritory = new HashSet<HexCoord>(territory.Coords);
        foreach (HexCoord coord in territory.Coords)
        {
            HexTile? tile = _state.Grid.Get(coord);
            if (tile?.Occupant is not Tower) continue;
            covered.Add(coord);
            foreach (HexCoord neighbor in coord.Neighbors())
            {
                if (inTerritory.Contains(neighbor)) covered.Add(neighbor);
            }
        }
        return covered;
    }

    /// <summary>
    /// Diagnostic for the BuildTower-rejection log line: walk the same
    /// checks as <see cref="PurchaseRules.IsValidTowerLocation"/> and
    /// describe whichever first one fails. Strictly debug — never read
    /// by gameplay logic.
    /// </summary>
    private string DescribeInvalidTowerReason(HexCoord coord)
    {
        HexTile? tile = _state.Grid.Get(coord);
        if (tile == null) return "off-map";
        Territory? sel = _session.SelectedTerritory;
        if (sel == null) return "no selected territory";
        if (!sel.Coords.Contains(coord))
            return $"tile not in selected territory (tile color={tile.Owner}, sel owner={sel.Owner})";
        if (tile.Occupant != null)
            return $"tile occupied by {tile.Occupant.GetType().Name}";
        return "(would have been valid — diagnostic stale?)";
    }

    // --- Buy / move / capture --------------------------------------------

    private void ExecuteBuyAndPlace(UnitLevel level, HexCoord destination)
    {
        if (_session.SelectedTerritory == null) return;

        HexCoord capital = _session.SelectedTerritory.Capital!.Value;
        if (_humanActionValidator != null && !_humanActionValidator(
            new ReplayBuyBeat { Capital = capital, To = destination, Level = level }))
        {
            CancelPendingAction();
            _ops.RefreshViews();
            return;
        }

        _handlerMutatedGame = true;
        _pendingHumanBeat = new ReplayBuyBeat
        {
            Capital = capital,
            To = destination,
            Level = level,
        };
        _state.Treasury.SetGold(capital, _state.Treasury.GetGold(capital) - PurchaseRules.CostFor(level, CurrentDifficulty));
        var unit = new Unit(_session.SelectedTerritory.Owner, level);
        // Detect combine before the rule mutates the destination — a
        // friendly Unit at the dst tile means MovementRules will merge
        // them, and we want to fire the level-up chime instead of the
        // place thud.
        bool wasCombine = _ops.WasFriendlyUnitAt(destination, _session.SelectedTerritory.Owner);
        MoveResult result = MovementRules.PlaceNew(unit, destination, _state.Grid, _session.SelectedTerritory);

        if (result.WasCapture)
        {
            _ops.HandleCapture($"Buy {level} → {destination}", capital);
            RebindSelectionToContaining(destination);
        }

        // Dispatch destruction FX after HandleCapture: that path's
        // RebuildAfterTerritoryChange clears the deaths layer to cancel
        // stale corpse animations, which would also wipe a freshly-
        // spawned capture burst if we played it before.
        if (result.Destroyed != null)
        {
            _ops.EmitDestruction(destination, result.Destroyed);
        }

        _ops.DispatchActionSound(destination, result, wasCombine);

        // Combining is an explicit punctuation point in a streak of buys:
        // even with gold left, exit the mode so the player re-presses the
        // buy button to keep going. Otherwise, fall through to the QoL
        // stay-in-mode logic below.
        if (wasCombine)
        {
            Log.Debug(Log.LogCategory.Hud, $"Combine exited BuyingX at {destination}");
            FinishPendingAction();
            return;
        }

        // QoL: stay in a buy mode for the highest level the (possibly
        // rebound) territory can still afford that is at most the level
        // just bought. Stay-at-same-level if still affordable; otherwise
        // degrade downward through Captain → Soldier → Recruit. If no
        // level is affordable, exit. Completion does NOT auto-cycle
        // upward — re-pressing the buy button is what cycles.
        UnitLevel? next = _session.SelectedTerritory == null
            ? null
            : HighestAffordableAtOrBelow(_session.SelectedTerritory, level);
        if (next.HasValue && _session.SelectedTerritory != null)
        {
            _session.Mode = SessionState.BuyModeFor(next.Value);
            _session.MoveSource = null;
            _map.ShowMoveTargets(ActionConsumingTargets(next.Value, _session.SelectedTerritory), next.Value);
            _map.ShowMoveSource(null);
            _ops.RefreshViews();
        }
        else
        {
            FinishPendingAction();
        }
    }

    /// <summary>
    /// Highest level ≤ <paramref name="ceiling"/> that
    /// <paramref name="territory"/> can currently afford, or null if
    /// none. Used by the post-buy fallback so a player who just spent
    /// down past their current level keeps buying at the next-lower
    /// affordable tier instead of being kicked out of buy mode.
    /// </summary>
    private UnitLevel? HighestAffordableAtOrBelow(Territory territory, UnitLevel ceiling)
    {
        for (int i = (int)ceiling; i >= (int)UnitLevel.Recruit; i--)
        {
            UnitLevel candidate = (UnitLevel)i;
            if (PurchaseRules.CanAfford(territory, _state.Treasury, candidate, CurrentDifficulty))
            {
                return candidate;
            }
        }
        return null;
    }

    private void ExecuteMove(HexCoord source, HexCoord destination)
    {
        if (_session.SelectedTerritory == null) return;

        if (_humanActionValidator != null && !_humanActionValidator(
            new ReplayMoveBeat { From = source, To = destination }))
        {
            CancelPendingAction();
            _ops.RefreshViews();
            return;
        }

        _handlerMutatedGame = true;
        _pendingHumanBeat = new ReplayMoveBeat { From = source, To = destination };

        // Capture the source unit's level before MovementRules.Move clears
        // the source tile — auto-advance needs it to walk the next entry
        // in the power-then-coord order.
        UnitLevel movedLevel = _state.Grid.Get(source)!.Unit!.Level;
        HexCoord? originCapital = _session.SelectedTerritory.Capital;
        bool wasCombine = _ops.WasFriendlyUnitAt(destination, _session.SelectedTerritory.Owner);
        MoveResult result = MovementRules.Move(source, destination, _state.Grid, _session.SelectedTerritory);

        if (result.WasCapture)
        {
            _ops.HandleCapture($"Move {source}→{destination}", originCapital);
            RebindSelectionToContaining(destination);
        }

        if (result.Destroyed != null)
        {
            _ops.EmitDestruction(destination, result.Destroyed);
        }

        _ops.DispatchActionSound(destination, result, wasCombine);

        FinishPendingAction();

        // Combining is an explicit punctuation point in a streak of moves:
        // clear the sticky flag so auto-advance stops and the player must
        // re-press N to keep going. Captures still auto-advance.
        if (wasCombine && _session.RepeatedMovement)
        {
            Log.Debug(Log.LogCategory.Hud, $"Combine cleared RepeatedMovement at {destination}");
            _session.RepeatedMovement = false;
        }
        else if (_session.RepeatedMovement)
        {
            AutoAdvanceAfterMove(movedLevel, source, destination);
        }
    }

    /// <summary>
    /// After a capture rebuilds the territory list, the previously
    /// selected <see cref="Territory"/> object is stale. Rebind the
    /// selection to whichever new territory now contains
    /// <paramref name="coord"/> (the tile the attacker just landed on),
    /// so the player's selection survives the capture. Safe to call
    /// after any capture — the attacker always ends up in a territory
    /// they own that contains the destination.
    /// </summary>
    private void RebindSelectionToContaining(HexCoord coord)
    {
        Territory? match = null;
        foreach (Territory t in _state.Territories)
        {
            if (t.Coords.Contains(coord))
            {
                match = t;
                break;
            }
        }
        MarkSelectedVisited(match);
        _session.SelectedTerritory = match;
        _map.ShowHighlight(match);
    }

    /// <summary>
    /// Deduct <see cref="PurchaseRules.TowerCost"/> from the selected
    /// territory's capital and drop a fresh <see cref="Tower"/> on the
    /// destination tile. Towers always build in own territory, so there
    /// is no capture path and the selection stays put.
    /// </summary>
    private void ExecuteBuildTower(HexCoord destination)
    {
        if (_session.SelectedTerritory == null) return;

        HexCoord capital = _session.SelectedTerritory.Capital!.Value;
        if (_humanActionValidator != null && !_humanActionValidator(
            new ReplayBuildTowerBeat { Capital = capital, To = destination }))
        {
            CancelPendingAction();
            _ops.RefreshViews();
            return;
        }

        _handlerMutatedGame = true;
        _pendingHumanBeat = new ReplayBuildTowerBeat
        {
            Capital = capital,
            To = destination,
        };
        _state.Treasury.SetGold(
            capital, _state.Treasury.GetGold(capital) - PurchaseRules.TowerCostFor(CurrentDifficulty));

        HexTile dst = _state.Grid.Get(destination)!;
        dst.Occupant = new Tower();
        if (dst.IsMountain)
        {
            // Towers may stand on mountains for the +1 bonus.
            Log.Debug(Log.LogCategory.Capture,
                $"[tower] placed on mountain at {destination} → defense " +
                $"{DefenseRules.Defense(destination, _state.Grid, _session.SelectedTerritory)}");
        }
        _ops.EmitSound(SoundEffect.TowerPlaced, destination);

        // QoL: stay in BuildingTower mode if the territory can still
        // afford another tower. Refresh both the tower-target preview
        // and the coverage tint — the just-placed tower expands the
        // covered set and removes its own tile from the legal set.
        if (PurchaseRules.CanAffordTower(_session.SelectedTerritory, _state.Treasury, CurrentDifficulty))
        {
            _session.Mode = SessionState.ActionMode.BuildingTower;
            _session.MoveSource = null;
            _map.ShowMoveTargets(System.Array.Empty<HexCoord>(), UnitLevel.Recruit);
            _map.ShowTowerTargets(ValidTowerTargets(_session.SelectedTerritory));
            _map.ShowTowerCoverage(TowerCoverageCoords(_session.SelectedTerritory));
            _map.ShowMoveSource(null);
            _ops.RefreshViews();
        }
        else
        {
            FinishPendingAction();
        }
    }

    private void FinishPendingAction()
    {
        _session.ClearPendingAction();
        _map.ClearAllOverlays();
        // Selection is maintained by the caller: a non-capturing
        // reposition leaves it alone; a capture re-binds it via
        // RebindSelectionToContaining; a tower build leaves it alone.
        _ops.RefreshViews();
    }

    private void CancelPendingAction(string reason = "cancel / invalid click / end-of-turn")
    {
        if (_session.RepeatedMovement)
        {
            Log.Debug(Log.LogCategory.Input,
                $"[RepeatedMovement] cleared ({reason})");
            _session.RepeatedMovement = false;
        }
        _session.ClearPendingAction();
        _map.ClearAllOverlays();
    }

    /// <summary>
    /// Called at the top of every handler that enters a non-None
    /// <see cref="SessionState.ActionMode"/> (buy / build) to clear the
    /// repeated-movement sticky bit. Quiet no-op when the bit is already
    /// off.
    /// </summary>
    private void ClearRepeatedMovementOnActionModeEntry(string reason)
    {
        if (!_session.RepeatedMovement) return;
        Log.Debug(Log.LogCategory.Input,
            $"[RepeatedMovement] cleared ({reason})");
        _session.RepeatedMovement = false;
    }

    private void OnCancelActionPressed() => TrackHandler(OnCancelActionPressedBody);

    private void OnCancelActionPressedBody()
    {
        if (_session.IsGameOver) return;
        CancelPendingAction();
        _ops.RefreshViews();
    }

    // --- Automate loop ------------------------------------------------------
    // A human-turn analogue of AiTurnDriver's paced step machine: ask the
    // automate chooser for the current human's next action, preview-highlight
    // the acting territory, execute it, repeat — until the chooser returns
    // null. Unlike the AI driver it never ends the turn, and every executed
    // move runs through TrackHandler so it pushes its own UndoEntry (with the
    // matching replay-beat batch) exactly like a hand-made move. Interruption
    // is a flag checked at every beat entry: stopping between beats abandons
    // at most an un-applied pending action, never a half-applied mutation.

    // Mirrors AiTurnDriver.MaxAiStepsPerPlayer: a runaway-chooser backstop,
    // far above any real turn's action count.
    private const int MaxAutomateStepsPerTurn = 64;
    private AiAction? _pendingAutomateAction;
    private readonly HashSet<HexCoord> _automateVisited = new();
    private int _automateSteps;
    // True only while StepAutomateExecute's own TrackHandler call is on the
    // stack, so the "any human input interrupts automation" hook in
    // TrackHandler can tell the loop's moves from real input.
    private bool _inAutomateStep;

    private void OnAutomatePressed()
    {
        if (_recorder.IsReplaying) return;
        if (_ops.HumanInputLocked) return;
        // Tutorial Record/Preview: automated moves would bypass the
        // script validator and desync the beat cursor.
        if (_previewMode || _recordingMode) return;
        if (_automating)
        {
            StopAutomation("user");
            return;
        }
        if (_session.IsGameOver || _ops.GameEndedFired) return;
        if (_state.Turns.CurrentPlayer.IsAi) return;
        if (_session.PendingDefeatScreen.HasValue || _session.PendingClaimVictory.HasValue) return;
        // Cancel any half-built intent (open buy/build/move mode) first,
        // same as Esc — TrackHandler-wrapped so the cancel is its own
        // undo entry (deduped away when nothing was pending).
        TrackHandler(OnCancelActionPressedBody);
        _automating = true;
        _automateVisited.Clear();
        _automateSteps = 0;
        _pendingAutomateAction = null;
        Log.Debug(Log.LogCategory.Automate,
            $"[automate] start T{_state.Turns.TurnNumber} {_state.Turns.CurrentPlayer.Name} " +
            $"undoDepth={_session.Undo.UndoCount}");
        _ops.RefreshViews(); // Automate button flips to running/Stop
        _aiPacer.Schedule(StepAutomatePreview, StepPacing.AiActionDelayMs);
    }

    /// <summary>
    /// Conditions that end automation regardless of the chooser: the
    /// game ended, control left the human (defensive), or an overlay
    /// needs a player decision (defeat / claim-victory).
    /// </summary>
    private bool AutomateHalted(out string reason)
    {
        if (_ops.GameEndedFired || _session.IsGameOver) { reason = "game-over"; return true; }
        if (_state.Turns.CurrentPlayer.IsAi) { reason = "not-human"; return true; }
        if (_session.PendingDefeatScreen.HasValue || _session.PendingClaimVictory.HasValue)
        {
            reason = "overlay";
            return true;
        }
        reason = "";
        return false;
    }

    private void StepAutomatePreview()
    {
        if (!_automating) return;
        if (AutomateHalted(out string halt)) { StopAutomation(halt); return; }
        if (_automateSteps >= MaxAutomateStepsPerTurn) { StopAutomation("step-cap"); return; }
        PlayerId me = _state.Turns.CurrentPlayer.Id;
        AiAction? action = _automateChooser(_state, me, _automateVisited, _ops.Rng);
        if (action == null)
        {
            StopAutomation("exhausted", exhausted: true);
            return;
        }
        _pendingAutomateAction = action;
        _ops.ShowHighlightAndRefresh(_aiDriver.ResolveAiActingTerritory(action));
        _aiPacer.Schedule(StepAutomateExecute, StepPacing.AiPreviewDelayMs);
    }

    private void StepAutomateExecute()
    {
        if (!_automating) return;
        AiAction? action = _pendingAutomateAction;
        _pendingAutomateAction = null;
        if (action == null) return;
        if (AutomateHalted(out string halt)) { StopAutomation(halt); return; }
        _automateSteps++;
        PlayerId me = _state.Turns.CurrentPlayer.Id;
        HexCoord? result = null;
        _inAutomateStep = true;
        try
        {
            // The move is a first-class human action: TrackHandler
            // captures the pre-state and pushes one UndoEntry, batching
            // the replay beat ApplyAiActionCore records inside the body.
            TrackHandler(() =>
            {
                _handlerMutatedGame = true;
                result = _aiDriver.ApplyAiActionCore(action);
            });
        }
        finally
        {
            _inAutomateStep = false;
        }
        RebindSelectionAfterAutomateMove();
        _ops.CheckGameEndConditions();
        Log.Debug(Log.LogCategory.Automate,
            $"[automate] move {_automateSteps}: {DescribeAutomateAction(action)} " +
            $"undoDepth={_session.Undo.UndoCount}");
        if (AutomateHalted(out string postHalt)) { StopAutomation(postHalt); return; }
        Territory? after = result.HasValue
            ? TerritoryLookup.FindOwnedContaining(_state.Territories, me, result.Value)
            : null;
        _ops.ShowHighlightAndRefresh(after);
        _aiPacer.Schedule(StepAutomatePreview, StepPacing.AiActionDelayMs);
    }

    /// <summary>
    /// End automation and hand control back to the human. Never called
    /// mid-mutation: every caller sits between pacer beats (or at a
    /// beat's guarded entry), so already-executed moves stay individually
    /// undoable and the state is a clean between-moves state. Stale
    /// scheduled beats no-op on the cleared flag, so the shared pacer is
    /// never cancelled (an AI-driver callback could be in flight).
    /// </summary>
    private void StopAutomation(string reason, bool exhausted = false)
    {
        if (!_automating) return;
        _automating = false;
        _pendingAutomateAction = null;
        _automateExhausted = exhausted;
        Log.Debug(Log.LogCategory.Automate,
            $"[automate] stop after {_automateSteps} move(s): {reason} " +
            $"undoDepth={_session.Undo.UndoCount}");
        // Restore the highlight to the player's own selection (clears
        // the loop's acting-territory highlight) and un-toggle the button.
        _ops.ShowHighlightAndRefresh(_session.SelectedTerritory);
    }

    /// <summary>
    /// An automated capture recomputes the territory list, leaving
    /// <see cref="SessionState.SelectedTerritory"/> pointing at a stale
    /// object (manual moves rebind via
    /// <see cref="RebindSelectionToContaining"/>; the ExecuteAi* path
    /// never touches the selection). Re-resolve the same logical
    /// territory by its capital so the selection border keeps tracking
    /// the territory's current shape; a selection whose capital no
    /// longer exists clears.
    /// </summary>
    private void RebindSelectionAfterAutomateMove()
    {
        Territory? sel = _session.SelectedTerritory;
        if (sel == null) return;
        foreach (Territory t in _state.Territories)
        {
            if (ReferenceEquals(t, sel)) return; // still current
        }
        _session.SelectedTerritory = sel.HasCapital
            ? TerritoryLookup.FindByCapital(_state.Territories, sel.Capital!.Value)
            : null;
    }

    private static string DescribeAutomateAction(AiAction action) => action switch
    {
        AiMoveAction mv => $"Move {mv.Source}->{mv.Destination}",
        AiBuyUnitAction bu => $"Buy {bu.Level}@{bu.Capital}->{bu.Destination}",
        AiBuildTowerAction bt => $"Tower@{bt.Capital}->{bt.Destination}",
        AiBuyCombineAction bc => $"BuyCombine {bc.BuyLevel}@{bc.Capital}->{bc.CombineTarget}",
        _ => action.GetType().Name,
    };

    /// <summary>
    /// Defeat-overlay Continue handler. Clears the overlay flag and
    /// re-arms the AI loop so it picks up where it left off (defeat
    /// fires inside the driver's execute beat, which then skipped
    /// scheduling the next preview while the flag was set).
    /// </summary>
    private void OnDefeatContinuePressed()
    {
        if (_recorder.IsReplaying) return;
        if (_ops.InSilentAiBatch()) return;
        if (_automating) StopAutomation("input");
        if (!_session.PendingDefeatScreen.HasValue) return;
        if (_humanActionValidator != null && !_humanActionValidator(new ReplayDismissDefeatBeat()))
        {
            return;
        }
        _hud.DismissCapitalAlertNotice();
        _recorder.RecordBeat(new ReplayDismissDefeatBeat());
        _session.PendingDefeatScreen = null;
        // Re-arm silent mode if we were in a silent batch — clearing
        // PendingDefeatScreen makes InSilentAiBatch() flip back to
        // true, so push that change to the view BEFORE the refresh
        // (otherwise tweens for the post-dismiss state would leak).
        _ops.RefreshSilentMode();
        _ops.RefreshViews();
        if (_session.IsGameOver) return;
        if (_state.Turns.CurrentPlayer.IsAi || _ops.VikingPhaseActive)
        {
            // Viking Raiders: a defeat raised mid-viking-phase pauses the
            // phase exactly like a mid-AI-turn defeat; the same re-arm
            // finishes it (the driver's viking scratch is still live).
            _aiDriver.Schedule(turnBoundary: false);
        }
        else if (WinConditionRules.IsEliminated(_state.Turns.CurrentPlayer.Id, _state.Grid))
        {
            // Rising Tides: the current human was defeated by their
            // own start-of-turn submerge — they can't act, so end their (empty)
            // turn to advance past them now that the overlay is dismissed.
            EndTurnNow();
        }
    }

    // --- Undo / redo ------------------------------------------------------

    // Fog Of War disables undo/redo entirely: fog memory is sticky across undo,
    // so undoing a capture/build after it revealed tiles would be free scouting.
    private void OnUndoLastPressed()
    {
        if (_state.FogEnabled) return;
        if (_recorder.IsReplaying) return;
        if (_ops.HumanInputLocked) return;
        if (_automating) StopAutomation("input");
        if (_session.IsGameOver) return;
        if (!_session.Undo.CanUndo) return;
        _hud.DismissCapitalAlertNotice();
        HexCoord? before = _session.SelectedTerritory?.Capital;
        ApplySnapshot(_recorder.UndoOneStep(CaptureCurrentSnapshot()));
        CenterIfSelectionChanged(before);
    }

    private void OnUndoTurnPressed()
    {
        if (_state.FogEnabled) return;
        if (_recorder.IsReplaying) return;
        if (_ops.HumanInputLocked) return;
        if (_automating) StopAutomation("input");
        if (_session.IsGameOver) return;
        if (!_session.Undo.CanUndo) return;
        _hud.DismissCapitalAlertNotice();
        // Step (not UndoAll) so each pop's beat bookkeeping fires atomically.
        UndoEntry restored = _recorder.UndoOneStep(CaptureCurrentSnapshot());
        while (_session.Undo.CanUndo)
        {
            restored = _recorder.UndoOneStep(restored);
        }
        ApplySnapshot(restored);
    }

    private void OnRedoLastPressed()
    {
        if (_state.FogEnabled) return;
        if (_recorder.IsReplaying) return;
        if (_ops.HumanInputLocked) return;
        if (_automating) StopAutomation("input");
        if (_session.IsGameOver) return;
        if (!_session.Undo.CanRedo) return;
        _hud.DismissCapitalAlertNotice();
        HexCoord? before = _session.SelectedTerritory?.Capital;
        ApplySnapshot(_recorder.RedoOneStep(CaptureCurrentSnapshot()));
        CenterIfSelectionChanged(before);
    }

    private void OnRedoAllPressed()
    {
        if (_state.FogEnabled) return;
        if (_recorder.IsReplaying) return;
        if (_ops.HumanInputLocked) return;
        if (_automating) StopAutomation("input");
        if (_session.IsGameOver) return;
        if (!_session.Undo.CanRedo) return;
        _hud.DismissCapitalAlertNotice();
        UndoEntry restored = _recorder.RedoOneStep(CaptureCurrentSnapshot());
        while (_session.Undo.CanRedo)
        {
            restored = _recorder.RedoOneStep(restored);
        }
        ApplySnapshot(restored);
    }

    /// <summary>
    /// Clear the per-turn undo stack and the parallel beat-tracking
    /// stacks on the recorder together. The three sites that commit
    /// "no more undo" (end of turn, mid-turn domination, claim-victory
    /// win) all need to drop replay bookkeeping in lockstep —
    /// otherwise a subsequent undo would pop into a phantom beat count.
    /// </summary>
    private void ClearUndoAndReplayBookkeeping() =>
        _recorder.ClearUndoAndBookkeeping();

    /// <summary>
    /// Single-step undo / redo centers the view on the new selection when
    /// it differs from the pre-step selection — so the player follows the
    /// territory they're rolling back to. Compared by capital coord, not
    /// reference, because <see cref="SessionStateSnapshot.ApplyTo"/>
    /// resolves the territory anew from the restored list.
    /// Undo-all and redo-all deliberately skip this — those are global
    /// rewinds, not selection navigation.
    /// </summary>
    private void CenterIfSelectionChanged(HexCoord? beforeCapital)
    {
        Territory? after = _session.SelectedTerritory;
        if (after == null || !after.HasCapital) return;
        if (after.Capital == beforeCapital) return;
        _map.CenterOnTerritory(after);
    }

    /// <summary>
    /// Restore game and session state from <paramref name="entry"/>,
    /// rebuild the view's derived state, re-emit the overlays implied by
    /// the restored mode, and refresh. Shared by undo and redo.
    /// </summary>
    private void ApplySnapshot(UndoEntry entry)
    {
        // Undo/redo re-opens moves the exhausted automate verdict ruled
        // out, so the Automate button un-latches.
        _automateExhausted = false;
        _state.Territories = entry.Game.ApplyTo(_state.Grid, _state.Treasury);
        _map.RebuildAfterTerritoryChange();
        entry.Session.ApplyTo(_session, _state.Territories);
        RestoreOverlaysForCurrentMode();
        _ops.RefreshViews();
    }

    /// <summary>
    /// Re-emit every map overlay implied by the current
    /// <see cref="SessionState"/>: highlight ring on the selected
    /// territory, plus move-target rings, move-source ring, tower-target
    /// previews, and tower-coverage tint for the pending action mode (if
    /// any). Called after undo/redo restores session state, so the view
    /// matches the restored intent. Every branch must drive each overlay
    /// sink to either the right set or empty — otherwise stale visuals
    /// from the pre-undo state survive the restore.
    /// </summary>
    private void RestoreOverlaysForCurrentMode()
    {
        _map.ShowHighlight(_session.SelectedTerritory);

        UnitLevel? buyLevel = SessionState.BuyModeLevel(_session.Mode);
        if (buyLevel.HasValue && _session.SelectedTerritory != null)
        {
            _map.ShowMoveTargets(ActionConsumingTargets(buyLevel.Value, _session.SelectedTerritory), buyLevel.Value);
            _map.ShowTowerTargets(System.Array.Empty<HexCoord>());
            _map.ShowTowerCoverage(System.Array.Empty<HexCoord>());
            _map.ShowMoveSource(null);
            return;
        }
        if (_session.Mode == SessionState.ActionMode.BuildingTower
            && _session.SelectedTerritory != null)
        {
            _map.ShowMoveTargets(System.Array.Empty<HexCoord>(), UnitLevel.Recruit);
            _map.ShowTowerTargets(ValidTowerTargets(_session.SelectedTerritory));
            _map.ShowTowerCoverage(TowerCoverageCoords(_session.SelectedTerritory));
            _map.ShowMoveSource(null);
            return;
        }
        if (_session.Mode == SessionState.ActionMode.MovingUnit
            && _session.MoveSource.HasValue
            && _session.SelectedTerritory != null)
        {
            HexTile? src = _state.Grid.Get(_session.MoveSource.Value);
            if (src?.Unit != null)
            {
                _map.ShowMoveTargets(ActionConsumingTargets(src.Unit.Level, _session.SelectedTerritory), src.Unit.Level);
                _map.ShowTowerTargets(System.Array.Empty<HexCoord>());
                _map.ShowTowerCoverage(System.Array.Empty<HexCoord>());
                _map.ShowMoveSource(_session.MoveSource);
                return;
            }
            // Defensive fallback: source unit no longer exists.
            _session.Mode = SessionState.ActionMode.None;
            _session.MoveSource = null;
        }
        _map.ClearAllOverlays();
    }

    // --- HUD buttons ------------------------------------------------------

    /// <summary>
    /// The unit levels considered by the buy-button cycle, in cycle
    /// order (Recruit→Soldier→Captain→Commander→Recruit).
    /// </summary>
    private static readonly UnitLevel[] BuyCycleOrder =
    {
        UnitLevel.Recruit,
        UnitLevel.Soldier,
        UnitLevel.Captain,
        UnitLevel.Commander,
    };

    /// <summary>
    /// Buy-cycle handler: from no buy mode, enters the lowest affordable
    /// level; from an existing buy mode, advances to the next higher
    /// affordable level. From the most-expensive affordable level the
    /// next press exits to <see cref="SessionState.ActionMode.None"/> —
    /// the cycle does NOT wrap back to Recruit. Same handler is invoked
    /// by the `u` hotkey.
    /// </summary>
    private void OnBuyPressed() => TrackHandler(OnBuyPressedBody);

    /// <summary>
    /// Direct per-level buy handler: clicking one of the four radio
    /// buttons enters that specific buy mode (no cycling). Toggles —
    /// clicking the already-active level cancels the mode (like Escape).
    /// Unaffordable or "no selection" clicks are no-ops.
    /// </summary>
    private void OnBuyUnitPressed(UnitLevel level) => TrackHandler(() => OnBuyUnitPressedBody(level));

    private void OnBuyUnitPressedBody(UnitLevel level)
    {
        if (_session.IsGameOver) return;
        if (_session.SelectedTerritory == null) return;
        ClearRepeatedMovementOnActionModeEntry("buy unit button");
        // Toggle off: a second click on the active buy level cancels the
        // mode (like Escape). Checked before affordability so you can
        // always back out of a mode you're already in.
        if (SessionState.BuyModeLevel(_session.Mode) == level)
        {
            Log.Debug(Log.LogCategory.Input,
                $"[Buy] re-click on active level {level} → toggle mode off");
            CancelPendingAction();
            _ops.RefreshViews();
            return;
        }
        if (!PurchaseRules.CanAfford(_session.SelectedTerritory, _state.Treasury, level, CurrentDifficulty)) return;
        // Tutorial Preview: refuse the switch if the script's next beat
        // isn't a buy at this level. Lets the dev only enter the
        // mode the tutorial expects.
        if (_buyLevelValidator != null && !_buyLevelValidator(level)) return;

        _session.Mode = SessionState.BuyModeFor(level);
        _session.MoveSource = null;
        _map.ShowMoveTargets(ActionConsumingTargets(level, _session.SelectedTerritory), level);
        // Switching into a buy mode from BuildingTower leaves the tower
        // preview + coverage tint stale; clear both so the player only
        // sees relevant CTAs.
        _map.ShowTowerTargets(System.Array.Empty<HexCoord>());
        _map.ShowTowerCoverage(System.Array.Empty<HexCoord>());
        _map.ShowMoveSource(null);
        _ops.RefreshViews();
    }

    private void OnBuyPressedBody()
    {
        if (_session.IsGameOver) return;
        if (_session.SelectedTerritory == null) return;
        ClearRepeatedMovementOnActionModeEntry("buy hotkey");

        UnitLevel? next = NextAffordableBuyLevel();
        UnitLevel? current = SessionState.BuyModeLevel(_session.Mode);

        // Tutorial Preview: if the cycle would land on a level the
        // script doesn't expect, refuse the switch (stay put) instead
        // of advancing or exiting.
        if (next.HasValue && _buyLevelValidator != null && !_buyLevelValidator(next.Value)) return;

        if (next == null)
        {
            // No higher affordable level. If we're already in a buy mode,
            // exit to None (cycle past the top). Otherwise nothing to do.
            if (current == null) return;
            _session.Mode = SessionState.ActionMode.None;
            _session.MoveSource = null;
            _map.ShowMoveTargets(System.Array.Empty<HexCoord>(), UnitLevel.Recruit);
            _map.ShowTowerTargets(System.Array.Empty<HexCoord>());
            _map.ShowTowerCoverage(System.Array.Empty<HexCoord>());
            _map.ShowMoveSource(null);
            _ops.RefreshViews();
            return;
        }

        _session.Mode = SessionState.BuyModeFor(next.Value);
        _session.MoveSource = null;
        _map.ShowMoveTargets(ActionConsumingTargets(next.Value, _session.SelectedTerritory), next.Value);
        // Switching into a buy mode from BuildingTower leaves the tower
        // preview + coverage tint stale; clear both so the player only
        // sees relevant CTAs.
        _map.ShowTowerTargets(System.Array.Empty<HexCoord>());
        _map.ShowTowerCoverage(System.Array.Empty<HexCoord>());
        _map.ShowMoveSource(null);
        _ops.RefreshViews();
    }

    /// <summary>
    /// Next strictly-higher affordable buy level after the current mode.
    /// If no buy mode is active, returns the lowest affordable level
    /// (cycle entry from None). Returns null when nothing affordable
    /// exists at-or-above the current position — the cycle does NOT
    /// wrap; the caller treats null as "exit to None".
    /// </summary>
    private UnitLevel? NextAffordableBuyLevel()
    {
        if (_session.SelectedTerritory == null) return null;
        Territory selected = _session.SelectedTerritory;

        UnitLevel? current = SessionState.BuyModeLevel(_session.Mode);
        int startIndex = 0;
        if (current.HasValue)
        {
            for (int i = 0; i < BuyCycleOrder.Length; i++)
            {
                if (BuyCycleOrder[i] == current.Value)
                {
                    startIndex = i + 1;
                    break;
                }
            }
        }

        for (int i = startIndex; i < BuyCycleOrder.Length; i++)
        {
            UnitLevel candidate = BuyCycleOrder[i];
            if (PurchaseRules.CanAfford(selected, _state.Treasury, candidate, CurrentDifficulty))
            {
                return candidate;
            }
        }
        return null;
    }

    private void OnBuildTowerPressed() => TrackHandler(OnBuildTowerPressedBody);

    private void OnBuildTowerPressedBody()
    {
        if (_session.IsGameOver) return;
        if (_session.SelectedTerritory == null) return;
        ClearRepeatedMovementOnActionModeEntry("build tower");
        // Toggle off: a second click while already building cancels the
        // mode (like Escape).
        if (_session.Mode == SessionState.ActionMode.BuildingTower)
        {
            Log.Debug(Log.LogCategory.Input, "[BuildTower] re-click while building → toggle mode off");
            CancelPendingAction();
            _ops.RefreshViews();
            return;
        }
        if (!PurchaseRules.CanAffordTower(_session.SelectedTerritory, _state.Treasury, CurrentDifficulty)) return;

        _session.Mode = SessionState.ActionMode.BuildingTower;
        _session.MoveSource = null;
        // Towers only build on empty own-territory tiles — no enemy
        // capture targets to highlight. The legal-tower preview goes
        // through ShowTowerTargets so the player sees where to click,
        // and ShowTowerCoverage tints already-defended cells so the
        // player can avoid stacking coverage.
        _map.ShowMoveTargets(System.Array.Empty<HexCoord>(), UnitLevel.Recruit);
        _map.ShowTowerTargets(ValidTowerTargets(_session.SelectedTerritory));
        _map.ShowTowerCoverage(TowerCoverageCoords(_session.SelectedTerritory));
        _map.ShowMoveSource(null);
        _ops.RefreshViews();
    }

    /// <summary>
    /// Advance the selection to the next or previous current-player
    /// multi-hex territory in lex-min-capital order, wrapping around.
    /// Used by Tab (forward) and Shift+Tab (backward). Singletons are
    /// excluded because you can't do anything with them, and territories
    /// with no available action (no unmoved unit and not enough gold for
    /// the cheapest purchase) are skipped over so a quick Tab-cycle only
    /// stops on territories where the player can act. If no territory
    /// in the cycle is actionable the press is a no-op — the End Turn
    /// CTA is highlighted in that case (see
    /// <see cref="HasAnyActionableForCurrentPlayer"/>) and stepping
    /// further would just churn through useless selections. Cancels any
    /// pending buy/build/move action so the user isn't stuck in a
    /// stale action mode on a different territory.
    /// </summary>
    private void OnNextTerritoryPressed() =>
        TrackHandler(() => StepTerritorySelection(forward: true));

    private void OnPreviousTerritoryPressed() =>
        TrackHandler(() => StepTerritorySelection(forward: false));

    private void StepTerritorySelection(bool forward)
    {
        if (_session.IsGameOver) return;

        PlayerId color = _state.Turns.CurrentPlayer.Id;
        List<Territory> owned = TerritoryLookup
            .OwnedCapitalBearing(_state.Territories, color)
            .ToList();
        if (owned.Count == 0) return;
        owned.Sort((a, b) =>
        {
            int sizeComp = b.Size.CompareTo(a.Size);
            return sizeComp != 0 ? sizeComp : a.Capital!.Value.CompareTo(b.Capital!.Value);
        });
        Log.Debug(Log.LogCategory.Input,
            $"StepTerritorySelection: {owned.Count} territories, largest first = capital {owned[0].Capital} size={owned[0].Size}");

        int currentIndex = -1;
        if (_session.SelectedTerritory != null)
        {
            for (int i = 0; i < owned.Count; i++)
            {
                if (ReferenceEquals(owned[i], _session.SelectedTerritory))
                {
                    currentIndex = i;
                    break;
                }
            }
        }

        // Walk forward/backward from the current index, stopping on the
        // first actionable territory. With a null selection the walk
        // visits every territory once; with one selected it visits every
        // OTHER territory exactly once and never revisits the current
        // one — so if the current selection is the sole actionable
        // territory the press is a no-op.
        //
        // Two passes: the sort key (Size) mutates as the player
        // acts, so walk position alone can't guarantee a fair tour —
        // pass 1 only stops on territories not yet visited this turn.
        // When every actionable territory has been toured, pass 2 starts
        // a fresh round: reset the visited set and stop on the first
        // actionable territory regardless. The reset only happens when
        // pass 2 actually has somewhere to land, so a press with no
        // reachable territory stays a true no-op (no set churn, no
        // undo entry).
        int step = forward ? 1 : -1;
        int startIndex = currentIndex == -1
            ? (forward ? -1 : owned.Count)
            : currentIndex;
        int maxOffset = currentIndex == -1 ? owned.Count : owned.Count - 1;

        int Find(bool skipVisited)
        {
            for (int offset = 1; offset <= maxOffset; offset++)
            {
                int idx = ((startIndex + step * offset) % owned.Count + owned.Count) % owned.Count;
                if (!_ops.TerritoryHasAvailableAction(owned[idx])) continue;
                if (skipVisited
                    && _session.VisitedTerritoryCapitals.Contains(owned[idx].Capital!.Value))
                {
                    continue;
                }
                return idx;
            }
            return -1;
        }

        int pick = Find(skipVisited: true);
        bool newRound = false;
        if (pick == -1)
        {
            pick = Find(skipVisited: false);
            newRound = pick != -1;
        }
        if (pick == -1)
        {
            Log.Debug(Log.LogCategory.Input,
                $"StepTerritorySelection(forward={forward}) -> no actionable territory (no-op)");
            return;
        }
        if (newRound)
        {
            Log.Debug(Log.LogCategory.Input,
                $"[visited] new round: set reset ({_session.VisitedTerritoryCapitals.Count} entries dropped)");
            _session.VisitedTerritoryCapitals.Clear();
        }
        CancelPendingAction();
        SetSelection(owned[pick]);
        _map.CenterOnTerritory(owned[pick]);
        Log.Debug(Log.LogCategory.Input,
            $"StepTerritorySelection(forward={forward}) -> selected capital {owned[pick].Capital} ({(newRound ? "new round" : "unvisited")})");
    }

    /// <summary>
    /// Cycle the move-source through the current player's unmoved units
    /// inside <see cref="SessionState.SelectedTerritory"/>. N goes forward
    /// (highest-power first when nothing is picked up; coord-lex within
    /// a tier); Shift+N goes backward (lowest-power first). Acts exactly
    /// like clicking the next unit: enters MovingUnit mode and re-emits
    /// the move-target ring. Does not pan the camera — the territory is
    /// already in view.
    /// </summary>
    private void OnNextUnitPressed() =>
        TrackHandler(() => StepUnitSelection(forward: true));

    private void OnPreviousUnitPressed() =>
        TrackHandler(() => StepUnitSelection(forward: false));

    private void StepUnitSelection(bool forward)
    {
        if (_session.IsGameOver) return;
        Territory? selected = _session.SelectedTerritory;
        if (selected == null) return;

        PlayerId color = _state.Turns.CurrentPlayer.Id;
        List<HexCoord> movable = SortedMovableCoords(selected, color);
        if (movable.Count == 0) return;

        int currentIndex = -1;
        if (_session.Mode == SessionState.ActionMode.MovingUnit
            && _session.MoveSource.HasValue)
        {
            currentIndex = movable.IndexOf(_session.MoveSource.Value);
        }
        int nextIndex = forward
            ? (currentIndex + 1) % movable.Count
            : (currentIndex == -1 ? movable.Count - 1 : (currentIndex - 1 + movable.Count) % movable.Count);
        if (nextIndex == currentIndex) return;

        HexCoord target = movable[nextIndex];
        Unit chosen = _state.Grid.Get(target)!.Unit!;
        _session.Mode = SessionState.ActionMode.MovingUnit;
        _session.MoveSource = target;
        _session.RepeatedMovement = true;
        Log.Debug(Log.LogCategory.Input,
            $"[N-cycle] forward={forward} count={movable.Count} pickedIdx={nextIndex} coord={target} level={chosen.Level} → RepeatedMovement on");
        _map.ShowMoveTargets(ActionConsumingTargets(chosen.Level, selected), chosen.Level);
        // Defensive: clear tower overlays in case we're transitioning out
        // of BuildingTower mode.
        _map.ShowTowerTargets(System.Array.Empty<HexCoord>());
        _map.ShowTowerCoverage(System.Array.Empty<HexCoord>());
        _map.ShowMoveSource(target);
        _ops.RefreshViews();
    }

    /// <summary>
    /// True if <paramref name="coord"/> sits inside
    /// <paramref name="territory"/> or shares a border with it — i.e.,
    /// a unit move or freshly-bought recruit *could* legally end up
    /// there given the right unit level / defender layout. Used by
    /// <see cref="OnTileClickedBody"/> to distinguish in-range near-miss
    /// clicks (flash + stay in mode) from out-of-range clicks (flash +
    /// cancel mode).
    /// </summary>
    private static bool IsCoordReachableForUnitAction(HexCoord coord, Territory territory)
    {
        if (territory.Coords.Contains(coord)) return true;
        foreach (HexCoord own in territory.Coords)
        {
            foreach (HexCoord neighbor in own.Neighbors())
            {
                if (neighbor.Equals(coord)) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// True if <paramref name="coord"/> sits inside
    /// <paramref name="territory"/>. Towers can only be built on the
    /// selected territory's own tiles — adjacent enemy / friendly-
    /// sibling / water tiles are out of range. Parallel to
    /// <see cref="IsCoordReachableForUnitAction"/>; kept as a named
    /// helper so the call site reads symmetrically and any future
    /// expansion of tower rules has one place to update.
    /// </summary>
    private static bool IsCoordReachableForTowerAction(HexCoord coord, Territory territory)
    {
        return territory.Coords.Contains(coord);
    }

    /// <summary>
    /// Movable units in <paramref name="territory"/> owned by
    /// <paramref name="color"/> with HasMovedThisTurn=false, returned in
    /// power-then-coord order. Thin forwarder to
    /// <see cref="MovementRules.MovableUnitsInPowerOrder"/>, the
    /// single source of truth shared with the AI candidate enumerator
    /// (<c>AiCommon.Enumerate</c>) so the N-cycle and the AI's
    /// tie-breaking iteration can't drift.
    /// </summary>
    private List<HexCoord> SortedMovableCoords(Territory territory, PlayerId color)
        => MovementRules.MovableUnitsInPowerOrder(territory, color, _state.Grid);

    /// <summary>
    /// Repeated-movement auto-advance: called after a successful
    /// <see cref="ExecuteMove"/> while <see cref="SessionState.RepeatedMovement"/>
    /// is on. Picks the next movable unit in power-then-coord order
    /// strictly after the moved unit's (Level, source) key, wrapping to
    /// the first if none qualify. If no movable units remain in the
    /// (possibly capture-rebound) selected territory, clears the flag.
    /// The just-acted-on tile (<paramref name="movedDestination"/>) is
    /// excluded from the candidate pool: an in-territory reposition or
    /// friendly combine doesn't set HasMovedThisTurn, so without this
    /// filter auto-advance would re-pick the same unit at its new spot.
    /// </summary>
    private void AutoAdvanceAfterMove(UnitLevel movedLevel, HexCoord movedSource, HexCoord movedDestination)
    {
        Territory? selected = _session.SelectedTerritory;
        if (selected == null)
        {
            _session.RepeatedMovement = false;
            Log.Debug(Log.LogCategory.Input,
                "[AutoAdvance] no selected territory after move → RepeatedMovement cleared");
            return;
        }
        PlayerId color = _state.Turns.CurrentPlayer.Id;
        List<HexCoord> movable = SortedMovableCoords(selected, color);
        movable.Remove(movedDestination);
        if (movable.Count == 0)
        {
            _session.RepeatedMovement = false;
            Log.Debug(Log.LogCategory.Input,
                "[AutoAdvance] no movable units remaining → RepeatedMovement cleared");
            return;
        }
        // Find first entry with (Level, Coord) strictly > (movedLevel,
        // movedSource); else wrap to first.
        HexCoord pick = movable[0];
        foreach (HexCoord c in movable)
        {
            Unit u = _state.Grid.Get(c)!.Unit!;
            int byLevel = movedLevel.CompareTo(u.Level);
            int cmp = byLevel != 0 ? byLevel : c.CompareTo(movedSource);
            if (cmp > 0) { pick = c; break; }
        }
        Unit chosen = _state.Grid.Get(pick)!.Unit!;
        _session.Mode = SessionState.ActionMode.MovingUnit;
        _session.MoveSource = pick;
        Log.Debug(Log.LogCategory.Input,
            $"[AutoAdvance] picked {pick} level={chosen.Level} (after move from {movedSource} level={movedLevel})");
        _map.ShowMoveTargets(ActionConsumingTargets(chosen.Level, selected), chosen.Level);
        _map.ShowTowerTargets(System.Array.Empty<HexCoord>());
        _map.ShowTowerCoverage(System.Array.Empty<HexCoord>());
        _map.ShowMoveSource(pick);
        _ops.RefreshViews();
    }

    private void OnEndTurnPressed()
    {
        if (_recorder.IsReplaying) return;
        if (_ops.HumanInputLocked) return;
        if (_automating) StopAutomation("input");
        if (_session.IsGameOver) return;
        if (_humanActionValidator != null && !_humanActionValidator(new ReplayEndTurnBeat()))
        {
            return;
        }
        _hud.DismissCapitalAlertNotice();

        // Claim-victory prompt: a human pressing End Turn while crossing
        // a tier in WinConditionRules.ClaimVictoryThresholdsPercent
        // (50/75/90) is offered an early win. Each tier fires at most
        // once per human per game; "show only highest unseen" picks the
        // topmost unseen tier the player meets. AI players are skipped;
        // Tutorial Preview and Record modes also suppress the prompt
        // (it would interrupt the scripted / recording flow with a
        // modal the tutorial author can't pre-record).
        Player current = _state.Turns.CurrentPlayer;
        // Skip the prompt entirely when this End Turn already wins
        // outright: the player is the sole capital-bearer (every opponent
        // is down to orphan singletons / eliminated), so EndTurnNow's
        // WinnerAtEndOfTurn check will declare the win. Offering "Claim
        // Victory or Continue?" is meaningless once victory is sealed —
        // either choice just shows the victory screen — so go straight
        // there.
        bool alreadyWon = WinConditionRules.WinnerAtEndOfTurn(
            current.Id, _state.Territories) == current.Id;
        // Preview suppresses the prompt while scripted, but once the
        // tutorial graduates to free play (see GraduateFromTutorialScripting)
        // ordinary claim-victory rules resume.
        bool previewScripted = _previewMode && !_previewScriptingComplete;
        // Claim-victory tiers apply in BOTH modes: in
        // Rising Tides the percentage is measured against the current,
        // non-sunk tiles — submerged tiles are removed from the grid, so
        // NextClaimVictoryThreshold (which counts _state.Grid) already tracks
        // the shrinking board automatically.
        // Viking Raiders: no claim while any viking threat remains — no
        // win of any kind is possible until the onslaught is cleared.
        if (!alreadyWon && !current.IsAi && !previewScripted && !_recordingMode
            && !_ops.VikingThreatActive)
        {
            int seen = _session.ClaimVictoryPromptedHighestThreshold
                .TryGetValue(current.Id, out int s) ? s : 0;
            int? next = WinConditionRules.NextClaimVictoryThreshold(
                current.Id, _state.Grid, seen);
            if (next.HasValue)
            {
                _session.PendingClaimVictory = (current.Id, next.Value);
                _ops.RefreshViews();
                return;
            }
        }

        EndTurnNow();
    }

    /// <summary>
    /// The body of End Turn after any optional pre-prompts have been
    /// resolved. Splits out so <see cref="OnClaimVictoryContinuePressed"/>
    /// can run the same end-of-turn flow after the user dismisses the
    /// claim-victory overlay with "Continue Playing".
    /// </summary>
    private void EndTurnNow()
    {
        // A fresh turn starts with a fresh automate verdict.
        _automateExhausted = false;
        // Record the end-turn beat with the *ending* player's actor /
        // turn metadata, before clearing the undo stack and advancing.
        // AI implicit end-of-turn (the driver's null-action branch)
        // records its own EndTurnBeat — the two sites are disjoint.
        if (!_recorder.IsReplaying) _recorder.RecordBeat(new ReplayEndTurnBeat());

        // Ending the turn commits everything; no further undo.
        ClearUndoAndReplayBookkeeping();

        _ops.EndOfTurnProcessing();
        if (_session.IsGameOver)
        {
            // End-of-turn win check fired. Don't advance to a player
            // who shouldn't get a turn — just announce the result.
            _ops.CheckGameEndConditions();
        }
        else
        {
            _ops.AdvanceToNextActivePlayer();
            // Round boundary in Viking Raiders: defer StartPlayerTurn —
            // the driver runs the viking pseudo-turn first and starts the
            // waiting player's turn when it completes.
            if (!_ops.VikingTurnPending) _ops.StartPlayerTurn();
            _aiDriver.RunUntilHumanOrDone();
        }

        CancelPendingAction();
        // Human-next: StartPlayerTurn's hand-off already auto-selected the
        // player's first territory (which clears any stale selection
        // itself); preserve it. Clear only when control rests on an AI (the
        // paced hand-off auto-selects later), the game ended, or auto-select
        // is off (then nothing set the selection, so fall back to the
        // classic clear). The per-turn visited reset moved to StartPlayerTurn
        // so an auto-selection's visited mark survives into the turn.
        if (_state.Turns.CurrentPlayer.IsAi || _session.IsGameOver
            || _ops.VikingPhaseActive || _ops.VikingTurnPending
            || !_autoSelectFirstTerritory)
            SetSelection(null);
        _ops.RefreshViews();
    }

    /// <summary>
    /// Win Now button on the claim-victory overlay: declare the prompted
    /// human as the winner immediately, fire <see cref="GameEnded"/>, and
    /// record the color so the prompt won't re-appear (defensive — the
    /// game is over anyway).
    /// </summary>
    private void OnClaimVictoryWinNowPressed()
    {
        if (_recorder.IsReplaying) return;
        if (_ops.InSilentAiBatch()) return;
        if (_automating) StopAutomation("input");
        if (!_session.PendingClaimVictory.HasValue) return;
        (PlayerId color, int threshold) = _session.PendingClaimVictory.Value;
        if (_humanActionValidator != null && !_humanActionValidator(
            new ReplayClaimVictoryBeat { ThresholdPercent = threshold }))
        {
            return;
        }
        _hud.DismissCapitalAlertNotice();
        _recorder.RecordBeat(new ReplayClaimVictoryBeat { ThresholdPercent = threshold });
        _session.PendingClaimVictory = null;
        _session.ClaimVictoryPromptedHighestThreshold[color] = threshold;
        _ops.DeclareWinner(color);
        ClearUndoAndReplayBookkeeping();
        _ops.CheckGameEndConditions();
        _ops.RefreshViews();
    }

    /// <summary>
    /// Continue Playing button on the claim-victory overlay: record the
    /// dismissed tier (so this tier won't re-fire, but a higher tier
    /// can later) and run the deferred End Turn body.
    /// </summary>
    private void OnClaimVictoryContinuePressed()
    {
        if (_recorder.IsReplaying) return;
        if (_ops.InSilentAiBatch()) return;
        if (_automating) StopAutomation("input");
        if (!_session.PendingClaimVictory.HasValue) return;
        (PlayerId color, int threshold) = _session.PendingClaimVictory.Value;
        if (_humanActionValidator != null && !_humanActionValidator(
            new ReplayDismissClaimBeat { ThresholdPercent = threshold }))
        {
            return;
        }
        _hud.DismissCapitalAlertNotice();
        _recorder.RecordBeat(new ReplayDismissClaimBeat { ThresholdPercent = threshold });
        _session.PendingClaimVictory = null;
        _session.ClaimVictoryPromptedHighestThreshold[color] = threshold;
        EndTurnNow();
    }

    /// <summary>
    /// Re-kick the paced AI run after an external replay pause clears
    /// (the Tutorial-Preview narration beat was tapped away). Thin
    /// forwarder over <see cref="AiTurnDriver.ResumeAfterReplayPause"/>
    /// so the Preview pane keeps a single controller entry point.
    /// </summary>
    public void ResumeAiTurnsAfterReplayPause() =>
        _aiDriver.ResumeAfterReplayPause();

    // --- Replay playback ------------------------------------------------
    // Mirrors the AI step machine: preview (highlight acting territory)
    // → pace → execute → pace → loop. Dispatches by record type, but
    // every concrete execute path calls into the same ExecuteAi* helpers
    // the live game uses — so replay fidelity comes "for free" from
    // converging on the live mutation paths.

    /// <summary>
    /// Rewind the game to <see cref="InitialReplaySnapshot"/> and begin
    /// paced playback of <see cref="ReplayBeats"/>. While playing,
    /// <see cref="IsReplayMode"/> is true: every input handler
    /// early-returns and the <c>HumanTurnStarted</c> autosave hook is
    /// suppressed. The view's victory overlay is hidden (Winner is
    /// reset) until the recorded game-ending beat re-fires it.
    /// </summary>
    /// <summary>
    /// Rewind and play back the recorded beat log. Forwards to
    /// <see cref="ReplayRecorder.BeginReplay"/>; the undo-stack clear
    /// is composite (session undo + beat-stack bookkeeping) so it
    /// happens here, not inside the recorder.
    /// </summary>
    public void BeginReplay()
    {
        if (!_recorder.HasInitialSnapshot) return;
        // Composite clear (session + beat stacks) before the recorder
        // resets its own state — keeps the three stacks in lockstep.
        ClearUndoAndReplayBookkeeping();
        _recorder.BeginReplay();
    }

}
