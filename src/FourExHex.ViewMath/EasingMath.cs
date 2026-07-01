using System;

/// <summary>
/// Godot-free easing / interpolation primitives for view-side animation
/// (e.g. the camera pan in <c>HexMapView.CenterOnTerritory</c>). Plain
/// <see cref="float"/> math so it stays unit-testable without Godot; the
/// view layer feeds elapsed/duration in and drives Node positions out.
/// </summary>
public static class EasingMath
{
    /// <summary>
    /// Classic smoothstep ease-in/ease-out: <c>3t² − 2t³</c>. Input is
    /// clamped to <c>[0, 1]</c> first, so a caller that overshoots the range
    /// (the final animation frame where elapsed ≥ duration) resolves exactly
    /// to <c>1</c> — and negative inputs to <c>0</c>. The curve starts and
    /// ends with zero slope, giving the slow-start / slow-finish feel.
    /// </summary>
    public static float SmoothStep(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>
    /// Linear interpolation from <paramref name="a"/> to <paramref name="b"/>
    /// by <paramref name="t"/> (<c>a + (b − a) · t</c>). No clamping — pass an
    /// eased <paramref name="t"/> from <see cref="SmoothStep"/> for eased motion.
    /// </summary>
    public static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
