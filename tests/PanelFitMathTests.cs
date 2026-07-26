// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using Xunit;

namespace FourExHex.Tests;

public class PanelFitMathTests
{
    private const float Tolerance = 0.0001f;

    // ---- AvailableBox ----------------------------------------------------

    [Fact]
    public void AvailableBox_SubtractsInsetsAndDoubleMargin()
    {
        // safe (Top,Bottom,Left,Right) = (40,30,20,10), margin 24 per side.
        var safe = new LogicalSafeInsets(40f, 30f, 20f, 10f);
        (float w, float h) = PanelFitMath.AvailableBox(1000f, 800f, safe, 24f);
        Assert.Equal(1000f - 20f - 10f - 48f, w, Tolerance); // 922
        Assert.Equal(800f - 40f - 30f - 48f, h, Tolerance);  // 682
    }

    [Fact]
    public void AvailableBox_ZeroInsets_IsViewportMinusDoubleMargin()
    {
        (float w, float h) = PanelFitMath.AvailableBox(800f, 600f, LogicalSafeInsets.Zero, 12f);
        Assert.Equal(776f, w, Tolerance);
        Assert.Equal(576f, h, Tolerance);
    }

    // ---- ScaleToFit ------------------------------------------------------

    [Fact]
    public void ScaleToFit_DesignFits_ClampsToOne_NeverUpscales()
    {
        // design 400x300 inside avail 1000x800 => both ratios > 1 => clamp to 1.
        Assert.Equal(1f, PanelFitMath.ScaleToFit(400f, 300f, 1000f, 800f), Tolerance);
    }

    [Fact]
    public void ScaleToFit_PicksBindingAxis()
    {
        // design 1000x500, avail 500x500 => X-fit 0.5, Y-fit 1.0 => 0.5.
        Assert.Equal(0.5f, PanelFitMath.ScaleToFit(1000f, 500f, 500f, 500f), Tolerance);
        // design 500x1000, avail 500x250 => Y binds at 0.25.
        Assert.Equal(0.25f, PanelFitMath.ScaleToFit(500f, 1000f, 500f, 250f), Tolerance);
    }

    [Theory]
    [InlineData(0f, 300f)]
    [InlineData(400f, 0f)]
    [InlineData(0f, 0f)]
    public void ScaleToFit_DegenerateDesign_ReturnsOne(float designW, float designH)
    {
        Assert.Equal(1f, PanelFitMath.ScaleToFit(designW, designH, 500f, 500f), Tolerance);
    }

    // ---- WidthFitWithHeightCap ------------------------------------------

    [Fact]
    public void WidthFitWithHeightCap_WideEnough_ScaleOne_HeightCappedToDesign()
    {
        // availW 600 >= designW 456 => scale 1; availH 800 >= designH 540 => panelH 540.
        (float scale, float panelH) = PanelFitMath.WidthFitWithHeightCap(456f, 540f, 600f, 800f);
        Assert.Equal(1f, scale, Tolerance);
        Assert.Equal(540f, panelH, Tolerance);
    }

    [Fact]
    public void WidthFitWithHeightCap_ShortViewport_CapsHeightNotScale()
    {
        // availW 600 >= 456 => scale 1; availH 300 < designH 540 => panelH capped to 300.
        (float scale, float panelH) = PanelFitMath.WidthFitWithHeightCap(456f, 540f, 600f, 300f);
        Assert.Equal(1f, scale, Tolerance);
        Assert.Equal(300f, panelH, Tolerance);
    }

    [Fact]
    public void WidthFitWithHeightCap_NarrowViewport_WidthOnlyShrink_HeightCapDividesByScale()
    {
        // availW 228 < designW 456 => scale 0.5; maxLogicalH = availH/scale = 270/0.5 = 540;
        // panelH = min(540, 540) = 540.
        (float scale, float panelH) = PanelFitMath.WidthFitWithHeightCap(456f, 540f, 228f, 270f);
        Assert.Equal(0.5f, scale, Tolerance);
        Assert.Equal(540f, panelH, Tolerance);
    }

    [Fact]
    public void WidthFitWithHeightCap_ZeroWidth_FallsBackToDesignHeight()
    {
        // availW 0 => scale 0 => the scale>0 guard falls back to designH for the cap.
        (float scale, float panelH) = PanelFitMath.WidthFitWithHeightCap(456f, 540f, 0f, 300f);
        Assert.Equal(0f, scale, Tolerance);
        Assert.Equal(540f, panelH, Tolerance);
    }

    // ---- CappedFill ------------------------------------------------------

    [Fact]
    public void CappedFill_SmallViewport_FillsAvailUnderCap()
    {
        // viewport 700x400, no insets, edge 12 => avail 676x376, both under cap 920x520.
        var (w, h) = PanelFitMath.CappedFill(700f, 400f, LogicalSafeInsets.Zero, 12f, 920f, 520f);
        Assert.Equal(676f, w, Tolerance);
        Assert.Equal(376f, h, Tolerance);
    }

    [Fact]
    public void CappedFill_LargeViewport_CapsToMax()
    {
        // viewport 1600x1080 => avail 1576x1056 => capped to 920x520.
        var (w, h) = PanelFitMath.CappedFill(1600f, 1080f, LogicalSafeInsets.Zero, 12f, 920f, 520f);
        Assert.Equal(920f, w, Tolerance);
        Assert.Equal(520f, h, Tolerance);
    }

    [Fact]
    public void CappedFill_TinyViewport_FloorsAtZero()
    {
        // viewport smaller than 2*edge => avail would be negative => floored to 0.
        var (w, h) = PanelFitMath.CappedFill(10f, 10f, LogicalSafeInsets.Zero, 12f, 920f, 520f);
        Assert.Equal(0f, w, Tolerance);
        Assert.Equal(0f, h, Tolerance);
    }

    [Fact]
    public void CappedFill_SubtractsInsets()
    {
        var safe = new LogicalSafeInsets(40f, 30f, 50f, 10f);
        var (w, h) = PanelFitMath.CappedFill(1600f, 1080f, safe, 12f, 920f, 520f);
        // avail w = 1600-50-10-24 = 1516 -> capped 920; h = 1080-40-30-24 = 986 -> 520.
        Assert.Equal(920f, w, Tolerance);
        Assert.Equal(520f, h, Tolerance);
    }

    // ---- ContentShrinkScale ----------------------------------------------

    [Fact]
    public void ContentShrinkScale_MiniLandscape_ShrinksToQuantizedFit()
    {
        // iPhone 13 mini landscape: play-config interior ~325px vs ~360px of
        // content minimum => ratio 0.9027 => floor-quantized to 0.90.
        Assert.Equal(0.90f, PanelFitMath.ContentShrinkScale(360f, 1f, 325f), Tolerance);
    }

    [Fact]
    public void ContentShrinkScale_ComposesWithCappedFill_MiniLandscapeNumbers()
    {
        // Mini landscape logical 921x425, safe insets (t0 b21 l47 r47), edge 12:
        // surface h = min(425-0-21-24, 520) = 380; interior = 380 - 2*26 = 328.
        var safe = new LogicalSafeInsets(0f, 21f, 47f, 47f);
        var (_, h) = PanelFitMath.CappedFill(921f, 425f, safe, 12f, 920f, 520f);
        Assert.Equal(380f, h, Tolerance);
        float interior = h - 52f;
        // measured 365 => ratio 0.8987 => quantized down to 0.85.
        Assert.Equal(0.85f, PanelFitMath.ContentShrinkScale(365f, 1f, interior), Tolerance);
    }

    [Fact]
    public void ContentShrinkScale_ContentFits_ReturnsOne()
    {
        // Mini portrait: content 743 fits interior 753 => no shrink.
        Assert.Equal(1f, PanelFitMath.ContentShrinkScale(743f, 1f, 753f), Tolerance);
    }

    [Fact]
    public void ContentShrinkScale_ContentJustOverflows_ShrinksOneQuantum()
    {
        // 780 into 753 => ratio 0.9654 => quantized to 0.95.
        Assert.Equal(0.95f, PanelFitMath.ContentShrinkScale(780f, 1f, 753f), Tolerance);
    }

    [Fact]
    public void ContentShrinkScale_StableUnderRemeasurement()
    {
        // Re-measuring a shrunk panel and mapping back through the scale it was
        // built at must converge, not oscillate: 706 measured at 0.95 recovers
        // design ~743 => target 0.90; the 0.90-built panel measures ~669 and
        // recovers the same design => target stays 0.90.
        Assert.Equal(0.90f, PanelFitMath.ContentShrinkScale(706f, 0.95f, 700f), Tolerance);
        Assert.Equal(0.90f, PanelFitMath.ContentShrinkScale(669f, 0.90f, 700f), Tolerance);
    }

    [Fact]
    public void ContentShrinkScale_NeverUpscales()
    {
        // Plenty of room (desktop window): stays at 1, never grows past design.
        Assert.Equal(1f, PanelFitMath.ContentShrinkScale(743f, 1f, 868f), Tolerance);
    }

    [Theory]
    [InlineData(100f)] // far too small => raw ratio 0.14 clamps up to the floor
    [InlineData(0f)]   // degenerate zero-height interior
    public void ContentShrinkScale_ClampsToMinScale(float availH)
    {
        Assert.Equal(0.65f, PanelFitMath.ContentShrinkScale(700f, 1f, availH), Tolerance);
    }

    [Fact]
    public void ContentShrinkScale_ExactQuantumBoundary_DoesNotFloatFloorDown()
    {
        // 340/400 = 0.85 exactly: the epsilon keeps float noise from flooring
        // an on-boundary ratio to the next quantum down (0.80).
        Assert.Equal(0.85f, PanelFitMath.ContentShrinkScale(400f, 1f, 340f), Tolerance);
    }

    [Fact]
    public void ContentShrinkScale_DegenerateMeasurement_ReturnsOne()
    {
        Assert.Equal(1f, PanelFitMath.ContentShrinkScale(0f, 1f, 500f), Tolerance);
    }

    [Fact]
    public void ContentShrinkScale_ZeroMeasuredAtScale_TreatsMeasuredAsDesign()
    {
        // Guard: a 0 built-at scale can't divide; the measurement stands in for
        // the design height directly. 360 into 325 => 0.90 as usual.
        Assert.Equal(0.90f, PanelFitMath.ContentShrinkScale(360f, 0f, 325f), Tolerance);
    }

    [Fact]
    public void ContentShrinkScale_HoldsBuiltScale_WhenGrowthWouldBeMarginal()
    {
        // Floored scaled metrics under-report the design height: true design
        // 757 measures 716 at 0.95 (recovered ~753.7). With avail 754 the
        // recovered design "fits" by a hair, but growing to 1 would measure
        // 757, not fit, shrink again — an endless oscillation. Growth needs
        // margin; inside the hysteresis band the built scale holds.
        Assert.Equal(0.95f, PanelFitMath.ContentShrinkScale(716f, 0.95f, 754f), Tolerance);
    }

    [Fact]
    public void ContentShrinkScale_GrowsBack_WhenFitsWithMargin()
    {
        // Same shrunk panel, but the window grew: recovered design ~753.7 fits
        // avail 800 with room beyond the hysteresis margin => back to 1.
        Assert.Equal(1f, PanelFitMath.ContentShrinkScale(716f, 0.95f, 800f), Tolerance);
    }
}
