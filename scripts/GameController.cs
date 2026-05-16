using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

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
    private readonly SessionState _session;
    private readonly IHexMapView _map;
    private readonly IHudView _hud;

    // The save/load contract requires deterministic-on-reload AI: a saved
    // master seed plus the (turn, player) tuple uniquely determines the
    // RNG sequence used during that player's turn. The per-turn reseed
    // happens at the top of StartPlayerTurn — it lets a save capture
    // just the seed (no RNG-consumption count) and still replay
    // identically on load.
    private readonly int _masterSeed;
    private Random _rng;
    public int MasterSeed => _masterSeed;

    private readonly Func<GameState, Color, HashSet<HexCoord>, Random, AiAction?> _aiChooser;
    private readonly IAiPacer _aiPacer;

    // Per-AI-turn scratch state for the step machine. Persists across
    // paced StepAi invocations and resets whenever control advances
    // to a new player.
    private readonly HashSet<HexCoord> _aiVisited = new();
    private int _aiStepsThisPlayer;

    // The action chosen during the "preview" beat and carried into
    // the "execute" beat that follows. Lets us highlight the acting
    // territory first, pause, then actually run the action.
    private AiAction? _pendingAiAction;

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

    // Safety cap on AI actions per player turn — the visited set
    // guarantees termination in practice, but this keeps a buggy
    // chooser from pacing forever.
    private const int MaxAiStepsPerPlayer = 64;

    // Hard cap on TurnState.TurnNumber. Default is unlimited; the
    // diagnostic launch path in Main sets a smaller value so
    // stasis runs terminate instead of looping forever.
    private readonly int _maxTurnNumber;
    private bool _gameEndedFired;

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
    /// <see cref="RefreshViews"/>. Save/load wires the autosave path to
    /// this event — the saved state matches what the player sees.
    /// Never fires for AI turns.
    /// </summary>
    public event Action? HumanTurnStarted;

    private readonly Func<bool> _aiSilentMode;
    private readonly IAiBackgroundRunner _aiBackgroundRunner;

    /// <summary>
    /// Tell the view to enter (or leave) silent mode based on whether
    /// the player about to act is AI and whether the user opted into
    /// Instant AI Speed. Called from every player-transition entry point
    /// — <see cref="Resume"/> for game start / load / replay seed, and
    /// <see cref="StartPlayerTurn"/> for the live AI→AI and AI→human
    /// hand-offs — so the flag tracks the active actor without leaking
    /// any UserSettings dependency into pure controller code.
    /// Also drives the "Opponents are taking their turns…" HUD overlay
    /// so the human knows their input is intentionally inert while the
    /// background chooser runs (the alternative is a silent freeze).
    /// </summary>
    private void RefreshSilentMode()
    {
        bool silent = InSilentAiBatch();
        _map.SetSilentMode(silent);
        // Tutorial Preview / Record use the tutorial-message slot for
        // their own scripted text; don't clobber it. Outside those
        // modes the slot is free, so reuse it as a passive "AI is
        // working" indicator.
        if (_previewMode || _recordingMode) return;
        if (silent && !_silentBatchOverlayShown)
        {
            _hud.ShowTutorialMessage("Opponents are taking their turns…");
            _silentBatchOverlayShown = true;
        }
        else if (!silent && _silentBatchOverlayShown)
        {
            _hud.HideTutorialMessage();
            _silentBatchOverlayShown = false;
        }
    }

    private bool _silentBatchOverlayShown;

    /// <summary>
    /// True while an AI player is acting under the Instant speed setting.
    /// The AI step machine consults this to skip per-beat highlight and
    /// view-refresh calls — they'd never reach the screen anyway (the
    /// SynchronousAiPacer drains the entire batch in one frame) and
    /// running them blocks the main thread for hundreds of milliseconds
    /// per AI player on a six-AI map. The final <c>RefreshViews</c> when
    /// control returns to a human (i.e. when this returns false again)
    /// is what the human actually sees.
    ///
    /// Returns false while <see cref="SessionState.PendingDefeatScreen"/>
    /// is set: the AI batch is paused waiting for the human to dismiss
    /// the overlay, so input gates should lift, the view should accept
    /// feedback, and the defeat HUD should render. The batch resumes
    /// when <see cref="OnDefeatContinuePressed"/> clears the flag and
    /// calls <see cref="RefreshSilentMode"/>.
    /// </summary>
    private bool InSilentAiBatch() =>
        _aiSilentMode()
        && _state.Turns.CurrentPlayer.IsAi
        && !_session.PendingDefeatScreen.HasValue;

    public GameController(
        GameState state,
        SessionState session,
        IHexMapView map,
        IHudView hud,
        int? seed = null,
        Func<GameState, Color, HashSet<HexCoord>, Random, AiAction?>? aiChooser = null,
        IAiPacer? aiPacer = null,
        int maxTurnNumber = int.MaxValue,
        Replay? loadedReplay = null,
        Func<ReplayBeat, bool>? humanActionValidator = null,
        bool previewMode = false,
        bool recordingMode = false,
        Action? onAfterRefresh = null,
        Func<bool>? aiSilentMode = null,
        IAiBackgroundRunner? aiBackgroundRunner = null)
    {
        _aiSilentMode = aiSilentMode ?? (() => false);
        _aiBackgroundRunner = aiBackgroundRunner ?? new SynchronousAiBackgroundRunner();
        _humanActionValidator = humanActionValidator;
        _previewMode = previewMode;
        _recordingMode = recordingMode;
        _onAfterRefresh = onAfterRefresh;
        _state = state;
        _session = session;
        _map = map;
        _hud = hud;
        _masterSeed = seed ?? Random.Shared.Next();
        // Initial _rng is set from the seed alone; StartPlayerTurn
        // replaces it with a per-turn reseed before any gameplay RNG
        // consumption begins. The non-null assignment here keeps the
        // field non-nullable and prevents a NRE if anything reads
        // _rng before the first StartPlayerTurn (currently nothing
        // does, but the contract should be safe).
        _rng = new Random(_masterSeed);
        _aiChooser = aiChooser ?? RandomAi.ChooseNextAction;
        _aiPacer = aiPacer ?? new SynchronousAiPacer();
        _maxTurnNumber = maxTurnNumber;

        if (loadedReplay != null)
        {
            _initialSnapshot = loadedReplay.InitialSnapshot;
            _initialTurnNumber = loadedReplay.InitialTurnNumber;
            _initialCurrentPlayerIndex = loadedReplay.InitialCurrentPlayerIndex;
            _replayBeats.AddRange(loadedReplay.Beats);
            _replayDataIsCompleteFromStart = true;
        }

        _map.TileClicked += OnTileClicked;
        _map.TileLongClicked += OnTileLongClicked;
        _map.OffGridClicked += OnOffGridClicked;
        _hud.BuyPeasantClicked += OnBuyPressed;
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
        _hud.DefeatContinueClicked += OnDefeatContinuePressed;
        _hud.ClaimVictoryWinNowClicked += OnClaimVictoryWinNowPressed;
        _hud.ClaimVictoryContinueClicked += OnClaimVictoryContinuePressed;

        // Tutorial Preview / Record use the bottom-center tutorial
        // message panel for game-over signaling; the click-blocking
        // victory modal would otherwise freeze further author input.
        _hud.SetVictoryOverlaySuppressed(_previewMode || _recordingMode);
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
    /// can't fire <c>StepAiExecute</c> after the scene's tile
    /// polygons have been disposed (which would throw
    /// <c>ObjectDisposedException</c> from the <c>HexTile.Color</c>
    /// setter mid-capture).
    /// </summary>
    public void AbandonGame()
    {
        _aiPacer.Cancel();
        // Drop any chooser worker in flight under silent batch — its
        // CallDeferred-marshaled onMain would otherwise wake against a
        // controller whose views have been disposed.
        _aiBackgroundRunner.Cancel();
        // Unsubscribe from view events so a downstream click can't
        // re-enter this stale controller's handlers — relevant when
        // the view is shared between sessions (TutorialBuilder's
        // Record ↔ Preview transitions reuse the same HexMapView).
        // Without this, the stale handler's RefreshViews call hits
        // a disposed HudView and throws ObjectDisposedException.
        _map.TileClicked -= OnTileClicked;
        _map.TileLongClicked -= OnTileLongClicked;
        _map.OffGridClicked -= OnOffGridClicked;
        _hud.BuyPeasantClicked -= OnBuyPressed;
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
        _hud.DefeatContinueClicked -= OnDefeatContinuePressed;
        _hud.ClaimVictoryWinNowClicked -= OnClaimVictoryWinNowPressed;
        _hud.ClaimVictoryContinueClicked -= OnClaimVictoryContinuePressed;
    }

    public void StartGame()
    {
        SeedStartingGold();
        // Replay-recording anchor: capture starting state after seeding
        // gold so replay's restore matches the seed the live game saw on
        // round 1. Skipped when the controller was constructed with a
        // loaded replay (the snapshot is already populated from disk).
        if (_initialSnapshot == null)
        {
            _initialSnapshot = GameStateSnapshot.Capture(
                _state.Grid, _state.Treasury, _state.Territories);
            _initialTurnNumber = _state.Turns.TurnNumber;
            _initialCurrentPlayerIndex = _state.Turns.CurrentPlayerIndex;
            _replayDataIsCompleteFromStart = true;
        }
        // No start-of-game income collection: the seed already equals
        // 5 × tree-free cells per territory, which is exactly what each
        // player sees on their first turn. Subsequent turns credit
        // income at the END of the turn (see OnEndTurnPressed and the
        // AI turn-end path).
        Resume();
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
    /// </summary>
    public void Resume()
    {
        // Resume reached without an initial snapshot means we loaded a
        // pre-replay save (v3). Capture at load time so future replays
        // of *this* game from save-then-load have something to anchor
        // on, but leave _replayDataIsCompleteFromStart false: the UI
        // disables the Replay button for this game because we have no
        // history before the load point.
        if (_initialSnapshot == null)
        {
            _initialSnapshot = GameStateSnapshot.Capture(
                _state.Grid, _state.Treasury, _state.Territories);
            _initialTurnNumber = _state.Turns.TurnNumber;
            _initialCurrentPlayerIndex = _state.Turns.CurrentPlayerIndex;
        }
        ReseedRngForCurrentTurn();
        RefreshSilentMode();
        RunAiTurnsUntilHumanOrDone();
        RefreshViews();
        // Initial player is human → StartPlayerTurn is never called
        // for them (it only runs at transitions), so fire the autosave
        // hook here. If a human turn was reached via AI hand-off
        // inside RunAiTurnsUntilHumanOrDone, the event already fired
        // from inside StartPlayerTurn.
        MaybeFireHumanTurnStartedFromStartGame();
    }

    // Tracks whether HumanTurnStarted has fired for the current player's
    // turn so StartGame doesn't double-fire when the AI hand-off path
    // already raised it from inside StartPlayerTurn.
    private bool _humanTurnFiredForCurrentTurn;

    private void MaybeFireHumanTurnStartedFromStartGame()
    {
        if (_humanTurnFiredForCurrentTurn) return;
        if (_session.IsGameOver || _gameEndedFired) return;
        if (_state.Turns.CurrentPlayer.IsAi) return;
        _humanTurnFiredForCurrentTurn = true;
        HumanTurnStarted?.Invoke();
    }

    /// <summary>
    /// Reset <see cref="_rng"/> to a fresh <see cref="Random"/> derived
    /// solely from <see cref="_masterSeed"/> and the current
    /// (turn, player) pair. This is the per-turn reseed that makes
    /// save/load deterministic: a save records only the master seed,
    /// and load reproduces identical RNG sequences regardless of how
    /// many random numbers the prior turns consumed.
    /// </summary>
    private void ReseedRngForCurrentTurn()
    {
        int subSeed = MixSeed(
            _masterSeed,
            _state.Turns.TurnNumber,
            _state.Turns.CurrentPlayerIndex);
        _rng = new Random(subSeed);
    }

    /// <summary>
    /// Deterministic 32-bit mixer over (masterSeed, turn, player).
    /// XOR-of-small-ints would correlate adjacent (turn, player) pairs;
    /// this uses three rounds of xorshift-multiply (the
    /// "splitmix32" pattern) so adjacent inputs hash to uncorrelated
    /// outputs.
    /// </summary>
    private static int MixSeed(int masterSeed, int turn, int playerIndex)
    {
        unchecked
        {
            uint x = (uint)masterSeed;
            x ^= (uint)turn * 0x9E3779B1u;
            x ^= (uint)playerIndex * 0x85EBCA77u;
            x ^= x >> 16;
            x *= 0x7feb352du;
            x ^= x >> 15;
            x *= 0x846ca68bu;
            x ^= x >> 16;
            return (int)x;
        }
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

    /// <summary>
    /// Reset HasMovedThisTurn on every unit owned by <paramref name="player"/>.
    /// Called at the start of that player's turn.
    /// </summary>
    private static void ResetMovementFor(Player player, HexGrid grid)
    {
        foreach (HexTile tile in grid.Tiles)
        {
            if (tile.Unit != null && tile.Unit.Owner == player.Color)
            {
                tile.Unit.HasMovedThisTurn = false;
            }
        }
    }

    private UndoEntry CaptureCurrentSnapshot() => new UndoEntry(
        GameStateSnapshot.Capture(_state.Grid, _state.Treasury, _state.Territories),
        SessionStateSnapshot.Capture(_session));

    // Set inside Execute* helpers right before the first game-state
    // mutation. Read by TrackHandler at handler exit to decide whether
    // to push an UndoEntry. Replaces the previous inline PushBefore
    // pattern: now the wrapping handler captures and pushes once,
    // covering both game and session state changes in a single entry.
    private bool _handlerMutatedGame;

    // Tutorial Preview hooks. _humanActionValidator (set via the
    // constructor's humanActionValidator param) gates every
    // state-mutating human input: input handlers build the proposed
    // ReplayBeat and call this BEFORE mutating; false → abort.
    // _previewMode (set via the previewMode param) parallels
    // _replayMode for the RecordBeat gate but does NOT block input
    // handlers — Preview wants player-0 clicks to flow through.
    private readonly Func<ReplayBeat, bool>? _humanActionValidator;
    private readonly bool _previewMode;
    public bool IsPreviewMode => _previewMode;

    // _recordingMode (set via the recordingMode param) is true when the
    // controller is hosting the Tutorial Builder's Record session. All
    // six slots are forced Human in that mode so the dev can play every
    // color, which would otherwise fire the defeat overlay (and similar
    // human-only UI) for non-player-0 eliminations even though those
    // slots will be AI in the eventual Preview playback. Used by
    // HandleCapture to gate PendingDefeatScreen to player 0 only.
    private readonly bool _recordingMode;
    public bool IsRecordingMode => _recordingMode;

    // Optional callback fired at the tail of every RefreshViews(). Used
    // by Tutorial Preview to repaint visual cues (auto-selection,
    // single-tile highlights, CTA-styled buttons) after every state
    // change — both human-driven and AI-driven. Null in ordinary play.
    private Action? _onAfterRefresh;

    // --- Replay recording / playback ------------------------------------
    // The replay log lives parallel to the per-turn undo stack: every
    // state-mutating action by every player (human and AI) appends a
    // ReplayBeat here. The list is never cleared by EndTurn or by load;
    // it grows monotonically for the lifetime of the game. BeginReplay
    // restores _initialSnapshot, then steps through the beats via
    // IAiPacer.
    private readonly List<ReplayBeat> _replayBeats = new();
    private GameStateSnapshot? _initialSnapshot;
    private int _initialTurnNumber;
    private int _initialCurrentPlayerIndex;

    // Set inside a human handler body that produced a state mutation;
    // TrackHandler reads it after the handler returns and appends it to
    // _replayBeats iff state actually changed. Mirrors how
    // _handlerMutatedGame is consumed. Cleared back to null at the start
    // of each TrackHandler invocation.
    private ReplayBeat? _pendingHumanBeat;

    private bool _replayMode;
    private int _replayIndex;

    // Parallel bookkeeping for undo/redo: track the beat-list size at
    // the moment each UndoEntry was pushed, so undo can trim
    // _replayBeats back to that size and stash the popped beats for
    // redo to restore. Synchronized externally with _session.Undo via
    // every push/pop site below.
    private readonly System.Collections.Generic.Stack<int> _undoBeatCounts = new();
    private readonly System.Collections.Generic.Stack<System.Collections.Generic.List<ReplayBeat>> _redoBeatLists = new();

    // True iff the recorded beats are sufficient to replay from the
    // game's start. False for v3-save resumes: we capture an initial
    // snapshot at load time so future replays from that game can begin
    // somewhere, but the replay button stays disabled in the current
    // game (loading code that pre-dates the feature has no recorded
    // history before the load point).
    private bool _replayDataIsCompleteFromStart;

    /// <summary>Read-only view of the recorded beat log.</summary>
    public System.Collections.Generic.IReadOnlyList<ReplayBeat> ReplayBeats => _replayBeats;
    /// <summary>The captured game-start snapshot, or null if recording
    /// hasn't started yet.</summary>
    public GameStateSnapshot? InitialReplaySnapshot => _initialSnapshot;
    /// <summary><see cref="TurnState.TurnNumber"/> at recording start.</summary>
    public int InitialReplayTurnNumber => _initialTurnNumber;
    /// <summary><see cref="TurnState.CurrentPlayerIndex"/> at recording start.</summary>
    public int InitialReplayCurrentPlayerIndex => _initialCurrentPlayerIndex;
    /// <summary>Whether <see cref="BeginReplay"/> would produce a
    /// faithful from-start playback. False after loading a save that
    /// pre-dates the replay feature.</summary>
    public bool ReplayDataIsCompleteFromStart => _replayDataIsCompleteFromStart;
    /// <summary>True while <see cref="BeginReplay"/> is driving playback.
    /// Input handlers early-return when this is set; autosave is
    /// suppressed.</summary>
    public bool IsReplayMode => _replayMode;

    /// <summary>
    /// Append a typed beat to the replay log, stamping
    /// <see cref="ReplayBeat.Index"/>, <see cref="ReplayBeat.Turn"/>,
    /// and <see cref="ReplayBeat.Actor"/> from the current turn state.
    /// Called from every state-mutation site; gated on
    /// <see cref="_replayMode"/> by each caller so playback doesn't
    /// re-record the beats it's replaying.
    /// </summary>
    private void RecordBeat(ReplayBeat beat)
    {
        // Preview mode also suppresses recording — the script lives
        // in ReplayDrivenAi + TutorialPreview, not in _replayBeats.
        // Leaving this guard inside the method (rather than at every
        // call site) means new RecordBeat callers don't need to
        // remember the gate.
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
    /// Per-event-handler push policy. Captures pre-handler state, runs
    /// <paramref name="work"/>, and pushes that pre-state onto the undo
    /// stack iff the handler actually changed something — either game
    /// state (signaled by <see cref="_handlerMutatedGame"/>) or session
    /// state (selection / mode / move source). De-dup is automatic: a
    /// no-op handler (e.g. Buy Peasant when already in BuyingPeasant
    /// and only peasant is affordable) leaves both signals false and
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
        if (_replayMode) return;
        // Drop the input outright while an AI player is acting under
        // Instant — the main thread is free between worker yields, so
        // a tile click or Tab press could otherwise race the chooser
        // and mutate SessionState behind it. The gate also covers the
        // mid-mutation window inside the synchronous trampoline (a
        // belt-and-braces guarantee: even if Godot's input ordering
        // ever changed, the controller's invariants are protected).
        if (InSilentAiBatch()) return;
        int beatsBefore = _replayBeats.Count;
        UndoEntry pre = CaptureCurrentSnapshot();
        _handlerMutatedGame = false;
        _pendingHumanBeat = null;
        work();
        // If the handler triggered a game-over (e.g., a winning capture
        // calls Undo.Clear()), don't push — there's nothing to undo past
        // game-end, and the pre-state would otherwise resurrect the
        // just-cleared stack. But still record the beat: the action that
        // ended the game must appear in the replay log.
        if (_session.IsGameOver)
        {
            if (_pendingHumanBeat != null) RecordBeat(_pendingHumanBeat);
            return;
        }
        SessionStateSnapshot postSession = SessionStateSnapshot.Capture(_session);
        bool sessionChanged = !pre.Session.Equals(postSession);
        if (_handlerMutatedGame || sessionChanged)
        {
            _session.Undo.PushBefore(pre);
            // Parallel bookkeeping: this entry corresponds to the
            // beat-list at size beatsBefore. New action invalidates
            // forward history on the replay side too.
            _undoBeatCounts.Push(beatsBefore);
            _redoBeatLists.Clear();
        }
        if (_pendingHumanBeat != null) RecordBeat(_pendingHumanBeat);
        // Tutorial Preview cue update: handler bodies sometimes paint
        // ShowMoveTargets / ShowTowerTargets AFTER their mid-body
        // RefreshViews call (e.g., OnTileClickedBody enters MovingUnit
        // mode and paints all valid targets AFTER SetSelection
        // refreshed). Re-fire onAfterRefresh at the tail so the cue
        // paints last and wins over the body's full-target sets.
        // Re-entrancy in TutorialPreviewCues.Apply is guarded
        // separately, so the extra invocation is safe.
        _onAfterRefresh?.Invoke();
    }

    // --- Click handling ---------------------------------------------------

    private void OnTileClicked(HexTile? tile) =>
        TrackHandler(() => OnTileClickedBody(tile));

    private void OnOffGridClicked(HexCoord coord) =>
        TrackHandler(() => OnOffGridClickedBody(coord));

    /// <summary>
    /// Handle a click whose coord is outside the land grid (water, etc.).
    /// In a pending placement mode (buy/move/tower) it's a rejected click
    /// just like a far in-grid click: flash + sound, stay in mode, keep
    /// selection. Outside of placement mode the click clears selection —
    /// preserves the long-standing "click off-grid to deselect" UX.
    /// </summary>
    private void OnOffGridClickedBody(HexCoord coord)
    {
        if (_session.IsGameOver) return;

        UnitLevel? buyLevel = SessionState.BuyModeLevel(_session.Mode);
        if (buyLevel.HasValue && _session.SelectedTerritory != null)
        {
            EmitRejection(buyLevel.Value, coord);
            return;
        }
        if (_session.Mode == SessionState.ActionMode.BuildingTower && _session.SelectedTerritory != null)
        {
            _map.FlashRejection(coord, RejectionShape.Tower, System.Array.Empty<HexCoord>());
            return;
        }
        if (_session.Mode == SessionState.ActionMode.MovingUnit && _session.MoveSource.HasValue)
        {
            Unit? sourceUnit = _state.Grid.Get(_session.MoveSource.Value)?.Unit;
            if (sourceUnit != null)
            {
                EmitRejection(sourceUnit.Level, coord);
            }
            return;
        }

        SetSelection(null);
    }

    private void OnTileClickedBody(HexTile? tile)
    {
        if (_session.IsGameOver) return;

        // Handle any pending action mode first.
        UnitLevel? buyLevel = SessionState.BuyModeLevel(_session.Mode);
        if (buyLevel.HasValue && tile != null && _session.SelectedTerritory != null)
        {
            if (IsValidTarget(buyLevel.Value, tile.Coord))
            {
                ExecuteBuyAndPlace(buyLevel.Value, tile.Coord);
                return;
            }
            // Stay in buy mode so the player can re-aim without
            // re-clicking the buy button. Feedback is the only response.
            EmitRejection(buyLevel.Value, tile.Coord);
            return;
        }
        else if (_session.Mode == SessionState.ActionMode.BuildingTower && tile != null && _session.SelectedTerritory != null)
        {
            if (IsValidTowerTarget(tile.Coord))
            {
                ExecuteBuildTower(tile.Coord);
                return;
            }
            System.Console.WriteLine(
                $"[BuildTower] click at {tile.Coord} rejected: {DescribeInvalidTowerReason(tile.Coord)}");
            _map.FlashRejection(tile.Coord, RejectionShape.Tower, System.Array.Empty<HexCoord>());
            return;
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
            return;
        }

        // Normal click handling.
        if (tile == null)
        {
            SetSelection(null);
            return;
        }

        Territory? territory = TerritoryLookup.FindContaining(_state.Territories, tile.Coord);
        if (territory == null || territory.Owner != _state.Turns.CurrentPlayer.Color)
        {
            SetSelection(null);
            return;
        }

        // Select the territory; if the clicked tile has one of our own
        // unused units, also pick it up for movement.
        SetSelection(territory);

        if (tile.Unit != null
            && tile.Unit.Owner == _state.Turns.CurrentPlayer.Color
            && !tile.Unit.HasMovedThisTurn)
        {
            _session.Mode = SessionState.ActionMode.MovingUnit;
            _session.MoveSource = tile.Coord;
            _map.ShowMoveTargets(ActionConsumingTargets(tile.Unit.Level, territory), tile.Unit.Level);
            _map.ShowMoveSource(tile.Coord);
            // Re-refresh after entering MovingUnit so HudView's cached
            // _hasPendingAction sees the new mode — otherwise Escape
            // routes to the pause menu instead of cancelling the move.
            RefreshViews();
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
        if (_session.Mode != SessionState.ActionMode.None) return;

        Color currentColor = _state.Turns.CurrentPlayer.Color;
        if (tile.Color != currentColor) return;

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
            _map.PlaySound(SoundEffect.Rally);
            SetSelection(territory);
        }
        RefreshViews();
    }

    /// <summary>
    /// Update <see cref="SessionState.SelectedTerritory"/>, redraw the
    /// view's highlight outline, and refresh the HUD.
    /// </summary>
    private void SetSelection(Territory? territory)
    {
        _session.SelectedTerritory = territory;
        ShowHighlightAndRefresh(territory);
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
        RefreshViews();
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
    public void RefreshViewsForTutorial() => RefreshViews();

    /// <summary>
    /// Append an authored tutorial-only beat to the recording log. Used
    /// by RecordPane's "+ Text" authoring path. Stamps Index + Turn
    /// from current state and forces <see cref="ReplayBeat.Actor"/> =
    /// -1 (no player owns these). Gated on recording mode: silently
    /// no-ops outside of an active recording (replay playback, preview
    /// mode, or before StartGame). Crashes if a non-<see cref="TutorialOnlyBeat"/>
    /// subclass is passed — only tutorial-only beats are authorable.
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
        Color currentColor = _state.Turns.CurrentPlayer.Color;

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
            return $"tile not in selected territory (tile color={tile.Color}, sel owner={sel.Owner})";
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
            RefreshViews();
            return;
        }

        _handlerMutatedGame = true;
        _pendingHumanBeat = new ReplayBuyBeat
        {
            Capital = capital,
            To = destination,
            Level = level,
        };
        _state.Treasury.SetGold(capital, _state.Treasury.GetGold(capital) - PurchaseRules.CostFor(level));
        var unit = new Unit(_session.SelectedTerritory.Owner, level);
        // Detect combine before the rule mutates the destination — a
        // friendly Unit at the dst tile means MovementRules will merge
        // them, and we want to fire the level-up chime instead of the
        // place thud.
        bool wasCombine = WasFriendlyUnitAt(destination, _session.SelectedTerritory.Owner);
        MoveResult result = MovementRules.PlaceNew(unit, destination, _state.Grid, _session.SelectedTerritory);

        if (result.WasCapture)
        {
            HandleCapture($"Buy {level} → {destination}");
            RebindSelectionToContaining(destination);
        }

        // Dispatch destruction FX after HandleCapture: that path's
        // RebuildAfterTerritoryChange clears the deaths layer to cancel
        // stale corpse animations, which would also wipe a freshly-
        // spawned capture burst if we played it before.
        if (result.Destroyed != null)
        {
            _map.PlayDestructionEffect(destination, result.Destroyed);
        }

        DispatchActionSound(destination, result, wasCombine);

        // QoL: stay in a buy mode for the highest level the (possibly
        // rebound) territory can still afford that is at most the level
        // just bought. Stay-at-same-level if still affordable; otherwise
        // degrade downward through Knight → Spearman → Peasant. If no
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
            RefreshViews();
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
        for (int i = (int)ceiling; i >= (int)UnitLevel.Peasant; i--)
        {
            UnitLevel candidate = (UnitLevel)i;
            if (PurchaseRules.CanAfford(territory, _state.Treasury, candidate))
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
            RefreshViews();
            return;
        }

        _handlerMutatedGame = true;
        _pendingHumanBeat = new ReplayMoveBeat { From = source, To = destination };

        bool wasCombine = WasFriendlyUnitAt(destination, _session.SelectedTerritory.Owner);
        MoveResult result = MovementRules.Move(source, destination, _state.Grid, _session.SelectedTerritory);

        if (result.WasCapture)
        {
            HandleCapture($"Move {source}→{destination}");
            RebindSelectionToContaining(destination);
        }

        if (result.Destroyed != null)
        {
            _map.PlayDestructionEffect(destination, result.Destroyed);
        }

        DispatchActionSound(destination, result, wasCombine);

        FinishPendingAction();
    }

    /// <summary>
    /// True iff <paramref name="coord"/>'s tile is colored
    /// <paramref name="owner"/> AND occupied by a Unit. The destination
    /// state right before a Move/PlaceNew that triggers MovementRules'
    /// combine branch — pulled out so all four Execute paths share the
    /// same predicate.
    /// </summary>
    private bool WasFriendlyUnitAt(HexCoord coord, Color owner)
    {
        HexTile? tile = _state.Grid.Get(coord);
        return tile != null && tile.Color == owner && tile.Occupant is Unit;
    }

    /// <summary>
    /// Decide and fire the single audio cue for a just-resolved
    /// Move/PlaceNew. Priority: combine > destruction (by destroyed
    /// occupant type) > generic place (only if the move was consumed).
    /// Reposition onto own-empty stays silent.
    /// </summary>
    private void DispatchActionSound(HexCoord destination, MoveResult result, bool wasCombine)
    {
        if (wasCombine)
        {
            _map.PlaySound(SoundEffect.UnitCombined, destination);
            return;
        }
        switch (result.Destroyed)
        {
            case Unit:
                _map.PlaySound(SoundEffect.UnitDestroyed, destination);
                return;
            case Tower:
                _map.PlaySound(SoundEffect.TowerDestroyed, destination);
                return;
            case Tree:
            case Grave:
                _map.PlaySound(SoundEffect.TreeCleared, destination);
                return;
            case Capital:
                _map.PlaySound(SoundEffect.CapitalDestroyed, destination);
                return;
        }
        if (_state.Grid.Get(destination)?.Unit?.HasMovedThisTurn == true)
        {
            _map.PlaySound(SoundEffect.UnitPlaced, destination);
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
            RefreshViews();
            return;
        }

        _handlerMutatedGame = true;
        _pendingHumanBeat = new ReplayBuildTowerBeat
        {
            Capital = capital,
            To = destination,
        };
        _state.Treasury.SetGold(
            capital, _state.Treasury.GetGold(capital) - PurchaseRules.TowerCost);

        HexTile dst = _state.Grid.Get(destination)!;
        dst.Occupant = new Tower();
        _map.PlaySound(SoundEffect.TowerPlaced, destination);

        // QoL: stay in BuildingTower mode if the territory can still
        // afford another tower. Refresh both the tower-target preview
        // and the coverage tint — the just-placed tower expands the
        // covered set and removes its own tile from the legal set.
        if (PurchaseRules.CanAffordTower(_session.SelectedTerritory, _state.Treasury))
        {
            _session.Mode = SessionState.ActionMode.BuildingTower;
            _session.MoveSource = null;
            _map.ShowMoveTargets(System.Array.Empty<HexCoord>(), UnitLevel.Peasant);
            _map.ShowTowerTargets(ValidTowerTargets(_session.SelectedTerritory));
            _map.ShowTowerCoverage(TowerCoverageCoords(_session.SelectedTerritory));
            _map.ShowMoveSource(null);
            RefreshViews();
        }
        else
        {
            FinishPendingAction();
        }
    }

    private void HandleCapture(string actionDesc)
    {
        IReadOnlyList<Territory> previous = _state.Territories;
        Dictionary<HexCoord, (Color Owner, int Gold)> oldCaps = SnapshotCapitals(previous);
        HashSet<Color> colorsWithCapitalBefore = ColorsWithCapital(previous);

        _state.Territories = TerritoryFinder.Recompute(_state.Grid, previous, _state.Treasury);

        Dictionary<HexCoord, (Color Owner, int Gold)> newCaps = SnapshotCapitals(_state.Territories);
        LogCaptureDiff(actionDesc, oldCaps, newCaps);

        // A player whose set of capital-bearing territories drops to
        // empty is freshly defeated by this capture. At most one color
        // can transition per capture (a single move/place captures one
        // tile from one color).
        HashSet<Color> colorsWithCapitalAfter = ColorsWithCapital(_state.Territories);
        foreach (Color c in colorsWithCapitalBefore)
        {
            if (!colorsWithCapitalAfter.Contains(c))
            {
                _map.PlaySound(SoundEffect.PlayerDefeated);
                int defeatedIndex = -1;
                for (int i = 0; i < _state.Turns.Players.Count; i++)
                {
                    if (_state.Turns.Players[i].Color == c) { defeatedIndex = i; break; }
                }
                if (defeatedIndex >= 0
                    && !_state.Turns.Players[defeatedIndex].IsAi
                    && (!_recordingMode || defeatedIndex == 0))
                {
                    _session.PendingDefeatScreen = c;
                }
            }
        }

        _map.RebuildAfterTerritoryChange();

        // Mid-turn win check: only ends the game if the current
        // player owns every cell. The "opponent reduced to orphan
        // singletons" win path is handled at end-of-turn instead
        // (see EndOfTurnProcessing). Undo is cleared so the player
        // can't rewind past the killing blow.
        Color? winner = WinConditionRules.WinnerByDomination(_state.Grid);
        if (winner.HasValue)
        {
            Player? winP = _state.Turns.Players
                .FirstOrDefault(p => p.Color == winner.Value);
            System.Console.WriteLine($"[T{_state.Turns.TurnNumber}] " +
                $"post-capture domination winner: {winP?.Name ?? "?"}");
            DeclareWinner(winner.Value);
            ClearUndoAndReplayBookkeeping();
            // Fire GameEnded for the mid-turn capture-win path. The
            // End-Turn and claim-victory paths call CheckGameEndConditions
            // themselves; without this, TrackHandler sees IsGameOver and
            // early-returns, leaving GameEnded never raised — so Main
            // never enables the victory-overlay Replay button. The
            // _gameEndedFired guard inside CheckGameEndConditions keeps
            // this idempotent if a subsequent caller fires it again.
            CheckGameEndConditions();
        }
    }

    /// <summary>
    /// Set <see cref="SessionState.Winner"/> and fire the game-won
    /// fanfare if the winner is human. Centralized here because Winner
    /// is set from two places (mid-turn domination capture in
    /// HandleCapture, end-of-turn orphan-singleton check in
    /// EndOfTurnProcessing) and CheckGameEndConditions doesn't run
    /// after every Execute path — the sound has to fire at the
    /// Winner-set point or it'd miss the mid-turn human win.
    /// </summary>
    private void DeclareWinner(Color winnerColor)
    {
        _session.Winner = winnerColor;
        Player? winnerPlayer = _state.Turns.Players
            .FirstOrDefault(p => p.Color == winnerColor);
        if (winnerPlayer != null && !winnerPlayer.IsAi)
        {
            _map.PlaySound(SoundEffect.GameWon);
        }
    }

    private void FinishPendingAction()
    {
        _session.ClearPendingAction();
        _map.ClearAllOverlays();
        // Selection is maintained by the caller: a non-capturing
        // reposition leaves it alone; a capture re-binds it via
        // RebindSelectionToContaining; a tower build leaves it alone.
        RefreshViews();
    }

    private void CancelPendingAction()
    {
        _session.ClearPendingAction();
        _map.ClearAllOverlays();
    }

    private void OnCancelActionPressed() => TrackHandler(OnCancelActionPressedBody);

    private void OnCancelActionPressedBody()
    {
        if (_session.IsGameOver) return;
        CancelPendingAction();
        RefreshViews();
    }

    /// <summary>
    /// Defeat-overlay Continue handler. Clears the overlay flag and
    /// re-arms the AI loop so it picks up where it left off (defeat
    /// fires inside StepAiExecute, which then skipped scheduling the
    /// next preview while the flag was set).
    /// </summary>
    private void OnDefeatContinuePressed()
    {
        if (_replayMode) return;
        if (InSilentAiBatch()) return;
        if (!_session.PendingDefeatScreen.HasValue) return;
        if (_humanActionValidator != null && !_humanActionValidator(new ReplayDismissDefeatBeat()))
        {
            return;
        }
        RecordBeat(new ReplayDismissDefeatBeat());
        _session.PendingDefeatScreen = null;
        // Re-arm silent mode if we were in a silent batch — clearing
        // PendingDefeatScreen makes InSilentAiBatch() flip back to
        // true, so push that change to the view BEFORE the refresh
        // (otherwise tweens for the post-dismiss state would leak).
        RefreshSilentMode();
        RefreshViews();
        if (_session.IsGameOver) return;
        if (_state.Turns.CurrentPlayer.IsAi)
        {
            _aiPacer.Schedule(StepAiPreview, AiActionDelayMs);
        }
    }

    // --- Undo / redo ------------------------------------------------------

    private void OnUndoLastPressed()
    {
        if (_replayMode) return;
        if (InSilentAiBatch()) return;
        if (_session.IsGameOver) return;
        if (!_session.Undo.CanUndo) return;
        HexCoord? before = _session.SelectedTerritory?.Capital;
        PopOneBeatBatchForUndo();
        ApplySnapshot(_session.Undo.UndoLast(CaptureCurrentSnapshot()));
        CenterIfSelectionChanged(before);
    }

    private void OnUndoTurnPressed()
    {
        if (_replayMode) return;
        if (InSilentAiBatch()) return;
        if (_session.IsGameOver) return;
        if (!_session.Undo.CanUndo) return;
        // Inline the UndoAll loop so each pop's beat bookkeeping fires.
        PopOneBeatBatchForUndo();
        UndoEntry restored = _session.Undo.UndoLast(CaptureCurrentSnapshot());
        while (_session.Undo.CanUndo)
        {
            PopOneBeatBatchForUndo();
            restored = _session.Undo.UndoLast(restored);
        }
        ApplySnapshot(restored);
    }

    private void OnRedoLastPressed()
    {
        if (_replayMode) return;
        if (InSilentAiBatch()) return;
        if (_session.IsGameOver) return;
        if (!_session.Undo.CanRedo) return;
        HexCoord? before = _session.SelectedTerritory?.Capital;
        PushOneBeatBatchForRedo();
        ApplySnapshot(_session.Undo.RedoLast(CaptureCurrentSnapshot()));
        CenterIfSelectionChanged(before);
    }

    private void OnRedoAllPressed()
    {
        if (_replayMode) return;
        if (InSilentAiBatch()) return;
        if (_session.IsGameOver) return;
        if (!_session.Undo.CanRedo) return;
        PushOneBeatBatchForRedo();
        UndoEntry restored = _session.Undo.RedoLast(CaptureCurrentSnapshot());
        while (_session.Undo.CanRedo)
        {
            PushOneBeatBatchForRedo();
            restored = _session.Undo.RedoLast(restored);
        }
        ApplySnapshot(restored);
    }

    /// <summary>
    /// Pop one entry from <see cref="_undoBeatCounts"/> and stash the
    /// corresponding tail of <see cref="_replayBeats"/> onto
    /// <see cref="_redoBeatLists"/>. Mirrors a single <see cref="UndoStack{T}.UndoLast"/>
    /// pop on the replay side.
    /// </summary>
    private void PopOneBeatBatchForUndo()
    {
        int targetCount = _undoBeatCounts.Pop();
        var popped = new System.Collections.Generic.List<ReplayBeat>();
        for (int i = targetCount; i < _replayBeats.Count; i++)
        {
            popped.Add(_replayBeats[i]);
        }
        _replayBeats.RemoveRange(targetCount, _replayBeats.Count - targetCount);
        _redoBeatLists.Push(popped);
    }

    /// <summary>
    /// Clear the per-turn undo stack and the parallel beat-tracking
    /// stacks together. The three sites that commit "no more undo"
    /// (end of turn, mid-turn domination, claim-victory win) all need
    /// to drop replay bookkeeping in lockstep — otherwise a subsequent
    /// undo would pop into a phantom beat count.
    /// </summary>
    private void ClearUndoAndReplayBookkeeping()
    {
        _session.Undo.Clear();
        _undoBeatCounts.Clear();
        _redoBeatLists.Clear();
    }

    /// <summary>
    /// Pop one stashed batch from <see cref="_redoBeatLists"/> and
    /// re-append to <see cref="_replayBeats"/>, recording the new
    /// pre-batch count on <see cref="_undoBeatCounts"/>. Mirrors a
    /// single <see cref="UndoStack{T}.RedoLast"/>.
    /// </summary>
    private void PushOneBeatBatchForRedo()
    {
        System.Collections.Generic.List<ReplayBeat> toRestore = _redoBeatLists.Pop();
        _undoBeatCounts.Push(_replayBeats.Count);
        _replayBeats.AddRange(toRestore);
    }

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
        _state.Territories = entry.Game.ApplyTo(_state.Grid, _state.Treasury);
        _map.RebuildAfterTerritoryChange();
        entry.Session.ApplyTo(_session, _state.Territories);
        RestoreOverlaysForCurrentMode();
        RefreshViews();
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
            _map.ShowMoveTargets(System.Array.Empty<HexCoord>(), UnitLevel.Peasant);
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
    /// order (Peasant→Spearman→Knight→Baron→Peasant).
    /// </summary>
    private static readonly UnitLevel[] BuyCycleOrder =
    {
        UnitLevel.Peasant,
        UnitLevel.Spearman,
        UnitLevel.Knight,
        UnitLevel.Baron,
    };

    /// <summary>
    /// Buy-button handler: enters the lowest affordable buy mode, or
    /// cycles to the next affordable level if already in a buy mode.
    /// If the only affordable level is the one already active, the
    /// press is a no-op. Same handler is invoked by the `u` hotkey.
    /// </summary>
    private void OnBuyPressed() => TrackHandler(OnBuyPressedBody);

    private void OnBuyPressedBody()
    {
        if (_session.IsGameOver) return;
        if (_session.SelectedTerritory == null) return;

        UnitLevel? next = NextAffordableBuyLevel();
        if (next == null) return;

        _session.Mode = SessionState.BuyModeFor(next.Value);
        _session.MoveSource = null;
        _map.ShowMoveTargets(ActionConsumingTargets(next.Value, _session.SelectedTerritory), next.Value);
        // Switching into a buy mode from BuildingTower leaves the tower
        // preview + coverage tint stale; clear both so the player only
        // sees relevant CTAs.
        _map.ShowTowerTargets(System.Array.Empty<HexCoord>());
        _map.ShowTowerCoverage(System.Array.Empty<HexCoord>());
        _map.ShowMoveSource(null);
        RefreshViews();
    }

    /// <summary>
    /// Pick the next buy level for the cycle: if not currently in a
    /// buy mode, return the lowest affordable level; if already in a
    /// buy mode, return the next affordable level after it (cyclically),
    /// or null if no other level is affordable. Returns null when
    /// nothing is affordable at all.
    /// </summary>
    private UnitLevel? NextAffordableBuyLevel()
    {
        if (_session.SelectedTerritory == null) return null;
        Territory selected = _session.SelectedTerritory;

        UnitLevel? current = SessionState.BuyModeLevel(_session.Mode);
        int startIndex = 0;
        if (current.HasValue)
        {
            // Start one past the current level so re-pressing advances.
            for (int i = 0; i < BuyCycleOrder.Length; i++)
            {
                if (BuyCycleOrder[i] == current.Value)
                {
                    startIndex = i + 1;
                    break;
                }
            }
        }

        for (int offset = 0; offset < BuyCycleOrder.Length; offset++)
        {
            UnitLevel candidate = BuyCycleOrder[(startIndex + offset) % BuyCycleOrder.Length];
            if (current.HasValue && candidate == current.Value) continue;
            if (PurchaseRules.CanAfford(selected, _state.Treasury, candidate))
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
        if (!PurchaseRules.CanAffordTower(_session.SelectedTerritory, _state.Treasury)) return;

        _session.Mode = SessionState.ActionMode.BuildingTower;
        _session.MoveSource = null;
        // Towers only build on empty own-territory tiles — no enemy
        // capture targets to highlight. The legal-tower preview goes
        // through ShowTowerTargets so the player sees where to click,
        // and ShowTowerCoverage tints already-defended cells so the
        // player can avoid stacking coverage.
        _map.ShowMoveTargets(System.Array.Empty<HexCoord>(), UnitLevel.Peasant);
        _map.ShowTowerTargets(ValidTowerTargets(_session.SelectedTerritory));
        _map.ShowTowerCoverage(TowerCoverageCoords(_session.SelectedTerritory));
        _map.ShowMoveSource(null);
        RefreshViews();
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

        Color color = _state.Turns.CurrentPlayer.Color;
        List<Territory> owned = TerritoryLookup
            .OwnedCapitalBearing(_state.Territories, color)
            .ToList();
        if (owned.Count == 0) return;
        owned.Sort((a, b) => a.Capital!.Value.CompareTo(b.Capital!.Value));

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
        int step = forward ? 1 : -1;
        int startIndex = currentIndex == -1
            ? (forward ? -1 : owned.Count)
            : currentIndex;
        int maxOffset = currentIndex == -1 ? owned.Count : owned.Count - 1;
        for (int offset = 1; offset <= maxOffset; offset++)
        {
            int idx = ((startIndex + step * offset) % owned.Count + owned.Count) % owned.Count;
            if (TerritoryHasAvailableAction(owned[idx]))
            {
                CancelPendingAction();
                SetSelection(owned[idx]);
                _map.CenterOnTerritory(owned[idx]);
                return;
            }
        }
    }

    /// <summary>
    /// Cycle the move-source through the current player's unmoved units
    /// inside <see cref="SessionState.SelectedTerritory"/>. N goes forward
    /// (lex-min first when nothing is picked up); Shift+N goes backward
    /// (lex-max first). Acts exactly like clicking the next unit: enters
    /// MovingUnit mode and re-emits the move-target ring. Does not pan
    /// the camera — the territory is already in view.
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

        Color color = _state.Turns.CurrentPlayer.Color;
        var movable = new List<HexCoord>();
        foreach (HexCoord coord in selected.Coords)
        {
            HexTile? tile = _state.Grid.Get(coord);
            Unit? unit = tile?.Unit;
            if (unit != null && unit.Owner == color && !unit.HasMovedThisTurn)
            {
                movable.Add(coord);
            }
        }
        if (movable.Count == 0) return;
        movable.Sort();

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
        _map.ShowMoveTargets(ActionConsumingTargets(chosen.Level, selected), chosen.Level);
        // Defensive: clear tower overlays in case we're transitioning out
        // of BuildingTower mode.
        _map.ShowTowerTargets(System.Array.Empty<HexCoord>());
        _map.ShowTowerCoverage(System.Array.Empty<HexCoord>());
        _map.ShowMoveSource(target);
        RefreshViews();
    }

    private void OnEndTurnPressed()
    {
        if (_replayMode) return;
        if (InSilentAiBatch()) return;
        if (_session.IsGameOver) return;
        if (_humanActionValidator != null && !_humanActionValidator(new ReplayEndTurnBeat()))
        {
            return;
        }

        // Claim-victory prompt: a human pressing End Turn while crossing
        // a tier in WinConditionRules.ClaimVictoryThresholdsPercent
        // (50/75/90) is offered an early win. Each tier fires at most
        // once per human per game; "show only highest unseen" picks the
        // topmost unseen tier the player meets. AI players are skipped;
        // Tutorial Preview and Record modes also suppress the prompt
        // (it would interrupt the scripted / recording flow with a
        // modal the tutorial author can't pre-record).
        Player current = _state.Turns.CurrentPlayer;
        if (!current.IsAi && !_previewMode && !_recordingMode)
        {
            int seen = _session.ClaimVictoryPromptedHighestThreshold
                .TryGetValue(current.Color, out int s) ? s : 0;
            int? next = WinConditionRules.NextClaimVictoryThreshold(
                current.Color, _state.Grid, seen);
            if (next.HasValue)
            {
                _session.PendingClaimVictory = (current.Color, next.Value);
                RefreshViews();
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
        // Record the end-turn beat with the *ending* player's actor /
        // turn metadata, before clearing the undo stack and advancing.
        // AI implicit end-of-turn (StepAiPreview's null-action branch)
        // records its own EndTurnBeat — the two sites are disjoint.
        if (!_replayMode) RecordBeat(new ReplayEndTurnBeat());

        // Ending the turn commits everything; no further undo.
        ClearUndoAndReplayBookkeeping();

        EndOfTurnProcessing();
        if (_session.IsGameOver)
        {
            // End-of-turn win check fired. Don't advance to a player
            // who shouldn't get a turn — just announce the result.
            CheckGameEndConditions();
        }
        else
        {
            AdvanceToNextActivePlayer();
            StartPlayerTurn();
            RunAiTurnsUntilHumanOrDone();
        }

        CancelPendingAction();
        SetSelection(null);
        RefreshViews();
    }

    /// <summary>
    /// Win Now button on the claim-victory overlay: declare the prompted
    /// human as the winner immediately, fire <see cref="GameEnded"/>, and
    /// record the color so the prompt won't re-appear (defensive — the
    /// game is over anyway).
    /// </summary>
    private void OnClaimVictoryWinNowPressed()
    {
        if (_replayMode) return;
        if (InSilentAiBatch()) return;
        if (!_session.PendingClaimVictory.HasValue) return;
        (Color color, int threshold) = _session.PendingClaimVictory.Value;
        if (_humanActionValidator != null && !_humanActionValidator(
            new ReplayClaimVictoryBeat { ThresholdPercent = threshold }))
        {
            return;
        }
        RecordBeat(new ReplayClaimVictoryBeat { ThresholdPercent = threshold });
        _session.PendingClaimVictory = null;
        _session.ClaimVictoryPromptedHighestThreshold[color] = threshold;
        DeclareWinner(color);
        ClearUndoAndReplayBookkeeping();
        CheckGameEndConditions();
        RefreshViews();
    }

    /// <summary>
    /// Continue Playing button on the claim-victory overlay: record the
    /// dismissed tier (so this tier won't re-fire, but a higher tier
    /// can later) and run the deferred End Turn body.
    /// </summary>
    private void OnClaimVictoryContinuePressed()
    {
        if (_replayMode) return;
        if (InSilentAiBatch()) return;
        if (!_session.PendingClaimVictory.HasValue) return;
        (Color color, int threshold) = _session.PendingClaimVictory.Value;
        if (_humanActionValidator != null && !_humanActionValidator(
            new ReplayDismissClaimBeat { ThresholdPercent = threshold }))
        {
            return;
        }
        RecordBeat(new ReplayDismissClaimBeat { ThresholdPercent = threshold });
        _session.PendingClaimVictory = null;
        _session.ClaimVictoryPromptedHighestThreshold[color] = threshold;
        EndTurnNow();
    }

    /// <summary>
    /// End-of-turn bookkeeping for the now-ending player: just the
    /// end-of-turn win check. The current player wins iff no other
    /// player still owns a capital-bearing territory — orphan
    /// singletons of other colors don't keep the game alive. Income
    /// and tree growth both run at the START of the NEXT player's
    /// turn (see <see cref="StartPlayerTurn"/>).
    /// </summary>
    private void EndOfTurnProcessing()
    {
        LogGameEndDiagnostics(
            $"end-of-turn check for {_state.Turns.CurrentPlayer.Name}");
        Color? winner = WinConditionRules.WinnerAtEndOfTurn(
            _state.Turns.CurrentPlayer.Color, _state.Territories);
        if (winner.HasValue)
        {
            Player? winP = _state.Turns.Players
                .FirstOrDefault(p => p.Color == winner.Value);
            System.Console.WriteLine($"[T{_state.Turns.TurnNumber}] " +
                $"end-of-turn winner declared: {winP?.Name ?? "?"}");
            DeclareWinner(winner.Value);
        }
    }

    /// <summary>
    /// One-line dump of per-player tile count and capital-bearing
    /// territory count, plus context. Always-on diagnostic for
    /// debugging stuck game-end conditions; emit volume is one
    /// line per turn-end + a few extras, so it's safe to leave on
    /// in normal play.
    /// </summary>
    private void LogGameEndDiagnostics(string context)
    {
        var tiles = new Dictionary<Color, int>();
        foreach (HexTile tile in _state.Grid.Tiles)
        {
            tiles.TryGetValue(tile.Color, out int n);
            tiles[tile.Color] = n + 1;
        }

        var caps = new Dictionary<Color, int>();
        foreach (Territory t in _state.Territories)
        {
            if (!t.HasCapital) continue;
            caps.TryGetValue(t.Owner, out int n);
            caps[t.Owner] = n + 1;
        }

        var parts = new List<string>();
        foreach (Player p in _state.Turns.Players)
        {
            tiles.TryGetValue(p.Color, out int t);
            caps.TryGetValue(p.Color, out int c);
            parts.Add($"{p.Name}:{t}t/{c}c");
        }

        System.Console.WriteLine($"[T{_state.Turns.TurnNumber}] {context} — " +
            string.Join(", ", parts));
    }

    /// <summary>
    /// Advance to the next non-eliminated player. A player with no
    /// capital-bearing territory is skipped entirely — they own
    /// nothing they can act on (no income, no purchases, no upkeep,
    /// no AI candidates), so a turn for them would be a phantom turn.
    /// The end-of-turn win check guarantees the current player has a
    /// capital when this is called, so at least one player remains in
    /// the rotation and the loop always terminates.
    /// </summary>
    private void AdvanceToNextActivePlayer()
    {
        _state.Turns.EndTurn();
        while (WinConditionRules.IsEliminated(_state.Turns.CurrentPlayer.Color, _state.Grid))
        {
            // Phantom turn for an eliminated player: they can't take any
            // input or AI action, but their tile-bound state still
            // ticks. Order mirrors StartPlayerTurn — tree growth (graves
            // on their color → trees; empty same-color cells with
            // enough neighbor trees spread), then upkeep (orphan units
            // bankrupt into graves because there's no capital to fund
            // them). Income / view refresh / AI dispatch are skipped:
            // there's nothing for them to act on. Without this, orphan
            // units stranded on the eliminated player's tiles would
            // linger forever on a turn rotation that always skipped
            // them.
            Player ghost = _state.Turns.CurrentPlayer;
            if (_state.Turns.TurnNumber > 1)
            {
                TreeRules.RunStartOfTurnGrowth(
                    _state.Grid, ghost.Color, _state.WaterCoords);
            }
            UpkeepRules.ApplyUpkeepFor(
                ghost, _state.Territories, _state.Grid, _state.Treasury);
            System.Console.WriteLine($"[T{_state.Turns.TurnNumber}] phantom turn for eliminated " +
                $"player {ghost.Name} (tree growth + upkeep)");
            _state.Turns.EndTurn();
        }
    }

    /// <summary>
    /// Start-of-turn bookkeeping for the now-current player. Order:
    ///   1. Tree-growth phase — graves on the player's tiles convert
    ///      to trees, and empty cells of their color with >= 2
    ///      neighboring trees become trees. Skipped during round 1
    ///      (every player's first turn).
    ///   2. Reset unit move flags.
    ///   3. Collect income from the player's territories (excludes
    ///      tree and grave tiles; see
    ///      <see cref="TreeRules.CountIncomeProducingTiles"/>).
    ///      Skipped during round 1 — no money is earned on each
    ///      player's first turn; the seed from
    ///      <see cref="SeedStartingGold"/> is the round-1 bankroll.
    ///   4. Apply upkeep (which may bankrupt territories and turn
    ///      their units into fresh graves; those graves wait until
    ///      this player's NEXT turn to mature).
    /// The income → upkeep ordering matters: it lets a territory's
    /// freshly-credited income subsidize that same turn's upkeep
    /// before bankruptcy is checked.
    /// </summary>
    private void StartPlayerTurn()
    {
        // Reseed first, before any RNG consumption this turn. Tree
        // growth (currently deterministic), AI dispatch, and any future
        // start-of-turn random effects all draw from the per-turn RNG
        // derived here.
        ReseedRngForCurrentTurn();
        _humanTurnFiredForCurrentTurn = false;
        // Toggle the view's silent flag for the player about to act.
        // Done BEFORE PlayBankruptcy below so the per-turn bankruptcy
        // toll respects the policy in HexMapView (currently not silenced).
        RefreshSilentMode();

        if (_state.Turns.TurnNumber > 1)
        {
            TreeRules.RunStartOfTurnGrowth(
                _state.Grid, _state.Turns.CurrentPlayer.Color, _state.WaterCoords);
        }

        ResetMovementFor(_state.Turns.CurrentPlayer, _state.Grid);

        if (_state.Turns.TurnNumber > 1)
        {
            _state.Treasury.CollectIncomeFor(
                _state.Turns.CurrentPlayer, _state.Territories, _state.Grid);
        }

        bool anyBankrupt = UpkeepRules.ApplyUpkeepFor(
            _state.Turns.CurrentPlayer, _state.Territories, _state.Grid, _state.Treasury);
        if (anyBankrupt)
        {
            // One toll per turn-start regardless of how many of the
            // player's territories went bankrupt — see IHexMapView.
            _map.PlaySound(SoundEffect.Bankruptcy);
        }

        LogTurnStart();
        CheckGameEndConditions();

        // Fire the autosave hook for human turns. Skipped for AI
        // (autosave is keyed to human turn-start, not AI). Skipped on
        // game-over (no point saving a finished game). The flag is
        // reset at the top of StartPlayerTurn so each turn re-arms.
        if (!_session.IsGameOver
            && !_gameEndedFired
            && !_state.Turns.CurrentPlayer.IsAi
            && !_humanTurnFiredForCurrentTurn
            && !_replayMode)
        {
            _humanTurnFiredForCurrentTurn = true;
            HumanTurnStarted?.Invoke();
        }
    }

    private void LogTurnStart()
    {
        if (!AiLog.Enabled) return;
        Player p = _state.Turns.CurrentPlayer;
        int tiles = 0;
        int ownedTerritories = 0;
        int totalGold = 0;
        int totalNet = 0;
        foreach (Territory t in _state.Territories)
        {
            if (t.Owner != p.Color) continue;
            ownedTerritories++;
            tiles += t.Coords.Count;
            int income = TreeRules.CountIncomeProducingTiles(t, _state.Grid);
            int upkeep = UpkeepRules.TotalUpkeepFor(t, _state.Grid);
            totalNet += income - upkeep;
            if (t.HasCapital)
            {
                totalGold += _state.Treasury.GetGold(t.Capital!.Value);
            }
        }
        AiLog.Print(
            $"[T{_state.Turns.TurnNumber}] {p.Name} ({p.Kind}) turn begins — " +
            $"{tiles} tiles, {ownedTerritories} territories, " +
            $"{totalNet:+#;-#;0} net income, {totalGold}g total");
    }

    /// <summary>
    /// Check for terminal game conditions — natural game over via
    /// <see cref="SessionState.IsGameOver"/>, or exceeding the
    /// constructor-provided turn cap — and fire the
    /// <see cref="GameEnded"/> event exactly once if either holds.
    /// </summary>
    private void CheckGameEndConditions()
    {
        if (_gameEndedFired) return;

        if (_session.IsGameOver)
        {
            Player? winner = null;
            foreach (Player p in _state.Turns.Players)
            {
                if (p.Color == _session.Winner)
                {
                    winner = p;
                    break;
                }
            }
            System.Console.WriteLine(
                $"[T{_state.Turns.TurnNumber}] GAME OVER — " +
                $"winner: {winner?.Name ?? "(none)"}");
            _gameEndedFired = true;
            GameEnded?.Invoke();
            return;
        }

        if (_state.Turns.TurnNumber > _maxTurnNumber)
        {
            System.Console.WriteLine(
                $"[T{_state.Turns.TurnNumber}] GAME OVER — " +
                $"turn cap {_maxTurnNumber} exceeded (stasis)");
            _gameEndedFired = true;
            GameEnded?.Invoke();
        }
    }

    /// <summary>
    /// If the current player is an AI, begin paced execution of their
    /// turn via the <see cref="IAiPacer"/>. With the default
    /// synchronous pacer the entire AI chain runs inline (existing
    /// behavior and what the unit tests rely on). With the Godot
    /// pacer each step is deferred so the player can see individual
    /// AI actions.
    /// </summary>
    private void RunAiTurnsUntilHumanOrDone()
    {
        if (_gameEndedFired) return;
        if (_session.IsGameOver) return;
        if (!_state.Turns.CurrentPlayer.IsAi) return;

        _aiVisited.Clear();
        _aiStepsThisPlayer = 0;
        _pendingAiAction = null;
        _aiPacer.Schedule(StepAiPreview, AiBetweenPlayersDelayMs);
    }

    /// <summary>
    /// Preview beat: pick the next AI action, highlight the territory
    /// that will perform it, and schedule <see cref="StepAiExecute"/>
    /// to run that action after a short pause. If the chooser has
    /// nothing left, instead transition to the next player.
    /// </summary>
    private void StepAiPreview()
    {
        if (_gameEndedFired) return;
        if (_session.IsGameOver)
        {
            ShowHighlightAndRefresh(null);
            return;
        }
        if (!_state.Turns.CurrentPlayer.IsAi)
        {
            // Control changed out from under a scheduled callback
            // (scene reload, test teardown). Just stop.
            return;
        }

        Color color = _state.Turns.CurrentPlayer.Color;

        // Under Instant the chooser is the dominant per-beat cost —
        // running it on the main thread freezes the renderer for
        // hundreds of ms per AI on a busy six-AI map. Hand it to the
        // background runner so the main thread can paint between
        // beats. The continuation re-checks game state because the
        // controller could have been abandoned mid-await (Main scene
        // swap, or BeginReplay). Input gating during the await window
        // is handled by InSilentAiBatch() checks on every human input
        // handler — see TrackHandler and the OnEndTurnPressed /
        // OnUndoLastPressed / OnDefeatContinuePressed / OnClaimVictory*
        // family.
        if (InSilentAiBatch())
        {
            _aiBackgroundRunner.Run(
                work: () => _aiChooser(_state, color, _aiVisited, _rng),
                onMain: a => StepAiPreviewAfterChoose(a, color));
            return;
        }

        StepAiPreviewAfterChoose(_aiChooser(_state, color, _aiVisited, _rng), color);
    }

    private void StepAiPreviewAfterChoose(AiAction? action, Color color)
    {
        // Defensive re-checks: the game may have ended or the player
        // changed (BeginReplay, AbandonGame mid-await) between the
        // chooser dispatch and this continuation. Mirrors the gates
        // at the top of StepAiPreview.
        if (_gameEndedFired) return;
        if (_session.IsGameOver) return;
        if (!_state.Turns.CurrentPlayer.IsAi) return;
        if (_state.Turns.CurrentPlayer.Color != color) return;

        if (action == null || _aiStepsThisPlayer >= MaxAiStepsPerPlayer)
        {
            if (AiLog.Enabled)
            {
                Player p = _state.Turns.CurrentPlayer;
                string reason = action == null ? "no positive-delta actions" : "step cap reached";
                AiLog.Print(
                    $"[T{_state.Turns.TurnNumber}] {p.Name} ends turn after " +
                    $"{_aiStepsThisPlayer} actions ({reason})");
            }

            // Current AI player is done. Run end-of-turn processing,
            // clear the lingering highlight, advance, and either stop
            // (human next) or schedule the next preview beat. If the
            // end-of-turn win check fires we skip the advance and just
            // announce — there's no next turn to start.
            // Record AI's implicit end-of-turn for the replay log. The
            // beat captures the *ending* AI player's actor/turn.
            if (!_replayMode) RecordBeat(new ReplayEndTurnBeat());
            EndOfTurnProcessing();
            if (_session.IsGameOver)
            {
                CheckGameEndConditions();
            }
            else
            {
                AdvanceToNextActivePlayer();
                StartPlayerTurn();
            }
            _aiVisited.Clear();
            _aiStepsThisPlayer = 0;
            _pendingAiAction = null;
            // Skip the hand-off refresh while still inside a silent
            // batch (next player is also AI) — the next StepAiPreview
            // would rebuild views again immediately. When the new
            // player is human, InSilentAiBatch flips false and this
            // becomes the single end-of-batch refresh the human sees.
            if (!InSilentAiBatch())
            {
                ShowHighlightAndRefresh(null);
            }

            if (_gameEndedFired) return;
            if (_session.IsGameOver) return;
            if (_state.Turns.CurrentPlayer.IsAi)
            {
                _aiPacer.Schedule(StepAiPreview, AiBetweenPlayersDelayMs);
            }
            return;
        }

        _pendingAiAction = action;
        // Per-action preview highlight is cosmetic; skip it (and the
        // attendant view rebuild) when nothing will be drawn before
        // the immediately-following execute beat.
        if (!InSilentAiBatch())
        {
            ShowHighlightAndRefresh(ResolveAiActingTerritory(action));
        }
        _aiPacer.Schedule(StepAiExecute, AiPreviewDelayMs);
    }

    /// <summary>
    /// Execute beat: run the previewed action, re-highlight the
    /// (possibly expanded) resulting territory so the player can see
    /// the outcome, then schedule the next preview beat.
    /// </summary>
    private void StepAiExecute()
    {
        if (_gameEndedFired) return;
        if (_session.IsGameOver)
        {
            ShowHighlightAndRefresh(null);
            return;
        }
        AiAction? action = _pendingAiAction;
        _pendingAiAction = null;
        if (action == null) return; // defensive; shouldn't happen

        _aiStepsThisPlayer++;
        LogAction(action);

        HexCoord resultCoord;
        switch (action)
        {
            case AiMoveAction mv:
                if (!_replayMode)
                {
                    RecordBeat(new ReplayMoveBeat { From = mv.Source, To = mv.Destination });
                }
                ExecuteAiMove(mv.Source, mv.Destination);
                resultCoord = mv.Destination;
                break;
            case AiBuyUnitAction bu:
                if (!_replayMode)
                {
                    RecordBeat(new ReplayBuyBeat
                    {
                        Capital = bu.Capital,
                        To = bu.Destination,
                        Level = bu.Level,
                    });
                }
                ExecuteAiBuyUnit(bu.Capital, bu.Destination, bu.Level);
                resultCoord = bu.Destination;
                break;
            case AiBuildTowerAction bt:
                if (!_replayMode)
                {
                    RecordBeat(new ReplayBuildTowerBeat
                    {
                        Capital = bt.Capital,
                        To = bt.Destination,
                    });
                }
                ExecuteAiBuildTower(bt.Capital, bt.Destination);
                resultCoord = bt.Destination;
                break;
            case AiLongPressRallyAction rl:
                if (!_replayMode)
                {
                    RecordBeat(new ReplayLongPressRallyBeat { Target = rl.Target });
                }
                ApplyLongPressRally(rl.Target);
                resultCoord = rl.Target;
                break;
            case AiClaimVictoryAction cv:
            {
                Color cvColor = _state.Turns.CurrentPlayer.Color;
                if (!_replayMode)
                {
                    RecordBeat(new ReplayClaimVictoryBeat { ThresholdPercent = cv.ThresholdPercent });
                }
                _session.ClaimVictoryPromptedHighestThreshold[cvColor] = cv.ThresholdPercent;
                DeclareWinner(cvColor);
                ClearUndoAndReplayBookkeeping();
                resultCoord = TerritoryLookup
                    .OwnedCapitalBearing(_state.Territories, cvColor)
                    .FirstOrDefault()?.Capital
                    ?? new HexCoord(0, 0);
                break;
            }
            case AiDismissClaimAction dc:
            {
                Color dcColor = _state.Turns.CurrentPlayer.Color;
                if (!_replayMode)
                {
                    RecordBeat(new ReplayDismissClaimBeat { ThresholdPercent = dc.ThresholdPercent });
                }
                _session.ClaimVictoryPromptedHighestThreshold[dcColor] = dc.ThresholdPercent;
                resultCoord = TerritoryLookup
                    .OwnedCapitalBearing(_state.Territories, dcColor)
                    .FirstOrDefault()?.Capital
                    ?? new HexCoord(0, 0);
                break;
            }
            case AiDismissDefeatAction _:
                if (!_replayMode)
                {
                    RecordBeat(new ReplayDismissDefeatBeat());
                }
                _session.PendingDefeatScreen = null;
                resultCoord = new HexCoord(0, 0);
                break;
            default:
                return;
        }

        CheckGameEndConditions();
        if (_gameEndedFired)
        {
            // Domination fired inside the action we just executed
            // (HandleCapture set _session.Winner). The HUD's victory
            // overlay is gated on session.Winner inside RefreshViews,
            // so without this final refresh the dialog never appears
            // and the game looks frozen mid-board.
            ShowHighlightAndRefresh(null);
            return;
        }

        // After a capture the old territory object is stale; find the
        // AI's territory now containing the result coord and
        // re-highlight so the outline matches the post-action board.
        // Skipped during a silent batch — the highlight pulse would
        // never paint and the rebuild is the dominant per-action cost.
        if (!InSilentAiBatch())
        {
            ShowHighlightAndRefresh(TerritoryLookup.FindOwnedContaining(
                _state.Territories, _state.Turns.CurrentPlayer.Color, resultCoord));
        }

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
            // Silent-batch handoff: InSilentAiBatch() now returns
            // false (PendingDefeatScreen unfreezes it) but the view's
            // silent flag is still on from the start of the batch.
            // RefreshSilentMode reconciles, lifting the map gate, and
            // the final RefreshViews paints the defeat overlay so the
            // human can dismiss it via the Continue button.
            RefreshSilentMode();
            RefreshViews();
            return;
        }
        _aiPacer.Schedule(StepAiPreview, AiActionDelayMs);
    }

    private void LogAction(AiAction action)
    {
        if (!AiLog.Enabled) return;
        Player p = _state.Turns.CurrentPlayer;
        string desc = action switch
        {
            AiMoveAction mv => $"Move {mv.Source}→{mv.Destination}",
            AiBuyUnitAction bu => $"Buy {bu.Level}@{bu.Capital} → {bu.Destination}",
            AiBuildTowerAction bt => $"Tower@{bt.Capital} → {bt.Destination}",
            _ => "?",
        };
        AiLog.Print($"[T{_state.Turns.TurnNumber}]   {p.Name}: {desc}");
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
        Color owner = _state.Turns.CurrentPlayer.Color;
        return action switch
        {
            AiMoveAction mv => TerritoryLookup.FindOwnedContaining(_state.Territories, owner, mv.Source),
            AiBuyUnitAction bu => TerritoryLookup.FindOwnedContaining(_state.Territories, owner, bu.Capital),
            AiBuildTowerAction bt => TerritoryLookup.FindOwnedContaining(_state.Territories, owner, bt.Capital),
            _ => null
        };
    }

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
    public void BeginReplay()
    {
        if (_initialSnapshot == null) return;
        _aiPacer.Cancel();
        _replayMode = true;
        _replayIndex = 0;
        _gameEndedFired = false;
        _humanTurnFiredForCurrentTurn = false;

        _state.Territories = _initialSnapshot.ApplyTo(_state.Grid, _state.Treasury);
        _state.Turns.Reset(_initialCurrentPlayerIndex, _initialTurnNumber);
        _session.Winner = null;
        _session.PendingDefeatScreen = null;
        _session.PendingClaimVictory = null;
        _session.ClaimVictoryPromptedHighestThreshold.Clear();
        _session.ClearPendingAction();
        _session.SelectedTerritory = null;
        ClearUndoAndReplayBookkeeping();

        _map.RebuildAfterTerritoryChange();
        _map.ShowHighlight(null);
        _map.ClearAllOverlays();
        RefreshViews();

        _aiPacer.Schedule(StepReplayPreview, AiBetweenPlayersDelayMs);
    }

    private void StepReplayPreview()
    {
        if (!_replayMode) return;
        if (_replayIndex >= _replayBeats.Count) { EndReplay(); return; }

        ReplayBeat beat = _replayBeats[_replayIndex];
        ShowHighlightAndRefresh(ResolveReplayActingTerritory(beat));

        int delay = beat is ReplayEndTurnBeat ? AiActionDelayMs : AiPreviewDelayMs;
        _aiPacer.Schedule(StepReplayExecute, delay);
    }

    private void StepReplayExecute()
    {
        if (!_replayMode) return;
        if (_replayIndex >= _replayBeats.Count) { EndReplay(); return; }

        ReplayBeat beat = _replayBeats[_replayIndex++];
        bool crossesTurn = beat is ReplayEndTurnBeat;

        switch (beat)
        {
            case ReplayMoveBeat mv:
                ExecuteAiMove(mv.From, mv.To);
                break;
            case ReplayBuyBeat bu:
                ExecuteAiBuyUnit(bu.Capital, bu.To, bu.Level);
                break;
            case ReplayBuildTowerBeat bt:
                ExecuteAiBuildTower(bt.Capital, bt.To);
                break;
            case ReplayEndTurnBeat _:
                ReplayApplyEndTurn();
                break;
            case ReplayClaimVictoryBeat cv:
                Color cvColor = _state.Turns.CurrentPlayer.Color;
                _session.ClaimVictoryPromptedHighestThreshold[cvColor] = cv.ThresholdPercent;
                DeclareWinner(cvColor);
                break;
            case ReplayDismissClaimBeat dcv:
                Color dcColor = _state.Turns.CurrentPlayer.Color;
                _session.ClaimVictoryPromptedHighestThreshold[dcColor] = dcv.ThresholdPercent;
                break;
            case ReplayDismissDefeatBeat _:
                _session.PendingDefeatScreen = null;
                break;
            case ReplayLongPressRallyBeat rally:
                ApplyLongPressRally(rally.Target);
                break;
            case TutorialOnlyBeat _:
                // Tutorial-only beats (e.g., narration text) are
                // authoring-only — the in-game Replay button silently
                // skips them. Tutorial Preview consumes them through
                // TutorialNarrationDriver instead.
                break;
        }

        CheckGameEndConditions();
        RefreshViews();

        if (_session.IsGameOver) { EndReplay(); return; }

        int delay = crossesTurn ? AiBetweenPlayersDelayMs : AiActionDelayMs;
        _aiPacer.Schedule(StepReplayPreview, delay);
    }

    private void EndReplay()
    {
        _replayMode = false;
        _aiPacer.Cancel();
        ShowHighlightAndRefresh(null);
    }

    /// <summary>
    /// Acting-territory resolver for replay preview highlights. Mirrors
    /// <see cref="ResolveAiActingTerritory"/> but covers the broader
    /// beat-kind family (human-only beats too).
    /// </summary>
    private Territory? ResolveReplayActingTerritory(ReplayBeat beat)
    {
        Color owner = _state.Turns.CurrentPlayer.Color;
        return beat switch
        {
            ReplayMoveBeat mv => TerritoryLookup.FindOwnedContaining(_state.Territories, owner, mv.From),
            ReplayBuyBeat bu => TerritoryLookup.FindOwnedContaining(_state.Territories, owner, bu.Capital),
            ReplayBuildTowerBeat bt => TerritoryLookup.FindOwnedContaining(_state.Territories, owner, bt.Capital),
            ReplayLongPressRallyBeat rally => TerritoryLookup.FindOwnedContaining(_state.Territories, owner, rally.Target),
            _ => null,
        };
    }

    /// <summary>
    /// Replay's EndTurn dispatch. Runs the same end-of-turn bookkeeping
    /// as <see cref="EndTurnNow"/> (win check, advance player, start
    /// next turn) but skips the AI loop's <c>RunAiTurnsUntilHumanOrDone</c>
    /// — replay drives every action beat explicitly, so leaving the AI
    /// loop scheduled would queue stale steps.
    /// </summary>
    private void ReplayApplyEndTurn()
    {
        EndOfTurnProcessing();
        if (_session.IsGameOver) return;
        AdvanceToNextActivePlayer();
        StartPlayerTurn();
    }

    /// <summary>
    /// Apply a long-press rally at <paramref name="target"/> against the
    /// current state — same algorithm as the live
    /// <c>OnTileLongClickedBody</c> path, but extracted to skip the
    /// pending-mode and game-over guards and to avoid touching
    /// <c>_handlerMutatedGame</c> / <c>_pendingHumanBeat</c> (which
    /// belong to TrackHandler's accounting). Deterministic from current
    /// state (explicit lex-min tiebreaks in both unit selection and
    /// destination choice).
    /// </summary>
    private void ApplyLongPressRally(HexCoord target)
    {
        HexTile? tile = _state.Grid.Get(target);
        if (tile == null) return;
        Color currentColor = _state.Turns.CurrentPlayer.Color;
        Territory? territory = TerritoryLookup.FindOwnedContaining(
            _state.Territories, currentColor, target);
        if (territory == null) return;

        RallyRules.ResolveRally(_state.Grid, territory, target, currentColor);
    }

    // --- AI action execution --------------------------------------------
    // These mirror ExecuteMove / ExecuteBuyAndPlace / ExecuteBuildTower
    // but bypass session state (no selection, no pending-action mode,
    // no undo push — AI actions are not undoable by the human player
    // since undo is cleared at end of turn anyway).
    //
    // Each execute method validates its preconditions before mutating
    // state. An AI that returns an illegal action (e.g. moving an
    // already-moved unit, buying without gold, building on an occupied
    // tile) triggers an InvalidOperationException that unwinds the
    // AI turn loop and halts the game in an obvious error state. This
    // is defense in depth: RandomAi only produces legal actions by
    // construction, but any future AI with a bug will surface the
    // failure loudly rather than corrupting game state.

    private void ExecuteAiMove(HexCoord source, HexCoord destination)
    {
        Territory? attacker = TerritoryLookup.FindOwnedContaining(
            _state.Territories, _state.Turns.CurrentPlayer.Color, source);
        if (attacker == null)
        {
            throw new InvalidOperationException(
                $"AI Move from {source}: that coord is not in a territory owned by " +
                $"{_state.Turns.CurrentPlayer.Name}.");
        }

        HexTile? srcTile = _state.Grid.Get(source);
        if (srcTile?.Unit == null)
        {
            throw new InvalidOperationException(
                $"AI Move from {source}: no unit on the source tile.");
        }
        if (srcTile.Unit.HasMovedThisTurn)
        {
            throw new InvalidOperationException(
                $"AI Move from {source}: unit has already moved this turn.");
        }

        List<HexCoord> legalTargets = MovementRules.ValidTargets(
            srcTile.Unit.Level, attacker, _state.Grid, _state.Territories);
        if (!legalTargets.Contains(destination))
        {
            throw new InvalidOperationException(
                $"AI Move from {source} to {destination}: destination is not a " +
                $"legal target for a {srcTile.Unit.Level}.");
        }

        // Reposition (own empty destination) is detected before the
        // move so the AI-side "consumes the move" rule can apply
        // afterward — see AiSimulator.MarkAiUnitMoved for why.
        HexTile? dstTile = _state.Grid.Get(destination);
        bool wasReposition = dstTile != null
            && dstTile.Color == attacker.Owner
            && dstTile.Occupant == null;
        bool wasCombine = WasFriendlyUnitAt(destination, attacker.Owner);

        MoveResult result = MovementRules.Move(source, destination, _state.Grid, attacker);
        if (result.WasCapture)
        {
            HandleCapture($"Move {source}→{destination}");
        }
        if (result.Destroyed != null)
        {
            _map.PlayDestructionEffect(destination, result.Destroyed);
        }
        if (wasReposition)
        {
            Unit? movedUnit = _state.Grid.Get(destination)?.Unit;
            if (movedUnit != null) movedUnit.HasMovedThisTurn = true;
        }

        // Sound after the AI's reposition fixup so AI repositions —
        // which the AI loop forces to consume the move — also play.
        DispatchActionSound(destination, result, wasCombine);
    }

    private void ExecuteAiBuyUnit(HexCoord capital, HexCoord destination, UnitLevel level)
    {
        Territory? attacker = TerritoryLookup.FindByCapital(_state.Territories, capital);
        if (attacker == null)
        {
            throw new InvalidOperationException(
                $"AI BuyUnit with capital {capital}: no territory has that capital.");
        }
        if (!PurchaseRules.CanAfford(attacker, _state.Treasury, level))
        {
            throw new InvalidOperationException(
                $"AI BuyUnit from capital {capital}: territory cannot afford a {level} " +
                $"(treasury = {_state.Treasury.GetGold(capital)}g, cost = {PurchaseRules.CostFor(level)}g).");
        }

        List<HexCoord> legalTargets = MovementRules.ValidTargets(
            level, attacker, _state.Grid, _state.Territories);
        if (!legalTargets.Contains(destination))
        {
            throw new InvalidOperationException(
                $"AI BuyUnit to {destination} from capital {capital}: destination is " +
                $"not a legal {level} placement target.");
        }

        // Same AI semantic as ExecuteAiMove: a buy onto an own empty
        // tile is treated as consuming the fresh unit's move so the
        // AI doesn't immediately move it again next call.
        HexTile? dstTile = _state.Grid.Get(destination);
        bool wasReposition = dstTile != null
            && dstTile.Color == attacker.Owner
            && dstTile.Occupant == null;
        bool wasCombine = WasFriendlyUnitAt(destination, attacker.Owner);

        _state.Treasury.SetGold(
            capital, _state.Treasury.GetGold(capital) - PurchaseRules.CostFor(level));
        var unit = new Unit(attacker.Owner, level);
        MoveResult result = MovementRules.PlaceNew(unit, destination, _state.Grid, attacker);
        if (result.WasCapture)
        {
            HandleCapture($"Buy {level} → {destination}");
        }
        if (result.Destroyed != null)
        {
            _map.PlayDestructionEffect(destination, result.Destroyed);
        }
        if (wasReposition)
        {
            Unit? placed = _state.Grid.Get(destination)?.Unit;
            if (placed != null) placed.HasMovedThisTurn = true;
        }

        DispatchActionSound(destination, result, wasCombine);
    }

    private void ExecuteAiBuildTower(HexCoord capital, HexCoord destination)
    {
        Territory? territory = TerritoryLookup.FindByCapital(_state.Territories, capital);
        if (territory == null)
        {
            throw new InvalidOperationException(
                $"AI BuildTower with capital {capital}: no territory has that capital.");
        }
        if (!PurchaseRules.CanAffordTower(territory, _state.Treasury))
        {
            throw new InvalidOperationException(
                $"AI BuildTower from capital {capital}: territory cannot afford a tower " +
                $"(treasury = {_state.Treasury.GetGold(capital)}g).");
        }
        if (!territory.Coords.Contains(destination))
        {
            throw new InvalidOperationException(
                $"AI BuildTower at {destination} from capital {capital}: destination is " +
                $"not in that territory.");
        }
        HexTile? dst = _state.Grid.Get(destination);
        if (dst == null)
        {
            throw new InvalidOperationException(
                $"AI BuildTower at {destination}: coord is off-map.");
        }
        if (!PurchaseRules.IsValidTowerLocation(dst, territory, _state.Grid)
            || !AiCommon.MeetsAiTowerSpacing(destination, territory, _state.Grid))
        {
            throw new InvalidOperationException(
                $"AI BuildTower at {destination} from capital {capital}: " +
                $"location is invalid (occupied, out-of-territory, or within " +
                $"{AiCommon.MinTowerSpacing} hexes of an existing tower).");
        }

        _state.Treasury.SetGold(
            capital, _state.Treasury.GetGold(capital) - PurchaseRules.TowerCost);
        dst.Occupant = new Tower();
        _map.PlaySound(SoundEffect.TowerPlaced, destination);
    }

    /// <summary>
    /// Snapshot every capital-bearing territory's (owner, gold) keyed by
    /// capital coord. Used by the [Capture] trace to diff before/after
    /// reconcile so the log records only what actually changed.
    /// </summary>
    private Dictionary<HexCoord, (Color Owner, int Gold)> SnapshotCapitals(
        IReadOnlyList<Territory> territories)
    {
        var snap = new Dictionary<HexCoord, (Color Owner, int Gold)>();
        foreach (Territory t in territories)
        {
            if (!t.HasCapital) continue;
            HexCoord cap = t.Capital!.Value;
            snap[cap] = (t.Owner, _state.Treasury.GetGold(cap));
        }
        return snap;
    }

    /// <summary>
    /// Set of colors that own at least one capital-bearing territory
    /// in <paramref name="territories"/>. Used by HandleCapture to
    /// detect freshly-eliminated players (had a capital before, none
    /// after).
    /// </summary>
    private static HashSet<Color> ColorsWithCapital(IReadOnlyList<Territory> territories)
    {
        var set = new HashSet<Color>();
        foreach (Territory t in territories)
        {
            if (t.HasCapital) set.Add(t.Owner);
        }
        return set;
    }

    /// <summary>
    /// Print the [Capture] trace: header + one body line per
    /// capital-coord whose existence, owner, or gold changed across the
    /// reconcile. Untouched capitals are omitted so the log stays
    /// readable even on large multi-player maps.
    /// </summary>
    private void LogCaptureDiff(
        string actionDesc,
        Dictionary<HexCoord, (Color Owner, int Gold)> oldCaps,
        Dictionary<HexCoord, (Color Owner, int Gold)> newCaps)
    {
        Console.WriteLine(
            $"[Capture T{_state.Turns.TurnNumber} {_state.Turns.CurrentPlayer.Name}] {actionDesc}");

        var coords = new HashSet<HexCoord>(oldCaps.Keys);
        coords.UnionWith(newCaps.Keys);
        var sorted = new List<HexCoord>(coords);
        sorted.Sort();

        bool any = false;
        foreach (HexCoord c in sorted)
        {
            bool inOld = oldCaps.TryGetValue(c, out (Color Owner, int Gold) o);
            bool inNew = newCaps.TryGetValue(c, out (Color Owner, int Gold) n);
            if (inOld && inNew && o.Owner == n.Owner && o.Gold == n.Gold) continue;

            string oldStr = inOld ? $"{PlayerNameFor(o.Owner)}={o.Gold}g" : "—";
            string newStr = inNew ? $"{PlayerNameFor(n.Owner)}={n.Gold}g" : "gone";
            Console.WriteLine($"  {c}: {oldStr} → {newStr}");
            any = true;
        }
        if (!any) Console.WriteLine("  (no capital/gold changes)");
    }

    private string PlayerNameFor(Color c)
    {
        foreach (Player p in _state.Turns.Players)
        {
            if (p.Color == c) return p.Name;
        }
        return c.ToString();
    }

    // --- View refresh -----------------------------------------------------

    /// <summary>
    /// Set the highlight (null clears) and immediately refresh the
    /// views. The pair appears in both the AI and replay step
    /// machines plus several game-end / dismissal sites — a one-line
    /// helper de-duplicates without inventing new abstraction.
    /// </summary>
    private void ShowHighlightAndRefresh(Territory? selected)
    {
        _map.ShowHighlight(selected);
        RefreshViews();
    }

    /// <summary>
    /// Push current state into both views in one call. Used after any
    /// state change (click, button press, turn end, undo/redo) — the
    /// controller's only way to update the UI.
    /// </summary>
    private void RefreshViews()
    {
        bool hasActionable = HasAnyActionableForCurrentPlayer();
        _hud.Refresh(_state, _session, hasActionable);
        _map.RefreshOccupantVisuals(_state.Turns.CurrentPlayer.Color, _state.Treasury);
        // End Turn CTA when the current player has nothing actionable
        // left. Lives here (not inside _hud.Refresh) so Tutorial Preview's
        // onAfterRefresh callback can overwrite it when the next scripted
        // beat is End Turn — "last write wins" with the preview cue.
        _hud.SetCta(CtaButton.EndTurn, !hasActionable, pulse: false);
        _onAfterRefresh?.Invoke();
    }

    private bool HasAnyActionableForCurrentPlayer()
    {
        Color color = _state.Turns.CurrentPlayer.Color;

        foreach (HexTile tile in _state.Grid.Tiles)
        {
            if (tile.Occupant is Unit unit
                && unit.Owner == color
                && !unit.HasMovedThisTurn)
            {
                return true;
            }
        }

        foreach (Territory territory in _state.Territories)
        {
            if (territory.Owner != color) continue;
            if (PurchaseRules.CanAffordPeasant(territory, _state.Treasury))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Per-territory variant of <see cref="HasAnyActionableForCurrentPlayer"/>:
    /// true iff the current player could do anything in
    /// <paramref name="territory"/> right now — either it contains an
    /// unmoved current-player unit, or the capital has enough gold to
    /// buy the cheapest unit (a peasant). Tower cost (15g) is a strict
    /// superset of peasant cost (10g), so checking peasant alone covers
    /// every purchase. Used by <see cref="StepTerritorySelection"/> to
    /// skip past territories where the player has nothing to do.
    /// </summary>
    private bool TerritoryHasAvailableAction(Territory territory)
    {
        if (PurchaseRules.CanAffordPeasant(territory, _state.Treasury))
        {
            return true;
        }
        Color color = _state.Turns.CurrentPlayer.Color;
        foreach (HexCoord coord in territory.Coords)
        {
            HexTile? tile = _state.Grid.Get(coord);
            if (tile?.Occupant is Unit unit
                && unit.Owner == color
                && !unit.HasMovedThisTurn)
            {
                return true;
            }
        }
        return false;
    }
}
