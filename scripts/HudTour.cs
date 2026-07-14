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

    private readonly List<Entry> _entries;
    private readonly HudTourSteps _cursor;

    private Panel _ring = null!;
    private Label _titleLabel = null!;
    private Label _bodyLabel = null!;
    private bool _closed;
    // Horizontal-swipe paging on the catcher (page-turning: left = Next,
    // right = Back). Pure ViewMath recognizer fed press/move/release
    // positions; the dialog tracks its Drag offset live.
    private readonly SwipeDetector _swipe = new SwipeDetector();
    // Manually-positioned wrapper the dialog centers inside — its
    // Position.X is the drag/animation target (the center-anchored dialog
    // itself can't be safely position-animated; layout would fight it).
    private Control _dialogSlider = null!;
    // Slide animation constants mirrored in InstructionsPanel — keep in step.
    private const float SlideOutSec = 0.18f;
    private const float SlideInSec = 0.18f;
    private const float SpringBackSec = 0.15f;
    private Tween? _slideTween;
    private bool _transitioning;

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
        pulse.TweenProperty(_ring, "modulate:a", 0.72f, 0.55).SetTrans(Tween.TransitionType.Sine);
        pulse.TweenProperty(_ring, "modulate:a", 1.0f, 0.55).SetTrans(Tween.TransitionType.Sine);

        // 3) Centered dialog (top of this layer so its buttons beat the
        //    catcher). No dim backdrop — keep the HUD bright so highlighted
        //    elements stay visible. It centers inside a manually-sized,
        //    click-through slider wrapper whose Position.X carries the
        //    swipe drag and the page-change slide.
        _dialogSlider = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(_dialogSlider);
        _dialogSlider.AddChild(BuildDialog());
        SyncDialogSliderRect();
        GetViewport().SizeChanged += SyncDialogSliderRect;

        // Always open on the intro page (it explains how to drive the tour,
        // highlighting nothing); Next from there steps into the elements.
        _cursor.JumpTo(HudTourStep.Intro);
        ShowCurrent("enter");
        Log.Info(Log.LogCategory.Hud,
            $"[tour] enter: {_cursor.Count} elements, start={_cursor.Current}");
    }

    private Control BuildDialog()
    {
        PanelContainer panel = ModalChrome.BuildCenteredPanel();

        var vbox = new VBoxContainer { CustomMinimumSize = new Vector2(440, 0) };
        vbox.AddThemeConstantOverride("separation", 16);
        panel.AddChild(vbox);

        _titleLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _titleLabel.AddThemeFontOverride("font", SerifFont);
        _titleLabel.AddThemeFontSizeOverride("font_size", 32);
        vbox.AddChild(_titleLabel);

        var goldRule = new ColorRect
        {
            Color = UiPalette.GoldDim,
            CustomMinimumSize = new Vector2(200, 1),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        };
        vbox.AddChild(goldRule);

        _bodyLabel = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _bodyLabel.AddThemeFontSizeOverride("font_size", 20);
        _bodyLabel.AddThemeColorOverride("font_color", UiPalette.InkSoft);
        vbox.AddChild(_bodyLabel);

        var buttonRow = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        buttonRow.AddThemeConstantOverride("separation", 12);
        vbox.AddChild(buttonRow);

        buttonRow.AddChild(MakeButton("Back", () => Step(forward: false)));
        buttonRow.AddChild(MakeButton("Next", () => Step(forward: true)));
        buttonRow.AddChild(MakeButton("Close", () => Close("button")));

        return panel;
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
        // Live tracking: the dialog follows the finger once the gesture
        // locks horizontal (vertical-locked drags never move it).
        if (@event is InputEventMouseMotion mm)
        {
            if (_transitioning) return;
            float offset = _swipe.Drag(mm.Position.X, mm.Position.Y);
            if (_swipe.IsTrackingHorizontal)
            {
                _dialogSlider.Position = new Vector2(offset, 0f);
            }
            return;
        }

        if (@event is not InputEventMouseButton mb || mb.ButtonIndex != MouseButton.Left)
        {
            return;
        }

        // Press arms the swipe recognizer; everything is judged at release
        // (standard button semantics — a tap that turns into a drag isn't a
        // click). Touch reaches here as emulated finger-0 mouse events.
        if (mb.Pressed)
        {
            if (!_transitioning) _swipe.Press(mb.Position.X, mb.Position.Y);
            return;
        }

        // Page-turning: finger left = Next, finger right = Back. A
        // sub-threshold horizontal drag springs the dialog back instead.
        bool wasTracking = _swipe.IsTrackingHorizontal;
        switch (_swipe.Release(mb.Position.X, mb.Position.Y))
        {
            case SwipeDirection.Left: Step(forward: true, via: "swipe"); return;
            case SwipeDirection.Right: Step(forward: false, via: "swipe"); return;
        }
        if (wasTracking)
        {
            SpringBack();
            return;
        }

        // Tap: click-to-jump — hit-test against each element's rect. The
        // catcher already ate the click, so the underlying button never fires.
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

    // Keep the slider matched to the viewport; a mid-transition resize
    // snaps it home rather than stranding the dialog off-center.
    private void SyncDialogSliderRect()
    {
        _dialogSlider.Size = GetViewport().GetVisibleRect().Size;
        if (!_transitioning) _dialogSlider.Position = Vector2.Zero;
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= SyncDialogSliderRect;
    }

    /// <summary>
    /// Animated step, shared by swipe commits, the Back/Next buttons, and
    /// the arrow keys: the dialog slides off in the travel direction
    /// (from wherever the drag left it) and re-enters from the opposite
    /// side around the cursor advance. Tap-to-jump stays instant.
    /// </summary>
    private void Step(bool forward, string? via = null)
    {
        if (_transitioning || _closed) return;
        _transitioning = true;
        // Slide most of a viewport width — enough to clear the centered
        // dialog past the edge in either orientation.
        float w = GetViewport().GetVisibleRect().Size.X;
        float outX = forward ? -w : w;

        _slideTween?.Kill();
        _slideTween = CreateTween();
        _slideTween.TweenProperty(_dialogSlider, "position:x", outX, SlideOutSec)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        _slideTween.TweenCallback(Callable.From(() =>
        {
            if (forward) _cursor.Next();
            else _cursor.Prev();
            ShowCurrent(via ?? (forward ? "next" : "back"));
            _dialogSlider.Position = new Vector2(-outX, 0f);
        }));
        _slideTween.TweenProperty(_dialogSlider, "position:x", 0f, SlideInSec)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        _slideTween.TweenCallback(Callable.From(() => _transitioning = false));
    }

    // Sub-threshold drag: ease the dialog back to center, no step.
    private void SpringBack()
    {
        _slideTween?.Kill();
        _slideTween = CreateTween();
        _slideTween.TweenProperty(_dialogSlider, "position:x", 0f, SpringBackSec)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
    }

    private void ShowCurrent(string via)
    {
        Entry e = _entries[_cursor.Index];
        _titleLabel.Text = e.Title;
        _bodyLabel.Text = e.Body;
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
