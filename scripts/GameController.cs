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

    public GameController(
        GameState state,
        SessionState session,
        IHexMapView map,
        IHudView hud,
        int? seed = null,
        Func<GameState, Color, HashSet<HexCoord>, Random, AiAction?>? aiChooser = null,
        IAiPacer? aiPacer = null,
        int maxTurnNumber = int.MaxValue)
    {
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

        _map.TileClicked += OnTileClicked;
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
    }

    public void StartGame()
    {
        SeedStartingGold();
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
        ReseedRngForCurrentTurn();
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
        UndoEntry pre = CaptureCurrentSnapshot();
        _handlerMutatedGame = false;
        work();
        // If the handler triggered a game-over (e.g., a winning capture
        // calls Undo.Clear()), don't push — there's nothing to undo past
        // game-end, and the pre-state would otherwise resurrect the
        // just-cleared stack.
        if (_session.IsGameOver) return;
        SessionStateSnapshot postSession = SessionStateSnapshot.Capture(_session);
        bool sessionChanged = !pre.Session.Equals(postSession);
        if (_handlerMutatedGame || sessionChanged)
        {
            _session.Undo.PushBefore(pre);
        }
    }

    // --- Click handling ---------------------------------------------------

    private void OnTileClicked(HexTile? tile) =>
        TrackHandler(() => OnTileClickedBody(tile));

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
            // Clicking somewhere invalid cancels the buy.
            CancelPendingAction();
            // Fall through to treat this as a fresh click.
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
            CancelPendingAction();
            // Fall through to treat this as a fresh click.
        }
        else if (_session.Mode == SessionState.ActionMode.MovingUnit && tile != null && _session.SelectedTerritory != null && _session.MoveSource.HasValue)
        {
            Unit? sourceUnit = _state.Grid.Get(_session.MoveSource.Value)?.Unit;
            if (sourceUnit != null && IsValidTarget(sourceUnit.Level, tile.Coord))
            {
                ExecuteMove(_session.MoveSource.Value, tile.Coord);
                return;
            }
            CancelPendingAction();
        }

        // Normal click handling.
        if (tile == null)
        {
            SetSelection(null);
            return;
        }

        Territory? territory = _map.TerritoryAt(tile.Coord);
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
            _map.ShowMoveTargets(ActionConsumingTargets(tile.Unit.Level, territory));
            _map.ShowMoveSource(tile.Coord);
        }
    }

    /// <summary>
    /// Update <see cref="SessionState.SelectedTerritory"/>, redraw the
    /// view's highlight outline, and refresh the HUD.
    /// </summary>
    private void SetSelection(Territory? territory)
    {
        _session.SelectedTerritory = territory;
        _map.ShowHighlight(territory);
        RefreshViews();
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
        Color owner = territory.Owner;
        foreach (HexCoord coord in MovementRules.ValidTargets(attackerLevel, territory, _state.Grid, _state.Territories))
        {
            HexTile? tile = _state.Grid.Get(coord);
            if (tile == null) continue;
            if (tile.Color != owner || tile.Occupant is Tree)
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

        _handlerMutatedGame = true;

        HexCoord capital = _session.SelectedTerritory.Capital!.Value;
        _state.Treasury.SetGold(capital, _state.Treasury.GetGold(capital) - PurchaseRules.CostFor(level));
        var unit = new Unit(_session.SelectedTerritory.Owner, level);
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

        // Place sound fires only when the placement consumed the new
        // unit's move (capture, tree/grave clear). Free placements onto
        // own empty tiles leave the unit actionable and stay silent.
        if (_state.Grid.Get(destination)?.Unit?.HasMovedThisTurn == true)
        {
            _map.PlayUnitPlaced(destination);
        }

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
            _map.ShowMoveTargets(ActionConsumingTargets(next.Value, _session.SelectedTerritory));
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

        _handlerMutatedGame = true;

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

        // See ExecuteBuyAndPlace: same gate — only fire when the move
        // was consumed (capture / tree / grave). Repositions are silent.
        if (_state.Grid.Get(destination)?.Unit?.HasMovedThisTurn == true)
        {
            _map.PlayUnitPlaced(destination);
        }

        FinishPendingAction();
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

        _handlerMutatedGame = true;

        HexCoord capital = _session.SelectedTerritory.Capital!.Value;
        _state.Treasury.SetGold(
            capital, _state.Treasury.GetGold(capital) - PurchaseRules.TowerCost);

        HexTile dst = _state.Grid.Get(destination)!;
        dst.Occupant = new Tower();
        _map.PlayTowerPlaced(destination);

        // QoL: stay in BuildingTower mode if the territory can still
        // afford another tower. Refresh both the tower-target preview
        // and the coverage tint — the just-placed tower expands the
        // covered set and removes its own tile from the legal set.
        if (PurchaseRules.CanAffordTower(_session.SelectedTerritory, _state.Treasury))
        {
            _session.Mode = SessionState.ActionMode.BuildingTower;
            _session.MoveSource = null;
            _map.ShowMoveTargets(System.Array.Empty<HexCoord>());
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

        IReadOnlyList<Territory> raw = TerritoryFinder.FindAll(_state.Grid);
        _state.Territories = CapitalReconciler.Reconcile(raw, previous, _state.Grid);
        _state.Treasury.ReconcileAfterCapture(previous, _state.Territories);

        Dictionary<HexCoord, (Color Owner, int Gold)> newCaps = SnapshotCapitals(_state.Territories);
        LogCaptureDiff(actionDesc, oldCaps, newCaps);

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
            _session.Winner = winner;
            _session.Undo.Clear();
        }
    }

    private void FinishPendingAction()
    {
        _session.ClearPendingAction();
        _map.ShowMoveTargets(System.Array.Empty<HexCoord>());
        _map.ShowTowerTargets(System.Array.Empty<HexCoord>());
        _map.ShowTowerCoverage(System.Array.Empty<HexCoord>());
        _map.ShowMoveSource(null);
        // Selection is maintained by the caller: a non-capturing
        // reposition leaves it alone; a capture re-binds it via
        // RebindSelectionToContaining; a tower build leaves it alone.
        RefreshViews();
    }

    private void CancelPendingAction()
    {
        _session.ClearPendingAction();
        _map.ShowMoveTargets(System.Array.Empty<HexCoord>());
        _map.ShowTowerTargets(System.Array.Empty<HexCoord>());
        _map.ShowTowerCoverage(System.Array.Empty<HexCoord>());
        _map.ShowMoveSource(null);
    }

    private void OnCancelActionPressed() => TrackHandler(OnCancelActionPressedBody);

    private void OnCancelActionPressedBody()
    {
        if (_session.IsGameOver) return;
        CancelPendingAction();
        RefreshViews();
    }

    // --- Undo / redo ------------------------------------------------------

    private void OnUndoLastPressed()
    {
        if (_session.IsGameOver) return;
        if (!_session.Undo.CanUndo) return;
        HexCoord? before = _session.SelectedTerritory?.Capital;
        ApplySnapshot(_session.Undo.UndoLast(CaptureCurrentSnapshot()));
        CenterIfSelectionChanged(before);
    }

    private void OnUndoTurnPressed()
    {
        if (_session.IsGameOver) return;
        if (!_session.Undo.CanUndo) return;
        ApplySnapshot(_session.Undo.UndoAll(CaptureCurrentSnapshot()));
    }

    private void OnRedoLastPressed()
    {
        if (_session.IsGameOver) return;
        if (!_session.Undo.CanRedo) return;
        HexCoord? before = _session.SelectedTerritory?.Capital;
        ApplySnapshot(_session.Undo.RedoLast(CaptureCurrentSnapshot()));
        CenterIfSelectionChanged(before);
    }

    private void OnRedoAllPressed()
    {
        if (_session.IsGameOver) return;
        if (!_session.Undo.CanRedo) return;
        ApplySnapshot(_session.Undo.RedoAll(CaptureCurrentSnapshot()));
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
            _map.ShowMoveTargets(ActionConsumingTargets(buyLevel.Value, _session.SelectedTerritory));
            _map.ShowTowerTargets(System.Array.Empty<HexCoord>());
            _map.ShowTowerCoverage(System.Array.Empty<HexCoord>());
            _map.ShowMoveSource(null);
            return;
        }
        if (_session.Mode == SessionState.ActionMode.BuildingTower
            && _session.SelectedTerritory != null)
        {
            _map.ShowMoveTargets(System.Array.Empty<HexCoord>());
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
                _map.ShowMoveTargets(ActionConsumingTargets(src.Unit.Level, _session.SelectedTerritory));
                _map.ShowTowerTargets(System.Array.Empty<HexCoord>());
                _map.ShowTowerCoverage(System.Array.Empty<HexCoord>());
                _map.ShowMoveSource(_session.MoveSource);
                return;
            }
            // Defensive fallback: source unit no longer exists.
            _session.Mode = SessionState.ActionMode.None;
            _session.MoveSource = null;
        }
        _map.ShowMoveTargets(System.Array.Empty<HexCoord>());
        _map.ShowTowerTargets(System.Array.Empty<HexCoord>());
        _map.ShowTowerCoverage(System.Array.Empty<HexCoord>());
        _map.ShowMoveSource(null);
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
        _map.ShowMoveTargets(ActionConsumingTargets(next.Value, _session.SelectedTerritory));
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
        _map.ShowMoveTargets(System.Array.Empty<HexCoord>());
        _map.ShowTowerTargets(ValidTowerTargets(_session.SelectedTerritory));
        _map.ShowTowerCoverage(TowerCoverageCoords(_session.SelectedTerritory));
        _map.ShowMoveSource(null);
        RefreshViews();
    }

    /// <summary>
    /// Advance the selection to the next or previous current-player
    /// multi-hex territory in lex-min-capital order, wrapping around.
    /// Used by Tab (forward) and Shift+Tab (backward). Singletons are
    /// excluded because you can't do anything with them. Cancels any
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
        var owned = new List<Territory>();
        foreach (Territory t in _state.Territories)
        {
            if (t.Owner == color && t.HasCapital)
            {
                owned.Add(t);
            }
        }
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
        // null selection → forward lands on first, backward lands on last.
        int nextIndex = forward
            ? (currentIndex + 1) % owned.Count
            : (currentIndex == -1 ? owned.Count - 1 : (currentIndex - 1 + owned.Count) % owned.Count);

        CancelPendingAction();
        SetSelection(owned[nextIndex]);
        _map.CenterOnTerritory(owned[nextIndex]);
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
        _map.ShowMoveTargets(ActionConsumingTargets(chosen.Level, selected));
        // Defensive: clear tower overlays in case we're transitioning out
        // of BuildingTower mode.
        _map.ShowTowerTargets(System.Array.Empty<HexCoord>());
        _map.ShowTowerCoverage(System.Array.Empty<HexCoord>());
        _map.ShowMoveSource(target);
        RefreshViews();
    }

    private void OnEndTurnPressed()
    {
        if (_session.IsGameOver) return;

        // Ending the turn commits everything; no further undo.
        _session.Undo.Clear();

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
            _session.Winner = winner;
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
    /// Advance to the next non-eliminated player. A player with zero
    /// tiles left is skipped entirely — they don't get a phantom turn
    /// just to see they can't act. HandleCapture's winner check
    /// guarantees at least one player still has tiles, so this loop
    /// always terminates.
    /// </summary>
    private void AdvanceToNextActivePlayer()
    {
        _state.Turns.EndTurn();
        while (WinConditionRules.IsEliminated(_state.Turns.CurrentPlayer.Color, _state.Grid))
        {
            System.Console.WriteLine($"[T{_state.Turns.TurnNumber}] skipping eliminated " +
                $"player {_state.Turns.CurrentPlayer.Name}");
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

        UpkeepRules.ApplyUpkeepFor(
            _state.Turns.CurrentPlayer, _state.Territories, _state.Grid, _state.Treasury);

        LogTurnStart();
        CheckGameEndConditions();

        // Fire the autosave hook for human turns. Skipped for AI
        // (autosave is keyed to human turn-start, not AI). Skipped on
        // game-over (no point saving a finished game). The flag is
        // reset at the top of StartPlayerTurn so each turn re-arms.
        if (!_session.IsGameOver
            && !_gameEndedFired
            && !_state.Turns.CurrentPlayer.IsAi
            && !_humanTurnFiredForCurrentTurn)
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
            _map.ShowHighlight(null);
            RefreshViews();
            return;
        }
        if (!_state.Turns.CurrentPlayer.IsAi)
        {
            // Control changed out from under a scheduled callback
            // (scene reload, test teardown). Just stop.
            return;
        }

        Color color = _state.Turns.CurrentPlayer.Color;
        AiAction? action = _aiChooser(_state, color, _aiVisited, _rng);

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
            _map.ShowHighlight(null);
            RefreshViews();

            if (_gameEndedFired) return;
            if (_session.IsGameOver) return;
            if (_state.Turns.CurrentPlayer.IsAi)
            {
                _aiPacer.Schedule(StepAiPreview, AiBetweenPlayersDelayMs);
            }
            return;
        }

        _pendingAiAction = action;
        Territory? acting = ResolveAiActingTerritory(action);
        _map.ShowHighlight(acting);
        RefreshViews();
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
            _map.ShowHighlight(null);
            RefreshViews();
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
                ExecuteAiMove(mv.Source, mv.Destination);
                resultCoord = mv.Destination;
                break;
            case AiBuyUnitAction bu:
                ExecuteAiBuyUnit(bu.Capital, bu.Destination, bu.Level);
                resultCoord = bu.Destination;
                break;
            case AiBuildTowerAction bt:
                ExecuteAiBuildTower(bt.Capital, bt.Destination);
                resultCoord = bt.Destination;
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
            _map.ShowHighlight(null);
            RefreshViews();
            return;
        }

        // After a capture the old territory object is stale; find the
        // AI's territory now containing the result coord and
        // re-highlight so the outline matches the post-action board.
        Territory? resulting = TerritoryLookup.FindOwnedContaining(
            _state.Territories, _state.Turns.CurrentPlayer.Color, resultCoord);
        _map.ShowHighlight(resulting);
        RefreshViews();

        if (_session.IsGameOver)
        {
            _map.ShowHighlight(null);
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
        if (_state.Grid.Get(destination)?.Unit?.HasMovedThisTurn == true)
        {
            _map.PlayUnitPlaced(destination);
        }
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

        if (_state.Grid.Get(destination)?.Unit?.HasMovedThisTurn == true)
        {
            _map.PlayUnitPlaced(destination);
        }
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
        _map.PlayTowerPlaced(destination);
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
    /// Push current state into both views in one call. Used after any
    /// state change (click, button press, turn end, undo/redo) — the
    /// controller's only way to update the UI.
    /// </summary>
    private void RefreshViews()
    {
        bool hasActionable = HasAnyActionableForCurrentPlayer();
        _hud.Refresh(_state, _session, hasActionable);
        _map.RefreshOccupantVisuals(_state.Turns.CurrentPlayer.Color, _state.Treasury);
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
}
