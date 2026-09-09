// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
/// <summary>
/// Layout math for the HUD's landscape bottom-left stack, bottom-up: the
/// seed / map-name label in the very corner (the same spot it takes in
/// portrait), the undo/redo corner strip lifted above it, and the left
/// rail's last button above that. The right-hand strip (End Turn + Automate)
/// and the right rail take the same lifts so the two sides stay level. The
/// label and the strips are corner-anchored blocks and the rails are
/// containers — nothing in the Godot layout pass relates them — so these
/// distances are the only thing keeping the blocks off each other. All
/// values are logical px measured up from the viewport's bottom edge.
/// Godot-free so it is unit-tested.
/// </summary>
public static class HudBottomStripLayout
{
    /// <summary>Gap between the strip top and the label bottom, and again
    /// between the label top and the rail's last button.</summary>
    public const float LabelGapPx = 10f;

    /// <summary>
    /// Baseline of the bottom corner strips: distance from the viewport's
    /// bottom edge to the strip's bottom. Deliberately the TOP inset plus the
    /// pad — the same distance the top corner chrome keeps from the top edge
    /// — so top and bottom chrome sit symmetrically and a centered rail
    /// cluster lands midway. The home-indicator inset (<c>Bottom</c>) is not
    /// used: the strip shares that band and its taps still route through.
    /// </summary>
    public static float CornerBottomOffset(LogicalSafeInsets safe, float edgePadPx)
        => safe.Top + edgePadPx;

    /// <summary>Distance from the viewport's bottom edge to the seed label's
    /// bottom: the bare pad, in both orientations.</summary>
    public static float LabelBottomOffset(float edgePadPx) => edgePadPx;

    /// <summary>Distance from the viewport's bottom edge to the bottom of the
    /// corner strips: the baseline lifted by the label and one gap.</summary>
    public static float StripBottomOffset(
        LogicalSafeInsets safe, float edgePadPx, float labelHeightPx, float gapPx)
        => CornerBottomOffset(safe, edgePadPx) + labelHeightPx + gapPx;

    /// <summary>
    /// How far an expanded (bottom-aligned) rail group backs its bottom edge
    /// off the baseline so its last button clears the label and the strip
    /// stacked above it: label + gap + button + gap.
    /// </summary>
    public static float RailClearance(float buttonSizePx, float gapPx, float labelHeightPx)
        => labelHeightPx + gapPx + buttonSizePx + gapPx;
}
