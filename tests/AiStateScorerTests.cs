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
        // differs (Easy 6, Normal 12, Hard 13, Brutal 15). Blue exists only
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
        int easy = AiStateScorer.Score(BuildState(Difficulty.Easy), Red);
        int normal = AiStateScorer.Score(BuildState(Difficulty.Normal), Red);
        int hard = AiStateScorer.Score(BuildState(Difficulty.Hard), Red);
        int brutal = AiStateScorer.Score(BuildState(Difficulty.Brutal), Red);

        Assert.True(normal > easy, $"expected normal {normal} > easy {easy}");
        Assert.True(hard > normal, $"expected hard {hard} > normal {normal}");
        Assert.True(brutal > hard, $"expected brutal {brutal} > hard {hard}");
    }
}
