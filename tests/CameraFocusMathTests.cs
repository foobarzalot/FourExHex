// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using Xunit;

namespace FourExHex.Tests;

public class CameraFocusMathTests
{
    // 1000×800 viewport, no insets, 60% comfort box → box spans
    // x ∈ [200, 800], y ∈ [160, 640].
    [Theory]
    [InlineData(500f, 400f, false)]  // dead center
    [InlineData(210f, 400f, false)]  // just inside left edge
    [InlineData(190f, 400f, true)]   // just outside left edge
    [InlineData(500f, 650f, true)]   // just below bottom edge
    [InlineData(-50f, 400f, true)]   // off-viewport entirely
    [InlineData(500f, 900f, true)]   // beyond viewport bottom
    public void NoInsets_SixtyPercentBox(float x, float y, bool expected)
    {
        Assert.Equal(expected, CameraFocusMath.IsOutsideComfortZone(
            1000f, 800f, topInset: 0f, bottomInset: 0f, x: x, y: y, comfortFrac: 0.6f));
    }

    [Fact]
    public void Insets_ShiftTheComfortZoneCenter()
    {
        // 1000×800 with a 200-px top inset: play area y ∈ [200, 800],
        // center y = 500. A 50% box spans y ∈ [350, 650] — so y=300 (inside
        // the no-inset box) is now outside, and y=620 (outside the no-inset
        // box) is now inside.
        Assert.True(CameraFocusMath.IsOutsideComfortZone(
            1000f, 800f, topInset: 200f, bottomInset: 0f, x: 500f, y: 300f, comfortFrac: 0.5f));
        Assert.False(CameraFocusMath.IsOutsideComfortZone(
            1000f, 800f, topInset: 200f, bottomInset: 0f, x: 500f, y: 620f, comfortFrac: 0.5f));
    }

    [Fact]
    public void FullComfortFrac_OnlyOffPlayAreaIsOutside()
    {
        Assert.False(CameraFocusMath.IsOutsideComfortZone(
            1000f, 800f, 0f, 0f, x: 990f, y: 790f, comfortFrac: 1f));
        Assert.True(CameraFocusMath.IsOutsideComfortZone(
            1000f, 800f, 0f, 0f, x: 1010f, y: 400f, comfortFrac: 1f));
    }
}
