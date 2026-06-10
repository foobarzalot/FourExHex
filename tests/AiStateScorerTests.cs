using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

public class AiStateScorerTests
{
    private static readonly PlayerId Red = PlayerId.FromIndex(0);
    private static readonly PlayerId Blue = PlayerId.FromIndex(1);

    private static GameState BuildState(Difficulty redDifficulty)
    {
        // An all-Red board → one Red territory with positive net income
        // (empty tiles, no upkeep). 12 tiles so the percent-based difficulty
        // bonuses survive integer truncation and every level's income
        // differs (Recruit 6, Soldier 12, Captain 13, Commander 15). Blue exists only
        // so the roster has two index-ordered slots for the owner lookup.
        HexGrid grid = TestHelpers.BuildRectGrid(4, 3, Red);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var players = new List<Player>
        {
            new Player("Red", Red, PlayerKind.Computer, redDifficulty),
            new Player("Blue", Blue, PlayerKind.Computer),
        };
        return new GameState(grid, territories, players, new TurnState(players), new Treasury());
    }

    [Fact]
    public void Score_RisesWithOwnerDifficulty()
    {
        int recruit = AiStateScorer.Score(BuildState(Difficulty.Recruit), Red);
        int soldier = AiStateScorer.Score(BuildState(Difficulty.Soldier), Red);
        int captain = AiStateScorer.Score(BuildState(Difficulty.Captain), Red);
        int commander = AiStateScorer.Score(BuildState(Difficulty.Commander), Red);

        Assert.True(soldier > recruit, $"expected soldier {soldier} > recruit {recruit}");
        Assert.True(captain > soldier, $"expected captain {captain} > soldier {soldier}");
        Assert.True(commander > captain, $"expected commander {commander} > captain {captain}");
    }
}
