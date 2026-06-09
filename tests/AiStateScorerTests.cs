using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

public class AiStateScorerTests
{
    private static readonly PlayerId Red = PlayerId.FromIndex(0);
    private static readonly PlayerId Blue = PlayerId.FromIndex(1);

    private static GameState BuildState(int redEarnMultiplier)
    {
        // A small all-Red board → one Red territory with positive net income
        // (empty tiles, no upkeep). Blue exists only so the roster has two
        // index-ordered slots for the owner lookup.
        HexGrid grid = TestHelpers.BuildRectGrid(3, 2, Red);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var players = new List<Player>
        {
            new Player("Red", Red, PlayerKind.Computer, earnMultiplier: redEarnMultiplier),
            new Player("Blue", Blue, PlayerKind.Computer),
        };
        return new GameState(grid, territories, players, new TurnState(players), new Treasury());
    }

    [Fact]
    public void Score_HigherOwnerEarnMultiplier_RaisesOwnTerritoryScore()
    {
        int baseScore = AiStateScorer.Score(BuildState(1), Red);
        int boostedScore = AiStateScorer.Score(BuildState(3), Red);

        Assert.True(boostedScore > baseScore,
            $"expected boosted score {boostedScore} > base {baseScore} " +
            "because the earn multiplier scales net income");
    }
}
