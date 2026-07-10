/// <summary>
/// Copy and gate logic for the one-time terrain-feature intro overlays (issue
/// #53). Gold tiles and mountains are shipped mechanics that nothing teaches, so
/// the first map the player starts that contains each one opens with a short
/// explainer over the board while the camera eases to a representative tile. The
/// "seen once" flag lives in <see cref="UserSettings"/>; the overlay itself is
/// <c>Main</c> driving <c>HudView.ShowTappableTutorialMessage</c> plus
/// <c>IHexMapView.CenterOnCoord</c> for the pan.
///
/// Pure content + predicate — no Godot nodes — mirroring <see cref="GameModeIntro"/>,
/// so the same <c>Main._Ready</c> seam teaches every launch path (campaign,
/// custom game, next-unbeaten, starting map, resume).
/// </summary>
public static class TerrainIntro
{
    /// <summary>
    /// The intro paragraph for <paramref name="feature"/>, or null when it has
    /// no explainer (<see cref="TerrainFeature.None"/>). The overlay has no
    /// separate title label, so each paragraph leads with the feature name.
    /// </summary>
    public static string? TextFor(TerrainFeature feature) => feature switch
    {
        TerrainFeature.Gold => Strings.Get(StringKeys.IntroGoldHex),
        TerrainFeature.Mountain => Strings.Get(StringKeys.IntroMountainHex),
        _ => null,
    };

    /// <summary>
    /// True when the map contains this feature (<paramref name="mapHasFeature"/>),
    /// the feature has an intro, AND the player hasn't dismissed it yet.
    /// </summary>
    public static bool ShouldShow(TerrainFeature feature, bool mapHasFeature) =>
        mapHasFeature
        && TextFor(feature) != null
        && !UserSettings.HasSeenTerrainIntro(feature);
}
