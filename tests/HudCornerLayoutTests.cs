// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Pure fit math for the HUD's two oppositely-anchored corner zones. Each zone
/// is content-sized and grows toward the other with no width budget, so their
/// combined width against the viewport is a layout invariant nothing else
/// enforces. These tests pin that invariant for the real chip/button widths at
/// the narrowest supported portrait viewports.
/// </summary>
public class HudCornerLayoutTests
{
    // --- The real widths the HUD builds, composed from the shared tokens ---

    /// <summary>Status chip: compact swatch + gutter + turn label, inside the
    /// chip's left/right padding.</summary>
    private const float StatusChipW =
        UiMetrics.CurrentSwatchSizePx + UiMetrics.GutterPx
        + UiMetrics.TurnLabelMinWidthPx + UiMetrics.ChipPaddingPx * 2f;

    /// <summary>Gold chip: the mono readout's floor inside the same padding.
    /// The wider of the two top-left chips.</summary>
    private const float GoldChipW =
        UiMetrics.GoldLabelMinWidthPx + UiMetrics.ChipPaddingPx * 2f;

    /// <summary>Top-right chrome, help + options only.</summary>
    private const float ChromeW =
        UiMetrics.TouchButtonSizePx * 2f + UiMetrics.CornerZoneSeparationPx;

    /// <summary>Top-right chrome with the undo/redo pair also in the corner —
    /// the composition that overlaps on a phone.</summary>
    private const float ChromeWithUndoW =
        ChromeW + UiMetrics.CornerZoneSeparationPx
        + UiMetrics.TouchButtonSizePx * 2f + UiMetrics.GutterPx;

    private const float Pad = UiMetrics.CornerZoneEdgePadPx;

    // iPhone 13 mini portrait / Galaxy S9 portrait / iPhone 13 mini landscape,
    // in logical px (RELEASE.md section 5).
    private const float MiniPortraitW = 425f;
    private const float S9PortraitW = 486f;
    private const float MiniLandscapeW = 921f;

    // --- Arithmetic + sign convention ---

    [Fact]
    public void CornerGap_IsViewportLessBothZonesAndBothPads()
    {
        // 1000 − 2*10 − 300 − 200 = 480.
        Assert.Equal(480f, HudCornerLayout.CornerGap(
            viewportWidth: 1000f, leftWidth: 300f, rightWidth: 200f, edgePadPx: 10f));
    }

    [Fact]
    public void CornerGap_IsNegativeWhenZonesOverlap()
    {
        // 400 − 2*10 − 300 − 200 = −120: the zones pass through each other.
        Assert.Equal(-120f, HudCornerLayout.CornerGap(
            viewportWidth: 400f, leftWidth: 300f, rightWidth: 200f, edgePadPx: 10f));
    }

    [Fact]
    public void CornersFit_TracksTheSignOfTheGap()
    {
        Assert.True(HudCornerLayout.CornersFit(1000f, 300f, 200f, 10f));
        Assert.False(HudCornerLayout.CornersFit(400f, 300f, 200f, 10f));
        // Exactly touching still fits: 520 − 20 − 300 − 200 = 0.
        Assert.True(HudCornerLayout.CornersFit(520f, 300f, 200f, 10f));
    }

    // --- Regression pin: undo/redo in the corner overlaps on a phone ---

    [Fact]
    public void UndoInCorner_OverlapsTheStatusChip_OnAPhonePortrait()
    {
        Assert.False(HudCornerLayout.CornersFit(
            MiniPortraitW, StatusChipW, ChromeWithUndoW, Pad));
    }

    [Fact]
    public void UndoInCorner_OverlapsTheGoldChip_OnEveryPhonePortrait()
    {
        Assert.False(HudCornerLayout.CornersFit(
            MiniPortraitW, GoldChipW, ChromeWithUndoW, Pad));
        Assert.False(HudCornerLayout.CornersFit(
            S9PortraitW, GoldChipW, ChromeWithUndoW, Pad));
    }

    // --- Fit pin: help + options alone clear both chips everywhere ---

    [Theory]
    [InlineData(MiniPortraitW)]
    [InlineData(S9PortraitW)]
    [InlineData(MiniLandscapeW)]
    public void HelpAndOptionsClearTheStatusChip(float viewportWidth)
    {
        Assert.True(HudCornerLayout.CornersFit(
            viewportWidth, StatusChipW, ChromeW, Pad));
    }

    [Theory]
    [InlineData(MiniPortraitW)]
    [InlineData(S9PortraitW)]
    [InlineData(MiniLandscapeW)]
    public void HelpAndOptionsClearTheGoldChip(float viewportWidth)
    {
        Assert.True(HudCornerLayout.CornersFit(
            viewportWidth, GoldChipW, ChromeW, Pad));
    }

    [Fact]
    public void GoldChipIsTheBindingConstraintOnTheNarrowestPhone()
    {
        // The gold chip is wider than the status chip, so it sets the margin:
        // 425 − 20 − 244 − 146 = 15 px of slack, and the status chip has more.
        float goldGap = HudCornerLayout.CornerGap(
            MiniPortraitW, GoldChipW, ChromeW, Pad);
        float statusGap = HudCornerLayout.CornerGap(
            MiniPortraitW, StatusChipW, ChromeW, Pad);
        Assert.Equal(15f, goldGap);
        Assert.True(statusGap > goldGap);
    }

    // --- Inset-aware nudge: corner chrome backs out a FRACTION of the
    // safe-area inset (not the rails' full-inset convention) ---

    /// <summary>iPhone 13 mini landscape logical insets: no top, home
    /// indicator bottom, notch on one side (rotation flips which).</summary>
    private static readonly LogicalSafeInsets MiniLandscape =
        new(Top: 0f, Bottom: 21f, Left: 47f, Right: 0f);

    [Fact]
    public void SideOffset_IsTheBarePad_AtZeroInsets()
    {
        Assert.Equal(Pad, HudCornerLayout.SideOffset(LogicalSafeInsets.Zero, Pad));
    }

    [Fact]
    public void SideOffset_AddsTheNudgeFractionOfTheLargerSideInset()
    {
        Assert.Equal(
            Pad + 47f * HudCornerLayout.CornerNudgeFactor,
            HudCornerLayout.SideOffset(MiniLandscape, Pad));
    }

    [Fact]
    public void SideOffset_IsRotationSymmetric()
    {
        var notchLeft = new LogicalSafeInsets(0f, 21f, 47f, 0f);
        var notchRight = new LogicalSafeInsets(0f, 21f, 0f, 47f);
        Assert.Equal(
            HudCornerLayout.SideOffset(notchLeft, Pad),
            HudCornerLayout.SideOffset(notchRight, Pad));
    }

    [Fact]
    public void InsetAwareCornerGap_ShrinksByTwiceTheSideNudge()
    {
        float legacy = HudCornerLayout.CornerGap(MiniLandscapeW, 300f, 200f, Pad);
        float inset = HudCornerLayout.CornerGap(
            MiniLandscapeW, 300f, 200f, MiniLandscape, Pad);
        Assert.Equal(
            legacy - 2f * 47f * HudCornerLayout.CornerNudgeFactor, inset);
        // Zero insets degrade to the legacy arithmetic exactly.
        Assert.Equal(legacy, HudCornerLayout.CornerGap(
            MiniLandscapeW, 300f, 200f, LogicalSafeInsets.Zero, Pad));
    }

    [Fact]
    public void LandscapeTopCorners_ClearTheNotch_WithBothChipsInline()
    {
        // Landscape sets the two chips side by side in the top-left zone and
        // help + options in the top-right (undo/redo ride the bottom-left
        // strip, End Turn + Automate the bottom-right). Even backed out by
        // the notch nudge, 921 logical px clears with room to spare.
        float inlineChipsW = StatusChipW + UiMetrics.CornerZoneSeparationPx + GoldChipW;
        Assert.True(HudCornerLayout.CornersFit(
            MiniLandscapeW, inlineChipsW, ChromeW, MiniLandscape, Pad));
    }
}
