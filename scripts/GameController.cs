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

    public GameController(
        GameState state,
        SessionState session,
        IHexMapView map,
        IHudView hud,
        Random? rng = null,
        Func<GameState, Color, HashSet<HexCoord>, Random, AiAction?>? aiChooser = null)
    {
        _state = state;
        _session = session;
        _map = map;
        _hud = hud;
        _rng = rng ?? new Random();
        _aiChooser = aiChooser ?? RandomAi.ChooseNextAction;

        _map.TileClicked += OnTileClicked;
        _hud.BuyPeasantClicked += OnBuyPeasantPressed;
        _hud.BuildTowerClicked += OnBuildTowerPressed;
        _hud.UndoLastClicked += OnUndoLastPressed;
        _hud.UndoTurnClicked += OnUndoTurnPressed;
        _hud.RedoLastClicked += OnRedoLastPressed;
        _hud.RedoAllClicked += OnRedoAllPressed;
        _hud.EndTurnClicked += OnEndTurnPressed;
        _hud.NextTerritoryClicked += OnNextTerritoryPressed;
    }

    /// <summary>
    /// Finish initial game setup: seed starting gold, collect the first
    /// player's income, and do the first view refresh. Main calls this
    /// once after constructing the controller and adding the views to
    /// the scene tree.
    /// </summary>
    public void StartGame()
    {
        SeedStartingGold();
        // First player gets their income but NOT upkeep at game start
        // — pre-placed units in test/scenario fixtures may not be
        // covered by seed gold yet, and we don't want to bankrupt
        // them before anyone has played. Subsequent turns run the
        // full start-of-turn sequence (see StartPlayerTurn).
        _state.Treasury.CollectIncomeFor(
            _state.Turns.CurrentPlayer, _state.Territories, _state.Grid);
        // If the starting player is an AI, auto-drive its turn (and
        // any subsequent consecutive AI players) so human input only
        // happens on a human's turn.
        RunAiTurnsUntilHumanOrDone();
        RefreshViews();
    }

    private void SeedStartingGold()
    {
        const int startingGoldPerTerritory = 10;
        foreach (Territory territory in _state.Territories)
        {
            if (territory.HasCapital)
            {
                _state.Treasury.SetGold(territory.Capital!.Value, startingGoldPerTerritory);
            }
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
        if (_session.Mode == SessionState.ActionMode.BuyingPeasant && tile != null && _session.SelectedTerritory != null)
        {
            // A fresh-bought unit is always a peasant (direct purchase
            // of higher levels is not implemented).
            if (IsValidTarget(UnitLevel.Peasant, tile.Coord))
            {
                ExecuteBuyAndPlace(tile.Coord);
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

    private void ExecuteBuyAndPlace(HexCoord destination)
    {
        if (_session.SelectedTerritory == null) return;

        _session.Undo.PushBefore(CaptureCurrentSnapshot());

        HexCoord capital = _session.SelectedTerritory.Capital!.Value;
        _state.Treasury.SetGold(capital, _state.Treasury.GetGold(capital) - PurchaseRules.PeasantCost);
        var unit = new Unit(_session.SelectedTerritory.Owner);
        MoveResult result = MovementRules.PlaceNew(unit, destination, _state.Grid, _session.SelectedTerritory);

        if (result.WasCapture)
        {
            HandleCapture();
            RebindSelectionToContaining(destination);
        }

        // QoL: if the (possibly rebound) territory can still afford
        // another peasant, keep the user in BuyingPeasant mode so they
        // can queue further purchases without re-clicking the button.
        if (_session.SelectedTerritory != null
            && PurchaseRules.CanAffordPeasant(_session.SelectedTerritory, _state.Treasury))
        {
            _session.Mode = SessionState.ActionMode.BuyingPeasant;
            _session.MoveSource = null;
            _map.ShowMoveTargets(CaptureTargetsOnly(UnitLevel.Peasant, _session.SelectedTerritory));
            _map.ShowMoveSource(null);
            RefreshViews();
        }
        else
        {
            FinishPendingAction();
        }
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

        // After every capture, check whether one color now owns the
        // entire board. If so, the game is over — freeze input until
        // a new game is started. Undo is disabled so players can't
        // rewind past the killing blow.
        Color? winner = WinConditionRules.Winner(_state.Grid);
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

    private void OnBuyPeasantPressed()
    {
        if (_session.IsGameOver) return;
        if (_session.SelectedTerritory == null) return;
        if (!PurchaseRules.CanAffordPeasant(_session.SelectedTerritory, _state.Treasury)) return;

        _session.Mode = SessionState.ActionMode.BuyingPeasant;
        _session.MoveSource = null;
        _map.ShowMoveTargets(CaptureTargetsOnly(UnitLevel.Peasant, _session.SelectedTerritory));
        _map.ShowMoveSource(null);
        RefreshViews();
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
    /// Convert graves left on the board (from combat this turn or
    /// bankruptcy at the start of this turn) into trees, then run one
    /// step of tree spreading. Called after any player's turn ends.
    /// </summary>
    private void EndOfTurnProcessing()
    {
        TreeRules.ConvertGravesToTrees(_state.Grid);
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
    /// unit move flags, collect income, apply upkeep (which may
    /// bankrupt territories and turn their units into graves).
    /// </summary>
    private void StartPlayerTurn()
    {
        ResetMovementFor(_state.Turns.CurrentPlayer, _state.Grid);
        _state.Treasury.CollectIncomeFor(
            _state.Turns.CurrentPlayer, _state.Territories, _state.Grid);
        UpkeepRules.ApplyUpkeepFor(
            _state.Turns.CurrentPlayer, _state.Territories, _state.Grid, _state.Treasury);
    }

    /// <summary>
    /// If the current player is an AI, run their entire turn and
    /// automatically transition to the next player. Keeps chaining
    /// while consecutive players are AIs, so human input is only
    /// needed when a human's turn begins. Terminates immediately if
    /// the game ends mid-chain (winning capture).
    /// </summary>
    private void RunAiTurnsUntilHumanOrDone()
    {
        while (!_session.IsGameOver && _state.Turns.CurrentPlayer.IsAi)
        {
            RunAiTurn();
            if (_session.IsGameOver) break;
            EndOfTurnProcessing();
            AdvanceToNextActivePlayer();
            StartPlayerTurn();
        }
    }

    /// <summary>
    /// Drive <see cref="RandomAi"/> to completion for the current
    /// player: repeatedly ask for the next action and execute it
    /// until the AI returns null (all its territories visited) or
    /// the game ends mid-turn.
    /// </summary>
    private void RunAiTurn()
    {
        Color color = _state.Turns.CurrentPlayer.Color;
        var visited = new HashSet<HexCoord>();

        // Safety cap: at most one action per owned territory plus a
        // generous margin. The visited set guarantees termination in
        // practice; this bound just prevents a runaway if a bug
        // breaks that invariant.
        const int maxIterations = 64;
        for (int i = 0; i < maxIterations; i++)
        {
            AiAction? action = _aiChooser(_state, color, visited, _rng);
            if (action == null) return;
            if (_session.IsGameOver) return;

            switch (action)
            {
                case AiMoveAction mv:
                    ExecuteAiMove(mv.Source, mv.Destination);
                    break;
                case AiBuyUnitAction bu:
                    ExecuteAiBuyUnit(bu.Capital, bu.Destination);
                    break;
                case AiBuildTowerAction bt:
                    ExecuteAiBuildTower(bt.Capital, bt.Destination);
                    break;
            }
        }
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

    private void ExecuteAiBuyUnit(HexCoord capital, HexCoord destination)
    {
        Territory? attacker = FindTerritoryByCapital(capital);
        if (attacker == null)
        {
            throw new InvalidOperationException(
                $"AI BuyUnit with capital {capital}: no territory has that capital.");
        }
        if (!PurchaseRules.CanAffordPeasant(attacker, _state.Treasury))
        {
            throw new InvalidOperationException(
                $"AI BuyUnit from capital {capital}: territory cannot afford a peasant " +
                $"(treasury = {_state.Treasury.GetGold(capital)}g).");
        }

        List<HexCoord> legalTargets = MovementRules.ValidTargets(
            UnitLevel.Peasant, attacker, _state.Grid, _state.Territories);
        if (!legalTargets.Contains(destination))
        {
            throw new InvalidOperationException(
                $"AI BuyUnit to {destination} from capital {capital}: destination is " +
                $"not a legal peasant placement target.");
        }

        _state.Treasury.SetGold(
            capital, _state.Treasury.GetGold(capital) - PurchaseRules.PeasantCost);
        var unit = new Unit(attacker.Owner);
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
