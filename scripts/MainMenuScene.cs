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
    // "None" disables the slot entirely — Player.BuildRoster drops
    // it, so the game runs with only the active (non-None) players.
    private const int NoneId = 2;

    /// <summary>Map a role dropdown's selected item ID to a kind, and back.
    /// Single source of truth so the dropdown init, the orientation-flip
    /// snapshot, and the Start handler all agree on the None/Human/Computer
    /// mapping.</summary>
    private static PlayerKind KindFromRoleId(int id) => id switch
    {
        ComputerId => PlayerKind.Computer,
        NoneId => PlayerKind.None,
        _ => PlayerKind.Human,
    };

    private static int RoleIdForKind(PlayerKind kind) => kind switch
    {
        PlayerKind.Computer => ComputerId,
        PlayerKind.None => NoneId,
        _ => HumanId,
    };

    /// <summary>Number of active (non-None) slots currently selected on the
    /// player-setup page. A valid game needs at least 2.</summary>
    private int ActivePlayerCount()
    {
        int n = 0;
        foreach (OptionButton role in _roleButtons)
        {
            if (role != null && role.GetSelectedId() != NoneId) n++;
        }
        return n;
    }

    /// <summary>One-shot handoff: when true, the menu opens straight to
    /// the campaign screen instead of the landing page (set by
    /// <see cref="Main"/>'s "Back to campaign" path so the player returns
    /// to the refreshed ladder, not the landing buttons). Mirrors the
    /// <see cref="LoadRequest.Pending"/> cross-scene handoff pattern.</summary>
    public static bool OpenCampaignOnArrival;

    private readonly OptionButton[] _roleButtons = new OptionButton[GameSettings.PlayerConfig.Length];
    private readonly OptionButton[] _difficultyButtons = new OptionButton[GameSettings.PlayerConfig.Length];
    // Game-mode selector (Freeform / Rising Tides) on the
    // Configure Game player-setup page; rebuilt with the page each show.
    private OptionButton? _gameModeButton;
    private static readonly Font SerifFont =
        GD.Load<FontFile>("res://fonts/DMSerifDisplay-Regular.ttf");
    private SaveStore _saveStore = null!;
    private SlotPickerDialog? _loadDialog;
    private ConfirmModal? _quitConfirmModal;

    private Control? _landingPanel;
    private Control? _playConfigPanel;
    private CampaignPanel? _campaignPanel;
    // The level "Play?" confirm sheet while it's open, so the
    // Escape handler can let the sheet consume Escape (cancel) instead of
    // backing out of the whole campaign ladder.
    private MapInfoSheet? _campaignSheet;
    // New Game / Map Editor source chooser (New Map | Load …).
    private EscMenu? _sourceChooser;
    // Design (unscaled) size of the landing panel, recorded at build time so
    // FitPanels can scale it down to fit a smaller-than-design viewport. (The
    // play-config panel is now a fill-to-cap surface and needs no design size.)
    private Vector2 _landingDesignSize;
    // Orientation the campaign panel was last built for; a resize that
    // flips it rebuilds the panel 8 columns ↔ 16 columns (see FitPanels).
    private ScreenOrientation _campaignOrientation = ScreenOrientation.Landscape;
    private SettingsPanel? _settingsPanel;
    private Button? _landingResumeButton;
    private Button? _landingPlayButton;
    private Button? _landingLoadButton;
    // Orientation the landing panel was last built for. Portrait keeps the
    // original button-stack panel (ScaleToFit shrinks it); landscape uses the
    // "split hero" fill layout. A resize that flips it rebuilds.
    private ScreenOrientation _landingOrientation = ScreenOrientation.Landscape;
    // Centered fill surfaces of the landscape menu panels (null when that panel
    // is built portrait); OnMenuSafeAreaChanged / FitPanels keep them centered +
    // size-capped against the current viewport / safe area.
    private PanelContainer? _landingSurface;
    private PanelContainer? _playConfigSurface;

    // The master seed driving the map preview. Not user-editable: re-roll
    // picks a fresh value and Start hands it to GameSettings.MasterSeed.
    // Initialized in _Ready and persists across orientation-flip rebuilds.
    private int _previewSeed;
    private Button? _startButton;
    // The player-page "Next" button (one is live at a time per orientation);
    // disabled when fewer than 2 active players are selected.
    private Button? _playerNextButton;
    private HudIconButton? _rerollButton;
    private MapGenSettingsPanel? _mapGenSettingsPanel;
    private MapThumbnailView? _thumbnail;

    // The New Game flow is split into two pages: player setup
    // (role + difficulty per slot) and map setup (re-roll die + a live
    // thumbnail of the board the previewed seed produces). Both page contents are built
    // up front and parented to the play-config panel; navigation toggles their
    // visibility (so selections survive paging back and forth). _playConfigPage
    // persists across the orientation-flip rebuild so a flip keeps you on the
    // same page.
    private enum PlayConfigPage { PlayerSetup, MapSetup }

    // What the player-setup screen feeds into: a new procedural
    // game, or a new map editor session. The kinds screen is shared; only the
    // forward action differs.
    private enum PlayConfigPurpose { NewGame, EditorNewMap }
    private PlayConfigPurpose _playConfigPurpose = PlayConfigPurpose.NewGame;
    private PlayConfigPage _playConfigPage = PlayConfigPage.PlayerSetup;
    private Control? _playerPageContent;
    private Control? _mapPageContent;
    // Clipping wrapper both page contents live in (full-rect anchored),
    // so the swipe carousel can slide them side by side without the
    // PanelContainer surface re-fitting positions and without content
    // overhanging the panel. Rebuilt with the panel per orientation.
    private Control? _pageClip;
    // Swipe paging between the two config pages (page-turning: left =
    // forward to map setup, right = back to player setup), with the same
    // finger-tracking + slide animation as the Instructions panel.
    // Durations mirrored in InstructionsPanel/HudTour — keep in step.
    private readonly SwipeDetector _configSwipe = new SwipeDetector();
    private const float ConfigSlideSec = 0.18f;
    private const float ConfigSpringBackSec = 0.15f;
    private Tween? _configSlideTween;
    private bool _configTransitioning;
    // Orientation the play-config panel was last built for; a viewport
    // resize that flips it triggers a rebuild (see FitPanels).
    private ScreenOrientation _playConfigOrientation = ScreenOrientation.Landscape;
    // True once _Ready hooked the viewport's SizeChanged (the diagnostic
    // 6AI branch returns before the hook; _ExitTree must not disconnect a
    // never-connected signal).
    private bool _viewportResizeHooked;

    public override void _Ready()
    {
        // Seed the map-preview value once; re-roll changes it thereafter.
        _previewSeed = SeedFormat.NextSeed(new System.Random());

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

        // Map-generation options, summoned by the "?" button on the
        // map-setup page. Toggling here does NOT re-render the thumbnail; the
        // preview refreshes only on a die press (fresh seed), which picks up the
        // current GameSettings flags.
        _mapGenSettingsPanel = new MapGenSettingsPanel();
        AddChild(_mapGenSettingsPanel);

        BuildLoadDialog();
        BuildQuitConfirmDialog();

        // Shared modal for the New Game / Map Editor "New Map | Load …" choice.
        _sourceChooser = new EscMenu();
        AddChild(_sourceChooser);

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
        // The landscape fill panels (landing and play-config) inset to
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
        // the safe rect, so it only needs its insets refreshed.
        if (_landingPanel != null)
        {
            if (_landingOrientation == ScreenOrientation.Portrait)
                ScaleToFit(_landingPanel, _landingDesignSize, viewport);
            else if (_landingSurface != null)
                LandscapeMenuChrome.ApplyLayout(_landingSurface, viewport, SafeArea.Current);
        }
        // Both orientations use the fill-to-cap surface now; ApplyPlayConfigLayout
        // picks the orientation cap.
        if (_playConfigSurface != null) ApplyPlayConfigLayout();
        // The campaign panel is NOT scaled — it fills the viewport and
        // scrolls (anchors re-solve on resize on their own; an orientation
        // flip rebuilds it via RebuildCampaignOnOrientationFlip below).
    }

    /// <summary>Re-apply safe-area insets to the fill surfaces when the notch /
    /// home indicator shifts without a resize (rotation, status-bar toggle).
    /// The portrait landing panel is still fixed-size — FitPanels scales it.</summary>
    private void OnMenuSafeAreaChanged(LogicalSafeInsets s)
    {
        Vector2 viewport = GetViewportRect().Size;
        if (_landingSurface != null) LandscapeMenuChrome.ApplyLayout(_landingSurface, viewport, s);
        if (_playConfigSurface != null) ApplyPlayConfigLayout();
    }

    /// <summary>Rebuild the landing panel when a viewport resize flips the
    /// orientation: portrait button-stack ↔ landscape "split hero".
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
    /// the orientation: portrait stacks each row's difficulty
    /// dropdown under the kind selector; landscape puts them side by side.
    /// Dropdown selections are written back into GameSettings — the build
    /// path initializes from there — and the preview seed survives in
    /// <see cref="_previewSeed"/> (a plain field, not a rebuilt control).</summary>
    private void RebuildPlayConfigOnOrientationFlip(Vector2 viewport)
    {
        if (_playConfigPanel == null) return;
        ScreenOrientation next = ScreenLayout.Resolve(viewport.X, viewport.Y);
        if (next == _playConfigOrientation) return;
        Log.Debug(Log.LogCategory.Render,
            $"MainMenu: orientation flip {_playConfigOrientation} -> {next}; rebuilding play-config panel");

        PersistRosterSelections();
        bool wasVisible = _playConfigPanel.Visible;

        Control old = _playConfigPanel;
        int treeIndex = old.GetIndex();
        old.Visible = false;
        old.QueueFree();

        _playConfigPanel = BuildPlayConfigPanel();
        AddChild(_playConfigPanel);
        MoveChild(_playConfigPanel, treeIndex);
        _playConfigPanel.Visible = wasVisible;
        // _playConfigPage persists across the rebuild; restore the matching page
        // visibility and re-render the thumbnail if the map page is showing.
        ShowCurrentPlayConfigPage();
        RefreshThumbnail();
    }

    private static void ScaleToFit(Control panel, Vector2 designSize, Vector2 viewport)
    {
        const float margin = 24f;
        float scale = PanelFitMath.ScaleToFit(designSize.X, designSize.Y,
            viewport.X - margin, viewport.Y - margin);
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
        // lives in the debug-only cheat menu). On mobile the Exit
        // button is suppressed (Apple HIG / Google Play guidance), so the
        // panel reclaims its slot — height is computed from the actual count
        // rather than the fixed 8-button design, otherwise ScaleToFit centers
        // against a phantom Exit slot, leaving dead space at the bottom.
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
            Text = Strings.Get(StringKeys.MenuWordmark),
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
        _landingResumeButton = new Button { Text = Strings.Get(StringKeys.MenuResume) };
        _landingResumeButton.AddThemeFontSizeOverride("font_size", 26);
        _landingResumeButton.Position = new Vector2(buttonInset, firstButtonY);
        _landingResumeButton.Size = new Vector2(buttonW, buttonH);
        _landingResumeButton.Pressed += OnResumePressed;
        AudioBus.AttachClick(_landingResumeButton);
        _landingResumeButton.Disabled = !slots.Any(s => s.IsAutosave);
        panel.AddChild(_landingResumeButton);

        _landingPlayButton = new Button { Text = Strings.Get(StringKeys.MenuPlayGame) };
        // Plain Button, not brass — brass marks terminal commit actions
        // (Start Game, Resume in Pause), not menu navigation.
        _landingPlayButton.AddThemeFontSizeOverride("font_size", 26);
        _landingPlayButton.Position = new Vector2(buttonInset, firstButtonY + (buttonH + buttonGap));
        _landingPlayButton.Size = new Vector2(buttonW, buttonH);
        _landingPlayButton.Pressed += OnPlayPressed;
        AudioBus.AttachClick(_landingPlayButton);
        panel.AddChild(_landingPlayButton);

        // Campaign ladder: 256 fixed-seed levels with
        // persistent progress. Sits right under Play Game — it's the
        // long-horizon progression mode.
        var campaignButton = new Button { Text = Strings.Get(StringKeys.MenuCampaign) };
        campaignButton.AddThemeFontSizeOverride("font_size", 26);
        campaignButton.Position = new Vector2(buttonInset, firstButtonY + (buttonH + buttonGap) * 2);
        campaignButton.Size = new Vector2(buttonW, buttonH);
        campaignButton.Pressed += OnCampaignPressed;
        AudioBus.AttachClick(campaignButton);
        panel.AddChild(campaignButton);

        // The end-user-facing tutorial entry point (the authoring tool
        // lives in the debug-only cheat menu).
        var playTutorialButton = new Button { Text = Strings.Get(StringKeys.MenuPlayTutorial) };
        playTutorialButton.AddThemeFontSizeOverride("font_size", 26);
        playTutorialButton.Position = new Vector2(buttonInset, firstButtonY + (buttonH + buttonGap) * 3);
        playTutorialButton.Size = new Vector2(buttonW, buttonH);
        playTutorialButton.Pressed += OnPlayTutorialPressed;
        AudioBus.AttachClick(playTutorialButton);
        panel.AddChild(playTutorialButton);

        _landingLoadButton = new Button { Text = Strings.Get(StringKeys.MenuLoadGame) };
        _landingLoadButton.AddThemeFontSizeOverride("font_size", 26);
        _landingLoadButton.Position = new Vector2(buttonInset, firstButtonY + (buttonH + buttonGap) * 4);
        _landingLoadButton.Size = new Vector2(buttonW, buttonH);
        _landingLoadButton.Pressed += OnLoadPressed;
        AudioBus.AttachClick(_landingLoadButton);
        // Disable when no saves exist so the user gets immediate visual
        // feedback rather than an empty popup.
        _landingLoadButton.Disabled = slots.Count == 0;
        panel.AddChild(_landingLoadButton);

        var mapEditorButton = new Button { Text = Strings.Get(StringKeys.MenuMapEditor) };
        mapEditorButton.AddThemeFontSizeOverride("font_size", 26);
        mapEditorButton.Position = new Vector2(buttonInset, firstButtonY + (buttonH + buttonGap) * 5);
        mapEditorButton.Size = new Vector2(buttonW, buttonH);
        mapEditorButton.Pressed += OnMapEditorPressed;
        AudioBus.AttachClick(mapEditorButton);
        panel.AddChild(mapEditorButton);

        var settingsButton = new Button { Text = Strings.Get(StringKeys.MenuSettings) };
        settingsButton.AddThemeFontSizeOverride("font_size", 26);
        settingsButton.Position = new Vector2(buttonInset, firstButtonY + (buttonH + buttonGap) * 6);
        settingsButton.Size = new Vector2(buttonW, buttonH);
        settingsButton.Pressed += OnSettingsPressed;
        AudioBus.AttachClick(settingsButton);
        panel.AddChild(settingsButton);

        // Exit suppressed on mobile (Apple HIG / Google Play); desktop only.
        if (!exitSuppressed)
        {
            var exitButton = new Button { Text = Strings.Get(StringKeys.MenuExit) };
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

    /// <summary>"Split hero" landscape landing: a wordmark + version
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
        var wordmark = new Label { Text = Strings.Get(StringKeys.MenuWordmark), HorizontalAlignment = HorizontalAlignment.Center };
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
        _landingPlayButton = MakeLandingButton(Strings.Get(StringKeys.MenuPlayGame), OnPlayPressed, 26);
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
        _landingResumeButton = MakeGridButton(Strings.Get(StringKeys.MenuResume), OnResumePressed);
        _landingResumeButton.Disabled = !slots.Any(s => s.IsAutosave);
        grid.AddChild(_landingResumeButton);
        grid.AddChild(MakeGridButton(Strings.Get(StringKeys.MenuCampaign), OnCampaignPressed));
        grid.AddChild(MakeGridButton(Strings.Get(StringKeys.MenuPlayTutorial), OnPlayTutorialPressed));
        _landingLoadButton = MakeGridButton(Strings.Get(StringKeys.MenuLoadGame), OnLoadPressed);
        _landingLoadButton.Disabled = slots.Count == 0;
        grid.AddChild(_landingLoadButton);
        grid.AddChild(MakeGridButton(Strings.Get(StringKeys.MenuMapEditor), OnMapEditorPressed));
        grid.AddChild(MakeGridButton(Strings.Get(StringKeys.MenuSettings), OnSettingsPressed));

        // Exit is suppressed on mobile (Apple HIG / Google Play); desktop gets
        // a full-width Exit below the grid.
        bool exitSuppressed = OS.HasFeature("mobile");
        if (!exitSuppressed)
        {
            Button exitButton = MakeLandingButton(Strings.Get(StringKeys.MenuExit), OnExitPressed, 26);
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
        // Portrait: the SAME fill-to-viewport-up-to-a-cap surface the landscape
        // New Game uses, but with the cap transposed (tall, not wide). The pages
        // are container-based and fill the surface, so on a phone the panel uses
        // the full long edge instead of being uniformly ScaleToFit-shrunk (which
        // left vertical space unused and made portrait smaller than landscape).
        PanelContainer surface = LandscapeMenuChrome.Build();
        _playConfigSurface = surface;

        _playerPageContent = BuildPortraitPlayerPage();
        _mapPageContent = BuildPortraitMapPage();
        MountPagedContent(surface);

        ShowCurrentPlayConfigPage();
        ApplyPlayConfigLayout();
        Log.Debug(Log.LogCategory.Render,
            "MainMenu: play-config built (Portrait, paged setup, fill surface)");
        return surface;
    }

    // Portrait New Game surface cap — the 90° transpose of the landscape cap
    // (LandscapeMenuChrome 920×520), so the dialog keeps a consistent footprint
    // across an orientation flip and fills the long edge on a phone.
    private const float PortraitMaxW = 520f;
    private const float PortraitMaxH = 920f;

    /// <summary>Size + center the play-config surface, filling the safe area up
    /// to the orientation-appropriate cap. The single sizing path shared by
    /// <see cref="FitPanels"/> and the safe-area hook so they can't drift.</summary>
    private void ApplyPlayConfigLayout()
    {
        if (_playConfigSurface == null) return;
        Vector2 vp = GetViewportRect().Size;
        bool portrait = ScreenLayout.Resolve(vp.X, vp.Y) == ScreenOrientation.Portrait;
        LandscapeMenuChrome.ApplyLayout(_playConfigSurface, vp, SafeArea.Current,
            maxW: portrait ? PortraitMaxW : LandscapeMenuChrome.MaxWidth,
            maxH: portrait ? PortraitMaxH : LandscapeMenuChrome.MaxHeight);
        // Debug: dump resolved geometry once the deferred container sort has
        // run, to compare against the pre-swipe baseline.
        Callable.From(() => LayoutDump.Dump(_playConfigSurface, "play-config")).CallDeferred();
    }

    /// <summary>Centered "New Game" wordmark + gold rule at the top of a portrait
    /// page.</summary>
    private void AddPortraitHeader(BoxContainer page)
    {
        var title = new Label { Text = Strings.Get(StringKeys.MenuNewGame), HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontOverride("font", SerifFont);
        title.AddThemeFontSizeOverride("font_size", 40);
        page.AddChild(title);
        page.AddChild(new ColorRect
        {
            Color = UiPalette.GoldDim,
            CustomMinimumSize = new Vector2(0, 1),
            SizeFlagsHorizontal = Control.SizeFlags.Fill,
        });
    }

    /// <summary>Portrait player-setup page (fills the surface): six two-line
    /// player blocks (swatch + name, then the Type / Difficulty dropdowns
    /// side-by-side below), a spacer, then Back (→ landing) / Next (→ map setup)
    /// at the bottom. Stacked-by-line so the dropdowns get the full (narrow)
    /// portrait width and the six players fit the height without scrolling.</summary>
    private Control BuildPortraitPlayerPage()
    {
        var col = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.Fill };
        col.AddThemeConstantOverride("separation", 8);
        AddPortraitHeader(col);

        // Game mode row above the roster — part of game setup,
        // shared with the map editor's new-map flow.
        col.AddChild(MakePortraitFieldRow(Strings.Get(StringKeys.MenuGameMode), ConfigureGameModeDropdown()));

        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 12);
        for (int i = 0; i < GameSettings.PlayerConfig.Length; i++)
            list.AddChild(MakePortraitPlayerBlock(i));
        col.AddChild(list);

        col.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });

        var nav = new HBoxContainer();
        nav.AddThemeConstantOverride("separation", 12);
        nav.AddChild(MakeLandscapeNavButton(Strings.Get(StringKeys.MenuBack), OnBackPressed));
        _playerNextButton = MakeLandscapeNavButton(Strings.Get(StringKeys.MenuNext), OnPlayerPageForward);
        nav.AddChild(_playerNextButton);
        col.AddChild(nav);
        RefreshPlayerNextGating();
        ApplyGameModeRoleLock();
        return col;
    }

    /// <summary>One portrait player block: swatch + name on the first line, the
    /// role and difficulty dropdowns side-by-side on the second (each filling
    /// half the row — wide enough at portrait width, unlike a single side-by-side
    /// row with the swatch/name competing for space). Computer slots lock
    /// difficulty to Soldier.</summary>
    private Control MakePortraitPlayerBlock(int slot)
    {
        (string name, string hex) = GameSettings.PlayerConfig[slot];
        var block = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        block.AddThemeConstantOverride("separation", 4);

        var idRow = new HBoxContainer();
        idRow.AddThemeConstantOverride("separation", 10);
        idRow.AddChild(new ColorRect
        {
            Color = new Color(hex),
            CustomMinimumSize = new Vector2(22, 22),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        });
        var nameLabel = new Label { Text = name, VerticalAlignment = VerticalAlignment.Center };
        nameLabel.AddThemeFontSizeOverride("font_size", 22);
        idRow.AddChild(nameLabel);
        block.AddChild(idRow);

        var ctrlRow = new HBoxContainer();
        ctrlRow.AddThemeConstantOverride("separation", 10);
        OptionButton role = ConfigureRoleDropdown(slot);
        role.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        role.CustomMinimumSize = new Vector2(0, 40);
        ctrlRow.AddChild(role);
        OptionButton difficulty = ConfigureDifficultyDropdown(slot);
        difficulty.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        difficulty.CustomMinimumSize = new Vector2(0, 40);
        ctrlRow.AddChild(difficulty);
        block.AddChild(ctrlRow);

        ApplyDifficultyLock(slot);
        return block;
    }

    /// <summary>A labeled control row — fixed-width caption then the control
    /// filling the rest (portrait Type / Difficulty / Map rows).</summary>
    private HBoxContainer MakePortraitFieldRow(string label, Control field)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        Label caption = MakeRailLabel(label);
        caption.CustomMinimumSize = new Vector2(108, 0);
        caption.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(caption);
        field.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        field.CustomMinimumSize = new Vector2(0, 44);
        row.AddChild(field);
        return row;
    }

    /// <summary>Portrait map-setup page (fills the surface): map selector, seed +
    /// re-roll, the live thumbnail (expands), and Back / Start at the bottom.</summary>
    private Control BuildPortraitMapPage()
    {
        var col = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.Fill };
        col.AddThemeConstantOverride("separation", 10);
        AddPortraitHeader(col);

        // Procedural-only map page: loading a saved starting map is its own
        // branch off the New Game source chooser, so the map page is just
        // a re-rollable preview.

        // Wide re-roll die + "?" map-gen options on one row above the preview.
        var seedRow = new HBoxContainer();
        seedRow.AddThemeConstantOverride("separation", 10);
        _rerollButton = MakeRerollButton();
        _rerollButton.CustomMinimumSize = new Vector2(44, 44);
        _rerollButton.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        seedRow.AddChild(_rerollButton);
        // "?" opens the shared Map Generation options panel.
        seedRow.AddChild(MakeMapGenSettingsButton());
        col.AddChild(seedRow);

        // Live board thumbnail — expands to fill the mid-page space.
        _thumbnail = BuildThumbnail();
        _thumbnail.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _thumbnail.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        col.AddChild(_thumbnail);

        var nav = new HBoxContainer();
        nav.AddThemeConstantOverride("separation", 12);
        nav.AddChild(MakeLandscapeNavButton(Strings.Get(StringKeys.MenuBack), GoToPlayerPage));
        _startButton = MakeLandscapeNavButton(Strings.Get(StringKeys.MenuStartGame), OnStartPressed);
        nav.AddChild(_startButton);
        col.AddChild(nav);
        return col;
    }

    // --- Shared play-config control factories (portrait + landscape) ---
    // Each configures the control + wiring (items, selection, events) and
    // stores it in its field; the caller positions / parents it.

    /// <summary>Game-mode selector (Freeform / Rising Tides) for the
    /// player-setup page. Items are keyed by the <see cref="GameMode"/> int so
    /// selection round-trips through <see cref="GameSettings.Mode"/>; writes the
    /// choice live. Shared by the new-game and map-editor new-map flows.</summary>
    private OptionButton ConfigureGameModeDropdown()
    {
        var dropdown = new OptionButton();
        dropdown.AddThemeFontSizeOverride("font_size", 21);
        dropdown.GetPopup().AddThemeFontSizeOverride("font_size", 21);
        dropdown.AddItem(Strings.Get(StringKeys.ModeFreeform), (int)GameMode.Freeform);
        dropdown.AddItem(Strings.Get(StringKeys.ModeRisingTides), (int)GameMode.RisingTides);
        dropdown.AddItem(Strings.Get(StringKeys.ModeFogOfWar), (int)GameMode.FogOfWar);
        dropdown.AddItem(Strings.Get(StringKeys.ModeVikingRaiders), (int)GameMode.VikingRaiders);
        UiDropdown.SelectItemById(dropdown, (int)GameSettings.Mode);
        dropdown.ItemSelected += _ =>
        {
            GameSettings.Mode = (GameMode)dropdown.GetSelectedId();
            Log.Debug(Log.LogCategory.Input, $"MainMenu: game mode → {GameSettings.Mode}");
            // Fog Of War forces exactly one human (red) + five computers; the
            // lock is reapplied here so flipping the mode reshapes the roster.
            ApplyGameModeRoleLock();
        };
        _gameModeButton = dropdown;
        return dropdown;
    }

    private OptionButton ConfigureRoleDropdown(int slot)
    {
        var dropdown = new OptionButton();
        dropdown.AddThemeFontSizeOverride("font_size", 21);
        // The button face and its drop-down popup are themed separately;
        // without this the expanded item list renders at the tiny default size.
        dropdown.GetPopup().AddThemeFontSizeOverride("font_size", 21);
        dropdown.AddItem(Strings.Get(StringKeys.PlayerKindHuman), HumanId);
        dropdown.AddItem(Strings.Get(StringKeys.PlayerKindComputer), ComputerId);
        dropdown.AddItem(Strings.Get(StringKeys.PlayerKindNone), NoneId);
        PlayerKind currentKind = slot < GameSettings.PlayerKinds.Length
            ? GameSettings.PlayerKinds[slot]
            : PlayerKind.Computer;
        UiDropdown.SelectItemById(dropdown, RoleIdForKind(currentKind));
        _roleButtons[slot] = dropdown;
        // Lock the difficulty dropdown for non-Human rows, and re-gate the
        // forward (Next) button whenever the active-player count can change.
        dropdown.ItemSelected += _ =>
        {
            ApplyDifficultyLock(slot);
            RefreshPlayerNextGating();
        };
        return dropdown;
    }

    private OptionButton ConfigureDifficultyDropdown(int slot)
    {
        var dropdown = new OptionButton();
        dropdown.AddThemeFontSizeOverride("font_size", 21);
        dropdown.GetPopup().AddThemeFontSizeOverride("font_size", 21);
        dropdown.AddItem(Strings.Get(StringKeys.UnitRecruit), (int)Difficulty.Recruit);
        dropdown.AddItem(Strings.Get(StringKeys.UnitSoldier), (int)Difficulty.Soldier);
        dropdown.AddItem(Strings.Get(StringKeys.UnitCaptain), (int)Difficulty.Captain);
        dropdown.AddItem(Strings.Get(StringKeys.UnitCommander), (int)Difficulty.Commander);
        // Initialize from GameSettings (mirrors the kind dropdown) so loaded
        // saves / Play Again round-trip per-slot levels.
        Difficulty currentDifficulty = slot < GameSettings.Difficulties.Length
            ? GameSettings.Difficulties[slot]
            : Difficulty.Soldier;
        UiDropdown.SelectItemById(dropdown, (int)currentDifficulty);
        _difficultyButtons[slot] = dropdown;
        return dropdown;
    }

    /// <summary>The "?" glyph that opens the shared Map Generation options panel.
    /// Sits beside the seed re-roll die; same button/panel as
    /// the map editor's.</summary>
    private HudIconButton MakeMapGenSettingsButton() =>
        MapGenSettingsPanel.MakeOpenButton(() => _mapGenSettingsPanel?.Open(), size: 44f);

    /// <summary>"Config rail + player list" landscape New Game: a
    /// fixed left rail (title, map, seed, Start, Back) beside a scrolling player
    /// list (swatch | name | Type | Difficulty per row), filling a centered,
    /// size-capped surface instead of the portrait stack.</summary>
    private Control BuildPlayConfigPanelLandscape()
    {
        PanelContainer surface = LandscapeMenuChrome.Build();
        _playConfigSurface = surface;

        // Both pages fill the surface via the paged-content clip;
        // ShowCurrentPlayConfigPage toggles which is visible.
        _playerPageContent = BuildLandscapePlayerPage();
        _mapPageContent = BuildLandscapeMapPage();
        MountPagedContent(surface);

        ShowCurrentPlayConfigPage();
        LandscapeMenuChrome.ApplyLayout(surface, GetViewportRect().Size, SafeArea.Current);
        Log.Debug(Log.LogCategory.Render, "MainMenu: play-config built (Landscape, paged player/map setup)");
        Callable.From(() => LayoutDump.Dump(_playConfigSurface, "play-config")).CallDeferred();
        return surface;
    }

    /// <summary>Landscape title + gold underline at the top of a page.</summary>
    private void AddLandscapeHeader(BoxContainer page)
    {
        var title = new Label { Text = Strings.Get(StringKeys.MenuNewGame), HorizontalAlignment = HorizontalAlignment.Left };
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
        // Game mode — part of game setup, shared with the map
        // editor's new-map flow.
        rail.AddChild(MakeRailLabel(Strings.Get(StringKeys.MenuGameMode)));
        OptionButton modeDropdown = ConfigureGameModeDropdown();
        modeDropdown.CustomMinimumSize = new Vector2(0, 40);
        rail.AddChild(modeDropdown);
        rail.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        // Back above the forward action (Next) in the vertical rail.
        rail.AddChild(MakeLandscapeNavButton(Strings.Get(StringKeys.MenuBack), OnBackPressed));
        _playerNextButton = MakeLandscapeNavButton(Strings.Get(StringKeys.MenuNext), OnPlayerPageForward);
        rail.AddChild(_playerNextButton);

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
        // No ScrollContainer: the six 40px rows fit the surface in both
        // orientations, so a plain list never shows a scrollbar.
        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 4);
        rightCol.AddChild(list);
        for (int i = 0; i < GameSettings.PlayerConfig.Length; i++)
        {
            list.AddChild(MakePlayerRow(i));
        }
        // Gate Next only after every role dropdown exists (the rail — and its
        // Next button — is built above before the rows, so _roleButtons are
        // still null at that point).
        RefreshPlayerNextGating();
        ApplyGameModeRoleLock();
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

        // Procedural-only map page: saved-map loading is its own branch off the
        // New Game source chooser, so no map selector here — just a
        // re-rollable preview.

        // Wide re-roll die + "?" map-gen options on one row.
        var seedRow = new HBoxContainer();
        seedRow.AddThemeConstantOverride("separation", 6);
        _rerollButton = MakeRerollButton();
        _rerollButton.CustomMinimumSize = new Vector2(44, 44);
        _rerollButton.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        seedRow.AddChild(_rerollButton);
        // "?" opens the shared Map Generation options panel.
        seedRow.AddChild(MakeMapGenSettingsButton());
        rail.AddChild(seedRow);

        rail.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });

        // Back above the forward action (Start Game) in the vertical rail.
        rail.AddChild(MakeLandscapeNavButton(Strings.Get(StringKeys.MenuBack), GoToPlayerPage));

        _startButton = new Button { Text = Strings.Get(StringKeys.MenuStartGame), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _startButton.AddThemeFontSizeOverride("font_size", 27);
        _startButton.CustomMinimumSize = new Vector2(0, 54);
        _startButton.Pressed += OnStartPressed;
        AudioBus.AttachClick(_startButton);
        rail.AddChild(_startButton);

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
        // 40px keeps all six rows inside the short phone-landscape surface
        // without a scrollbar (and there's ample room in portrait).
        var row = new HBoxContainer { CustomMinimumSize = new Vector2(0, 40) };
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
        Label type = MakeRailLabel(Strings.Get(StringKeys.MenuType));
        type.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(type);
        Label difficulty = MakeRailLabel(Strings.Get(StringKeys.MenuDifficulty));
        difficulty.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(difficulty);
        return row;
    }

    /// <summary>Section label (Map / column headers) for the
    /// landscape config rail — title case, matching the portrait labels.</summary>
    private static Label MakeRailLabel(string text)
    {
        var label = new Label { Text = text };
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
        // Only a Human slot picks its own difficulty; Computer pins to Soldier
        // and None has no difficulty at all — both disable the dropdown.
        bool isHuman = _roleButtons[slot].GetSelectedId() == HumanId;
        if (!isHuman && (Difficulty)difficultyDropdown.GetSelectedId() != Difficulty.Soldier)
        {
            UiDropdown.SelectItemById(difficultyDropdown, (int)Difficulty.Soldier);
            Log.Debug(Log.LogCategory.Input,
                $"MainMenu: {GameSettings.PlayerConfig[slot].Name} difficulty reset to "
                + "Soldier (non-Human slot)");
        }
        difficultyDropdown.Disabled = !isHuman;
    }

    /// <summary>Fog Of War demands exactly one human player. When that mode is
    /// selected, slot 0 (red) is forced to Human and every other slot to
    /// Computer, and all six role dropdowns are disabled so the roster can't be
    /// changed — only red's difficulty stays editable (via
    /// <see cref="ApplyDifficultyLock"/>). Any other mode re-enables the role
    /// dropdowns, restoring free roster editing. Driven off the shared
    /// <c>_roleButtons</c> array so it covers both the portrait and landscape
    /// player pages. Forced <see cref="UiDropdown.SelectItemById"/> does not raise
    /// <c>ItemSelected</c>, so this also persists the roster and re-gates Next.</summary>
    private void ApplyGameModeRoleLock()
    {
        bool fog = GameSettings.Mode == GameMode.FogOfWar;
        for (int slot = 0; slot < _roleButtons.Length; slot++)
        {
            OptionButton role = _roleButtons[slot];
            if (role == null) continue;
            if (fog)
            {
                int forcedId = slot == 0 ? HumanId : ComputerId;
                if (role.GetSelectedId() != forcedId) UiDropdown.SelectItemById(role, forcedId);
            }
            role.Disabled = fog;
            ApplyDifficultyLock(slot);
        }
        if (fog)
        {
            PersistRosterSelections();
            RefreshPlayerNextGating();
            Log.Debug(Log.LogCategory.Input,
                "MainMenu: Fog Of War — roster locked to 1 human (red) + 5 computers");
        }
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
        // Confirm sheet with a live thumbnail of the level's board.
        // The sheet derives title/status/seed from the level itself.
        MapInfoSheet sheet = CampaignConfirmSheet.Create(level);
        _campaignSheet = sheet;
        sheet.Confirmed += () => LaunchCampaignLevel(level);
        sheet.Canceled += () => { _campaignSheet = null; sheet.QueueFree(); };
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

    private void ShowPlayConfig() => ShowPlayConfig(PlayConfigPurpose.NewGame);

    private void ShowPlayConfig(PlayConfigPurpose purpose)
    {
        _playConfigPurpose = purpose;
        Log.Info(Log.LogCategory.Input,
            $"MainMenu: player-setup ({purpose}) defaults — " + string.Join(", ",
                GameSettings.PlayerConfig.Select((c, i) => $"{c.Name}={GameSettings.PlayerKinds[i]}")));
        if (_landingPanel != null) _landingPanel.Visible = false;
        if (_playConfigPanel != null) _playConfigPanel.Visible = true;
        // The shared player-setup screen feeds either a procedural game ("Next"
        // → map page) or a new editor map ("Create Map" → launch editor).
        if (_playerNextButton != null)
        {
            _playerNextButton.Text = purpose == PlayConfigPurpose.EditorNewMap
                ? Strings.Get(StringKeys.MenuCreateMap) : Strings.Get(StringKeys.MenuNext);
        }
        // A fresh entry always starts on the player-setup page (selections are
        // preserved, but the flow begins at page 1).
        _playConfigPage = PlayConfigPage.PlayerSetup;
        ShowCurrentPlayConfigPage();
    }

    // --- Paged New Game navigation ---

    /// <summary>Forward action of the shared player-setup page:
    /// gate on >=2 active players, persist the selections, then either advance
    /// to the procedural map page (New Game) or launch the editor (New Map).
    /// The Next button is disabled below 2 players, but the Enter key routes
    /// here directly, so the guard lives here.</summary>
    private void OnPlayerPageForward()
    {
        if (ActivePlayerCount() < 2)
        {
            Log.Info(Log.LogCategory.Input,
                $"MainMenu: blocked forward (only {ActivePlayerCount()} active player(s); need 2)");
            return;
        }
        // Commit the dropdown selections first: the map thumbnail builds from
        // Player.BuildRoster() (reads GameSettings.PlayerKinds), and the editor
        // launch snapshots the same arrays.
        PersistRosterSelections();
        if (_playConfigPurpose == PlayConfigPurpose.EditorNewMap) LaunchEditorNewMap();
        else GoToMapPage();
    }

    /// <summary>Launch the map editor for a fresh map, handing it the per-color
    /// kinds + difficulties chosen on the shared player-setup screen.</summary>
    private void LaunchEditorNewMap()
    {
        MapEditorRequest.Pending = new MapEditorRequest.Request
        {
            Source = MapEditorRequest.Source.NewMap,
            Kinds = (PlayerKind[])GameSettings.PlayerKinds.Clone(),
            Difficulties = (Difficulty[])GameSettings.Difficulties.Clone(),
        };
        Log.Info(Log.LogCategory.Input,
            "MainMenu: Map Editor new map — " + string.Join(", ",
                GameSettings.PlayerConfig.Select((c, i) => $"{c.Name}={GameSettings.PlayerKinds[i]}")));
        GetTree().ChangeSceneToFile("res://scenes/map_editor.tscn");
    }

    /// <summary>Parent both prebuilt page contents into a clipping wrapper
    /// under <paramref name="surface"/> so the swipe carousel can slide
    /// them side by side (a PanelContainer would re-fit their positions).
    /// Also resets any swipe/transition state a prior panel left behind
    /// (orientation rebuilds tear the old pages down mid-anything).</summary>
    private void MountPagedContent(PanelContainer surface)
    {
        _configSlideTween?.Kill();
        _configSlideTween = null;
        _configTransitioning = false;
        _configSwipe.Cancel();

        _pageClip = new Control
        {
            ClipContents = true,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        surface.AddChild(_pageClip);
        // AND-offsets: the pages arrive carrying offsets from their
        // pre-mount layout; anchors alone would keep them, sizing the
        // pages past the clip.
        _playerPageContent!.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _mapPageContent!.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _pageClip.AddChild(_playerPageContent);
        _pageClip.AddChild(_mapPageContent);
        // A resize re-resolves the pages' full-rect anchors (zeroing any
        // slide offset), which is exactly right outside a transition.
        _pageClip.Resized += () =>
        {
            if (!_configTransitioning) ResetConfigPagePositions();
        };
    }

    /// <summary>Horizontal placement for a full-rect-anchored page: shift
    /// via matching left/right offsets so the page keeps the clip's size.
    /// (Writing <c>Position</c> instead would bake the control's current
    /// effective size — its minimum, while the clip is still unsized —
    /// into the offsets, inflating the page past the clip.)</summary>
    private static void PlaceConfigPage(Control page, float x)
    {
        page.OffsetLeft = x;
        page.OffsetRight = x;
        page.OffsetTop = 0f;
        page.OffsetBottom = 0f;
    }

    private void ResetConfigPagePositions()
    {
        if (_playerPageContent != null) PlaceConfigPage(_playerPageContent, 0f);
        if (_mapPageContent != null) PlaceConfigPage(_mapPageContent, 0f);
    }

    private void ShowCurrentPlayConfigPage()
    {
        if (_playerPageContent != null)
            _playerPageContent.Visible = _playConfigPage == PlayConfigPage.PlayerSetup;
        if (_mapPageContent != null)
            _mapPageContent.Visible = _playConfigPage == PlayConfigPage.MapSetup;
        if (!_configTransitioning) ResetConfigPagePositions();
    }

    /// <summary>
    /// Animated page change (shared by swipe commits, the nav buttons,
    /// and Enter/Escape): the current page slides off from wherever the
    /// drag left it, the other page slides in beside it. Falls back to an
    /// instant switch when the pages aren't mounted.
    /// </summary>
    private void AnimateConfigPage(bool forward)
    {
        Control? from = forward ? _playerPageContent : _mapPageContent;
        Control? to = forward ? _mapPageContent : _playerPageContent;
        PlayConfigPage target = forward ? PlayConfigPage.MapSetup : PlayConfigPage.PlayerSetup;
        if (from == null || to == null || _pageClip == null)
        {
            _playConfigPage = target;
            ShowCurrentPlayConfigPage();
            if (forward) RefreshThumbnail();
            return;
        }
        if (_configTransitioning) return;
        _configTransitioning = true;

        // Commit the logical page up front: RefreshThumbnail (and the
        // Enter/Escape key mapping) key off it, and the slide is just
        // presentation from here.
        _playConfigPage = target;

        float w = _pageClip.Size.X;
        float sign = forward ? -1f : 1f;   // current page exits left going forward
        float fromX = from.OffsetLeft;
        to.Visible = true;
        PlaceConfigPage(to, fromX - sign * w);
        if (forward) RefreshThumbnail();   // render while it slides in

        Control fromPage = from;
        Control toPage = to;
        _configSlideTween?.Kill();
        _configSlideTween = CreateTween();
        _configSlideTween.SetParallel(true);
        _configSlideTween.TweenMethod(
                Callable.From((float x) => PlaceConfigPage(fromPage, x)),
                fromX, sign * w, ConfigSlideSec)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        _configSlideTween.TweenMethod(
                Callable.From((float x) => PlaceConfigPage(toPage, x)),
                fromX - sign * w, 0f, ConfigSlideSec)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        _configSlideTween.Chain().TweenCallback(Callable.From(() =>
        {
            _configTransitioning = false;
            ShowCurrentPlayConfigPage();
        }));
    }

    // Sub-threshold drag: ease both pages home, no page change.
    private void ConfigSpringBack()
    {
        Control? from = _playConfigPage == PlayConfigPage.PlayerSetup
            ? _playerPageContent : _mapPageContent;
        Control? neighbor = _playConfigPage == PlayConfigPage.PlayerSetup
            ? _mapPageContent : _playerPageContent;
        if (from == null || neighbor == null || _pageClip == null) return;

        float w = _pageClip.Size.X;
        float neighborHome = neighbor == _mapPageContent ? w : -w;
        Control fromPage = from;
        Control neighborPage = neighbor;
        _configSlideTween?.Kill();
        _configSlideTween = CreateTween();
        _configSlideTween.SetParallel(true);
        _configSlideTween.TweenMethod(
                Callable.From((float x) => PlaceConfigPage(fromPage, x)),
                from.OffsetLeft, 0f, ConfigSpringBackSec)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        _configSlideTween.TweenMethod(
                Callable.From((float x) => PlaceConfigPage(neighborPage, x)),
                neighbor.OffsetLeft, neighborHome, ConfigSpringBackSec)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        _configSlideTween.Chain().TweenCallback(Callable.From(ShowCurrentPlayConfigPage));
    }

    private void GoToMapPage()
    {
        Log.Debug(Log.LogCategory.Input, "MainMenu: New Game → map setup page");
        AnimateConfigPage(forward: true);
    }

    // --- Config-page swipe input -------------------------------------------
    //
    // Observed pre-GUI (like InstructionsPanel/HudTour) so a swipe can begin
    // over the page content, including its buttons. Presses/motion are
    // observe-only; only a completed swipe or spring-back consumes its
    // release. Touch arrives as emulated finger-0 mouse events.

    public override void _Input(InputEvent @event)
    {
        if (!ConfigSwipeEligible())
        {
            _configSwipe.Cancel();
            return;
        }

        if (@event is InputEventMouseMotion mm)
        {
            if (_configTransitioning) return;
            float offset = _configSwipe.Drag(mm.Position.X, mm.Position.Y);
            if (_configSwipe.IsTrackingHorizontal) UpdateConfigDrag(offset);
            return;
        }

        if (@event is not InputEventMouseButton mb || mb.ButtonIndex != MouseButton.Left) return;

        if (mb.Pressed)
        {
            if (_configTransitioning) return;
            if (_pageClip == null || !_pageClip.GetGlobalRect().HasPoint(mb.Position)) return;
            // Controls that own horizontal drags keep them: LineEdit text
            // selection is a horizontal drag, and OptionButton pops its
            // list on press (a swipe from it would strand the popup).
            if (StartsOnHorizontalDragControl(GetViewport().GuiGetHoveredControl())) return;
            _configSwipe.Press(mb.Position.X, mb.Position.Y);
            return;
        }

        bool wasTracking = _configSwipe.IsTrackingHorizontal;
        SwipeDirection dir = _configSwipe.Release(mb.Position.X, mb.Position.Y);
        bool onPlayerPage = _playConfigPage == PlayConfigPage.PlayerSetup;
        // Page-turning: left = forward to map setup — never into the map
        // editor (a swipe must not launch a scene; that stays on the
        // explicit Create Map button).
        if (dir == SwipeDirection.Left && onPlayerPage
            && _playConfigPurpose == PlayConfigPurpose.NewGame)
        {
            OnPlayerPageForward();                        // gates <2 players + persists
            if (!_configTransitioning) ConfigSpringBack(); // gate refused the page turn
            GetViewport().SetInputAsHandled();
            return;
        }
        if (dir == SwipeDirection.Right && !onPlayerPage)
        {
            GoToPlayerPage();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (wasTracking)
        {
            ConfigSpringBack();
            GetViewport().SetInputAsHandled();
        }
    }

    // Swipes only make sense while the play-config panel is the active
    // surface and nothing modal floats above it (this scene's _Input sees
    // every event, including ones aimed at overlays).
    private bool ConfigSwipeEligible() =>
        _playConfigPanel is { Visible: true }
        && _pageClip != null
        && !(_settingsPanel?.IsOpen ?? false)
        && !(_quitConfirmModal?.IsOpen ?? false)
        && !(_campaignSheet?.IsOpen ?? false)
        && !(_sourceChooser?.IsOpen ?? false)
        && !(_mapGenSettingsPanel?.IsOpen ?? false)
        && !(_loadDialog?.Visible ?? false);

    private static bool StartsOnHorizontalDragControl(Control? hovered)
    {
        for (Control? c = hovered; c != null; c = c.GetParentOrNull<Control>())
        {
            if (c is LineEdit or OptionButton or HSlider or SpinBox) return true;
        }
        return false;
    }

    /// <summary>Live drag: the current page follows the finger (clamped to
    /// directions that actually have a destination — no wiggle toward a
    /// missing or gated neighbor), with the neighbor page tracking beside
    /// it like a carousel.</summary>
    private void UpdateConfigDrag(float rawOffset)
    {
        if (_pageClip == null || _playerPageContent == null || _mapPageContent == null) return;
        bool onPlayerPage = _playConfigPage == PlayConfigPage.PlayerSetup;
        bool forwardAllowed = onPlayerPage
            && _playConfigPurpose == PlayConfigPurpose.NewGame
            && ActivePlayerCount() >= 2;
        bool backAllowed = !onPlayerPage;

        float offset = rawOffset;
        if (offset < 0f && !forwardAllowed) offset = 0f;
        if (offset > 0f && !backAllowed) offset = 0f;

        Control current = onPlayerPage ? _playerPageContent : _mapPageContent;
        Control neighbor = onPlayerPage ? _mapPageContent : _playerPageContent;
        float w = _pageClip.Size.X;
        PlaceConfigPage(current, offset);
        PlaceConfigPage(neighbor, onPlayerPage ? offset + w : offset - w);
        neighbor.Visible = offset != 0f;
        if (neighbor == _mapPageContent && neighbor.Visible && _playConfigPage != PlayConfigPage.MapSetup)
        {
            // Peeking at the map page mid-drag: give the thumbnail the
            // current roster so it doesn't slide in stale. Cheap and
            // token-coalesced in MapThumbnailView; only worth it once per
            // drag, so gate on the first reveal.
            if (!_dragThumbnailRefreshed)
            {
                _dragThumbnailRefreshed = true;
                PersistRosterSelections();
                _thumbnail?.RequestRandom(_previewSeed);
            }
        }
        if (offset == 0f) _dragThumbnailRefreshed = false;
    }

    // One thumbnail refresh per drag-reveal of the map page (see above).
    private bool _dragThumbnailRefreshed;

    /// <summary>Write the current player-row dropdown selections (kind incl.
    /// None, and difficulty) into <see cref="GameSettings"/> — the single
    /// persist path shared by the map-page transition, the Start handler, and
    /// the orientation-flip snapshot.</summary>
    private void PersistRosterSelections()
    {
        for (int i = 0; i < _roleButtons.Length; i++)
        {
            GameSettings.PlayerKinds[i] = KindFromRoleId(_roleButtons[i].GetSelectedId());
            GameSettings.Difficulties[i] = (Difficulty)_difficultyButtons[i].GetSelectedId();
        }
    }

    /// <summary>Disable the player-page "Next" button when fewer than 2 active
    /// players are selected, so a 0/1-player game can't be started.</summary>
    private void RefreshPlayerNextGating()
    {
        if (_playerNextButton != null)
        {
            _playerNextButton.Disabled = ActivePlayerCount() < 2;
        }
    }

    private void GoToPlayerPage()
    {
        AnimateConfigPage(forward: false);
        Log.Debug(Log.LogCategory.Input, "MainMenu: New Game → player setup page");
    }

    /// <summary>Re-render the live thumbnail from the current preview seed. No-op
    /// unless the map-setup page (the only page with a thumbnail) is showing.</summary>
    private void RefreshThumbnail()
    {
        if (_thumbnail == null || _playConfigPage != PlayConfigPage.MapSetup) return;
        _thumbnail.RequestRandom(_previewSeed);
    }

    private MapThumbnailView BuildThumbnail()
    {
        var thumb = new MapThumbnailView();
        thumb.SetSaveStore(_saveStore);
        return thumb;
    }

    /// <summary>The die button that re-rolls the preview seed, modeled on the
    /// map editor's.</summary>
    private HudIconButton MakeRerollButton()
    {
        var button = new HudIconButton(HudIcon.Die) { FocusMode = Control.FocusModeEnum.None };
        button.Pressed += OnReRollSeedPressed;
        AudioBus.AttachClick(button);
        return button;
    }

    private void OnReRollSeedPressed()
    {
        _previewSeed = SeedFormat.NextSeed(new System.Random());
        _rerollButton?.FlashPress();
        Log.Debug(Log.LogCategory.Input, $"MainMenu: reroll seed={SeedFormat.ToHex(_previewSeed)}");
        RefreshThumbnail();
    }

    private void OnPlayPressed()
    {
        // Play Game source chooser: configure a fresh game,
        // load a saved starting map, or jump straight into a default game.
        Log.Info(Log.LogCategory.Input, "MainMenu: Play Game → source chooser");
        _sourceChooser?.Show(Strings.Get(StringKeys.MenuPlayGame), new[]
        {
            // Game mode (Freeform / Rising Tides) is chosen on the
            // Configure Game player-setup page, not here — it's part of game
            // setup and is shared with the map editor's new-map flow.
            new EscMenu.Option(Strings.Get(StringKeys.MenuConfigureGame), ShowPlayConfig),
            new EscMenu.Option(Strings.Get(StringKeys.MenuLoadStartingMap), OpenLoadStartingMapToPlay),
            new EscMenu.Option(Strings.Get(StringKeys.MenuQuickPlay), OnQuickPlay),
        });
    }

    /// <summary>Quick Play: skip both setup pages and launch a basic
    /// freeform game — Red human + 5 Computer (all Soldier), a fresh random seed,
    /// default densities, not a campaign. Mirrors the FOUREXHEX_6AI bypass but
    /// user-triggered and with a human in slot 0.</summary>
    private void OnQuickPlay()
    {
        GameSettings.CampaignLevel = null; // freeform — no campaign result recorded
        GameSettings.Mode = GameMode.Freeform; // Quick Play is always basic freeform
        // Set the roster explicitly so prior session / campaign / load state
        // can't leak in: Red human, the rest Computer, all Soldier.
        for (int i = 0; i < GameSettings.PlayerKinds.Length; i++)
        {
            GameSettings.PlayerKinds[i] = i == 0 ? PlayerKind.Human : PlayerKind.Computer;
            GameSettings.Difficulties[i] = Difficulty.Soldier;
        }
        // Reset map-gen to the documented defaults so a prior Configure-Game
        // tweak doesn't carry into the "basic" Quick Play map.
        GameSettings.TreeDensity = 5;
        GameSettings.MountainDensity = 0;
        GameSettings.GoldDensity = 0;
        GameSettings.ClumpingFactor = 0;

        int seed = SeedFormat.NextSeed(new System.Random());
        GameSettings.MasterSeed = seed;
        Log.Info(Log.LogCategory.Input,
            $"MainMenu: Quick Play — seed {SeedFormat.ToHex(seed)} (Red human + 5 AI)");
        LaunchGameScene();
    }

    private void OnMapEditorPressed()
    {
        // Map Editor source chooser: describe the players up-front
        // for a fresh map, or open a saved map for further editing.
        Log.Info(Log.LogCategory.Input, "MainMenu: Map Editor → source chooser");
        _sourceChooser?.Show(Strings.Get(StringKeys.MenuMapEditor), new[]
        {
            new EscMenu.Option(Strings.Get(StringKeys.MenuNewMap), () => ShowPlayConfig(PlayConfigPurpose.EditorNewMap)),
            new EscMenu.Option(Strings.Get(StringKeys.MenuLoadMap), OpenLoadMapToEdit),
        });
    }

    /// <summary>Load-map-to-edit branch of the Map Editor chooser:
    /// the same map picker as the play flow; selection launches the editor with
    /// the map loaded.</summary>
    private void OpenLoadMapToEdit()
    {
        if (_loadDialog == null) return;
        Log.Info(Log.LogCategory.Input, "MainMenu: Map Editor → load map");
        _loadDialog.ShowSlots(
            _saveStore.ListMaps(),
            Strings.Get(StringKeys.MenuNoMapsFound),
            info => info.SlotName,
            OnPickMapToEdit,
            thumbnailStore: _saveStore,
            previewMaps: true);
    }

    private void OnPickMapToEdit(string mapName)
    {
        MapEditorRequest.Pending = new MapEditorRequest.Request
        {
            Source = MapEditorRequest.Source.LoadMap,
            MapName = mapName,
        };
        Log.Info(Log.LogCategory.Input, $"MainMenu: open map \"{mapName}\" in editor");
        GetTree().ChangeSceneToFile("res://scenes/map_editor.tscn");
    }

    /// <summary>Load-starting-map-to-play branch of the New Game chooser:
    /// pick a saved map, preview it + who you're playing as in the
    /// shared <see cref="MapInfoSheet"/>, then launch the game on its baked
    /// roster.</summary>
    private void OpenLoadStartingMapToPlay()
    {
        if (_loadDialog == null) return;
        Log.Info(Log.LogCategory.Input, "MainMenu: New Game → load starting map");
        _loadDialog.ShowSlots(
            _saveStore.ListMaps(),
            Strings.Get(StringKeys.MenuNoMapsFound),
            info => info.SlotName,
            OnPickStartingMapToPlay,
            thumbnailStore: _saveStore,
            previewMaps: true);
    }

    private void OnPickStartingMapToPlay(string mapName)
    {
        LoadedSave loaded;
        try
        {
            loaded = _saveStore.LoadMap(mapName);
        }
        catch (System.Exception ex)
        {
            _loadDialog?.ShowError(Strings.Get(StringKeys.MenuCouldNotLoadMap,
                ("name", mapName), ("error", ex.Message)));
            return;
        }
        // The picker already previews the board, so launch straight into the
        // game on the map's baked roster — no extra confirm step.
        LoadRequest.Pending = loaded;
        GameSettings.MasterSeed = loaded.MasterSeed;
        Log.Info(Log.LogCategory.Input, $"MainMenu: launch starting map \"{mapName}\"");
        GetTree().ChangeSceneToFile("res://scenes/main.tscn");
    }

    private void OnSettingsPressed()
    {
        // Settings is a modal layered over the landing page; leaves
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
            Strings.Get(StringKeys.MenuExitTitle), Strings.Get(StringKeys.MenuExitBody), Strings.Get(StringKeys.MenuExit));
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

    private void BuildLoadDialog()
    {
        _loadDialog = new SlotPickerDialog(Strings.Get(StringKeys.MenuLoadGame), Strings.Get(StringKeys.MenuLoadFailed));
        _loadDialog.Attach(this);
    }

    private void OnLoadPressed()
    {
        if (_loadDialog == null) return;
        _loadDialog.ShowSlots(
            _saveStore.ListSlots(),
            Strings.Get(StringKeys.MenuNoSavesFound),
            info => info.IsAutosave
                ? Strings.Get(StringKeys.SaveAutosaveRow,
                    ("turn", info.TurnNumber.ToString()),
                    ("time", SlotPickerDialog.FormatTimestamp(info.SavedAtUnix)))
                : Strings.Get(StringKeys.SaveSlotRow,
                    ("name", info.SlotName),
                    ("turn", info.TurnNumber.ToString()),
                    ("time", SlotPickerDialog.FormatTimestamp(info.SavedAtUnix))),
            OnLoadSlotPressed,
            thumbnailStore: _saveStore);
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
            GameSettings.AdoptRosterFrom(loaded);
            GetTree().ChangeSceneToFile("res://scenes/main.tscn");
        }
        catch (System.Exception ex)
        {
            _loadDialog?.ShowError(Strings.Get(StringKeys.MenuCouldNotLoad,
                ("name", slotName), ("error", ex.Message)));
        }
    }

    private void OnResumePressed()
    {
        Log.Info(Log.LogCategory.Input, "MainMenu Resume pressed — loading autosave.");
        try
        {
            LoadedSave loaded = _saveStore.LoadSlot(SaveStore.AutosaveSlotName);
            LoadRequest.Pending = loaded;
            GameSettings.AdoptRosterFrom(loaded);
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


    // --- Android system back (#149) ----------------------------------------
    // The OS back button/gesture arrives as NOTIFICATION_WM_GO_BACK_REQUEST
    // (quit_on_go_back=false in project.godot). One rung per press. The rungs
    // mirror the Escape dispatch in _UnhandledInput below, with one twist:
    // open modals consume Escape themselves via their own input handlers,
    // but the notification never reaches them — so back closes the top
    // modal explicitly, via the same close path its own Escape uses.

    public override void _Notification(int what)
    {
        if (what == NotificationWMGoBackRequest) HandleSystemBack();
    }

    private void HandleSystemBack()
    {
        // Modals, topmost first.
        if (_settingsPanel is { IsOpen: true })
        {
            Log.Debug(Log.LogCategory.Input, "[back] close settings");
            _settingsPanel.Close();
            return;
        }
        if (_quitConfirmModal is { IsOpen: true })
        {
            Log.Debug(Log.LogCategory.Input, "[back] cancel quit confirm");
            _quitConfirmModal.Close();
            return;
        }
        if (_campaignSheet is { IsOpen: true })
        {
            Log.Debug(Log.LogCategory.Input, "[back] cancel campaign sheet");
            _campaignSheet.CloseAsCancel();
            return;
        }
        if (_sourceChooser is { IsOpen: true })
        {
            Log.Debug(Log.LogCategory.Input, "[back] close source chooser");
            _sourceChooser.CloseAsEscape();
            return;
        }
        if (_loadDialog is { Visible: true })
        {
            Log.Debug(Log.LogCategory.Input, "[back] close load dialog");
            _loadDialog.Hide();
            return;
        }

        // Panel ladder — same rungs as the Escape keys in
        // HandlePlayConfigKey / the campaign-panel dispatch below.
        if (_playConfigPanel is { Visible: true })
        {
            if (_playConfigPage == PlayConfigPage.PlayerSetup)
            {
                Log.Debug(Log.LogCategory.Input, "[back] play config → landing");
                OnBackPressed();
            }
            else
            {
                Log.Debug(Log.LogCategory.Input, "[back] map setup → player setup");
                GoToPlayerPage();
            }
            return;
        }
        if (_campaignPanel is { Visible: true })
        {
            Log.Debug(Log.LogCategory.Input, "[back] campaign → landing");
            ShowLanding();
            return;
        }

        // Landing root: Android convention — back exits the app outright
        // (what the engine's quit_on_go_back default did before this
        // ladder). Desktop never receives the notification, so its Escape
        // keeps routing through the quit-confirm modal.
        Log.Debug(Log.LogCategory.Input, "[back] landing root → quit");
        GetTree().Quit();
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

        // The campaign level confirm sheet owns its own Escape (cancel) while
        // open — don't also back out of the ladder underneath it.
        if (_campaignSheet != null && _campaignSheet.IsOpen) return;

        // The New Game / Map Editor source chooser owns its own Escape while
        // open.
        if (_sourceChooser != null && _sourceChooser.IsOpen) return;

        // Per-panel input dispatch: each panel only sees the keys that
        // make sense while it's the visible one.
        if (_playConfigPanel != null && _playConfigPanel.Visible)
        {
            HandlePlayConfigKey(keyEvent);
            return;
        }
        // Campaign ladder: Escape backs out to the landing menu (mirrors the
        // panel's Back button).
        if (_campaignPanel != null && _campaignPanel.Visible)
        {
            if (keyEvent.Keycode == Key.Escape && !keyEvent.Echo)
            {
                ShowLanding();
                GetViewport()?.SetInputAsHandled();
            }
            return;
        }
        if (_landingPanel != null && _landingPanel.Visible)
        {
            HandleLandingKey(keyEvent);
        }
    }

    private void HandlePlayConfigKey(InputEventKey keyEvent)
    {
        if (keyEvent.Echo) return;

        // Escape and Enter mirror the per-page Back / forward buttons:
        // on the player page Enter advances to map setup and Escape
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
            OnPlayerPageForward();
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
        // leftover campaign context from a prior launch.
        GameSettings.CampaignLevel = null;

        // Persist the dropdown selections — kinds and per-slot difficulty
        // always come from this panel (saved maps don't carry per-color
        // roles). Difficulty is stored per-slot so mixed
        // configurations land directly on the roster and round-trip
        // through the save.
        PersistRosterSelections();
        Log.Info(Log.LogCategory.Input,
            "MainMenu: start — " + string.Join(", ",
                GameSettings.PlayerConfig.Select((config, i) =>
                    $"{config.Name}={GameSettings.PlayerKinds[i]}/{GameSettings.Difficulties[i]}")));

        // Procedural map flow (loading a saved starting map is its own branch
        // off the source chooser): hand the previewed seed to the game.
        GameSettings.MasterSeed = _previewSeed;
        Log.Debug(Log.LogCategory.Input,
            $"MainMenu: start seed={SeedFormat.ToHex(_previewSeed)}");
        GetTree().ChangeSceneToFile("res://scenes/main.tscn");
    }
}
