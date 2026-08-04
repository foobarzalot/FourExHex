// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;

/// <summary>
/// Keyboard navigation math for vertical pick-one lists (the save/map/tutorial
/// slot rows in the load modals). Godot-free so it is unit-testable, mirroring
/// <see cref="HudPanelMath"/>; the view supplies the measured row geometry and
/// applies the returned scroll offset.
/// </summary>
public static class ListNavMath
{
    /// <summary>
    /// Move a selection index by <paramref name="delta"/> rows. The ends are
    /// walls — stepping past the first or last row holds position rather than
    /// wrapping. <paramref name="index"/> of -1 means nothing is selected yet:
    /// stepping down enters at the first row, up at the last. An index left
    /// stale by a rebuilt (shorter) list is clamped into range before the step,
    /// so the first keypress lands on a neighbour of the nearest valid row.
    /// Returns -1 for an empty list.
    /// </summary>
    public static int Step(int index, int count, int delta)
    {
        if (count <= 0) return -1;
        if (index < 0) return delta >= 0 ? 0 : count - 1;
        int current = Math.Clamp(index, 0, count - 1);
        return Math.Clamp(current + delta, 0, count - 1);
    }

    /// <summary>
    /// Scroll offset that brings a row fully into view, scrolling the minimum
    /// distance: a row above the viewport top-aligns, one below bottom-aligns,
    /// and one already visible leaves the offset untouched. A row taller than
    /// the viewport top-aligns — bottom-aligning it would push its top edge off
    /// screen. Never returns a negative offset.
    /// </summary>
    public static float ScrollToReveal(
        float itemTop, float itemHeight, float scrollOffset, float viewportHeight)
    {
        if (itemTop < scrollOffset) return MathF.Max(0f, itemTop);
        float bottomAligned = itemTop + itemHeight - viewportHeight;
        if (bottomAligned > scrollOffset) return MathF.Max(0f, MathF.Min(bottomAligned, itemTop));
        return scrollOffset;
    }
}
