using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

public class ComputerAiTests
{
    private static readonly PlayerId Red = PlayerId.FromIndex(0);
    private static readonly PlayerId Blue = PlayerId.FromIndex(1);

    private static GameState BuildState(HexGrid grid, params Player[] players)
    {
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var list = players.ToList();
        return new GameState(grid, territories, list, new TurnState(list), new Treasury());
    }

    // --- AiSimulator.Clone -------------------------------------------------

    [Fact]
    public void Clone_MutatingCloneDoesNotAffectOriginal()
    {
        // 3-tile Red island with a recruit on (1,0). Clone, then
        // remove the recruit from the clone's tile and verify the
        // original still has it.
        var grid = new HexGrid();
        grid.Add(new HexTile(HexCoord.FromOffset(0, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(1, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(2, 0), Red));
        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = new Unit(Red);
        GameState state = BuildState(grid, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));

        GameState clone = AiSimulator.Clone(state);
        clone.Grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = null;

        Assert.NotNull(state.Grid.Get(HexCoord.FromOffset(1, 0))!.Unit);
        Assert.Null(clone.Grid.Get(HexCoord.FromOffset(1, 0))!.Unit);
    }

    [Fact]
    public void Clone_TreasuryIsIndependent()
    {
        var grid = new HexGrid();
        grid.Add(new HexTile(HexCoord.FromOffset(0, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(1, 0), Red));
        GameState state = BuildState(grid, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));
        HexCoord cap = state.Territories.First(t => t.Owner == Red).Capital!.Value;
        state.Treasury.SetGold(cap, 42);

        GameState clone = AiSimulator.Clone(state);
        clone.Treasury.SetGold(cap, 0);

        Assert.Equal(42, state.Treasury.GetGold(cap));
        Assert.Equal(0, clone.Treasury.GetGold(cap));
    }

    // --- AiStateScorer: merges vs fragmentation ---------------------------

    [Fact]
    public void Score_PrefersMergedOwnTerritoriesOverFragmented()
    {
        // Scenario A: one contiguous 6-tile Red island.
        var gridA = new HexGrid();
        for (int col = 0; col < 6; col++)
        {
            gridA.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        }
        GameState stateA = BuildState(gridA, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));

        // Scenario B: two disjoint 3-tile Red islands separated by
        // a Blue-colored gap tile (Blue has no territory because
        // a singleton is excluded — pass an explicit BuildRectGrid).
        // Use two separate 3-strips instead, widely spaced so hex
        // adjacency doesn't link them.
        var gridB = new HexGrid();
        for (int col = 0; col < 3; col++)
        {
            gridB.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        }
        for (int col = 6; col < 9; col++)
        {
            gridB.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        }
        GameState stateB = BuildState(gridB, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));

        double scoreA = AiStateScorer.Score(stateA, Red);
        double scoreB = AiStateScorer.Score(stateB, Red);

        // Same total tile count, but one territory (A) pays the
        // fragmentation penalty once, two territories (B) pay it
        // twice → A scores higher.
        Assert.True(scoreA > scoreB,
            $"expected merged (A={scoreA}) to beat fragmented (B={scoreB})");
    }

    // --- AiStateScorer: gold is invisible to standing score ---------------

    [Fact]
    public void Score_IgnoresTreasuryGold()
    {
        // Two identical Red 3-tile islands; only the capital treasury
        // differs (0g vs 1000g). With the GoldWeight term removed,
        // the scorer must read them as exactly equal — hoarded gold
        // contributes nothing to standing value.
        var gridA = new HexGrid();
        gridA.Add(new HexTile(HexCoord.FromOffset(0, 0), Red));
        gridA.Add(new HexTile(HexCoord.FromOffset(1, 0), Red));
        gridA.Add(new HexTile(HexCoord.FromOffset(2, 0), Red));
        GameState stateA = BuildState(gridA, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));

        var gridB = new HexGrid();
        gridB.Add(new HexTile(HexCoord.FromOffset(0, 0), Red));
        gridB.Add(new HexTile(HexCoord.FromOffset(1, 0), Red));
        gridB.Add(new HexTile(HexCoord.FromOffset(2, 0), Red));
        GameState stateB = BuildState(gridB, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));
        HexCoord capB = stateB.Territories.First(t => t.Owner == Red).Capital!.Value;
        stateB.Treasury.SetGold(capB, 1000);

        Assert.Equal(AiStateScorer.Score(stateA, Red), AiStateScorer.Score(stateB, Red));
    }

    // --- AiStateScorer: treasury covers transient deficit -----------------

    [Fact]
    public void Score_TreasuryCoversNegativeNetIncomeKeepsUnitsAlive()
    {
        // 3-tile Red territory with two Recruits: income 3, upkeep 4
        // (Recruit upkeep is 2), net = -1. The current willBankrupt
        // check zeroes unit value the moment netIncome < 0, regardless
        // of how much gold the capital holds. But the actual game only
        // bankrupts when treasury can't cover the shortfall on the next
        // upkeep step. A 100g treasury at -1/turn has 100 turns of
        // runway — the units should read as fully alive.
        HexGrid MakeGrid()
        {
            var g = new HexGrid();
            g.Add(new HexTile(HexCoord.FromOffset(0, 0), Red));
            g.Add(new HexTile(HexCoord.FromOffset(1, 0), Red));
            g.Add(new HexTile(HexCoord.FromOffset(2, 0), Red));
            return g;
        }

        GameState empty = BuildState(MakeGrid(), new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));
        HexCoord capEmpty = empty.Territories.First(t => t.Owner == Red).Capital!.Value;
        foreach (HexCoord c in new[] { HexCoord.FromOffset(0, 0), HexCoord.FromOffset(1, 0), HexCoord.FromOffset(2, 0) })
        {
            if (c.Equals(capEmpty)) continue;
            empty.Grid.Get(c)!.Occupant = new Unit(Red);
        }

        GameState rich = BuildState(MakeGrid(), new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));
        HexCoord capRich = rich.Territories.First(t => t.Owner == Red).Capital!.Value;
        foreach (HexCoord c in new[] { HexCoord.FromOffset(0, 0), HexCoord.FromOffset(1, 0), HexCoord.FromOffset(2, 0) })
        {
            if (c.Equals(capRich)) continue;
            rich.Grid.Get(c)!.Occupant = new Unit(Red);
        }
        rich.Treasury.SetGold(capRich, 100);

        double emptyScore = AiStateScorer.Score(empty, Red);
        double richScore = AiStateScorer.Score(rich, Red);

        // empty: 0g + (-1) = -1 < 0 → still bankrupt, no unit value.
        // rich : 100g + (-1) = 99 ≥ 0 → solvent, two Recruits worth
        // 4 each → +8 above empty.
        Assert.True(richScore > emptyScore,
            $"expected solvent-by-treasury (rich={richScore}) to beat " +
            $"insolvent (empty={emptyScore})");
    }

    // --- MovementRules.MovableUnitsInPowerOrder ---------------------------

    [Fact]
    public void MovableUnitsInPowerOrder_DescendsByLevel()
    {
        // 3-tile Red strip with Recruit / Soldier / Captain across the
        // tiles. Helper must return them in power-descending order:
        // Captain first, then Soldier, then Recruit.
        var grid = new HexGrid();
        grid.Add(new HexTile(HexCoord.FromOffset(0, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(1, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(2, 0), Red));
        GameState state = BuildState(grid, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));
        Territory red = state.Territories.First(t => t.Owner == Red);
        HexCoord cap = red.Capital!.Value;
        HexCoord[] offCap = red.Coords.Where(c => !c.Equals(cap)).Take(2).ToArray();
        // Capital tile is the 3rd unit position.
        grid.Get(offCap[0])!.Occupant = new Unit(Red, UnitLevel.Recruit);
        grid.Get(offCap[1])!.Occupant = new Unit(Red, UnitLevel.Soldier);
        grid.Get(cap)!.Occupant = new Unit(Red, UnitLevel.Captain);

        List<HexCoord> ordered = MovementRules.MovableUnitsInPowerOrder(red, Red, grid);

        Assert.Equal(3, ordered.Count);
        Assert.Equal(cap, ordered[0]);        // Captain
        Assert.Equal(offCap[1], ordered[1]);  // Soldier
        Assert.Equal(offCap[0], ordered[2]);  // Recruit
    }

    [Fact]
    public void MovableUnitsInPowerOrder_LexTiebreakerWithinTier()
    {
        // Two Soldiers at distinct coords. Tie on level → lex-min
        // coord wins. HexCoord.CompareTo orders by R then Q (row-major).
        var grid = new HexGrid();
        grid.Add(new HexTile(HexCoord.FromOffset(0, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(1, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(2, 0), Red));
        GameState state = BuildState(grid, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));
        Territory red = state.Territories.First(t => t.Owner == Red);
        HexCoord a = HexCoord.FromOffset(0, 0);
        HexCoord b = HexCoord.FromOffset(2, 0);
        grid.Get(a)!.Occupant = new Unit(Red, UnitLevel.Soldier);
        grid.Get(b)!.Occupant = new Unit(Red, UnitLevel.Soldier);

        List<HexCoord> ordered = MovementRules.MovableUnitsInPowerOrder(red, Red, grid);

        Assert.Equal(2, ordered.Count);
        HexCoord first = a.CompareTo(b) < 0 ? a : b;
        HexCoord second = a.CompareTo(b) < 0 ? b : a;
        Assert.Equal(first, ordered[0]);
        Assert.Equal(second, ordered[1]);
    }

    [Fact]
    public void MovableUnitsInPowerOrder_ExcludesMovedAndNonOwnerUnits()
    {
        // Three Red tiles: one fresh Recruit, one already-moved
        // Recruit, one empty tile. Helper must return ONLY the fresh
        // one. The non-owner case can't realistically arise (all
        // tiles in a territory share the owner) but the helper's
        // defensive check is still exercised by passing the wrong
        // owner — the result should be empty.
        var grid = new HexGrid();
        grid.Add(new HexTile(HexCoord.FromOffset(0, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(1, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(2, 0), Red));
        GameState state = BuildState(grid, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));
        Territory red = state.Territories.First(t => t.Owner == Red);
        HexCoord fresh = HexCoord.FromOffset(0, 0);
        HexCoord moved = HexCoord.FromOffset(1, 0);
        grid.Get(fresh)!.Occupant = new Unit(Red, UnitLevel.Recruit);
        var movedUnit = new Unit(Red, UnitLevel.Recruit) { HasMovedThisTurn = true };
        grid.Get(moved)!.Occupant = movedUnit;

        List<HexCoord> ordered = MovementRules.MovableUnitsInPowerOrder(red, Red, grid);
        Assert.Single(ordered);
        Assert.Equal(fresh, ordered[0]);

        // Pass wrong owner: defensive owner check yields empty list.
        List<HexCoord> wrongOwner = MovementRules.MovableUnitsInPowerOrder(red, Blue, grid);
        Assert.Empty(wrongOwner);
    }

    // --- MovementRules.MovableUnitsWeakestFirst ----------------------------

    [Fact]
    public void MovableUnitsWeakestFirst_AscendsByLevel()
    {
        // 3-tile Red strip with Recruit / Soldier / Captain across the
        // tiles. Helper must return them in power-ascending order:
        // Recruit first, then Soldier, then Captain.
        var grid = new HexGrid();
        grid.Add(new HexTile(HexCoord.FromOffset(0, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(1, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(2, 0), Red));
        GameState state = BuildState(grid, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));
        Territory red = state.Territories.First(t => t.Owner == Red);
        HexCoord cap = red.Capital!.Value;
        HexCoord[] offCap = red.Coords.Where(c => !c.Equals(cap)).Take(2).ToArray();
        grid.Get(offCap[0])!.Occupant = new Unit(Red, UnitLevel.Recruit);
        grid.Get(offCap[1])!.Occupant = new Unit(Red, UnitLevel.Soldier);
        grid.Get(cap)!.Occupant = new Unit(Red, UnitLevel.Captain);

        List<HexCoord> ordered = MovementRules.MovableUnitsWeakestFirst(red, Red, grid);

        Assert.Equal(3, ordered.Count);
        Assert.Equal(offCap[0], ordered[0]); // Recruit
        Assert.Equal(offCap[1], ordered[1]); // Soldier
        Assert.Equal(cap, ordered[2]);       // Captain
    }

    [Fact]
    public void MovableUnitsWeakestFirst_LexTiebreakerWithinTier_AndExcludesMoved()
    {
        // Two fresh Soldiers tie on level → lex-min coord first (same
        // tiebreak as the power-descending helper). An already-moved
        // Recruit is excluded by the shared gather scan.
        var grid = new HexGrid();
        grid.Add(new HexTile(HexCoord.FromOffset(0, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(1, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(2, 0), Red));
        GameState state = BuildState(grid, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));
        Territory red = state.Territories.First(t => t.Owner == Red);
        HexCoord a = HexCoord.FromOffset(0, 0);
        HexCoord b = HexCoord.FromOffset(2, 0);
        grid.Get(a)!.Occupant = new Unit(Red, UnitLevel.Soldier);
        grid.Get(b)!.Occupant = new Unit(Red, UnitLevel.Soldier);
        var movedRecruit = new Unit(Red, UnitLevel.Recruit) { HasMovedThisTurn = true };
        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = movedRecruit;

        List<HexCoord> ordered = MovementRules.MovableUnitsWeakestFirst(red, Red, grid);

        Assert.Equal(2, ordered.Count);
        HexCoord first = a.CompareTo(b) < 0 ? a : b;
        HexCoord second = a.CompareTo(b) < 0 ? b : a;
        Assert.Equal(first, ordered[0]);
        Assert.Equal(second, ordered[1]);
    }

    // --- AiCommon.MovableUnitTiersWeakestFirst ------------------------------

    [Fact]
    public void MovableUnitTiersWeakestFirst_OneEntryPerLevelAscending()
    {
        // Two Recruits and a Soldier → exactly one entry per distinct
        // level, ascending, each represented by its tier's lex-min unit.
        // This is the structural tier-skip guarantee: within one
        // ChooseNextAction call, each tier is scanned once, not once per
        // unit (same-level units share an identical ValidTargets set).
        var grid = new HexGrid();
        for (int col = 0; col < 4; col++)
            grid.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        GameState state = BuildState(grid, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));
        Territory red = state.Territories.First(t => t.Owner == Red);
        HexCoord recruitA = HexCoord.FromOffset(1, 0);
        HexCoord recruitB = HexCoord.FromOffset(3, 0);
        HexCoord soldier = HexCoord.FromOffset(2, 0);
        grid.Get(recruitA)!.Occupant = new Unit(Red, UnitLevel.Recruit);
        grid.Get(recruitB)!.Occupant = new Unit(Red, UnitLevel.Recruit);
        grid.Get(soldier)!.Occupant = new Unit(Red, UnitLevel.Soldier);

        List<(UnitLevel Level, HexCoord Coord)> tiers =
            AiCommon.MovableUnitTiersWeakestFirst(red, Red, grid);

        Assert.Equal(2, tiers.Count);
        Assert.Equal(UnitLevel.Recruit, tiers[0].Level);
        Assert.Equal(recruitA, tiers[0].Coord); // lex-min of the two Recruits
        Assert.Equal(UnitLevel.Soldier, tiers[1].Level);
        Assert.Equal(soldier, tiers[1].Coord);
    }

    [Fact]
    public void Enumerate_FirstMoveCandidateIsHighestPowerUnit()
    {
        // 5-tile Red strip sandwiched between two Blue singletons
        // so the strip's outer Red tiles have enemy adjacency on
        // each end and both unit placements can generate at least
        // one Capture candidate against defense-0 Blue.
        //
        // Soldier (level 2, upkeep 6) and Recruit (level 1, upkeep 2)
        // keep the territory solvent: income 5, upkeep 8, net -3, and
        // 30g treasury covers the 5-turn horizon (30 + 5 × -3 = 15 ≥ 0).
        // Higher tiers like Commander would bankrupt this small a
        // territory before any candidate cleared the solvency gate.
        //
        // The shared helper orders by power descending, so Soldier
        // candidates yield first.
        var grid = new HexGrid();
        grid.Add(new HexTile(HexCoord.FromOffset(0, 0), Blue));
        for (int col = 1; col <= 5; col++)
        {
            grid.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        }
        grid.Add(new HexTile(HexCoord.FromOffset(6, 0), Blue));
        GameState state = BuildState(grid, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));
        Territory red = state.Territories.First(t => t.Owner == Red);

        HexCoord cap = red.Capital!.Value;
        List<HexCoord> nonCap = red.Coords.Where(c => !c.Equals(cap)).ToList();
        HexCoord recruitTile = nonCap.First();
        HexCoord soldierTile = nonCap.Last();
        Assert.NotEqual(recruitTile, soldierTile);
        grid.Get(recruitTile)!.Occupant = new Unit(Red, UnitLevel.Recruit);
        grid.Get(soldierTile)!.Occupant = new Unit(Red, UnitLevel.Soldier);
        state.Treasury.SetGold(cap, 30);

        List<AiMoveAction> moves = AiCommon.Enumerate(red, state)
            .Select(c => c.Action)
            .OfType<AiMoveAction>()
            .ToList();
        // Sanity: both units contributed candidates so the ordering
        // assertion below isn't trivially satisfied by either having
        // zero moves.
        Assert.Contains(moves, m => m.Source.Equals(recruitTile));
        Assert.Contains(moves, m => m.Source.Equals(soldierTile));
        Assert.Equal(soldierTile, moves[0].Source);
    }

    // --- AiCommon: treasury-aware enumerator solvency ---------------------

    [Fact]
    public void Enumerate_HighTreasuryUnlocksHigherTierBuy()
    {
        // 5-tile Red strip bordering 2-tile Blue. Red has one Recruit
        // (upkeep 2), giving income 5 / upkeep 2 / net +1. Soldier
        // costs 15 gold (PurchaseRules.CostFor) with upkeep 6. With
        // 200g in treasury the solvency gate sees
        // (200 - 15) + (+1 - 6) = 180 ≥ 0 — Soldier buys are legal.
        // Captain (cost 30, upkeep 18) is similar.
        var grid = new HexGrid();
        for (int col = 0; col < 5; col++)
        {
            grid.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        }
        grid.Add(new HexTile(HexCoord.FromOffset(5, 0), Blue));
        grid.Add(new HexTile(HexCoord.FromOffset(6, 0), Blue));
        GameState state = BuildState(grid, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));
        Territory red = state.Territories.First(t => t.Owner == Red);
        HexCoord cap = red.Capital!.Value;
        // Place a Recruit somewhere off-capital so the territory has
        // exactly +1 net income (5 income - 2 upkeep = +1).
        HexCoord recruitTile = red.Coords.First(c => !c.Equals(cap));
        state.Grid.Get(recruitTile)!.Occupant = new Unit(Red);
        state.Treasury.SetGold(cap, 200);

        List<AiCandidate> candidates = AiCommon.Enumerate(red, state).ToList();
        Assert.True(
            candidates.Any(c => c.Action is AiBuyUnitAction b && b.Level == UnitLevel.Soldier),
            $"expected at least one Buy-Soldier candidate with 200g treasury; got: " +
            string.Join(", ", candidates.Select(c => c.Action.GetType().Name)));
    }

    // --- UpkeepRules.SurvivesNextUpkeep horizon cases ---------------------

    [Fact]
    public void SurvivesNextUpkeep_FiveTurnHorizon()
    {
        // Healthy: net income >= 0 → survives regardless of gold.
        Assert.True(UpkeepRules.SurvivesNextUpkeep(0, 0));
        Assert.True(UpkeepRules.SurvivesNextUpkeep(0, 5));

        // 5-turn horizon: treasury must cover 5 turns of the deficit,
        // not just one. Boundary: gold + 5 * netIncome == 0 just
        // survives.
        Assert.True(UpkeepRules.SurvivesNextUpkeep(5, -1));
        Assert.True(UpkeepRules.SurvivesNextUpkeep(25, -5));
        Assert.True(UpkeepRules.SurvivesNextUpkeep(500, -100));

        // Just below the 5-turn boundary: fails.
        Assert.False(UpkeepRules.SurvivesNextUpkeep(4, -1));
        Assert.False(UpkeepRules.SurvivesNextUpkeep(24, -5));
        Assert.False(UpkeepRules.SurvivesNextUpkeep(499, -100));

        // Cases that pass a 1-turn check but fail the 5-turn horizon —
        // correctly rejected.
        Assert.False(UpkeepRules.SurvivesNextUpkeep(1, -1));
        Assert.False(UpkeepRules.SurvivesNextUpkeep(100, -100));
    }

    [Fact]
    public void Enumerate_RejectsBuyThatWouldBankruptWithinHorizon()
    {
        // 5-tile Red strip bordering Blue with an existing Recruit
        // for +1 net income (5 income - 2 upkeep), 20g treasury.
        // Buy Soldier (cost 15g, upkeep 6) post-state is gold=5g,
        // net=-4 (or -5 for reposition). Under the 5-turn gate
        // 5 + 5*(-4) = -15 < 0 — Soldier candidate filtered.
        var grid = new HexGrid();
        for (int col = 0; col < 5; col++)
        {
            grid.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        }
        grid.Add(new HexTile(HexCoord.FromOffset(5, 0), Blue));
        grid.Add(new HexTile(HexCoord.FromOffset(6, 0), Blue));
        GameState state = BuildState(grid, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));
        Territory red = state.Territories.First(t => t.Owner == Red);
        HexCoord cap = red.Capital!.Value;
        HexCoord recruitTile = red.Coords.First(c => !c.Equals(cap));
        state.Grid.Get(recruitTile)!.Occupant = new Unit(Red);
        state.Treasury.SetGold(cap, 20);

        List<AiCandidate> candidates = AiCommon.Enumerate(red, state).ToList();
        Assert.DoesNotContain(candidates,
            c => c.Action is AiBuyUnitAction b && b.Level == UnitLevel.Soldier);
    }

    // --- AiStateScorer: orphaning enemies ---------------------------------

    [Fact]
    public void Score_RewardsOrphanedEnemyTerritory()
    {
        // Both scenarios isolate Red from Blue (gap of one empty
        // column between them) so no enemy-edge / undefended-border
        // terms apply to Red — the test stays a clean read on the
        // bankruptcy lookahead alone.
        //
        // Scenario A: Red 3-tile strip + healthy Blue 3-tile strip
        // with a capital.
        var gridA = new HexGrid();
        gridA.Add(new HexTile(HexCoord.FromOffset(0, 0), Red));
        gridA.Add(new HexTile(HexCoord.FromOffset(1, 0), Red));
        gridA.Add(new HexTile(HexCoord.FromOffset(2, 0), Red));
        gridA.Add(new HexTile(HexCoord.FromOffset(4, 0), Blue));
        gridA.Add(new HexTile(HexCoord.FromOffset(5, 0), Blue));
        gridA.Add(new HexTile(HexCoord.FromOffset(6, 0), Blue));
        GameState stateA = BuildState(gridA, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));

        // Scenario B: same Red, but Blue is three disconnected
        // singletons (placed two columns apart so no Blue neighbors
        // any other Blue). Singletons have no capital → bankruptcy
        // lookahead zeros their unit value, and they each pay the
        // FragmentationPenalty. Same total Blue tile count as A.
        var gridB = new HexGrid();
        gridB.Add(new HexTile(HexCoord.FromOffset(0, 0), Red));
        gridB.Add(new HexTile(HexCoord.FromOffset(1, 0), Red));
        gridB.Add(new HexTile(HexCoord.FromOffset(2, 0), Red));
        gridB.Add(new HexTile(HexCoord.FromOffset(4, 0), Blue));
        gridB.Add(new HexTile(HexCoord.FromOffset(6, 0), Blue));
        gridB.Add(new HexTile(HexCoord.FromOffset(8, 0), Blue));
        GameState stateB = BuildState(gridB, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));

        double scoreA = AiStateScorer.Score(stateA, Red);
        double scoreB = AiStateScorer.Score(stateB, Red);

        // Identical Red contribution in both. Blue side: A is one
        // healthy 3-tile territory worth 18 to Blue; B is three
        // capital-less singletons each worth -4 to Blue (10 tile +
        // 1 income - 15 fragmentation). B's enemy total is therefore
        // strictly lower → B's score for Red is strictly higher.
        Assert.True(scoreB > scoreA,
            $"expected orphaned-enemy (B={scoreB}) to beat healthy-enemy (A={scoreA})");
    }

    // --- ComputerAi: action selection ------------------------------------

    [Fact]
    public void ChooseNextAction_PrefersCaptureOverCombine()
    {
        // Two adjacent Red recruits with both a combine target
        // (each other) AND an undefended Blue capturable tile next
        // to them. The AI must prefer the capture because it
        // strictly increases tile count and net income, while the
        // combine only changes unit topology.
        var grid = new HexGrid();
        for (int col = 0; col < 6; col++)
        {
            grid.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        }
        grid.Add(new HexTile(HexCoord.FromOffset(6, 0), Blue));
        grid.Get(HexCoord.FromOffset(4, 0))!.Occupant = new Unit(Red);
        grid.Get(HexCoord.FromOffset(5, 0))!.Occupant = new Unit(Red);
        GameState state = BuildState(grid, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));

        AiAction? result = ComputerAi.ChooseNextAction(
            state, Red, new HashSet<HexCoord>(), new Random(0));

        AiMoveAction move = Assert.IsType<AiMoveAction>(result);
        HexTile? dst = state.Grid.Get(move.Destination);
        Assert.NotNull(dst);
        Assert.Equal(Blue, dst!.Owner); // destination is the captured Blue tile
    }

    [Fact]
    public void ChooseNextAction_SparseRoster_DoesNotThrow()
    {
        // Full 1-ply lookahead for a player whose slot index (5) exceeds the
        // compact roster size (slots 0 and 5 present; 1–4 are None).
        // Exercises AiCommon.Enumerate, AiSimulator.Clone/mutate, and
        // AiStateScorer.Score — each resolves the owner's difficulty by
        // slot, which must handle the sparse roster without throwing.
        PlayerId orange = PlayerId.FromIndex(5);
        var grid = new HexGrid();
        for (int col = 0; col < 6; col++)
            grid.Add(new HexTile(HexCoord.FromOffset(col, 0), orange));
        grid.Add(new HexTile(HexCoord.FromOffset(6, 0), Red));
        grid.Get(HexCoord.FromOffset(4, 0))!.Occupant = new Unit(orange);
        grid.Get(HexCoord.FromOffset(5, 0))!.Occupant = new Unit(orange);
        GameState state = BuildState(grid,
            new Player("Red", Red), new Player("Orange", orange, PlayerKind.Computer));

        AiAction? result = ComputerAi.ChooseNextAction(
            state, orange, new HashSet<HexCoord>(), new Random(0));

        // The lookahead must run without throwing; the score-positive capture
        // of the lone Red tile is the expected pick.
        Assert.NotNull(result);
    }

    [Fact]
    public void Score_PenalizesOwnTrees()
    {
        // Same Red territory, one with a tree occupant and one
        // without. The tree-bearing state should score strictly
        // worse so the AI treats tree clearing as a positive
        // action.
        var gridA = new HexGrid();
        for (int col = 0; col < 4; col++)
            gridA.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        GameState cleanState = BuildState(gridA, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));

        var gridB = new HexGrid();
        for (int col = 0; col < 4; col++)
            gridB.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        gridB.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Tree();
        GameState treedState = BuildState(gridB, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));

        double cleanScore = AiStateScorer.Score(cleanState, Red);
        double treedScore = AiStateScorer.Score(treedState, Red);

        Assert.True(cleanScore > treedScore,
            $"expected tree-free state ({cleanScore}) to score above tree-present state ({treedScore})");
    }

    [Fact]
    public void Score_PenalizesOwnGraves_SameAsOwnTrees()
    {
        // A grave on an own tile is guaranteed to become a tree
        // on the next start-of-turn (TreeRules.ConvertGravesToTrees
        // runs unconditionally). The scorer should treat graves and
        // trees on own tiles identically so the AI prioritizes
        // burying graves with the same urgency as chopping trees.
        var gridTree = new HexGrid();
        for (int col = 0; col < 4; col++)
            gridTree.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        gridTree.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Tree();
        GameState treeState = BuildState(gridTree, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));

        var gridGrave = new HexGrid();
        for (int col = 0; col < 4; col++)
            gridGrave.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        gridGrave.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Grave();
        GameState graveState = BuildState(gridGrave, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));

        double treeScore = AiStateScorer.Score(treeState, Red);
        double graveScore = AiStateScorer.Score(graveState, Red);

        Assert.Equal(treeScore, graveScore);
    }

    [Fact]
    public void Score_OwnTreePenaltyIsRaisedSoChopsBeatBorderExposure()
    {
        // A chop is worth OwnTreePenalty (tree removed) minus
        // UndefendedBorderPenalty(10) per border the chopping unit stops
        // covering, and on a bankrupt territory the +1 income gain is
        // clamped away. OwnTreePenalty is 35 so a chop beats even a
        // three-border exposure (35 - 30 = +5).
        //
        // Pin it on a BANKRUPT territory (Captain upkeep 18 >> income) so
        // income clamps to 0 in both states and unit value is zeroed in
        // both: the only score difference is the single own tree, so
        // Score(no-tree) - Score(tree) == OwnTreePenalty exactly.
        var gridTree = new HexGrid();
        for (int col = 0; col < 3; col++)
            gridTree.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        gridTree.Get(HexCoord.FromOffset(0, 0))!.Occupant = new Unit(Red, UnitLevel.Captain);
        gridTree.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Tree();
        GameState treeState = BuildState(gridTree,
            new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));

        var gridNoTree = new HexGrid();
        for (int col = 0; col < 3; col++)
            gridNoTree.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        gridNoTree.Get(HexCoord.FromOffset(0, 0))!.Occupant = new Unit(Red, UnitLevel.Captain);
        GameState noTreeState = BuildState(gridNoTree,
            new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));

        int treeScore = AiStateScorer.Score(treeState, Red);
        int noTreeScore = AiStateScorer.Score(noTreeState, Red);

        Assert.Equal(35, noTreeScore - treeScore);
    }

    [Fact]
    public void Score_DoesNotApplyTreePenaltyToEnemyGraves()
    {
        // Penalty must remain own-side only. Two states with one
        // grave each: one on a Red tile, one on an equivalent Blue
        // tile. From Red's perspective, a Red grave should be worse
        // (own penalty + own income loss) than a Blue grave (which
        // just hurts Blue's territory value, slightly helping Red).
        var gridRedGrave = TestHelpers.BuildRectGrid(6, 1, Blue);
        for (int col = 0; col < 3; col++)
            gridRedGrave.Get(HexCoord.FromOffset(col, 0))!.Owner = Red;
        gridRedGrave.Get(HexCoord.FromOffset(1, 0))!.Occupant = new Grave();
        GameState redGraveState = BuildState(gridRedGrave, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));

        var gridBlueGrave = TestHelpers.BuildRectGrid(6, 1, Blue);
        for (int col = 0; col < 3; col++)
            gridBlueGrave.Get(HexCoord.FromOffset(col, 0))!.Owner = Red;
        gridBlueGrave.Get(HexCoord.FromOffset(4, 0))!.Occupant = new Grave();
        GameState blueGraveState = BuildState(gridBlueGrave, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));

        double redGraveScore = AiStateScorer.Score(redGraveState, Red);
        double blueGraveScore = AiStateScorer.Score(blueGraveState, Red);

        Assert.True(redGraveScore < blueGraveScore,
            $"expected Red grave ({redGraveScore}) to score below Blue grave ({blueGraveScore})");
    }

    // --- BuildTowerBonus: per-action tower-defense incentive -------------

    [Fact]
    public void BuildTowerBonus_CountsAllBorderTilesInCoverage_WhenNonePreviouslyTowered()
    {
        // 6-tile Red strip in a Blue field, no existing towers.
        // A new tower at (2,0) covers itself + same-territory
        // neighbors (1,0) and (3,0). All three are border tiles
        // (each has Blue neighbors in row 1). None pre-defended by
        // another tower → bonus = 3 × 10 = 30.
        var grid = TestHelpers.BuildRectGrid(6, 3, Blue);
        for (int col = 0; col < 6; col++)
            grid.Get(HexCoord.FromOffset(col, 0))!.Owner = Red;
        GameState state = BuildState(grid, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));

        double bonus = AiStateScorer.BuildTowerBonus(HexCoord.FromOffset(2, 0), state, Red);

        Assert.Equal(30.0, bonus);
    }

    [Fact]
    public void BuildTowerBonus_ExcludesBorderTilesAlreadyCoveredByAnotherTower()
    {
        // Same 6-tile Red strip with an existing tower at (5,0). A
        // would-be new placement at (4,0) covers {(3,0), (4,0),
        // (5,0)}. (5,0) has its own Tower (≠ placement), and (4,0)
        // has neighbor (5,0) hosting a Tower (≠ placement) — both
        // already tower-defended, so they don't contribute. (3,0)
        // has no other tower covering it → counts. Bonus = 10.
        var grid = TestHelpers.BuildRectGrid(6, 3, Blue);
        for (int col = 0; col < 6; col++)
            grid.Get(HexCoord.FromOffset(col, 0))!.Owner = Red;
        grid.Get(HexCoord.FromOffset(5, 0))!.Occupant = new Tower();
        GameState state = BuildState(grid, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));

        double bonus = AiStateScorer.BuildTowerBonus(HexCoord.FromOffset(4, 0), state, Red);

        Assert.Equal(10.0, bonus);
    }

    [Fact]
    public void BuildTowerBonus_ZeroWhenNoBorderTilesInCoverage()
    {
        // Isolated 3x3 Red island with no Blue anywhere on the
        // grid. Every tile is interior (off-map neighbors don't
        // count as enemy). Placing a tower in the middle covers 7
        // tiles, none of which are borders → bonus = 0.
        var grid = TestHelpers.BuildRectGrid(3, 3, Red);
        GameState state = BuildState(grid, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));

        double bonus = AiStateScorer.BuildTowerBonus(HexCoord.FromOffset(1, 1), state, Red);

        Assert.Equal(0.0, bonus);
    }

    // --- Standing contested-border defense magnitude term ----------------
    // Capturing an enemy mountain lands the defender on +1 high-ground, so
    // the capture's Score delta is strictly higher than capturing a plain
    // tile. (The defense magnitude lives in Score() now, not a per-action
    // bonus — see AiStateScorerTests for the positional/cap coverage.)

    [Fact]
    public void Score_CapturingEnemyMountain_BeatsCapturingPlain()
    {
        int CaptureDelta(bool mountain)
        {
            var grid = TestHelpers.BuildRectGrid(5, 4, Blue);
            for (int col = 0; col < 5; col++)
                grid.Get(HexCoord.FromOffset(col, 0))!.Owner = Red;
            grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Unit(Red, UnitLevel.Soldier);
            HexCoord target = HexCoord.FromOffset(2, 1);
            grid.Get(target)!.IsMountain = mountain;
            GameState state = BuildState(grid, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));

            int baseScore = AiStateScorer.Score(state, Red);
            GameState clone = AiSimulator.Clone(state);
            AiSimulator.Apply(new AiMoveAction(HexCoord.FromOffset(2, 0), target), clone);
            return AiStateScorer.Score(clone, Red) - baseScore;
        }

        Assert.True(CaptureDelta(true) > CaptureDelta(false),
            $"expected mountain capture {CaptureDelta(true)} > plain capture {CaptureDelta(false)}");
    }

    [Fact]
    public void ChooseNextAction_TakesEnclosedEnemyCapture_DespiteSurroundingTowers()
    {
        // A capture that turns own border tiles into interior must
        // still go through (delta ~+25). Six Red tiles ringing a Blue
        // singleton, with one Red tile holding a Tower covering 3
        // borders, plus a Red recruit adjacent to the enclave.
        var grid = new HexGrid();
        grid.Add(new HexTile(HexCoord.FromOffset(1, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(2, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(0, 1), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(2, 1), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(1, 2), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(2, 2), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(1, 1), Blue));
        grid.Get(HexCoord.FromOffset(2, 1))!.Occupant = new Tower();
        GameState state = BuildState(grid, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));
        // Capital lands at (1,0) by lex-min over the empty Red
        // tiles, so a recruit at (0,1) is the adjacent attacker.
        grid.Get(HexCoord.FromOffset(0, 1))!.Occupant = new Unit(Red);

        AiAction? result = ComputerAi.ChooseNextAction(
            state, Red, new HashSet<HexCoord>(), new Random(0));

        AiMoveAction move = Assert.IsType<AiMoveAction>(result);
        Assert.Equal(HexCoord.FromOffset(0, 1), move.Source);
        Assert.Equal(HexCoord.FromOffset(1, 1), move.Destination);
    }

    [Fact]
    public void ChooseNextAction_PrefersChopOverCombine()
    {
        // 10-tile Red territory with two adjacent recruits and a
        // tree the recruits can reach. 10 tiles / 1 tree /
        // 2 recruits → net income 9 - 4 = 5, which is enough for a
        // P+P→S combine (upkeep delta +2) AND for a chop; both
        // are legal. With the own-tree penalty, chopping must
        // outrank combining.
        var grid = new HexGrid();
        for (int col = 0; col < 10; col++)
            grid.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Tree();
        grid.Get(HexCoord.FromOffset(0, 0))!.Occupant = new Unit(Red);
        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = new Unit(Red);
        GameState state = BuildState(grid, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));

        AiAction? result = ComputerAi.ChooseNextAction(
            state, Red, new HashSet<HexCoord>(), new Random(0));

        AiMoveAction move = Assert.IsType<AiMoveAction>(result);
        HexTile? dst = state.Grid.Get(move.Destination);
        Assert.NotNull(dst);
        Assert.IsType<Tree>(dst!.Occupant); // destination is the tree → chop
    }

    [Fact]
    public void Score_PenalizesLongerEnemyBorder()
    {
        // Two states with the same total tiles but different
        // shapes: a compact 2x3 own blob vs a 1x6 own strip.
        // The strip has more enemy-facing edges → should score
        // strictly worse.
        //
        // Build both inside a 6x3 Blue field so adjacency is
        // well-defined and the Red blob has identifiable borders.
        var gridCompact = TestHelpers.BuildRectGrid(6, 3, Blue);
        for (int col = 0; col < 2; col++)
            for (int row = 0; row < 3; row++)
                gridCompact.Get(HexCoord.FromOffset(col, row))!.Owner = Red;
        GameState compact = BuildState(gridCompact, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));

        var gridStrip = TestHelpers.BuildRectGrid(6, 3, Blue);
        for (int col = 0; col < 6; col++)
            gridStrip.Get(HexCoord.FromOffset(col, 0))!.Owner = Red;
        GameState strip = BuildState(gridStrip, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));

        double compactScore = AiStateScorer.Score(compact, Red);
        double stripScore = AiStateScorer.Score(strip, Red);

        Assert.True(compactScore > stripScore,
            $"expected compact blob ({compactScore}) to outscore strip ({stripScore})");
    }

    [Fact]
    public void Score_EnemyEdgePenalty_DominatesShapeQuality()
    {
        // Tightens Score_PenalizesLongerEnemyBorder: not just
        // "compact > strip" but compact wins by a margin large enough
        // that EnemyEdgePenalty has to weigh meaningfully against
        // TileWeight (10) for the AI to value enclosure / concavity-
        // fill captures.
        //
        // Layout: compact 2x3 Red blob vs strip 1x6 Red blob, both in
        // a 6x3 Blue field. Both have 6 Red tiles, 12 Blue tiles,
        // identical TerritoryValue (no units, no trees, no gold).
        // Capitals land at HexCoord(0,0) by lex-min in both. Counted:
        //   compact: 5 enemy-facing edges, 2 undefended border tiles
        //            (capital covers (1,0); (1,1) and (1,2) exposed).
        //   strip:   11 enemy-facing edges, 4 undefended border tiles
        //            (capital covers (1,0); (2..5,0) exposed).
        // Score gap = 10 * (4 - 2) + W * (11 - 5) = 20 + 6W.
        // At W=1 (the original weight) the gap is only 26, indicating
        // surface area is comparable to ~half a tile — too weak to
        // motivate enclosure captures. The threshold below requires
        // W > ~2.5, forcing the constant well above 1.
        var gridCompact = TestHelpers.BuildRectGrid(6, 3, Blue);
        for (int col = 0; col < 2; col++)
            for (int row = 0; row < 3; row++)
                gridCompact.Get(HexCoord.FromOffset(col, row))!.Owner = Red;
        GameState compact = BuildState(gridCompact, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));

        var gridStrip = TestHelpers.BuildRectGrid(6, 3, Blue);
        for (int col = 0; col < 6; col++)
            gridStrip.Get(HexCoord.FromOffset(col, 0))!.Owner = Red;
        GameState strip = BuildState(gridStrip, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));

        double gap = AiStateScorer.Score(compact, Red) - AiStateScorer.Score(strip, Red);

        Assert.True(gap >= 35,
            $"shape-quality gap {gap} too small; surface-area term must " +
            "outweigh ~half a tile for the AI to value enclosure captures");
    }

    [Fact]
    public void Score_PenalizesUndefendedBorderTiles()
    {
        // Same 2x3 Red blob in a Blue field. In state A a recruit
        // sits on a border tile (providing defense). In state B
        // there's no unit at all → every border tile is
        // undefended. A should score strictly higher.
        var gridDefended = TestHelpers.BuildRectGrid(6, 3, Blue);
        for (int col = 0; col < 2; col++)
            for (int row = 0; row < 3; row++)
                gridDefended.Get(HexCoord.FromOffset(col, row))!.Owner = Red;
        gridDefended.Get(HexCoord.FromOffset(1, 1))!.Occupant = new Unit(Red);
        GameState defended = BuildState(gridDefended, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));

        var gridUndefended = TestHelpers.BuildRectGrid(6, 3, Blue);
        for (int col = 0; col < 2; col++)
            for (int row = 0; row < 3; row++)
                gridUndefended.Get(HexCoord.FromOffset(col, row))!.Owner = Red;
        GameState undefended = BuildState(gridUndefended, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));

        double defendedScore = AiStateScorer.Score(defended, Red);
        double undefendedScore = AiStateScorer.Score(undefended, Red);

        Assert.True(defendedScore > undefendedScore,
            $"expected defended state ({defendedScore}) to outscore undefended ({undefendedScore})");
    }

    [Fact]
    public void ChooseNextAction_ReturnsNull_WhenNoPositiveDeltaActionExists()
    {
        // 3-tile isolated Red island with no units, no enemies,
        // no gold, no trees. Nothing productive to do → the AI
        // returns null rather than playing a bad move.
        var grid = new HexGrid();
        grid.Add(new HexTile(HexCoord.FromOffset(0, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(1, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(2, 0), Red));
        GameState state = BuildState(grid, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));

        AiAction? result = ComputerAi.ChooseNextAction(
            state, Red, new HashSet<HexCoord>(), new Random(0));

        Assert.Null(result);
    }

    // --- Reposition: defensive moves into undefended border tiles -------

    [Fact]
    public void ChooseNextAction_PicksDefensiveReposition_ToCoverUndefendedBorder()
    {
        // Red 2x3 blob in a Blue field, no captures available
        // (recruit adjacent only to friendly tiles). Place the
        // recruit on an interior Red tile, leaving the border tiles
        // undefended. The heuristic must pick a reposition that
        // moves the recruit onto a border tile, reducing the
        // undefended-border penalty and improving its score.
        var grid = TestHelpers.BuildRectGrid(6, 3, Blue);
        for (int col = 0; col < 2; col++)
            for (int row = 0; row < 3; row++)
                grid.Get(HexCoord.FromOffset(col, row))!.Owner = Red;
        // Recruit on interior Red tile (0,1) — its enemy-color
        // neighbors all sit OUTSIDE its current tile, so it gains
        // no defense by staying put.
        grid.Get(HexCoord.FromOffset(0, 1))!.Occupant = new Unit(Red);
        GameState state = BuildState(grid, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));

        AiAction? result = ComputerAi.ChooseNextAction(
            state, Red, new HashSet<HexCoord>(), new Random(0));

        AiMoveAction move = Assert.IsType<AiMoveAction>(result);
        Assert.Equal(HexCoord.FromOffset(0, 1), move.Source);
        Assert.True(AiCommon.IsBorderTile(move.Destination, state.Grid, Red),
            $"recruit should reposition onto a border tile (got {move.Destination})");
    }

    // --- Simulator: reposition apply --------------------------------------

    [Fact]
    public void Simulator_ApplyMoveReposition_MarksUnitMoved_ToPreventAiPingPong()
    {
        // MovementRules.ResolveArrival leaves HasMovedThisTurn = false
        // for a pure reposition (empty own destination) so a human
        // can stack micro-actions on a single unit. The AI applies a
        // stricter rule: a reposition consumes the unit's action,
        // otherwise the AI would re-enumerate the same unit and
        // ping-pong it between border tiles. AiSimulator.Apply (and
        // GameController.ExecuteAiMove) explicitly mark the unit as
        // moved after a reposition.
        var grid = new HexGrid();
        for (int col = 0; col <= 3; col++)
            grid.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(4, 0), Blue));
        grid.Get(HexCoord.FromOffset(0, 0))!.Occupant = new Unit(Red);
        GameState state = BuildState(grid, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));
        IReadOnlyList<Territory> beforeTerritories = state.Territories;

        var move = new AiMoveAction(HexCoord.FromOffset(0, 0), HexCoord.FromOffset(3, 0));
        AiSimulator.Apply(move, state);

        Assert.Null(state.Grid.Get(HexCoord.FromOffset(0, 0))!.Occupant);
        Unit moved = Assert.IsType<Unit>(state.Grid.Get(HexCoord.FromOffset(3, 0))!.Occupant);
        Assert.True(moved.HasMovedThisTurn);
        Assert.Same(beforeTerritories, state.Territories);
    }

    [Fact]
    public void Simulator_ApplyBuyReposition_MarksUnitMoved()
    {
        var grid = new HexGrid();
        for (int col = 0; col <= 3; col++)
            grid.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(4, 0), Blue));
        grid.Get(HexCoord.FromOffset(4, 0))!.Occupant = new Unit(Blue, UnitLevel.Soldier);
        GameState state = BuildState(grid, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));
        HexCoord cap = state.Territories.First(t => t.Owner == Red).Capital!.Value;
        state.Treasury.SetGold(cap, 10);
        IReadOnlyList<Territory> beforeTerritories = state.Territories;

        var buy = new AiBuyUnitAction(cap, HexCoord.FromOffset(3, 0), UnitLevel.Recruit);
        AiSimulator.Apply(buy, state);

        // Recruit @ Soldier costs 10 and the capital started with 10, so the literal
        // post-buy gold is 0. Pin it rather than recompute with CostFor, the same call
        // AiSimulator.Apply deducts with (which would shift both sides together).
        Assert.Equal(0, state.Treasury.GetGold(cap));
        Unit placed = Assert.IsType<Unit>(state.Grid.Get(HexCoord.FromOffset(3, 0))!.Occupant);
        Assert.Equal(Red, placed.Owner);
        Assert.Equal(UnitLevel.Recruit, placed.Level);
        Assert.True(placed.HasMovedThisTurn);
        Assert.Same(beforeTerritories, state.Territories);
    }

    [Theory]
    [MemberData(nameof(UnsupportedAiActions))]
    public void Simulator_Apply_ThrowsOnUnsupportedActionKind(AiAction action)
    {
        // Defense in depth: AiSimulator.Apply only models the three
        // mutation kinds that AiCommon.Enumerate emits. If the
        // enumerator (or a future AI) ever produces another kind, we
        // want a loud crash rather than a silent no-op that would
        // make scored futures disagree with live play.
        var grid = new HexGrid();
        grid.Add(new HexTile(HexCoord.FromOffset(0, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(1, 0), Red));
        GameState state = BuildState(grid, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));

        Assert.Throws<NotSupportedException>(() => AiSimulator.Apply(action, state));
    }

    public static IEnumerable<object[]> UnsupportedAiActions()
    {
        yield return new object[] { new AiLongPressRallyAction(HexCoord.FromOffset(0, 0)) };
        yield return new object[] { new AiClaimVictoryAction(60) };
        yield return new object[] { new AiDismissClaimAction(60) };
        yield return new object[] { new AiDismissDefeatAction() };
    }

    [Fact]
    public void ComputerAi_DoesNotPingPongRepositions()
    {
        // After repositioning a unit once, calling the chooser again
        // must not return another move whose source is where that
        // unit ended up — that would be the ping-pong the user
        // surfaced in playtest. Apply via AiSimulator (the live
        // controller path mirrors it) and confirm the second call
        // either picks a different action or returns null.
        var grid = TestHelpers.BuildRectGrid(6, 3, Blue);
        for (int col = 0; col < 2; col++)
            for (int row = 0; row < 3; row++)
                grid.Get(HexCoord.FromOffset(col, row))!.Owner = Red;
        grid.Get(HexCoord.FromOffset(0, 1))!.Occupant = new Unit(Red);
        GameState state = BuildState(grid, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));

        AiAction? first = ComputerAi.ChooseNextAction(
            state, Red, new HashSet<HexCoord>(), new Random(0));
        AiMoveAction firstMove = Assert.IsType<AiMoveAction>(first);
        AiSimulator.Apply(firstMove, state);

        AiAction? second = ComputerAi.ChooseNextAction(
            state, Red, new HashSet<HexCoord>(), new Random(0));
        if (second is AiMoveAction secondMove)
        {
            Assert.NotEqual(firstMove.Destination, secondMove.Source);
        }
    }

    [Fact]
    public void ChooseNextAction_VisitsLargestTerritoryFirst()
    {
        // Two Red territories of different sizes. Both have one capture candidate
        // with identical score delta (+22). Under strict > comparison, the FIRST
        // territory visited locks in its action; the second's equal delta cannot
        // displace it. So the result reveals visit order. Sort is size desc,
        // so the big territory (3 cells) visits first.
        //
        // Grid: 30×1, all Blue. Small Red = cols 10-11, big Red = cols 20-22.
        // Blue territories {0-9}, {12-19}, {23-29} have capitals at cols 0, 12, 23.
        // Each territory's left-facing Blue cell (9 and 19) is NOT a capital → defense=0,
        // so a Recruit placed at the left border of each territory can capture it.
        //
        // Recruits are placed at the LEFT border cell of each territory so:
        //   - CapitalPlacer lands the capital on the next cell (lex-min empty).
        //   - After the Recruit captures leftward, the capital's own defense covers
        //     any remaining border tiles — no undefended tile is created.
        //   - Score delta is identical for both captures (+22), so the FIRST territory
        //     visited locks in the winner (strict > means equal delta can't displace it).
        //
        // Sort is size desc: big (3 cells) visits first → Source=(20,0).
        var grid = TestHelpers.BuildRectGrid(30, 1, Blue);

        grid.Get(HexCoord.FromOffset(10, 0))!.Owner = Red;
        grid.Get(HexCoord.FromOffset(11, 0))!.Owner = Red;
        grid.Get(HexCoord.FromOffset(10, 0))!.Occupant = new Unit(Red);  // left border; capital → (11,0)

        grid.Get(HexCoord.FromOffset(20, 0))!.Owner = Red;
        grid.Get(HexCoord.FromOffset(21, 0))!.Owner = Red;
        grid.Get(HexCoord.FromOffset(22, 0))!.Owner = Red;
        grid.Get(HexCoord.FromOffset(20, 0))!.Occupant = new Unit(Red);  // left border; capital → (21,0)

        GameState state = BuildState(grid,
            new Player("Red", PlayerId.FromIndex(0)),
            new Player("Blue", PlayerId.FromIndex(1)));

        AiAction? action = ComputerAi.ChooseNextAction(
            state, Red, new HashSet<HexCoord>(), new Random(0));

        AiMoveAction mv = Assert.IsType<AiMoveAction>(action);
        Assert.Equal(HexCoord.FromOffset(20, 0), mv.Source);
    }

    // -----------------------------------------------------------------------
    // Phase-ordering invariants (stepwise-greedy AI)
    // -----------------------------------------------------------------------

    [Fact]
    public void ChooseNextAction_Phase3BuyCaptureBeforePhase4Tower()
    {
        // Same setup as the previous ChooseNextAction_BuildsTower test:
        // 3x3 Red blob facing 3x3 Blue blob, no units, TowerCost gold.
        // Under flat scoring, the tower wins (coverage bonus large).
        // Under phase ordering, buy-capture is phase 3 and tower is phase
        // 4 — so buy-capture must fire first even though it scores lower.
        var grid = TestHelpers.BuildRectGrid(6, 3, Blue);
        for (int col = 0; col < 3; col++)
            for (int row = 0; row < 3; row++)
                grid.Get(HexCoord.FromOffset(col, row))!.Owner = Red;
        GameState state = BuildState(grid, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));
        HexCoord cap = state.Territories.First(t => t.Owner == Red).Capital!.Value;
        state.Treasury.SetGold(cap, PurchaseRules.TowerCostFor(Difficulty.Soldier));

        AiAction? result = ComputerAi.ChooseNextAction(
            state, Red, new HashSet<HexCoord>(), new Random(0));

        Assert.IsType<AiBuyUnitAction>(result);
    }

    [Fact]
    public void ChooseNextAction_Phase4TowerWhenNoBuyCaptureAvailable()
    {
        // Isolated Red blob (no adjacent enemies) → phase 3 buy-capture
        // is impossible. With TowerCost gold and undefended border tiles,
        // phase 4a tower build must fire.
        //
        // Layout: 3x3 Red island surrounded by water (off-grid tiles) on
        // all sides except the right column which borders Blue. Wait, that
        // would enable phase 3. Instead: Red with trees as its only
        // neighbors, no enemy tiles reachable by any affordable buy.
        //
        // Simpler: a standalone 5-tile Red cross in an otherwise-empty
        // grid — no Blue tiles at all. Phase 3 has no capture targets.
        // But then no border tiles exist either, so towers don't help.
        //
        // Correct approach: Red blob in a Blue field BUT the Blue tiles
        // are all defended so even a Commander can't capture them. That
        // removes all phase-3 candidates while keeping border tiles for
        // phase 4a.
        //
        // 3x3 Red blob (cols 0-2) facing 3x3 Blue blob (cols 3-5) with
        // a Blue Commander on each Blue border tile — defense >= 4 on all
        // Blue border tiles, nothing in phase 1-3 can breach them. Red has
        // TowerCost gold. Phase 4a tower must fire.
        var grid = TestHelpers.BuildRectGrid(6, 3, Blue);
        for (int col = 0; col < 3; col++)
            for (int row = 0; row < 3; row++)
                grid.Get(HexCoord.FromOffset(col, row))!.Owner = Red;
        // Place Blue Commanders on the three Blue border tiles so defense >= 4.
        grid.Get(HexCoord.FromOffset(3, 0))!.Occupant = new Unit(Blue, UnitLevel.Commander);
        grid.Get(HexCoord.FromOffset(3, 1))!.Occupant = new Unit(Blue, UnitLevel.Commander);
        grid.Get(HexCoord.FromOffset(3, 2))!.Occupant = new Unit(Blue, UnitLevel.Commander);
        GameState state = BuildState(grid, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));
        HexCoord cap = state.Territories.First(t => t.Owner == Red).Capital!.Value;
        state.Treasury.SetGold(cap, PurchaseRules.TowerCostFor(Difficulty.Soldier));

        AiAction? result = ComputerAi.ChooseNextAction(
            state, Red, new HashSet<HexCoord>(), new Random(0));

        Assert.IsType<AiBuildTowerAction>(result);
    }

    [Fact]
    public void ChooseNextAction_Phase1FreeCaptureBeforePhase3BuyCapture()
    {
        // Red 5-tile strip with a Recruit at (4,0) adjacent to an
        // undefended Blue tile at (5,0) — phase-1 capture, delta ~+20.
        // Blue Captain at (-1,0) adjacent to Red's (0,0): only a
        // Commander can capture it (defense 3 < Commander 4). Destroying
        // the Captain boosts Blue's unit-value loss → Commander buy-capture
        // scores higher than the Recruit free-capture under flat scoring.
        // Phase ordering must pick the Recruit free-capture first (phase 1).
        var grid = new HexGrid();
        for (int col = 0; col < 5; col++)
            grid.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(5, 0), Blue));          // undefended
        grid.Add(new HexTile(HexCoord.FromOffset(-1, 0), Blue));         // defended by Captain
        grid.Get(HexCoord.FromOffset(-1, 0))!.Occupant = new Unit(Blue, UnitLevel.Captain);
        HexCoord recruitCoord = HexCoord.FromOffset(4, 0);
        grid.Get(recruitCoord)!.Occupant = new Unit(Red, UnitLevel.Recruit);
        GameState state = BuildState(grid,
            new Player("Red", PlayerId.FromIndex(0)),
            new Player("Blue", PlayerId.FromIndex(1)));
        HexCoord cap = state.Territories.First(t => t.Owner == Red).Capital!.Value;
        state.Treasury.SetGold(cap, 80); // enough for Commander (40g)

        AiAction? result = ComputerAi.ChooseNextAction(
            state, Red, new HashSet<HexCoord>(), new Random(0));

        // Phase 1 fires: Recruit free-captures (5,0).
        AiMoveAction mv = Assert.IsType<AiMoveAction>(result);
        Assert.Equal(recruitCoord, mv.Source);
        Assert.Equal(HexCoord.FromOffset(5, 0), mv.Destination);
    }

    [Fact]
    public void ChooseNextAction_Phase2aUnlockFilter_NoCombineWhenNoUnlock()
    {
        // Two adjacent Red Recruits. Both can already reach the same
        // undefended Blue tile (so a Recruit→Recruit combine into a
        // Soldier doesn't unlock any NEW movement-consuming target —
        // both could already capture it). Under phase-2a's unlock filter,
        // this combine must NOT be emitted. With no phase-1 capture
        // available (e.g., both Recruits are on interior tiles only) and
        // no phase-3/4 candidates, ChooseNextAction returns null.
        //
        // Layout: 6-tile Red strip (cols 0-5). Recruit at (0,0) and (1,0).
        // Undefended Blue at (6,0). Both Recruits can reach (6,0) already.
        // No gold for buys (phase 3 out). No existing towers needed.
        // Result: null (combine doesn't unlock, no other candidates).
        var grid = new HexGrid();
        for (int col = 0; col < 6; col++)
            grid.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(6, 0), Blue));
        grid.Get(HexCoord.FromOffset(0, 0))!.Occupant = new Unit(Red, UnitLevel.Recruit);
        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = new Unit(Red, UnitLevel.Recruit);
        GameState state = BuildState(grid,
            new Player("Red", PlayerId.FromIndex(0)),
            new Player("Blue", PlayerId.FromIndex(1)));
        // No gold: eliminates phase 3 and 4a.

        AiAction? result = ComputerAi.ChooseNextAction(
            state, Red, new HashSet<HexCoord>(), new Random(0));

        // Both Recruits can capture (6,0) directly — there IS a phase-1 action.
        // So the result should be a capture, not null. This test verifies
        // phase 1 fires (capture at (6,0)) rather than a non-unlocking combine.
        AiMoveAction mv = Assert.IsType<AiMoveAction>(result);
        Assert.Equal(HexCoord.FromOffset(6, 0), mv.Destination);
    }

    [Fact]
    public void ChooseNextAction_Phase2aUnlockFilter_CombineWhenUnlocks()
    {
        // 8-tile Red strip with a Recruit and a Soldier on it. Adjacent
        // Blue Soldier at (8,0) has defense 2. Neither Red unit can
        // capture alone (Recruit level 1 < 2, Soldier level 2 not < 2).
        // Combining Recruit+Soldier → Captain (1+2=3): defense 2 < 3 →
        // unlock! Phase 1 is empty (no captures possible), so phase 2a
        // fires and returns the combine.
        //
        // Income = 8, upkeep = Recruit(2)+Soldier(6) = 8, net = 0.
        // Post-combine upkeep = Captain(18), net = -10. Solvency needs
        // gold + 5×(-10) ≥ 0 → gold ≥ 50. 80g covers it.
        // Phase 3: no unit can afford to capture Blue Soldier — bought
        // Captain costs 30g (net -17, SurvivesNextUpkeep(50,-17) < 0).
        var grid = new HexGrid();
        for (int col = 0; col < 8; col++)
            grid.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(8, 0), Blue));
        grid.Get(HexCoord.FromOffset(8, 0))!.Occupant = new Unit(Blue, UnitLevel.Soldier);
        GameState state = BuildState(grid,
            new Player("Red", PlayerId.FromIndex(0)),
            new Player("Blue", PlayerId.FromIndex(1)));
        Territory red = state.Territories.First(t => t.Owner == Red);
        HexCoord cap = red.Capital!.Value;
        List<HexCoord> nonCap = red.Coords.Where(c => !c.Equals(cap)).Take(2).ToList();
        grid.Get(nonCap[0])!.Occupant = new Unit(Red, UnitLevel.Recruit);
        grid.Get(nonCap[1])!.Occupant = new Unit(Red, UnitLevel.Soldier);
        state.Treasury.SetGold(cap, 80);

        AiAction? result = ComputerAi.ChooseNextAction(
            state, Red, new HashSet<HexCoord>(), new Random(0));

        // Phase 2a fires: combine Recruit+Soldier → Captain (unlocks capture).
        AiMoveAction mv = Assert.IsType<AiMoveAction>(result);
        bool sourceIsRedUnit = grid.Get(mv.Source)?.Unit?.Owner == Red;
        bool destIsRedUnit = grid.Get(mv.Destination)?.Unit?.Owner == Red;
        Assert.True(sourceIsRedUnit && destIsRedUnit,
            $"expected Red-unit→Red-unit combine; got {mv.Source}→{mv.Destination}");
    }

    // -----------------------------------------------------------------------
    // Never combine into an exhausted unit (issue #132): the combined unit
    // inherits the destination's HasMovedThisTurn, so a combine into a moved
    // unit is unusable this turn while its upkeep increase bills immediately
    // — strictly worse than deferring the same combine to next turn.
    // -----------------------------------------------------------------------

    /// <summary>The CombineWhenUnlocks fixture with the Red Soldier already
    /// moved: 8-tile Red strip, unmoved Recruit + MOVED Soldier, Blue Soldier
    /// (defense 2) at (8,0), 80g. Without the moved-target filter, the
    /// Recruit→moved-Soldier combine passes the unlock filter and solvency.</summary>
    private static (GameState State, Territory Red, HexCoord RecruitCoord, HexCoord MovedSoldierCoord)
        MovedCombineTargetState()
    {
        var grid = new HexGrid();
        for (int col = 0; col < 8; col++)
            grid.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(8, 0), Blue));
        grid.Get(HexCoord.FromOffset(8, 0))!.Occupant = new Unit(Blue, UnitLevel.Soldier);
        GameState state = BuildState(grid,
            new Player("Red", PlayerId.FromIndex(0)),
            new Player("Blue", PlayerId.FromIndex(1)));
        Territory red = state.Territories.First(t => t.Owner == Red);
        HexCoord cap = red.Capital!.Value;
        List<HexCoord> nonCap = red.Coords.Where(c => !c.Equals(cap)).Take(2).ToList();
        grid.Get(nonCap[0])!.Occupant = new Unit(Red, UnitLevel.Recruit);
        grid.Get(nonCap[1])!.Occupant =
            new Unit(Red, UnitLevel.Soldier) { HasMovedThisTurn = true };
        state.Treasury.SetGold(cap, 80);
        return (state, red, nonCap[0], nonCap[1]);
    }

    [Fact]
    public void EnumeratePhase2aForUnit_SkipsCombineIntoMovedUnit()
    {
        (GameState state, Territory red, HexCoord recruitCoord, HexCoord movedSoldierCoord) =
            MovedCombineTargetState();
        Unit recruit = state.Grid.Get(recruitCoord)!.Unit!;

        List<AiCandidate> candidates = AiCommon.EnumeratePhase2aForUnit(
            recruitCoord, recruit, red, state).ToList();

        Assert.DoesNotContain(candidates,
            c => c.Action is AiMoveAction mv && mv.Destination.Equals(movedSoldierCoord));
        Assert.Empty(candidates); // the moved Soldier was the only combine partner
    }

    [Fact]
    public void ChooseNextAction_NeverCombinesIntoMovedUnit()
    {
        (GameState state, Territory _, HexCoord _, HexCoord _) = MovedCombineTargetState();

        AiAction? result = ComputerAi.ChooseNextAction(
            state, Red, new HashSet<HexCoord>(), new Random(0));

        // Whatever the AI picks (or null), it must never land a combine on
        // an already-moved friendly unit.
        HexCoord? combineDest = result switch
        {
            AiMoveAction mv => mv.Destination,
            AiBuyCombineAction bc => bc.CombineTarget,
            _ => null,
        };
        if (combineDest is HexCoord dest)
        {
            Unit? destUnit = state.Grid.Get(dest)?.Unit;
            Assert.False(destUnit != null && destUnit.Owner == Red && destUnit.HasMovedThisTurn,
                $"AI combined into an exhausted unit at {dest} via {result}");
        }
    }

    [Fact]
    public void EnumeratePhase2aForUnit_LogsSkipAtTraceLevel()
    {
        // Pins the [p2a] skip instrumentation: the 6AI harness pins Ai to
        // Debug (so the line can't be observed there); this sink capture is
        // the verification that the skip path emits under Ai:Trace.
        Action<string>? savedSink = Log.Sink;
        try
        {
            Log.ResetLevels();
            var seen = new List<string>();
            Log.Sink = seen.Add;
            Log.SetLevel(Log.LogCategory.Ai, Log.LogLevel.Trace);

            (GameState state, Territory red, HexCoord recruitCoord, HexCoord _) =
                MovedCombineTargetState();
            Unit recruit = state.Grid.Get(recruitCoord)!.Unit!;
            _ = AiCommon.EnumeratePhase2aForUnit(recruitCoord, recruit, red, state).ToList();

            Assert.Contains(seen, line => line.Contains("[p2a] skip combine"));
        }
        finally
        {
            Log.Sink = savedSink;
            Log.ResetLevels();
        }
    }

    [Fact]
    public void EnumeratePhase2b_SkipsCombineIntoMovedUnit()
    {
        // Pins the existing moved-target exclusion in phase 2b: a MOVED Red
        // Soldier is the only combine partner; buying a Recruit to merge
        // (→ Captain) would pass the unlock filter and solvency, so without
        // the HasMovedThisTurn check a candidate WOULD be emitted.
        var grid = new HexGrid();
        for (int col = 0; col < 8; col++)
            grid.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(8, 0), Blue));
        grid.Get(HexCoord.FromOffset(8, 0))!.Occupant = new Unit(Blue, UnitLevel.Soldier);
        GameState state = BuildState(grid,
            new Player("Red", PlayerId.FromIndex(0)),
            new Player("Blue", PlayerId.FromIndex(1)));
        Territory red = state.Territories.First(t => t.Owner == Red);
        HexCoord cap = red.Capital!.Value;
        HexCoord soldierCoord = red.Coords.First(c => !c.Equals(cap));
        grid.Get(soldierCoord)!.Occupant =
            new Unit(Red, UnitLevel.Soldier) { HasMovedThisTurn = true };
        state.Treasury.SetGold(cap, 80);

        List<AiCandidate> candidates = AiCommon.EnumeratePhase2b(red, state).ToList();

        Assert.Empty(candidates);
    }

    [Fact]
    public void EnumeratePhase1ForUnit_ReturnsCapture_EvenWhenTerritoryBankrupt()
    {
        // A bankrupt territory (upkeep > income, 0 gold) still has a free unit
        // that can capture an adjacent undefended Blue tile. Captures don't
        // change upkeep — they can only help — so the solvency gate must NOT
        // block them.
        var grid = new HexGrid();
        for (int col = 0; col < 3; col++)
            grid.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(3, 0), Blue));
        // 3 tiles, 1 Captain (upkeep 18) → income 3, upkeep 18, net -15. Bankrupt.
        // 0g treasury. SurvivesNextUpkeep(0, -14) = 0+5×(-14) = -70 < 0.
        GameState state = BuildState(grid,
            new Player("Red", PlayerId.FromIndex(0)),
            new Player("Blue", PlayerId.FromIndex(1)));
        Territory red = state.Territories.First(t => t.Owner == Red);
        HexCoord unitCoord = HexCoord.FromOffset(2, 0);
        grid.Get(unitCoord)!.Occupant = new Unit(Red, UnitLevel.Captain);
        Unit unit = state.Grid.Get(unitCoord)!.Unit!;

        List<AiCandidate> candidates = AiCommon.EnumeratePhase1ForUnit(
            unitCoord, unit, red, state).ToList();

        Assert.Contains(candidates,
            c => c.Action is AiMoveAction mv && mv.Destination.Equals(HexCoord.FromOffset(3, 0)));
    }

    [Fact]
    public void EnumeratePhase1ForUnit_ReturnsCaptures()
    {
        // Verify the new phase-1 helper returns capture candidates for a
        // Recruit adjacent to an undefended Blue tile.
        var grid = new HexGrid();
        for (int col = 0; col < 4; col++)
            grid.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(4, 0), Blue));
        grid.Get(HexCoord.FromOffset(0, 0))!.Occupant = new Unit(Red, UnitLevel.Recruit);
        GameState state = BuildState(grid,
            new Player("Red", PlayerId.FromIndex(0)),
            new Player("Blue", PlayerId.FromIndex(1)));
        Territory red = state.Territories.First(t => t.Owner == Red);
        HexCoord unitCoord = HexCoord.FromOffset(3, 0); // rightmost Red tile, adjacent to Blue
        grid.Get(unitCoord)!.Occupant = new Unit(Red, UnitLevel.Recruit);
        // Rebuild territories after placing units.
        state = BuildState(grid,
            new Player("Red", PlayerId.FromIndex(0)),
            new Player("Blue", PlayerId.FromIndex(1)));
        red = state.Territories.First(t => t.Owner == Red);
        Unit unit = state.Grid.Get(unitCoord)!.Unit!;

        List<AiCandidate> candidates = AiCommon.EnumeratePhase1ForUnit(
            unitCoord, unit, red, state).ToList();

        Assert.Contains(candidates,
            c => c.Action is AiMoveAction mv && mv.Destination.Equals(HexCoord.FromOffset(4, 0)));
        Assert.All(candidates, c => Assert.True(
            c.Kind == AiActionKind.Capture || c.Kind == AiActionKind.Chop,
            $"phase 1 must only emit Capture/Chop/Grave; got {c.Kind}"));
    }

    [Fact]
    public void EnumeratePhase1ForUnit_ReturnsGraveClear()
    {
        // Moving onto an own grave clears it (movement-consuming) — this
        // must appear in phase 1, not phase 4b repositions.
        var grid = new HexGrid();
        for (int col = 0; col < 5; col++)
            grid.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        HexCoord graveCoord = HexCoord.FromOffset(2, 0);
        HexCoord unitCoord = HexCoord.FromOffset(1, 0);
        grid.Get(graveCoord)!.Occupant = new Grave();
        grid.Get(unitCoord)!.Occupant = new Unit(Red, UnitLevel.Recruit);
        GameState state = BuildState(grid,
            new Player("Red", PlayerId.FromIndex(0)),
            new Player("Blue", PlayerId.FromIndex(1)));
        Territory red = state.Territories.First(t => t.Owner == Red);
        Unit unit = state.Grid.Get(unitCoord)!.Unit!;

        List<AiCandidate> candidates = AiCommon.EnumeratePhase1ForUnit(
            unitCoord, unit, red, state).ToList();

        Assert.Contains(candidates,
            c => c.Action is AiMoveAction mv && mv.Destination.Equals(graveCoord));
    }

    [Fact]
    public void EnumeratePhase1ForUnit_DoesNotReturnRepositions()
    {
        // Phase 1 must not include moves to empty own-territory tiles
        // (those belong to phase 4b).
        var grid = new HexGrid();
        for (int col = 0; col < 5; col++)
            grid.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        HexCoord unitCoord = HexCoord.FromOffset(0, 0);
        grid.Get(unitCoord)!.Occupant = new Unit(Red, UnitLevel.Recruit);
        // No enemies, no trees, no graves — only empty own tiles reachable.
        GameState state = BuildState(grid,
            new Player("Red", PlayerId.FromIndex(0)),
            new Player("Blue", PlayerId.FromIndex(1)));
        Territory red = state.Territories.First(t => t.Owner == Red);
        Unit unit = state.Grid.Get(unitCoord)!.Unit!;

        List<AiCandidate> candidates = AiCommon.EnumeratePhase1ForUnit(
            unitCoord, unit, red, state).ToList();

        Assert.Empty(candidates);
    }

    [Fact]
    public void EnumeratePhase3_ReturnsBuyCaptures()
    {
        // Phase 3 returns buy-capture candidates for each affordable level
        // whose placement would land on an enemy tile.
        var grid = new HexGrid();
        for (int col = 0; col < 5; col++)
            grid.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(5, 0), Blue));
        GameState state = BuildState(grid,
            new Player("Red", PlayerId.FromIndex(0)),
            new Player("Blue", PlayerId.FromIndex(1)));
        Territory red = state.Territories.First(t => t.Owner == Red);
        state.Treasury.SetGold(red.Capital!.Value, 50);

        List<AiCandidate> candidates = AiCommon.EnumeratePhase3(red, state).ToList();

        Assert.NotEmpty(candidates);
        Assert.All(candidates, c =>
        {
            Assert.True(c.Kind == AiActionKind.Capture || c.Kind == AiActionKind.Chop,
                $"phase 3 must only emit Capture/Chop; got {c.Kind}");
            Assert.IsType<AiBuyUnitAction>(c.Action);
        });
    }

    [Fact]
    public void EnumeratePhase3_DoesNotReturnBuyRepositions()
    {
        // Phase 3 must not include buy-repositions (dropped by design).
        // Setup: Red territory with no adjacent enemies (no captures), only
        // own empty tiles reachable. Even with gold, phase 3 returns empty.
        var grid = new HexGrid();
        for (int col = 0; col < 5; col++)
            grid.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        GameState state = BuildState(grid,
            new Player("Red", PlayerId.FromIndex(0)),
            new Player("Blue", PlayerId.FromIndex(1)));
        Territory red = state.Territories.First(t => t.Owner == Red);
        state.Treasury.SetGold(red.Capital!.Value, 100);

        List<AiCandidate> candidates = AiCommon.EnumeratePhase3(red, state).ToList();

        Assert.Empty(candidates);
    }

    [Fact]
    public void UnlocksMovementConsumingTarget_TrueWhenCombineUnlocksCapture()
    {
        // Recruit(1)+Soldier(2) → Captain(3). Adjacent Blue Soldier
        // (defense 2): Recruit can't capture (1 < 2 false), Soldier
        // can't (2 < 2 false), Captain can (2 < 3 true). Unlock filter
        // must return true.
        var grid = new HexGrid();
        for (int col = 0; col < 6; col++)
            grid.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(6, 0), Blue));
        grid.Get(HexCoord.FromOffset(6, 0))!.Occupant = new Unit(Blue, UnitLevel.Soldier);
        GameState state = BuildState(grid,
            new Player("Red", PlayerId.FromIndex(0)),
            new Player("Blue", PlayerId.FromIndex(1)));
        Territory red = state.Territories.First(t => t.Owner == Red);

        bool unlocks = AiCommon.UnlocksMovementConsumingTarget(
            UnitLevel.Recruit, UnitLevel.Soldier, red, state);

        Assert.True(unlocks, "Recruit+Soldier→Captain should unlock capture of Blue Soldier tile");
    }

    [Fact]
    public void UnlocksMovementConsumingTarget_FalseWhenTargetAlreadyReachable()
    {
        // Two Recruits combine → Soldier(2). Adjacent undefended Blue tile
        // (defense 0): both Recruits could already capture it (0 < 1 true).
        // The Soldier adds no new movement-consuming targets → returns false.
        var grid = new HexGrid();
        for (int col = 0; col < 6; col++)
            grid.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(6, 0), Blue));
        GameState state = BuildState(grid,
            new Player("Red", PlayerId.FromIndex(0)),
            new Player("Blue", PlayerId.FromIndex(1)));
        Territory red = state.Territories.First(t => t.Owner == Red);

        bool unlocks = AiCommon.UnlocksMovementConsumingTarget(
            UnitLevel.Recruit, UnitLevel.Recruit, red, state);

        Assert.False(unlocks, "Recruit+Recruit→Soldier should NOT unlock when Recruits already reached the Blue tile");
    }

    // -----------------------------------------------------------------------
    // Phases 1, 2a, 2b, and 3 never decline a legal action for the status
    // quo: a free chop / unlock-combine / spend-to-attack must be taken even
    // when its score delta is <= 0. Only phase 4 (towers, defensive
    // repositions) keeps the strictly-positive gate — doing nothing is a
    // valid defensive choice. Phase 2b has no red->green test because no
    // negative-delta buy-combine state is constructible under the scorer
    // (no tile is vacated and no enemy territory is touched, so the
    // unit-value gain always beats the clamped income loss); its threshold
    // expresses intent and guards against future scorer changes.
    // -----------------------------------------------------------------------

    [Fact]
    public void ChooseNextAction_Phase1ForcesChopEvenWhenDeltaNotPositive()
    {
        // A lone Red Recruit at (0,0) is the SOLE defender (via radiation) of
        // three Red border tiles (1,0),(0,1),(-1,1), each touching a defended
        // Blue tile (Blue Soldier, defence 2 -> not capturable by a Recruit,
        // so no phase-1 capture candidate). A tree sits on interior tile
        // (0,-2), whose neighbourhood contains none of the three borders, so
        // moving the Recruit there to chop EXPOSES all three borders.
        //
        // Chop delta = +20 (tree removed) +1 (chopped tile now earns income,
        // territory solvent both states) -30 (three borders go undefended)
        // = -9 <= 0. Phase 1 must commit to the chop regardless of delta sign;
        // with no gold and no other candidates the chop is the only action.
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));    // Recruit
        grid.Add(new HexTile(new HexCoord(1, 0), Red));    // border (E -> Blue 2,0)
        grid.Add(new HexTile(new HexCoord(0, 1), Red));    // border (SE -> Blue 0,2)
        grid.Add(new HexTile(new HexCoord(-1, 1), Red));   // border (W -> Blue -2,1)
        grid.Add(new HexTile(new HexCoord(0, -1), Red));   // capital (lex-min empty)
        grid.Add(new HexTile(new HexCoord(0, -2), Red));   // tree (interior)
        grid.Get(new HexCoord(0, 0))!.Occupant = new Unit(Red, UnitLevel.Recruit);
        grid.Get(new HexCoord(0, -2))!.Occupant = new Tree();
        // Defended Blue tiles (defence 2): make our tiles borders but block
        // any Recruit capture so phase 1 has only the chop.
        grid.Add(new HexTile(new HexCoord(2, 0), Blue));
        grid.Add(new HexTile(new HexCoord(0, 2), Blue));
        grid.Add(new HexTile(new HexCoord(-2, 1), Blue));
        grid.Get(new HexCoord(2, 0))!.Occupant = new Unit(Blue, UnitLevel.Soldier);
        grid.Get(new HexCoord(0, 2))!.Occupant = new Unit(Blue, UnitLevel.Soldier);
        grid.Get(new HexCoord(-2, 1))!.Occupant = new Unit(Blue, UnitLevel.Soldier);

        GameState state = BuildState(grid,
            new Player("Red", PlayerId.FromIndex(0)),
            new Player("Blue", PlayerId.FromIndex(1)));
        // gold left at 0: no phase 2b/3/4a buys.

        AiAction? result = ComputerAi.ChooseNextAction(
            state, Red, new HashSet<HexCoord>(), new Random(0));

        AiMoveAction mv = Assert.IsType<AiMoveAction>(result);
        Assert.Equal(new HexCoord(0, 0), mv.Source);
        Assert.Equal(new HexCoord(0, -2), mv.Destination);
    }

    [Fact]
    public void ChooseNextAction_Phase2aForcesCombineEvenWhenDeltaNotPositive()
    {
        // Two Red Recruits — (0,0) and (4,0) — each the sole defender of its
        // own tile, which is a border (a defence-1 Blue Recruit sits just
        // outside: (-1,0) beside (0,0), (5,0) beside (4,0)). A Recruit
        // (defence 1) cannot capture a defence-1 tile, so PHASE 1 IS EMPTY; a
        // combined Soldier (level 2) can (1 < 2), so the unlock filter passes.
        //
        // Either combine ordering merges the two Recruits into one Soldier and
        // exposes exactly the vacated unit's border. Combine value =
        // ΔunitValue(+4) − Δnet-upkeep(−2) = +2; minus one exposed border −10
        // = −8 <= 0 for BOTH orderings, so the best phase-2a delta is <= 0.
        // Phase 2a must commit to the combine even at delta <= 0.
        var grid = new HexGrid();
        foreach (var c in new[]
        {
            new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(2, 0),
            new HexCoord(3, 0), new HexCoord(4, 0),
            new HexCoord(2, 1), new HexCoord(2, -1), // extra tiles for income + capital
        })
        {
            grid.Add(new HexTile(c, Red));
        }
        grid.Get(new HexCoord(0, 0))!.Occupant = new Unit(Red, UnitLevel.Recruit);
        grid.Get(new HexCoord(4, 0))!.Occupant = new Unit(Red, UnitLevel.Recruit);
        // Defence-1 Blue tiles just outside each Recruit's tile.
        grid.Add(new HexTile(new HexCoord(-1, 0), Blue));
        grid.Add(new HexTile(new HexCoord(5, 0), Blue));
        grid.Get(new HexCoord(-1, 0))!.Occupant = new Unit(Blue, UnitLevel.Recruit);
        grid.Get(new HexCoord(5, 0))!.Occupant = new Unit(Blue, UnitLevel.Recruit);

        GameState state = BuildState(grid,
            new Player("Red", PlayerId.FromIndex(0)),
            new Player("Blue", PlayerId.FromIndex(1)));
        // gold left at 0: no phase 2b/3/4a.

        AiAction? result = ComputerAi.ChooseNextAction(
            state, Red, new HashSet<HexCoord>(), new Random(0));

        // Phase 2a fires: a Red-unit -> Red-unit combine (delta -8).
        AiMoveAction mv = Assert.IsType<AiMoveAction>(result);
        bool sourceIsRedUnit = grid.Get(mv.Source)?.Unit?.Owner == Red;
        bool destIsRedUnit = grid.Get(mv.Destination)?.Unit?.Owner == Red;
        Assert.True(sourceIsRedUnit && destIsRedUnit,
            $"expected Red-unit→Red-unit combine; got {mv.Source}→{mv.Destination}");
    }

    [Fact]
    public void ChooseNextAction_Phase3ForcesBuyCaptureEvenWhenDeltaNotPositive()
    {
        // Issue #129 analog: the only phase-3 candidate is a buy-Recruit
        // capture of a Blue SINGLETON fragment, and its delta is negative —
        // the strictly-positive gate used to decline it and the AI idled
        // with an affordable capture on the board.
        //
        // Red strip (0,0)..(3,0) — capital (0,0), tower (2,0) so border
        // (3,0) is defended in both states. Blue singleton X=(4,0). Green
        // (third player) owns X's five other neighbors; the two that touch
        // Red — (4,-1),(3,1) — carry Green Recruits (defense 1) so a Red
        // Recruit can't capture them: X is the ONLY capture target. Gold 10
        // affords exactly a Recruit (tower 15 and Soldier 20 are out).
        //
        // Delta for buy-Recruit onto X: +10 tile, +4 recruit value,
        // -1 net income (+1 tile, -2 upkeep), -12 enemy-edge exposure
        // (3 edges before, 7 after — X faces five Green tiles), +2 contested
        // defense (X at defense 1), -4 fragment forfeiture (the singleton's
        // 10+1-15=-4 value stops being subtracted) = -1. Phase 3 must commit
        // to it anyway: a spend-to-attack is never declined for the status quo.
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));    // capital (lex-min empty)
        grid.Add(new HexTile(new HexCoord(1, 0), Red));
        grid.Add(new HexTile(new HexCoord(2, 0), Red));    // tower
        grid.Add(new HexTile(new HexCoord(3, 0), Red));    // border
        grid.Add(new HexTile(new HexCoord(4, 0), Blue));   // X — the singleton
        PlayerId green = PlayerId.FromIndex(2);
        grid.Add(new HexTile(new HexCoord(5, 0), green));
        grid.Add(new HexTile(new HexCoord(4, 1), green));
        grid.Add(new HexTile(new HexCoord(5, -1), green));
        grid.Add(new HexTile(new HexCoord(4, -1), green)); // touches Red (3,0)
        grid.Add(new HexTile(new HexCoord(3, 1), green));  // touches Red (3,0)
        grid.Get(new HexCoord(2, 0))!.Occupant = new Tower();
        grid.Get(new HexCoord(4, -1))!.Occupant = new Unit(green, UnitLevel.Recruit);
        grid.Get(new HexCoord(3, 1))!.Occupant = new Unit(green, UnitLevel.Recruit);

        GameState state = BuildState(grid,
            new Player("Red", PlayerId.FromIndex(0)),
            new Player("Blue", PlayerId.FromIndex(1)),
            new Player("Green", green));
        Territory red = state.Territories.First(t => t.Owner == Red);
        state.Treasury.SetGold(red.Capital!.Value, 10);

        // Premise guards: X's capture is the sole phase-3 candidate, and its
        // simulated delta is <= 0 (otherwise this test isn't exercising the
        // gate at all and the fixture has drifted).
        List<AiCandidate> p3 = AiCommon.EnumeratePhase3(red, state).ToList();
        AiCandidate only = Assert.Single(p3);
        AiBuyUnitAction buy = Assert.IsType<AiBuyUnitAction>(only.Action);
        Assert.Equal(new HexCoord(4, 0), buy.Destination);
        int baseScore = AiStateScorer.Score(state, Red);
        GameState clone = AiSimulator.Clone(state);
        AiSimulator.Apply(only.Action, clone);
        int delta = AiStateScorer.Score(clone, Red) - baseScore;
        Assert.True(delta <= 0,
            $"fixture drift: expected a non-positive phase-3 delta, got {delta}");

        AiAction? result = ComputerAi.ChooseNextAction(
            state, Red, new HashSet<HexCoord>(), new Random(0));

        AiBuyUnitAction chosen = Assert.IsType<AiBuyUnitAction>(result);
        Assert.Equal(new HexCoord(4, 0), chosen.Destination);
        Assert.Equal(UnitLevel.Recruit, chosen.Level);
    }

    // -----------------------------------------------------------------------
    // Weakest-first capture phases (issue #130): the offensive phases (1, 3)
    // iterate tiers weakest→strongest so the cheapest sufficient unit takes
    // each tile, restarting from the weakest tier after every action.
    // -----------------------------------------------------------------------

    [Fact]
    public void ChooseNextAction_Phase1_WeakestUnitTakesCapture()
    {
        // Red strip with an unmoved Commander AND Recruit; one undefended
        // Blue singleton adjacent to the territory. Both units can reach it
        // (capture targets are territory-adjacency based), so phase 1 must
        // spend the Recruit, not waste the Commander.
        var grid = new HexGrid();
        for (int col = 0; col < 5; col++)
            grid.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(5, 0), Blue)); // undefended
        HexCoord commanderCoord = HexCoord.FromOffset(1, 0);
        HexCoord recruitCoord = HexCoord.FromOffset(2, 0);
        grid.Get(commanderCoord)!.Occupant = new Unit(Red, UnitLevel.Commander);
        grid.Get(recruitCoord)!.Occupant = new Unit(Red, UnitLevel.Recruit);
        GameState state = BuildState(grid,
            new Player("Red", PlayerId.FromIndex(0)),
            new Player("Blue", PlayerId.FromIndex(1)));

        AiAction? result = ComputerAi.ChooseNextAction(
            state, Red, new HashSet<HexCoord>(), new Random(0));

        AiMoveAction mv = Assert.IsType<AiMoveAction>(result);
        Assert.Equal(recruitCoord, mv.Source);
        Assert.Equal(HexCoord.FromOffset(5, 0), mv.Destination);
    }

    [Fact]
    public void ChooseNextAction_Phase1_CapturesLowestTierFirstAcrossTurn()
    {
        // Recruit + Soldier; two capture targets: an undefended Blue
        // singleton at (5,0) and a defense-1 Blue tile at (-1,0) (a Blue
        // Recruit stands on it) that only the Soldier can take. The turn
        // must unfold weakest-first with a restart after the capture:
        // call 1 → Recruit takes (5,0); call 2 → Soldier takes (-1,0).
        var grid = new HexGrid();
        for (int col = 0; col < 5; col++)
            grid.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(5, 0), Blue));  // undefended
        grid.Add(new HexTile(HexCoord.FromOffset(-1, 0), Blue)); // defense 1
        grid.Get(HexCoord.FromOffset(-1, 0))!.Occupant = new Unit(Blue, UnitLevel.Recruit);
        HexCoord recruitCoord = HexCoord.FromOffset(1, 0);
        HexCoord soldierCoord = HexCoord.FromOffset(2, 0);
        grid.Get(recruitCoord)!.Occupant = new Unit(Red, UnitLevel.Recruit);
        grid.Get(soldierCoord)!.Occupant = new Unit(Red, UnitLevel.Soldier);
        GameState state = BuildState(grid,
            new Player("Red", PlayerId.FromIndex(0)),
            new Player("Blue", PlayerId.FromIndex(1)));
        var visited = new HashSet<HexCoord>();

        AiAction? first = ComputerAi.ChooseNextAction(state, Red, visited, new Random(0));

        AiMoveAction firstMv = Assert.IsType<AiMoveAction>(first);
        Assert.Equal(recruitCoord, firstMv.Source);
        Assert.Equal(HexCoord.FromOffset(5, 0), firstMv.Destination);

        // Apply the capture and restart — mirrors AiTurnDriver's loop.
        AiSimulator.Apply(first!, state);

        AiAction? second = ComputerAi.ChooseNextAction(state, Red, visited, new Random(0));

        AiMoveAction secondMv = Assert.IsType<AiMoveAction>(second);
        Assert.Equal(soldierCoord, secondMv.Source);
        Assert.Equal(HexCoord.FromOffset(-1, 0), secondMv.Destination);
    }

    [Fact]
    public void ChooseNextAction_Phase3_BuysCheapestSufficientTierFirst()
    {
        // No Red units, gold for a Recruit or a Soldier. Two buy-capture
        // targets: a plain undefended Blue singleton at (5,0)
        // (Recruit-capturable) and a defense-1 Blue GOLD tile at (-1,0)
        // (Blue Recruit on it — needs a Soldier) whose capture out-scores
        // the plain one. Phase 3 must commit to the cheapest sufficient
        // tier — the Recruit buy — and leave the Soldier buy for the
        // restarted next call, instead of picking the best delta across
        // all tiers.
        var grid = new HexGrid();
        for (int col = 0; col < 5; col++)
            grid.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(5, 0), Blue));  // undefended, plain
        grid.Add(new HexTile(HexCoord.FromOffset(-1, 0), Blue)); // defense 1, gold
        grid.Get(HexCoord.FromOffset(-1, 0))!.IsGold = true;
        grid.Get(HexCoord.FromOffset(-1, 0))!.Occupant = new Unit(Blue, UnitLevel.Recruit);
        GameState state = BuildState(grid,
            new Player("Red", PlayerId.FromIndex(0)),
            new Player("Blue", PlayerId.FromIndex(1)));
        Territory red = state.Territories.First(t => t.Owner == Red);
        state.Treasury.SetGold(red.Capital!.Value, 20); // Recruit(10) + Soldier(20) affordable

        // Premise guard: the Soldier buy-capture of the gold tile must
        // out-delta the Recruit buy-capture of the plain tile, otherwise
        // this fixture isn't exercising tier priority over score at all.
        int baseScore = AiStateScorer.Score(state, Red);
        int DeltaOf(AiAction action)
        {
            GameState clone = AiSimulator.Clone(state);
            AiSimulator.Apply(action, clone);
            return AiStateScorer.Score(clone, Red) - baseScore;
        }
        int recruitDelta = DeltaOf(new AiBuyUnitAction(
            red.Capital!.Value, HexCoord.FromOffset(5, 0), UnitLevel.Recruit));
        int soldierDelta = DeltaOf(new AiBuyUnitAction(
            red.Capital!.Value, HexCoord.FromOffset(-1, 0), UnitLevel.Soldier));
        Assert.True(soldierDelta > recruitDelta,
            $"fixture drift: soldier-buy delta ({soldierDelta}) must exceed recruit-buy delta ({recruitDelta})");

        AiAction? result = ComputerAi.ChooseNextAction(
            state, Red, new HashSet<HexCoord>(), new Random(0));

        AiBuyUnitAction buy = Assert.IsType<AiBuyUnitAction>(result);
        Assert.Equal(UnitLevel.Recruit, buy.Level);
        Assert.Equal(HexCoord.FromOffset(5, 0), buy.Destination);
    }

    [Fact]
    public void EnumeratePhase3ForLevel_YieldsOnlyThatLevelsAffordableCaptures()
    {
        // Same board as the tier-priority test. Per-level enumeration:
        // the Recruit tier reaches only the undefended tile; the Soldier
        // tier reaches both (no cross-tier "covered" dedup at this layer —
        // the sequential tier walk in ChooseNextAction subsumes it). An
        // unaffordable tier yields nothing.
        var grid = new HexGrid();
        for (int col = 0; col < 5; col++)
            grid.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(5, 0), Blue));
        grid.Add(new HexTile(HexCoord.FromOffset(-1, 0), Blue));
        grid.Get(HexCoord.FromOffset(-1, 0))!.Occupant = new Unit(Blue, UnitLevel.Recruit);
        GameState state = BuildState(grid,
            new Player("Red", PlayerId.FromIndex(0)),
            new Player("Blue", PlayerId.FromIndex(1)));
        Territory red = state.Territories.First(t => t.Owner == Red);
        state.Treasury.SetGold(red.Capital!.Value, 20);

        List<AiCandidate> recruitTier =
            AiCommon.EnumeratePhase3ForLevel(red, state, UnitLevel.Recruit).ToList();
        List<AiCandidate> soldierTier =
            AiCommon.EnumeratePhase3ForLevel(red, state, UnitLevel.Soldier).ToList();
        List<AiCandidate> commanderTier =
            AiCommon.EnumeratePhase3ForLevel(red, state, UnitLevel.Commander).ToList();

        AiCandidate recruitOnly = Assert.Single(recruitTier);
        Assert.Equal(HexCoord.FromOffset(5, 0),
            Assert.IsType<AiBuyUnitAction>(recruitOnly.Action).Destination);
        Assert.Equal(2, soldierTier.Count);
        Assert.All(soldierTier, c =>
            Assert.Equal(UnitLevel.Soldier, Assert.IsType<AiBuyUnitAction>(c.Action).Level));
        Assert.Empty(commanderTier); // 40g > 20g treasury
    }

    // --- Rising Tides evacuation -----------------------------------------

    // A solid 5x3 Red block (one capital, no enemies/trees/graves), with a
    // Soldier on a doomed shore corner and PendingTide marking that corner.
    private static (GameState State, HexCoord Doomed, HexCoord Interior) DoomedUnitState(
        bool markDoomed)
    {
        HexGrid grid = TestHelpers.BuildRectGrid(5, 3, Red);
        HexCoord doomed = HexCoord.FromOffset(0, 0); // a shore (border) corner
        grid.Get(doomed)!.Occupant = new Unit(Red, UnitLevel.Soldier);
        GameState state = BuildState(
            grid, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));
        if (markDoomed)
        {
            state.PendingTide = new List<TideStep> { new TideStep(doomed, DemoteOnly: false) };
        }
        // A genuinely interior empty Red tile (all six neighbours Red).
        HexCoord interior = grid.Tiles
            .First(t => t.Owner == Red && t.Occupant == null
                        && !AiCommon.IsBorderTile(t.Coord, grid, Red)).Coord;
        return (state, doomed, interior);
    }

    [Fact]
    public void EvacuationBonus_EscapingMove_ReturnsUnitValue()
    {
        (GameState state, HexCoord doomed, HexCoord interior) = DoomedUnitState(markDoomed: true);

        // Soldier is worth 12; fleeing the doomed tile saves exactly that.
        Assert.Equal(12, AiStateScorer.EvacuationBonus(
            new AiMoveAction(doomed, interior), state, Red));
    }

    [Fact]
    public void EvacuationBonus_NonEscapingOrIntoDoom_ReturnsZero()
    {
        (GameState state, HexCoord doomed, HexCoord interior) = DoomedUnitState(markDoomed: true);
        HexCoord otherSafe = HexCoord.FromOffset(4, 2);

        // Source not doomed → no bonus.
        Assert.Equal(0, AiStateScorer.EvacuationBonus(
            new AiMoveAction(interior, otherSafe), state, Red));
        // Destination is itself doomed → fleeing into the sea earns nothing.
        state.PendingTide = new List<TideStep>
        {
            new TideStep(doomed, DemoteOnly: false),
            new TideStep(otherSafe, DemoteOnly: false),
        };
        Assert.Equal(0, AiStateScorer.EvacuationBonus(
            new AiMoveAction(doomed, otherSafe), state, Red));
    }

    [Fact]
    public void EnumeratePhase4b_DoomedUnit_OffersInteriorDestination()
    {
        (GameState state, HexCoord doomed, HexCoord interior) = DoomedUnitState(markDoomed: true);
        Territory terr = state.Territories.First(t => t.Owner == Red);
        Unit unit = state.Grid.Get(doomed)!.Unit!;

        var dests = AiCommon.EnumeratePhase4bForUnit(doomed, unit, terr, state)
            .Select(c => ((AiMoveAction)c.Action).Destination)
            .ToList();

        // The doomed unit may flee inland to a non-border interior tile.
        Assert.Contains(interior, dests);
    }

    [Fact]
    public void EnumeratePhase4b_SafeUnit_DoesNotOfferInteriorDestination()
    {
        (GameState state, HexCoord doomed, HexCoord interior) = DoomedUnitState(markDoomed: false);
        Territory terr = state.Territories.First(t => t.Owner == Red);
        Unit unit = state.Grid.Get(doomed)!.Unit!;

        var dests = AiCommon.EnumeratePhase4bForUnit(doomed, unit, terr, state)
            .Select(c => ((AiMoveAction)c.Action).Destination)
            .ToList();

        // Not doomed: the border-only rule holds — no interior tile offered.
        Assert.DoesNotContain(interior, dests);
    }

    [Fact]
    public void ChooseNextAction_DoomedStationaryUnit_Evacuates()
    {
        (GameState state, HexCoord doomed, _) = DoomedUnitState(markDoomed: true);

        AiAction? result = ComputerAi.ChooseNextAction(
            state, Red, new HashSet<HexCoord>(), new Random(0));

        // The AI moves the doomed unit off its tile to safety rather than
        // leaving it to drown at turn end.
        AiMoveAction mv = Assert.IsType<AiMoveAction>(result);
        Assert.Equal(doomed, mv.Source);
        Assert.DoesNotContain(mv.Destination, state.PendingTide.Select(s => s.Coord));
    }
}
