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
    private readonly OptionButton[] _difficultyButtons = new OptionButton[GameSettings.PlayerConfig.Length];
    private static readonly Font SerifFont =
        GD.Load<FontFile>("res://fonts/DMSerifDisplay-Regular.ttf");
    private SaveStore _saveStore = null!;
    private SlotPickerDialog? _loadDialog;
    private ConfirmModal? _quitConfirmModal;

    private Control? _landingPanel;
    private Control? _playConfigPanel;
    private CampaignPanel? _campaignPanel;
    // Design (unscaled) sizes of the two panels, recorded at build time so
    // FitPanels can scale them down to fit a smaller-than-design viewport.
    private Vector2 _landingDesignSize;
    private Vector2 _playConfigDesignSize;
    // Orientation the campaign panel was last built for; a resize that
    // flips it rebuilds the panel 8 columns ↔ 16 columns (see FitPanels).
    private ScreenOrientation _campaignOrientation = ScreenOrientation.Landscape;
    private SettingsPanel? _settingsPanel;
    private Button? _landingResumeButton;
    private Button? _landingPlayButton;
    private Button? _landingLoadButton;

    private LineEdit? _seedField;
    private Button? _startButton;
    private OptionButton? _mapSelector;
    // Orientation the play-config panel was last built for; a viewport
    // resize that flips it triggers a rebuild (see FitPanels).
    private ScreenOrientation _playConfigOrientation = ScreenOrientation.Landscape;
    // Slot name of the selected starting map, or null when "Random Map"
    // is chosen — that's the dropdown's first/default entry, which keeps
    // the seed field active and triggers the procedural-generation flow.
    private string? _selectedMapName;
    // True once _Ready hooked the viewport's SizeChanged (the diagnostic
    // 6AI branch returns before the hook; _ExitTree must not disconnect a
    // never-connected signal).
    private bool _viewportResizeHooked;

    // Mobile keyboard avoidance for the seed field (issue #4).
    // FOUREXHEX_FAKE_KB=<physical px> simulates an on-screen keyboard on
    // desktop (and forces the mobile Return-dismisses behavior) so the
    // lift is testable without a device.
    private const float KeyboardLiftMargin = 16f;
    private float _fakeKeyboardPhysicalHeight;
    private bool _mobileSeedFieldBehavior;
    private float _keyboardLift;

    public override void _Ready()
    {
        // Keyboard-lift polling and tap-outside detection run only while
        // the seed field has focus (see OnSeedFieldFocusEntered/Exited).
        SetProcess(false);
        SetProcessInput(false);

        // Diagnostic launch: if FOUREXHEX_6AI or FOUREXHEX_6AI_QUICK
        // is set we skip the menu entirely and jump straight into the
        // game scene with all slots pre-configured as Computer.
        // Main.cs handles the rest (sync pacer, logging, turn cap,
        // grid size, auto-quit) and branches on which of the two was
        // set. Deferred so the scene change happens after _Ready
        // completes.
        if (OS.GetEnvironment("FOUREXHEX_6AI").Length > 0
            || OS.GetEnvironment("FOUREXHEX_6AI_QUICK").Length > 0)
        {
            for (int i = 0; i < GameSettings.PlayerKinds.Length; i++)
            {
                GameSettings.PlayerKinds[i] = PlayerKind.Computer;
            }
            CallDeferred(nameof(LaunchGameScene));
            return;
        }

#if DEBUG
        CheatMenu.Attach(this);
#endif

        string fakeKb = OS.GetEnvironment("FOUREXHEX_FAKE_KB");
        if (fakeKb.Length > 0 && float.TryParse(fakeKb, out float fakeKbHeight))
        {
            _fakeKeyboardPhysicalHeight = fakeKbHeight;
        }
        _mobileSeedFieldBehavior = OS.HasFeature("mobile") || _fakeKeyboardPhysicalHeight > 0f;

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

        _campaignPanel = BuildCampaignPanel();
        AddChild(_campaignPanel);

        _settingsPanel = new SettingsPanel();
        AddChild(_settingsPanel);

        BuildLoadDialog();
        BuildQuitConfirmDialog();

        ShowLanding();

        // Shrink the fixed-size panels to fit windows shorter/narrower than
        // their design size (e.g. 720p), and keep fitting them on resize.
        FitPanels();
        GetViewport().SizeChanged += FitPanels;
        _viewportResizeHooked = true;
    }

    public override void _ExitTree()
    {
        // The root Window outlives this scene across the menu→game swap;
        // without the unsubscribe a later resize invokes FitPanels on a
        // freed node. Guarded: the diagnostic 6AI branch leaves _Ready
        // before the subscription, and disconnecting a never-connected
        // Godot signal errors.
        if (!_viewportResizeHooked) return;
        GetViewport().SizeChanged -= FitPanels;
        _viewportResizeHooked = false;
        Log.Debug(Log.LogCategory.Display,
            "MainMenuScene: viewport SizeChanged unsubscribed on exit");
    }

    /// <summary>Scale each fixed-size panel down (never up) so it fits the
    /// current viewport with a small margin. The panels are center-anchored,
    /// so scaling around their center keeps them centered.</summary>
    private void FitPanels()
    {
        Vector2 viewport = GetViewportRect().Size;
        RebuildPlayConfigOnOrientationFlip(viewport);
        RebuildCampaignOnOrientationFlip(viewport);
        if (_landingPanel != null) ScaleToFit(_landingPanel, _landingDesignSize, viewport);
        if (_playConfigPanel != null) ScaleToFit(_playConfigPanel, _playConfigDesignSize, viewport);
        if (_campaignPanel != null) ScaleToFit(_campaignPanel, _campaignPanel.DesignSize, viewport);
    }

    /// <summary>Rebuild the campaign panel when a viewport resize flips the
    /// orientation: the honeycomb reflows 8 columns (portrait) ↔ 16 columns
    /// (landscape). All campaign state lives in CampaignStore, so a rebuild
    /// loses nothing.</summary>
    private void RebuildCampaignOnOrientationFlip(Vector2 viewport)
    {
        if (_campaignPanel == null) return;
        ScreenOrientation next = ScreenLayout.Resolve(viewport.X, viewport.Y);
        if (next == _campaignOrientation) return;
        Log.Debug(Log.LogCategory.Campaign,
            $"MainMenu: orientation flip {_campaignOrientation} -> {next}; rebuilding campaign panel");

        bool wasVisible = _campaignPanel.Visible;
        CampaignPanel old = _campaignPanel;
        int treeIndex = old.GetIndex();
        old.Visible = false;
        old.QueueFree();

        _campaignPanel = BuildCampaignPanel();
        AddChild(_campaignPanel);
        MoveChild(_campaignPanel, treeIndex);
        _campaignPanel.Visible = wasVisible;
        if (wasVisible) _campaignPanel.Refresh();
    }

    /// <summary>Rebuild the play-config panel when a viewport resize flips
    /// the orientation (issue #38: portrait stacks each row's difficulty
    /// dropdown under the kind selector; landscape puts them side by side).
    /// Dropdown selections are written back into GameSettings — the build
    /// path initializes from there — and the seed/map controls are restored
    /// explicitly.</summary>
    private void RebuildPlayConfigOnOrientationFlip(Vector2 viewport)
    {
        if (_playConfigPanel == null) return;
        ScreenOrientation next = ScreenLayout.Resolve(viewport.X, viewport.Y);
        if (next == _playConfigOrientation) return;
        Log.Debug(Log.LogCategory.Render,
            $"MainMenu: orientation flip {_playConfigOrientation} -> {next}; rebuilding play-config panel");

        for (int i = 0; i < _roleButtons.Length; i++)
        {
            GameSettings.PlayerKinds[i] = _roleButtons[i].GetSelectedId() == ComputerId
                ? PlayerKind.Computer
                : PlayerKind.Human;
            GameSettings.Difficulties[i] = (Difficulty)_difficultyButtons[i].GetSelectedId();
        }
        string seedText = _seedField?.Text ?? "";
        int mapSelected = _mapSelector?.Selected ?? 0;
        bool wasVisible = _playConfigPanel.Visible;

        // The freed seed field never fires FocusExited, so stop the
        // keyboard-lift polling it may have left running. The new panel
        // starts with default anchor offsets, i.e. zero lift.
        SetProcess(false);
        SetProcessInput(false);
        _keyboardLift = 0f;

        Control old = _playConfigPanel;
        int treeIndex = old.GetIndex();
        old.Visible = false;
        old.QueueFree();

        _playConfigPanel = BuildPlayConfigPanel();
        AddChild(_playConfigPanel);
        MoveChild(_playConfigPanel, treeIndex);
        _playConfigPanel.Visible = wasVisible;
        if (_seedField != null) _seedField.Text = seedText;
        if (_mapSelector != null && mapSelected < _mapSelector.ItemCount)
        {
            _mapSelector.Selected = mapSelected;
            // Setting Selected programmatically doesn't fire ItemSelected;
            // re-derive map-name / seed-enabled / start-gating state.
            OnMapSelectorChanged(mapSelected);
        }
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
        // 820f accommodates the tallest stack: Resume, Play, Campaign,
        // Play Tutorial, Load, Map Editor, Settings, and Exit (8 buttons;
        // the Tutorial Builder entry moved to the debug-only cheat menu,
        // issue #7).
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

        // Campaign ladder (issue #2): 256 fixed-seed levels with
        // persistent progress. Sits right under Play Game — it's the
        // long-horizon progression mode.
        var campaignButton = new Button { Text = "Campaign" };
        campaignButton.AddThemeFontSizeOverride("font_size", 26);
        campaignButton.Position = new Vector2(buttonInset, firstButtonY + (buttonH + buttonGap) * 2);
        campaignButton.Size = new Vector2(buttonW, buttonH);
        campaignButton.Pressed += OnCampaignPressed;
        AudioBus.AttachClick(campaignButton);
        panel.AddChild(campaignButton);

        // The end-user-facing tutorial entry point (the authoring tool
        // lives in the debug-only cheat menu).
        var playTutorialButton = new Button { Text = "Play Tutorial" };
        playTutorialButton.AddThemeFontSizeOverride("font_size", 26);
        playTutorialButton.Position = new Vector2(buttonInset, firstButtonY + (buttonH + buttonGap) * 3);
        playTutorialButton.Size = new Vector2(buttonW, buttonH);
        playTutorialButton.Pressed += OnPlayTutorialPressed;
        AudioBus.AttachClick(playTutorialButton);
        panel.AddChild(playTutorialButton);

        _landingLoadButton = new Button { Text = "Load Game" };
        _landingLoadButton.AddThemeFontSizeOverride("font_size", 26);
        _landingLoadButton.Position = new Vector2(buttonInset, firstButtonY + (buttonH + buttonGap) * 4);
        _landingLoadButton.Size = new Vector2(buttonW, buttonH);
        _landingLoadButton.Pressed += OnLoadPressed;
        AudioBus.AttachClick(_landingLoadButton);
        // Disable when no saves exist so the user gets immediate visual
        // feedback rather than an empty popup.
        _landingLoadButton.Disabled = slots.Count == 0;
        panel.AddChild(_landingLoadButton);

        var mapEditorButton = new Button { Text = "Map Editor" };
        mapEditorButton.AddThemeFontSizeOverride("font_size", 26);
        mapEditorButton.Position = new Vector2(buttonInset, firstButtonY + (buttonH + buttonGap) * 5);
        mapEditorButton.Size = new Vector2(buttonW, buttonH);
        mapEditorButton.Pressed += OnMapEditorPressed;
        AudioBus.AttachClick(mapEditorButton);
        panel.AddChild(mapEditorButton);

        var settingsButton = new Button { Text = "Settings" };
        settingsButton.AddThemeFontSizeOverride("font_size", 26);
        settingsButton.Position = new Vector2(buttonInset, firstButtonY + (buttonH + buttonGap) * 6);
        settingsButton.Size = new Vector2(buttonW, buttonH);
        settingsButton.Pressed += OnSettingsPressed;
        AudioBus.AttachClick(settingsButton);
        panel.AddChild(settingsButton);

        // Suppress the app-quit button on mobile — Apple HIG (and Google Play
        // guidance) discourage user-initiated quit on phones/tablets; the home
        // gesture is the platform-native way out. Desktop builds still get it.
        if (!OS.HasFeature("mobile"))
        {
            var exitButton = new Button { Text = "Exit" };
            exitButton.AddThemeFontSizeOverride("font_size", 26);
            exitButton.Position = new Vector2(buttonInset, firstButtonY + (buttonH + buttonGap) * 7);
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
        //
        // The layout is orientation-dependent (issue #38): in landscape
        // each player row is Swatch | Name | Kind | Difficulty (two
        // narrow dropdowns side by side, panel widened to fit); in
        // portrait the difficulty dropdown drops to a sub-row directly
        // under the kind selector (single full-width dropdown column,
        // panel grows taller instead). FitPanels rebuilds the panel when
        // a resize flips the orientation.
        Vector2 viewportSize = GetViewportRect().Size;
        _playConfigOrientation = ScreenLayout.Resolve(viewportSize.X, viewportSize.Y);
        bool portrait = _playConfigOrientation == ScreenOrientation.Portrait;

        const float kindDropdownWidth = 170f;
        const float difficultyDropdownWidth = 170f;
        const float dropdownGap = 12f;
        const float portraitDropdownWidth = 240f;

        float panelW = portrait ? 624f : 736f;
        // Portrait: six two-row player blocks + Map + Seed + buttons.
        // Landscape: six single rows + Map + Seed + buttons.
        float panelH = portrait ? 1100f : 800f;
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

        const float rowStartY = 154f;
        const float rowHeight = 62f;
        const float rowInset = 48f;
        const float swatchSize = 34f;
        const float nameWidth = 144f;

        // The right-hand control column. Landscape fits the kind +
        // difficulty dropdowns side by side; portrait keeps one wide
        // column and stacks the difficulty dropdown beneath the kind.
        float rightColW = portrait
            ? portraitDropdownWidth
            : kindDropdownWidth + dropdownGap + difficultyDropdownWidth;
        float rightColXShared = panelW - rowInset - rightColW;
        // Portrait player blocks are two rows tall (kind + difficulty).
        float playerBlockH = portrait ? rowHeight + 50f : rowHeight;
        // Headers gap between a portrait row label's right edge and the
        // dropdown column it describes.
        const float headerGap = 12f;

        // Landscape names the two dropdown columns once, in the band the
        // old subtitle occupied. Portrait has no columns to head — each
        // block's rows get their own Type / Difficulty labels instead
        // (see the player loop below).
        if (!portrait)
        {
            panel.AddChild(MakeFieldHeader("Type",
                new Vector2(rightColXShared + 8f, rowStartY - 28f),
                new Vector2(kindDropdownWidth - 8f, 22f),
                HorizontalAlignment.Left));
            panel.AddChild(MakeFieldHeader("Difficulty",
                new Vector2(rightColXShared + kindDropdownWidth + dropdownGap + 8f, rowStartY - 28f),
                new Vector2(difficultyDropdownWidth - 8f, 22f),
                HorizontalAlignment.Left));
        }

        // Player rows. Each row is Swatch | Name | Kind | Difficulty
        // (issue #38: difficulty is per-slot). Difficulty is the human
        // player's self-imposed handicap — Recruit (easiest) … Commander
        // (hardest); the level→cost mapping lives in DifficultyRules and
        // item ids match the enum values. Computer slots always play the
        // Soldier baseline, so their dropdown is locked to Soldier and
        // disabled; flipping a row to Computer resets any other level
        // (see ApplyDifficultyLock).
        for (int i = 0; i < GameSettings.PlayerConfig.Length; i++)
        {
            (string name, string hex) = GameSettings.PlayerConfig[i];
            float rowY = rowStartY + playerBlockH * i;

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

            if (portrait)
            {
                // Row headers: right-aligned against the dropdown column.
                // The Type label squeezes between the name column and the
                // dropdown; the Difficulty sub-row has the full left side.
                float nameEndX = rowInset + swatchSize + 19f + nameWidth;
                float labelRightX = rightColXShared - headerGap;
                panel.AddChild(MakeFieldHeader("Type",
                    new Vector2(nameEndX, rowY + 20f),
                    new Vector2(labelRightX - nameEndX, 22f),
                    HorizontalAlignment.Right));
                panel.AddChild(MakeFieldHeader("Difficulty",
                    new Vector2(rowInset + swatchSize + 19f, rowY + rowHeight + 8f),
                    new Vector2(labelRightX - (rowInset + swatchSize + 19f), 22f),
                    HorizontalAlignment.Right));
            }

            float kindWidth = portrait ? portraitDropdownWidth : kindDropdownWidth;
            var dropdown = new OptionButton
            {
                Position = new Vector2(rightColXShared, rowY + 12f),
                Size = new Vector2(kindWidth, 38f),
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
            SelectItemById(dropdown, currentKind == PlayerKind.Computer ? ComputerId : HumanId);
            panel.AddChild(dropdown);
            _roleButtons[i] = dropdown;

            // Portrait: sub-row directly under the kind selector.
            // Landscape: beside it in the same row.
            Vector2 difficultyPosition = portrait
                ? new Vector2(rightColXShared, rowY + rowHeight)
                : new Vector2(rightColXShared + kindDropdownWidth + dropdownGap, rowY + 12f);
            float difficultyWidth = portrait ? portraitDropdownWidth : difficultyDropdownWidth;
            var difficultyDropdown = new OptionButton
            {
                Position = difficultyPosition,
                Size = new Vector2(difficultyWidth, 38f),
            };
            difficultyDropdown.AddThemeFontSizeOverride("font_size", 21);
            difficultyDropdown.GetPopup().AddThemeFontSizeOverride("font_size", 21);
            difficultyDropdown.AddItem("Recruit", (int)Difficulty.Recruit);
            difficultyDropdown.AddItem("Soldier", (int)Difficulty.Soldier);
            difficultyDropdown.AddItem("Captain", (int)Difficulty.Captain);
            difficultyDropdown.AddItem("Commander", (int)Difficulty.Commander);
            // Initialize from GameSettings (mirrors the kind dropdown) so
            // loaded saves / Play Again round-trip per-slot levels.
            Difficulty currentDifficulty = i < GameSettings.Difficulties.Length
                ? GameSettings.Difficulties[i]
                : Difficulty.Soldier;
            SelectItemById(difficultyDropdown, (int)currentDifficulty);
            panel.AddChild(difficultyDropdown);
            _difficultyButtons[i] = difficultyDropdown;

            // Lock the difficulty dropdown whenever the row is a Computer
            // slot — now (initial state) and on every kind change.
            int slot = i;
            dropdown.ItemSelected += _ => ApplyDifficultyLock(slot);
            ApplyDifficultyLock(slot);
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
        float rightColX = rightColXShared;

        // Map row sits just below the last player row. The dropdown lists
        // "Random Map" (the default) plus every saved starting map. Picking
        // a map disables the seed field below — the map's terrain replaces
        // procedural generation.
        float mapRowY = rowStartY + playerBlockH * GameSettings.PlayerConfig.Length;
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
        float seedRowY = mapRowY + rowHeight;
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
            // Tapping/clicking into the field selects the existing seed so
            // the next keystroke replaces it (issue #4).
            SelectAllOnFocus = true,
        };
        _seedField.AddThemeFontSizeOverride("font_size", 21);
        _seedField.TextChanged += OnSeedTextChanged;
        _seedField.FocusEntered += OnSeedFieldFocusEntered;
        _seedField.FocusExited += OnSeedFieldFocusExited;
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

        Log.Debug(Log.LogCategory.Render,
            $"MainMenu: play-config built ({_playConfigOrientation}, per-row difficulty "
            + (portrait ? "sub-rows)" : "side-by-side)"));
        _playConfigDesignSize = new Vector2(panelW, panelH);
        return panel;
    }

    /// <summary>Small muted label naming a dropdown column (landscape
    /// column headers) or a control row (portrait row headers) on the
    /// play-config panel.</summary>
    private static Label MakeFieldHeader(
        string text, Vector2 position, Vector2 size, HorizontalAlignment alignment)
    {
        var label = new Label
        {
            Text = text,
            Position = position,
            Size = size,
            HorizontalAlignment = alignment,
        };
        label.AddThemeFontSizeOverride("font_size", 18);
        label.AddThemeColorOverride("font_color", UiPalette.InkSoft);
        return label;
    }

    /// <summary>Computer slots always play the Soldier baseline: while a
    /// row's kind is Computer its difficulty dropdown is pinned to Soldier
    /// and disabled. The reset sticks — flipping the row back to Human
    /// re-enables the dropdown at Soldier rather than restoring the old
    /// level.</summary>
    private void ApplyDifficultyLock(int slot)
    {
        OptionButton difficultyDropdown = _difficultyButtons[slot];
        bool isComputer = _roleButtons[slot].GetSelectedId() == ComputerId;
        if (isComputer && (Difficulty)difficultyDropdown.GetSelectedId() != Difficulty.Soldier)
        {
            SelectItemById(difficultyDropdown, (int)Difficulty.Soldier);
            Log.Debug(Log.LogCategory.Input,
                $"MainMenu: {GameSettings.PlayerConfig[slot].Name} difficulty reset to "
                + "Soldier (Computer slot)");
        }
        difficultyDropdown.Disabled = isComputer;
    }

    /// <summary>Select the OptionButton entry whose item ID matches
    /// <paramref name="id"/>. Selected is an index, so callers can't
    /// assume item order == id order.</summary>
    private static void SelectItemById(OptionButton button, int id)
    {
        for (int item = 0; item < button.ItemCount; item++)
        {
            if (button.GetItemId(item) == id)
            {
                button.Selected = item;
                return;
            }
        }
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

    /// <summary>Build the campaign screen for the current orientation and
    /// wire its navigation. Hidden until <see cref="ShowCampaign"/>.</summary>
    private CampaignPanel BuildCampaignPanel()
    {
        Vector2 viewportSize = GetViewportRect().Size;
        _campaignOrientation = ScreenLayout.Resolve(viewportSize.X, viewportSize.Y);
        var panel = new CampaignPanel(_campaignOrientation) { Visible = false };
        panel.BackPressed += ShowLanding;
        panel.LevelTapped += OnCampaignLevelTapped;
        return panel;
    }

    private void OnCampaignPressed()
    {
        ShowCampaign();
    }

    private void ShowCampaign()
    {
        if (_landingPanel != null) _landingPanel.Visible = false;
        if (_playConfigPanel != null) _playConfigPanel.Visible = false;
        if (_campaignPanel != null)
        {
            _campaignPanel.Refresh();
            _campaignPanel.Visible = true;
        }
        Log.Info(Log.LogCategory.Campaign, "MainMenu: campaign screen opened");
    }

    /// <summary>Tap on a campaign hex: open the confirmation sheet
    /// (level number, tier, current status, Play / Cancel). A fresh
    /// modal per tap — content is level-specific and the modal family
    /// builds its UI once in _Ready.</summary>
    private void OnCampaignLevelTapped(int level)
    {
        string status = CampaignStore.Progress.StatusOf(level) switch
        {
            CampaignLevelStatus.Won => "Already won — replaying can't lose it.",
            CampaignLevelStatus.Lost => "Attempted, not yet won.",
            _ => "Not yet attempted.",
        };
        var sheet = new ConfirmModal(
            $"Level {CampaignProgress.LabelFor(level)}",
            $"{CampaignProgress.DifficultyForLevel(level)} tier · {status}",
            "Play");
        sheet.Confirmed += () => LaunchCampaignLevel(level);
        sheet.Canceled += sheet.QueueFree;
        AddChild(sheet);
        sheet.Open();
    }

    /// <summary>Launch a campaign level: pin the master seed (identity:
    /// seed = level), lock the roster to 1 Human + 5 Computer with the
    /// human's handicap set to the tier difficulty (AIs stay Soldier),
    /// mark the level attempted (Untried → Lost until won — abandon and
    /// crash safe), and swap to the game scene.</summary>
    private void LaunchCampaignLevel(int level)
    {
        GameSettings.CampaignLevel = level;
        GameSettings.MasterSeed = CampaignProgress.SeedForLevel(level);
        for (int i = 0; i < GameSettings.PlayerKinds.Length; i++)
        {
            GameSettings.PlayerKinds[i] = i == 0 ? PlayerKind.Human : PlayerKind.Computer;
            GameSettings.Difficulties[i] = i == 0
                ? CampaignProgress.DifficultyForLevel(level)
                : Difficulty.Soldier;
        }
        // A stale starting-map handoff from an earlier Load/Play flow
        // would override procedural generation — campaign maps are
        // always seed-generated.
        LoadRequest.Pending = null;
        CampaignStore.MarkAttempted(level);
        Log.Info(Log.LogCategory.Campaign,
            $"MainMenu: launching campaign level {CampaignProgress.LabelFor(level)} " +
            $"(seed {GameSettings.MasterSeed}, human difficulty {GameSettings.Difficulties[0]})");
        GetTree().ChangeSceneToFile("res://scenes/main.tscn");
    }

    private void ShowLanding()
    {
        if (_landingPanel != null) _landingPanel.Visible = true;
        if (_playConfigPanel != null) _playConfigPanel.Visible = false;
        if (_campaignPanel != null) _campaignPanel.Visible = false;
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
            // is focused.
            _seedField?.AcceptEvent();
            if (_mobileSeedFieldBehavior)
            {
                // Mobile: Return dismisses the on-screen keyboard and stays
                // on the config screen so the rest of the settings remain
                // adjustable (issue #4).
                Log.Debug(Log.LogCategory.Input,
                    "MainMenu: seed-field Return (mobile) -> dismiss keyboard");
                _seedField?.ReleaseFocus();
            }
            else
            {
                // Desktop: mirror the unfocused behavior — start the game.
                Log.Debug(Log.LogCategory.Input,
                    "MainMenu: seed-field Enter (desktop) -> start game");
                OnStartPressed();
            }
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

    private void OnSeedFieldFocusEntered()
    {
        Log.Debug(Log.LogCategory.Display,
            "MainMenu: seed field focused; keyboard-lift polling on");
        // Poll per-frame while focused: the on-screen keyboard animates in
        // and Godot has no keyboard-height-changed signal.
        SetProcess(true);
        SetProcessInput(true);
    }

    private void OnSeedFieldFocusExited()
    {
        Log.Debug(Log.LogCategory.Display,
            "MainMenu: seed field unfocused; keyboard-lift polling off");
        SetProcess(false);
        SetProcessInput(false);
        ApplyKeyboardLift(0f);
    }

    public override void _Process(double delta)
    {
        UpdateKeyboardLift();
    }

    /// <summary>A press that lands outside the focused seed field dismisses
    /// the on-screen keyboard (issue #4). Runs in _Input because the root
    /// Control and panels have MouseFilter.Stop, so an outside tap never
    /// reaches _UnhandledInput. The event is NOT consumed — the tap still
    /// activates whatever control it landed on.</summary>
    public override void _Input(InputEvent @event)
    {
        if (_seedField == null || !_seedField.HasFocus()) return;
        Vector2? pressPosition = @event switch
        {
            InputEventMouseButton { Pressed: true } mouse => mouse.Position,
            InputEventScreenTouch { Pressed: true } touch => touch.Position,
            _ => null,
        };
        if (pressPosition == null) return;
        if (_seedField.GetGlobalRect().HasPoint(pressPosition.Value)) return;
        Log.Debug(Log.LogCategory.Input,
            "MainMenu: tap outside seed field -> dismiss keyboard");
        _seedField.ReleaseFocus();
    }

    private void UpdateKeyboardLift()
    {
        if (_seedField == null || _playConfigPanel == null) return;
        float physicalHeight = _fakeKeyboardPhysicalHeight > 0f
            ? _fakeKeyboardPhysicalHeight
            : DisplayServer.VirtualKeyboardGetHeight();
        float scaleFactor = GetWindow().ContentScaleFactor;
        float logicalHeight = scaleFactor > 0f ? physicalHeight / scaleFactor : physicalHeight;
        // Measure the field's unlifted bottom edge (add back the applied
        // lift) so the lift doesn't feed back into its own input.
        float fieldBottomY = _seedField.GetGlobalRect().End.Y + _keyboardLift;
        float lift = KeyboardAvoidance.LiftFor(
            fieldBottomY, GetViewportRect().Size.Y, logicalHeight, KeyboardLiftMargin);
        ApplyKeyboardLift(lift);
    }

    /// <summary>Translate the center-anchored play-config panel up by
    /// <paramref name="lift"/> logical px via its anchor offsets. FitPanels
    /// only touches Scale/PivotOffset, so the two never fight; a viewport
    /// resize re-solves the anchors against these offsets and the next
    /// _Process frame re-derives the lift.</summary>
    private void ApplyKeyboardLift(float lift)
    {
        if (_playConfigPanel == null) return;
        if (Mathf.IsEqualApprox(lift, _keyboardLift)) return;
        float halfH = _playConfigDesignSize.Y * 0.5f;
        _playConfigPanel.OffsetTop = -halfH - lift;
        _playConfigPanel.OffsetBottom = halfH - lift;
        _keyboardLift = lift;
        Log.Debug(Log.LogCategory.Display,
            $"MainMenu: keyboard lift -> {lift:0.#} logical px");
    }

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
                GameSettings.Difficulties[i] = loaded.Players[i].Difficulty;
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
                GameSettings.Difficulties[i] = loaded.Players[i].Difficulty;
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
        // Freeform games never record campaign results — clear any
        // leftover campaign context from a prior launch (issue #2).
        GameSettings.CampaignLevel = null;

        // Persist the dropdown selections — kinds and per-slot difficulty
        // always come from this panel (saved maps don't carry per-color
        // roles). Difficulty is stored per-slot (issue #38) so mixed
        // configurations land directly on the roster and round-trip
        // through the save.
        for (int i = 0; i < _roleButtons.Length; i++)
        {
            int selectedId = _roleButtons[i].GetSelectedId();
            GameSettings.PlayerKinds[i] = selectedId == ComputerId
                ? PlayerKind.Computer
                : PlayerKind.Human;
            GameSettings.Difficulties[i] = (Difficulty)_difficultyButtons[i].GetSelectedId();
        }
        Log.Info(Log.LogCategory.Input,
            "MainMenu: start — " + string.Join(", ",
                GameSettings.PlayerConfig.Select((config, i) =>
                    $"{config.Name}={GameSettings.PlayerKinds[i]}/{GameSettings.Difficulties[i]}")));

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
