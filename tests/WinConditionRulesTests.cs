using System.Collections.Generic;
using Godot;
using Xunit;

namespace FourExHex.Tests;

public class WinConditionRulesTests
{
    private static readonly Color Red = new Color(1f, 0f, 0f);
    private static readonly Color Blue = new Color(0f, 0f, 1f);
    private static readonly Color Green = new Color(0f, 1f, 0f);

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
    public void IsEliminated_PlayerWithOneTile_True()
    {
        // A singleton (1 hex, no capital) is functionally dead: no
        // income, no purchases, no upkeep, nothing the AI or player
        // can do. Treat them as eliminated for rotation purposes —
        // they don't get a phantom turn.
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Add(new HexTile(new HexCoord(1, 0), Red));
        grid.Add(new HexTile(new HexCoord(5, 5), Blue)); // lone Blue hex
        TestHelpers.BuildTerritoriesFromGrid(grid); // place capitals

        Assert.True(WinConditionRules.IsEliminated(Blue, grid));
    }

    [Fact]
    public void IsEliminated_PlayerWithOnlySingletonTiles_True()
    {
        // Multiple scattered singletons are still all capital-less,
        // so the player has nothing to act on.
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Add(new HexTile(new HexCoord(1, 0), Red));
        grid.Add(new HexTile(new HexCoord(5, 5), Blue));
        grid.Add(new HexTile(new HexCoord(8, 8), Blue));
        TestHelpers.BuildTerritoriesFromGrid(grid);

        Assert.True(WinConditionRules.IsEliminated(Blue, grid));
    }

    [Fact]
    public void IsEliminated_PlayerWithCapitalBearingTerritory_False()
    {
        // A 2-hex same-color cluster gets a capital from the
        // reconciler — the player is in the rotation.
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Add(new HexTile(new HexCoord(1, 0), Red));
        grid.Add(new HexTile(new HexCoord(5, 5), Blue));
        grid.Add(new HexTile(new HexCoord(6, 5), Blue));
        TestHelpers.BuildTerritoriesFromGrid(grid);

        Assert.False(WinConditionRules.IsEliminated(Blue, grid));
    }

    [Fact]
    public void IsEliminated_EmptyGrid_True()
    {
        var grid = new HexGrid();

        Assert.True(WinConditionRules.IsEliminated(Red, grid));
    }

    // --- WinnerByDomination (mid-turn check) -----------------------------

    [Fact]
    public void WinnerByDomination_OneColorOwnsEveryTile_ReturnsThatColor()
    {
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Add(new HexTile(new HexCoord(1, 0), Red));
        grid.Add(new HexTile(new HexCoord(2, 0), Red));

        Assert.Equal(Red, WinConditionRules.WinnerByDomination(grid));
    }

    [Fact]
    public void WinnerByDomination_OpponentHasOrphanSingleton_ReturnsNull()
    {
        // Mid-turn check is strict: any non-current-color tile,
        // even an orphan singleton with no capital, blocks the
        // domination check. The end-of-turn check handles the
        // "opponent reduced to singletons" win case.
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Add(new HexTile(new HexCoord(1, 0), Red));
        grid.Add(new HexTile(new HexCoord(5, 5), Blue));

        Assert.Null(WinConditionRules.WinnerByDomination(grid));
    }

    [Fact]
    public void WinnerByDomination_EmptyGrid_ReturnsNull()
    {
        var grid = new HexGrid();

        Assert.Null(WinConditionRules.WinnerByDomination(grid));
    }

    // --- WinnerAtEndOfTurn (end-of-turn check) ---------------------------

    [Fact]
    public void WinnerAtEndOfTurn_OnlyCurrentPlayerHasCapitalBearingTerritory_ReturnsCurrent()
    {
        // Red has a 2-tile capital-bearing territory. Blue has a
        // singleton orphan (no capital). At end of Red's turn, no
        // OTHER player has a capital-bearing territory → Red wins.
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Add(new HexTile(new HexCoord(1, 0), Red));
        grid.Add(new HexTile(new HexCoord(5, 5), Blue));
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);

        Assert.Equal(Red, WinConditionRules.WinnerAtEndOfTurn(Red, territories));
    }

    [Fact]
    public void WinnerAtEndOfTurn_OpponentStillHasCapitalBearingTerritory_ReturnsNull()
    {
        // Both Red and Blue have multi-hex territories with capitals
        // → game continues even if it's end of Red's turn.
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Add(new HexTile(new HexCoord(1, 0), Red));
        grid.Add(new HexTile(new HexCoord(5, 5), Blue));
        grid.Add(new HexTile(new HexCoord(6, 5), Blue));
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);

        Assert.Null(WinConditionRules.WinnerAtEndOfTurn(Red, territories));
    }

    [Fact]
    public void WinnerAtEndOfTurn_CurrentPlayerHasNoCapital_ReturnsNull()
    {
        // Edge case: end-of-turn for a player who only has a
        // singleton orphan. They can't win — declaring them
        // the winner would be nonsensical.
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Add(new HexTile(new HexCoord(5, 5), Blue));
        grid.Add(new HexTile(new HexCoord(6, 5), Blue));
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);

        Assert.Null(WinConditionRules.WinnerAtEndOfTurn(Red, territories));
    }

    [Fact]
    public void WinnerAtEndOfTurn_AllPlayersHaveOnlySingletons_ReturnsNull()
    {
        // No capital-bearing territories anywhere. Nobody wins —
        // game continues until somebody consolidates.
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Add(new HexTile(new HexCoord(5, 5), Blue));
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);

        Assert.Null(WinConditionRules.WinnerAtEndOfTurn(Red, territories));
    }

    [Fact]
    public void WinnerAtEndOfTurn_ThreePlayersOneEliminated_ReturnsNull()
    {
        // Red and Green still have territories; Blue is gone.
        // Game continues — Red doesn't win just because Blue is out.
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Add(new HexTile(new HexCoord(1, 0), Red));
        grid.Add(new HexTile(new HexCoord(5, 5), Green));
        grid.Add(new HexTile(new HexCoord(6, 5), Green));
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);

        Assert.Null(WinConditionRules.WinnerAtEndOfTurn(Red, territories));
    }
}
