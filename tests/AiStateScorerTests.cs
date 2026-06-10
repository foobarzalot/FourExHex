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
    public void Score_RecruitIncomeHandicap_LowersScore()
    {
        // Income is the only Recruit-difficulty lever: on a unit-less board
        // (no upkeep) a Recruit owner earns 50% (6 of 12 tiles) and must
        // score below a Soldier owner of the identical board.
        int recruit = AiStateScorer.Score(BuildState(Difficulty.Recruit), Red);
        int soldier = AiStateScorer.Score(BuildState(Difficulty.Soldier), Red);

        Assert.True(soldier > recruit, $"expected soldier {soldier} > recruit {recruit}");
    }

    [Fact]
    public void Score_UnitUpkeepRelief_RisesWithHardDifficulty()
    {
        // Same 12-tile board with a Captain unit (base upkeep 18) and 10
        // gold. Net income / solvency per difficulty (income flat 100%):
        //   Soldier:   12 − 18 = −6  → 10 + 5×(−6) < 0 → bankrupt, unit worth 0
        //   Captain:   12 − 13 = −1  → 10 + 5×(−1) ≥ 0 → solvent, unit counts
        //   Commander: 12 −  9 = +3  → solvent AND positive recurring income
        // So the score must rise strictly: Soldier < Captain < Commander —
        // entirely via the upkeep table.
        int ScoreFor(Difficulty d)
        {
            GameState state = BuildState(d);
            state.Grid.Get(HexCoord.FromOffset(1, 1))!.Occupant =
                new Unit(Red, UnitLevel.Captain);
            Territory red = state.Territories.First(t => t.Owner == Red);
            state.Treasury.SetGold(red.Capital!.Value, 10);
            return AiStateScorer.Score(state, Red);
        }

        int soldier = ScoreFor(Difficulty.Soldier);
        int captain = ScoreFor(Difficulty.Captain);
        int commander = ScoreFor(Difficulty.Commander);

        Assert.True(captain > soldier, $"expected captain {captain} > soldier {soldier}");
        Assert.True(commander > captain, $"expected commander {commander} > captain {captain}");
    }
}
