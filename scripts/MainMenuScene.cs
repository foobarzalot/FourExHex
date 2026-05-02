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
    private SaveStore _saveStore = null!;
    private Window? _loadDialog;
    private VBoxContainer? _loadDialogList;
    private AcceptDialog? _loadErrorDialog;
    private Button? _loadButton;

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
                : AiKind.Heuristic;
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

        _saveStore = new SaveStore();

        const float buttonH = 52f;
        float buttonRowY = panelH - buttonH - 36f;

        // Each button stretches under its column above so the action
        // visually attaches to the content it relates to: Load Game
        // sits under the swatch+name pair, Start Game under the
        // dropdown. Enter is bound to Start Game (see _UnhandledInput)
        // so the action key falls on the rightmost button.
        float leftColX = rowInset;
        float leftColW = swatchSize + 16f + nameWidth;
        float rightColX = panelW - rowInset - dropdownWidth;
        float rightColW = dropdownWidth;

        _loadButton = new Button { Text = "Load Game" };
        _loadButton.AddThemeFontSizeOverride("font_size", 24);
        _loadButton.Position = new Vector2(leftColX, buttonRowY);
        _loadButton.Size = new Vector2(leftColW, buttonH);
        _loadButton.Pressed += OnLoadPressed;
        panel.AddChild(_loadButton);

        var startButton = new Button { Text = "Start Game" };
        startButton.AddThemeFontSizeOverride("font_size", 24);
        startButton.Position = new Vector2(rightColX, buttonRowY);
        startButton.Size = new Vector2(rightColW, buttonH);
        startButton.Pressed += OnStartPressed;
        panel.AddChild(startButton);
        // Disable Load Game when no saves exist so the user gets
        // immediate visual feedback rather than an empty popup.
        _loadButton.Disabled = _saveStore.ListSlots().Count == 0;

        BuildLoadDialog();
    }

    private void BuildLoadDialog()
    {
        _loadDialog = new Window
        {
            Title = "Load Game",
            Size = new Vector2I(440, 360),
            Visible = false,
            Exclusive = true,
        };
        _loadDialog.CloseRequested += () => _loadDialog!.Hide();
        AddChild(_loadDialog);

        var scroll = new ScrollContainer
        {
            AnchorLeft = 0f,
            AnchorTop = 0f,
            AnchorRight = 1f,
            AnchorBottom = 1f,
            OffsetLeft = 16f,
            OffsetTop = 16f,
            OffsetRight = -16f,
            OffsetBottom = -16f,
        };
        _loadDialog.AddChild(scroll);

        _loadDialogList = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _loadDialogList.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(_loadDialogList);

        _loadErrorDialog = new AcceptDialog
        {
            Title = "Load failed",
            OkButtonText = "OK",
        };
        AddChild(_loadErrorDialog);
    }

    private void OnLoadPressed()
    {
        if (_loadDialog == null || _loadDialogList == null) return;

        // Rebuild the list each time so newly-saved slots show up.
        foreach (Node child in _loadDialogList.GetChildren())
        {
            child.QueueFree();
        }
        System.Collections.Generic.IReadOnlyList<SaveSlotInfo> slots = _saveStore.ListSlots();
        if (slots.Count == 0)
        {
            var emptyLabel = new Label { Text = "No save files found." };
            emptyLabel.AddThemeFontSizeOverride("font_size", 18);
            _loadDialogList.AddChild(emptyLabel);
        }
        foreach (SaveSlotInfo info in slots)
        {
            string capturedName = info.SlotName;
            string label = info.IsAutosave
                ? $"[Autosave] turn {info.TurnNumber} — {FormatTimestamp(info.SavedAtUnix)}"
                : $"{info.SlotName} — turn {info.TurnNumber} — {FormatTimestamp(info.SavedAtUnix)}";
            var btn = new Button
            {
                Text = label,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                Alignment = HorizontalAlignment.Left,
            };
            btn.AddThemeFontSizeOverride("font_size", 18);
            btn.Pressed += () => OnLoadSlotPressed(capturedName);
            _loadDialogList.AddChild(btn);
        }
        _loadDialog.PopupCentered();
    }

    private void OnLoadSlotPressed(string slotName)
    {
        try
        {
            LoadedSave loaded = _saveStore.LoadSlot(slotName);
            LoadRequest.Pending = loaded;
            // Mirror the saved player roster into GameSettings so the
            // menu reflects them next time it opens (and so re-launches
            // from a Play-Again button preserve the saved kinds).
            for (int i = 0; i < loaded.Players.Count && i < GameSettings.PlayerKinds.Length; i++)
            {
                GameSettings.PlayerKinds[i] = loaded.Players[i].Kind;
            }
            GetTree().ChangeSceneToFile("res://scenes/main.tscn");
        }
        catch (System.Exception ex)
        {
            ShowLoadError($"Could not load '{slotName}': {ex.Message}");
        }
    }

    private void ShowLoadError(string message)
    {
        if (_loadErrorDialog == null)
        {
            GD.PushError(message);
            return;
        }
        _loadErrorDialog.DialogText = message;
        _loadErrorDialog.PopupCentered();
    }

    private static string FormatTimestamp(long unixSeconds)
    {
        var dt = System.DateTimeOffset.FromUnixTimeSeconds(unixSeconds).LocalDateTime;
        return dt.ToString("yyyy-MM-dd HH:mm");
    }

    private void LaunchGameScene()
    {
        GetTree().ChangeSceneToFile("res://scenes/main.tscn");
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey keyEvent) return;
        if (!keyEvent.Pressed || keyEvent.Echo) return;
        if (keyEvent.Keycode != Key.Enter && keyEvent.Keycode != Key.KpEnter) return;

        // The Load Game dialog handles its own input via the Window
        // focus chain, so when it's open _UnhandledInput won't see the
        // key — Enter dismisses or activates the dialog as expected.
        OnStartPressed();
        // OnStartPressed queues a scene change; if the input event is
        // delivered after Godot has already orphaned this node from the
        // tree, GetViewport() returns null. Null-conditional keeps the
        // handler quiet — the scene swap proceeds either way.
        GetViewport()?.SetInputAsHandled();
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
