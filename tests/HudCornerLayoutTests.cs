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
    public void LandscapeKeepsUndoInTheCorner_WithBothChipsInline()
    {
        // Landscape sets the two chips side by side in the top-left zone and
        // keeps the full chrome (undo/redo + help + options) in the top-right.
        // 921 is wide enough for all of it, which is why landscape is left
        // alone: 921 − 20 − 394 − 300 = 207 px clear.
        float inlineChipsW = StatusChipW + UiMetrics.CornerZoneSeparationPx + GoldChipW;
        Assert.True(HudCornerLayout.CornersFit(
            MiniLandscapeW, inlineChipsW, ChromeWithUndoW, Pad));
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
}
