/// <summary>
/// Per-feature densities for procedural map generation (issue #48 / #66). Each
/// density is a <b>percent of land tiles</b> the matching scatter pass in
/// <see cref="MapGenerator.BuildInitialGrid"/> aims to cover; <c>0</c> turns the
/// feature off entirely (mountains/gold make zero extra RNG draws when off, so a
/// map with <see cref="None"/> is byte-identical to the pre-#48 baseline — the
/// #20 determinism reference). Written from <c>GameSettings</c> by the new-game
/// setup panel and the map editor's Generate action; the campaign derives its own
/// per-level densities via <c>CampaignProgress.MapGenOptionsForLevel</c>.
/// </summary>
/// <param name="TreeDensity">Forest coverage, percent of land. Default 5 reproduces
/// the historical <c>grid.Count / 20</c> tree scatter exactly.</param>
/// <param name="MountainDensity">Mountain-range coverage, percent of land. 0 = none.</param>
/// <param name="GoldDensity">Gold-cluster coverage, percent of land. 0 = none.</param>
public sealed record MapGenOptions(
    int TreeDensity = 5, int MountainDensity = 0, int GoldDensity = 0)
{
    /// <summary>Default densities — trees at the historical 5%, no mountains or
    /// gold. The backward-compatible baseline.</summary>
    public static readonly MapGenOptions None = new();
}
