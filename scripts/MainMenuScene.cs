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
    // Indices into the OptionButton item list; values are stored as
    // the OptionButton's item ID so the selection round-trips via
    // GetSelectedId() regardless of reordering.
    private const int HumanId = 0;
    private const int RandomAiId = 1;
    private const int HeuristicAiId = 2;

    private readonly OptionButton[] _roleButtons = new OptionButton[GameSettings.PlayerConfig.Length];

    public override void _Ready()
    {
        // Diagnostic launch: if FOUREXHEX_6AI is set we skip the
        // menu entirely and jump straight into the game scene with
        // all slots pre-configured as Heuristic. Main.cs handles the
        // rest (sync pacer, logging, turn cap, auto-quit). Deferred
        // so the scene change happens after _Ready completes.
        if (OS.GetEnvironment("FOUREXHEX_6AI").Length > 0)
        {
            for (int i = 0; i < GameSettings.PlayerKinds.Length; i++)
            {
                GameSettings.PlayerKinds[i] = AiKind.Heuristic;
            }
            CallDeferred(nameof(LaunchGameScene));
            return;
        }

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
            dropdown.AddItem("Human", HumanId);
            dropdown.AddItem("Random AI", RandomAiId);
            dropdown.AddItem("Heuristic AI", HeuristicAiId);
            AiKind currentKind = i < GameSettings.PlayerKinds.Length
                ? GameSettings.PlayerKinds[i]
                : AiKind.Random;
            int initialId = currentKind switch
            {
                AiKind.Human => HumanId,
                AiKind.Random => RandomAiId,
                AiKind.Heuristic => HeuristicAiId,
                _ => HumanId,
            };
            // Selected is an index; find the entry that matches the
            // ID we want and select that index.
            for (int item = 0; item < dropdown.ItemCount; item++)
            {
                if (dropdown.GetItemId(item) == initialId)
                {
                    dropdown.Selected = item;
                    break;
                }
            }
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

    private void LaunchGameScene()
    {
        GetTree().ChangeSceneToFile("res://scenes/main.tscn");
    }

    private void OnStartPressed()
    {
        for (int i = 0; i < _roleButtons.Length; i++)
        {
            int selectedId = _roleButtons[i].GetSelectedId();
            GameSettings.PlayerKinds[i] = selectedId switch
            {
                HumanId => AiKind.Human,
                RandomAiId => AiKind.Random,
                HeuristicAiId => AiKind.Heuristic,
                _ => AiKind.Human,
            };
        }
        GetTree().ChangeSceneToFile("res://scenes/main.tscn");
    }
}
