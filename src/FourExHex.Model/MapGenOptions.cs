// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
/// <summary>
/// Per-feature densities for procedural map generation. Each
/// density is a <b>percent of land tiles</b> the matching scatter pass in
/// <see cref="MapGenerator.BuildInitialGrid"/> aims to cover; <c>0</c> turns the
/// feature off entirely (mountains/gold make zero extra RNG draws when off, so a
/// map with <see cref="None"/> is byte-identical to the default-density baseline).
/// Written from <c>GameSettings</c> by the new-game
/// setup panel and the map editor's Generate action; the campaign derives its own
/// per-level densities via <c>CampaignProgress.MapGenOptionsForLevel</c>.
/// </summary>
/// <param name="TreeDensity">Forest coverage, percent of land. Default 5% ≈ one
/// tree per 20 land tiles.</param>
/// <param name="MountainDensity">Mountain-range coverage, percent of land. 0 = none.</param>
/// <param name="GoldDensity">Gold-cluster coverage, percent of land. 0 = none.</param>
/// <param name="ClumpingFactor">Sparse↔clumped player-territory assignment,
/// 0..100. <c>0</c> = per-cell random (fragmented "salt-and-pepper") owner
/// assignment exactly — zero extra RNG draws, byte-identical to the zero-extra-RNG baseline.
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
    /// noise.</summary>
    public static readonly int[] ClumpingFactorStops = { 0, 50, 75, 90, 95, 100 };
}
