using System.Linq;
using Godot;
using Xunit;

namespace FourExHex.Tests;

public class HexGridTests
{
    private static HexTile MakeTile(int q, int r) =>
        new HexTile(new HexCoord(q, r), new Color(1f, 1f, 1f));

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
    public void NeighborsOf_InteriorTile_ReturnsAllSix()
    {
        var grid = new HexGrid();
        var center = new HexCoord(0, 0);
        grid.Add(MakeTile(0, 0));
        foreach (HexCoord n in center.Neighbors())
        {
            grid.Add(new HexTile(n, new Color()));
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
}
