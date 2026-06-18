/// <summary>
/// Per-feature toggles for procedural map generation (issue #48). Each flag
/// gates an optional terrain-scatter pass in
/// <see cref="MapGenerator.BuildInitialGrid"/>; <b>both default off</b> so a
/// generated map with <see cref="None"/> is byte-identical to the pre-#48
/// baseline (the #20 determinism reference — no extra RNG draws happen when a
/// pass is disabled). Written from <c>GameSettings</c> by the new-game setup
/// panel and the map editor's Generate action.
/// </summary>
/// <param name="IncludeMountains">Scatter mountain ranges onto land (Phase 1).</param>
/// <param name="IncludeGold">Scatter gold tiles onto land (Phase 2 — wired but
/// not yet implemented; the flag is present so call sites are threaded once).</param>
public sealed record MapGenOptions(bool IncludeMountains = false, bool IncludeGold = false)
{
    /// <summary>All passes off — the backward-compatible default.</summary>
    public static readonly MapGenOptions None = new();
}
