// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using Xunit;

namespace FourExHex.Tests;

public class PanMathTests
{
    private const float Tolerance = 0.0001f;

    // ---- VisualCenter ----------------------------------------------------

    [Fact]
    public void VisualCenter_SymmetricInsets_CentersInRemainingArea()
    {
        // 1920x1080, 60 top + 60 bottom => availY 960, center at 60 + 480 = 540.
        (float x, float y) = PanMath.VisualCenter(1920f, 1080f, 60f, 60f);
        Assert.Equal(960f, x, Tolerance);
        Assert.Equal(540f, y, Tolerance);
    }

    [Fact]
    public void VisualCenter_AsymmetricInsets_ShiftsTowardLargerBottom()
    {
        // 1000x1000, 200 top + 0 bottom => availY 800, center y = 200 + 400 = 600.
        (float x, float y) = PanMath.VisualCenter(1000f, 1000f, 200f, 0f);
        Assert.Equal(500f, x, Tolerance);
        Assert.Equal(600f, y, Tolerance);
    }

    [Fact]
    public void VisualCenter_ZeroInsets_IsGeometricCenter()
    {
        (float x, float y) = PanMath.VisualCenter(800f, 600f, 0f, 0f);
        Assert.Equal(400f, x, Tolerance);
        Assert.Equal(300f, y, Tolerance);
    }

    // ---- Clamp: fitting axis (board+pad smaller than available) ----------
    // A fitting axis is pannable across its slack: the reachable range runs
    // from one viewport alignment to the other (board edge pad-clear of the
    // viewport edge at each extreme). Desired values inside the range pass
    // through; beyond it they clamp to the alignment endpoint.

    [Fact]
    public void Clamp_FittingAxis_PassesDesiredThroughInsideRange()
    {
        // vp 1920x1080, no insets. Board box [0,0,400,300], no pad.
        // x range [-minX, availX-maxX] = [0, 1520]; y range [0, 780].
        (float x, float y) = PanMath.Clamp(
            desiredX: 200f, desiredY: 700f,
            vpWidth: 1920f, vpHeight: 1080f, topInset: 0f, bottomInset: 0f,
            boxMinX: 0f, boxMinY: 0f, boxMaxX: 400f, boxMaxY: 300f,
            scaledPad: 0f);
        Assert.Equal(200f, x, Tolerance);
        Assert.Equal(700f, y, Tolerance);
    }

    [Fact]
    public void Clamp_FittingAxis_ClampsToAlignmentEndpoints()
    {
        // Same board: beyond the range, desired clamps to the alignments.
        (float xHi, float yHi) = PanMath.Clamp(
            9999f, 9999f, 1920f, 1080f, 0f, 0f, 0f, 0f, 400f, 300f, 0f);
        Assert.Equal(1520f, xHi, Tolerance);   // right-aligned: 1920-400
        Assert.Equal(780f, yHi, Tolerance);    // bottom-aligned: 1080-300
        (float xLo, float yLo) = PanMath.Clamp(
            -5000f, -5000f, 1920f, 1080f, 0f, 0f, 0f, 0f, 400f, 300f, 0f);
        Assert.Equal(0f, xLo, Tolerance);      // left-aligned
        Assert.Equal(0f, yLo, Tolerance);      // top-aligned
    }

    [Fact]
    public void Clamp_CenteredDesired_StaysCentered()
    {
        // The centering flows (RecenterMap / CenterOnCoord) pass a centered
        // desired through Clamp — it must round-trip unchanged.
        (float x, float y) = PanMath.Clamp(
            760f, 390f, 1920f, 1080f, 0f, 0f, 0f, 0f, 400f, 300f, 0f);
        Assert.Equal(760f, x, Tolerance);
        Assert.Equal(390f, y, Tolerance);
    }

    [Fact]
    public void Clamp_FittingAxis_RespectsTopInset()
    {
        // vp 1000x1000, top 100 bottom 0 => availY 900. Board box [0,0,200,200].
        // y range [topInset - minY, topInset + availY - maxY] = [100, 800].
        Assert.Equal(100f, PanMath.Clamp(
            0f, 0f, 1000f, 1000f, 100f, 0f, 0f, 0f, 200f, 200f, 0f).y, Tolerance);
        Assert.Equal(800f, PanMath.Clamp(
            0f, 9999f, 1000f, 1000f, 100f, 0f, 0f, 0f, 200f, 200f, 0f).y, Tolerance);
        Assert.Equal(450f, PanMath.Clamp(
            0f, 450f, 1000f, 1000f, 100f, 0f, 0f, 0f, 200f, 200f, 0f).y, Tolerance);
    }

    [Fact]
    public void Clamp_PortraitMaxZoom_PerpendicularAxisPansAcrossItsSlack()
    {
        // The #243 repro shape: portrait avail 425x900, board overflowing X
        // (600 > 425) while fitting Y (700 <= 900). Y must pan across its
        // slack [0, 200] instead of pinning to center.
        Assert.Equal(150f, PanMath.Clamp(
            0f, 150f, 425f, 900f, 0f, 0f, 0f, 0f, 600f, 700f, 0f).y, Tolerance);
        Assert.Equal(200f, PanMath.Clamp(
            0f, 9999f, 425f, 900f, 0f, 0f, 0f, 0f, 600f, 700f, 0f).y, Tolerance);
        Assert.Equal(0f, PanMath.Clamp(
            0f, -5f, 425f, 900f, 0f, 0f, 0f, 0f, 600f, 700f, 0f).y, Tolerance);
        // X still clamps into its overflow range [-175, 0].
        Assert.Equal(-175f, PanMath.Clamp(
            -9999f, 0f, 425f, 900f, 0f, 0f, 0f, 0f, 600f, 700f, 0f).x, Tolerance);
        Assert.Equal(0f, PanMath.Clamp(
            9999f, 0f, 425f, 900f, 0f, 0f, 0f, 0f, 600f, 700f, 0f).x, Tolerance);
    }

    // ---- Clamp: range branch (board larger than available) ---------------

    [Fact]
    public void Clamp_BoardWiderThanAvail_ClampsDesiredIntoRange()
    {
        // vp 800 wide, board box [0,0,2000,300]. boxW 2000 > 800 => clamp branch.
        // x range = [availX - maxX, -minX] = [800-2000, -0] = [-1200, 0].
        // desired 500 -> clamped to 0 (upper); desired -3000 -> -1200 (lower);
        // desired -600 -> -600 (in range).
        Assert.Equal(0f,
            PanMath.Clamp(500f, 0f, 800f, 1080f, 0f, 0f, 0f, 0f, 2000f, 300f, 0f).x, Tolerance);
        Assert.Equal(-1200f,
            PanMath.Clamp(-3000f, 0f, 800f, 1080f, 0f, 0f, 0f, 0f, 2000f, 300f, 0f).x, Tolerance);
        Assert.Equal(-600f,
            PanMath.Clamp(-600f, 0f, 800f, 1080f, 0f, 0f, 0f, 0f, 2000f, 300f, 0f).x, Tolerance);
    }

    [Fact]
    public void Clamp_BoardTallerThanAvail_ClampsYWithTopInset()
    {
        // vp 1000x600, top 100 bottom 0 => availY 500. Board box [0,0,200,2000].
        // boxH 2000 > 500 => clamp branch.
        // y range = [topInset + availY - maxY, topInset - minY]
        //         = [100 + 500 - 2000, 100 - 0] = [-1400, 100].
        Assert.Equal(100f,
            PanMath.Clamp(0f, 9999f, 1000f, 600f, 100f, 0f, 0f, 0f, 200f, 2000f, 0f).y, Tolerance);
        Assert.Equal(-1400f,
            PanMath.Clamp(0f, -9999f, 1000f, 600f, 100f, 0f, 0f, 0f, 200f, 2000f, 0f).y, Tolerance);
        Assert.Equal(-500f,
            PanMath.Clamp(0f, -500f, 1000f, 600f, 100f, 0f, 0f, 0f, 200f, 2000f, 0f).y, Tolerance);
    }

    // ---- Pad: always extra travel OUTWARD beyond the alignments ----------
    // The scroll pad widens the reachable range on both ends in BOTH
    // regimes: on an overflowing axis it lets edge hexes pull pad px clear
    // inside the viewport (the long-standing behavior), and on a fitting
    // axis it adds travel past the alignments instead of shrinking the
    // slack — so the range never collapses near the fit boundary (minimum
    // travel is 2·pad).

    [Fact]
    public void Clamp_Pad_ExtendsTravelPastTheAlignments()
    {
        // vp 1000 wide, board box [0,0,800,300] fits (slack 200). Raw
        // alignments [0, 200]; pad 50 widens outward to [-50, 250].
        Assert.Equal(-50f, PanMath.Clamp(
            -5000f, 0f, 1000f, 1080f, 0f, 0f, 0f, 0f, 800f, 300f, 50f).x, Tolerance);
        Assert.Equal(250f, PanMath.Clamp(
            5000f, 0f, 1000f, 1080f, 0f, 0f, 0f, 0f, 800f, 300f, 50f).x, Tolerance);
    }

    [Fact]
    public void Clamp_Pad_NeverCollapsesTheRangeNearTheFitBoundary()
    {
        // vp 1000 wide, board box [0,0,900,300] — nearly filling the axis
        // (slack 100), the max-zoom-out shape from #243 where the old
        // padded-box formula collapsed travel to |slack - 2·pad|. Raw
        // alignments [0, 100]; pad 100 widens outward to [-100, 200]:
        // travel = slack + 2·pad, not a sliver.
        Assert.Equal(-100f, PanMath.Clamp(
            -5000f, 0f, 1000f, 1080f, 0f, 0f, 0f, 0f, 900f, 300f, 100f).x, Tolerance);
        Assert.Equal(200f, PanMath.Clamp(
            5000f, 0f, 1000f, 1080f, 0f, 0f, 0f, 0f, 900f, 300f, 100f).x, Tolerance);
    }

    [Fact]
    public void Clamp_Pad_OverflowingAxisKeepsTheLongStandingRange()
    {
        // vp 800 wide, board box [0,0,2000,300] overflows. Raw alignments
        // [-1200, 0]; pad 75 widens outward to [-1275, 75] — identical to
        // the padded-box formula on this regime.
        Assert.Equal(-1275f, PanMath.Clamp(
            -5000f, 0f, 800f, 1080f, 0f, 0f, 0f, 0f, 2000f, 300f, 75f).x, Tolerance);
        Assert.Equal(75f, PanMath.Clamp(
            5000f, 0f, 800f, 1080f, 0f, 0f, 0f, 0f, 2000f, 300f, 75f).x, Tolerance);
    }

    // ---- Rotation: 90-degree board box (swapped extents, negative origin) -

    [Fact]
    public void Clamp_RotatedBox_NegativeOrigin_RangeUsesAlignments()
    {
        // A board rotated +90deg yields a box like [-300,0,0,500] (negative
        // minX). vp 1920x1080, no insets, no pad. x range [-minX = 300,
        // availX - maxX = 1920]; y range [0, 580]. Desired below the range
        // clamps to the left/top alignment; in-range passes through.
        (float x, float y) = PanMath.Clamp(
            0f, 0f, 1920f, 1080f, 0f, 0f,
            boxMinX: -300f, boxMinY: 0f, boxMaxX: 0f, boxMaxY: 500f,
            scaledPad: 0f);
        Assert.Equal(300f, x, Tolerance);
        Assert.Equal(0f, y, Tolerance);
        (float xMid, float yMax) = PanMath.Clamp(
            1110f, 9999f, 1920f, 1080f, 0f, 0f, -300f, 0f, 0f, 500f, 0f);
        Assert.Equal(1110f, xMid, Tolerance);
        Assert.Equal(580f, yMax, Tolerance);
    }

    [Fact]
    public void Clamp_RotatedBoxLargerThanViewport_ClampsWithNegativeExtents()
    {
        // Rotated tall box [-100,-50,1900,1100] in a 800x600 vp, no insets, no pad.
        // boxW = 1900-(-100) = 2000 > 800 => x range = [800-1900, 100] = [-1100, 100].
        // desired -5000 -> -1100; desired 5000 -> 100.
        Assert.Equal(-1100f,
            PanMath.Clamp(-5000f, 0f, 800f, 600f, 0f, 0f, -100f, -50f, 1900f, 1100f, 0f).x, Tolerance);
        Assert.Equal(100f,
            PanMath.Clamp(5000f, 0f, 800f, 600f, 0f, 0f, -100f, -50f, 1900f, 1100f, 0f).x, Tolerance);
    }
}
