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
    /// Landscape reserves a single bottom strip (<paramref name="landscapeBarHeight"/>)
    /// and nothing at the top. Portrait reserves the bottom bar always, and the
    /// top bar only when it's currently shown (the gameplay HUD keeps it up
    /// always now; the param stays for callers that hide it).
    /// </summary>
    public static MapInsets ComputeInsets(
        ScreenOrientation orientation,
        bool topBarVisible,
        float landscapeBarHeight,
        float portraitTopBarHeight,
        float portraitBottomBarHeight) =>
        orientation == ScreenOrientation.Landscape
            ? new MapInsets(0f, landscapeBarHeight)
            : new MapInsets(topBarVisible ? portraitTopBarHeight : 0f, portraitBottomBarHeight);
}
