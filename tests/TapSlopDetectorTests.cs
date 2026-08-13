// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using Xunit;

namespace FourExHex.Tests;

// Tap-vs-drag discrimination for pointer gestures that start on a tappable
// child of a ScrollContainer. Without it the child swallows the touch and the
// list can't be drag-scrolled; with it a press that travels is left alone so
// the scroller pans, and only a press that stays put activates the child.
// The view feeds GLOBAL positions: a scroll drags the content under the
// finger, so local coordinates barely move even during a real scroll.
public class TapSlopDetectorTests
{
    [Fact]
    public void PressThenReleaseAtSamePoint_IsATap()
    {
        var detector = new TapSlopDetector();
        detector.Press(100f, 200f);

        Assert.True(detector.Release(100f, 200f));
    }

    [Fact]
    public void SmallJitterWithinSlop_IsStillATap()
    {
        // A finger never releases on the exact pixel it pressed.
        var detector = new TapSlopDetector();
        detector.Press(100f, 200f);

        Assert.True(detector.Release(103f, 204f)); // travel 5px < 12px
    }

    [Fact]
    public void VerticalDragBeyondSlop_IsNotATap()
    {
        // The reported bug: dragging up/down the list must scroll, not select.
        var detector = new TapSlopDetector();
        detector.Press(100f, 200f);

        Assert.False(detector.Release(100f, 260f));
    }

    [Fact]
    public void HorizontalDragBeyondSlop_IsNotATap()
    {
        var detector = new TapSlopDetector();
        detector.Press(100f, 200f);

        Assert.False(detector.Release(160f, 200f));
    }

    [Fact]
    public void DiagonalTravel_UsesDistanceNotPerAxis()
    {
        // 9px on each axis is under the threshold per-axis but ~12.7px of
        // actual travel — a naive per-axis test would call this a tap.
        var detector = new TapSlopDetector();
        detector.Press(0f, 0f);

        Assert.False(detector.Release(9f, 9f));
    }

    [Fact]
    public void TravelExactlyAtSlop_IsATap()
    {
        // The boundary belongs to the tap: only travel strictly beyond the
        // slop is a drag, matching "moved more than this is a scroll".
        var detector = new TapSlopDetector();
        detector.Press(0f, 0f);

        Assert.True(detector.Release(TapSlopDetector.SlopPx, 0f));
    }

    [Fact]
    public void ReleaseWithoutPress_IsNotATap()
    {
        // A release arriving with no press of ours — the press was consumed
        // elsewhere, or the panel was rebuilt mid-gesture.
        var detector = new TapSlopDetector();

        Assert.False(detector.Release(100f, 200f));
    }

    [Fact]
    public void SecondReleaseAfterATap_IsNotATap()
    {
        // One press yields at most one verdict; a stray second release must
        // not re-activate the row.
        var detector = new TapSlopDetector();
        detector.Press(100f, 200f);
        Assert.True(detector.Release(100f, 200f));

        Assert.False(detector.Release(100f, 200f));
    }

    [Fact]
    public void PressAfterAReleasedGesture_StartsFresh()
    {
        var detector = new TapSlopDetector();
        detector.Press(0f, 0f);
        detector.Release(500f, 500f); // a drag

        detector.Press(300f, 300f);
        Assert.True(detector.Release(302f, 301f));
    }

    [Fact]
    public void Cancel_DropsTheGestureInFlight()
    {
        // The dialog hides or rebuilds its rows mid-press; the release that
        // arrives afterwards must not activate anything.
        var detector = new TapSlopDetector();
        detector.Press(100f, 200f);
        detector.Cancel();

        Assert.False(detector.Release(100f, 200f));
    }
}
