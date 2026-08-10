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
}
