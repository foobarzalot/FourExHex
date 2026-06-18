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
        // Issue #39: neutral (unowned) hexes are editor-only. Random
        // generation must assign every land tile to a real player.
        MapGenResult result = Build(seed);
        Assert.All(result.Grid.Tiles, t => Assert.False(t.Owner.IsNone));
    }

    // ── Mountain scatter (issue #48, Phase 1) ───────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(9999)]
    public void MountainsOff_NoMountainTiles(int seed)
    {
        // Default (no options) and an explicit IncludeMountains:false must both
        // leave every tile flat.
        Assert.Equal(0, CountMountains(Build(seed)));
        Assert.Equal(0, CountMountains(BuildWith(seed, new MapGenOptions(IncludeMountains: false))));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(9999)]
    public void MountainsOff_ByteIdenticalToBaseline(int seed)
    {
        // The #20 determinism baseline: turning the (off) mountain pass into a
        // no-op must not perturb owner assignment, tree scatter, or water — i.e.
        // no stray RNG draws happen when IncludeMountains is false.
        MapGenResult baseline = Build(seed);
        MapGenResult off = BuildWith(seed, new MapGenOptions(IncludeMountains: false));

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
        MapGenResult result = BuildWith(seed, new MapGenOptions(IncludeMountains: true));
        int mountains = CountMountains(result);

        Assert.True(mountains > 0, $"Expected some mountains for seed {seed}");
        // Density target is ~9% of land; cap the assertion generously at 25% so
        // a mis-tuned pass that paves the map fails loudly.
        Assert.True(mountains <= result.Grid.Count / 4,
            $"Mountains {mountains} exceed 25% of {result.Grid.Count} land tiles (seed {seed})");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(9999)]
    public void MountainsOn_SameSeedIdenticalMountainSet(int seed)
    {
        var opts = new MapGenOptions(IncludeMountains: true);
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
        var opts = new MapGenOptions(IncludeMountains: true);
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
    public void MountainsOn_NeverCoexistWithTrees(int seed)
    {
        // Mountain and tree are mutually exclusive (matches the editor brush rule).
        MapGenResult result = BuildWith(seed, new MapGenOptions(IncludeMountains: true));
        foreach (HexTile t in result.Grid.Tiles)
        {
            Assert.False(t.IsMountain && t.Occupant is Tree,
                $"Tile {t.Coord} is both a mountain and a tree (seed {seed})");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(9999)]
    public void MountainsOn_OnlyOnLand(int seed)
    {
        MapGenResult result = BuildWith(seed, new MapGenOptions(IncludeMountains: true));
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
        MapGenResult result = BuildWith(seed, new MapGenOptions(IncludeMountains: true));
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
        MapGenResult result = BuildWith(seed, new MapGenOptions(IncludeMountains: true));
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

    // ── Gold scatter (issue #48, Phase 2) ───────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(9999)]
    public void GoldOff_NoGoldTiles(int seed)
    {
        Assert.Equal(0, CountGold(Build(seed)));
        Assert.Equal(0, CountGold(BuildWith(seed, new MapGenOptions(IncludeGold: false))));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(9999)]
    public void GoldOff_ByteIdenticalToBaseline(int seed)
    {
        MapGenResult baseline = Build(seed);
        MapGenResult off = BuildWith(seed, new MapGenOptions(IncludeGold: false));

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
        MapGenResult result = BuildWith(seed, new MapGenOptions(IncludeGold: true));
        int gold = CountGold(result);

        Assert.True(gold > 0, $"Expected some gold for seed {seed}");
        // Target is ~3% of land; cap generously at 12% to catch a paving bug.
        Assert.True(gold <= (result.Grid.Count * 12) / 100,
            $"Gold {gold} exceeds 12% of {result.Grid.Count} land tiles (seed {seed})");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(9999)]
    public void GoldOn_SameSeedIdenticalGoldSet(int seed)
    {
        var opts = new MapGenOptions(IncludeGold: true);
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
        var opts = new MapGenOptions(IncludeGold: true);
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
        MapGenResult result = BuildWith(seed, new MapGenOptions(IncludeGold: true));
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
        MapGenResult result = BuildWith(seed, new MapGenOptions(IncludeGold: true));
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
    public void GoldOn_WithMountains_BiasesGoldOntoMountains()
    {
        // With both passes on, gold cluster seeds are biased toward mountain tiles,
        // so the share of gold tiles that are also mountains sits well above the
        // ~9% mountain land coverage. Aggregate over seeds for a stable fraction.
        int goldTotal = 0;
        int goldOnMountain = 0;
        var opts = new MapGenOptions(IncludeMountains: true, IncludeGold: true);
        foreach (int seed in new[] { 1, 7, 42, 100, 9999 })
        {
            MapGenResult result = BuildWith(seed, opts);
            foreach (HexTile t in result.Grid.Tiles)
            {
                if (!t.IsGold) continue;
                goldTotal++;
                if (t.IsMountain) goldOnMountain++;
            }
        }
        Assert.True(goldTotal > 0, "Expected gold tiles across the sampled seeds");
        // Well above 9% chance coverage — the seed bias should land many gold
        // clusters on or beside mountains.
        Assert.True(goldOnMountain * 100 > goldTotal * 20,
            $"Only {goldOnMountain}/{goldTotal} gold tiles are also mountains — bias not evident");
    }
}
