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
/// <param name="ClumpingFactor">Sparse↔clumped player-territory assignment (issue #72),
/// 0..100. <c>0</c> = today's per-cell random (fragmented "salt-and-pepper") owner
/// assignment exactly — zero extra RNG draws, byte-identical to the pre-#72 baseline.
/// Higher values seed fewer, larger contiguous regions (seed-flood Voronoi); <c>100</c>
/// = one contiguous blob per player. Affects only owner assignment, never land shape or
/// the tree/mountain/gold scatter.</param>
public sealed record MapGenOptions(
    int TreeDensity = 5, int MountainDensity = 0, int GoldDensity = 0, int ClumpingFactor = 0)
{
    /// <summary>Default densities — trees at the historical 5%, no mountains or
    /// gold. The backward-compatible baseline.</summary>
    public static readonly MapGenOptions None = new();

    /// <summary>The selectable <see cref="ClumpingFactor"/> values, ascending. The
    /// single source of truth for both the New Game / map-editor stepper and the
    /// per-level campaign draw (<c>CampaignProgress.MapGenOptionsForLevel</c>). The
    /// spacing is deliberately nonlinear (bunched near the top): the visible
    /// difference between clumping levels grows toward 100 — the seed count drops
    /// geometrically — so even spacing would waste the low half on indistinguishable
    /// noise (issue #72).</summary>
    public static readonly int[] ClumpingFactorStops = { 0, 50, 75, 90, 95, 100 };
}
