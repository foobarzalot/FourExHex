// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using Xunit;

namespace FourExHex.Tests;

// Pure geometry behind the layout audit: given a Control's resolved global rect,
// the viewport, and the safe-area insets, decide whether it is a violation and
// which kind. This is the detector the view-matrix harness depends on, so it is
// pinned here rather than only exercised through a running scene — the Godot
// side (scripts/LayoutAudit.cs) contributes only traversal and logging.
public class LayoutAssertTests
{
    private static readonly LayoutRect Viewport = new(0f, 0f, 1280f, 800f);

    private static LayoutViolation Check(
        LayoutRect rect, LogicalSafeInsets safe = default,
        bool visible = true, float minWidth = 0f, float minHeight = 0f,
        bool enforceSafeArea = true)
    {
        LayoutAssert.TryFindViolation(
            rect, Viewport, safe, visible, minWidth, minHeight,
            enforceSafeArea, LayoutAssert.TolerancePx, out LayoutViolation v);
        return v;
    }

    [Fact]
    public void RectFullyInsideViewport_IsNotAViolation()
    {
        Assert.Equal(LayoutViolationKind.None, Check(new LayoutRect(10f, 10f, 200f, 44f)).Kind);
    }

    [Fact]
    public void RectPastRightEdge_ReportsOverflowWithTheOvershootInPx()
    {
        // Right edge lands at 1318 against a 1280-wide viewport.
        LayoutViolation v = Check(new LayoutRect(1198f, 700f, 120f, 44f));

        Assert.Equal(LayoutViolationKind.OverflowsViewport, v.Kind);
        Assert.Equal(38f, v.OverflowRight, 3);
        Assert.Equal(0f, v.OverflowLeft, 3);
        Assert.Equal(0f, v.OverflowTop, 3);
    }

    [Fact]
    public void RectPastTopAndLeftEdges_ReportsBothOvershoots()
    {
        LayoutViolation v = Check(new LayoutRect(-12f, -5f, 100f, 40f));

        Assert.Equal(LayoutViolationKind.OverflowsViewport, v.Kind);
        Assert.Equal(12f, v.OverflowLeft, 3);
        Assert.Equal(5f, v.OverflowTop, 3);
    }

    // Godot container sorts land on half-pixel boundaries and Scale-based fits
    // accumulate float error; sub-pixel overshoot is not a bug.
    [Fact]
    public void SubPixelOvershoot_IsWithinToleranceAndIgnored()
    {
        Assert.Equal(
            LayoutViolationKind.None,
            Check(new LayoutRect(0f, 0f, 1280.4f, 800f)).Kind);
    }

    [Fact]
    public void InteractiveRectUnderTheNotch_IntrudesSafeArea()
    {
        // 47px Dynamic Island; a button starting at y=20 sits under it.
        LayoutViolation v = Check(
            new LayoutRect(100f, 20f, 200f, 44f),
            safe: new LogicalSafeInsets(Top: 47f, Bottom: 34f, Left: 0f, Right: 0f));

        Assert.Equal(LayoutViolationKind.IntrudesSafeArea, v.Kind);
        Assert.Equal(27f, v.OverflowTop, 3);
    }

    [Fact]
    public void RectBelowTheNotch_IsClean()
    {
        Assert.Equal(
            LayoutViolationKind.None,
            Check(new LayoutRect(100f, 60f, 200f, 44f),
                  safe: new LogicalSafeInsets(47f, 34f, 0f, 0f)).Kind);
    }

    // Full-rect scrims and panel chrome legitimately extend under the notch —
    // the design rule is "nothing tappable or readable there", so the caller
    // decides per node whether the safe area is enforced.
    [Fact]
    public void NonInteractiveRectUnderTheNotch_IsExemptWhenNotEnforced()
    {
        Assert.Equal(
            LayoutViolationKind.None,
            Check(new LayoutRect(0f, 0f, 1280f, 800f),
                  safe: new LogicalSafeInsets(47f, 34f, 0f, 0f),
                  enforceSafeArea: false).Kind);
    }

    // HudPanelMath.ClampWidth has no lower clamp and PanelFitMath.AvailableBox
    // subtracts without flooring, so a narrow viewport really can invert a rect.
    [Fact]
    public void NegativeWidth_ReportsNegativeSize()
    {
        Assert.Equal(
            LayoutViolationKind.NegativeSize,
            Check(new LayoutRect(100f, 100f, -3f, 40f)).Kind);
    }

    // A degenerate rect overflows too; reporting one node once beats four lines.
    [Fact]
    public void NegativeSize_OutranksOverflow()
    {
        Assert.Equal(
            LayoutViolationKind.NegativeSize,
            Check(new LayoutRect(-50f, 100f, -3f, 40f)).Kind);
    }

    [Fact]
    public void VisibleControlThatAskedForSpaceAndGotNone_ReportsZeroSize()
    {
        Assert.Equal(
            LayoutViolationKind.ZeroSizeButVisible,
            Check(new LayoutRect(100f, 100f, 200f, 0f), minHeight: 44f).Kind);
    }

    // An empty container measuring zero is ordinary, not a bug.
    [Fact]
    public void ZeroSizedControlThatAskedForNothing_IsClean()
    {
        Assert.Equal(
            LayoutViolationKind.None,
            Check(new LayoutRect(100f, 100f, 0f, 0f)).Kind);
    }

    [Fact]
    public void HiddenControl_IsNeverAViolation()
    {
        Assert.Equal(
            LayoutViolationKind.None,
            Check(new LayoutRect(-9999f, 0f, 200f, 44f), visible: false).Kind);
    }

    // Modal scrims and full-bleed backgrounds are deliberately sized to cover
    // everything, often with slack. A control that fully contains the viewport
    // is a backdrop, not content that escaped its container — flagging it would
    // fire on every modal in every cell and bury the real findings.
    [Fact]
    public void ControlThatFullyCoversTheViewport_IsABackdropNotAnOverflow()
    {
        Assert.Equal(
            LayoutViolationKind.None,
            Check(new LayoutRect(0f, 0f, 2400f, 1880f)).Kind);
    }

    [Fact]
    public void BackdropExemption_DoesNotExcuseAControlCoveringOnlyOneAxis()
    {
        // Spans the full width and beyond, but sits inside vertically: real content.
        Assert.Equal(
            LayoutViolationKind.OverflowsViewport,
            Check(new LayoutRect(-10f, 100f, 1400f, 200f)).Kind);
    }

    [Fact]
    public void BackdropExemption_StillReportsADegenerateRect()
    {
        Assert.Equal(
            LayoutViolationKind.NegativeSize,
            Check(new LayoutRect(0f, 0f, -2400f, 1880f)).Kind);
    }

    // Godot ellipsizes or clips text INSIDE a Label without changing the
    // Label's rect, so a panel that fits perfectly can still read
    // "Confirm Purchas…". Rect-only auditing cannot see it — which matters most
    // right after a fix shrinks a panel to stop it overflowing.
    [Fact]
    public void TextWiderThanItsControl_ReportsTruncation()
    {
        LayoutAssert.TryFindTextTruncation(
            controlWidth: 280f, controlHeight: 25f,
            desiredWidth: 340f, desiredHeight: 25f,
            tolerance: LayoutAssert.TolerancePx,
            out LayoutViolation v);

        Assert.Equal(LayoutViolationKind.TextTruncated, v.Kind);
        Assert.Equal(60f, v.OverflowRight, 3);
    }

    // Height is deliberately NOT a truncation signal. A control shorter than its
    // measured minimum is nearly always theme content-margin accounting, not
    // lost text: measured across the real screens, the height axis produced 90
    // findings to the width axis's 28, and none of the 90 were real clipping.
    // Keeping it would have made the check useless noise.
    [Fact]
    public void TextTallerThanItsControl_IsNotReported()
    {
        Assert.False(LayoutAssert.TryFindTextTruncation(
            controlWidth: 280f, controlHeight: 25f,
            desiredWidth: 200f, desiredHeight: 48f,
            tolerance: LayoutAssert.TolerancePx,
            out _));
    }

    [Fact]
    public void TextThatFits_IsNotTruncation()
    {
        Assert.False(LayoutAssert.TryFindTextTruncation(
            controlWidth: 280f, controlHeight: 25f,
            desiredWidth: 240f, desiredHeight: 22f,
            tolerance: LayoutAssert.TolerancePx,
            out _));
    }

    // Text measurement lands on fractional pixels constantly; without slack this
    // would fire on nearly every label in the game.
    [Fact]
    public void SubPixelTextOvershoot_IsWithinTolerance()
    {
        Assert.False(LayoutAssert.TryFindTextTruncation(
            controlWidth: 280f, controlHeight: 25f,
            desiredWidth: 280.3f, desiredHeight: 25f,
            tolerance: LayoutAssert.TolerancePx,
            out _));
    }

    [Fact]
    public void SafeBox_DeflatesTheViewportByTheInsets()
    {
        LayoutRect box = LayoutAssert.SafeBox(Viewport, new LogicalSafeInsets(47f, 34f, 10f, 20f));

        Assert.Equal(10f, box.X, 3);
        Assert.Equal(47f, box.Y, 3);
        Assert.Equal(1250f, box.Width, 3);   // 1280 - 10 - 20
        Assert.Equal(719f, box.Height, 3);   // 800 - 47 - 34
    }

    [Fact]
    public void Describe_NamesTheKindAndTheOffendingEdges()
    {
        LayoutViolation v = Check(new LayoutRect(1198f, 700f, 120f, 44f));
        string text = LayoutAssert.Describe(v);

        Assert.Contains("OverflowsViewport", text);
        Assert.Contains("r=38", text);
        Assert.DoesNotContain("l=", text);   // clean edges stay out of the line
    }
}
