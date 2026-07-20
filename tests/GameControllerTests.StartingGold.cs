// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

public partial class GameControllerTests
{
    // --- Starting-gold seeding -------------------------------------------

    /// <summary>
    /// Build a two-player game where Red owns a single contiguous strip of
    /// <paramref name="redTiles"/> tree-free tiles (earning cells == tile
    /// count) and Blue owns one filler row below, then StartGame so the
    /// treasuries are seeded.
    /// </summary>
    private static (GameState State, Territory RedTerritory) BuildSeededGame(int redTiles)
    {
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        var players = new List<Player> { red, blue };

        var grid = TestHelpers.BuildRectGrid(redTiles, 2, blue.Id);
        for (int col = 0; col < redTiles; col++)
        {
            grid.Get(HexCoord.FromOffset(col, 0))!.Owner = red.Id;
        }

        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var controller = new GameController(
            state, new SessionState(), new MockHexMapView(), new MockHudView());
        controller.StartGame();

        Territory redTerritory = Assert.Single(state.Territories, t => t.Owner == red.Id);
        return (state, redTerritory);
    }

    [Theory]
    [InlineData(4, 20)]   // small territory: 5 gold per earning cell, uncapped
    [InlineData(10, 50)]  // exactly at the cap boundary: 10 × 5 = 50
    public void StartGame_SeedsSmallTerritoryAtFiveGoldPerEarningCell(int redTiles, int expectedGold)
    {
        (GameState state, Territory red) = BuildSeededGame(redTiles);
        Assert.Equal(expectedGold, state.Treasury.GetGold(red.Capital!.Value));
    }

    [Theory]
    [InlineData(11)]
    [InlineData(27)]
    public void StartGame_CapsLargeTerritorySeedAtFifty(int redTiles)
    {
        // A big starting region no longer buys an outsized bankroll: the
        // seed clamps at 50 regardless of earning-cell count.
        (GameState state, Territory red) = BuildSeededGame(redTiles);
        Assert.Equal(50, state.Treasury.GetGold(red.Capital!.Value));
    }
}
