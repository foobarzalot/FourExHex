using System.Collections.Generic;
using System.Linq;
using Godot;
using Xunit;

namespace FourExHex.Tests;

public class TerritoryFinderTests
{
    private static readonly Color Red = new Color(1f, 0f, 0f);
    private static readonly Color Blue = new Color(0f, 0f, 1f);
    private static readonly Color Green = new Color(0f, 1f, 0f);
    private static readonly Color Yellow = new Color(1f, 1f, 0f);
    private static readonly Color Purple = new Color(0.5f, 0f, 0.5f);
    private static readonly Color Teal = new Color(0f, 0.7f, 0.7f);

    private static readonly Color[] Palette = { Red, Blue, Green, Yellow, Purple, Teal };

    // --- Helpers -----------------------------------------------------------

    private static HexGrid BuildUniformGrid(int cols, int rows, Color color)
    {
        var grid = new HexGrid();
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                grid.Add(new HexTile(HexCoord.FromOffset(c, r), color));
            }
        }
        return grid;
    }

    private static HexGrid BuildRandomColoredGrid(int cols, int rows, int seed)
    {
        var rng = new System.Random(seed);
        var grid = new HexGrid();
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                Color color = Palette[rng.Next(Palette.Length)];
                grid.Add(new HexTile(HexCoord.FromOffset(c, r), color));
            }
        }
        return grid;
    }

    // --- Trivial shapes ----------------------------------------------------

    [Fact]
    public void EmptyGrid_ReturnsNoTerritories()
    {
        var grid = new HexGrid();

        var territories = TerritoryFinder.FindAll(grid);

        Assert.Empty(territories);
    }

    [Fact]
    public void SingleTile_ReturnsOneTerritoryOfSizeOne()
    {
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));

        var territories = TerritoryFinder.FindAll(grid);

        Assert.Single(territories);
        Assert.Equal(1, territories[0].Size);
        Assert.Equal(Red, territories[0].Owner);
        Assert.Contains(new HexCoord(0, 0), territories[0].Coords);
    }

    [Fact]
    public void TwoAdjacentSameColor_FormsOneTerritoryOfSizeTwo()
    {
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Add(new HexTile(new HexCoord(1, 0), Red)); // E neighbor

        var territories = TerritoryFinder.FindAll(grid);

        Assert.Single(territories);
        Assert.Equal(2, territories[0].Size);
    }

    [Fact]
    public void TwoAdjacentDifferentColor_FormsTwoSingletons()
    {
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Add(new HexTile(new HexCoord(1, 0), Blue));

        var territories = TerritoryFinder.FindAll(grid);

        Assert.Equal(2, territories.Count);
        Assert.All(territories, t => Assert.Equal(1, t.Size));
        Assert.Contains(territories, t => t.Owner == Red);
        Assert.Contains(territories, t => t.Owner == Blue);
    }

    [Fact]
    public void TwoDisconnectedSameColorTiles_FormsTwoSingletons()
    {
        // Sparse grid: two red tiles with nothing between them. A naive
        // "group by color" impl would collapse them to one territory.
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Add(new HexTile(new HexCoord(5, 5), Red));

        var territories = TerritoryFinder.FindAll(grid);

        Assert.Equal(2, territories.Count);
        Assert.All(territories, t => Assert.Equal(1, t.Size));
        Assert.All(territories, t => Assert.Equal(Red, t.Owner));
    }

    // --- Shape correctness -------------------------------------------------

    [Fact]
    public void UniformGrid_FormsOneTerritoryCoveringAllTiles()
    {
        HexGrid grid = BuildUniformGrid(18, 13, Red);

        var territories = TerritoryFinder.FindAll(grid);

        Assert.Single(territories);
        Assert.Equal(18 * 13, territories[0].Size);
        Assert.Equal(Red, territories[0].Owner);
    }

    [Fact]
    public void UShape_ConnectedThroughBottomOfU_FormsOneTerritory()
    {
        // A 10-tile U: left column (col 0, rows 0-3), right column
        // (col 3, rows 0-3), and the bottom of the U (cols 1,2 at row 3).
        // All red. Nothing in between upper left and upper right.
        var grid = new HexGrid();
        for (int r = 0; r < 4; r++)
        {
            grid.Add(new HexTile(HexCoord.FromOffset(0, r), Red));
            grid.Add(new HexTile(HexCoord.FromOffset(3, r), Red));
        }
        grid.Add(new HexTile(HexCoord.FromOffset(1, 3), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(2, 3), Red));

        var territories = TerritoryFinder.FindAll(grid);

        Assert.Single(territories);
        Assert.Equal(10, territories[0].Size);
    }

    [Fact]
    public void LineSplitByEnemyTile_FormsTwoSeparateRedTerritories()
    {
        // Row 0: red red BLUE red red. Every cell is populated; the blue
        // is a wall, not a gap. Naive neighbor-walk without color check
        // would fail this.
        var grid = new HexGrid();
        grid.Add(new HexTile(HexCoord.FromOffset(0, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(1, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(2, 0), Blue));
        grid.Add(new HexTile(HexCoord.FromOffset(3, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(4, 0), Red));

        var territories = TerritoryFinder.FindAll(grid);

        Assert.Equal(3, territories.Count);
        var reds = territories.Where(t => t.Owner == Red).ToList();
        var blues = territories.Where(t => t.Owner == Blue).ToList();
        Assert.Equal(2, reds.Count);
        Assert.All(reds, r => Assert.Equal(2, r.Size));
        Assert.Single(blues);
        Assert.Equal(1, blues[0].Size);
    }

    [Fact]
    public void IslandSurroundedByEnemy_IsItsOwnTerritory()
    {
        var grid = new HexGrid();
        var center = new HexCoord(5, 5);
        grid.Add(new HexTile(center, Red));
        foreach (HexCoord n in center.Neighbors())
        {
            grid.Add(new HexTile(n, Blue));
        }

        var territories = TerritoryFinder.FindAll(grid);

        Assert.Equal(2, territories.Count);
        Territory red = territories.Single(t => t.Owner == Red);
        Territory blue = territories.Single(t => t.Owner == Blue);
        Assert.Equal(1, red.Size);
        Assert.Equal(6, blue.Size);
    }

    // --- Global invariants over a random multi-color grid ------------------

    [Fact]
    public void EveryTileBelongsToExactlyOneTerritory()
    {
        HexGrid grid = BuildRandomColoredGrid(18, 13, seed: 42);
        var territories = TerritoryFinder.FindAll(grid);

        var allCoords = territories.SelectMany(t => t.Coords).ToList();

        // No duplicates.
        Assert.Equal(allCoords.Count, allCoords.Distinct().Count());
        // Every grid coord is covered exactly once.
        Assert.Equal(grid.Count, allCoords.Count);
        var expected = grid.Tiles.Select(t => t.Coord).ToHashSet();
        Assert.Equal(expected, allCoords.ToHashSet());
    }

    [Fact]
    public void EveryTerritoryIsColorHomogeneous()
    {
        HexGrid grid = BuildRandomColoredGrid(18, 13, seed: 7);
        var territories = TerritoryFinder.FindAll(grid);

        foreach (Territory territory in territories)
        {
            foreach (HexCoord coord in territory.Coords)
            {
                HexTile? tile = grid.Get(coord);
                Assert.NotNull(tile);
                Assert.Equal(territory.Owner, tile!.Color);
            }
        }
    }

    [Fact]
    public void EveryTerritoryIsInternallyConnected()
    {
        // Independent BFS within each territory verifies connectedness without
        // going through the same code path as TerritoryFinder.
        HexGrid grid = BuildRandomColoredGrid(18, 13, seed: 123);
        var territories = TerritoryFinder.FindAll(grid);

        foreach (Territory territory in territories)
        {
            AssertInternallyConnected(territory, grid);
        }
    }

    private static void AssertInternallyConnected(Territory territory, HexGrid grid)
    {
        if (territory.Size == 0) return;

        var inside = territory.Coords.ToHashSet();
        HexCoord start = territory.Coords.First();
        var visited = new HashSet<HexCoord> { start };
        var queue = new Queue<HexCoord>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            HexCoord current = queue.Dequeue();
            foreach (HexTile neighbor in grid.NeighborsOf(current))
            {
                if (inside.Contains(neighbor.Coord) && visited.Add(neighbor.Coord))
                {
                    queue.Enqueue(neighbor.Coord);
                }
            }
        }

        Assert.Equal(territory.Size, visited.Count);
    }

    // --- Territory class basics --------------------------------------------

    [Fact]
    public void Territory_SizeMatchesCoordCount()
    {
        var coords = new[]
        {
            new HexCoord(0, 0),
            new HexCoord(1, 0),
            new HexCoord(0, 1),
        };

        var t = new Territory(Red, coords);

        Assert.Equal(3, t.Size);
        Assert.Equal(3, t.Coords.Count);
    }

    [Fact]
    public void EveryFoundTerritory_HasNoDuplicateCoords()
    {
        HexGrid grid = BuildRandomColoredGrid(18, 13, seed: 99);
        var territories = TerritoryFinder.FindAll(grid);

        foreach (Territory t in territories)
        {
            Assert.Equal(t.Coords.Count, t.Coords.Distinct().Count());
        }
    }
}
