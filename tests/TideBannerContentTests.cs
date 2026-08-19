// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// The tide-banner copy rules: "Tide level L — rising in N turns" at each
/// Rising Tides human turn start, where L is the round's submerge budget
/// (<see cref="RisingTidesRules.SubmergeBudgetForRound"/>) and N counts
/// rounds to the next level increment (singular "turn" at N==1). Null
/// outside Rising Tides.
/// </summary>
public class TideBannerContentTests
{
    private static readonly PlayerId Red = PlayerId.FromIndex(0);

    private static GameState MakeState(GameMode mode = GameMode.RisingTides, int turnNumber = 1)
    {
        var players = new List<Player> { new Player("Red", Red) };
        HexGrid grid = TestHelpers.BuildRectGrid(3, 3, Red);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        return new GameState(
            grid, territories, players, new TurnState(players, 0, turnNumber), new Treasury(),
            waterCoords: null, mode: mode);
    }

    [Fact]
    public void For_NullOutsideRisingTides()
    {
        Assert.Null(TideBannerContent.For(MakeState(GameMode.Freeform)));
        Assert.Null(TideBannerContent.For(MakeState(GameMode.VikingRaiders)));
    }

    [Fact]
    public void For_FirstRound_LevelOneCountingDownFullInterval()
    {
        Assert.Equal(
            "Tide level 1 — rising in 6 turns",
            TideBannerContent.For(MakeState(turnNumber: 1)));
    }

    [Fact]
    public void For_LastRoundOfLevel_SingularTurn()
    {
        Assert.Equal(
            "Tide level 1 — rising in 1 turn",
            TideBannerContent.For(MakeState(turnNumber: 6)));
    }

    [Fact]
    public void For_AfterIncrement_LevelTwoRestartsCountdown()
    {
        Assert.Equal(
            "Tide level 2 — rising in 6 turns",
            TideBannerContent.For(MakeState(turnNumber: 7)));
    }
}
