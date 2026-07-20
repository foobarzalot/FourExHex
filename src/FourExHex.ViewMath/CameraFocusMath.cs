// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
/// <summary>
/// Comfort-zone test for the demo-replay camera follow: given a point in
/// viewport space, decide whether it has drifted outside the centered
/// "comfortable" region of the visible play area (viewport minus the HUD's
/// top/bottom insets) and the camera should ease over to it. Godot-free
/// (plain floats), mirroring <see cref="PanMath"/>; the view supplies the
/// point via its own coord→viewport transform.
/// </summary>
public static class CameraFocusMath
{
    /// <summary>
    /// True if (x, y) lies outside the centered box occupying
    /// <paramref name="comfortFrac"/> (0..1) of the inset-adjusted play
    /// area — i.e. the camera should pan. Points beyond the viewport
    /// entirely are always outside.
    /// </summary>
    public static bool IsOutsideComfortZone(
        float viewportW, float viewportH,
        float topInset, float bottomInset,
        float x, float y,
        float comfortFrac)
    {
        float usableH = viewportH - topInset - bottomInset;
        float centerX = viewportW / 2f;
        float centerY = topInset + usableH / 2f;
        float halfW = viewportW * comfortFrac / 2f;
        float halfH = usableH * comfortFrac / 2f;
        return System.Math.Abs(x - centerX) > halfW
            || System.Math.Abs(y - centerY) > halfH;
    }
}
