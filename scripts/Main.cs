using System.Collections.Generic;
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
    private TurnState _turnState = null!;
    private Treasury _treasury = null!;

    private Label _turnLabel = null!;
    private Label _playerLabel = null!;
    private Label _goldLabel = null!;
    private Button _buyPeasantButton = null!;

    private Territory? _selected;
    private bool _placementMode;

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

        BuildHud();
        RefreshHud();

        _map.TileClicked += OnTileClicked;
        _map.SelectionChanged += OnSelectionChanged;

        // Starting gold: every multi-hex territory begins with exactly one
        // peasant's worth (10), regardless of size. Not documented in the
        // official rules but matches the feel of the original game per
        // empirical testing. Player 1 then also collects their turn-1 income
        // right now so they're on the same footing as later players, who
        // collect when End Turn advances to them.
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

        var endTurnButton = new Button { Text = "End Turn" };
        endTurnButton.AddThemeFontSizeOverride("font_size", 20);
        endTurnButton.AnchorLeft = 1f;
        endTurnButton.AnchorRight = 1f;
        endTurnButton.OffsetLeft = -136f;
        endTurnButton.OffsetRight = -16f;
        endTurnButton.OffsetTop = 12f;
        endTurnButton.OffsetBottom = 48f;
        endTurnButton.Pressed += OnEndTurnPressed;
        layer.AddChild(endTurnButton);
    }

    private void OnTileClicked(HexTile? tile)
    {
        // Placement mode: next valid click drops a peasant and exits mode.
        if (_placementMode && _selected != null && tile != null)
        {
            if (PurchaseRules.IsValidPeasantTarget(tile, _selected))
            {
                PurchaseRules.BuyPeasant(tile, _selected, _treasury);
                _map.RefreshUnitVisual(tile.Coord);
            }
            _placementMode = false;
            RefreshSelectionUi();
            return;
        }

        if (tile == null)
        {
            _map.SelectTerritory(null);
            return;
        }

        Territory? territory = _map.TerritoryAt(tile.Coord);

        // Only the current player may select their own territories.
        if (territory != null && territory.Owner == _turnState.CurrentPlayer.Color)
        {
            _map.SelectTerritory(territory);
        }
        else
        {
            _map.SelectTerritory(null);
        }
    }

    private void OnSelectionChanged(Territory? territory)
    {
        _selected = territory;
        // Any selection change cancels placement mode.
        _placementMode = false;
        RefreshSelectionUi();
    }

    private void OnBuyPeasantPressed()
    {
        if (_selected == null) return;
        if (!PurchaseRules.CanAffordPeasant(_selected, _treasury)) return;
        _placementMode = true;
        _buyPeasantButton.Text = "Click a tile...";
    }

    private void OnEndTurnPressed()
    {
        _turnState.EndTurn();
        _treasury.CollectIncomeFor(_turnState.CurrentPlayer, _map.Territories);
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
        if (!_placementMode)
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
    }
}
