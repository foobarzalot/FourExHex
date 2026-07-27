// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using Godot;

/// <summary>
/// <see cref="Vector2"/> shim over <see cref="HexProjection"/> for the view
/// layer. The projection arithmetic itself lives in FourExHex.ViewMath, where
/// it is Godot-free and unit-tested; this class exists only so HexMapView's
/// draw and hit-test call sites can stay in Godot's vector type.
/// </summary>
public static class HexPixel
{
    /// <summary>
    /// Pixel center for a hex of radius <paramref name="size"/>, measured
    /// from axial origin (0,0). Callers add their own padding offset.
    /// </summary>
    public static Vector2 ToPixel(HexCoord coord, float size)
    {
        (float x, float y) = HexProjection.ToPixel(coord, size);
        return new Vector2(x, y);
    }

    /// <summary>
    /// Inverse of <see cref="ToPixel"/>: find the hex whose footprint
    /// contains <paramref name="pixel"/>.
    /// </summary>
    public static HexCoord FromPixel(Vector2 pixel, float size) =>
        HexProjection.FromPixel(pixel.X, pixel.Y, size);
}
