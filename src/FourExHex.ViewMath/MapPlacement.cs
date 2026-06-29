using System;
using System.Collections.Generic;

/// <summary>
/// Pure geometry for placing the (optionally rotated) hex board inside the
/// viewport. Godot-free (plain floats) so it lives in the engine-free model
/// and is unit-testable, mirroring <see cref="ZoomMath"/>. The view
/// (HexMapView) feeds in its board pixel size, zoom, and rotation angle and
/// uses the returned on-screen bounding box to center and clamp the pan.
/// </summary>
public static class MapPlacement
{
    /// <summary>
    /// Unscaled board-pixel bounding box of <paramref name="coords"/>, in the
    /// same local space as <c>HexMapView.PixelSize</c> (origin at 0,0, with the
    /// first-hex offset folded in). Mirrors the view's
    /// <c>FirstHexCenterOffset + HexPixel.ToPixel</c> for pointy-top hexes plus
    /// the hex extent (half-width √3·s/2, half-height s). Used for content-aware
    /// centering: pass the playable (non-water) tile coords to frame the content
    /// rather than the padded grid. Empty input returns a zero box.
    /// </summary>
    public static (float minX, float minY, float maxX, float maxY) ContentPixelBounds(
        IEnumerable<HexCoord> coords, float hexSize)
    {
        float sqrt3 = MathF.Sqrt(3f);
        float offX = 0.5f * sqrt3 * hexSize;  // FirstHexCenterOffset.X
        float offY = hexSize;                 // FirstHexCenterOffset.Y
        float halfW = 0.5f * sqrt3 * hexSize; // pointy-top hex half-width
        float halfH = hexSize;                // pointy-top hex half-height

        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        bool any = false;
        foreach (HexCoord c in coords)
        {
            any = true;
            float cx = offX + hexSize * sqrt3 * (c.Q + c.R * 0.5f);
            float cy = offY + hexSize * 1.5f * c.R;
            minX = MathF.Min(minX, cx - halfW);
            maxX = MathF.Max(maxX, cx + halfW);
            minY = MathF.Min(minY, cy - halfH);
            maxY = MathF.Max(maxY, cy + halfH);
        }
        return any ? (minX, minY, maxX, maxY) : (0f, 0f, 0f, 0f);
    }

    /// <summary>
    /// Axis-aligned bounding box (relative to the board node's origin) of the
    /// rectangle <c>[0,width]×[0,height]</c> after scaling by
    /// <paramref name="zoom"/> and rotating by <paramref name="angleRad"/>.
    /// Uses the same rotation convention as Godot's Node2D
    /// (<c>x' = x·cos − y·sin, y' = x·sin + y·cos</c>), so the view can apply
    /// the box directly. At angle 0 this is <c>(0, 0, width·zoom, height·zoom)</c>.
    /// </summary>
    /// <summary>
    /// Like <see cref="RotatedBoardBox"/> but for an arbitrary rectangle
    /// <c>[left,right]×[top,bottom]</c> that need not start at the origin —
    /// used for content-aware clamping, where the playable tiles sit at an
    /// offset within the grid. Returns the AABB after scaling by
    /// <paramref name="zoom"/> and rotating by <paramref name="angleRad"/>.
    /// </summary>
    public static (float minX, float minY, float maxX, float maxY) RotatedRectBox(
        float left, float top, float right, float bottom, float zoom, float angleRad)
    {
        float cos = MathF.Cos(angleRad);
        float sin = MathF.Sin(angleRad);
        float l = left * zoom, t = top * zoom, r = right * zoom, b = bottom * zoom;

        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        (float x, float y)[] corners = { (l, t), (r, t), (r, b), (l, b) };
        foreach ((float x, float y) in corners)
        {
            float rx = x * cos - y * sin;
            float ry = x * sin + y * cos;
            minX = MathF.Min(minX, rx);
            maxX = MathF.Max(maxX, rx);
            minY = MathF.Min(minY, ry);
            maxY = MathF.Max(maxY, ry);
        }
        return (minX, minY, maxX, maxY);
    }

    public static (float minX, float minY, float maxX, float maxY) RotatedBoardBox(
        float width, float height, float zoom, float angleRad) =>
        RotatedRectBox(0f, 0f, width, height, zoom, angleRad);

    /// <summary>Midpoint of the box <c>[minX,maxX]×[minY,maxY]</c> — the center
    /// the view frames on (content box for centering, grid box for the
    /// thumbnail).</summary>
    public static (float x, float y) BoxCenter(float minX, float minY, float maxX, float maxY) =>
        ((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);

    /// <summary>Map an unscaled local board offset to a world-space offset by
    /// applying <paramref name="zoom"/> then rotating by
    /// <paramref name="angleRad"/>. Uses Godot's <c>Vector2.Rotated</c>
    /// convention (<c>x' = x·cos − y·sin, y' = x·sin + y·cos</c>) so the view can
    /// subtract the result from a viewport-space center directly.</summary>
    public static (float x, float y) ToWorldOffset(
        float offsetX, float offsetY, float zoom, float angleRad)
    {
        float sx = offsetX * zoom, sy = offsetY * zoom;
        float cos = MathF.Cos(angleRad), sin = MathF.Sin(angleRad);
        return (sx * cos - sy * sin, sx * sin + sy * cos);
    }
}
