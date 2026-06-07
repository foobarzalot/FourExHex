using Godot;

/// <summary>
/// View-side pixel ↔ axial projection for pointy-top hexes. Lives in the
/// Godot layer (uses <see cref="Vector2"/>) so <see cref="HexCoord"/> can
/// stay engine-free. The cube-rounding correctness still lives in the
/// model via <see cref="HexRounding.Round"/>, which this calls.
/// </summary>
public static class HexPixel
{
    /// <summary>
    /// Pixel center for a hex of radius <paramref name="size"/>, measured
    /// from axial origin (0,0). Callers add their own padding offset.
    /// </summary>
    public static Vector2 ToPixel(HexCoord coord, float size)
    {
        float x = size * Mathf.Sqrt(3f) * (coord.Q + coord.R * 0.5f);
        float y = size * 1.5f * coord.R;
        return new Vector2(x, y);
    }

    /// <summary>
    /// Inverse of <see cref="ToPixel"/>: find the hex whose footprint
    /// contains <paramref name="pixel"/>. Uses cube-coordinate rounding
    /// (<see cref="HexRounding.Round"/>) to pick the correct hex near an edge.
    /// </summary>
    public static HexCoord FromPixel(Vector2 pixel, float size)
    {
        float qFrac = (pixel.X * Mathf.Sqrt(3f) / 3f - pixel.Y / 3f) / size;
        float rFrac = (pixel.Y * 2f / 3f) / size;
        return HexRounding.Round(qFrac, rFrac);
    }
}
