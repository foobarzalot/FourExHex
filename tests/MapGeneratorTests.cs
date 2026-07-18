// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Determinism + shape tests for <see cref="MapGenerator.BuildInitialGrid"/>.
/// Reproducibility is the headline guarantee (same seed → same map). The
/// shape tests pin down the water-island contract: rim is always water,
/// land is one connected blob, water is one connected ring around it,
/// and there are no 1-wide land corridors.
/// </summary>
public class MapGeneratorTests
{
    private const int Cols = 20;
    private const int Rows = 15;

    private static IReadOnlyList<Player> SixPlayers()
    {
        var list = new List<Player>();
        for (int i = 0; i < GameSettings.PlayerConfig.Length; i++)
        {
            (string name, _) = GameSettings.PlayerConfig[i];
            list.Add(new Player(name, PlayerId.FromIndex(i), PlayerKind.Computer));
        }
        return list;
    }

    private static MapGenResult Build(int seed) =>
        MapGenerator.BuildInitialGrid(Cols, Rows, SixPlayers(), seed);

    private static MapGenResult BuildWith(int seed, MapGenOptions options) =>
        MapGenerator.BuildInitialGrid(Cols, Rows, SixPlayers(), seed, options);

    private static int CountMountains(MapGenResult result)
    {
        int n = 0;
        foreach (HexTile t in result.Grid.Tiles)
        {
            if (t.IsMountain) n++;
        }
        return n;
    }

    private static int CountGold(MapGenResult result)
    {
        int n = 0;
        foreach (HexTile t in result.Grid.Tiles)
        {
            if (t.IsGold) n++;
        }
        return n;
    }

    private static int CountTrees(MapGenResult result)
    {
        int n = 0;
        foreach (HexTile t in result.Grid.Tiles)
        {
            if (t.Occupant is Tree) n++;
        }
        return n;
    }

    // Representative "on" densities (percent of land) for the feature-on tests.
    private const int MountainOnDensity = 10;
    private const int GoldOnDensity = 5;

    private static IEnumerable<HexCoord> RimCoords()
    {
        for (int col = 0; col < Cols; col++)
        {
            yield return HexCoord.FromOffset(col, 0);
            yield return HexCoord.FromOffset(col, Rows - 1);
        }
        for (int row = 1; row < Rows - 1; row++)
        {
            yield return HexCoord.FromOffset(0, row);
            yield return HexCoord.FromOffset(Cols - 1, row);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4242)]
    [InlineData(9999)]
    public void SameSeedProducesIdenticalGrid(int seed)
    {
        MapGenResult a = Build(seed);
        MapGenResult b = Build(seed);

        Assert.Equal(a.Grid.Count, b.Grid.Count);
        foreach (HexTile tA in a.Grid.Tiles)
        {
            HexTile? tB = b.Grid.Get(tA.Coord);
            Assert.NotNull(tB);
            Assert.Equal(tA.Owner, tB!.Owner);
            Assert.Equal(tA.Occupant is Tree, tB.Occupant is Tree);
        }
        Assert.Equal(a.WaterCoords, b.WaterCoords);
    }

    [Fact]
    public void DifferentSeedsProduceDifferentGrids()
    {
        MapGenResult a = Build(1);
        MapGenResult b = Build(2);

        bool anyDifference = a.Grid.Count != b.Grid.Count;
        if (!anyDifference)
        {
            foreach (HexTile tA in a.Grid.Tiles)
            {
                HexTile? tB = b.Grid.Get(tA.Coord);
                if (tB == null) { anyDifference = true; break; }
                if (tA.Owner != tB.Owner) { anyDifference = true; break; }
                if ((tA.Occupant is Tree) != (tB.Occupant is Tree)) { anyDifference = true; break; }
            }
        }
        Assert.True(anyDifference, "Seeds 1 and 2 should produce visibly different maps");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(9999)]
    public void RimIsAlwaysWater(int seed)
    {
        MapGenResult result = Build(seed);
        foreach (HexCoord rim in RimCoords())
        {
            Assert.True(result.WaterCoords.Contains(rim),
                $"Rim coord {rim} should be water for seed {seed}");
            Assert.False(result.Grid.Contains(rim),
                $"Rim coord {rim} should not be in the land grid for seed {seed}");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(9999)]
    public void LandIsContiguous(int seed)
    {
        MapGenResult result = Build(seed);
        Assert.NotEmpty(result.Grid.Tiles);

        var visited = new HashSet<HexCoord>();
        var queue = new Queue<HexCoord>();
        HexTile firstTile = System.Linq.Enumerable.First(result.Grid.Tiles);
        queue.Enqueue(firstTile.Coord);
        visited.Add(firstTile.Coord);
        while (queue.Count > 0)
        {
            HexCoord c = queue.Dequeue();
            foreach (HexCoord n in c.Neighbors())
            {
                if (!result.Grid.Contains(n)) continue;
                if (!visited.Add(n)) continue;
                queue.Enqueue(n);
            }
        }
        Assert.Equal(result.Grid.Count, visited.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(9999)]
    public void WaterIsContiguous(int seed)
    {
        MapGenResult result = Build(seed);
        Assert.NotEmpty(result.WaterCoords);

        var visited = new HashSet<HexCoord>();
        var queue = new Queue<HexCoord>();
        HexCoord start = HexCoord.FromOffset(0, 0);
        queue.Enqueue(start);
        visited.Add(start);
        while (queue.Count > 0)
        {
            HexCoord c = queue.Dequeue();
            foreach (HexCoord n in c.Neighbors())
            {
                if (!result.WaterCoords.Contains(n)) continue;
                if (!visited.Add(n)) continue;
                queue.Enqueue(n);
            }
        }
        Assert.Equal(result.WaterCoords.Count, visited.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(9999)]
    public void NoLandTileHasFewerThanThreeLandNeighbors(int seed)
    {
        // A land tile with ≤2 land neighbors is either a peninsula tip (1) or
        // a 1-wide bridge cell (2). Both are exactly the "long thin corridor"
        // shapes the spec rules out. After CA smoothing every surviving land
        // cell must have at least 3 land neighbors.
        MapGenResult result = Build(seed);
        foreach (HexTile tile in result.Grid.Tiles)
        {
            int landNeighbors = 0;
            foreach (HexCoord n in tile.Coord.Neighbors())
            {
                if (result.Grid.Contains(n)) landNeighbors++;
            }
            Assert.True(landNeighbors >= 3,
                $"Land tile at {tile.Coord} has only {landNeighbors} land neighbors (seed {seed})");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(9999)]
    public void OwnersOnlyOnLand(int seed)
    {
        MapGenResult result = Build(seed);
        foreach (HexTile tile in result.Grid.Tiles)
        {
            Assert.False(result.WaterCoords.Contains(tile.Coord),
                $"Tile at {tile.Coord} appears in both grid and water set");
        }
        // Every land tile carries one of the player colors.
        var validColors = new HashSet<PlayerId>();
        foreach (Player p in SixPlayers()) validColors.Add(p.Id);
        foreach (HexTile tile in result.Grid.Tiles)
        {
            Assert.Contains(tile.Owner, validColors);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(9999)]
    public void LandPlusWaterCoversTheRectangle(int seed)
    {
        MapGenResult result = Build(seed);
        Assert.Equal(Cols * Rows, result.Grid.Count + result.WaterCoords.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(9999)]
    public void LandHasReasonableSize(int seed)
    {
        // Sanity check: at least 30 land cells and at most ~2/3 of the map.
        // Anything outside this window is a sign the CA passes are mis-tuned.
        MapGenResult result = Build(seed);
        int total = Cols * Rows;
        Assert.InRange(result.Grid.Count, 30, (total * 2) / 3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(9999)]
    public void GeneratedMapHasNoNeutralTiles(int seed)
    {
        // Neutral (unowned) hexes are editor-only. DeterministicRng
        // generation must assign every land tile to a real player.
        MapGenResult result = Build(seed);
        Assert.All(result.Grid.Tiles, t => Assert.False(t.Owner.IsNone));
    }

    // ── Mountain scatter ───────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(9999)]
    public void MountainsOff_NoMountainTiles(int seed)
    {
        // Default (no options) and an explicit MountainDensity:0 must both
        // leave every tile flat.
        Assert.Equal(0, CountMountains(Build(seed)));
        Assert.Equal(0, CountMountains(BuildWith(seed, new MapGenOptions(MountainDensity: 0))));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(9999)]
    public void MountainsOff_ByteIdenticalToBaseline(int seed)
    {
        // The determinism baseline: turning the (off) mountain pass into a
        // no-op must not perturb owner assignment, tree scatter, or water — i.e.
        // no stray RNG draws happen when MountainDensity is 0.
        MapGenResult baseline = Build(seed);
        MapGenResult off = BuildWith(seed, new MapGenOptions(MountainDensity: 0));

        Assert.Equal(baseline.Grid.Count, off.Grid.Count);
        foreach (HexTile tA in baseline.Grid.Tiles)
        {
            HexTile? tB = off.Grid.Get(tA.Coord);
            Assert.NotNull(tB);
            Assert.Equal(tA.Owner, tB!.Owner);
            Assert.Equal(tA.Occupant is Tree, tB.Occupant is Tree);
            Assert.Equal(tA.IsMountain, tB.IsMountain);
        }
        Assert.Equal(baseline.WaterCoords, off.WaterCoords);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(9999)]
    public void MountainsOn_PlacesMountainsInSaneBand(int seed)
    {
        MapGenResult result = BuildWith(seed, new MapGenOptions(MountainDensity: MountainOnDensity));
        int mountains = CountMountains(result);

        Assert.True(mountains > 0, $"Expected some mountains for seed {seed}");
        // Density target is MountainOnDensity% of land; cap the assertion generously
        // at 25% so a mis-tuned pass that paves the map fails loudly.
        Assert.True(mountains <= result.Grid.Count / 4,
            $"Mountains {mountains} exceed 25% of {result.Grid.Count} land tiles (seed {seed})");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(9999)]
    public void MountainsOn_SameSeedIdenticalMountainSet(int seed)
    {
        var opts = new MapGenOptions(MountainDensity: MountainOnDensity);
        MapGenResult a = BuildWith(seed, opts);
        MapGenResult b = BuildWith(seed, opts);

        foreach (HexTile tA in a.Grid.Tiles)
        {
            HexTile? tB = b.Grid.Get(tA.Coord);
            Assert.NotNull(tB);
            Assert.Equal(tA.IsMountain, tB!.IsMountain);
        }
    }

    [Fact]
    public void MountainsOn_DifferentSeedsProduceDifferentMountains()
    {
        var opts = new MapGenOptions(MountainDensity: MountainOnDensity);
        MapGenResult a = BuildWith(1, opts);
        MapGenResult b = BuildWith(2, opts);

        var mountainsA = new HashSet<HexCoord>();
        foreach (HexTile t in a.Grid.Tiles)
        {
            if (t.IsMountain) mountainsA.Add(t.Coord);
        }
        bool anyDifference = false;
        foreach (HexTile t in b.Grid.Tiles)
        {
            if (t.IsMountain != mountainsA.Contains(t.Coord)) { anyDifference = true; break; }
        }
        Assert.True(anyDifference, "Seeds 1 and 2 should produce different mountain layouts");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(9999)]
    public void MountainsAndGold_NeverShareTile(int seed)
    {
        // Gold and mountain are mutually exclusive: with both
        // passes on, no generated tile carries both flags.
        MapGenResult result = BuildWith(seed, new MapGenOptions(
            MountainDensity: MountainOnDensity, GoldDensity: GoldOnDensity));
        foreach (HexTile t in result.Grid.Tiles)
        {
            Assert.False(t.IsMountain && t.IsGold,
                $"Tile {t.Coord} is both a mountain and gold (seed {seed})");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(9999)]
    public void MountainsOn_OnlyOnLand(int seed)
    {
        MapGenResult result = BuildWith(seed, new MapGenOptions(MountainDensity: MountainOnDensity));
        foreach (HexTile t in result.Grid.Tiles)
        {
            if (!t.IsMountain) continue;
            Assert.False(result.WaterCoords.Contains(t.Coord),
                $"Mountain tile {t.Coord} is also water (seed {seed})");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(9999)]
    public void MountainsOn_MountainTilesAreNeutral_OthersStillOwned(int seed)
    {
        // Generated mountain ranges are neutral terrain players must capture, not
        // pre-owned land. Every non-mountain land tile keeps a real owner.
        MapGenResult result = BuildWith(seed, new MapGenOptions(MountainDensity: MountainOnDensity));
        foreach (HexTile t in result.Grid.Tiles)
        {
            if (t.IsMountain)
                Assert.True(t.Owner.IsNone, $"Mountain tile {t.Coord} should be neutral (seed {seed})");
            else
                Assert.False(t.Owner.IsNone, $"Non-mountain tile {t.Coord} should be owned (seed {seed})");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(9999)]
    public void MountainsOn_FormRangesNotSpeckle(int seed)
    {
        // Mountains should generate in connected ranges: the bulk of mountain
        // tiles must have at least one mountain neighbor. A purely random
        // speckle would leave most tiles isolated.
        MapGenResult result = BuildWith(seed, new MapGenOptions(MountainDensity: MountainOnDensity));
        var mountains = new HashSet<HexCoord>();
        foreach (HexTile t in result.Grid.Tiles)
        {
            if (t.IsMountain) mountains.Add(t.Coord);
        }
        Assert.NotEmpty(mountains);

        int withMountainNeighbor = 0;
        foreach (HexCoord m in mountains)
        {
            foreach (HexCoord n in m.Neighbors())
            {
                if (mountains.Contains(n)) { withMountainNeighbor++; break; }
            }
        }
        // Most mountain tiles (here: > half) should touch another mountain.
        Assert.True(withMountainNeighbor * 2 > mountains.Count,
            $"Only {withMountainNeighbor}/{mountains.Count} mountains are part of a range (seed {seed})");
    }

    // ── Gold scatter ───────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(9999)]
    public void GoldOff_NoGoldTiles(int seed)
    {
        Assert.Equal(0, CountGold(Build(seed)));
        Assert.Equal(0, CountGold(BuildWith(seed, new MapGenOptions(GoldDensity: 0))));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(9999)]
    public void GoldOff_ByteIdenticalToBaseline(int seed)
    {
        MapGenResult baseline = Build(seed);
        MapGenResult off = BuildWith(seed, new MapGenOptions(GoldDensity: 0));

        Assert.Equal(baseline.Grid.Count, off.Grid.Count);
        foreach (HexTile tA in baseline.Grid.Tiles)
        {
            HexTile? tB = off.Grid.Get(tA.Coord);
            Assert.NotNull(tB);
            Assert.Equal(tA.Owner, tB!.Owner);
            Assert.Equal(tA.Occupant is Tree, tB.Occupant is Tree);
            Assert.Equal(tA.IsGold, tB.IsGold);
        }
        Assert.Equal(baseline.WaterCoords, off.WaterCoords);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(9999)]
    public void GoldOn_PlacesGoldInSaneBand(int seed)
    {
        MapGenResult result = BuildWith(seed, new MapGenOptions(GoldDensity: GoldOnDensity));
        int gold = CountGold(result);

        Assert.True(gold > 0, $"Expected some gold for seed {seed}");
        // Target is GoldOnDensity% of land; cap generously at 12% to catch a paving bug.
        Assert.True(gold <= (result.Grid.Count * 12) / 100,
            $"Gold {gold} exceeds 12% of {result.Grid.Count} land tiles (seed {seed})");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(9999)]
    public void GoldOn_SameSeedIdenticalGoldSet(int seed)
    {
        var opts = new MapGenOptions(GoldDensity: GoldOnDensity);
        MapGenResult a = BuildWith(seed, opts);
        MapGenResult b = BuildWith(seed, opts);

        foreach (HexTile tA in a.Grid.Tiles)
        {
            HexTile? tB = b.Grid.Get(tA.Coord);
            Assert.NotNull(tB);
            Assert.Equal(tA.IsGold, tB!.IsGold);
        }
    }

    [Fact]
    public void GoldOn_DifferentSeedsProduceDifferentGold()
    {
        var opts = new MapGenOptions(GoldDensity: GoldOnDensity);
        MapGenResult a = BuildWith(1, opts);
        MapGenResult b = BuildWith(2, opts);

        var goldA = new HashSet<HexCoord>();
        foreach (HexTile t in a.Grid.Tiles)
        {
            if (t.IsGold) goldA.Add(t.Coord);
        }
        bool anyDifference = false;
        foreach (HexTile t in b.Grid.Tiles)
        {
            if (t.IsGold != goldA.Contains(t.Coord)) { anyDifference = true; break; }
        }
        Assert.True(anyDifference, "Seeds 1 and 2 should produce different gold layouts");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(9999)]
    public void GoldOn_TilesAreNeutral(int seed)
    {
        // Generated gold is a contested objective — unowned until captured.
        MapGenResult result = BuildWith(seed, new MapGenOptions(GoldDensity: GoldOnDensity));
        foreach (HexTile t in result.Grid.Tiles)
        {
            if (!t.IsGold) continue;
            Assert.True(t.Owner.IsNone, $"Gold tile {t.Coord} should be neutral (seed {seed})");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(9999)]
    public void GoldOn_FormsClustersNotSpeckle(int seed)
    {
        // Min cluster size 2 means every gold tile touches another gold tile.
        MapGenResult result = BuildWith(seed, new MapGenOptions(GoldDensity: GoldOnDensity));
        var gold = new HashSet<HexCoord>();
        foreach (HexTile t in result.Grid.Tiles)
        {
            if (t.IsGold) gold.Add(t.Coord);
        }
        Assert.NotEmpty(gold);

        int withGoldNeighbor = 0;
        foreach (HexCoord g in gold)
        {
            foreach (HexCoord n in g.Neighbors())
            {
                if (gold.Contains(n)) { withGoldNeighbor++; break; }
            }
        }
        Assert.True(withGoldNeighbor * 2 > gold.Count,
            $"Only {withGoldNeighbor}/{gold.Count} gold tiles are part of a cluster (seed {seed})");
    }

    [Fact]
    public void GoldOn_WithMountains_NeverLandsOnMountain()
    {
        // Gold and mountain are mutually exclusive: gold never lands
        // on a mountain tile. Both resources still appear when both passes
        // are on.
        int goldTotal = 0;
        int mountainTotal = 0;
        var opts = new MapGenOptions(MountainDensity: MountainOnDensity, GoldDensity: GoldOnDensity);
        foreach (int seed in new[] { 1, 7, 42, 100, 9999 })
        {
            MapGenResult result = BuildWith(seed, opts);
            foreach (HexTile t in result.Grid.Tiles)
            {
                if (t.IsGold) goldTotal++;
                if (t.IsMountain) mountainTotal++;
                Assert.False(t.IsGold && t.IsMountain,
                    $"Tile {t.Coord} is both gold and mountain (seed {seed})");
            }
        }
        Assert.True(goldTotal > 0, "Expected gold tiles across the sampled seeds");
        Assert.True(mountainTotal > 0, "Expected mountain tiles across the sampled seeds");
    }

    // ── Tree density ────────────────────────────────────────────

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(9999)]
    public void DefaultOptions_ByteIdenticalToBaseline(int seed)
    {
        // The default MapGenOptions (trees 5, no mountains/gold) must reproduce the
        // no-options baseline exactly — owners, trees, mountains, gold, and water.
        // This pins the determinism reference: density is a no-op at defaults.
        MapGenResult baseline = Build(seed);
        MapGenResult deflt = BuildWith(seed, new MapGenOptions());

        Assert.Equal(baseline.Grid.Count, deflt.Grid.Count);
        foreach (HexTile tA in baseline.Grid.Tiles)
        {
            HexTile? tB = deflt.Grid.Get(tA.Coord);
            Assert.NotNull(tB);
            Assert.Equal(tA.Owner, tB!.Owner);
            Assert.Equal(tA.Occupant is Tree, tB.Occupant is Tree);
            Assert.Equal(tA.IsMountain, tB.IsMountain);
            Assert.Equal(tA.IsGold, tB.IsGold);
        }
        Assert.Equal(baseline.WaterCoords, deflt.WaterCoords);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(9999)]
    public void TreeDensity_DefaultReproducesHistoricalScatter(int seed)
    {
        // With no occupants to dodge (default: no mountains/gold), the default 5%
        // density places exactly grid.Count / 20 trees — the byte-identical-tree-
        // baseline guarantee.
        MapGenResult result = Build(seed);
        Assert.Equal(result.Grid.Count / 20, CountTrees(result));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(9999)]
    public void TreesOff_NoTreeTiles(int seed)
    {
        // TreeDensity 0 turns the forest scatter off entirely.
        Assert.Equal(0, CountTrees(BuildWith(seed, new MapGenOptions(TreeDensity: 0))));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(9999)]
    public void TreeDensity_HigherPlacesMoreTrees(int seed)
    {
        // Denser settings place proportionally more forest. With no occupants to
        // dodge, the count equals land.Count * density / 100.
        int sparse = CountTrees(BuildWith(seed, new MapGenOptions(TreeDensity: 5)));
        int dense = CountTrees(BuildWith(seed, new MapGenOptions(TreeDensity: 20)));
        Assert.True(dense > sparse,
            $"TreeDensity 20 ({dense}) should place more than 5 ({sparse}) for seed {seed}");
    }

    // ── Mountain / gold density scaling ─────────────────────────

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(9999)]
    public void MountainDensity_HigherPlacesMoreMountains(int seed)
    {
        int sparse = CountMountains(BuildWith(seed, new MapGenOptions(MountainDensity: 5)));
        int dense = CountMountains(BuildWith(seed, new MapGenOptions(MountainDensity: 20)));
        Assert.True(dense > sparse,
            $"MountainDensity 20 ({dense}) should place more than 5 ({sparse}) for seed {seed}");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(9999)]
    public void GoldDensity_HigherPlacesMoreGold(int seed)
    {
        int sparse = CountGold(BuildWith(seed, new MapGenOptions(GoldDensity: 5)));
        int dense = CountGold(BuildWith(seed, new MapGenOptions(GoldDensity: 20)));
        Assert.True(dense > sparse,
            $"GoldDensity 20 ({dense}) should place more than 5 ({sparse}) for seed {seed}");
    }

    // ── Clumping factor ─────────────────────────────────────────

    /// <summary>Fraction (as integer percent) of land–land adjacencies whose two
    /// tiles share an owner. Higher = more spatially contiguous (clumped); the
    /// salt-and-pepper baseline scores low (≈ 1/playerCount). Integer-only so the
    /// metric itself doesn't smuggle a float into a test of a no-floats subsystem.</summary>
    private static int SameOwnerNeighborPercent(MapGenResult result)
    {
        int sameOwner = 0;
        int adjacencies = 0;
        foreach (HexTile tile in result.Grid.Tiles)
        {
            foreach (HexCoord nb in tile.Coord.Neighbors())
            {
                HexTile? other = result.Grid.Get(nb);
                if (other == null) continue;
                adjacencies++;
                if (other.Owner == tile.Owner) sameOwner++;
            }
        }
        return adjacencies == 0 ? 0 : sameOwner * 100 / adjacencies;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(9999)]
    public void ClumpingZero_ByteIdenticalToBaseline(int seed)
    {
        // Factor 0 must reproduce today's per-cell random owner assignment exactly —
        // zero extra RNG draws, so owners, trees, and water all match the no-options
        // baseline. This pins the determinism reference: clumping is a no-op
        // at 0 (gated like the mountain/gold passes).
        MapGenResult baseline = Build(seed);
        MapGenResult zero = BuildWith(seed, new MapGenOptions(ClumpingFactor: 0));

        Assert.Equal(baseline.Grid.Count, zero.Grid.Count);
        foreach (HexTile tA in baseline.Grid.Tiles)
        {
            HexTile? tB = zero.Grid.Get(tA.Coord);
            Assert.NotNull(tB);
            Assert.Equal(tA.Owner, tB!.Owner);
            Assert.Equal(tA.Occupant is Tree, tB.Occupant is Tree);
        }
        Assert.Equal(baseline.WaterCoords, zero.WaterCoords);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(9999)]
    public void MaxClumping_IsMoreContiguous(int seed)
    {
        // "Clumping actually clumps": at factor 100 each player owns a coherent blob,
        // so the share of same-owner adjacencies is far above the fragmented baseline
        // (which sits near 1/6 ≈ 17% for six players).
        int baseline = SameOwnerNeighborPercent(BuildWith(seed, new MapGenOptions(ClumpingFactor: 0)));
        int clumped = SameOwnerNeighborPercent(BuildWith(seed, new MapGenOptions(ClumpingFactor: 100)));

        Assert.True(clumped > baseline + 20,
            $"Max clumping ({clumped}%) should be well above baseline ({baseline}%) for seed {seed}");
        Assert.True(clumped >= 60,
            $"Max clumping ({clumped}%) should be strongly contiguous for seed {seed}");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(9999)]
    public void MaxClumping_DistributesLandFairly(int seed)
    {
        // At factor 100 the one-region-per-player Voronoi keeps every player's land
        // within a sane band of the fair average. The band (fair/3 .. 5/2×fair) is a
        // gross-unfairness guard, not a tight equality claim — a few point-seeds over
        // a jagged small continent can't carve perfectly equal areas.
        MapGenResult result = BuildWith(seed, new MapGenOptions(ClumpingFactor: 100));
        var counts = new Dictionary<PlayerId, int>();
        foreach (Player p in SixPlayers()) counts[p.Id] = 0;
        foreach (HexTile tile in result.Grid.Tiles) counts[tile.Owner]++;

        int fair = result.Grid.Count / counts.Count;
        foreach (KeyValuePair<PlayerId, int> kv in counts)
        {
            Assert.True(kv.Value >= fair / 3,
                $"{kv.Key} starved with {kv.Value} of fair {fair} land (seed {seed})");
            Assert.True(kv.Value <= fair * 5 / 2,
                $"{kv.Key} hogged {kv.Value} of fair {fair} land (seed {seed})");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(9999)]
    public void SameSeedSameClumping_ProducesIdenticalGrid(int seed)
    {
        // Determinism in the new clumped path: a fixed seed + fixed factor reproduces
        // the same owner assignment and tree scatter byte-for-byte.
        var opts = new MapGenOptions(ClumpingFactor: 60);
        MapGenResult a = BuildWith(seed, opts);
        MapGenResult b = BuildWith(seed, opts);

        Assert.Equal(a.Grid.Count, b.Grid.Count);
        foreach (HexTile tA in a.Grid.Tiles)
        {
            HexTile? tB = b.Grid.Get(tA.Coord);
            Assert.NotNull(tB);
            Assert.Equal(tA.Owner, tB!.Owner);
            Assert.Equal(tA.Occupant is Tree, tB.Occupant is Tree);
        }
        Assert.Equal(a.WaterCoords, b.WaterCoords);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(9999)]
    public void Clumped_OwnersOnlyOnLandAndAmongPlayers(int seed)
    {
        // Clumping reassigns owners but must never introduce neutral/unowned land or
        // a stray id: every land tile still carries one of the player colors.
        MapGenResult result = BuildWith(seed, new MapGenOptions(ClumpingFactor: 100));
        var validColors = new HashSet<PlayerId>();
        foreach (Player p in SixPlayers()) validColors.Add(p.Id);
        foreach (HexTile tile in result.Grid.Tiles)
        {
            Assert.False(tile.Owner.IsNone, $"Tile {tile.Coord} is neutral under clumping (seed {seed})");
            Assert.Contains(tile.Owner, validColors);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(9999)]
    public void Clumped_CapitalsPlaceable(int seed)
    {
        // Fairness / capital-placeability: a fully clumped map must still
        // reconcile into territories with a capital on every multi-hex region.
        MapGenResult result = BuildWith(seed, new MapGenOptions(ClumpingFactor: 100));
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(result.Grid);
        foreach (Territory t in territories)
        {
            if (t.Owner.IsNone) continue; // neutral regions never get capitals
            if (t.Size < 2) continue;     // singletons are capital-less by rule
            Assert.True(t.HasCapital,
                $"Multi-hex territory of {t.Owner} (size {t.Size}) lacks a capital (seed {seed})");
        }
    }
}
