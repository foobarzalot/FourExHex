// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
/// <summary>
/// Fit math for the HUD's two top corner zones. Each zone
/// (<c>HudBars.MakeCornerZone</c>) is a content-sized anchor point that grows
/// toward the middle: the left one rightward, the right one leftward. Neither
/// has a width budget, shrinks, or clips, and neither knows the other exists —
/// so on a narrow viewport they pass through each other and the right zone,
/// added last, paints over the left. Nothing in the Godot layout pass catches
/// that.
///
/// This is the arithmetic that does: given the viewport and each zone's
/// content width, how much clear space is left between them. The view feeds it
/// the zones' measured minimum sizes after every layout pass and logs the
/// result, so an overflow shows up in a player's log instead of only on a
/// device. Godot-free (plain floats) so it is unit-tested.
/// </summary>
public static class HudCornerLayout
{
    /// <summary>
    /// Clear horizontal space between the two corner zones, in logical px:
    /// the viewport less both edge pads and both zones' content widths.
    /// Zero means they exactly touch; <b>negative means they overlap</b>, by
    /// that many px.
    /// </summary>
    public static float CornerGap(
        float viewportWidth, float leftWidth, float rightWidth, float edgePadPx)
        => viewportWidth - edgePadPx * 2f - leftWidth - rightWidth;

    /// <summary>True when the two corner zones clear each other (touching
    /// counts as fitting) — see <see cref="CornerGap"/>.</summary>
    public static bool CornersFit(
        float viewportWidth, float leftWidth, float rightWidth, float edgePadPx)
        => CornerGap(viewportWidth, leftWidth, rightWidth, edgePadPx) >= 0f;

    /// <summary>
    /// Fraction of the safe-area inset the corner chrome backs away from the
    /// display edge. Deliberately partial: the corners are clipped by the
    /// rounded display corners and the notch's shoulder, not its full depth,
    /// so the full rails-style inset would waste screen. This is the single
    /// on-device tuning dial for how far the corner elements sit in.
    /// </summary>
    public const float CornerNudgeFactor = 0.5f;

    /// <summary>Horizontal edge offset for a corner-anchored block:
    /// the pad plus the nudge fraction of the larger side inset. Uses
    /// max(Left, Right) so both notch rotations place chrome identically.</summary>
    public static float SideOffset(LogicalSafeInsets safe, float edgePadPx)
        => edgePadPx + System.MathF.Max(safe.Left, safe.Right) * CornerNudgeFactor;

    /// <summary>Inset-aware <see cref="CornerGap"/>: both zones sit at
    /// <see cref="SideOffset"/> rather than the bare pad, so the clear space
    /// shrinks by twice the side nudge. Zero insets degrade to the legacy
    /// arithmetic exactly.</summary>
    public static float CornerGap(
        float viewportWidth, float leftWidth, float rightWidth,
        LogicalSafeInsets safe, float edgePadPx)
        => viewportWidth - SideOffset(safe, edgePadPx) * 2f - leftWidth - rightWidth;

    public static bool CornersFit(
        float viewportWidth, float leftWidth, float rightWidth,
        LogicalSafeInsets safe, float edgePadPx)
        => CornerGap(viewportWidth, leftWidth, rightWidth, safe, edgePadPx) >= 0f;
}
