// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using Xunit;

namespace FourExHex.Tests;

public class ListNavMathTests
{
    private const float Tolerance = 0.0001f;

    // --- Step: moving the selection ---

    [Fact]
    public void Step_Down_AdvancesOne()
    {
        Assert.Equal(3, ListNavMath.Step(2, 5, +1));
    }

    [Fact]
    public void Step_Up_RetreatsOne()
    {
        Assert.Equal(1, ListNavMath.Step(2, 5, -1));
    }

    [Fact]
    public void Step_DownAtLast_StaysOnLast()
    {
        // No wrap-around: the ends are walls, not portals.
        Assert.Equal(4, ListNavMath.Step(4, 5, +1));
    }

    [Fact]
    public void Step_UpAtFirst_StaysOnFirst()
    {
        Assert.Equal(0, ListNavMath.Step(0, 5, -1));
    }

    [Fact]
    public void Step_DownFromNoSelection_SelectsFirst()
    {
        Assert.Equal(0, ListNavMath.Step(-1, 5, +1));
    }

    [Fact]
    public void Step_UpFromNoSelection_SelectsLast()
    {
        // Entering the list from the bottom edge — the conventional entry
        // point, and not wrap-around: it only applies with nothing selected.
        Assert.Equal(4, ListNavMath.Step(-1, 5, -1));
    }

    [Fact]
    public void Step_EmptyList_StaysUnselected()
    {
        Assert.Equal(-1, ListNavMath.Step(-1, 0, +1));
        Assert.Equal(-1, ListNavMath.Step(-1, 0, -1));
    }

    [Fact]
    public void Step_IndexPastEnd_ClampsIntoRange()
    {
        // A rebuilt (shorter) list can leave a stale index behind.
        Assert.Equal(2, ListNavMath.Step(9, 3, +1));
        Assert.Equal(1, ListNavMath.Step(9, 3, -1));
    }

    // --- ScrollToReveal: keeping the selection on screen ---

    [Fact]
    public void ScrollToReveal_ItemFullyVisible_LeavesOffsetAlone()
    {
        Assert.Equal(40f, ListNavMath.ScrollToReveal(60f, 30f, 40f, 200f), Tolerance);
    }

    [Fact]
    public void ScrollToReveal_ItemAboveViewport_ScrollsItemToTop()
    {
        Assert.Equal(30f, ListNavMath.ScrollToReveal(30f, 30f, 100f, 200f), Tolerance);
    }

    [Fact]
    public void ScrollToReveal_ItemBelowViewport_ScrollsItemToBottom()
    {
        // Item spans 380..410, viewport shows 100..300 → bottom-align to 210.
        Assert.Equal(210f, ListNavMath.ScrollToReveal(380f, 30f, 100f, 200f), Tolerance);
    }

    [Fact]
    public void ScrollToReveal_ItemPartlyClippedAtBottom_ScrollsJustEnough()
    {
        // Item spans 290..320, viewport 100..300 → 20px short.
        Assert.Equal(120f, ListNavMath.ScrollToReveal(290f, 30f, 100f, 200f), Tolerance);
    }

    [Fact]
    public void ScrollToReveal_ItemTallerThanViewport_TopAligns()
    {
        // Bottom-aligning a too-tall item would hide its top; prefer the top.
        Assert.Equal(50f, ListNavMath.ScrollToReveal(50f, 400f, 0f, 200f), Tolerance);
    }

    [Fact]
    public void ScrollToReveal_FirstItem_NeverScrollsNegative()
    {
        Assert.Equal(0f, ListNavMath.ScrollToReveal(0f, 30f, 0f, 200f), Tolerance);
    }
}
