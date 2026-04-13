using Godot;

/// <summary>
/// Main menu scene root. Shows a row per player slot where each slot
/// can be toggled between Human and AI, then launches the game by
/// writing the choices into <see cref="GameSettings.PlayerIsAi"/> and
/// changing to <c>main.tscn</c>. Purely UI wiring — owns no game
/// logic and is excluded from the test project for the same reason as
/// <see cref="HudView"/>.
/// </summary>
public partial class MainMenuScene : Control
{
    private const int HumanIndex = 0;
    private const int AiIndex = 1;

    private readonly OptionButton[] _roleButtons = new OptionButton[GameSettings.PlayerConfig.Length];

    public override void _Ready()
    {
        // Stretch to fill the viewport so the scrim/background and
        // centered content behave predictably.
        AnchorLeft = 0f;
        AnchorTop = 0f;
        AnchorRight = 1f;
        AnchorBottom = 1f;
        MouseFilter = MouseFilterEnum.Stop;

        // Dark background.
        var background = new ColorRect
        {
            Color = new Color(0.08f, 0.09f, 0.12f),
            AnchorLeft = 0f,
            AnchorTop = 0f,
            AnchorRight = 1f,
            AnchorBottom = 1f,
        };
        AddChild(background);

        Vector2 viewport = GetViewportRect().Size;

        // Centered panel containing the title, player-role grid, and
        // Start Game button.
        const float panelW = 520f;
        const float panelH = 560f;
        var panel = new Panel
        {
            Position = new Vector2((viewport.X - panelW) * 0.5f, (viewport.Y - panelH) * 0.5f),
            Size = new Vector2(panelW, panelH),
        };
        AddChild(panel);

        var title = new Label
        {
            Text = "FourExHex",
            HorizontalAlignment = HorizontalAlignment.Center,
            Position = new Vector2(0, 28),
            Size = new Vector2(panelW, 48),
        };
        title.AddThemeFontSizeOverride("font_size", 40);
        panel.AddChild(title);

        var subtitle = new Label
        {
            Text = "Assign each player to Human or AI",
            HorizontalAlignment = HorizontalAlignment.Center,
            Position = new Vector2(0, 84),
            Size = new Vector2(panelW, 24),
        };
        subtitle.AddThemeFontSizeOverride("font_size", 16);
        panel.AddChild(subtitle);

        // Player rows. Each row is Swatch | Name | OptionButton.
        const float rowStartY = 128f;
        const float rowHeight = 52f;
        const float rowInset = 40f;
        const float swatchSize = 28f;
        const float nameWidth = 120f;
        const float dropdownWidth = 200f;

        for (int i = 0; i < GameSettings.PlayerConfig.Length; i++)
        {
            (string name, string hex) = GameSettings.PlayerConfig[i];
            float rowY = rowStartY + rowHeight * i;

            var swatch = new ColorRect
            {
                Color = new Color(hex),
                Position = new Vector2(rowInset, rowY + (rowHeight - swatchSize) * 0.5f),
                Size = new Vector2(swatchSize, swatchSize),
            };
            panel.AddChild(swatch);

            var nameLabel = new Label
            {
                Text = name,
                Position = new Vector2(rowInset + swatchSize + 16f, rowY + 14f),
                Size = new Vector2(nameWidth, 24f),
            };
            nameLabel.AddThemeFontSizeOverride("font_size", 20);
            panel.AddChild(nameLabel);

            var dropdown = new OptionButton
            {
                Position = new Vector2(panelW - rowInset - dropdownWidth, rowY + 10f),
                Size = new Vector2(dropdownWidth, 32f),
            };
            dropdown.AddItem("Human", HumanIndex);
            dropdown.AddItem("AI", AiIndex);
            bool isAi = i < GameSettings.PlayerIsAi.Length && GameSettings.PlayerIsAi[i];
            dropdown.Selected = isAi ? AiIndex : HumanIndex;
            panel.AddChild(dropdown);
            _roleButtons[i] = dropdown;
        }

        var startButton = new Button { Text = "Start Game" };
        startButton.AddThemeFontSizeOverride("font_size", 24);
        const float startW = 220f;
        const float startH = 52f;
        startButton.Position = new Vector2((panelW - startW) * 0.5f, panelH - startH - 36f);
        startButton.Size = new Vector2(startW, startH);
        startButton.Pressed += OnStartPressed;
        panel.AddChild(startButton);
    }

    private void OnStartPressed()
    {
        for (int i = 0; i < _roleButtons.Length; i++)
        {
            GameSettings.PlayerIsAi[i] = _roleButtons[i].Selected == AiIndex;
        }
        GetTree().ChangeSceneToFile("res://scenes/main.tscn");
    }
}
