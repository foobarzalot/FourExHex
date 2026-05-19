using System.Linq;
using Xunit;

namespace FourExHex.Tests;

public class HexGridTests
{
    private static HexTile MakeTile(int q, int r) =>
        new HexTile(new HexCoord(q, r), PlayerId.None);

    /// <summary>
    /// Fill a grid with one tile per (col, row) in a rectangular odd-r offset
    /// shape. Mirrors what HexMap builds at runtime, so the neighbor-at-edge
    /// tests exercise the real map topology.
    /// </summary>
    private static HexGrid BuildRectangularGrid(int cols, int rows)
    {
        var grid = new HexGrid();
        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                grid.Add(new HexTile(HexCoord.FromOffset(col, row), PlayerId.None));
            }
        }
        return grid;
    }

    [Fact]
    public void Add_ThenGet_ReturnsSameInstance()
    {
        var grid = new HexGrid();
        HexTile tile = MakeTile(2, 3);

        grid.Add(tile);

        Assert.Same(tile, grid.Get(new HexCoord(2, 3)));
    }

    [Fact]
    public void Get_MissingCoord_ReturnsNull()
    {
        var grid = new HexGrid();

        Assert.Null(grid.Get(new HexCoord(0, 0)));
    }

    [Fact]
    public void Contains_TracksAddedTiles()
    {
        var grid = new HexGrid();
        grid.Add(MakeTile(1, 1));

        Assert.True(grid.Contains(new HexCoord(1, 1)));
        Assert.False(grid.Contains(new HexCoord(2, 2)));
    }

    [Fact]
    public void Add_SameCoordTwice_Replaces()
    {
        var grid = new HexGrid();
        HexTile first = MakeTile(0, 0);
        HexTile second = MakeTile(0, 0);

        grid.Add(first);
        grid.Add(second);

        Assert.Equal(1, grid.Count);
        Assert.Same(second, grid.Get(new HexCoord(0, 0)));
    }

    [Fact]
    public void Remove_ExistingCoord_DropsTile()
    {
        var grid = new HexGrid();
        grid.Add(MakeTile(2, 3));

        bool removed = grid.Remove(new HexCoord(2, 3));

        Assert.True(removed);
        Assert.False(grid.Contains(new HexCoord(2, 3)));
        Assert.Null(grid.Get(new HexCoord(2, 3)));
        Assert.Equal(0, grid.Count);
    }

    [Fact]
    public void Remove_MissingCoord_ReturnsFalseAndIsNoop()
    {
        var grid = new HexGrid();
        grid.Add(MakeTile(0, 0));

        bool removed = grid.Remove(new HexCoord(5, 5));

        Assert.False(removed);
        Assert.Equal(1, grid.Count);
        Assert.True(grid.Contains(new HexCoord(0, 0)));
    }

    [Fact]
    public void Remove_ThenAddSameCoord_ReplacesEntry()
    {
        var grid = new HexGrid();
        HexTile first = MakeTile(0, 0);
        HexTile second = MakeTile(0, 0);
        grid.Add(first);

        grid.Remove(new HexCoord(0, 0));
        grid.Add(second);

        Assert.Equal(1, grid.Count);
        Assert.Same(second, grid.Get(new HexCoord(0, 0)));
    }

    [Fact]
    public void NeighborsOf_InteriorTile_ReturnsAllSix()
    {
        var grid = new HexGrid();
        var center = new HexCoord(0, 0);
        grid.Add(MakeTile(0, 0));
        foreach (HexCoord n in center.Neighbors())
        {
            grid.Add(new HexTile(n, PlayerId.None));
        }

        var neighbors = grid.NeighborsOf(center).ToList();

        Assert.Equal(6, neighbors.Count);
    }

    [Fact]
    public void NeighborsOf_PartialNeighborhood_ReturnsOnlyPresentTiles()
    {
        var grid = new HexGrid();
        var center = new HexCoord(0, 0);
        grid.Add(MakeTile(0, 0));
        grid.Add(MakeTile(1, 0));  // E
        grid.Add(MakeTile(0, 1));  // SE

        var neighbors = grid.NeighborsOf(center).ToList();

        Assert.Equal(2, neighbors.Count);
        Assert.Contains(neighbors, t => t.Coord == new HexCoord(1, 0));
        Assert.Contains(neighbors, t => t.Coord == new HexCoord(0, 1));
    }

    [Fact]
    public void NeighborsOf_MissingCenter_StillReturnsExistingNeighbors()
    {
        // Neighbor lookup shouldn't require the center tile itself to exist.
        var grid = new HexGrid();
        grid.Add(MakeTile(1, 0));

        var neighbors = grid.NeighborsOf(new HexCoord(0, 0)).ToList();

        Assert.Single(neighbors);
    }

    [Fact]
    public void Tiles_EnumeratesAllAdded()
    {
        var grid = new HexGrid();
        grid.Add(MakeTile(0, 0));
        grid.Add(MakeTile(1, 0));
        grid.Add(MakeTile(0, 1));

        Assert.Equal(3, grid.Count);
        Assert.Equal(3, grid.Tiles.Count());
    }

    // -- Edges of a real 18x13 bounded grid (matches HexMap's default shape) --

    [Theory]
    // Corners of an 18x13 odd-r offset grid. Top/bottom rows (0 and 12) are
    // even rows so they don't shift; the left corners have only 2 neighbors.
    [InlineData(0, 0, 2)]    // top-left    : E, SE
    [InlineData(17, 0, 3)]   // top-right   : W, SW, SE
    [InlineData(0, 12, 2)]   // bottom-left : E, NE
    [InlineData(17, 12, 3)]  // bottom-right: W, NW, NE
    public void NeighborsOf_CornerOfBoundedGrid_HasExpectedCount(int col, int row, int expected)
    {
        HexGrid grid = BuildRectangularGrid(18, 13);
        HexCoord corner = HexCoord.FromOffset(col, row);

        int count = grid.NeighborsOf(corner).Count();

        Assert.Equal(expected, count);
    }

    [Theory]
    // Non-corner edges. Row parity governs how many neighbors an edge tile
    // has because odd rows are shifted right by half a hex:
    //   Left edge  -> even row has only E/NE/SE present (3);
    //                 odd row additionally has NW=(col,row-1) and SW=(col,row+1) (5).
    //   Right edge -> even row has only E missing, keeping 5;
    //                 odd row loses E, NE, and SE to the right wall, keeping 3.
    // Top/bottom interior columns always lose the 2 neighbors above or below
    // the grid, leaving 4.
    [InlineData(5, 0, 4)]    // top edge, interior column
    [InlineData(5, 12, 4)]   // bottom edge, interior column
    [InlineData(0, 2, 3)]    // left edge, even row
    [InlineData(0, 1, 5)]    // left edge, odd row
    [InlineData(17, 2, 5)]   // right edge, even row
    [InlineData(17, 1, 3)]   // right edge, odd row
    public void NeighborsOf_NonCornerEdgeOfBoundedGrid_HasExpectedCount(int col, int row, int expected)
    {
        HexGrid grid = BuildRectangularGrid(18, 13);
        HexCoord edge = HexCoord.FromOffset(col, row);

        int count = grid.NeighborsOf(edge).Count();

        Assert.Equal(expected, count);
    }

    [Fact]
    public void NeighborsOf_InteriorTileOfBoundedGrid_HasSix()
    {
        HexGrid grid = BuildRectangularGrid(18, 13);
        HexCoord interior = HexCoord.FromOffset(9, 6);

        int count = grid.NeighborsOf(interior).Count();

        Assert.Equal(6, count);
    }

    [Fact]
    public void NeighborsOf_TopLeftCorner_ReturnsExactlyEastAndSouthEast()
    {
        HexGrid grid = BuildRectangularGrid(18, 13);
        HexCoord topLeft = HexCoord.FromOffset(0, 0);

        var neighborCoords = grid.NeighborsOf(topLeft)
            .Select(t => t.Coord)
            .ToHashSet();

        Assert.Equal(2, neighborCoords.Count);
        Assert.Contains(HexCoord.FromOffset(1, 0), neighborCoords);
        Assert.Contains(HexCoord.FromOffset(0, 1), neighborCoords);
    }

    [Fact]
    public void NeighborsOf_IsReciprocalAcrossBoundedGrid()
    {
        // For every pair (A, B) where B is listed as a neighbor of A, A must
        // appear in B's neighbor list. Catches asymmetric neighbor logic.
        HexGrid grid = BuildRectangularGrid(18, 13);

        foreach (HexTile tile in grid.Tiles)
        {
            foreach (HexTile neighbor in grid.NeighborsOf(tile.Coord))
            {
                var back = grid.NeighborsOf(neighbor.Coord).Select(t => t.Coord);
                Assert.Contains(tile.Coord, back);
            }
        }
    }

    [Fact]
    public void NeighborsOf_SumOfDegreesIsEvenAcrossBoundedGrid()
    {
        // Each adjacency is counted twice in the sum (once from each end),
        // so the total must be even. A cheap sanity check for correctness.
        HexGrid grid = BuildRectangularGrid(18, 13);

        int sum = grid.Tiles.Sum(t => grid.NeighborsOf(t.Coord).Count());

        Assert.Equal(0, sum % 2);
    }
}
