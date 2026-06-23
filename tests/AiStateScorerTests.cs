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

    [Fact]
    public void Score_GoldTile_RaisesScoreViaIncome()
    {
        // Same board scored with and without a single gold tile. The gold
        // bonus flows through IncomeRules.IncomeFor into the net-income term,
        // so the AI values the gold board strictly higher (issue #45).
        GameState plain = BuildState(Difficulty.Soldier);

        GameState gilded = BuildState(Difficulty.Soldier);
        gilded.Grid.Get(HexCoord.FromOffset(1, 1))!.IsGold = true;

        int plainScore = AiStateScorer.Score(plain, Red);
        int gildedScore = AiStateScorer.Score(gilded, Red);

        Assert.True(gildedScore > plainScore,
            $"expected gold {gildedScore} > plain {plainScore}");
    }

    // --- Gold standing premium (#61) -------------------------------------
    // A gold tile earns 5x an ordinary tile (1 + GoldTileBonus), so its
    // territorial worth is 5x TileWeight: the base TileWeight is
    // unconditional (every tile) and an extra TileWeight * GoldTileBonus
    // "earning premium" is added for gold tiles that actually produce.

    [Fact]
    public void Score_GoldPremium_AddsTileWeightTimesBonus_OnSolventBoard()
    {
        // Same all-Red board with vs without one gold tile. Beyond the
        // +GoldTileBonus income blip the existing test covers, the gold
        // tile must add the durable standing premium = TileWeight(10) *
        // GoldTileBonus on top, so the delta clears that floor.
        GameState plain = BuildState(Difficulty.Soldier);
        GameState gilded = BuildState(Difficulty.Soldier);
        gilded.Grid.Get(HexCoord.FromOffset(1, 1))!.IsGold = true;

        int delta = AiStateScorer.Score(gilded, Red) - AiStateScorer.Score(plain, Red);

        // 10 == TileWeight (private). Premium floor; income adds a bit more.
        Assert.True(delta >= 10 * IncomeRules.GoldTileBonus,
            $"expected gold delta {delta} >= premium {10 * IncomeRules.GoldTileBonus}");
    }

    [Fact]
    public void Score_GoldPremium_SurvivesBankruptcy()
    {
        // A bankrupt territory zeroes its unit value and clamps recurring
        // income to 0 — but the gold premium is durable (the tile keeps
        // its terrain), so it must still register. A 3-tile Red territory
        // with a Commander (huge upkeep) and an empty treasury is bankrupt;
        // adding one gold tile leaves it bankrupt, isolating the premium.
        GameState BuildBankrupt(bool gold)
        {
            var grid = new HexGrid();
            grid.Add(new HexTile(HexCoord.FromOffset(0, 0), Red));
            grid.Add(new HexTile(HexCoord.FromOffset(1, 0), Red));
            grid.Add(new HexTile(HexCoord.FromOffset(2, 0), Red));
            IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
            // Commander on a non-capital tile drives upkeep far past income.
            HexCoord[] all = { HexCoord.FromOffset(0, 0), HexCoord.FromOffset(1, 0), HexCoord.FromOffset(2, 0) };
            HexCoord capital = territories.First(t => t.Owner == Red).Capital!.Value;
            HexCoord unitTile = all.First(c => !c.Equals(capital));
            grid.Get(unitTile)!.Occupant = new Unit(Red, UnitLevel.Commander);
            if (gold)
            {
                HexCoord goldTile = all.First(c => !c.Equals(capital) && !c.Equals(unitTile));
                grid.Get(goldTile)!.IsGold = true;
            }
            var players = new List<Player>
            {
                new Player("Red", Red, PlayerKind.Computer, Difficulty.Soldier),
                new Player("Blue", Blue, PlayerKind.Computer),
            };
            // Empty treasury → no gold to pay upkeep → bankrupt.
            return new GameState(grid, territories, players, new TurnState(players), new Treasury());
        }

        int delta = AiStateScorer.Score(BuildBankrupt(true), Red)
                  - AiStateScorer.Score(BuildBankrupt(false), Red);

        // Pure premium: income is clamped and unit value zeroed under
        // bankruptcy, so the only difference is TileWeight * GoldTileBonus.
        Assert.Equal(10 * IncomeRules.GoldTileBonus, delta);
    }

    [Fact]
    public void Score_TreeBlockedGold_SuppressesPremium_AndClearingUnlocksIt()
    {
        int ScoreWith(bool gold, bool tree)
        {
            GameState s = BuildState(Difficulty.Soldier);
            HexTile tile = s.Grid.Get(HexCoord.FromOffset(1, 1))!;
            tile.IsGold = gold;
            if (tree) tile.Occupant = new Tree();
            return AiStateScorer.Score(s, Red);
        }

        // While a tree blocks the gold tile it earns nothing, so the
        // premium is suppressed: gold-under-tree reads identically to
        // plain-under-tree.
        Assert.Equal(ScoreWith(false, true), ScoreWith(true, true));

        // Unblocked, the gold premium is present.
        Assert.True(ScoreWith(true, false) > ScoreWith(false, false));

        // Therefore clearing the tree off a gold tile gains strictly more
        // than clearing it off a plain tile — gold-trees are the most
        // desirable chops.
        int goldChopGain = ScoreWith(true, false) - ScoreWith(true, true);
        int plainChopGain = ScoreWith(false, false) - ScoreWith(false, true);
        Assert.True(goldChopGain > plainChopGain,
            $"expected gold-chop gain {goldChopGain} > plain-chop gain {plainChopGain}");
    }

    [Fact]
    public void Score_OwningGold_BeatsEnemyOwningGold()
    {
        // 3-wide, 3-tall board: row 0 Red (3 tiles), rows 1-2 Blue (6).
        // The gold premium is two-sided (added for own, subtracted for
        // enemy), so Red scores higher when the gold tile is Red's than
        // when the identical tile belongs to Blue.
        GameState Build(bool goldOnRed)
        {
            var grid = TestHelpers.BuildRectGrid(3, 3, Blue);
            for (int col = 0; col < 3; col++)
                grid.Get(HexCoord.FromOffset(col, 0))!.Owner = Red;
            grid.Get(goldOnRed ? HexCoord.FromOffset(1, 0) : HexCoord.FromOffset(1, 1))!.IsGold = true;
            IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
            var players = new List<Player>
            {
                new Player("Red", Red, PlayerKind.Computer, Difficulty.Soldier),
                new Player("Blue", Blue, PlayerKind.Computer),
            };
            return new GameState(grid, territories, players, new TurnState(players), new Treasury());
        }

        int redOwns = AiStateScorer.Score(Build(true), Red);
        int blueOwns = AiStateScorer.Score(Build(false), Red);

        // Two-sided premium: +40 when ours, -40 when theirs ≈ 80 swing.
        Assert.True(redOwns - blueOwns >= 2 * 10 * IncomeRules.GoldTileBonus,
            $"expected swing {redOwns - blueOwns} >= {2 * 10 * IncomeRules.GoldTileBonus}");
    }
}
