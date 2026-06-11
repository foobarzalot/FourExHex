using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

public class AiStateScorerTests
{
    private static readonly PlayerId Red = PlayerId.FromIndex(0);
    private static readonly PlayerId Blue = PlayerId.FromIndex(1);

    private static GameState BuildState(Difficulty redDifficulty)
    {
        // An all-Red 12-tile board → one Red territory. Blue exists only
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
    public void Score_IncomeIsFlat_UnitlessBoardScoresEquallyAtEveryDifficulty()
    {
        // Earn rate is flat 1× everywhere and difficulty only touches unit
        // upkeep, so a board with no units scores identically at every level.
        int recruit = AiStateScorer.Score(BuildState(Difficulty.Recruit), Red);
        int soldier = AiStateScorer.Score(BuildState(Difficulty.Soldier), Red);
        int commander = AiStateScorer.Score(BuildState(Difficulty.Commander), Red);

        Assert.Equal(soldier, recruit);
        Assert.Equal(soldier, commander);
    }

    [Fact]
    public void Score_UpkeepHandicap_LowersScoreAsDifficultyRises()
    {
        // Same 12-tile board with a Soldier unit (base upkeep 6), zero gold.
        // Net income per difficulty (income flat 12):
        //   Recruit:   12 − 4 = 8   (cheaper-than-baseline easy mode)
        //   Soldier:   12 − 6 = 6   (baseline)
        //   Commander: 12 − 9 = 3   (1.5× handicap)
        // All solvent, so the score differs purely via recurring net income
        // and must fall strictly as difficulty rises.
        int ScoreFor(Difficulty d)
        {
            GameState state = BuildState(d);
            state.Grid.Get(HexCoord.FromOffset(1, 1))!.Occupant =
                new Unit(Red, UnitLevel.Soldier);
            return AiStateScorer.Score(state, Red);
        }

        int recruit = ScoreFor(Difficulty.Recruit);
        int soldier = ScoreFor(Difficulty.Soldier);
        int commander = ScoreFor(Difficulty.Commander);

        Assert.True(recruit > soldier, $"expected recruit {recruit} > soldier {soldier}");
        Assert.True(soldier > commander, $"expected soldier {soldier} > commander {commander}");
    }
}
