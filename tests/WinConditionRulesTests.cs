using System.Collections.Generic;
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

    [Fact]
    public void Winner_OnlyOneCapitalBearingPlayer_ReturnsThatColor()
    {
        // Red has a 2-tile territory with a capital. Blue has a
        // lone orphan tile (singleton, no territory, no capital).
        // The orphan is dead weight — Red should be declared
        // winner without having to physically scrub the map.
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Add(new HexTile(new HexCoord(1, 0), Red));
        grid.Add(new HexTile(new HexCoord(5, 5), Blue));
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);

        Assert.Equal(Red, WinConditionRules.Winner(grid, territories));
    }

    [Fact]
    public void Winner_TwoCapitalBearingPlayers_ReturnsNull()
    {
        // Both Red and Blue have multi-hex territories with
        // capitals → game continues.
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Add(new HexTile(new HexCoord(1, 0), Red));
        grid.Add(new HexTile(new HexCoord(5, 5), Blue));
        grid.Add(new HexTile(new HexCoord(6, 5), Blue));
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);

        Assert.Null(WinConditionRules.Winner(grid, territories));
    }

    [Fact]
    public void Winner_LargerLeader_DoesNotWin()
    {
        // Red has 12 capital-bearing tiles, Blue has 4. As long as
        // Blue still holds a capital-bearing territory the game
        // continues — size advantage alone does not end the game.
        var grid = new HexGrid();
        for (int col = 0; col < 12; col++)
            grid.Add(new HexTile(new HexCoord(col, 0), Red));
        for (int col = 0; col < 4; col++)
            grid.Add(new HexTile(new HexCoord(col, 5), Blue));
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);

        Assert.Null(WinConditionRules.Winner(grid, territories));
    }

    [Fact]
    public void Winner_NoCapitalBearingPlayer_ReturnsNull()
    {
        // Edge case: all remaining territories are singletons.
        // No one has won (no one has a capital). Game keeps
        // running until somebody consolidates.
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Add(new HexTile(new HexCoord(5, 5), Blue));
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);

        Assert.Null(WinConditionRules.Winner(grid, territories));
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
