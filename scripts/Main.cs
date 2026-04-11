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
    private const int PeasantLevel = (int)UnitLevel.Peasant;

    private enum ActionMode { None, BuyingPeasant, MovingUnit }

    private HexMap _map = null!;
    private TurnState _turnState = null!;
    private Treasury _treasury = null!;
    private UndoStack _undo = null!;

    private Label _turnLabel = null!;
    private Label _playerLabel = null!;
    private Label _goldLabel = null!;
    private Button _buyPeasantButton = null!;
    private Button _undoLastButton = null!;
    private Button _undoTurnButton = null!;
    private Button _redoLastButton = null!;
    private Button _redoAllButton = null!;

    private Territory? _selected;
    private ActionMode _mode = ActionMode.None;
    private HexCoord? _moveSource;

    public override void _Ready()
    {
        _map = new HexMap();
        AddChild(_map);

        Vector2 viewport = GetViewportRect().Size;
        float x = (viewport.X - _map.PixelSize.X) * 0.5f;
        float y = HudHeight + (viewport.Y - HudHeight - _map.PixelSize.Y) * 0.5f;
        _map.Position = new Vector2(x, y);

        _turnState = new TurnState(BuildPlayers());
        _treasury = new Treasury();
        _undo = new UndoStack();

        BuildHud();
        RefreshHud();

        _map.TileClicked += OnTileClicked;
        _map.SelectionChanged += OnSelectionChanged;

        SeedStartingGold();
        _treasury.CollectIncomeFor(_turnState.CurrentPlayer, _map.Territories);
    }

    private void SeedStartingGold()
    {
        const int startingGoldPerTerritory = 10;
        foreach (Territory territory in _map.Territories)
        {
            if (territory.HasCapital)
            {
                _treasury.SetGold(territory.Capital!.Value, startingGoldPerTerritory);
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

        var endTurnButton = new Button { Text = "End Turn" };
        endTurnButton.AddThemeFontSizeOverride("font_size", 18);
        endTurnButton.Pressed += OnEndTurnPressed;
        rightHbox.AddChild(endTurnButton);
    }

    private GameStateSnapshot CaptureCurrentSnapshot() =>
        GameStateSnapshot.Capture(_map.Grid, _treasury, _map.Territories);

    private void OnTileClicked(HexTile? tile)
    {
        // Handle any pending action mode first.
        if (_mode == ActionMode.BuyingPeasant && tile != null && _selected != null)
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
        else if (_mode == ActionMode.MovingUnit && tile != null && _selected != null && _moveSource.HasValue)
        {
            if (IsValidMoveOrPlacementTarget(tile.Coord))
            {
                ExecuteMove(_moveSource.Value, tile.Coord);
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
        if (territory == null || territory.Owner != _turnState.CurrentPlayer.Color)
        {
            _map.SelectTerritory(null);
            return;
        }

        // Select the territory; if the clicked tile has one of our own
        // unused units, also pick it up for movement.
        _map.SelectTerritory(territory);

        if (tile.Unit != null
            && tile.Unit.Owner == _turnState.CurrentPlayer.Color
            && !tile.Unit.HasMovedThisTurn)
        {
            _mode = ActionMode.MovingUnit;
            _moveSource = tile.Coord;
            _map.ShowMoveTargets(
                MovementRules.ValidTargets(PeasantLevel, territory, _map.Grid, _map.Territories));
        }
    }

    private bool IsValidMoveOrPlacementTarget(HexCoord coord)
    {
        if (_selected == null) return false;
        var targets = MovementRules.ValidTargets(
            PeasantLevel, _selected, _map.Grid, _map.Territories);
        return targets.Contains(coord);
    }

    private void ExecuteBuyAndPlace(HexCoord destination)
    {
        if (_selected == null) return;

        _undo.PushBefore(CaptureCurrentSnapshot());

        HexCoord capital = _selected.Capital!.Value;
        _treasury.SetGold(capital, _treasury.GetGold(capital) - PurchaseRules.PeasantCost);
        var unit = new Unit(UnitLevel.Peasant, _selected.Owner);
        MoveResult result = MovementRules.PlaceNew(unit, destination, _map.Grid, _selected);

        _map.RefreshUnitVisual(destination);

        if (result.WasCapture)
        {
            HandleCapture();
        }

        FinishPendingAction();
    }

    private void ExecuteMove(HexCoord source, HexCoord destination)
    {
        if (_selected == null) return;

        _undo.PushBefore(CaptureCurrentSnapshot());

        MoveResult result = MovementRules.Move(source, destination, _map.Grid, _selected);
        _map.RefreshUnitVisual(source);
        _map.RefreshUnitVisual(destination);

        if (result.WasCapture)
        {
            HandleCapture();
        }

        FinishPendingAction();
    }

    private void OnUndoLastPressed()
    {
        if (!_undo.CanUndo) return;
        GameStateSnapshot snap = _undo.UndoLast(CaptureCurrentSnapshot());
        _map.RestoreFromSnapshot(snap, _treasury);
        CancelPendingAction();
        RefreshHud();
    }

    private void OnUndoTurnPressed()
    {
        if (!_undo.CanUndo) return;
        GameStateSnapshot snap = _undo.UndoTurn(CaptureCurrentSnapshot());
        _map.RestoreFromSnapshot(snap, _treasury);
        CancelPendingAction();
        RefreshHud();
    }

    private void OnRedoLastPressed()
    {
        if (!_undo.CanRedo) return;
        GameStateSnapshot snap = _undo.RedoLast(CaptureCurrentSnapshot());
        _map.RestoreFromSnapshot(snap, _treasury);
        CancelPendingAction();
        RefreshHud();
    }

    private void OnRedoAllPressed()
    {
        if (!_undo.CanRedo) return;
        GameStateSnapshot snap = _undo.RedoAll(CaptureCurrentSnapshot());
        _map.RestoreFromSnapshot(snap, _treasury);
        CancelPendingAction();
        RefreshHud();
    }

    private void HandleCapture()
    {
        IReadOnlyList<Territory> old = _map.RecomputeTerritoriesAfterCapture();
        _treasury.ReconcileAfterCapture(old, _map.Territories);
    }

    private void FinishPendingAction()
    {
        _mode = ActionMode.None;
        _moveSource = null;
        _map.ShowMoveTargets(System.Array.Empty<HexCoord>());
        // Selection may have been invalidated by a capture's TerritoryFinder
        // rerun — clear it so the HUD doesn't show stale info.
        _map.SelectTerritory(null);
        RefreshHud();
    }

    private void CancelPendingAction()
    {
        _mode = ActionMode.None;
        _moveSource = null;
        _map.ShowMoveTargets(System.Array.Empty<HexCoord>());
        if (_buyPeasantButton != null)
        {
            _buyPeasantButton.Text = "Buy Peasant (10g)";
        }
    }

    private void OnSelectionChanged(Territory? territory)
    {
        _selected = territory;
        RefreshSelectionUi();
    }

    private void OnBuyPeasantPressed()
    {
        if (_selected == null) return;
        if (!PurchaseRules.CanAffordPeasant(_selected, _treasury)) return;

        _mode = ActionMode.BuyingPeasant;
        _moveSource = null;
        _buyPeasantButton.Text = "Click a tile...";
        _map.ShowMoveTargets(
            MovementRules.ValidTargets(PeasantLevel, _selected, _map.Grid, _map.Territories));
    }

    private void OnEndTurnPressed()
    {
        // Ending the turn commits everything; no further undo.
        _undo.Clear();

        _turnState.EndTurn();
        _map.ResetMovementFor(_turnState.CurrentPlayer);
        _treasury.CollectIncomeFor(_turnState.CurrentPlayer, _map.Territories);
        CancelPendingAction();
        _map.SelectTerritory(null);
        RefreshHud();
    }

    private void RefreshSelectionUi()
    {
        if (_selected == null || !_selected.HasCapital)
        {
            _goldLabel.Text = "";
            _buyPeasantButton.Visible = false;
            _buyPeasantButton.Text = "Buy Peasant (10g)";
            return;
        }

        int gold = _treasury.GetGold(_selected.Capital!.Value);
        _goldLabel.Text = $"Gold: {gold} (size {_selected.Size})";

        _buyPeasantButton.Visible = PurchaseRules.CanAffordPeasant(_selected, _treasury);
        if (_mode != ActionMode.BuyingPeasant)
        {
            _buyPeasantButton.Text = "Buy Peasant (10g)";
        }
    }

    private void RefreshHud()
    {
        _turnLabel.Text = $"Turn: {_turnState.TurnNumber}";
        Player current = _turnState.CurrentPlayer;
        _playerLabel.Text = $"Current: {current.Name}";
        _playerLabel.AddThemeColorOverride("font_color", current.Color);
        RefreshSelectionUi();

        bool canUndo = _undo.CanUndo;
        bool canRedo = _undo.CanRedo;
        _undoLastButton.Disabled = !canUndo;
        _undoTurnButton.Disabled = !canUndo;
        _redoLastButton.Disabled = !canRedo;
        _redoAllButton.Disabled = !canRedo;
    }
}
