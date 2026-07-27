// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;

/// <summary>
/// Pointy-top pixel ↔ axial projection — the board-geometry constants
/// (<c>√3</c> hex width, <c>1.5·s</c> row pitch, and the <c>2/3</c> inverse)
/// in one place. A hex of radius <c>s</c> is <c>√3·s</c> wide and <c>2·s</c>
/// tall; each row steps <c>1.5·s</c> down and half a width right.
///
/// Lives in FourExHex.ViewMath (the float-allowed Godot-free library) so the
/// math is unit-testable without dragging Node / SceneTree into the test
/// graph, and so <see cref="HexCoord"/> itself can stay integer-only in
/// FourExHex.Model. Three callers share it: <c>scripts/HexPixel</c> (the
/// thin <c>Vector2</c> shim the view draws through), <see cref="MapPlacement"/>
/// (content-aware centering and pan-clamping), and <c>HexMapView</c>'s
/// <c>PixelSize</c> / <c>FirstHexCenterOffset</c>. They must agree exactly —
/// a board framed by different constants than it is drawn with drifts
/// off-center — which is why the constants live here rather than in each.
/// </summary>
public static class HexProjection
{
    /// <summary>
    /// Pixel center for a hex of radius <paramref name="size"/>, measured from
    /// the axial origin (0,0). Callers add their own padding offset — see
    /// <see cref="FirstHexCenterOffset"/> for the board-anchoring one.
    /// </summary>
    public static (float x, float y) ToPixel(HexCoord coord, float size)
    {
        float x = size * MathF.Sqrt(3f) * (coord.Q + coord.R * 0.5f);
        float y = size * 1.5f * coord.R;
        return (x, y);
    }

    /// <summary>
    /// Raw inverse of <see cref="ToPixel"/>, stopping short of rounding: the
    /// fractional axial coordinate of <paramref name="x"/>,<paramref name="y"/>.
    /// Split out from <see cref="FromPixel"/> so the transform can be pinned
    /// independently of the cube-rounding rule applied on top of it.
    /// </summary>
    public static (float qFrac, float rFrac) FromPixelFrac(float x, float y, float size)
    {
        float qFrac = (x * MathF.Sqrt(3f) / 3f - y / 3f) / size;
        float rFrac = (y * 2f / 3f) / size;
        return (qFrac, rFrac);
    }

    /// <summary>
    /// Inverse of <see cref="ToPixel"/>: the hex whose footprint contains the
    /// pixel. Rounds via <see cref="HexRounding.Round"/>, which uses cube
    /// coordinates to pick correctly near an edge or corner where rounding q
    /// and r independently would not.
    /// </summary>
    public static HexCoord FromPixel(float x, float y, float size)
    {
        (float qFrac, float rFrac) = FromPixelFrac(x, y, size);
        return HexRounding.Round(qFrac, rFrac);
    }

    /// <summary>
    /// Where hex (0,0)'s center sits relative to the board's origin, so the
    /// grid's visual bounding box starts at local (0,0) rather than clipping
    /// the first hex in half. Exactly one hex half-extent.
    /// </summary>
    public static (float x, float y) FirstHexCenterOffset(float size) =>
        (0.5f * MathF.Sqrt(3f) * size, size);

    /// <summary>
    /// Half-width (<c>√3·s/2</c>) and half-height (<c>s</c>) of a single
    /// pointy-top hex — the distance from its center to its bounding box.
    /// </summary>
    public static (float halfW, float halfH) HexExtent(float size) =>
        (0.5f * MathF.Sqrt(3f) * size, size);

    /// <summary>
    /// Bounding-box size of a <paramref name="cols"/>×<paramref name="rows"/>
    /// offset grid. The extra half hex-width covers the odd-row shift; the
    /// extra <c>0.5·s</c> of height covers the bottom cap of the last row,
    /// whose rows otherwise pitch only <c>1.5·s</c> apart.
    /// </summary>
    public static (float width, float height) GridPixelSize(int cols, int rows, float size) =>
        ((cols + 0.5f) * MathF.Sqrt(3f) * size, (1.5f * rows + 0.5f) * size);
}
