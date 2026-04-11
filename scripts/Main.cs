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

    private TurnState _turnState = null!;
    private Label _turnLabel = null!;
    private Label _playerLabel = null!;

    public override void _Ready()
    {
        var map = new HexMap();
        AddChild(map);

        // Center the map horizontally and vertically in the area below the
        // reserved HUD strip at the top of the viewport.
        Vector2 viewport = GetViewportRect().Size;
        float x = (viewport.X - map.PixelSize.X) * 0.5f;
        float y = HudHeight + (viewport.Y - HudHeight - map.PixelSize.Y) * 0.5f;
        map.Position = new Vector2(x, y);

        _turnState = new TurnState(BuildPlayers());
        BuildHud();
        RefreshHud();
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
        RefreshHud();
    }

    private void RefreshHud()
    {
        _turnLabel.Text = $"Turn: {_turnState.TurnNumber}";
        Player current = _turnState.CurrentPlayer;
        _playerLabel.Text = $"Current: {current.Name}";
        _playerLabel.AddThemeColorOverride("font_color", current.Color);
    }
}
