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
}
