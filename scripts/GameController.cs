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
    private readonly Random _rng;
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

    public GameController(
        GameState state,
        SessionState session,
        IHexMapView map,
        IHudView hud,
        Random? rng = null,
        Func<GameState, Color, HashSet<HexCoord>, Random, AiAction?>? aiChooser = null,
        IAiPacer? aiPacer = null,
        int maxTurnNumber = int.MaxValue)
    {
        _state = state;
        _session = session;
        _map = map;
        _hud = hud;
        _rng = rng ?? new Random();
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
        _hud.CancelActionPressed += OnCancelActionPressed;
    }

    /// <summary>
    /// Finish initial game setup: seed starting gold and do the first
    /// view refresh. Main calls this once after constructing the
    /// controller and adding the views to the scene tree.
    /// </summary>
    public void StartGame()
    {
        SeedStartingGold();
        // No start-of-game income collection: the seed already equals
        // 5 × tree-free cells per territory, which is exactly what each
        // player sees on their first turn. Subsequent turns credit
        // income at the END of the turn (see OnEndTurnPressed and the
        // AI turn-end path).
        RunAiTurnsUntilHumanOrDone();
        RefreshViews();
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
            int earningCells = TreeRules.CountNonTreeTiles(territory, _state.Grid);
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

    private GameStateSnapshot CaptureCurrentSnapshot() =>
        GameStateSnapshot.Capture(_state.Grid, _state.Treasury, _state.Territories);

    // --- Click handling ---------------------------------------------------

    private void OnTileClicked(HexTile? tile)
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
            _map.ShowMoveTargets(CaptureTargetsOnly(tile.Unit.Level, territory));
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
    /// Returns only the capture targets (adjacent enemy tiles we can take)
    /// for a would-be attacker of level <paramref name="attackerLevel"/>.
    /// Repositions to empty own-territory tiles and combines onto friendly
    /// units are legal but not highlighted — they don't consume the
    /// unit's action, so the green rings only advertise action-consuming
    /// moves.
    /// </summary>
    private IEnumerable<HexCoord> CaptureTargetsOnly(UnitLevel attackerLevel, Territory territory)
    {
        Color owner = territory.Owner;
        foreach (HexCoord coord in MovementRules.ValidTargets(attackerLevel, territory, _state.Grid, _state.Territories))
        {
            HexTile? tile = _state.Grid.Get(coord);
            if (tile != null && tile.Color != owner)
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
    /// selected territory. The tile's Occupant must be null — graves,
    /// trees, units, and capitals all block tower construction.
    /// </summary>
    private bool IsValidTowerTarget(HexCoord coord)
    {
        if (_session.SelectedTerritory == null) return false;
        if (!_session.SelectedTerritory.Coords.Contains(coord)) return false;
        HexTile? tile = _state.Grid.Get(coord);
        return tile != null && tile.Occupant == null;
    }

    // --- Buy / move / capture --------------------------------------------

    private void ExecuteBuyAndPlace(UnitLevel level, HexCoord destination)
    {
        if (_session.SelectedTerritory == null) return;

        _session.Undo.PushBefore(CaptureCurrentSnapshot());

        HexCoord capital = _session.SelectedTerritory.Capital!.Value;
        _state.Treasury.SetGold(capital, _state.Treasury.GetGold(capital) - PurchaseRules.CostFor(level));
        var unit = new Unit(_session.SelectedTerritory.Owner, level);
        MoveResult result = MovementRules.PlaceNew(unit, destination, _state.Grid, _session.SelectedTerritory);

        if (result.WasCapture)
        {
            HandleCapture();
            RebindSelectionToContaining(destination);
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
            _map.ShowMoveTargets(CaptureTargetsOnly(next.Value, _session.SelectedTerritory));
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

        _session.Undo.PushBefore(CaptureCurrentSnapshot());

        MoveResult result = MovementRules.Move(source, destination, _state.Grid, _session.SelectedTerritory);

        if (result.WasCapture)
        {
            HandleCapture();
            RebindSelectionToContaining(destination);
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

        _session.Undo.PushBefore(CaptureCurrentSnapshot());

        HexCoord capital = _session.SelectedTerritory.Capital!.Value;
        _state.Treasury.SetGold(
            capital, _state.Treasury.GetGold(capital) - PurchaseRules.TowerCost);

        HexTile dst = _state.Grid.Get(destination)!;
        dst.Occupant = new Tower();

        // QoL: stay in BuildingTower mode if the territory can still
        // afford another tower.
        if (PurchaseRules.CanAffordTower(_session.SelectedTerritory, _state.Treasury))
        {
            _session.Mode = SessionState.ActionMode.BuildingTower;
            _session.MoveSource = null;
            _map.ShowMoveTargets(System.Array.Empty<HexCoord>());
            _map.ShowMoveSource(null);
            RefreshViews();
        }
        else
        {
            FinishPendingAction();
        }
    }

    private void HandleCapture()
    {
        IReadOnlyList<Territory> previous = _state.Territories;
        IReadOnlyList<Territory> raw = TerritoryFinder.FindAll(_state.Grid);
        _state.Territories = CapitalReconciler.Reconcile(raw, previous, _state.Grid);
        _state.Treasury.ReconcileAfterCapture(previous, _state.Territories);
        _map.RebuildAfterTerritoryChange();

        // After every capture, check whether only one player still
        // has a capital-bearing territory. If so, the game is over
        // even if some orphan tiles of other colors remain on the
        // map — they can't come back. Undo is disabled so players
        // can't rewind past the killing blow.
        Color? winner = WinConditionRules.Winner(_state.Grid, _state.Territories);
        if (winner.HasValue)
        {
            _session.Winner = winner;
            _session.Undo.Clear();
        }
    }

    private void FinishPendingAction()
    {
        _session.ClearPendingAction();
        _map.ShowMoveTargets(System.Array.Empty<HexCoord>());
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
        _map.ShowMoveSource(null);
    }

    private void OnCancelActionPressed()
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
        ApplySnapshot(_session.Undo.UndoLast(CaptureCurrentSnapshot()));
    }

    private void OnUndoTurnPressed()
    {
        if (_session.IsGameOver) return;
        if (!_session.Undo.CanUndo) return;
        ApplySnapshot(_session.Undo.UndoTurn(CaptureCurrentSnapshot()));
    }

    private void OnRedoLastPressed()
    {
        if (_session.IsGameOver) return;
        if (!_session.Undo.CanRedo) return;
        ApplySnapshot(_session.Undo.RedoLast(CaptureCurrentSnapshot()));
    }

    private void OnRedoAllPressed()
    {
        if (_session.IsGameOver) return;
        if (!_session.Undo.CanRedo) return;
        ApplySnapshot(_session.Undo.RedoAll(CaptureCurrentSnapshot()));
    }

    /// <summary>
    /// Restore game state from <paramref name="snapshot"/>, rebuild the
    /// view's derived state, and refresh. Shared by undo and redo.
    /// </summary>
    private void ApplySnapshot(GameStateSnapshot snapshot)
    {
        _state.Territories = snapshot.ApplyTo(_state.Grid, _state.Treasury);
        _map.RebuildAfterTerritoryChange();
        SetSelection(null);
        CancelPendingAction();
        RefreshViews();
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
    private void OnBuyPressed()
    {
        if (_session.IsGameOver) return;
        if (_session.SelectedTerritory == null) return;

        UnitLevel? next = NextAffordableBuyLevel();
        if (next == null) return;

        _session.Mode = SessionState.BuyModeFor(next.Value);
        _session.MoveSource = null;
        _map.ShowMoveTargets(CaptureTargetsOnly(next.Value, _session.SelectedTerritory));
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

    private void OnBuildTowerPressed()
    {
        if (_session.IsGameOver) return;
        if (_session.SelectedTerritory == null) return;
        if (!PurchaseRules.CanAffordTower(_session.SelectedTerritory, _state.Treasury)) return;

        _session.Mode = SessionState.ActionMode.BuildingTower;
        _session.MoveSource = null;
        // Towers only build on empty own-territory tiles — no enemy
        // capture targets to highlight.
        _map.ShowMoveTargets(System.Array.Empty<HexCoord>());
        _map.ShowMoveSource(null);
        RefreshViews();
    }

    /// <summary>
    /// Advance the selection to the next current-player multi-hex
    /// territory in lex-min-capital order, wrapping around. Used by
    /// the Tab hotkey. Singletons are excluded because you can't do
    /// anything with them anyway. Cancels any pending buy/build/move
    /// action so the user isn't stuck in a stale action mode on a
    /// different territory.
    /// </summary>
    private void OnNextTerritoryPressed()
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
        int nextIndex = (currentIndex + 1) % owned.Count;

        CancelPendingAction();
        SetSelection(owned[nextIndex]);
    }

    private void OnEndTurnPressed()
    {
        if (_session.IsGameOver) return;

        // Ending the turn commits everything; no further undo.
        _session.Undo.Clear();

        EndOfTurnProcessing();
        AdvanceToNextActivePlayer();
        StartPlayerTurn();
        RunAiTurnsUntilHumanOrDone();

        CancelPendingAction();
        SetSelection(null);
        RefreshViews();
    }

    /// <summary>
    /// End-of-turn bookkeeping for the now-ending player: credit their
    /// income (based on gold-earning cells in their current territories),
    /// convert graves into trees, then run one step of tree spreading.
    /// Called on the ending player while they are still the current
    /// player — <see cref="AdvanceToNextActivePlayer"/> runs after.
    /// </summary>
    private void EndOfTurnProcessing()
    {
        _state.Treasury.CollectIncomeFor(
            _state.Turns.CurrentPlayer, _state.Territories, _state.Grid);
        TreeRules.ConvertGravesToTrees(_state.Grid, _state.Turns.CurrentPlayer.Color);
        TreeRules.SpreadTrees(_state.Grid);
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
            _state.Turns.EndTurn();
        }
    }

    /// <summary>
    /// Start-of-turn bookkeeping for the now-current player: reset
    /// unit move flags and apply upkeep (which may bankrupt
    /// territories and turn their units into graves). Income is NOT
    /// collected here — it's credited at the end of the turn that
    /// earned it (see <see cref="EndOfTurnProcessing"/>).
    /// </summary>
    private void StartPlayerTurn()
    {
        ResetMovementFor(_state.Turns.CurrentPlayer, _state.Grid);
        UpkeepRules.ApplyUpkeepFor(
            _state.Turns.CurrentPlayer, _state.Territories, _state.Grid, _state.Treasury);

        LogTurnStart();
        CheckGameEndConditions();
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
            int income = TreeRules.CountNonTreeTiles(t, _state.Grid);
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
            AiLog.Print(
                $"[T{_state.Turns.TurnNumber}] GAME OVER — " +
                $"winner: {winner?.Name ?? "(none)"}");
            _gameEndedFired = true;
            GameEnded?.Invoke();
            return;
        }

        if (_state.Turns.TurnNumber > _maxTurnNumber)
        {
            AiLog.Print(
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
            // (human next) or schedule the next preview beat.
            EndOfTurnProcessing();
            AdvanceToNextActivePlayer();
            StartPlayerTurn();
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
        if (_gameEndedFired) return;

        // After a capture the old territory object is stale; find the
        // AI's territory now containing the result coord and
        // re-highlight so the outline matches the post-action board.
        Territory? resulting = FindOwnedTerritoryContaining(resultCoord);
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
        return action switch
        {
            AiMoveAction mv => FindOwnedTerritoryContaining(mv.Source),
            AiBuyUnitAction bu => FindOwnedTerritoryContaining(bu.Capital),
            AiBuildTowerAction bt => FindOwnedTerritoryContaining(bt.Capital),
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
        Territory? attacker = FindOwnedTerritoryContaining(source);
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

        MoveResult result = MovementRules.Move(source, destination, _state.Grid, attacker);
        if (result.WasCapture)
        {
            HandleCapture();
        }
    }

    private void ExecuteAiBuyUnit(HexCoord capital, HexCoord destination, UnitLevel level)
    {
        Territory? attacker = FindTerritoryByCapital(capital);
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

        _state.Treasury.SetGold(
            capital, _state.Treasury.GetGold(capital) - PurchaseRules.CostFor(level));
        var unit = new Unit(attacker.Owner, level);
        MoveResult result = MovementRules.PlaceNew(unit, destination, _state.Grid, attacker);
        if (result.WasCapture)
        {
            HandleCapture();
        }
    }

    private void ExecuteAiBuildTower(HexCoord capital, HexCoord destination)
    {
        Territory? territory = FindTerritoryByCapital(capital);
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
        if (dst.Occupant != null)
        {
            throw new InvalidOperationException(
                $"AI BuildTower at {destination}: tile is occupied by a " +
                $"{dst.Occupant.GetType().Name}.");
        }

        _state.Treasury.SetGold(
            capital, _state.Treasury.GetGold(capital) - PurchaseRules.TowerCost);
        dst.Occupant = new Tower();
    }

    private Territory? FindOwnedTerritoryContaining(HexCoord coord)
    {
        Color color = _state.Turns.CurrentPlayer.Color;
        foreach (Territory t in _state.Territories)
        {
            if (t.Owner == color && t.Coords.Contains(coord))
            {
                return t;
            }
        }
        return null;
    }

    private Territory? FindTerritoryByCapital(HexCoord capital)
    {
        foreach (Territory t in _state.Territories)
        {
            if (t.HasCapital && t.Capital!.Value == capital)
            {
                return t;
            }
        }
        return null;
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
