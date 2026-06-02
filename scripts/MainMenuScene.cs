using System.Linq;
using Godot;

/// <summary>
/// Main menu scene root. Hosts two child panels — a landing page with
/// Play / Load / Map Editor buttons and a play-config page with the
/// player-kind dropdowns + map seed + Start Game button — and toggles
/// between them via <see cref="Control.Visible"/>. The Load Game modal
/// is a free-floating <see cref="Window"/> child that pops over the
/// landing page.
/// </summary>
public partial class MainMenuScene : Control
{
    // Indices into the OptionButton item list; values are stored as
    // the OptionButton's item ID so the selection round-trips via
    // GetSelectedId() regardless of reordering.
    private const int HumanId = 0;
    private const int ComputerId = 1;

    private const int SeedMin = 1;
    private const int SeedMax = 9999;

    private readonly OptionButton[] _roleButtons = new OptionButton[GameSettings.PlayerConfig.Length];
    private static readonly Font SerifFont =
        GD.Load<FontFile>("res://fonts/DMSerifDisplay-Regular.ttf");
    private SaveStore _saveStore = null!;
    private SlotPickerDialog? _loadDialog;
    private ConfirmModal? _quitConfirmModal;

    private Control? _landingPanel;
    private Control? _playConfigPanel;
    // Design (unscaled) sizes of the two panels, recorded at build time so
    // FitPanels can scale them down to fit a smaller-than-design viewport.
    private Vector2 _landingDesignSize;
    private Vector2 _playConfigDesignSize;
    private SettingsPanel? _settingsPanel;
    private Button? _landingResumeButton;
    private Button? _landingPlayButton;
    private Button? _landingLoadButton;

    private LineEdit? _seedField;
    private Button? _startButton;
    private OptionButton? _mapSelector;
    // Slot name of the selected starting map, or null when "Random Map"
    // is chosen — that's the dropdown's first/default entry, which keeps
    // the seed field active and triggers the procedural-generation flow.
    private string? _selectedMapName;

    public override void _Ready()
    {
        // Diagnostic launch: if FOUREXHEX_6AI is set we skip the
        // menu entirely and jump straight into the game scene with
        // all slots pre-configured as Computer. Main.cs handles the
        // rest (sync pacer, logging, turn cap, auto-quit). Deferred
        // so the scene change happens after _Ready completes.
        if (OS.GetEnvironment("FOUREXHEX_6AI").Length > 0)
        {
            for (int i = 0; i < GameSettings.PlayerKinds.Length; i++)
            {
                GameSettings.PlayerKinds[i] = PlayerKind.Computer;
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

        _saveStore = new SaveStore();

        _landingPanel = BuildLandingPanel();
        AddChild(_landingPanel);

        _playConfigPanel = BuildPlayConfigPanel();
        AddChild(_playConfigPanel);

        _settingsPanel = new SettingsPanel();
        AddChild(_settingsPanel);

        BuildLoadDialog();
        BuildQuitConfirmDialog();

        ShowLanding();

        // Shrink the fixed-size panels to fit windows shorter/narrower than
        // their design size (e.g. 720p), and keep fitting them on resize.
        FitPanels();
        GetViewport().SizeChanged += FitPanels;
    }

    /// <summary>Scale each fixed-size panel down (never up) so it fits the
    /// current viewport with a small margin. The panels are center-anchored,
    /// so scaling around their center keeps them centered.</summary>
    private void FitPanels()
    {
        Vector2 viewport = GetViewportRect().Size;
        if (_landingPanel != null) ScaleToFit(_landingPanel, _landingDesignSize, viewport);
        if (_playConfigPanel != null) ScaleToFit(_playConfigPanel, _playConfigDesignSize, viewport);
    }

    private static void ScaleToFit(Control panel, Vector2 designSize, Vector2 viewport)
    {
        const float margin = 24f;
        float scale = Mathf.Min(1f, Mathf.Min(
            (viewport.X - margin) / designSize.X,
            (viewport.Y - margin) / designSize.Y));
        panel.PivotOffset = designSize * 0.5f;
        panel.Scale = new Vector2(scale, scale);
    }

    private Control BuildLandingPanel()
    {
        const float panelW = 520f;
        // 820f accommodates the tallest stack: Resume, Play, Play Tutorial,
        // Load, Map Editor, Settings, the debug-only Tutorial Builder, and
        // Exit (8 buttons). Release builds render a 7-button stack (no
        // Tutorial Builder) against a panel that's 80px taller than
        // necessary; not enough to be worth a runtime resize since
        // OS.IsDebugBuild() is compile-time-stable for any given binary.
        const float panelH = 820f;
        // Center-anchored so Godot re-solves the position on every window
        // resize (matches ModalChrome.BuildCenteredPanel). Children below
        // are laid out in the panel's local space against the fixed
        // panelW/panelH, so only the panel's screen position tracks resize.
        var panel = new Panel
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = -panelW * 0.5f, OffsetRight = panelW * 0.5f,
            OffsetTop = -panelH * 0.5f, OffsetBottom = panelH * 0.5f,
            GrowHorizontal = GrowDirection.Both,
            GrowVertical = GrowDirection.Both,
        };
        Log.Trace(Log.LogCategory.Render, "MainMenu: built landing panel (center-anchored).");

        var title = new Label
        {
            Text = "FourExHex",
            HorizontalAlignment = HorizontalAlignment.Center,
            Position = new Vector2(0, 36),
            Size = new Vector2(panelW, 68),
        };
        title.AddThemeFontOverride("font", SerifFont);
        title.AddThemeFontSizeOverride("font_size", 56);
        panel.AddChild(title);

        // Decorative gold rule under the wordmark — the redesign's
        // "thin gold horizontal" that separates brand from action.
        var goldRule = new ColorRect
        {
            Color = UiPalette.GoldDim,
            Position = new Vector2(panelW * 0.5f - 110f, 110f),
            Size = new Vector2(220f, 1f),
        };
        panel.AddChild(goldRule);

        const float buttonH = 64f;
        const float buttonGap = 16f;
        const float buttonInset = 80f;
        const float buttonW = panelW - buttonInset * 2f;
        const float firstButtonY = 140f;

        // Single ListSlots() call shared between Resume (needs the autosave
        // entry specifically) and Load Game (needs any slot) so we don't
        // walk the saves directory twice on every panel build.
        System.Collections.Generic.IReadOnlyList<SaveSlotInfo> slots = _saveStore.ListSlots();

        // Resume sits above Play Game so a returning player hits the
        // one-click path first; new players see it disabled and fall to
        // Play Game directly below.
        _landingResumeButton = new Button { Text = "Resume" };
        _landingResumeButton.AddThemeFontSizeOverride("font_size", 26);
        _landingResumeButton.Position = new Vector2(buttonInset, firstButtonY);
        _landingResumeButton.Size = new Vector2(buttonW, buttonH);
        _landingResumeButton.Pressed += OnResumePressed;
        AudioBus.AttachClick(_landingResumeButton);
        _landingResumeButton.Disabled = !slots.Any(s => s.IsAutosave);
        panel.AddChild(_landingResumeButton);

        _landingPlayButton = new Button { Text = "Play Game" };
        // Intentionally NOT brass-primary here — the redesign spec
        // suggested it, but brass should mark terminal commit actions
        // (Start Game in the setup panel, Resume in Pause), not menu
        // navigation entries that the player passes through every
        // launch. Uniform Button styling is the cleaner choice.
        _landingPlayButton.AddThemeFontSizeOverride("font_size", 26);
        _landingPlayButton.Position = new Vector2(buttonInset, firstButtonY + (buttonH + buttonGap));
        _landingPlayButton.Size = new Vector2(buttonW, buttonH);
        _landingPlayButton.Pressed += OnPlayPressed;
        AudioBus.AttachClick(_landingPlayButton);
        panel.AddChild(_landingPlayButton);

        // Always visible (unlike the debug-only Tutorial Builder) — this is
        // the end-user-facing tutorial entry point. Sits just under Play
        // Game so a new player finds it immediately.
        var playTutorialButton = new Button { Text = "Play Tutorial" };
        playTutorialButton.AddThemeFontSizeOverride("font_size", 26);
        playTutorialButton.Position = new Vector2(buttonInset, firstButtonY + (buttonH + buttonGap) * 2);
        playTutorialButton.Size = new Vector2(buttonW, buttonH);
        playTutorialButton.Pressed += OnPlayTutorialPressed;
        AudioBus.AttachClick(playTutorialButton);
        panel.AddChild(playTutorialButton);

        _landingLoadButton = new Button { Text = "Load Game" };
        _landingLoadButton.AddThemeFontSizeOverride("font_size", 26);
        _landingLoadButton.Position = new Vector2(buttonInset, firstButtonY + (buttonH + buttonGap) * 3);
        _landingLoadButton.Size = new Vector2(buttonW, buttonH);
        _landingLoadButton.Pressed += OnLoadPressed;
        AudioBus.AttachClick(_landingLoadButton);
        // Disable when no saves exist so the user gets immediate visual
        // feedback rather than an empty popup.
        _landingLoadButton.Disabled = slots.Count == 0;
        panel.AddChild(_landingLoadButton);

        var mapEditorButton = new Button { Text = "Map Editor" };
        mapEditorButton.AddThemeFontSizeOverride("font_size", 26);
        mapEditorButton.Position = new Vector2(buttonInset, firstButtonY + (buttonH + buttonGap) * 4);
        mapEditorButton.Size = new Vector2(buttonW, buttonH);
        mapEditorButton.Pressed += OnMapEditorPressed;
        AudioBus.AttachClick(mapEditorButton);
        panel.AddChild(mapEditorButton);

        var settingsButton = new Button { Text = "Settings" };
        settingsButton.AddThemeFontSizeOverride("font_size", 26);
        settingsButton.Position = new Vector2(buttonInset, firstButtonY + (buttonH + buttonGap) * 5);
        settingsButton.Size = new Vector2(buttonW, buttonH);
        settingsButton.Pressed += OnSettingsPressed;
        AudioBus.AttachClick(settingsButton);
        panel.AddChild(settingsButton);

        // Debug-only entry point into the new authoring tool. Per spec
        // §"Dev-mode gating", this button is gated on OS.IsDebugBuild()
        // — release exports never see it.
        int nextRow = 6;
        if (OS.IsDebugBuild())
        {
            var tutorialBuilderButton = new Button { Text = "Tutorial Builder" };
            tutorialBuilderButton.AddThemeFontSizeOverride("font_size", 26);
            tutorialBuilderButton.Position = new Vector2(
                buttonInset, firstButtonY + (buttonH + buttonGap) * nextRow);
            tutorialBuilderButton.Size = new Vector2(buttonW, buttonH);
            tutorialBuilderButton.Pressed += OnTutorialBuilderPressed;
            AudioBus.AttachClick(tutorialBuilderButton);
            panel.AddChild(tutorialBuilderButton);
            nextRow++;
        }

        // Suppress the app-quit button on mobile — Apple HIG (and Google Play
        // guidance) discourage user-initiated quit on phones/tablets; the home
        // gesture is the platform-native way out. Desktop builds still get it.
        if (!OS.HasFeature("mobile"))
        {
            var exitButton = new Button { Text = "Exit" };
            exitButton.AddThemeFontSizeOverride("font_size", 26);
            exitButton.Position = new Vector2(buttonInset, firstButtonY + (buttonH + buttonGap) * nextRow);
            exitButton.Size = new Vector2(buttonW, buttonH);
            exitButton.Pressed += OnExitPressed;
            AudioBus.AttachClick(exitButton);
            panel.AddChild(exitButton);
            Log.Info(Log.LogCategory.Render, "MainMenu: Exit button rendered (desktop build).");
        }
        else
        {
            Log.Info(Log.LogCategory.Render, "MainMenu: Exit button suppressed (mobile build).");
        }

        _landingDesignSize = new Vector2(panelW, panelH);
        return panel;
    }

    private Control BuildPlayConfigPanel()
    {
        // Centered panel containing the title, player-role grid, the
        // Map Seed entry, and the Back / Start Game buttons. All
        // dimensions are scaled 1.2x from the spec's base to read
        // comfortably at the 1600x1080 viewport (matches the heavier
        // play-HUD scale the rest of the redesign settled on).
        const float panelW = 624f;
        const float panelH = 800f;
        // Center-anchored so Godot re-solves the position on every window
        // resize (matches ModalChrome.BuildCenteredPanel). Children below
        // are laid out in the panel's local space against the fixed
        // panelW/panelH, so only the panel's screen position tracks resize.
        var panel = new Panel
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = -panelW * 0.5f, OffsetRight = panelW * 0.5f,
            OffsetTop = -panelH * 0.5f, OffsetBottom = panelH * 0.5f,
            GrowHorizontal = GrowDirection.Both,
            GrowVertical = GrowDirection.Both,
        };
        Log.Trace(Log.LogCategory.Render, "MainMenu: built play-config panel (center-anchored).");

        var title = new Label
        {
            Text = "New Game",
            HorizontalAlignment = HorizontalAlignment.Center,
            Position = new Vector2(0, 34),
            Size = new Vector2(panelW, 68),
        };
        title.AddThemeFontOverride("font", SerifFont);
        title.AddThemeFontSizeOverride("font_size", 50);
        panel.AddChild(title);

        // Thin gold rule under the wordmark — matches the landing panel.
        var goldRule = new ColorRect
        {
            Color = UiPalette.GoldDim,
            Position = new Vector2(panelW * 0.5f - 132f, 115f),
            Size = new Vector2(264f, 1f),
        };
        panel.AddChild(goldRule);

        var subtitle = new Label
        {
            Text = "Assign each player to Human or AI",
            HorizontalAlignment = HorizontalAlignment.Center,
            Position = new Vector2(0, 130),
            Size = new Vector2(panelW, 32),
        };
        subtitle.AddThemeFontSizeOverride("font_size", 22);
        subtitle.AddThemeColorOverride("font_color", UiPalette.InkSoft);
        panel.AddChild(subtitle);

        // Player rows. Each row is Swatch | Name | OptionButton.
        const float rowStartY = 154f;
        const float rowHeight = 62f;
        const float rowInset = 48f;
        const float swatchSize = 34f;
        const float nameWidth = 144f;
        const float dropdownWidth = 240f;

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
                Position = new Vector2(rowInset + swatchSize + 19f, rowY + 17f),
                Size = new Vector2(nameWidth, 29f),
            };
            nameLabel.AddThemeFontSizeOverride("font_size", 24);
            panel.AddChild(nameLabel);

            var dropdown = new OptionButton
            {
                Position = new Vector2(panelW - rowInset - dropdownWidth, rowY + 12f),
                Size = new Vector2(dropdownWidth, 38f),
            };
            dropdown.AddThemeFontSizeOverride("font_size", 21);
            // The button face and its drop-down popup are themed
            // separately; without this the expanded item list renders
            // at the tiny default size instead of matching the face.
            dropdown.GetPopup().AddThemeFontSizeOverride("font_size", 21);
            dropdown.AddItem("Human", HumanId);
            dropdown.AddItem("Computer", ComputerId);
            PlayerKind currentKind = i < GameSettings.PlayerKinds.Length
                ? GameSettings.PlayerKinds[i]
                : PlayerKind.Computer;
            int initialId = currentKind == PlayerKind.Computer ? ComputerId : HumanId;
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

        const float buttonH = 62f;
        float buttonRowY = panelH - buttonH - 43f;

        // Each button stretches under its column above so the action
        // visually attaches to the content it relates to: Back sits
        // under the swatch+name pair, Start Game under the dropdown.
        // Enter is bound to Start Game (see _UnhandledInput) so the
        // action key falls on the rightmost button.
        float leftColX = rowInset;
        float leftColW = swatchSize + 16f + nameWidth;
        float rightColX = panelW - rowInset - dropdownWidth;
        float rightColW = dropdownWidth;

        // Map row sits just below the last player row. The dropdown lists
        // "Random Map" (the default) plus every saved starting map. Picking
        // a map disables the seed field below — the map's terrain replaces
        // procedural generation.
        float mapRowY = rowStartY + rowHeight * GameSettings.PlayerConfig.Length;
        var mapLabel = new Label
        {
            Text = "Map",
            Position = new Vector2(leftColX + swatchSize + 19f, mapRowY + 17f),
            Size = new Vector2(nameWidth, 29f),
        };
        mapLabel.AddThemeFontSizeOverride("font_size", 24);
        panel.AddChild(mapLabel);

        _mapSelector = new OptionButton
        {
            Position = new Vector2(rightColX, mapRowY + 12f),
            Size = new Vector2(rightColW, 38f),
        };
        _mapSelector.AddThemeFontSizeOverride("font_size", 21);
        _mapSelector.GetPopup().AddThemeFontSizeOverride("font_size", 21);
        // Item 0 is the default — generates a fresh procedural map from
        // the seed below. Subsequent items (id == index in ListMaps) are
        // the user's saved starting maps.
        _mapSelector.AddItem("Random Map", 0);
        System.Collections.Generic.IReadOnlyList<SaveSlotInfo> mapSlots = _saveStore.ListMaps();
        for (int i = 0; i < mapSlots.Count; i++)
        {
            _mapSelector.AddItem(mapSlots[i].SlotName, i + 1);
        }
        _mapSelector.Selected = 0;
        _mapSelector.ItemSelected += OnMapSelectorChanged;
        panel.AddChild(_mapSelector);

        // Map Seed row sits just below the Map row, aligned with the
        // dropdown column so the input lines up with the AI selectors
        // above it.
        float seedRowY = rowStartY + rowHeight * (GameSettings.PlayerConfig.Length + 1);
        var seedLabel = new Label
        {
            Text = "Map Seed",
            Position = new Vector2(leftColX + swatchSize + 19f, seedRowY + 17f),
            Size = new Vector2(nameWidth, 29f),
        };
        seedLabel.AddThemeFontSizeOverride("font_size", 24);
        panel.AddChild(seedLabel);

        _seedField = new LineEdit
        {
            Position = new Vector2(rightColX, seedRowY + 12f),
            Size = new Vector2(rightColW, 38f),
            MaxLength = 4,
            Alignment = HorizontalAlignment.Right,
            Text = new System.Random().Next(SeedMin, SeedMax + 1).ToString(),
        };
        _seedField.AddThemeFontSizeOverride("font_size", 21);
        _seedField.TextChanged += OnSeedTextChanged;
        // Intercept = / - even when the LineEdit has focus so the hotkey
        // is focus-agnostic. Without GuiInput here, the LineEdit would
        // consume printable-key events before _UnhandledInput sees them.
        _seedField.GuiInput += OnSeedFieldGuiInput;
        panel.AddChild(_seedField);

        var backButton = new Button { Text = "Back" };
        backButton.AddThemeFontSizeOverride("font_size", 29);
        backButton.Position = new Vector2(leftColX, buttonRowY);
        backButton.Size = new Vector2(leftColW, buttonH);
        backButton.Pressed += OnBackPressed;
        AudioBus.AttachClick(backButton);
        panel.AddChild(backButton);

        _startButton = new Button { Text = "Start Game" };
        _startButton.AddThemeFontSizeOverride("font_size", 29);
        _startButton.Position = new Vector2(rightColX, buttonRowY);
        _startButton.Size = new Vector2(rightColW, buttonH);
        _startButton.Pressed += OnStartPressed;
        AudioBus.AttachClick(_startButton);
        panel.AddChild(_startButton);

        RefreshStartButtonGating();

        _playConfigDesignSize = new Vector2(panelW, panelH);
        return panel;
    }

    private void OnMapSelectorChanged(long index)
    {
        if (_mapSelector == null) return;
        if (index == 0)
        {
            _selectedMapName = null;
            SetSeedFieldEnabled(true);
        }
        else
        {
            _selectedMapName = _mapSelector.GetItemText((int)index);
            SetSeedFieldEnabled(false);
        }
        RefreshStartButtonGating();
    }

    /// <summary>
    /// Enable/disable the seed field. When disabled, the field also drops
    /// focus and refuses click-to-focus — Editable=false alone leaves the
    /// LineEdit clickable and able to capture keystrokes (which the field
    /// would just swallow), confusing the user.
    /// </summary>
    private void SetSeedFieldEnabled(bool enabled)
    {
        if (_seedField == null) return;
        _seedField.Editable = enabled;
        _seedField.FocusMode = enabled ? Control.FocusModeEnum.All : Control.FocusModeEnum.None;
        if (!enabled && _seedField.HasFocus()) _seedField.ReleaseFocus();
    }

    /// <summary>
    /// Start Game is enabled when EITHER a starting map is selected
    /// (terrain comes from disk) OR the seed field has digits (we'll
    /// generate procedurally). Disabled only when both are absent.
    /// </summary>
    private void RefreshStartButtonGating()
    {
        if (_startButton == null) return;
        bool seedEmpty = string.IsNullOrEmpty(_seedField?.Text);
        bool mapSelected = _selectedMapName != null;
        _startButton.Disabled = seedEmpty && !mapSelected;
    }

    private void ShowLanding()
    {
        if (_landingPanel != null) _landingPanel.Visible = true;
        if (_playConfigPanel != null) _playConfigPanel.Visible = false;
        // Re-check save-driven button states on every return to landing
        // so a save that landed in the meantime would unblock them. One
        // ListSlots() call drives both gates.
        System.Collections.Generic.IReadOnlyList<SaveSlotInfo> slots = _saveStore.ListSlots();
        if (_landingResumeButton != null)
        {
            _landingResumeButton.Disabled = !slots.Any(s => s.IsAutosave);
        }
        if (_landingLoadButton != null)
        {
            _landingLoadButton.Disabled = slots.Count == 0;
        }
    }

    private void ShowPlayConfig()
    {
        if (_landingPanel != null) _landingPanel.Visible = false;
        if (_playConfigPanel != null) _playConfigPanel.Visible = true;
    }

    private void OnPlayPressed()
    {
        ShowPlayConfig();
    }

    private void OnMapEditorPressed()
    {
        GetTree().ChangeSceneToFile("res://scenes/map_editor.tscn");
    }

    private void OnSettingsPressed()
    {
        // Settings is a modal layered over the landing page now — leaves
        // the landing buttons visible underneath the backdrop.
        _settingsPanel?.Open();
    }

    private void OnTutorialBuilderPressed()
    {
        GetTree().ChangeSceneToFile("res://scenes/tutorial_builder.tscn");
    }

    private void OnPlayTutorialPressed()
    {
        GetTree().ChangeSceneToFile("res://scenes/play_tutorial.tscn");
    }

    private void BuildQuitConfirmDialog()
    {
        _quitConfirmModal = new ConfirmModal(
            "Exit FourExHex?", "Are you sure you want to exit?", "Exit");
        _quitConfirmModal.Confirmed += OnQuitConfirmed;
        AddChild(_quitConfirmModal);
    }

    // Exit button and the landing-page Escape key both route here — they
    // open the confirmation dialog rather than quitting outright.
    private void OnExitPressed()
    {
        Log.Info(Log.LogCategory.Input, "MainMenu Exit requested — confirming.");
        _quitConfirmModal?.Open();
    }

    private void OnQuitConfirmed()
    {
        Log.Info(Log.LogCategory.Input, "MainMenu quit confirmed — quitting.");
        GetTree().Quit();
    }

    private void OnBackPressed()
    {
        ShowLanding();
    }

    private void OnSeedTextChanged(string newText)
    {
        if (_seedField == null) return;
        // Strip any non-digit characters that slipped past MaxLength
        // (paste, IME, etc.) and keep the caret at the same logical
        // position so typing isn't disrupted.
        string filtered = new string(newText.Where(char.IsAsciiDigit).ToArray());
        if (filtered != newText)
        {
            int caret = _seedField.CaretColumn;
            _seedField.Text = filtered;
            _seedField.CaretColumn = System.Math.Min(caret, filtered.Length);
        }
        RefreshStartButtonGating();
    }

    private void OnSeedFieldGuiInput(InputEvent @event)
    {
        if (@event is not InputEventKey keyEvent || !keyEvent.Pressed) return;
        if (IsIncrementKey(keyEvent.Keycode))
        {
            NudgeSeed(+1);
            _seedField?.AcceptEvent();
        }
        else if (IsDecrementKey(keyEvent.Keycode))
        {
            NudgeSeed(-1);
            _seedField?.AcceptEvent();
        }
        else if (!keyEvent.Echo
            && (keyEvent.Keycode == Key.Enter || keyEvent.Keycode == Key.KpEnter))
        {
            // LineEdit consumes Enter by default, so without this hook
            // _UnhandledInput would never see it while the seed field
            // is focused. Mirror the unfocused behavior: start the game.
            _seedField?.AcceptEvent();
            OnStartPressed();
        }
    }

    private void NudgeSeed(int delta)
    {
        if (_seedField == null) return;
        int.TryParse(_seedField.Text, out int current);
        int next = System.Math.Clamp(current + delta, SeedMin, SeedMax);
        _seedField.Text = next.ToString();
        _seedField.CaretColumn = _seedField.Text.Length;
        if (_startButton != null) _startButton.Disabled = false;
    }

    private static bool IsIncrementKey(Key k) =>
        k == Key.Equal || k == Key.Plus || k == Key.KpAdd;

    private static bool IsDecrementKey(Key k) =>
        k == Key.Minus || k == Key.KpSubtract;

    private void BuildLoadDialog()
    {
        _loadDialog = new SlotPickerDialog("Load Game", "Load failed");
        _loadDialog.Attach(this);
    }

    private void OnLoadPressed()
    {
        if (_loadDialog == null) return;
        _loadDialog.ShowSlots(
            _saveStore.ListSlots(),
            "No save files found.",
            info => info.IsAutosave
                ? $"[Autosave] turn {info.TurnNumber} — {SlotPickerDialog.FormatTimestamp(info.SavedAtUnix)}"
                : $"{info.SlotName} — turn {info.TurnNumber} — {SlotPickerDialog.FormatTimestamp(info.SavedAtUnix)}",
            OnLoadSlotPressed);
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
            _loadDialog?.ShowError($"Could not load '{slotName}': {ex.Message}");
        }
    }

    private void OnResumePressed()
    {
        Log.Info(Log.LogCategory.Input, "MainMenu Resume pressed — loading autosave.");
        try
        {
            LoadedSave loaded = _saveStore.LoadSlot(SaveStore.AutosaveSlotName);
            LoadRequest.Pending = loaded;
            for (int i = 0; i < loaded.Players.Count && i < GameSettings.PlayerKinds.Length; i++)
            {
                GameSettings.PlayerKinds[i] = loaded.Players[i].Kind;
            }
            GetTree().ChangeSceneToFile("res://scenes/main.tscn");
        }
        catch (System.Exception ex)
        {
            // Resume has no dialog of its own — log and re-disable so the
            // user falls back to Load Game (which surfaces errors in its
            // own picker dialog).
            Log.Error(Log.LogCategory.Input, $"MainMenu Resume failed: {ex.Message}");
            if (_landingResumeButton != null) _landingResumeButton.Disabled = true;
        }
    }

    private void LaunchGameScene()
    {
        GetTree().ChangeSceneToFile("res://scenes/main.tscn");
    }


    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey keyEvent || !keyEvent.Pressed) return;

        // Settings modal is a CanvasLayer with its own input handler —
        // when it's open, it consumes Escape itself, so any other key
        // that bubbles up here while it's open is meant to be ignored
        // (the player shouldn't trigger landing shortcuts under the
        // backdrop).
        if (_settingsPanel != null && _settingsPanel.IsOpen) return;

        // Quit-confirm modal owns its own Escape (cancel) while open; let
        // it consume the key instead of the landing handler re-opening it.
        if (_quitConfirmModal != null && _quitConfirmModal.IsOpen) return;

        // Per-panel input dispatch: each panel only sees the keys that
        // make sense while it's the visible one.
        if (_playConfigPanel != null && _playConfigPanel.Visible)
        {
            HandlePlayConfigKey(keyEvent);
            return;
        }
        if (_landingPanel != null && _landingPanel.Visible)
        {
            HandleLandingKey(keyEvent);
        }
    }

    private void HandlePlayConfigKey(InputEventKey keyEvent)
    {
        // Allow auto-repeat (Echo == true) for = / - so holding the key
        // smoothly scrolls the seed value. Enter still rejects echoes
        // below so a held Return doesn't double-launch the game.
        if (IsIncrementKey(keyEvent.Keycode))
        {
            NudgeSeed(+1);
            GetViewport()?.SetInputAsHandled();
            return;
        }
        if (IsDecrementKey(keyEvent.Keycode))
        {
            NudgeSeed(-1);
            GetViewport()?.SetInputAsHandled();
            return;
        }

        if (keyEvent.Echo) return;

        if (keyEvent.Keycode == Key.Escape)
        {
            OnBackPressed();
            GetViewport()?.SetInputAsHandled();
            return;
        }

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

    private void HandleLandingKey(InputEventKey keyEvent)
    {
        if (keyEvent.Echo) return;
        // Escape on the landing page acts like clicking Exit — pops the
        // quit-confirmation dialog. (Once the dialog is open it grabs
        // input focus, so a second Escape cancels it rather than reaching
        // here.)
        if (keyEvent.Keycode == Key.Escape)
        {
            OnExitPressed();
            GetViewport()?.SetInputAsHandled();
            return;
        }
        if (keyEvent.Keycode != Key.Enter && keyEvent.Keycode != Key.KpEnter) return;
        // Mirror button state: only fire if Play is actually clickable.
        if (_landingPlayButton != null && !_landingPlayButton.Disabled)
        {
            OnPlayPressed();
            GetViewport()?.SetInputAsHandled();
        }
    }

    private void OnStartPressed()
    {
        // Persist the dropdown selections — kinds always come from this
        // panel (saved maps don't carry per-color roles).
        for (int i = 0; i < _roleButtons.Length; i++)
        {
            int selectedId = _roleButtons[i].GetSelectedId();
            GameSettings.PlayerKinds[i] = selectedId == ComputerId
                ? PlayerKind.Computer
                : PlayerKind.Human;
        }

        if (_selectedMapName != null)
        {
            // Starting-map flow: load the saved map and hand it to the
            // game scene. Main detects TurnNumber == 0 and treats it as
            // a fresh game (turn 1, red first) over that terrain.
            try
            {
                LoadedSave loaded = _saveStore.LoadMap(_selectedMapName);
                LoadRequest.Pending = loaded;
                GameSettings.MasterSeed = loaded.MasterSeed;
            }
            catch (System.Exception ex)
            {
                _loadDialog?.ShowError($"Could not load map '{_selectedMapName}': {ex.Message}");
                return;
            }
            GetTree().ChangeSceneToFile("res://scenes/main.tscn");
            return;
        }

        // Random-map flow: needs a seed.
        if (_seedField == null || string.IsNullOrEmpty(_seedField.Text)) return;
        int.TryParse(_seedField.Text, out int seed);
        GameSettings.MasterSeed = System.Math.Clamp(seed, SeedMin, SeedMax);
        GetTree().ChangeSceneToFile("res://scenes/main.tscn");
    }
}
