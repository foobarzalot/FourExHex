// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
/// <summary>
/// Pure DPI → UI-scale-factor mapping for the window-level
/// <c>ContentScaleFactor</c> applied by the Godot-side <c>DisplayScale</c>
/// autoload. Kept Godot-free and in the model assembly so the clamp/baseline
/// behavior is unit-testable. See <c>scripts/DisplayScale.cs</c> for the caller.
/// </summary>
public static class DisplayScaleMath
{
    /// <summary>Density (dpi) that maps to a 1.0 factor — Android's mdpi
    /// baseline. Densities below this floor to <see cref="MinFactor"/>, so
    /// typical desktop displays render at design size.</summary>
    public const float ReferenceDpi = 160f;

    /// <summary>Never render below the authored design size.</summary>
    public const float MinFactor = 1.0f;

    /// <summary>Cap so an extreme/misreported density can't produce an absurd
    /// blow-up.</summary>
    public const float MaxFactor = 3.0f;

    /// <summary>Minimum factor the Godot adapter passes when running on a
    /// mobile platform (<c>OS.HasFeature("mobile")</c>): a touch-target floor
    /// tuned to ≈2.22 so iPhone and S9 land on the same factor.</summary>
    public const float MobileMinFactor = 2.2222f;

    /// <summary>Scale factor for a screen density, with a default floor of
    /// <see cref="MinFactor"/>. <paramref name="dpi"/> &lt;= 0 (headless or
    /// unknown screen) yields the floor.</summary>
    public static float FactorForDpi(float dpi) => FactorForDpi(dpi, MinFactor);

    /// <summary>Scale factor for a screen density with a caller-supplied
    /// floor. Used by the Godot adapter to pass <see cref="MobileMinFactor"/>
    /// on mobile. <paramref name="minFactor"/> below <see cref="MinFactor"/>
    /// is clamped up so callers can never shrink design size. <paramref
    /// name="dpi"/> &lt;= 0 yields the effective floor.</summary>
    public static float FactorForDpi(float dpi, float minFactor)
    {
        float floor = System.Math.Max(minFactor, MinFactor);
        if (dpi <= 0f) return floor;
        float raw = dpi / ReferenceDpi;
        return System.Math.Clamp(raw, floor, MaxFactor);
    }

    /// <summary>Mobile reference DPI for the raw-DPI factor formula.
    /// Reverse-engineered from S9 FHD+ portrait at the 2.22 factor we
    /// already ship: 401 raw DPI / 2.22 ≈ 180. Equalizes physical
    /// button size across iPhone (raw DPI 476) and S9 (raw DPI 401)
    /// without resorting to a per-device floor.</summary>
    public const float MobileReferenceDpi = 180f;

    /// <summary>
    /// Mobile-only factor formula: <c>rawDpi / MobileReferenceDpi</c>,
    /// clamped to [<paramref name="minFactor"/>, <see cref="MaxFactor"/>].
    /// Used on <c>OS.HasFeature("mobile")</c>; desktop keeps
    /// <see cref="FactorForDpi(float, float)"/>, where retina OS-scaling
    /// genuinely pre-renders content at logical points.
    /// </summary>
    public static float FactorForRawMobileDpi(float rawDpi, float minFactor)
    {
        float floor = System.Math.Max(minFactor, MinFactor);
        if (rawDpi <= 0f) return floor;
        float raw = rawDpi / MobileReferenceDpi;
        return System.Math.Clamp(raw, floor, MaxFactor);
    }
}
