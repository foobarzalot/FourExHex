// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

public class AiStateScorerTests
{
    private static readonly PlayerId Red = PlayerId.FromIndex(0);
    private static readonly PlayerId Blue = PlayerId.FromIndex(1);

    private static GameState BuildState()
    {
        // An all-Red 12-tile board → one Red territory. Blue exists only
        // so the roster has two index-ordered slots for the owner lookup.
        HexGrid grid = TestHelpers.BuildRectGrid(4, 3, Red);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var players = new List<Player>
        {
            new Player("Red", Red, PlayerKind.Computer),
            new Player("Blue", Blue, PlayerKind.Computer),
        };
        return new GameState(grid, territories, players, new TurnState(players), new Treasury());
    }

    [Fact]
    public void Score_GoldTile_RaisesScoreViaIncome()
    {
        // Same board scored with and without a single gold tile. The gold
        // bonus flows through IncomeRules.IncomeFor into the net-income term,
        // so the AI values the gold board strictly higher.
        GameState plain = BuildState();

        GameState gilded = BuildState();
        gilded.Grid.Get(HexCoord.FromOffset(1, 1))!.IsGold = true;

        int plainScore = AiStateScorer.Score(plain, Red);
        int gildedScore = AiStateScorer.Score(gilded, Red);

        Assert.True(gildedScore > plainScore,
            $"expected gold {gildedScore} > plain {plainScore}");
    }

    // --- Gold standing premium -------------------------------------------
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
        GameState plain = BuildState();
        GameState gilded = BuildState();
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
            GameState s = BuildState();
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

    // --- Standing contested-border defense magnitude ---------------------
    // A one-sided term in Score(): each own tile bordering an enemy adds
    // ContestedDefenseWeight × min(Defense, ContestedDefenseCap). It values
    // *holding* a strong defensive position (so a defender stays put rather
    // than chasing a per-action arrival bonus), and mountains win because
    // DefenseRules.Defense bakes in the +1 high-ground. All tests below vary
    // exactly one flag on a fixed board so economy/unit-value cancel and the
    // delta is purely the defense term.

    // Build a Red strip (row 0) over a Blue field with a unit on a Red
    // contested-border tile (1,0); capital lands on (0,0) (smallest empty).
    private static int ScoreWithDefender(UnitLevel level, bool mountain)
    {
        HexGrid grid = TestHelpers.BuildRectGrid(4, 3, Blue);
        for (int col = 0; col < 4; col++)
            grid.Get(HexCoord.FromOffset(col, 0))!.Owner = Red;
        HexTile tile = grid.Get(HexCoord.FromOffset(1, 0))!;
        tile.Occupant = new Unit(Red, level);
        tile.IsMountain = mountain;
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var players = new List<Player>
        {
            new Player("Red", Red, PlayerKind.Computer, Difficulty.Soldier),
            new Player("Blue", Blue, PlayerKind.Computer),
        };
        return AiStateScorer.Score(
            new GameState(grid, territories, players, new TurnState(players), new Treasury()), Red);
    }

    [Fact]
    public void Score_MountainUnderContestedDefender_RaisesScoreViaDefenseMagnitude()
    {
        // The only difference is the mountain flag under the same Soldier on
        // the same contested-border tile: defense 2 → 3 raises the score.
        Assert.True(ScoreWithDefender(UnitLevel.Soldier, mountain: true)
                  > ScoreWithDefender(UnitLevel.Soldier, mountain: false));
    }

    [Fact]
    public void Score_DefenseCap_ClampsMountainBonusForHighLevelDefender()
    {
        // Soldier: mountain lifts defense 2 → 3 (under cap 3) → score rises.
        // Captain: 3 → 4 but clamped to 3 → no change. Demonstrates the cap
        // stops over-strong defenders from reading ever-higher.
        int soldierGain = ScoreWithDefender(UnitLevel.Soldier, true)
                        - ScoreWithDefender(UnitLevel.Soldier, false);
        int captainGain = ScoreWithDefender(UnitLevel.Captain, true)
                        - ScoreWithDefender(UnitLevel.Captain, false);

        Assert.True(soldierGain > 0, $"expected soldier gain {soldierGain} > 0");
        Assert.Equal(0, captainGain);
    }

    [Fact]
    public void Score_LateralRepositionBetweenCoveredBorders_LeavesScoreUnchanged()
    {
        // The anti-shuffle guard: a standing term values the *state*, so
        // sliding the lone defender between two mutually-covering border
        // tiles changes nothing — there's no arrival bonus to chase, so the
        // AI won't pointlessly reposition an already-well-placed unit.
        // Red rows 0-2 over Blue row 3; the four row-2 tiles are the borders.
        int ScoreWithUnitAt(HexCoord unitTile)
        {
            HexGrid grid = TestHelpers.BuildRectGrid(4, 4, Blue);
            for (int r = 0; r < 3; r++)
                for (int col = 0; col < 4; col++)
                    grid.Get(HexCoord.FromOffset(col, r))!.Owner = Red;
            grid.Get(unitTile)!.Occupant = new Unit(Red, UnitLevel.Soldier);
            IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
            var players = new List<Player>
            {
                new Player("Red", Red, PlayerKind.Computer, Difficulty.Soldier),
                new Player("Blue", Blue, PlayerKind.Computer),
            };
            return AiStateScorer.Score(
                new GameState(grid, territories, players, new TurnState(players), new Treasury()), Red);
        }

        // (1,2) and (2,2) each cover three of the four border tiles, leaving
        // one undefended — symmetric, so both Score identically.
        Assert.Equal(ScoreWithUnitAt(HexCoord.FromOffset(1, 2)),
                     ScoreWithUnitAt(HexCoord.FromOffset(2, 2)));
    }

    [Fact]
    public void Score_SparseRoster_ScoresSlotFivesBoardWithoutThrowing()
    {
        // Compact roster with a gap: slots 0, 2, 5 present (1,3,4 are None).
        // The whole board belongs to the slot-5 player, whose position in
        // the 3-element roster is 2. Scoring must resolve owners by SLOT,
        // not by indexing the roster at slot 5 (which threw
        // IndexOutOfRange before the fix).
        PlayerId orange = PlayerId.FromIndex(5);
        HexGrid grid = TestHelpers.BuildRectGrid(4, 3, orange);
        grid.Get(HexCoord.FromOffset(1, 1))!.Occupant = new Unit(orange, UnitLevel.Soldier);
        IReadOnlyList<Territory> terr = TestHelpers.BuildTerritoriesFromGrid(grid);
        var players = new List<Player>
        {
            new Player("Red", PlayerId.FromIndex(0), PlayerKind.Human),
            new Player("Green", PlayerId.FromIndex(2), PlayerKind.Computer),
            new Player("Orange", orange, PlayerKind.Computer),
        };
        var state = new GameState(grid, terr, players, new TurnState(players), new Treasury());

        int score = AiStateScorer.Score(state, orange);

        // 12 owned tiles with positive net income must score positive.
        Assert.True(score > 0, $"expected sparse-roster score {score} > 0");
    }

    // --- Tower-removal credit (#198) ----------------------------------------

    /// <summary>
    /// 6x1 strip: (0,0)-(2,0) Blue (a 3-tile territory, so the reconciler
    /// gives it a real capital), (3,0)-(5,0) Red. Red's attacker sits at
    /// (3,0); (2,0) is the contested tile every case decorates.
    /// </summary>
    private static GameState BuildDestructionState()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(6, 1, Red);
        for (int col = 0; col <= 2; col++)
            grid.Get(HexCoord.FromOffset(col, 0))!.Owner = Blue;
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var players = new List<Player>
        {
            new Player("Red", Red, PlayerKind.Computer),
            new Player("Blue", Blue, PlayerKind.Computer),
        };
        return new GameState(grid, territories, players, new TurnState(players), new Treasury());
    }

    private static AiMoveAction CaptureOf(HexCoord target) =>
        new AiMoveAction(HexCoord.FromOffset(3, 0), target);

    [Fact]
    public void TowerRemovalCredit_EnemyTower_ReturnsTowerKillValue()
    {
        GameState state = BuildDestructionState();
        HexCoord target = HexCoord.FromOffset(2, 0);
        state.Grid.Get(target)!.Occupant = new Tower();

        Assert.Equal(
            GameSettings.TowerKillValueFor(Red),
            AiStateScorer.TowerRemovalCredit(CaptureOf(target), state, Red));
    }

    [Fact]
    public void TowerRemovalCredit_EnemyUnit_ReturnsZero()
    {
        // Deliberate: Score already credits a kill through the drop in the
        // victim territory's unit value. Crediting it again here would make
        // the AI overpay for kills, so units are worth nothing to this term.
        GameState state = BuildDestructionState();
        HexCoord target = HexCoord.FromOffset(2, 0);
        state.Grid.Get(target)!.Occupant = new Unit(Blue, UnitLevel.Commander);

        Assert.Equal(0, AiStateScorer.TowerRemovalCredit(CaptureOf(target), state, Red));
    }

    [Fact]
    public void TowerRemovalCredit_BuyOntoEnemyTower_ReturnsTowerKillValue()
    {
        // The buy-to-capture arm destroys exactly what the move arm does.
        GameState state = BuildDestructionState();
        HexCoord target = HexCoord.FromOffset(2, 0);
        state.Grid.Get(target)!.Occupant = new Tower();
        HexCoord cap = state.Territories.First(t => t.Owner == Red).Capital!.Value;

        Assert.Equal(
            GameSettings.TowerKillValueFor(Red),
            AiStateScorer.TowerRemovalCredit(
                new AiBuyUnitAction(cap, target, UnitLevel.Captain), state, Red));
    }

    [Fact]
    public void TowerRemovalCredit_NonDestructiveActions_ReturnZero()
    {
        GameState state = BuildDestructionState();
        HexCoord red = HexCoord.FromOffset(3, 0);
        HexCoord cap = state.Territories.First(t => t.Owner == Red).Capital!.Value;

        // An empty enemy tile — a capture that destroys no tower.
        Assert.Equal(0, AiStateScorer.TowerRemovalCredit(
            CaptureOf(HexCoord.FromOffset(2, 0)), state, Red));

        // An enemy CAPITAL is not a tower and earns nothing: capital
        // destruction was measured to cost win rate and is not credited.
        Assert.Equal(0, AiStateScorer.TowerRemovalCredit(
            CaptureOf(state.Territories.First(t => t.Owner == Blue).Capital!.Value),
            state, Red));

        // An own-territory tree chop is not a destruction…
        state.Grid.Get(HexCoord.FromOffset(5, 0))!.Occupant = new Tree();
        Assert.Equal(0, AiStateScorer.TowerRemovalCredit(
            new AiMoveAction(red, HexCoord.FromOffset(5, 0)), state, Red));

        // …nor is a tower build or a buy-combine (both target own tiles)…
        Assert.Equal(0, AiStateScorer.TowerRemovalCredit(
            new AiBuildTowerAction(cap, HexCoord.FromOffset(5, 0)), state, Red));
        Assert.Equal(0, AiStateScorer.TowerRemovalCredit(
            new AiBuyCombineAction(cap, red, UnitLevel.Recruit), state, Red));

        // …and an off-map destination is inert.
        Assert.Equal(0, AiStateScorer.TowerRemovalCredit(
            new AiMoveAction(red, HexCoord.FromOffset(99, 99)), state, Red));
    }

    [Fact]
    public void TowerRemovalCredit_IsPerPlayer()
    {
        // Asymmetric credits are how a sweep measures whether the credit
        // makes an AI stronger: give it to some slots, withhold it from
        // others, and compare win rates. So the value is read per actor,
        // not process-wide.
        int[] towers = (int[])GameSettings.TowerKillValues.Clone();
        try
        {
            GameSettings.TowerKillValues[Red.Index] = 40;
            GameSettings.TowerKillValues[Blue.Index] = 0;

            // Red's territory faces Blue's; give Blue an attacker of its own
            // so the same tile can be evaluated from both sides.
            GameState state = BuildDestructionState();
            HexCoord blueTile = HexCoord.FromOffset(2, 0);
            HexCoord redTile = HexCoord.FromOffset(3, 0);
            state.Grid.Get(blueTile)!.Occupant = new Tower();
            state.Grid.Get(redTile)!.Occupant = new Tower();

            // Red capturing Blue's tower earns Red's value…
            Assert.Equal(40, AiStateScorer.TowerRemovalCredit(
                new AiMoveAction(redTile, blueTile), state, Red));
            // …and Blue capturing Red's tower earns Blue's (zero).
            Assert.Equal(0, AiStateScorer.TowerRemovalCredit(
                new AiMoveAction(blueTile, redTile), state, Blue));
        }
        finally
        {
            GameSettings.TowerKillValues = towers;
        }
    }

    [Fact]
    public void TowerRemovalCredit_NeutralActorUsesDefaults()
    {
        // Vikings (PlayerId.None) have no slot, so they fall back to the
        // defaults rather than indexing off the end of the array.
        GameState state = BuildDestructionState();
        HexCoord target = HexCoord.FromOffset(2, 0);
        state.Grid.Get(target)!.Occupant = new Tower();

        Assert.Equal(
            GameSettings.DefaultTowerKillValue,
            AiStateScorer.TowerRemovalCredit(
                new AiMoveAction(HexCoord.FromOffset(3, 0), target), state, PlayerId.None));
    }

    [Fact]
    public void TowerRemovalCredit_HonorsGameSettingsOverride()
    {
        // The tuning knobs are what the sweep runs move, so the term must
        // read them rather than bake in its defaults.
        int[] tower = (int[])GameSettings.TowerKillValues.Clone();
        try
        {
            GameSettings.TowerKillValues[Red.Index] = 99;

            GameState state = BuildDestructionState();
            HexCoord cap = state.Territories.First(t => t.Owner == Blue).Capital!.Value;
            HexCoord towerTile = state.Territories.First(t => t.Owner == Blue)
                .Coords.First(c => !c.Equals(cap));
            state.Grid.Get(towerTile)!.Occupant = new Tower();

            Assert.Equal(99, AiStateScorer.TowerRemovalCredit(
                CaptureOf(towerTile), state, Red));
            Assert.Equal(0, AiStateScorer.TowerRemovalCredit(CaptureOf(cap), state, Red));
        }
        finally
        {
            GameSettings.TowerKillValues = tower;
        }
    }

    // --- Barbarian provoke penalty (#188) ---------------------------------

    /// <summary>
    /// 5x1 strip: (0,0)-(2,0) neutral holding a passive Recruit and
    /// Soldier, (3,0)-(4,0) Red.
    /// </summary>
    private static GameState BuildProvokeState(bool aggro = false)
    {
        HexGrid grid = TestHelpers.BuildRectGrid(5, 1, Red);
        for (int col = 0; col <= 2; col++)
            grid.Get(HexCoord.FromOffset(col, 0))!.Owner = PlayerId.None;
        grid.Get(HexCoord.FromOffset(0, 0))!.Occupant =
            new Unit(PlayerId.None, UnitLevel.Recruit) { IsAggro = aggro };
        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant =
            new Unit(PlayerId.None, UnitLevel.Soldier) { IsAggro = aggro };
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var players = new List<Player>
        {
            new Player("Red", Red, PlayerKind.Computer),
            new Player("Blue", Blue, PlayerKind.Computer),
        };
        return new GameState(grid, territories, players, new TurnState(players), new Treasury());
    }

    [Fact]
    public void BarbarianProvokePenalty_ChargesPerFlippedUnitValue()
    {
        // Capturing into the passive territory would flip a Recruit (4)
        // and a Soldier (12): penalty = BarbarianProvokeWeight (2) x 16.
        GameState state = BuildProvokeState();

        int penalty = AiStateScorer.BarbarianProvokePenalty(
            HexCoord.FromOffset(2, 0), state, Red);

        Assert.Equal(32, penalty);
    }

    [Fact]
    public void BarbarianProvokePenalty_ZeroWhenNotProvoking()
    {
        // An already-aggro territory costs nothing extra to attack…
        GameState aggro = BuildProvokeState(aggro: true);
        Assert.Equal(0, AiStateScorer.BarbarianProvokePenalty(
            HexCoord.FromOffset(2, 0), aggro, Red));

        GameState state = BuildProvokeState();
        // …nor does a player-owned destination…
        Assert.Equal(0, AiStateScorer.BarbarianProvokePenalty(
            HexCoord.FromOffset(4, 0), state, Red));
        // …and the neutral actor itself never pays it (its own tiles are
        // not captures for it in the first place).
        Assert.Equal(0, AiStateScorer.BarbarianProvokePenalty(
            HexCoord.FromOffset(2, 0), state, PlayerId.None));
    }
}
