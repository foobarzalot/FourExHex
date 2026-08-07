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
/// <param name="NeutralDensity">Total neutral coverage target, percent of land,
/// 0..<see cref="NeutralDensityMax"/> — the share of land left unclaimed,
/// features included. <c>0</c> = off — zero extra RNG draws, byte-identical to
/// the baseline. When on, generation inverts: every land tile starts neutral
/// and the players expand into it from spread-out seeds, one cell per player
/// per round, until each reaches an equal quota
/// (<c>(land − land×NeutralDensity/100) / playerCount</c>, ±1 — exact balance
/// by construction); the unclaimed remainder stays neutral. All land is
/// claimable — mountains freely, gold only as a last resort (it should stay
/// neutral and contested). Each player is at most two coherent regions and
/// never a stranded single tile. <see cref="ClumpingFactor"/> still governs
/// sparse↔clumped expansion (it sets the per-player seed count: 100 = one
/// compact blob each).</param>
/// <param name="BarbarianDensity">Passive barbarian coverage, percent of
/// <b>neutral</b> land seeded with neutral Recruit units (non-aggro; see
/// <c>Unit.IsAggro</c>). <c>0</c> = off — zero extra RNG draws, byte-identical
/// to the baseline; likewise when the board has no neutral land to seed.
/// Runs after the tree scatter, on occupant-free non-gold neutral tiles.</param>
public sealed record MapGenOptions(
    int TreeDensity = MapGenOptions.DefaultTreeDensity,
    int MountainDensity = MapGenOptions.DefaultMountainDensity,
    int GoldDensity = MapGenOptions.DefaultGoldDensity,
    int ClumpingFactor = MapGenOptions.DefaultClumpingFactor,
    int NeutralDensity = MapGenOptions.DefaultNeutralDensity,
    int BarbarianDensity = MapGenOptions.DefaultBarbarianDensity)
{
    // Fresh-map defaults — the single source for both this record's parameter
    // defaults and the GameSettings field initializers the setup panel edits.
    // Trees at 5% of land; every other feature off.
    public const int DefaultTreeDensity = 5;
    public const int DefaultMountainDensity = 0;
    public const int DefaultGoldDensity = 0;
    public const int DefaultClumpingFactor = 0;
    public const int DefaultNeutralDensity = 0;
    public const int DefaultBarbarianDensity = 0;

    /// <summary>Default densities — trees at the historical 5%, no mountains or
    /// gold. The backward-compatible baseline.</summary>
    public static readonly MapGenOptions None = new();

    /// <summary>Upper bound for <see cref="NeutralDensity"/>: at most 75% of land
    /// may be neutral, so the players always split at least a quarter of the map.</summary>
    public const int NeutralDensityMax = 75;

    /// <summary>The selectable <see cref="ClumpingFactor"/> values, ascending. The
    /// single source of truth for both the New Game / map-editor stepper and the
    /// per-level campaign draw (<c>CampaignProgress.MapGenOptionsForLevel</c>). The
    /// spacing is deliberately nonlinear (bunched near the top): the visible
    /// difference between clumping levels grows toward 100 — the seed count drops
    /// geometrically — so even spacing would waste the low half on indistinguishable
    /// noise.</summary>
    public static readonly int[] ClumpingFactorStops = { 0, 50, 75, 90, 95, 100 };
}
