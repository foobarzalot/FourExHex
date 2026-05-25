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

    /// <summary>Scale factor for a screen density. <paramref name="dpi"/> &lt;= 0
    /// (headless or unknown screen) yields <see cref="MinFactor"/>.</summary>
    public static float FactorForDpi(float dpi)
    {
        if (dpi <= 0f) return MinFactor;
        float raw = dpi / ReferenceDpi;
        return System.Math.Clamp(raw, MinFactor, MaxFactor);
    }
}
