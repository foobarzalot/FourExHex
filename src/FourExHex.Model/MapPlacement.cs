using System;

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
    /// Axis-aligned bounding box (relative to the board node's origin) of the
    /// rectangle <c>[0,width]×[0,height]</c> after scaling by
    /// <paramref name="zoom"/> and rotating by <paramref name="angleRad"/>.
    /// Uses the same rotation convention as Godot's Node2D
    /// (<c>x' = x·cos − y·sin, y' = x·sin + y·cos</c>), so the view can apply
    /// the box directly. At angle 0 this is <c>(0, 0, width·zoom, height·zoom)</c>.
    /// </summary>
    public static (float minX, float minY, float maxX, float maxY) RotatedBoardBox(
        float width, float height, float zoom, float angleRad)
    {
        float w = width * zoom;
        float h = height * zoom;
        float cos = MathF.Cos(angleRad);
        float sin = MathF.Sin(angleRad);

        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        // The four corners of [0,w]×[0,h]; (0,0) maps to itself so it's folded
        // into the loop via the explicit list.
        (float x, float y)[] corners = { (0f, 0f), (w, 0f), (w, h), (0f, h) };
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
}
