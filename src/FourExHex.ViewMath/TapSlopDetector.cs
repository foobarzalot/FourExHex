// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;

/// <summary>
/// Tap-vs-drag discrimination for a tappable control living inside a
/// scroller. The control must not consume the pointer stream — otherwise the
/// scroller never sees the drag and the list can only be panned from the gaps
/// between items — so it passes the events through and asks here whether the
/// completed gesture was a tap (activate) or a drag (leave it to the
/// scroller).
///
/// Feed it <b>global</b> positions. A scroll drags the content under the
/// finger, so an item's local coordinates barely move even during a real
/// scroll; only the screen point travels.
///
/// Godot-free so the gesture logic is unit-testable, like its neighbours
/// <see cref="SwipeDetector"/> and <see cref="MultiTouchTapDetector"/>.
/// </summary>
public sealed class TapSlopDetector
{
    /// <summary>Travel beyond this (px) makes the gesture a scroll rather than
    /// a tap. A finger never releases on the exact pixel it pressed, so the
    /// threshold has to be forgiving enough for ordinary jitter.</summary>
    public const float SlopPx = 12f;

    private float _pressX;
    private float _pressY;
    private bool _pressed;

    /// <summary>Begin a gesture at the given global position.</summary>
    public void Press(float x, float y)
    {
        _pressX = x;
        _pressY = y;
        _pressed = true;
    }

    /// <summary>End the gesture. True iff it was a tap: a press of ours that
    /// traveled no further than <see cref="SlopPx"/>. One press yields at most
    /// one true — a stray second release can't re-activate anything.</summary>
    public bool Release(float x, float y)
    {
        if (!_pressed) return false;
        _pressed = false;
        float dx = x - _pressX;
        float dy = y - _pressY;
        return MathF.Sqrt(dx * dx + dy * dy) <= SlopPx;
    }

    /// <summary>Drop a gesture in flight — the host hid or rebuilt itself
    /// between press and release, so the release belongs to nothing.</summary>
    public void Cancel() => _pressed = false;
}
