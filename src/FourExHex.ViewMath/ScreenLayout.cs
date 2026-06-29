/// <summary>
/// Pure orientation + HUD-inset math for the responsive (landscape/portrait)
/// layout. Godot-free (plain floats / enums) so it lives in the engine-free
/// model and is unit-testable, mirroring <see cref="ZoomMath"/>. The view
/// layer owns the actual node arrangement and passes its own bar heights in —
/// no view magic numbers live here.
/// </summary>
public enum ScreenOrientation
{
    Landscape,
    Portrait,
}

/// <summary>Pixels the map must reserve at the top and bottom of the viewport
/// for the HUD bars. Consumed by HexMapView's centering / pan-clamp.</summary>
public readonly record struct MapInsets(float Top, float Bottom);

public static class ScreenLayout
{
    /// <summary>Portrait when taller than wide; landscape otherwise (a square
    /// tie resolves to landscape).</summary>
    public static ScreenOrientation Resolve(float width, float height) =>
        width >= height ? ScreenOrientation.Landscape : ScreenOrientation.Portrait;

    /// <summary>
    /// Phone↔tablet breakpoint for the responsive HUD, in logical px on the
    /// shorter viewport edge. Picked to put every phone we test on the
    /// compact side and every tablet on the expanded side:
    ///
    ///   iPhone 13 mini (on-device, min=507)        ✓ compact
    ///   iPhone 13 mini Option-B repro (min=625)    ✓ compact
    ///   Galaxy S9 portrait/landscape (min=486)     ✓ compact
    ///   iPad mini (min=768)                        ✓ expanded
    ///   iPad Pro (min=1024+)                       ✓ expanded
    ///
    /// 700 sits midway between the largest phone repro (625) and the
    /// smallest tablet (768), with the ±32 dead-band giving lower=668 /
    /// upper=732 — comfortably outside both extremes.
    /// </summary>
    public const float CompactBreakpointPx = 700f;

    /// <summary>
    /// True when the HUD should render in its compact (phone) form: the
    /// shorter viewport edge is below <see cref="CompactBreakpointPx"/>
    /// (with hysteresis). The ±dead-band is symmetric: an expanded window
    /// flips to compact only when min(w,h) drops below
    /// <c>CompactBreakpointPx - deadBand</c>; a compact window flips to
    /// expanded only when min(w,h) climbs above
    /// <c>CompactBreakpointPx + deadBand</c>. Inside the dead-band the
    /// prior state holds, so a window resize that hovers around the
    /// breakpoint can't thrash the layout. Callers pass the previous
    /// compact bit so the function stays pure (no internal state).
    /// </summary>
    public static bool IsCompact(
        float width, float height, bool prevWasCompact, float deadBand = 32f)
    {
        float minSide = System.Math.Min(width, height);
        float threshold = prevWasCompact
            ? CompactBreakpointPx + deadBand
            : CompactBreakpointPx - deadBand;
        return minSide < threshold;
    }
}
