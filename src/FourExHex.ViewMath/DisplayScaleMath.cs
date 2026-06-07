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
    /// mobile platform (<c>OS.HasFeature("mobile")</c>). Lifts touch targets
    /// up to a tappable size on phones whose natural DPI factor underperforms
    /// — notably iPhones, where Apple's logical-points system targets ~160 dpi
    /// exactly so the unified mdpi/160 math floors to 1.0. Tuned to
    /// **S9-portrait parity (≈2.22)** so the iPhone 13 mini (logical dpi
    /// ~158.67) and the S9 portrait (natural factor 2.22) land on the same
    /// factor — equalizing physical button size and producing near-identical
    /// logical viewports (iPhone 507×1097, S9 486×999). Side effect on
    /// mobile landscape: the S9's natural landscape factor (≈1.67) and the
    /// iPhone's (≈1.0) are both lifted to 2.22, so both devices use the
    /// width-collapsed (cycling) buy palette in landscape — a consistency win
    /// that mirrors the long-standing S9-landscape collapse onto iPhone too.
    /// </summary>
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
    /// Use this on <c>OS.HasFeature("mobile")</c> in place of
    /// <see cref="FactorForDpi(float, float)"/> — that one divides by
    /// <c>osScale</c>, which mis-counts iOS's retina pixel doubling and
    /// makes high-DPI iPhones render physically smaller than mid-DPI
    /// Androids at the same factor floor. Desktop continues to use
    /// <see cref="FactorForDpi(float, float)"/> because retina OS-scaling
    /// there genuinely pre-renders content at logical points.
    /// </summary>
    public static float FactorForRawMobileDpi(float rawDpi, float minFactor)
    {
        float floor = System.Math.Max(minFactor, MinFactor);
        if (rawDpi <= 0f) return floor;
        float raw = rawDpi / MobileReferenceDpi;
        return System.Math.Clamp(raw, floor, MaxFactor);
    }
}
