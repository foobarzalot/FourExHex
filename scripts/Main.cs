using System.Collections.Generic;
using System.Linq;
using Godot;

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

    private const float HudHeight = 60f;

    private HexMap _map = null!;
    private GameState _state = null!;
    private SessionState _session = null!;

    private Label _turnLabel = null!;
    private Label _playerLabel = null!;
    private Label _goldLabel = null!;
    private Button _buyPeasantButton = null!;
    private Button _undoLastButton = null!;
    private Button _undoTurnButton = null!;
    private Button _redoLastButton = null!;
    private Button _redoAllButton = null!;
    private Button _endTurnButton = null!;

    public override void _Ready()
    {
        // Build the view shell first so we can read its layout dimensions,
        // but don't add it to the tree until its GameState is prepared.
        _map = new HexMap();

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
        float y = HudHeight + (viewport.Y - HudHeight - _map.PixelSize.Y) * 0.5f;
        _map.Position = new Vector2(x, y);

        BuildHud();

        _map.TileClicked += OnTileClicked;
        _map.SelectionChanged += OnSelectionChanged;

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

    private void BuildHud()
    {
        var layer = new CanvasLayer();
        AddChild(layer);

        Vector2 viewport = GetViewportRect().Size;

        var background = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.8f),
            Position = Vector2.Zero,
            Size = new Vector2(viewport.X, HudHeight),
        };
        layer.AddChild(background);

        var leftHbox = new HBoxContainer
        {
            Position = new Vector2(16, 12),
        };
        leftHbox.AddThemeConstantOverride("separation", 24);
        layer.AddChild(leftHbox);

        _turnLabel = new Label { Text = "Turn: 1" };
        _turnLabel.AddThemeFontSizeOverride("font_size", 24);
        leftHbox.AddChild(_turnLabel);

        _playerLabel = new Label { Text = "Current: Red" };
        _playerLabel.AddThemeFontSizeOverride("font_size", 24);
        leftHbox.AddChild(_playerLabel);

        _goldLabel = new Label { Text = "" };
        _goldLabel.AddThemeFontSizeOverride("font_size", 24);
        leftHbox.AddChild(_goldLabel);

        _buyPeasantButton = new Button { Text = "Buy Peasant (10g)", Visible = false };
        _buyPeasantButton.AddThemeFontSizeOverride("font_size", 20);
        _buyPeasantButton.Pressed += OnBuyPeasantPressed;
        leftHbox.AddChild(_buyPeasantButton);

        // Right-anchored action row: Undo Turn / Undo Last / Redo Last /
        // Redo All / End Turn. The HBoxContainer spans the HUD width with
        // End-alignment so children pack to the right edge.
        var rightHbox = new HBoxContainer
        {
            AnchorLeft = 0f,
            AnchorRight = 1f,
            AnchorTop = 0f,
            AnchorBottom = 0f,
            OffsetLeft = 0f,
            OffsetRight = -16f,
            OffsetTop = 12f,
            OffsetBottom = 48f,
            Alignment = BoxContainer.AlignmentMode.End,
            // The container spans the full width but its children only fill
            // the right side. Ignore mouse events on the container itself so
            // it doesn't steal clicks destined for siblings underneath
            // (e.g., the Buy Peasant button in leftHbox). Buttons have their
            // own MouseFilter.Stop and still receive clicks normally.
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        rightHbox.AddThemeConstantOverride("separation", 8);
        layer.AddChild(rightHbox);

        _undoTurnButton = new Button { Text = "Undo Turn", Disabled = true };
        _undoTurnButton.AddThemeFontSizeOverride("font_size", 18);
        _undoTurnButton.Pressed += OnUndoTurnPressed;
        rightHbox.AddChild(_undoTurnButton);

        _undoLastButton = new Button { Text = "Undo Last", Disabled = true };
        _undoLastButton.AddThemeFontSizeOverride("font_size", 18);
        _undoLastButton.Pressed += OnUndoLastPressed;
        rightHbox.AddChild(_undoLastButton);

        _redoLastButton = new Button { Text = "Redo Last", Disabled = true };
        _redoLastButton.AddThemeFontSizeOverride("font_size", 18);
        _redoLastButton.Pressed += OnRedoLastPressed;
        rightHbox.AddChild(_redoLastButton);

        _redoAllButton = new Button { Text = "Redo All", Disabled = true };
        _redoAllButton.AddThemeFontSizeOverride("font_size", 18);
        _redoAllButton.Pressed += OnRedoAllPressed;
        rightHbox.AddChild(_redoAllButton);

        _endTurnButton = new Button { Text = "End Turn" };
        _endTurnButton.AddThemeFontSizeOverride("font_size", 18);
        _endTurnButton.Pressed += OnEndTurnPressed;
        rightHbox.AddChild(_endTurnButton);
    }

    private GameStateSnapshot CaptureCurrentSnapshot() =>
        GameStateSnapshot.Capture(_map.Grid, _state.Treasury, _map.Territories);

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
            _map.SelectTerritory(null);
            return;
        }

        Territory? territory = _map.TerritoryAt(tile.Coord);
        if (territory == null || territory.Owner != _state.Turns.CurrentPlayer.Color)
        {
            _map.SelectTerritory(null);
            return;
        }

        // Select the territory; if the clicked tile has one of our own
        // unused units, also pick it up for movement.
        _map.SelectTerritory(territory);

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
    /// Returns only the capture targets (adjacent enemy tiles we can take).
    /// Repositions to empty own-territory tiles are legal but not
    /// highlighted — they don't consume the unit's action, so the green
    /// rings only advertise action-consuming moves.
    /// </summary>
    private IEnumerable<HexCoord> CaptureTargetsOnly(Territory territory)
    {
        Color owner = territory.Owner;
        foreach (HexCoord coord in MovementRules.ValidTargets(territory, _map.Grid, _map.Territories))
        {
            HexTile? tile = _map.Grid.Get(coord);
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
            _session.SelectedTerritory, _map.Grid, _map.Territories);
        return targets.Contains(coord);
    }

    private void ExecuteBuyAndPlace(HexCoord destination)
    {
        if (_session.SelectedTerritory == null) return;

        _session.Undo.PushBefore(CaptureCurrentSnapshot());

        HexCoord capital = _session.SelectedTerritory.Capital!.Value;
        _state.Treasury.SetGold(capital, _state.Treasury.GetGold(capital) - PurchaseRules.PeasantCost);
        var unit = new Unit(_session.SelectedTerritory.Owner);
        MoveResult result = MovementRules.PlaceNew(unit, destination, _map.Grid, _session.SelectedTerritory);

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

        MoveResult result = MovementRules.Move(source, destination, _map.Grid, _session.SelectedTerritory);

        if (result.WasCapture)
        {
            HandleCapture();
        }

        FinishPendingAction(clearSelection: result.WasCapture);
    }

    private void OnUndoLastPressed()
    {
        if (!_session.Undo.CanUndo) return;
        GameStateSnapshot snap = _session.Undo.UndoLast(CaptureCurrentSnapshot());
        _map.RestoreFromSnapshot(snap, _state.Treasury);
        CancelPendingAction();
        RefreshHud();
    }

    private void OnUndoTurnPressed()
    {
        if (!_session.Undo.CanUndo) return;
        GameStateSnapshot snap = _session.Undo.UndoTurn(CaptureCurrentSnapshot());
        _map.RestoreFromSnapshot(snap, _state.Treasury);
        CancelPendingAction();
        RefreshHud();
    }

    private void OnRedoLastPressed()
    {
        if (!_session.Undo.CanRedo) return;
        GameStateSnapshot snap = _session.Undo.RedoLast(CaptureCurrentSnapshot());
        _map.RestoreFromSnapshot(snap, _state.Treasury);
        CancelPendingAction();
        RefreshHud();
    }

    private void OnRedoAllPressed()
    {
        if (!_session.Undo.CanRedo) return;
        GameStateSnapshot snap = _session.Undo.RedoAll(CaptureCurrentSnapshot());
        _map.RestoreFromSnapshot(snap, _state.Treasury);
        CancelPendingAction();
        RefreshHud();
    }

    private void HandleCapture()
    {
        IReadOnlyList<Territory> old = _map.RecomputeTerritoriesAfterCapture();
        _state.Treasury.ReconcileAfterCapture(old, _map.Territories);
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
            _map.SelectTerritory(null);
        }
        RefreshHud();
    }

    private void CancelPendingAction()
    {
        _session.ClearPendingAction();
        _map.ShowMoveTargets(System.Array.Empty<HexCoord>());
        if (_buyPeasantButton != null)
        {
            _buyPeasantButton.Text = "Buy Peasant (10g)";
        }
    }

    private void OnSelectionChanged(Territory? territory)
    {
        _session.SelectedTerritory = territory;
        RefreshSelectionUi();
    }

    private void OnBuyPeasantPressed()
    {
        if (_session.SelectedTerritory == null) return;
        if (!PurchaseRules.CanAffordPeasant(_session.SelectedTerritory, _state.Treasury)) return;

        _session.Mode = SessionState.ActionMode.BuyingPeasant;
        _session.MoveSource = null;
        _buyPeasantButton.Text = "Click a tile...";
        _map.ShowMoveTargets(CaptureTargetsOnly(_session.SelectedTerritory));
    }

    private void OnEndTurnPressed()
    {
        // Ending the turn commits everything; no further undo.
        _session.Undo.Clear();

        _state.Turns.EndTurn();
        _map.ResetMovementFor(_state.Turns.CurrentPlayer);
        _state.Treasury.CollectIncomeFor(_state.Turns.CurrentPlayer, _map.Territories);
        CancelPendingAction();
        _map.SelectTerritory(null);
        RefreshHud();
    }

    private void RefreshSelectionUi()
    {
        if (_session.SelectedTerritory == null || !_session.SelectedTerritory.HasCapital)
        {
            _goldLabel.Text = "";
            _buyPeasantButton.Visible = false;
            _buyPeasantButton.Text = "Buy Peasant (10g)";
            return;
        }

        int gold = _state.Treasury.GetGold(_session.SelectedTerritory.Capital!.Value);
        _goldLabel.Text = $"Gold: {gold} (size {_session.SelectedTerritory.Size})";

        _buyPeasantButton.Visible = PurchaseRules.CanAffordPeasant(_session.SelectedTerritory, _state.Treasury);
        if (_session.Mode != SessionState.ActionMode.BuyingPeasant)
        {
            _buyPeasantButton.Text = "Buy Peasant (10g)";
        }
    }

    private void RefreshHud()
    {
        _turnLabel.Text = $"Turn: {_state.Turns.TurnNumber}";
        Player current = _state.Turns.CurrentPlayer;
        _playerLabel.Text = $"Current: {current.Name}";
        _playerLabel.AddThemeColorOverride("font_color", current.Color);
        RefreshSelectionUi();

        bool canUndo = _session.Undo.CanUndo;
        bool canRedo = _session.Undo.CanRedo;
        _undoLastButton.Disabled = !canUndo;
        _undoTurnButton.Disabled = !canUndo;
        _redoLastButton.Disabled = !canRedo;
        _redoAllButton.Disabled = !canRedo;

        // Recolor occupant icons so capitals/units with a CTA (affordable
        // buy, move available) show a white interior and non-actionable
        // ones show a solid black interior.
        _map.RefreshOccupantVisuals(current.Color, _state.Treasury);

        // When the current player has nothing left to do, the End Turn
        // button becomes the CTA and gets the white-fill treatment.
        bool anyActionRemaining = HasAnyActionableForCurrentPlayer();
        SetEndTurnCta(!anyActionRemaining);
    }

    private bool HasAnyActionableForCurrentPlayer()
    {
        Color color = _state.Turns.CurrentPlayer.Color;

        foreach (HexTile tile in _map.Grid.Tiles)
        {
            if (tile.Occupant is Unit unit
                && unit.Owner == color
                && !unit.HasMovedThisTurn)
            {
                return true;
            }
        }

        foreach (Territory territory in _map.Territories)
        {
            if (territory.Owner != color) continue;
            if (PurchaseRules.CanAffordPeasant(territory, _state.Treasury))
            {
                return true;
            }
        }

        return false;
    }

    private void SetEndTurnCta(bool isCta)
    {
        if (isCta)
        {
            var style = new StyleBoxFlat
            {
                BgColor = new Color(1f, 1f, 1f, 1f),
                BorderColor = new Color(0f, 0f, 0f, 1f),
                BorderWidthLeft = 2,
                BorderWidthRight = 2,
                BorderWidthTop = 2,
                BorderWidthBottom = 2,
                CornerRadiusTopLeft = 4,
                CornerRadiusTopRight = 4,
                CornerRadiusBottomLeft = 4,
                CornerRadiusBottomRight = 4,
                ContentMarginLeft = 12,
                ContentMarginRight = 12,
                ContentMarginTop = 6,
                ContentMarginBottom = 6,
            };
            _endTurnButton.AddThemeStyleboxOverride("normal", style);
            _endTurnButton.AddThemeStyleboxOverride("hover", style);
            _endTurnButton.AddThemeStyleboxOverride("pressed", style);
            _endTurnButton.AddThemeColorOverride("font_color", new Color(0f, 0f, 0f));
            _endTurnButton.AddThemeColorOverride("font_hover_color", new Color(0f, 0f, 0f));
            _endTurnButton.AddThemeColorOverride("font_pressed_color", new Color(0f, 0f, 0f));
        }
        else
        {
            _endTurnButton.RemoveThemeStyleboxOverride("normal");
            _endTurnButton.RemoveThemeStyleboxOverride("hover");
            _endTurnButton.RemoveThemeStyleboxOverride("pressed");
            _endTurnButton.RemoveThemeColorOverride("font_color");
            _endTurnButton.RemoveThemeColorOverride("font_hover_color");
            _endTurnButton.RemoveThemeColorOverride("font_pressed_color");
        }
    }
}
