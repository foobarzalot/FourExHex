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

    public GameController(GameState state, SessionState session, IHexMapView map, IHudView hud)
    {
        _state = state;
        _session = session;
        _map = map;
        _hud = hud;

        _map.TileClicked += OnTileClicked;
        _hud.BuyPeasantClicked += OnBuyPeasantPressed;
        _hud.UndoLastClicked += OnUndoLastPressed;
        _hud.UndoTurnClicked += OnUndoTurnPressed;
        _hud.RedoLastClicked += OnRedoLastPressed;
        _hud.RedoAllClicked += OnRedoAllPressed;
        _hud.EndTurnClicked += OnEndTurnPressed;
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
        _state.Treasury.CollectIncomeFor(
            _state.Turns.CurrentPlayer, _state.Territories, _state.Grid);
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
        }

        FinishPendingAction(clearSelection: result.WasCapture);
    }

    private void ExecuteMove(HexCoord source, HexCoord destination)
    {
        if (_session.SelectedTerritory == null) return;

        _session.Undo.PushBefore(CaptureCurrentSnapshot());

        MoveResult result = MovementRules.Move(source, destination, _state.Grid, _session.SelectedTerritory);

        if (result.WasCapture)
        {
            HandleCapture();
        }

        FinishPendingAction(clearSelection: result.WasCapture);
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

    private void FinishPendingAction(bool clearSelection = true)
    {
        _session.ClearPendingAction();
        _map.ShowMoveTargets(System.Array.Empty<HexCoord>());

        // After a capture, the territory list is rebuilt so the old
        // selection object is stale — clear it. After a non-consuming
        // reposition/placement, keep the selection so the user can
        // immediately see their territory + still-actionable units.
        if (clearSelection)
        {
            SetSelection(null);
        }
        RefreshViews();
    }

    private void CancelPendingAction()
    {
        _session.ClearPendingAction();
        _map.ShowMoveTargets(System.Array.Empty<HexCoord>());
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
        RefreshViews();
    }

    private void OnEndTurnPressed()
    {
        if (_session.IsGameOver) return;

        // Ending the turn commits everything; no further undo.
        _session.Undo.Clear();

        // Convert graves left on the board (from combat this turn or
        // bankruptcy at the start of this turn) into trees, then run
        // one step of tree spreading. Income collected on the next
        // player's turn will be tree-aware.
        TreeRules.ConvertGravesToTrees(_state.Grid);
        TreeRules.SpreadTrees(_state.Grid);

        // Advance to the next non-eliminated player. A player with zero
        // tiles left is skipped entirely — they don't get a phantom
        // turn just to see they can't act. HandleCapture's winner check
        // guarantees at least one player still has tiles, so this loop
        // always terminates.
        _state.Turns.EndTurn();
        while (WinConditionRules.IsEliminated(_state.Turns.CurrentPlayer.Color, _state.Grid))
        {
            _state.Turns.EndTurn();
        }
        ResetMovementFor(_state.Turns.CurrentPlayer, _state.Grid);
        _state.Treasury.CollectIncomeFor(
            _state.Turns.CurrentPlayer, _state.Territories, _state.Grid);
        // Upkeep is deducted AFTER income. A territory that can't afford
        // its total upkeep goes bankrupt: every unit in it dies and
        // leaves a grave behind; the remaining gold stays put.
        UpkeepRules.ApplyUpkeepFor(
            _state.Turns.CurrentPlayer, _state.Territories, _state.Grid, _state.Treasury);
        CancelPendingAction();
        SetSelection(null);
        RefreshViews();
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
