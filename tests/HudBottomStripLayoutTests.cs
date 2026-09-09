// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Pure math for the landscape bottom-left stack: expanded rail above the
/// undo/redo strip above the seed / map-name label, which sits in the very
/// corner exactly as it does in portrait. Nothing in the Godot
/// layout pass ties the three together — the rail is a container, the label
/// and strip are corner-anchored blocks — so the clearances are a layout
/// invariant only this arithmetic enforces. Pinned against the real button
/// size and the strip pad the HUD uses.
/// </summary>
public class HudBottomStripLayoutTests
{
    private const float Button = UiMetrics.TouchButtonSizePx;   // 68
    private const float Pad = UiMetrics.CornerZoneEdgePadPx;    // 10
    private const float Gap = HudBottomStripLayout.LabelGapPx;  // 10

    private static readonly LogicalSafeInsets NoInsets = new(0f, 0f, 0f, 0f);

    [Fact]
    public void CornerBottomOffset_IsPadPlusTopInset()
    {
        Assert.Equal(10f, HudBottomStripLayout.CornerBottomOffset(NoInsets, Pad));
        Assert.Equal(31f, HudBottomStripLayout.CornerBottomOffset(new LogicalSafeInsets(21f, 0f, 0f, 0f), Pad));
    }

    [Fact]
    public void LabelBottom_IsTheEdgePad_SameAsPortrait()
    {
        Assert.Equal(10f, HudBottomStripLayout.LabelBottomOffset(Pad));
    }

    [Theory]
    [InlineData(22f)]
    [InlineData(26f)]
    [InlineData(30f)]
    public void StripBottom_SitsOneGapAboveTheLabelTop(float labelHeight)
    {
        float labelTop = HudBottomStripLayout.LabelBottomOffset(Pad) + labelHeight;
        float stripBottom = HudBottomStripLayout.StripBottomOffset(NoInsets, Pad, labelHeight, Gap);
        Assert.Equal(Gap, stripBottom - labelTop);
    }

    [Theory]
    [InlineData(22f)]
    [InlineData(26f)]
    [InlineData(30f)]
    public void RailBottom_SitsOneGapAboveTheStripTop(float labelHeight)
    {
        float stripTop = HudBottomStripLayout.StripBottomOffset(NoInsets, Pad, labelHeight, Gap) + Button;
        float railBottom = HudBottomStripLayout.CornerBottomOffset(NoInsets, Pad)
            + HudBottomStripLayout.RailClearance(Button, Gap, labelHeight);
        Assert.Equal(Gap, railBottom - stripTop);
    }

    // --- Regression pin: a rail clearance that only covers the strip puts
    // the rail's last button on top of whatever sits above the strip (#249). ---

    [Fact]
    public void StripOnlyClearance_WouldOverlapTheStack()
    {
        const float stripOnly = Button + 20f;
        float stripTop = HudBottomStripLayout.StripBottomOffset(NoInsets, Pad, 22f, Gap) + Button;
        float oldRailBottom = HudBottomStripLayout.CornerBottomOffset(NoInsets, Pad) + stripOnly;
        Assert.True(oldRailBottom < stripTop);
        Assert.True(HudBottomStripLayout.RailClearance(Button, Gap, 1f) > stripOnly);
    }

    // --- Insets: the bottom strip mirrors the TOP chrome's edge distance so
    // top and bottom chrome sit symmetrically; the home-indicator inset
    // (Bottom) is deliberately not what drives it. ---

    [Fact]
    public void Strip_MirrorsTheTopInset_NotTheBottomOne()
    {
        var homeIndicator = new LogicalSafeInsets(0f, 21f, 0f, 0f);
        Assert.Equal(
            HudBottomStripLayout.StripBottomOffset(NoInsets, Pad, 22f, Gap),
            HudBottomStripLayout.StripBottomOffset(homeIndicator, Pad, 22f, Gap));

        var topInset = new LogicalSafeInsets(21f, 0f, 0f, 0f);
        Assert.Equal(
            HudBottomStripLayout.StripBottomOffset(NoInsets, Pad, 22f, Gap) + 21f,
            HudBottomStripLayout.StripBottomOffset(topInset, Pad, 22f, Gap));
    }
}
