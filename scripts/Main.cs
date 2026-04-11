using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Scene root + game controller. Owns the model (<see cref="GameState"/>)
/// and session state (<see cref="SessionState"/>), wires the two views
/// (<see cref="HexMapView"/>, <see cref="HudView"/>), handles input and
/// button events, applies rules, mutates state, and refreshes the views.
/// </summary>
public partial class Main : Node2D
{
    private static readonly (string Name, string Hex)[] PlayerConfig =
    {
        ("Red",    "e53935"),
        ("Blue",   "1e88e5"),
        ("Green",  "43a047"),
        ("Yellow", "fdd835"),
        ("Purple", "8e24aa"),
        ("Orange", "fb8c00"),
    };

    private HexMapView _map = null!;
    private HudView _hud = null!;
    private GameState _state = null!;
    private SessionState _session = null!;

    public override void _Ready()
    {
        // Build the view shell first so we can read its layout dimensions,
        // but don't add it to the tree until its GameState is prepared.
        _map = new HexMapView();

        // --- Model construction ------------------------------------------
        List<Player> players = BuildPlayers();
        var turnState = new TurnState(players);
        var treasury = new Treasury();

        HexGrid grid = BuildInitialGrid(_map.Cols, _map.Rows, players);
        IReadOnlyList<Territory> raw = TerritoryFinder.FindAll(grid);
        IReadOnlyList<Territory> territories = CapitalReconciler.Reconcile(
            raw, new List<Territory>(), grid);

        _state = new GameState(grid, territories, players, turnState, treasury);
        _session = new SessionState();

        // --- View initialization -----------------------------------------
        _map.Init(_state);
        AddChild(_map);

        Vector2 viewport = GetViewportRect().Size;
        float x = (viewport.X - _map.PixelSize.X) * 0.5f;
        float y = HudView.HudHeight + (viewport.Y - HudView.HudHeight - _map.PixelSize.Y) * 0.5f;
        _map.Position = new Vector2(x, y);

        _hud = new HudView();
        _hud.BuyPeasantClicked += OnBuyPeasantPressed;
        _hud.UndoLastClicked += OnUndoLastPressed;
        _hud.UndoTurnClicked += OnUndoTurnPressed;
        _hud.RedoLastClicked += OnRedoLastPressed;
        _hud.RedoAllClicked += OnRedoAllPressed;
        _hud.EndTurnClicked += OnEndTurnPressed;
        AddChild(_hud);

        _map.TileClicked += OnTileClicked;

        SeedStartingGold();
        _state.Treasury.CollectIncomeFor(_state.Turns.CurrentPlayer, _state.Territories);

        // Final HUD refresh AFTER the treasury is seeded so the CTA
        // coloring on capitals reflects the actual starting gold.
        RefreshHud();
    }

    private static HexGrid BuildInitialGrid(int cols, int rows, IReadOnlyList<Player> players)
    {
        var rng = new RandomNumberGenerator();
        rng.Randomize();

        var grid = new HexGrid();
        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                HexCoord coord = HexCoord.FromOffset(col, row);
                Color color = players[rng.RandiRange(0, players.Count - 1)].Color;
                grid.Add(new HexTile(coord, color));
            }
        }
        return grid;
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

    private static List<Player> BuildPlayers()
    {
        var players = new List<Player>();
        foreach ((string name, string hex) in PlayerConfig)
        {
            players.Add(new Player(name, new Color(hex)));
        }
        return players;
    }

    private GameStateSnapshot CaptureCurrentSnapshot() =>
        GameStateSnapshot.Capture(_state.Grid, _state.Treasury, _state.Territories);

    private void OnTileClicked(HexTile? tile)
    {
        // Handle any pending action mode first.
        if (_session.Mode == SessionState.ActionMode.BuyingPeasant && tile != null && _session.SelectedTerritory != null)
        {
            if (IsValidMoveOrPlacementTarget(tile.Coord))
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
            if (IsValidMoveOrPlacementTarget(tile.Coord))
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
            _map.ShowMoveTargets(CaptureTargetsOnly(territory));
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
        RefreshHud();
    }

    /// <summary>
    /// Returns only the capture targets (adjacent enemy tiles we can take).
    /// Repositions to empty own-territory tiles are legal but not
    /// highlighted — they don't consume the unit's action, so the green
    /// rings only advertise action-consuming moves.
    /// </summary>
    private IEnumerable<HexCoord> CaptureTargetsOnly(Territory territory)
    {
        Color owner = territory.Owner;
        foreach (HexCoord coord in MovementRules.ValidTargets(territory, _state.Grid, _state.Territories))
        {
            HexTile? tile = _state.Grid.Get(coord);
            if (tile != null && tile.Color != owner)
            {
                yield return coord;
            }
        }
    }

    private bool IsValidMoveOrPlacementTarget(HexCoord coord)
    {
        if (_session.SelectedTerritory == null) return false;
        var targets = MovementRules.ValidTargets(
            _session.SelectedTerritory, _state.Grid, _state.Territories);
        return targets.Contains(coord);
    }

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

    private void OnUndoLastPressed()
    {
        if (!_session.Undo.CanUndo) return;
        ApplySnapshot(_session.Undo.UndoLast(CaptureCurrentSnapshot()));
    }

    private void OnUndoTurnPressed()
    {
        if (!_session.Undo.CanUndo) return;
        ApplySnapshot(_session.Undo.UndoTurn(CaptureCurrentSnapshot()));
    }

    private void OnRedoLastPressed()
    {
        if (!_session.Undo.CanRedo) return;
        ApplySnapshot(_session.Undo.RedoLast(CaptureCurrentSnapshot()));
    }

    private void OnRedoAllPressed()
    {
        if (!_session.Undo.CanRedo) return;
        ApplySnapshot(_session.Undo.RedoAll(CaptureCurrentSnapshot()));
    }

    /// <summary>
    /// Restore game state from <paramref name="snapshot"/>, rebuild the
    /// view's derived state, and refresh the HUD. Shared by both undo
    /// and redo.
    /// </summary>
    private void ApplySnapshot(GameStateSnapshot snapshot)
    {
        _state.Territories = snapshot.ApplyTo(_state.Grid, _state.Treasury);
        _map.RebuildAfterTerritoryChange();
        SetSelection(null);
        CancelPendingAction();
        RefreshHud();
    }

    private void HandleCapture()
    {
        IReadOnlyList<Territory> previous = _state.Territories;
        IReadOnlyList<Territory> raw = TerritoryFinder.FindAll(_state.Grid);
        _state.Territories = CapitalReconciler.Reconcile(raw, previous, _state.Grid);
        _state.Treasury.ReconcileAfterCapture(previous, _state.Territories);
        _map.RebuildAfterTerritoryChange();
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
        RefreshHud();
    }

    private void CancelPendingAction()
    {
        _session.ClearPendingAction();
        _map.ShowMoveTargets(System.Array.Empty<HexCoord>());
    }

    private void OnBuyPeasantPressed()
    {
        if (_session.SelectedTerritory == null) return;
        if (!PurchaseRules.CanAffordPeasant(_session.SelectedTerritory, _state.Treasury)) return;

        _session.Mode = SessionState.ActionMode.BuyingPeasant;
        _session.MoveSource = null;
        _map.ShowMoveTargets(CaptureTargetsOnly(_session.SelectedTerritory));
        RefreshHud();
    }

    private void OnEndTurnPressed()
    {
        // Ending the turn commits everything; no further undo.
        _session.Undo.Clear();

        _state.Turns.EndTurn();
        ResetMovementFor(_state.Turns.CurrentPlayer, _state.Grid);
        _state.Treasury.CollectIncomeFor(_state.Turns.CurrentPlayer, _state.Territories);
        CancelPendingAction();
        SetSelection(null);
        RefreshHud();
    }

    private void RefreshHud()
    {
        bool hasActionable = HasAnyActionableForCurrentPlayer();
        _hud.Refresh(_state, _session, hasActionable);

        // Recolor occupant icons so capitals/units with a CTA (affordable
        // buy, move available) show a white interior and non-actionable
        // ones show a solid black interior.
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
