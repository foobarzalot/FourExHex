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

    // The master seed is a full 32-bit value entered as 8 hex digits.
    private const int SeedHexDigits = 8;

    /// <summary>One-shot handoff: when true, the menu opens straight to
    /// the campaign screen instead of the landing page (set by
    /// <see cref="Main"/>'s "Back to campaign" path so the player returns
    /// to the refreshed ladder, not the landing buttons). Mirrors the
    /// <see cref="LoadRequest.Pending"/> cross-scene handoff pattern.</summary>
    public static bool OpenCampaignOnArrival;

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
    // Orientation the landing panel was last built for. Portrait keeps the
    // original button-stack panel (ScaleToFit shrinks it); landscape uses the
    // "split hero" fill layout (issue #34). A resize that flips it rebuilds.
    private ScreenOrientation _landingOrientation = ScreenOrientation.Landscape;
    // Centered fill surfaces of the landscape menu panels (null when that panel
    // is built portrait); OnMenuSafeAreaChanged / FitPanels keep them centered +
    // size-capped against the current viewport / safe area.
    private PanelContainer? _landingSurface;
    private PanelContainer? _playConfigSurface;

    private LineEdit? _seedField;
    private Button? _startButton;
    private OptionButton? _mapSelector;
    private HudIconButton? _rerollButton;
    private MapThumbnailView? _thumbnail;

    // The New Game flow is split into two pages (issue #40): player setup
    // (role + difficulty per slot) and map setup (map selector, seed +
    // re-roll, and a live thumbnail of the board the selection produces).
    // Both page contents are built up front and parented to the play-config
    // panel; navigation toggles their visibility (so selections survive paging
    // back and forth). _playConfigPage persists across the orientation-flip
    // rebuild so a flip keeps you on the same page.
    private enum PlayConfigPage { PlayerSetup, MapSetup }
    private PlayConfigPage _playConfigPage = PlayConfigPage.PlayerSetup;
    private Control? _playerPageContent;
    private Control? _mapPageContent;
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

        if (OpenCampaignOnArrival)
        {
            OpenCampaignOnArrival = false;
            ShowCampaign();
        }
        else
        {
            ShowLanding();
        }

        // Shrink the fixed-size panels to fit windows shorter/narrower than
        // their design size (e.g. 720p), and keep fitting them on resize.
        FitPanels();
        GetViewport().SizeChanged += FitPanels;
        _viewportResizeHooked = true;
        // The landscape fill panels (landing now; play-config in #34) inset to
        // the device safe area — keep them clear of the notch / home indicator
        // when it shifts without a resize.
        SafeArea.Changed += OnMenuSafeAreaChanged;
    }

    public override void _ExitTree()
    {
        SafeArea.Changed -= OnMenuSafeAreaChanged;
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
        RebuildLandingOnOrientationFlip(viewport);
        RebuildPlayConfigOnOrientationFlip(viewport);
        RebuildCampaignOnOrientationFlip(viewport);
        // Portrait panels are fixed-size and ScaleToFit shrinks them to fit a
        // smaller viewport; the landscape "split hero" landing instead fills
        // the safe rect, so it only needs its insets refreshed (issue #34).
        if (_landingPanel != null)
        {
            if (_landingOrientation == ScreenOrientation.Portrait)
                ScaleToFit(_landingPanel, _landingDesignSize, viewport);
            else if (_landingSurface != null)
                LandscapeMenuChrome.ApplyLayout(_landingSurface, viewport, SafeArea.Current);
        }
        if (_playConfigPanel != null)
        {
            if (_playConfigOrientation == ScreenOrientation.Portrait)
                ScaleToFit(_playConfigPanel, _playConfigDesignSize, viewport);
            else if (_playConfigSurface != null)
                // Carry any active seed-field keyboard lift so a resize while the
                // on-screen keyboard is up doesn't snap the panel back down.
                LandscapeMenuChrome.ApplyLayout(_playConfigSurface, viewport, SafeArea.Current,
                    verticalShift: _keyboardLift);
        }
        // The campaign panel is NOT scaled — it fills the viewport and
        // scrolls (anchors re-solve on resize on their own; an orientation
        // flip rebuilds it via RebuildCampaignOnOrientationFlip below).
    }

    /// <summary>Re-apply safe-area insets to the landscape fill panels when the
    /// notch / home indicator shifts without a resize (rotation, status-bar
    /// toggle). Portrait panels ignore this — FitPanels scales them instead.</summary>
    private void OnMenuSafeAreaChanged(LogicalSafeInsets s)
    {
        Vector2 viewport = GetViewportRect().Size;
        if (_landingSurface != null) LandscapeMenuChrome.ApplyLayout(_landingSurface, viewport, s);
        if (_playConfigSurface != null)
            LandscapeMenuChrome.ApplyLayout(_playConfigSurface, viewport, s, verticalShift: _keyboardLift);
    }

    /// <summary>Rebuild the landing panel when a viewport resize flips the
    /// orientation: portrait button-stack ↔ landscape "split hero" (issue #34).
    /// Save-state button gating is re-derived from disk on every build, so a
    /// rebuild loses nothing.</summary>
    private void RebuildLandingOnOrientationFlip(Vector2 viewport)
    {
        if (_landingPanel == null) return;
        ScreenOrientation next = ScreenLayout.Resolve(viewport.X, viewport.Y);
        if (next == _landingOrientation) return;
        Log.Debug(Log.LogCategory.Render,
            $"MainMenu: orientation flip {_landingOrientation} -> {next}; rebuilding landing panel");

        bool wasVisible = _landingPanel.Visible;
        Control old = _landingPanel;
        int treeIndex = old.GetIndex();
        old.Visible = false;
        old.QueueFree();

        _landingPanel = BuildLandingPanel();
        AddChild(_landingPanel);
        MoveChild(_landingPanel, treeIndex);
        _landingPanel.Visible = wasVisible;
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
        // _playConfigPage persists across the rebuild; restore the matching page
        // visibility and re-render the thumbnail if the map page is showing.
        ShowCurrentPlayConfigPage();
        RefreshThumbnail();
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
        Vector2 landingViewport = GetViewportRect().Size;
        _landingOrientation = ScreenLayout.Resolve(landingViewport.X, landingViewport.Y);
        if (_landingOrientation == ScreenOrientation.Landscape)
        {
            return BuildLandingPanelLandscape();
        }
        // Portrait: the original fixed-size button-stack panel below.
        _landingSurface = null;

        const float panelW = 520f;
        // Button-stack layout: a 64px button plus a 16px gap per slot,
        // starting at y=140. Hoisted above panelH so the panel height can be
        // derived from the count of buttons actually rendered.
        const float buttonH = 64f;
        const float buttonGap = 16f;
        const float firstButtonY = 140f;

        // The full stack is Resume, Play, Campaign, Play Tutorial, Load, Map
        // Editor, Settings, and Exit (8 buttons; the Tutorial Builder entry
        // moved to the debug-only cheat menu, issue #7). On mobile the Exit
        // button is suppressed (Apple HIG / Google Play guidance), so the
        // panel reclaims its slot — height is computed from the actual count
        // rather than the fixed 8-button design, otherwise ScaleToFit centers
        // against a phantom Exit slot, leaving dead space at the bottom (#42).
        bool exitSuppressed = OS.HasFeature("mobile");
        int landingButtonCount = exitSuppressed ? 7 : 8;
        const float bottomMargin = 56f; // matches the desktop 8-button design (820 - 764)
        float panelH = firstButtonY + (buttonH + buttonGap) * (landingButtonCount - 1)
                       + buttonH + bottomMargin;
        Log.Info(Log.LogCategory.Render,
            $"MainMenu: landing panel sized for {landingButtonCount} buttons "
            + $"(panelH={panelH}, exitSuppressed={exitSuppressed}).");
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

        const float buttonInset = 80f;
        const float buttonW = panelW - buttonInset * 2f;

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
        if (!exitSuppressed)
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

    /// <summary>"Split hero" landscape landing (issue #34): a wordmark + version
    /// rail on the left, a full-width Play Game over a 2-column action grid on
    /// the right, filling the safe rect instead of downscaling the portrait
    /// button stack. Exit (desktop only) is a full-width button below the grid.</summary>
    private Control BuildLandingPanelLandscape()
    {
        PanelContainer surface = LandscapeMenuChrome.Build();
        _landingSurface = surface;

        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 20);
        surface.AddChild(hbox);

        // Left rail: wordmark + gold underline vertically centered, version
        // string pinned to the bottom.
        var leftCol = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(300, 0),
            SizeFlagsVertical = Control.SizeFlags.Fill,
        };
        leftCol.AddThemeConstantOverride("separation", 6);
        leftCol.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        // Single-line wordmark, centered in the rail, at the portrait font size.
        var wordmark = new Label { Text = "FourExHex", HorizontalAlignment = HorizontalAlignment.Center };
        wordmark.AddThemeFontOverride("font", SerifFont);
        wordmark.AddThemeFontSizeOverride("font_size", 56);
        leftCol.AddChild(wordmark);
        // Gold underline — extended and centered beneath the wordmark.
        leftCol.AddChild(new ColorRect
        {
            Color = UiPalette.Gold,
            CustomMinimumSize = new Vector2(220, 3),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        });
        leftCol.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        var version = new Label { Text = AppVersion.Display };
        version.AddThemeFontSizeOverride("font_size", 15);
        version.AddThemeColorOverride("font_color", UiPalette.InkMute);
        leftCol.AddChild(version);
        hbox.AddChild(leftCol);

        // Hairline divider between the rail and the action column.
        hbox.AddChild(new ColorRect
        {
            Color = UiPalette.LineSoft,
            CustomMinimumSize = new Vector2(1, 0),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        });

        var rightCol = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.Fill,
        };
        rightCol.AddThemeConstantOverride("separation", 11);
        hbox.AddChild(rightCol);

        // Play Game sits on top, full width; the grid fills the rest.
        _landingPlayButton = MakeLandingButton("Play Game", OnPlayPressed, 26);
        _landingPlayButton.CustomMinimumSize = new Vector2(0, 62);
        rightCol.AddChild(_landingPlayButton);

        var grid = new GridContainer
        {
            Columns = 2,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        grid.AddThemeConstantOverride("h_separation", 11);
        grid.AddThemeConstantOverride("v_separation", 11);
        rightCol.AddChild(grid);

        // One ListSlots() call drives both Resume (needs the autosave entry)
        // and Load Game (needs any slot), matching the portrait build.
        System.Collections.Generic.IReadOnlyList<SaveSlotInfo> slots = _saveStore.ListSlots();

        // Resume / Load render disabled but keep their grid slots so the grid
        // never reflows when a save exists (design handoff).
        _landingResumeButton = MakeGridButton("Resume", OnResumePressed);
        _landingResumeButton.Disabled = !slots.Any(s => s.IsAutosave);
        grid.AddChild(_landingResumeButton);
        grid.AddChild(MakeGridButton("Campaign", OnCampaignPressed));
        grid.AddChild(MakeGridButton("Play Tutorial", OnPlayTutorialPressed));
        _landingLoadButton = MakeGridButton("Load Game", OnLoadPressed);
        _landingLoadButton.Disabled = slots.Count == 0;
        grid.AddChild(_landingLoadButton);
        grid.AddChild(MakeGridButton("Map Editor", OnMapEditorPressed));
        grid.AddChild(MakeGridButton("Settings", OnSettingsPressed));

        // Exit is suppressed on mobile (Apple HIG / Google Play); desktop gets
        // a full-width Exit below the grid (per the #34 layout decision).
        bool exitSuppressed = OS.HasFeature("mobile");
        if (!exitSuppressed)
        {
            Button exitButton = MakeLandingButton("Exit", OnExitPressed, 26);
            exitButton.CustomMinimumSize = new Vector2(0, 52);
            rightCol.AddChild(exitButton);
        }

        LandscapeMenuChrome.ApplyLayout(surface, GetViewportRect().Size, SafeArea.Current);
        Log.Info(Log.LogCategory.Render,
            $"MainMenu: landing built (Landscape split-hero, exitSuppressed={exitSuppressed})");
        return surface;
    }

    /// <summary>Full-width landing action button (Play Game / Exit).</summary>
    private static Button MakeLandingButton(string text, System.Action onPressed, int fontSize)
    {
        var button = new Button
        {
            Text = text,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        button.AddThemeFontSizeOverride("font_size", fontSize);
        button.Pressed += onPressed;
        AudioBus.AttachClick(button);
        return button;
    }

    /// <summary>Grid action button that expands to fill its cell.</summary>
    private static Button MakeGridButton(string text, System.Action onPressed)
    {
        var button = new Button
        {
            Text = text,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        button.AddThemeFontSizeOverride("font_size", 24);
        button.Pressed += onPressed;
        AudioBus.AttachClick(button);
        return button;
    }

    private Control BuildPlayConfigPanel()
    {
        Vector2 viewportSize = GetViewportRect().Size;
        _playConfigOrientation = ScreenLayout.Resolve(viewportSize.X, viewportSize.Y);
        if (_playConfigOrientation == ScreenOrientation.Landscape)
        {
            return BuildPlayConfigPanelLandscape();
        }
        _playConfigSurface = null;

        // Portrait: a centered fixed-size panel holding two pages (issue #40) —
        // player setup and map setup — toggled by ShowCurrentPlayConfigPage.
        // ScaleToFit shrinks it for a short viewport; FitPanels rebuilds it into
        // the landscape layout when a resize flips orientation (#34).
        const float panelW = 624f;
        const float panelH = 1100f;
        // Center-anchored so Godot re-solves the position on every window
        // resize (matches ModalChrome.BuildCenteredPanel).
        var panel = new Panel
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = -panelW * 0.5f, OffsetRight = panelW * 0.5f,
            OffsetTop = -panelH * 0.5f, OffsetBottom = panelH * 0.5f,
            GrowHorizontal = GrowDirection.Both,
            GrowVertical = GrowDirection.Both,
        };
        Log.Trace(Log.LogCategory.Render, "MainMenu: built play-config panel (center-anchored).");

        _playerPageContent = BuildPortraitPlayerPage(panelW, panelH);
        panel.AddChild(_playerPageContent);
        _mapPageContent = BuildPortraitMapPage(panelW, panelH);
        panel.AddChild(_mapPageContent);

        RefreshStartButtonGating();
        ShowCurrentPlayConfigPage();

        Log.Debug(Log.LogCategory.Render,
            "MainMenu: play-config built (Portrait, paged player/map setup)");
        _playConfigDesignSize = new Vector2(panelW, panelH);
        return panel;
    }

    // Shared portrait page metrics.
    private const float PortraitPanelInset = 48f;
    private const float PortraitDropdownW = 240f;
    private const float PortraitTitleY = 34f;
    private const float PortraitContentTop = 154f;
    private const float PortraitButtonH = 62f;

    /// <summary>A "New Game" wordmark + gold rule at the top of a portrait
    /// page, parented to <paramref name="page"/>.</summary>
    private void AddPortraitHeader(Control page, float panelW)
    {
        var title = new Label
        {
            Text = "New Game",
            HorizontalAlignment = HorizontalAlignment.Center,
            Position = new Vector2(0, PortraitTitleY),
            Size = new Vector2(panelW, 68),
        };
        title.AddThemeFontOverride("font", SerifFont);
        title.AddThemeFontSizeOverride("font_size", 50);
        page.AddChild(title);
        page.AddChild(new ColorRect
        {
            Color = UiPalette.GoldDim,
            Position = new Vector2(panelW * 0.5f - 132f, 115f),
            Size = new Vector2(264f, 1f),
        });
    }

    /// <summary>Full-page Control sized to the panel; children use panel-local
    /// (absolute) coordinates.</summary>
    private static Control NewFullPage()
    {
        var page = new Control();
        page.SetAnchorsPreset(LayoutPreset.FullRect);
        return page;
    }

    /// <summary>Portrait player-setup page: the six role + difficulty rows, with
    /// Back (→ landing) and Next (→ map setup) pinned at the bottom.</summary>
    private Control BuildPortraitPlayerPage(float panelW, float panelH)
    {
        Control page = NewFullPage();
        AddPortraitHeader(page, panelW);

        const float rowHeight = 62f;
        const float swatchSize = 34f;
        const float nameWidth = 144f;
        const float headerGap = 12f;
        float rightColX = panelW - PortraitPanelInset - PortraitDropdownW;
        float playerBlockH = rowHeight + 50f; // kind + difficulty sub-row

        // Player rows: Swatch | Name | Kind | Difficulty (issue #38). Computer
        // slots lock difficulty to Soldier (see ApplyDifficultyLock).
        for (int i = 0; i < GameSettings.PlayerConfig.Length; i++)
        {
            (string name, string hex) = GameSettings.PlayerConfig[i];
            float rowY = PortraitContentTop + playerBlockH * i;

            page.AddChild(new ColorRect
            {
                Color = new Color(hex),
                Position = new Vector2(PortraitPanelInset, rowY + (rowHeight - swatchSize) * 0.5f),
                Size = new Vector2(swatchSize, swatchSize),
            });

            var nameLabel = new Label
            {
                Text = name,
                Position = new Vector2(PortraitPanelInset + swatchSize + 19f, rowY + 17f),
                Size = new Vector2(nameWidth, 29f),
            };
            nameLabel.AddThemeFontSizeOverride("font_size", 24);
            page.AddChild(nameLabel);

            float nameEndX = PortraitPanelInset + swatchSize + 19f + nameWidth;
            float labelRightX = rightColX - headerGap;
            page.AddChild(MakeFieldHeader("Type",
                new Vector2(nameEndX, rowY + 20f),
                new Vector2(labelRightX - nameEndX, 22f),
                HorizontalAlignment.Right));
            page.AddChild(MakeFieldHeader("Difficulty",
                new Vector2(PortraitPanelInset + swatchSize + 19f, rowY + rowHeight + 8f),
                new Vector2(labelRightX - (PortraitPanelInset + swatchSize + 19f), 22f),
                HorizontalAlignment.Right));

            OptionButton dropdown = ConfigureRoleDropdown(i);
            dropdown.Position = new Vector2(rightColX, rowY + 12f);
            dropdown.Size = new Vector2(PortraitDropdownW, 38f);
            page.AddChild(dropdown);

            OptionButton difficultyDropdown = ConfigureDifficultyDropdown(i);
            difficultyDropdown.Position = new Vector2(rightColX, rowY + rowHeight);
            difficultyDropdown.Size = new Vector2(PortraitDropdownW, 38f);
            page.AddChild(difficultyDropdown);

            ApplyDifficultyLock(i);
        }

        float buttonRowY = panelH - PortraitButtonH - 43f;
        float leftColW = swatchSize + 16f + nameWidth;
        page.AddChild(MakePortraitNavButton("Back", PortraitPanelInset, buttonRowY,
            leftColW, OnBackPressed));
        page.AddChild(MakePortraitNavButton("Next", rightColX, buttonRowY,
            PortraitDropdownW, GoToMapPage));
        return page;
    }

    /// <summary>Portrait map-setup page: map selector, seed + re-roll, a live
    /// board thumbnail, and Back (→ player setup) / Start Game at the bottom.</summary>
    private Control BuildPortraitMapPage(float panelW, float panelH)
    {
        Control page = NewFullPage();
        AddPortraitHeader(page, panelW);

        const float rowHeight = 62f;
        const float swatchSize = 34f;
        const float nameWidth = 144f;
        float leftLabelX = PortraitPanelInset + swatchSize + 19f;
        float rightColX = panelW - PortraitPanelInset - PortraitDropdownW;

        float mapRowY = PortraitContentTop;
        var mapLabel = new Label
        {
            Text = "Map",
            Position = new Vector2(leftLabelX, mapRowY + 17f),
            Size = new Vector2(nameWidth, 29f),
        };
        mapLabel.AddThemeFontSizeOverride("font_size", 24);
        page.AddChild(mapLabel);

        OptionButton mapSelector = ConfigureMapSelector();
        mapSelector.Position = new Vector2(rightColX, mapRowY + 12f);
        mapSelector.Size = new Vector2(PortraitDropdownW, 38f);
        page.AddChild(mapSelector);

        float seedRowY = mapRowY + rowHeight;
        var seedLabel = new Label
        {
            Text = "Map Seed",
            Position = new Vector2(leftLabelX, seedRowY + 17f),
            Size = new Vector2(nameWidth, 29f),
        };
        seedLabel.AddThemeFontSizeOverride("font_size", 24);
        page.AddChild(seedLabel);

        // Seed field shares its column with a square re-roll button (issue #5).
        const float rerollSize = 38f;
        const float rerollGap = 6f;
        float seedFieldW = PortraitDropdownW - rerollSize - rerollGap;
        LineEdit seedField = ConfigureSeedField();
        seedField.Position = new Vector2(rightColX, seedRowY + 12f);
        seedField.Size = new Vector2(seedFieldW, 38f);
        page.AddChild(seedField);

        _rerollButton = MakeRerollButton();
        // HudIconButton defaults to a 68×68 minimum; override it so the die
        // button shrinks to match the 38px seed field instead of towering over
        // it (issue: portrait die looked oversized).
        _rerollButton.CustomMinimumSize = new Vector2(rerollSize, rerollSize);
        _rerollButton.Position = new Vector2(rightColX + seedFieldW + rerollGap, seedRowY + 12f);
        _rerollButton.Size = new Vector2(rerollSize, rerollSize);
        page.AddChild(_rerollButton);

        // Live board thumbnail (issue #40): fills the tall mid-page space so the
        // rotated portrait board reads at a usable size.
        float thumbTop = seedRowY + rowHeight + 20f;
        float buttonRowY = panelH - PortraitButtonH - 43f;
        _thumbnail = BuildThumbnail();
        _thumbnail.Position = new Vector2(PortraitPanelInset, thumbTop);
        _thumbnail.Size = new Vector2(panelW - 2f * PortraitPanelInset, buttonRowY - thumbTop - 24f);
        page.AddChild(_thumbnail);

        float leftColW = swatchSize + 16f + nameWidth;
        page.AddChild(MakePortraitNavButton("Back", PortraitPanelInset, buttonRowY,
            leftColW, GoToPlayerPage));
        _startButton = MakePortraitNavButton("Start Game", rightColX, buttonRowY,
            PortraitDropdownW, OnStartPressed);
        page.AddChild(_startButton);
        return page;
    }

    private Button MakePortraitNavButton(
        string text, float x, float y, float width, System.Action onPressed)
    {
        var button = new Button { Text = text };
        button.AddThemeFontSizeOverride("font_size", 29);
        button.Position = new Vector2(x, y);
        button.Size = new Vector2(width, PortraitButtonH);
        button.Pressed += onPressed;
        AudioBus.AttachClick(button);
        return button;
    }

    // --- Shared play-config control factories (portrait + landscape) ---
    // Each configures the control + wiring (items, selection, events) and
    // stores it in its field; the caller positions / parents it.

    private OptionButton ConfigureRoleDropdown(int slot)
    {
        var dropdown = new OptionButton();
        dropdown.AddThemeFontSizeOverride("font_size", 21);
        // The button face and its drop-down popup are themed separately;
        // without this the expanded item list renders at the tiny default size.
        dropdown.GetPopup().AddThemeFontSizeOverride("font_size", 21);
        dropdown.AddItem("Human", HumanId);
        dropdown.AddItem("Computer", ComputerId);
        PlayerKind currentKind = slot < GameSettings.PlayerKinds.Length
            ? GameSettings.PlayerKinds[slot]
            : PlayerKind.Computer;
        SelectItemById(dropdown, currentKind == PlayerKind.Computer ? ComputerId : HumanId);
        _roleButtons[slot] = dropdown;
        // Lock the difficulty dropdown whenever the row is a Computer slot.
        dropdown.ItemSelected += _ => ApplyDifficultyLock(slot);
        return dropdown;
    }

    private OptionButton ConfigureDifficultyDropdown(int slot)
    {
        var dropdown = new OptionButton();
        dropdown.AddThemeFontSizeOverride("font_size", 21);
        dropdown.GetPopup().AddThemeFontSizeOverride("font_size", 21);
        dropdown.AddItem("Recruit", (int)Difficulty.Recruit);
        dropdown.AddItem("Soldier", (int)Difficulty.Soldier);
        dropdown.AddItem("Captain", (int)Difficulty.Captain);
        dropdown.AddItem("Commander", (int)Difficulty.Commander);
        // Initialize from GameSettings (mirrors the kind dropdown) so loaded
        // saves / Play Again round-trip per-slot levels.
        Difficulty currentDifficulty = slot < GameSettings.Difficulties.Length
            ? GameSettings.Difficulties[slot]
            : Difficulty.Soldier;
        SelectItemById(dropdown, (int)currentDifficulty);
        _difficultyButtons[slot] = dropdown;
        return dropdown;
    }

    private OptionButton ConfigureMapSelector()
    {
        var selector = new OptionButton();
        selector.AddThemeFontSizeOverride("font_size", 21);
        selector.GetPopup().AddThemeFontSizeOverride("font_size", 21);
        // Item 0 is the default — generates a fresh procedural map from the
        // seed. Subsequent items (id == index in ListMaps) are saved maps.
        selector.AddItem("Random Map", 0);
        System.Collections.Generic.IReadOnlyList<SaveSlotInfo> mapSlots = _saveStore.ListMaps();
        for (int i = 0; i < mapSlots.Count; i++)
        {
            selector.AddItem(mapSlots[i].SlotName, i + 1);
        }
        selector.Selected = 0;
        selector.ItemSelected += OnMapSelectorChanged;
        _mapSelector = selector;
        return selector;
    }

    private LineEdit ConfigureSeedField()
    {
        var field = new LineEdit
        {
            MaxLength = SeedHexDigits,
            Alignment = HorizontalAlignment.Right,
            Text = SeedFormat.ToHex(SeedFormat.NextSeed(new System.Random())),
            // Tapping/clicking into the field selects the existing seed so the
            // next keystroke replaces it (issue #4).
            SelectAllOnFocus = true,
        };
        field.AddThemeFontSizeOverride("font_size", 21);
        field.TextChanged += OnSeedTextChanged;
        field.FocusEntered += OnSeedFieldFocusEntered;
        field.FocusExited += OnSeedFieldFocusExited;
        // Intercept = / - even when the LineEdit has focus so the hotkey is
        // focus-agnostic. Without GuiInput here, the LineEdit would consume
        // printable-key events before _UnhandledInput sees them.
        field.GuiInput += OnSeedFieldGuiInput;
        _seedField = field;
        return field;
    }

    /// <summary>"Config rail + player list" landscape New Game (issue #34): a
    /// fixed left rail (title, map, seed, Start, Back) beside a scrolling player
    /// list (swatch | name | Type | Difficulty per row), filling a centered,
    /// size-capped surface instead of the portrait stack.</summary>
    private Control BuildPlayConfigPanelLandscape()
    {
        PanelContainer surface = LandscapeMenuChrome.Build();
        _playConfigSurface = surface;

        // Both pages fill the surface (PanelContainer fits every child to its
        // content rect); ShowCurrentPlayConfigPage toggles which is visible.
        _playerPageContent = BuildLandscapePlayerPage();
        surface.AddChild(_playerPageContent);
        _mapPageContent = BuildLandscapeMapPage();
        surface.AddChild(_mapPageContent);

        RefreshStartButtonGating();
        ShowCurrentPlayConfigPage();
        LandscapeMenuChrome.ApplyLayout(surface, GetViewportRect().Size, SafeArea.Current);
        Log.Debug(Log.LogCategory.Render, "MainMenu: play-config built (Landscape, paged player/map setup)");
        return surface;
    }

    /// <summary>Landscape title + gold underline at the top of a page.</summary>
    private void AddLandscapeHeader(BoxContainer page)
    {
        var title = new Label { Text = "New Game", HorizontalAlignment = HorizontalAlignment.Left };
        title.AddThemeFontOverride("font", SerifFont);
        title.AddThemeFontSizeOverride("font_size", 38);
        page.AddChild(title);
        page.AddChild(new ColorRect
        {
            Color = UiPalette.Gold,
            CustomMinimumSize = new Vector2(90, 3),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
        });
    }

    /// <summary>Landscape player-setup page: the same rail + content split the
    /// map page (and the original single-panel layout) use — a fixed left rail
    /// holding the title and Next / Back, beside the full-height player list. The
    /// nav lives in the rail rather than stacked above/below the list, so the
    /// list keeps the whole surface height and doesn't scroll for the 6-player
    /// roster (the ScrollContainer only engages for a taller-than-surface list).</summary>
    private Control BuildLandscapePlayerPage()
    {
        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 20);

        var rail = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(262, 0),
            SizeFlagsVertical = Control.SizeFlags.Fill,
        };
        rail.AddThemeConstantOverride("separation", 8);
        hbox.AddChild(rail);

        AddLandscapeHeader(rail);
        rail.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        rail.AddChild(MakeLandscapeNavButton("Next", GoToMapPage));
        rail.AddChild(MakeLandscapeNavButton("Back", OnBackPressed));

        hbox.AddChild(new ColorRect
        {
            Color = UiPalette.LineSoft,
            CustomMinimumSize = new Vector2(1, 0),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        });

        var rightCol = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.Fill,
        };
        rightCol.AddThemeConstantOverride("separation", 6);
        hbox.AddChild(rightCol);

        rightCol.AddChild(MakePlayerColumnHeader());
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        rightCol.AddChild(scroll);
        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 9);
        scroll.AddChild(list);
        for (int i = 0; i < GameSettings.PlayerConfig.Length; i++)
        {
            list.AddChild(MakePlayerRow(i));
        }
        return hbox;
    }

    /// <summary>Landscape map-setup page: a left rail (map selector, seed +
    /// re-roll, Back / Start) beside a large live board thumbnail.</summary>
    private Control BuildLandscapeMapPage()
    {
        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 20);

        // Narrow rail (shifted left, controls compressed horizontally) so the
        // thumbnail gets most of the surface width — closer to the portrait
        // preview's size.
        var rail = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(190, 0),
            SizeFlagsVertical = Control.SizeFlags.Fill,
        };
        rail.AddThemeConstantOverride("separation", 8);
        hbox.AddChild(rail);

        AddLandscapeHeader(rail);

        rail.AddChild(MakeRailLabel("Map"));
        OptionButton mapSelector = ConfigureMapSelector();
        mapSelector.CustomMinimumSize = new Vector2(0, 44);
        rail.AddChild(mapSelector);

        rail.AddChild(MakeRailLabel("Map Seed"));
        // Seed field + square re-roll button on one row (issue #5).
        var seedRow = new HBoxContainer();
        seedRow.AddThemeConstantOverride("separation", 6);
        LineEdit seedField = ConfigureSeedField();
        seedField.CustomMinimumSize = new Vector2(0, 44);
        seedField.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        seedRow.AddChild(seedField);
        _rerollButton = MakeRerollButton();
        _rerollButton.CustomMinimumSize = new Vector2(44, 44);
        seedRow.AddChild(_rerollButton);
        rail.AddChild(seedRow);

        rail.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });

        _startButton = new Button { Text = "Start Game", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _startButton.AddThemeFontSizeOverride("font_size", 27);
        _startButton.CustomMinimumSize = new Vector2(0, 54);
        _startButton.Pressed += OnStartPressed;
        AudioBus.AttachClick(_startButton);
        rail.AddChild(_startButton);

        rail.AddChild(MakeLandscapeNavButton("Back", GoToPlayerPage));

        // Hairline divider between the rail and the thumbnail.
        hbox.AddChild(new ColorRect
        {
            Color = UiPalette.LineSoft,
            CustomMinimumSize = new Vector2(1, 0),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        });

        _thumbnail = BuildThumbnail();
        _thumbnail.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _thumbnail.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        hbox.AddChild(_thumbnail);
        return hbox;
    }

    private Button MakeLandscapeNavButton(string text, System.Action onPressed)
    {
        var button = new Button { Text = text, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        button.AddThemeFontSizeOverride("font_size", 24);
        button.CustomMinimumSize = new Vector2(0, 48);
        button.Pressed += onPressed;
        AudioBus.AttachClick(button);
        return button;
    }

    /// <summary>One landscape player row: swatch | name | Type | Difficulty.</summary>
    private HBoxContainer MakePlayerRow(int slot)
    {
        (string name, string hex) = GameSettings.PlayerConfig[slot];
        var row = new HBoxContainer { CustomMinimumSize = new Vector2(0, 44) };
        row.AddThemeConstantOverride("separation", 10);

        row.AddChild(new ColorRect
        {
            Color = new Color(hex),
            CustomMinimumSize = new Vector2(22, 22),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        });
        var nameLabel = new Label
        {
            Text = name,
            CustomMinimumSize = new Vector2(82, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        nameLabel.AddThemeFontSizeOverride("font_size", 22);
        row.AddChild(nameLabel);

        OptionButton role = ConfigureRoleDropdown(slot);
        role.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(role);
        OptionButton difficulty = ConfigureDifficultyDropdown(slot);
        difficulty.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(difficulty);

        ApplyDifficultyLock(slot);
        return row;
    }

    /// <summary>Header row for the landscape player list — spacers matching the
    /// swatch + name columns, then TYPE / DIFFICULTY over the dropdown columns.</summary>
    private HBoxContainer MakePlayerColumnHeader()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        row.AddChild(new Control { CustomMinimumSize = new Vector2(22, 0) });
        row.AddChild(new Control { CustomMinimumSize = new Vector2(82, 0) });
        Label type = MakeRailLabel("Type");
        type.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(type);
        Label difficulty = MakeRailLabel("Difficulty");
        difficulty.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(difficulty);
        return row;
    }

    /// <summary>Section label (Map / Map Seed / column headers) for the
    /// landscape config rail — title case, matching the portrait labels.</summary>
    private static Label MakeRailLabel(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", 18);
        label.AddThemeColorOverride("font_color", UiPalette.InkSoft);
        return label;
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
        RefreshThumbnail();
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
        // The seed is inert while a saved map drives the terrain — re-roll too.
        if (_rerollButton != null) _rerollButton.Disabled = !enabled;
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
        var panel = new CampaignPanel(_campaignOrientation, viewportSize) { Visible = false };
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

    /// <summary>Launch a campaign level: <see cref="CampaignStore.PrepareLaunch"/>
    /// pins the seed, locks the roster, and marks the level attempted
    /// (Untried → Lost until won — abandon and crash safe).</summary>
    private void LaunchCampaignLevel(int level)
    {
        CampaignStore.PrepareLaunch(level);
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
        // A fresh entry always starts on the player-setup page (selections are
        // preserved, but the flow begins at page 1).
        _playConfigPage = PlayConfigPage.PlayerSetup;
        ShowCurrentPlayConfigPage();
    }

    // --- Paged New Game navigation (issue #40) ---

    private void ShowCurrentPlayConfigPage()
    {
        if (_playerPageContent != null)
            _playerPageContent.Visible = _playConfigPage == PlayConfigPage.PlayerSetup;
        if (_mapPageContent != null)
            _mapPageContent.Visible = _playConfigPage == PlayConfigPage.MapSetup;
    }

    private void GoToMapPage()
    {
        _playConfigPage = PlayConfigPage.MapSetup;
        ShowCurrentPlayConfigPage();
        Log.Debug(Log.LogCategory.Input, "MainMenu: New Game → map setup page");
        RefreshThumbnail();
    }

    private void GoToPlayerPage()
    {
        _playConfigPage = PlayConfigPage.PlayerSetup;
        ShowCurrentPlayConfigPage();
        Log.Debug(Log.LogCategory.Input, "MainMenu: New Game → player setup page");
    }

    /// <summary>Re-render the live thumbnail from the current selection. No-op
    /// unless the map-setup page is showing (the seed field only lives there).</summary>
    private void RefreshThumbnail()
    {
        if (_thumbnail == null || _playConfigPage != PlayConfigPage.MapSetup) return;
        if (_selectedMapName != null) _thumbnail.RequestMap(_selectedMapName);
        else if (SeedFormat.TryParseHex(_seedField?.Text, out int seed)) _thumbnail.RequestRandom(seed);
    }

    private MapThumbnailView BuildThumbnail()
    {
        var thumb = new MapThumbnailView();
        thumb.SetSaveStore(_saveStore);
        return thumb;
    }

    /// <summary>Re-roll the seed in place (issue #5), modeled on the map
    /// editor's die button. Setting LineEdit.Text programmatically doesn't fire
    /// text_changed, so dependent state is refreshed explicitly.</summary>
    private HudIconButton MakeRerollButton()
    {
        var button = new HudIconButton(HudIcon.Die) { FocusMode = Control.FocusModeEnum.None };
        button.Pressed += OnReRollSeedPressed;
        AudioBus.AttachClick(button);
        return button;
    }

    private void OnReRollSeedPressed()
    {
        if (_seedField == null) return;
        int seed = SeedFormat.NextSeed(new System.Random());
        _seedField.Text = SeedFormat.ToHex(seed);
        _rerollButton?.FlashPress();
        Log.Debug(Log.LogCategory.Input, $"MainMenu: reroll seed={SeedFormat.ToHex(seed)}");
        RefreshStartButtonGating();
        RefreshThumbnail();
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
        // Strip any non-hex characters that slipped past MaxLength
        // (paste, IME, etc.), uppercase the rest, and keep the caret at
        // the same logical position so typing isn't disrupted.
        string filtered = new string(
            newText.Where(char.IsAsciiHexDigit).ToArray()).ToUpperInvariant();
        if (filtered != newText)
        {
            int caret = _seedField.CaretColumn;
            _seedField.Text = filtered;
            _seedField.CaretColumn = System.Math.Min(caret, filtered.Length);
        }
        RefreshStartButtonGating();
        RefreshThumbnail();
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
        SeedFormat.TryParseHex(_seedField.Text, out int current);
        // Full 32-bit range, so wrap naturally rather than clamp.
        int next = unchecked(current + delta);
        _seedField.Text = SeedFormat.ToHex(next);
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

    /// <summary>Translate the play-config panel up by <paramref name="lift"/>
    /// logical px so the on-screen keyboard never covers the seed field.
    /// Portrait shifts the fixed panel's anchor offsets (FitPanels only touches
    /// Scale, so they never fight); landscape re-lays out the centered fill
    /// surface with the lift applied (FitPanels / safe-area changes re-pass
    /// <see cref="_keyboardLift"/>, so they don't snap it back down).</summary>
    private void ApplyKeyboardLift(float lift)
    {
        if (_playConfigPanel == null) return;
        if (Mathf.IsEqualApprox(lift, _keyboardLift)) return;
        _keyboardLift = lift;
        if (_playConfigOrientation == ScreenOrientation.Portrait)
        {
            float halfH = _playConfigDesignSize.Y * 0.5f;
            _playConfigPanel.OffsetTop = -halfH - lift;
            _playConfigPanel.OffsetBottom = halfH - lift;
        }
        else if (_playConfigSurface != null)
        {
            LandscapeMenuChrome.ApplyLayout(_playConfigSurface, GetViewportRect().Size,
                SafeArea.Current, verticalShift: lift);
        }
        Log.Debug(Log.LogCategory.Display,
            $"MainMenu: keyboard lift -> {lift:0.#} logical px ({_playConfigOrientation})");
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

        // Escape and Enter mirror the per-page Back / forward buttons (issue
        // #40): on the player page Enter advances to map setup and Escape
        // returns to the landing menu; on the map page Enter starts the game
        // and Escape returns to player setup.
        bool onPlayerPage = _playConfigPage == PlayConfigPage.PlayerSetup;

        if (keyEvent.Keycode == Key.Escape)
        {
            if (onPlayerPage) OnBackPressed();
            else GoToPlayerPage();
            GetViewport()?.SetInputAsHandled();
            return;
        }

        if (keyEvent.Keycode != Key.Enter && keyEvent.Keycode != Key.KpEnter) return;

        if (onPlayerPage)
        {
            GoToMapPage();
        }
        else
        {
            // The Load Game dialog handles its own input via the Window focus
            // chain, so when it's open _UnhandledInput won't see the key.
            OnStartPressed();
        }
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
        SeedFormat.TryParseHex(_seedField.Text, out int seed);
        GameSettings.MasterSeed = seed;
        Log.Debug(Log.LogCategory.Input,
            $"MainMenu: start seed={SeedFormat.ToHex(seed)}");
        GetTree().ChangeSceneToFile("res://scenes/main.tscn");
    }
}
