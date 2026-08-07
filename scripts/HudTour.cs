// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// The gameplay HUD guided tour overlay (issue #101). A CanvasLayer above the
/// HUD that walks the player through the play controls one element at a time:
/// a centered dialog (description + Back / Next / Close) plus a pulsing gold
/// ring highlighting the current element. A full-viewport transparent
/// click-catcher sits below the dialog so (a) clicking any HUD element jumps
/// the tour to it instead of firing its normal action, and (b) the normally
/// click-through readouts (turn counter, profit/loss chip) become "clickable"
/// for the tour without any MouseFilter juggling. Escape or Close exits.
///
/// Owns only presentation + the <see cref="HudTourSteps"/> cursor; the
/// element list (which nodes, in what order, with what copy) is supplied by
/// <see cref="HudView"/>, and the one bit of game-state coupling — selecting a
/// territory so the profit/loss chip renders — happens before this is built.
/// </summary>
public sealed partial class HudTour : CanvasLayer
{
    /// <summary>One toured HUD element: the step id, the live node to point at
    /// (read for its global rect; null for the intro page, which highlights
    /// nothing), and the copy shown in the dialog.</summary>
    public readonly record struct Entry(HudTourStep Step, Control? Node, string Title, string Body);

    /// <summary>Raised once when the tour closes (Close button or Escape).
    /// The host frees this node.</summary>
    public event Action? Closed;

    private static readonly Font SerifFont =
        GD.Load<FontFile>("res://fonts/DMSerifDisplay-Regular.ttf");

    // Gold pulsing ring, matching the CTA / terrain-focus highlight aesthetic.
    private static readonly Color RingColor = UiPalette.Gold;
    private const float RingInset = 4f;   // grow the ring a touch past the element

    // Authored card width. The card reflows narrower when the safe box can't
    // afford it (small phones) and never grows past it on a roomy screen.
    private const float DesignCardWidth = 440f;
    // Breathing room between the card and the safe-area edge — the same margin
    // the rest of the centered-modal family fits against.
    private const float ViewportMargin = UiMetrics.ViewportMarginPx;

    private readonly List<Entry> _entries;
    private readonly HudTourSteps _cursor;

    private Panel _ring = null!;
    private bool _closed;
    // Horizontal-swipe paging on the catcher (page-turning: left = Next,
    // right = Back). Pure ViewMath recognizer fed press/move/release
    // positions; the dialog carousel tracks its Drag offset live.
    private readonly SwipeDetector _swipe = new SwipeDetector();

    // One carousel slot: a viewport-sized click-through page with the
    // dialog center-anchored inside, plus its label refs.
    private sealed class DialogView
    {
        public Control Root = null!;
        public PanelContainer Panel = null!;
        public VBoxContainer Box = null!;
        public Label Title = null!;
        public Label Body = null!;
    }

    // Two dialogs slide as a carousel so the incoming step's dialog is
    // visible beside the outgoing one throughout a page turn.
    private readonly DialogView[] _dialogs = new DialogView[2];
    private PageCarousel _carousel = null!;
    // Entry index currently populated into the carousel's back dialog for
    // a drag peek; -1 when the back dialog holds nothing meaningful.
    private int _peekIndex = -1;

    public HudTour(IReadOnlyList<Entry> entries)
    {
        _entries = entries.ToList();
        _cursor = new HudTourSteps(_entries.Select(e => e.Step).ToList());
    }

    public override void _Ready()
    {
        // Above the HUD (default canvas, layer 0) and its overlays, but below
        // the pause menu / modal family (layer 100) — the tour swallows Escape
        // so pause is unreachable while it's up anyway.
        Layer = 50;
        ProcessMode = ProcessModeEnum.Always;

        // 1) Full-viewport click-catcher (bottom of this layer). Stops clicks
        //    from reaching the real HUD buttons / map and routes them to
        //    click-to-jump. Anchored full-rect so it tracks viewport resizes.
        var catcher = new Control
        {
            AnchorRight = 1f,
            AnchorBottom = 1f,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        catcher.GuiInput += OnCatcherInput;
        AddChild(catcher);

        // 2) Highlight ring — a thick gold border with a faint gold wash,
        //    click-through, repositioned to the current element every frame in
        //    _Process. Pulses between a bright floor and full for emphasis.
        var ringStyle = new StyleBoxFlat { BgColor = new Color(RingColor, 0.14f) };
        ringStyle.SetBorderWidthAll(5);
        ringStyle.BorderColor = RingColor;
        ringStyle.SetCornerRadiusAll(8);
        _ring = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _ring.AddThemeStyleboxOverride("panel", ringStyle);
        AddChild(_ring);
        var pulse = CreateTween().SetLoops();
        pulse.TweenProperty(_ring, "modulate:a", 0.72f, UiMetrics.CtaPulseHalfPeriodSec)
            .SetTrans(Tween.TransitionType.Sine);
        pulse.TweenProperty(_ring, "modulate:a", 1.0f, UiMetrics.CtaPulseHalfPeriodSec)
            .SetTrans(Tween.TransitionType.Sine);

        // 3) Centered dialogs (top of this layer so their buttons beat the
        //    catcher). No dim backdrop — keep the HUD bright so highlighted
        //    elements stay visible. Two dialog instances ride a
        //    manually-sized carousel so page turns show both at once.
        _dialogs[0] = BuildDialogPage();
        _dialogs[1] = BuildDialogPage();
        _carousel = new PageCarousel(_dialogs[0].Root, _dialogs[1].Root);
        AddChild(_carousel);
        SyncLayout();
        GetViewport().SizeChanged += SyncLayout;
        // A notch/status-bar toggle can shift the safe rect without resizing
        // the viewport, so the card re-fits on that too.
        SafeArea.Changed += OnSafeAreaChanged;

        // Always open on the intro page (it explains how to drive the tour,
        // highlighting nothing); Next from there steps into the elements.
        _cursor.JumpTo(HudTourStep.Intro);
        ShowCurrent("enter");
        Log.Info(Log.LogCategory.Hud,
            $"[tour] enter: {_cursor.Count} elements, start={_cursor.Current}");
    }

    // One carousel slot: a click-through page holding the centered dialog
    // panel; both slots' buttons drive the same step/close handlers.
    private DialogView BuildDialogPage()
    {
        var view = new DialogView { Root = new Control() };

        PanelContainer panel = ModalChrome.BuildCenteredPanel();
        view.Panel = panel;
        view.Root.AddChild(panel);

        // Design width; FitCards narrows it to whatever the safe box affords.
        var vbox = new VBoxContainer { CustomMinimumSize = new Vector2(DesignCardWidth, 0) };
        vbox.AddThemeConstantOverride("separation", 16);
        view.Box = vbox;
        panel.AddChild(vbox);

        view.Title = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        view.Title.AddThemeFontOverride("font", SerifFont);
        view.Title.AddThemeFontSizeOverride("font_size", 32);
        vbox.AddChild(view.Title);

        vbox.AddChild(ModalChrome.GoldRule());

        view.Body = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        view.Body.AddThemeFontSizeOverride("font_size", 20);
        view.Body.AddThemeColorOverride("font_color", UiPalette.InkSoft);
        vbox.AddChild(view.Body);

        var buttonRow = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        buttonRow.AddThemeConstantOverride("separation", 12);
        vbox.AddChild(buttonRow);

        buttonRow.AddChild(MakeButton("Back", () => Step(forward: false)));
        buttonRow.AddChild(MakeButton("Next", () => Step(forward: true)));
        buttonRow.AddChild(MakeButton("Close", () => Close("button")));

        return view;
    }

    private DialogView ViewOf(Control root) =>
        _dialogs[0].Root == root ? _dialogs[0] : _dialogs[1];

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

    public override void _Process(double delta)
    {
        // Track the highlight ring to the current element's live rect so it
        // follows layout changes (a newly-visible chip, an orientation flip).
        Control? node = _entries[_cursor.Index].Node;
        if (node != null && node.IsInsideTree() && node.Visible)
        {
            Rect2 rect = node.GetGlobalRect();
            _ring.Visible = true;
            _ring.GlobalPosition = rect.Position - new Vector2(RingInset, RingInset);
            _ring.Size = rect.Size + new Vector2(RingInset * 2f, RingInset * 2f);
        }
        else
        {
            _ring.Visible = false;
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (_closed) return;

        // Swipe machinery lives HERE (pre-GUI) rather than on the catcher's
        // GuiInput, so a swipe can begin anywhere — including over the
        // dialog, whose panel and buttons would otherwise eat the press.
        // Observe-only for presses/motion (taps continue on to buttons and
        // the catcher); only a completed swipe or spring-back consumes its
        // release so nothing underneath treats the drag-end as a click.
        if (@event is InputEventMouseMotion mm)
        {
            if (_carousel.Transitioning) return;
            // Live tracking: the dialogs follow the finger once the gesture
            // locks horizontal (vertical-locked drags never move them).
            float offset = _swipe.Drag(mm.Position.X, mm.Position.Y);
            if (_swipe.IsTrackingHorizontal)
            {
                EnsurePeek(offset);
                _carousel.Track(offset);
            }
            return;
        }
        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                if (!_carousel.Transitioning) _swipe.Press(mb.Position.X, mb.Position.Y);
                return;
            }
            // Page-turning: finger left = Next, finger right = Back. A
            // sub-threshold horizontal drag springs the dialogs back instead.
            bool wasTracking = _swipe.IsTrackingHorizontal;
            switch (_swipe.Release(mb.Position.X, mb.Position.Y))
            {
                case SwipeDirection.Left:
                    Step(forward: true, via: "swipe");
                    GetViewport().SetInputAsHandled();
                    return;
                case SwipeDirection.Right:
                    Step(forward: false, via: "swipe");
                    GetViewport().SetInputAsHandled();
                    return;
            }
            if (wasTracking)
            {
                _peekIndex = -1;
                _carousel.SpringBack();
                GetViewport().SetInputAsHandled();
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
        // Swallow every key while the tour is up so HUD hotkeys (Tab / N / U /
        // Enter …) can't mutate the game underneath it.
        GetViewport().SetInputAsHandled();
    }

    private void OnCatcherInput(InputEvent @event)
    {
        // Tap release on the catcher (swipe releases were consumed in
        // _Input and never reach the GUI): click-to-jump — hit-test
        // against each element's rect. The catcher already ate the click,
        // so the underlying button never fires.
        if (@event is not InputEventMouseButton mb || mb.Pressed
            || mb.ButtonIndex != MouseButton.Left)
        {
            return;
        }
        Vector2 pos = mb.Position;
        for (int i = 0; i < _entries.Count; i++)
        {
            Control? node = _entries[i].Node;
            if (node != null && node.IsInsideTree() && node.Visible
                && node.GetGlobalRect().HasPoint(pos))
            {
                if (i != _cursor.Index && _cursor.JumpTo(_entries[i].Step))
                {
                    ShowCurrent("click");
                }
                break;
            }
        }
    }

    // Keep the carousel matched to the viewport (its Resized handler re-homes
    // the dialogs when not transitioning) and re-fit the cards inside it.
    private void SyncLayout()
    {
        _carousel.Size = GetViewport().GetVisibleRect().Size;
        FitCards();
    }

    private void OnSafeAreaChanged(LogicalSafeInsets _) => FitCards();

    /// <summary>
    /// Size both carousel cards to the safe box. The card reflows first — its
    /// body label word-wraps at whatever interior width
    /// <see cref="PanelFitMath.CardInteriorWidth"/> allows — and a uniform
    /// shrink backstops the residual cases reflow can't fix (a tall body in a
    /// short landscape safe area, or the button row's own minimum width on a
    /// very narrow screen). Without this the fixed design width overflows a
    /// small phone and the carousel's ClipContents shears it off at both edges.
    /// </summary>
    private void FitCards()
    {
        Vector2 vp = GetViewport().GetVisibleRect().Size;
        LogicalSafeInsets safe = SafeArea.Current;
        (float availW, float availH) = PanelFitMath.AvailableBox(vp.X, vp.Y, safe, ViewportMargin);

        // The panel's own stylebox margins are part of its width budget.
        StyleBox chrome = _dialogs[0].Panel.GetThemeStylebox("panel");
        float chromeW = chrome.GetMargin(Side.Left) + chrome.GetMargin(Side.Right);
        float interiorW = PanelFitMath.CardInteriorWidth(DesignCardWidth, availW, chromeW);

        foreach (DialogView view in _dialogs)
        {
            view.Box.CustomMinimumSize = new Vector2(interiorW, 0);
            // Clear any prior shrink so the deferred pass measures the design.
            view.Panel.Scale = Vector2.One;
        }

        Log.Debug(Log.LogCategory.Hud,
            $"[tour] fit viewport={vp.X:0}x{vp.Y:0} " +
            $"safe=(t{safe.Top:0},b{safe.Bottom:0},l{safe.Left:0},r{safe.Right:0}) " +
            $"avail={availW:0}x{availH:0} chrome={chromeW:0} interior={interiorW:0}");

        // The reflowed height is only knowable once the new width has laid
        // out, so the shrink backstop runs a frame later. Scale is a transform
        // and doesn't feed back into the measurement, so one pass settles it.
        Callable.From(() => ApplyShrinkBackstop(availW, availH)).CallDeferred();
    }

    private void ApplyShrinkBackstop(float availW, float availH)
    {
        if (_closed || !IsInsideTree()) return;
        foreach (DialogView view in _dialogs)
        {
            Vector2 design = view.Panel.GetCombinedMinimumSize();
            float scale = PanelFitMath.ScaleToFit(design.X, design.Y, availW, availH);
            view.Panel.PivotOffset = design * 0.5f;
            view.Panel.Scale = new Vector2(scale, scale);
            if (view == _dialogs[0])
            {
                Log.Debug(Log.LogCategory.Hud,
                    $"[tour] fit card={design.X:0}x{design.Y:0} scale={scale:0.00}");
            }
        }
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= SyncLayout;
        SafeArea.Changed -= OnSafeAreaChanged;
    }

    private int WrapIndex(int index) =>
        ((index % _cursor.Count) + _cursor.Count) % _cursor.Count;

    // Fill a dialog's labels from the entry at an index — a peek doesn't
    // move the HudTourSteps cursor.
    private void PopulateDialog(DialogView view, int index)
    {
        view.Title.Text = _entries[index].Title;
        view.Body.Text = _entries[index].Body;
    }

    // Populate the back dialog for the neighbor a drag is revealing (or
    // re-populate when the drag flips sides mid-gesture).
    private void EnsurePeek(float offset)
    {
        if (offset == 0f) return;
        int target = WrapIndex(_cursor.Index + (offset < 0f ? 1 : -1));
        if (target == _peekIndex) return;
        _peekIndex = target;
        PopulateDialog(ViewOf(_carousel.Back), target);
    }

    /// <summary>
    /// Animated step, shared by swipe commits, the Back/Next buttons, and
    /// the arrow keys: the outgoing and incoming dialogs slide together
    /// as one page turn (from wherever the drag left them); the cursor —
    /// and with it the highlight ring — advances as the incoming dialog
    /// lands. Tap-to-jump stays instant.
    /// </summary>
    private void Step(bool forward, string? via = null)
    {
        if (_carousel.Transitioning || _closed) return;
        int target = WrapIndex(_cursor.Index + (forward ? 1 : -1));

        if (_peekIndex != target) PopulateDialog(ViewOf(_carousel.Back), target);
        _peekIndex = -1;

        _carousel.Commit(forward, onLanded: () =>
        {
            if (forward) _cursor.Next();
            else _cursor.Prev();
            ShowCurrent(via ?? (forward ? "next" : "back"));
        });
    }

    private void ShowCurrent(string via)
    {
        Entry e = _entries[_cursor.Index];
        DialogView front = ViewOf(_carousel.Front);
        front.Title.Text = e.Title;
        front.Body.Text = e.Body;
        Log.Debug(Log.LogCategory.Hud,
            $"[tour] step -> {e.Step} (via {via}) [{_cursor.Index + 1}/{_cursor.Count}]");
    }

    private void Close(string reason)
    {
        if (_closed) return;
        _closed = true;
        Log.Info(Log.LogCategory.Hud, $"[tour] exit ({reason})");
        Closed?.Invoke();
    }
}
