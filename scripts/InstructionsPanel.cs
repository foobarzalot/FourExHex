using System;
using Godot;

/// <summary>
/// The paged Instructions modal (issue #134): a near-fullscreen dialog
/// teaching the game's rules, opened from the HUD Help menu. Every page
/// splits into two sub-panels — an <see cref="InstructionDemoView"/>
/// playing a looping mini-board animation of the rule, and the
/// explanatory text. Portrait stacks the demo above the text; landscape
/// puts the demo left, text right.
///
/// Interaction model matches the guided tour (<see cref="HudTour"/>):
/// Back / Next wrap through the pages, Close or Escape exits, and every
/// key is swallowed while the panel is up so HUD hotkeys can't mutate
/// the live game underneath. Paging is a two-panel carousel
/// (<see cref="PageCarousel"/>): swipes track the finger with the
/// neighboring page peeking in beside the current one, and commits
/// slide both together; buttons and arrow keys ride the same slide.
/// </summary>
public sealed partial class InstructionsPanel : CanvasLayer
{
    /// <summary>One page: title/body string-store keys plus the bundled
    /// tutorial (in <c>res://tutorials/</c>) whose recorded beats drive
    /// the demo animation — null for a text-only page.</summary>
    private sealed record InstructionPage(string TitleKey, string BodyKey, string? TutorialName);

    // The ordered rule pages. Grows as content is authored (issue #134
    // ships them in phases).
    private static readonly InstructionPage[] Pages =
    {
        new(StringKeys.HudInstrTerritoriesTitle,
            StringKeys.HudInstrTerritoriesBody,
            "instr_territories"),
        new(StringKeys.HudInstrRecruitTitle,
            StringKeys.HudInstrRecruitBody,
            "instr_recruit"),
        new(StringKeys.HudInstrDefenseTitle,
            StringKeys.HudInstrDefenseBody,
            "instr_defense"),
        new(StringKeys.HudInstrTowersTitle,
            StringKeys.HudInstrTowersBody,
            "instr_towers"),
        new(StringKeys.HudInstrCommanderTitle,
            StringKeys.HudInstrCommanderBody,
            "instr_commander"),
        new(StringKeys.HudInstrIncomeTitle,
            StringKeys.HudInstrIncomeBody,
            "instr_income"),
        new(StringKeys.HudInstrTreesTitle,
            StringKeys.HudInstrTreesBody,
            "instr_trees"),
        new(StringKeys.HudInstrWinningTitle,
            StringKeys.HudInstrWinningBody,
            "instr_winning"),
    };

    /// <summary>Raised once when the panel closes (Close button or
    /// Escape). The host frees this node.</summary>
    public event Action? Closed;

    private static readonly Font SerifFont =
        GD.Load<FontFile>("res://fonts/DMSerifDisplay-Regular.ttf");

    // Panel inset from the viewport edges — near-fullscreen.
    private const float EdgeMarginPx = 12f;

    private readonly SaveStore _saveStore = new SaveStore();

    /// <summary>One carousel slot's widgets: a full-rect root holding the
    /// page's own title, split region, demo, and body — everything that
    /// slides with the page (only the button row stays fixed).</summary>
    private sealed class PageView
    {
        public Control Root = null!;
        public Label Title = null!;
        public Label Body = null!;
        public BoxContainer Split = null!;
        public InstructionDemoView Demo = null!;
    }

    private readonly PageView[] _views = new PageView[2];
    private PageCarousel _carousel = null!;
    private Label _pageLabel = null!;
    private int _index;
    // Page index currently populated into the carousel's back view for a
    // drag peek; -1 when the back view holds nothing meaningful.
    private int _peekIndex = -1;
    private bool _closed;
    // Horizontal-swipe paging anywhere over the panel (page-turning:
    // left = Next, right = Back). Pure ViewMath recognizer fed
    // press/move/release positions observed in _Input; the carousel
    // tracks its Drag offset live.
    private readonly SwipeDetector _swipe = new SwipeDetector();

    public override void _Ready()
    {
        // Same layer family as the Help menu / pause modal — above the
        // HUD and the tour.
        Layer = 100;
        ProcessMode = ProcessModeEnum.Always;

        Viewport viewport = GetViewport();
        AddChild(ModalChrome.BuildBackdrop(viewport.GetVisibleRect().Size));

        // Near-fullscreen slate panel (theme Panel stylebox, like the
        // rest of the modal family, but edge-anchored instead of
        // content-sized).
        var panel = new PanelContainer
        {
            AnchorLeft = 0f, AnchorRight = 1f, AnchorTop = 0f, AnchorBottom = 1f,
            OffsetLeft = EdgeMarginPx, OffsetRight = -EdgeMarginPx,
            OffsetTop = EdgeMarginPx, OffsetBottom = -EdgeMarginPx,
        };
        AddChild(panel);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 10);
        panel.AddChild(root);

        _views[0] = BuildPageView();
        _views[1] = BuildPageView();
        _carousel = new PageCarousel(_views[0].Root, _views[1].Root)
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        root.AddChild(_carousel);

        // Button row: Back / Next / Close plus the page indicator —
        // same controls as the tour dialog. Fixed; only pages slide.
        var buttonRow = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        buttonRow.AddThemeConstantOverride("separation", 12);
        root.AddChild(buttonRow);

        _pageLabel = new Label { VerticalAlignment = VerticalAlignment.Center };
        _pageLabel.AddThemeFontSizeOverride("font_size", 18);
        _pageLabel.AddThemeColorOverride("font_color", UiPalette.InkSoft);
        buttonRow.AddChild(_pageLabel);

        buttonRow.AddChild(MakeButton("Back", () => Step(forward: false)));
        buttonRow.AddChild(MakeButton("Next", () => Step(forward: true)));
        buttonRow.AddChild(MakeButton("Close", () => Close("button")));

        viewport.SizeChanged += ApplyOrientation;
        ApplyOrientation();

        Log.Info(Log.LogCategory.Hud, $"[instr] open: {Pages.Length} pages");
        Populate(_views[0], 0);
        StartDemo(_views[0], 0);
        _pageLabel.Text = $"1 / {Pages.Length}";
        Log.Info(Log.LogCategory.Hud,
            $"[instr] page -> 1/{Pages.Length} ({Pages[0].TitleKey}) (via open)");
    }

    // One carousel slot: full-rect root → vbox → serif title + gold rule
    // + the orientation-aware demo/text split.
    private PageView BuildPageView()
    {
        var view = new PageView { Root = new Control() };

        var vbox = new VBoxContainer();
        vbox.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        vbox.AddThemeConstantOverride("separation", 10);
        view.Root.AddChild(vbox);

        view.Title = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        view.Title.AddThemeFontOverride("font", SerifFont);
        view.Title.AddThemeFontSizeOverride("font_size", 32);
        vbox.AddChild(view.Title);
        vbox.AddChild(new ColorRect
        {
            Color = UiPalette.GoldDim,
            CustomMinimumSize = new Vector2(200, 1),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        });

        view.Split = new BoxContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        view.Split.AddThemeConstantOverride("separation", 14);
        vbox.AddChild(view.Split);

        view.Demo = new InstructionDemoView
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 1.3f,
        };
        view.Split.AddChild(view.Demo);

        view.Body = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        view.Body.AddThemeFontSizeOverride("font_size", 20);
        view.Body.AddThemeColorOverride("font_color", UiPalette.InkSoft);
        view.Split.AddChild(view.Body);

        return view;
    }

    private static Button MakeButton(string text, Action onPressed)
    {
        var button = new Button
        {
            Text = text,
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        button.AddThemeFontSizeOverride("font_size", 22);
        button.Pressed += onPressed;
        AudioBus.AttachClick(button);
        return button;
    }

    private void ApplyOrientation()
    {
        Vector2 vp = GetViewport().GetVisibleRect().Size;
        bool portrait = ScreenLayout.Resolve(vp.X, vp.Y) == ScreenOrientation.Portrait;
        foreach (PageView view in _views)
        {
            view.Split.Vertical = portrait;
        }
    }

    private static int WrapIndex(int index) =>
        ((index % Pages.Length) + Pages.Length) % Pages.Length;

    private PageView ViewOf(Control root) =>
        _views[0].Root == root ? _views[0] : _views[1];

    // Fill a view's text from a page — without starting its demo (peeks
    // stay cheap; the demo starts when the page actually lands).
    private void Populate(PageView view, int index)
    {
        InstructionPage page = Pages[index];
        view.Title.Text = Strings.Get(page.TitleKey);
        view.Body.Text = Strings.Get(page.BodyKey);
    }

    private void StartDemo(PageView view, int index)
    {
        string? tutorial = Pages[index].TutorialName;
        if (tutorial == null) return;
        try
        {
            view.Demo.Play(_saveStore.LoadBundledTutorial(tutorial));
        }
        catch (Exception ex)
        {
            // A missing/corrupt bundled demo shouldn't take down the help
            // dialog — the page still shows its text.
            Log.Warn(Log.LogCategory.Hud,
                $"[instr] demo \"{tutorial}\" failed to load: {ex.Message}");
        }
    }

    // Populate the back view for the neighbor a drag is revealing (or
    // re-populate when the drag flips sides mid-gesture) — with its demo
    // playing live. The front demo is frozen for the whole drag, so only
    // one demo renders and paces at a time.
    private void EnsurePeek(float offset)
    {
        if (offset == 0f) return;
        int target = WrapIndex(_index + (offset < 0f ? 1 : -1));
        if (target == _peekIndex) return;
        PageView back = ViewOf(_carousel.Back);
        back.Demo.Stop();   // side flip mid-gesture: drop the other neighbor's stack
        _peekIndex = target;
        Populate(back, target);
        StartDemo(back, target);
        Log.Debug(Log.LogCategory.Hud, $"[instr] peek -> {target + 1}/{Pages.Length}");
    }

    /// <summary>
    /// Animated page change, shared by swipe commits, the Back/Next
    /// buttons, and the arrow keys: the current page slides off (from
    /// wherever the drag left it) while the new page slides in beside
    /// it; the incoming demo starts as the slide begins so it's playing
    /// when the page lands.
    /// </summary>
    private void Step(bool forward, string? via = null)
    {
        if (_carousel.Transitioning || _closed) return;
        int target = WrapIndex(_index + (forward ? 1 : -1));

        // A drag peek already has the target populated and its demo
        // playing; button/key paging populates and starts it here.
        PageView incoming = ViewOf(_carousel.Back);
        bool peeked = _peekIndex == target;
        _peekIndex = -1;
        ViewOf(_carousel.Front).Demo.Stop();
        if (!peeked)
        {
            Populate(incoming, target);
            StartDemo(incoming, target);
        }

        _index = target;
        _carousel.Commit(forward, onLanded: () =>
        {
            _pageLabel.Text = $"{target + 1} / {Pages.Length}";
        });
        Log.Info(Log.LogCategory.Hud,
            $"[instr] page -> {target + 1}/{Pages.Length} ({Pages[target].TitleKey}) " +
            $"(via {via ?? (forward ? "next" : "back")})");
    }

    /// <summary>Swallow every key while the panel is up (same contract as
    /// the tour): Escape closes, Left/Right page, everything else is
    /// eaten so HUD hotkeys can't fire underneath. Mouse presses are
    /// observed (not consumed) to recognize swipe paging anywhere over
    /// the panel; touch arrives here as emulated finger-0 mouse events.</summary>
    public override void _Input(InputEvent @event)
    {
        if (_closed) return;

        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                // Observe only — the press continues on to the buttons.
                // Mid-transition presses don't arm (no drag-during-slide).
                if (!_carousel.Transitioning) _swipe.Press(mb.Position.X, mb.Position.Y);
                return;
            }
            bool wasTracking = _swipe.IsTrackingHorizontal;
            SwipeDirection dir = _swipe.Release(mb.Position.X, mb.Position.Y);
            if (dir != SwipeDirection.None)
            {
                // Page-turning: finger left = Next, finger right = Back.
                Step(forward: dir == SwipeDirection.Left, via: "swipe");
                // A drag-release isn't a click anyone needs — eat it so no
                // button underneath fires. Taps pass through untouched.
                GetViewport().SetInputAsHandled();
            }
            else if (wasTracking)
            {
                // Cancel: drop the peeked stack, thaw the front demo in
                // place (it resumes mid-loop where it froze).
                _peekIndex = -1;
                ViewOf(_carousel.Back).Demo.Stop();
                ViewOf(_carousel.Front).Demo.SetFrozen(false);
                _carousel.SpringBack();
                GetViewport().SetInputAsHandled();
            }
            return;
        }

        // Live tracking: the page carousel follows the finger once the
        // gesture locks horizontal (vertical-locked drags never move it).
        // The visible demo freezes for the duration of the gesture; the
        // peeked neighbor's demo plays live.
        if (@event is InputEventMouseMotion mm)
        {
            if (_carousel.Transitioning) return;
            float offset = _swipe.Drag(mm.Position.X, mm.Position.Y);
            if (_swipe.IsTrackingHorizontal)
            {
                ViewOf(_carousel.Front).Demo.SetFrozen(true);
                EnsurePeek(offset);
                _carousel.Track(offset);
            }
            return;
        }

        if (@event is not InputEventKey key || !key.Pressed || key.Echo) return;

        switch (key.Keycode)
        {
            case Key.Escape: Close("escape"); break;
            case Key.Left: Step(forward: false); break;
            case Key.Right: Step(forward: true); break;
        }
        GetViewport().SetInputAsHandled();
    }

    private void Close(string reason)
    {
        if (_closed) return;
        _closed = true;
        foreach (PageView view in _views) view.Demo.Stop();
        Log.Info(Log.LogCategory.Hud, $"[instr] close ({reason})");
        Closed?.Invoke();
    }
}
