using Godot;
using Xunit;

namespace FourExHex.Tests;

public class WinConditionRulesTests
{
    private static readonly Color Red = new Color(1f, 0f, 0f);
    private static readonly Color Blue = new Color(0f, 0f, 1f);
    private static readonly Color Green = new Color(0f, 1f, 0f);

    // --- Winner ----------------------------------------------------------

    [Fact]
    public void Winner_MultiplePlayersHaveTiles_ReturnsNull()
    {
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Add(new HexTile(new HexCoord(1, 0), Blue));

        Assert.Null(WinConditionRules.Winner(grid));
    }

    [Fact]
    public void Winner_OneColorOwnsEveryTile_ReturnsThatColor()
    {
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Add(new HexTile(new HexCoord(1, 0), Red));
        grid.Add(new HexTile(new HexCoord(2, 0), Red));

        Assert.Equal(Red, WinConditionRules.Winner(grid));
    }

    [Fact]
    public void Winner_EmptyGrid_ReturnsNull()
    {
        var grid = new HexGrid();

        Assert.Null(WinConditionRules.Winner(grid));
    }

    [Fact]
    public void Winner_ThreePlayersAllPresent_ReturnsNull()
    {
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Add(new HexTile(new HexCoord(1, 0), Blue));
        grid.Add(new HexTile(new HexCoord(2, 0), Green));

        Assert.Null(WinConditionRules.Winner(grid));
    }

    // --- IsEliminated ----------------------------------------------------

    [Fact]
    public void IsEliminated_PlayerWithZeroTiles_True()
    {
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Add(new HexTile(new HexCoord(1, 0), Red));

        Assert.True(WinConditionRules.IsEliminated(Blue, grid));
    }

    [Fact]
    public void IsEliminated_PlayerWithOneTile_False()
    {
        // A singleton territory (1 hex, no capital, no income) still
        // keeps the player alive. They can only lose by having that
        // tile captured.
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Add(new HexTile(new HexCoord(1, 0), Red));
        grid.Add(new HexTile(new HexCoord(5, 5), Blue)); // lone Blue hex

        Assert.False(WinConditionRules.IsEliminated(Blue, grid));
    }

    [Fact]
    public void IsEliminated_EmptyGrid_True()
    {
        var grid = new HexGrid();

        Assert.True(WinConditionRules.IsEliminated(Red, grid));
    }
}
