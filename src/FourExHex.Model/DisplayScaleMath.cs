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
    /// exactly so the unified mdpi/160 math floors to 1.0. The S9's natural
    /// factor exceeds this floor and is unaffected. Tuned to S9-landscape
    /// parity (≈1.67) rounded up — on iPhone 13 mini this maps the authored
    /// 96-logical-px HUD bar to ~9 mm, well past Apple HIG's 44 pt minimum.
    /// </summary>
    public const float MobileMinFactor = 1.8f;

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
}
