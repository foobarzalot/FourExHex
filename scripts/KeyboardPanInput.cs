using System;
using Godot;

/// <summary>
/// Pure helper that turns held movement keys (WASD / arrows) into a
/// pan-direction vector. Extracted from <see cref="HexMapView._Process"/>
/// so the suppression rule (don't pan while a popup like the save dialog
/// is open) is unit-testable; the view layer is excluded from the test
/// build. The caller decides when to suppress.
/// </summary>
public static class KeyboardPanInput
{
    /// <summary>
    /// Returns the un-normalized pan direction implied by held keys.
    /// Right/Down map to +X/+Y respectively. The caller normalizes and
    /// scales by per-second speed.
    ///
    /// Returns <see cref="Vector2.Zero"/> whenever
    /// <paramref name="suppressPan"/> is true, so typing a letter
    /// in a save-name dialog does not also pan the map.
    /// </summary>
    public static Vector2 ComputeDirection(Func<Key, bool> isPressed, bool suppressPan)
    {
        if (suppressPan) return Vector2.Zero;
        Vector2 dir = Vector2.Zero;
        if (isPressed(Key.W) || isPressed(Key.Up))    dir.Y -= 1f;
        if (isPressed(Key.S) || isPressed(Key.Down))  dir.Y += 1f;
        if (isPressed(Key.A) || isPressed(Key.Left))  dir.X -= 1f;
        if (isPressed(Key.D) || isPressed(Key.Right)) dir.X += 1f;
        return dir;
    }
}
