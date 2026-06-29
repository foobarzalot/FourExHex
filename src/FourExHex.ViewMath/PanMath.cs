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
    /// axis is locked to its centered value when the board (widened by
    /// <paramref name="scaledPad"/>) is smaller than the available area on that
    /// axis; otherwise the desired value is clamped into the reachable range.
    /// The board box <c>(boxMin/boxMax)</c> is the on-screen AABB of the scaled
    /// + rotated grid relative to the board node's origin
    /// (<see cref="MapPlacement.RotatedBoardBox"/>); <paramref name="scaledPad"/>
    /// is the symmetric scroll pad already scaled by zoom. Mirrors
    /// <c>HexMapView.ClampPan</c> byte-for-byte.
    /// </summary>
    public static (float x, float y) Clamp(
        float desiredX, float desiredY,
        float vpWidth, float vpHeight, float topInset, float bottomInset,
        float boxMinX, float boxMinY, float boxMaxX, float boxMaxY,
        float scaledPad)
    {
        float availX = vpWidth;
        float availY = vpHeight - topInset - bottomInset;

        // Widen the rotated AABB by the symmetric pad (still symmetric after
        // rotation, so applied directly in viewport space).
        float minX = boxMinX - scaledPad, minY = boxMinY - scaledPad;
        float maxX = boxMaxX + scaledPad, maxY = boxMaxY + scaledPad;

        float boxW = maxX - minX;
        float boxH = maxY - minY;

        float x = boxW <= availX
            ? (availX - boxW) * 0.5f - minX
            : ClampValue(desiredX, availX - maxX, -minX);
        float y = boxH <= availY
            ? topInset + (availY - boxH) * 0.5f - minY
            : ClampValue(desiredY, topInset + availY - maxY, topInset - minY);
        return (x, y);
    }

    // Replicates Godot's Mathf.Clamp(float) semantics — a plain three-way
    // select that never throws even if min > max (System.Math.Clamp throws),
    // keeping the clamp safe for any rotation/pad inputs.
    private static float ClampValue(float value, float min, float max) =>
        value < min ? min : (value > max ? max : value);
}
