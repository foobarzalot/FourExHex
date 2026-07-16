// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

public class WinConditionRulesTests
{
    private static readonly PlayerId Red = PlayerId.FromIndex(0);
    private static readonly PlayerId Blue = PlayerId.FromIndex(1);
    private static readonly PlayerId Green = PlayerId.FromIndex(2);

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

    // --- LastPlayerStanding (Rising Tides win check) ---------------------

    [Fact]
    public void LastPlayerStanding_OnlyOneOwnerHasCapital_ReturnsThatOwner()
    {
        // Red has a 2-tile capital-bearing territory; Blue is down to a
        // singleton orphan. Unlike WinnerAtEndOfTurn this doesn't care whose
        // turn it is — Red is the sole survivor.
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Add(new HexTile(new HexCoord(1, 0), Red));
        grid.Add(new HexTile(new HexCoord(5, 5), Blue));
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);

        Assert.Equal(Red, WinConditionRules.LastPlayerStanding(territories));
    }

    [Fact]
    public void LastPlayerStanding_TwoOwnersHaveCapitals_ReturnsNull()
    {
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Add(new HexTile(new HexCoord(1, 0), Red));
        grid.Add(new HexTile(new HexCoord(5, 5), Blue));
        grid.Add(new HexTile(new HexCoord(6, 5), Blue));
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);

        Assert.Null(WinConditionRules.LastPlayerStanding(territories));
    }

    [Fact]
    public void LastPlayerStanding_NobodyHasCapital_ReturnsNull()
    {
        // Degenerate all-singletons state — no winner declared.
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Add(new HexTile(new HexCoord(5, 5), Blue));
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);

        Assert.Null(WinConditionRules.LastPlayerStanding(territories));
    }

    // --- NextClaimVictoryThreshold (highest-unseen helper) ---------------

    /// <summary>
    /// Build a grid with the given count of Red tiles out of 100 total
    /// (rest Blue), so percentages map cleanly: redCount = ownership %.
    /// </summary>
    private static HexGrid Build100TileGrid(int redCount)
    {
        var grid = new HexGrid();
        for (int i = 0; i < 100; i++)
        {
            PlayerId c = i < redCount ? Red : Blue;
            grid.Add(new HexTile(new HexCoord(i, 0), c));
        }
        return grid;
    }

    [Fact]
    public void NextClaimVictoryThreshold_BelowAllTiers_ReturnsNull()
    {
        HexGrid grid = Build100TileGrid(40);
        Assert.Null(WinConditionRules.NextClaimVictoryThreshold(Red, grid, 0));
    }

    [Fact]
    public void NextClaimVictoryThreshold_AtSixty_NoPriors_Returns50()
    {
        HexGrid grid = Build100TileGrid(60);
        Assert.Equal(50, WinConditionRules.NextClaimVictoryThreshold(Red, grid, 0));
    }

    [Fact]
    public void NextClaimVictoryThreshold_AtEighty_NoPriors_Returns75()
    {
        // Show only highest unseen — jumping past 50% lands on 75%.
        HexGrid grid = Build100TileGrid(80);
        Assert.Equal(75, WinConditionRules.NextClaimVictoryThreshold(Red, grid, 0));
    }

    [Fact]
    public void NextClaimVictoryThreshold_AtNinetyFive_NoPriors_Returns90()
    {
        HexGrid grid = Build100TileGrid(95);
        Assert.Equal(90, WinConditionRules.NextClaimVictoryThreshold(Red, grid, 0));
    }

    [Fact]
    public void NextClaimVictoryThreshold_AtSixty_Prior50_ReturnsNull()
    {
        // Already prompted at 50; doesn't meet 75 or 90.
        HexGrid grid = Build100TileGrid(60);
        Assert.Null(WinConditionRules.NextClaimVictoryThreshold(Red, grid, 50));
    }

    [Fact]
    public void NextClaimVictoryThreshold_AtEighty_Prior50_Returns75()
    {
        HexGrid grid = Build100TileGrid(80);
        Assert.Equal(75, WinConditionRules.NextClaimVictoryThreshold(Red, grid, 50));
    }

    [Fact]
    public void NextClaimVictoryThreshold_AtNinetyFive_Prior75_Returns90()
    {
        HexGrid grid = Build100TileGrid(95);
        Assert.Equal(90, WinConditionRules.NextClaimVictoryThreshold(Red, grid, 75));
    }

    [Fact]
    public void NextClaimVictoryThreshold_AtNinetyFive_Prior90_ReturnsNull()
    {
        // All three tiers seen, no further prompts.
        HexGrid grid = Build100TileGrid(95);
        Assert.Null(WinConditionRules.NextClaimVictoryThreshold(Red, grid, 90));
    }

    [Fact]
    public void NextClaimVictoryThreshold_CountsOnlyCurrentGridTiles_SunkTilesExcluded()
    {
        // Rising Tides sinks tiles by REMOVING them from
        // the grid, so the claim-victory denominator is automatically the count
        // of active (non-sunk) tiles. Red owns 4 of 8 (exactly 50% — not >50%,
        // so no tier). After a Blue tile sinks (is removed), Red owns 4 of 7
        // (>50%), tripping the 50% tier — proving the percentage tracks the
        // remaining-tile count, not the original board size.
        var grid = new HexGrid();
        for (int q = 0; q < 4; q++) grid.Add(new HexTile(new HexCoord(q, 0), Red));
        for (int q = 0; q < 4; q++) grid.Add(new HexTile(new HexCoord(q, 2), Blue));
        Assert.Null(WinConditionRules.NextClaimVictoryThreshold(Red, grid, 0));

        grid.Remove(new HexCoord(0, 2)); // a Blue tile sinks

        Assert.Equal(50, WinConditionRules.NextClaimVictoryThreshold(Red, grid, 0));
    }
}
