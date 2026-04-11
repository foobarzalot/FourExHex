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

    public override void _Ready()
    {
        _map = new HexMap();
        AddChild(_map);

        // Center the map horizontally and vertically in the area below the
        // reserved HUD strip at the top of the viewport.
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

        // Seed income for player 1 at the start of the game so their first
        // turn doesn't begin with empty treasuries.
        _treasury.CollectIncomeFor(_turnState.CurrentPlayer, _map.Territories);
    }

    private void OnTileClicked(Territory? clicked)
    {
        // Only the current player may select their own territories. Clicking
        // on an enemy territory (or outside the grid) clears any existing
        // selection so the HUD doesn't keep showing stale gold.
        if (clicked != null && clicked.Owner == _turnState.CurrentPlayer.Color)
        {
            _map.SelectTerritory(clicked);
        }
        else
        {
            _map.SelectTerritory(null);
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

        // Dark bar across the top so labels stay readable against the map.
        var background = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.8f),
            Position = Vector2.Zero,
            Size = new Vector2(viewport.X, HudHeight),
        };
        layer.AddChild(background);

        // Left-aligned info labels.
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

        // Right-anchored End Turn button.
        var button = new Button { Text = "End Turn" };
        button.AddThemeFontSizeOverride("font_size", 20);
        button.AnchorLeft = 1f;
        button.AnchorRight = 1f;
        button.OffsetLeft = -136f;
        button.OffsetRight = -16f;
        button.OffsetTop = 12f;
        button.OffsetBottom = 48f;
        button.Pressed += OnEndTurnPressed;
        layer.AddChild(button);
    }

    private void OnEndTurnPressed()
    {
        _turnState.EndTurn();
        // The new current player collects income at the start of their turn.
        _treasury.CollectIncomeFor(_turnState.CurrentPlayer, _map.Territories);
        // Drop the previous player's selection so the HUD starts fresh.
        _map.SelectTerritory(null);
        RefreshHud();
    }

    private void OnSelectionChanged(Territory? territory)
    {
        if (territory == null || !territory.HasCapital)
        {
            _goldLabel.Text = "";
            return;
        }

        int gold = _treasury.GetGold(territory.Capital!.Value);
        _goldLabel.Text = $"Gold: {gold} (size {territory.Size})";
    }

    private void RefreshHud()
    {
        _turnLabel.Text = $"Turn: {_turnState.TurnNumber}";
        Player current = _turnState.CurrentPlayer;
        _playerLabel.Text = $"Current: {current.Name}";
        _playerLabel.AddThemeColorOverride("font_color", current.Color);
    }
}
