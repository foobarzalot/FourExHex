// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using Xunit;

namespace FourExHex.Tests;

// Pure horizontal-swipe recognition for the paged overlays (guided tour,
// Instructions panel): Press records the start point, Release returns the
// verdict — Left/Right when the drag is long enough and mostly horizontal
// (page-turning: left = Next, right = Back at the call sites).
public class SwipeDetectorTests
{
    [Fact]
    public void LeftSwipe_AtThreshold_FiresLeft()
    {
        var d = new SwipeDetector();
        d.Press(200f, 100f);
        Assert.Equal(SwipeDirection.Left,
            d.Release(200f - SwipeDetector.MinDistancePx, 100f));
    }

    [Fact]
    public void RightSwipe_AtThreshold_FiresRight()
    {
        var d = new SwipeDetector();
        d.Press(200f, 100f);
        Assert.Equal(SwipeDirection.Right,
            d.Release(200f + SwipeDetector.MinDistancePx, 100f));
    }

    [Fact]
    public void ShortDrag_IsNone()
    {
        var d = new SwipeDetector();
        d.Press(200f, 100f);
        Assert.Equal(SwipeDirection.None,
            d.Release(200f - (SwipeDetector.MinDistancePx - 1f), 100f));
    }

    [Fact]
    public void VerticalDrag_IsNone()
    {
        var d = new SwipeDetector();
        d.Press(200f, 100f);
        Assert.Equal(SwipeDirection.None, d.Release(200f, 300f));
    }

    [Fact]
    public void DiagonalDrag_WithoutHorizontalDominance_IsNone()
    {
        // dx = 80 but dy = 50: |dx| < 2*|dy| → not a horizontal swipe.
        var d = new SwipeDetector();
        d.Press(200f, 100f);
        Assert.Equal(SwipeDirection.None, d.Release(120f, 150f));
    }

    [Fact]
    public void DiagonalDrag_WithHorizontalDominance_Fires()
    {
        // dx = -80, dy = 30: |dx| >= 2*|dy| → left swipe.
        var d = new SwipeDetector();
        d.Press(200f, 100f);
        Assert.Equal(SwipeDirection.Left, d.Release(120f, 130f));
    }

    [Fact]
    public void ReleaseWithoutPress_IsNone()
    {
        var d = new SwipeDetector();
        Assert.Equal(SwipeDirection.None, d.Release(0f, 0f));
    }

    [Fact]
    public void ReleaseDisarms_SecondReleaseIsNone()
    {
        var d = new SwipeDetector();
        d.Press(200f, 100f);
        Assert.Equal(SwipeDirection.Left, d.Release(100f, 100f));
        Assert.Equal(SwipeDirection.None, d.Release(0f, 100f));
    }

    [Fact]
    public void Cancel_Disarms()
    {
        var d = new SwipeDetector();
        d.Press(200f, 100f);
        d.Cancel();
        Assert.Equal(SwipeDirection.None, d.Release(100f, 100f));
    }

    [Fact]
    public void RearmsOnNextPress()
    {
        var d = new SwipeDetector();
        d.Press(200f, 100f);
        d.Cancel();
        d.Press(300f, 100f);
        Assert.Equal(SwipeDirection.Right, d.Release(400f, 100f));
    }

    // --- Live tracking (Drag / axis lock) --------------------------------

    [Fact]
    public void Drag_BeforeAxisLock_IsZero()
    {
        var d = new SwipeDetector();
        d.Press(100f, 100f);
        Assert.Equal(0f, d.Drag(100f + SwipeDetector.AxisLockPx - 1f, 100f));
        Assert.False(d.IsTrackingHorizontal);
    }

    [Fact]
    public void Drag_HorizontalLock_ReturnsRawDx()
    {
        var d = new SwipeDetector();
        d.Press(100f, 100f);
        Assert.Equal(15f, d.Drag(115f, 102f));      // locks horizontal, tracks
        Assert.True(d.IsTrackingHorizontal);
        Assert.Equal(-10f, d.Drag(90f, 105f));      // keeps tracking raw dx
    }

    [Fact]
    public void Drag_VerticalLock_StaysZero_AndReleaseIsNone()
    {
        var d = new SwipeDetector();
        d.Press(100f, 100f);
        Assert.Equal(0f, d.Drag(102f, 115f));       // locks vertical
        Assert.False(d.IsTrackingHorizontal);
        Assert.Equal(0f, d.Drag(250f, 120f));       // later horizontal hook: still 0
        // Even though the final dx would qualify, a vertical-locked
        // gesture never pages.
        Assert.Equal(SwipeDirection.None, d.Release(250f, 120f));
    }

    [Fact]
    public void Drag_Unarmed_IsZero()
    {
        var d = new SwipeDetector();
        Assert.Equal(0f, d.Drag(500f, 500f));
        Assert.False(d.IsTrackingHorizontal);
    }

    [Fact]
    public void AxisLock_ResetsOnNextPress()
    {
        var d = new SwipeDetector();
        d.Press(100f, 100f);
        d.Drag(100f, 130f);                          // vertical lock
        d.Release(100f, 130f);

        d.Press(100f, 100f);
        Assert.Equal(20f, d.Drag(120f, 100f));       // fresh horizontal lock
        Assert.True(d.IsTrackingHorizontal);
        Assert.Equal(SwipeDirection.Right, d.Release(200f, 100f));
    }
}
