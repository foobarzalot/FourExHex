// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
/// <summary>
/// Pure camera-framing math for the hex board: the visible play-area center
/// (inset-aware) and the pan clamp that keeps the board on-screen. Godot-free
/// (plain floats, tuple returns) so it's unit-testable, mirroring
/// <see cref="MapPlacement"/> and <see cref="ZoomMath"/>. The view (HexMapView)
/// gathers viewport size, HUD insets, and the rotated board box (from
/// <see cref="MapPlacement.RotatedBoardBox"/>) and feeds them in; the returned
/// tuples become Godot Vector2s on the view side.
/// </summary>
public static class PanMath
{
    /// <summary>
    /// Visible center of the play area in viewport space, accounting for the
    /// HUD's reserved insets at the top and bottom:
    /// <c>(vpWidth/2, topInset + (vpHeight - topInset - bottomInset)/2)</c>.
    /// </summary>
    public static (float x, float y) VisualCenter(
        float vpWidth, float vpHeight, float topInset, float bottomInset)
    {
        float availY = vpHeight - topInset - bottomInset;
        return (vpWidth * 0.5f, topInset + availY * 0.5f);
    }

    /// <summary>
    /// Clamp a proposed board position so it can't be panned off-screen. Each
    /// axis clamps into the range spanned by the board's two viewport
    /// alignments — left-edge-at-edge (<c>-minX</c>) and right-edge-at-edge
    /// (<c>availX - maxX</c>) — widened OUTWARD by <paramref name="scaledPad"/>
    /// on both ends. The same rule bounds both regimes: a board overflowing
    /// the available area pans far enough to pull every edge hex pad-clear
    /// inside the viewport, and a board that fits pans across its slack plus
    /// the pad instead of pinning — so a max-zoom-out mobile aspect can pan
    /// on the perpendicular axis too. Travel is <c>|slack| + 2·pad</c> on
    /// every axis, so the range never collapses near the fit boundary. The
    /// board box <c>(boxMin/boxMax)</c> is the on-screen AABB of the scaled +
    /// rotated grid relative to the board node's origin
    /// (<see cref="MapPlacement.RotatedBoardBox"/>); <paramref name="scaledPad"/>
    /// is the symmetric scroll pad already scaled by zoom. Backs
    /// <c>HexMapView.ClampPan</c>.
    /// </summary>
    public static (float x, float y) Clamp(
        float desiredX, float desiredY,
        float vpWidth, float vpHeight, float topInset, float bottomInset,
        float boxMinX, float boxMinY, float boxMaxX, float boxMaxY,
        float scaledPad)
    {
        float availX = vpWidth;
        float availY = vpHeight - topInset - bottomInset;

        float xA = -boxMinX, xB = availX - boxMaxX;
        float yA = topInset - boxMinY, yB = topInset + availY - boxMaxY;

        float x = ClampValue(desiredX,
            System.MathF.Min(xA, xB) - scaledPad, System.MathF.Max(xA, xB) + scaledPad);
        float y = ClampValue(desiredY,
            System.MathF.Min(yA, yB) - scaledPad, System.MathF.Max(yA, yB) + scaledPad);
        return (x, y);
    }

    // Replicates Godot's Mathf.Clamp(float) semantics — a plain three-way
    // select that never throws even if min > max (System.Math.Clamp throws),
    // keeping the clamp safe for any rotation/pad inputs.
    private static float ClampValue(float value, float min, float max) =>
        value < min ? min : (value > max ? max : value);
}
