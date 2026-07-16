// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using Godot;

/// <summary>
/// Two-panel slide host for swipe-paged surfaces (the Instructions
/// panel and the guided tour's dialog): a clipping viewport holding a
/// <b>front</b> page (the one visible at idle) and a <b>back</b> page
/// that peeks in beside it during a drag and slides to center on
/// commit — one continuous page turn, both panels visible throughout.
///
/// The caller owns page content: it populates the back page for
/// whichever neighbor a drag reveals, then drives <see cref="Track"/> /
/// <see cref="Commit"/> / <see cref="SpringBack"/>. Placement is
/// offsets-only (<see cref="PlaceX"/>): the pages are full-rect
/// anchored, and writing <c>Position</c> on such a control bakes its
/// current effective size into the offsets, inflating it past the clip.
/// </summary>
public sealed partial class PageCarousel : Control
{
    public const float SlideSec = 0.18f;
    public const float SpringBackSec = 0.15f;

    private Control _front = null!;
    private Control _back = null!;
    private Tween? _tween;

    /// <summary>True while a commit slide is running; callers ignore new
    /// gestures until it lands.</summary>
    public bool Transitioning { get; private set; }

    /// <summary>The idle-visible page.</summary>
    public Control Front => _front;

    /// <summary>The peek/incoming page. Populate before revealing.</summary>
    public Control Back => _back;

    public PageCarousel(Control front, Control back)
    {
        ClipContents = true;
        MouseFilter = MouseFilterEnum.Ignore;
        _front = front;
        _back = back;
        foreach (Control page in new[] { front, back })
        {
            page.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            // Pages are click-through slabs; interactive children (dialog
            // buttons) still hit-test on their own. The tour relies on
            // clicks outside its dialog reaching the catcher below.
            page.MouseFilter = MouseFilterEnum.Ignore;
            AddChild(page);
        }
        _back.Visible = false;
        Resized += () =>
        {
            if (!Transitioning) Home();
        };
    }

    /// <summary>Live drag: front at <paramref name="offsetX"/>, back
    /// beside it on the revealed side, visible while displaced.</summary>
    public void Track(float offsetX)
    {
        PlaceX(_front, offsetX);
        PlaceX(_back, offsetX < 0f ? offsetX + Size.X : offsetX - Size.X);
        _back.Visible = offsetX != 0f;
    }

    /// <summary>
    /// Slide front out and back to center together (from wherever the
    /// drag left them), swap the roles at landing, then run
    /// <paramref name="onLanded"/>. The back page must already be
    /// populated (and, if it wasn't revealed by a drag, positioned by a
    /// preceding <c>Track</c> call or it starts from its home slot).
    /// </summary>
    public void Commit(bool forward, Action onLanded)
    {
        if (Transitioning) return;
        Transitioning = true;

        float w = Size.X;
        float sign = forward ? -1f : 1f;   // front exits left going forward
        Control front = _front;
        Control back = _back;
        if (!back.Visible)
        {
            // Button/key paging without a drag: start the back page at
            // the side it would have been revealed from.
            PlaceX(back, -sign * w);
            back.Visible = true;
        }

        _tween?.Kill();
        _tween = CreateTween();
        _tween.SetParallel(true);
        _tween.TweenMethod(Callable.From((float x) => PlaceX(front, x)),
                front.OffsetLeft, sign * w, SlideSec)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        _tween.TweenMethod(Callable.From((float x) => PlaceX(back, x)),
                back.OffsetLeft, 0f, SlideSec)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        _tween.Chain().TweenCallback(Callable.From(() =>
        {
            (_front, _back) = (back, front);
            Transitioning = false;
            Home();
            onLanded();
        }));
    }

    /// <summary>Sub-threshold drag: ease both pages home, no swap.</summary>
    public void SpringBack()
    {
        Control front = _front;
        Control back = _back;
        float backHome = back.OffsetLeft < front.OffsetLeft ? -Size.X : Size.X;
        _tween?.Kill();
        _tween = CreateTween();
        _tween.SetParallel(true);
        _tween.TweenMethod(Callable.From((float x) => PlaceX(front, x)),
                front.OffsetLeft, 0f, SpringBackSec)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        _tween.TweenMethod(Callable.From((float x) => PlaceX(back, x)),
                back.OffsetLeft, backHome, SpringBackSec)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        _tween.Chain().TweenCallback(Callable.From(Home));
    }

    // Idle state: front centered and visible, back parked hidden.
    private void Home()
    {
        PlaceX(_front, 0f);
        PlaceX(_back, Size.X);
        _back.Visible = false;
    }

    // Horizontal placement for a full-rect-anchored page: matching
    // left/right offsets shift the page while it keeps the clip's size.
    private static void PlaceX(Control page, float x)
    {
        page.OffsetLeft = x;
        page.OffsetRight = x;
        page.OffsetTop = 0f;
        page.OffsetBottom = 0f;
    }
}
