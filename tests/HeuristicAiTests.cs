using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Xunit;

namespace FourExHex.Tests;

public class HeuristicAiTests
{
    private static readonly Color Red = new(1f, 0f, 0f);
    private static readonly Color Blue = new(0f, 0f, 1f);

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
        // 3-tile Red island with a peasant on (1,0). Clone, then
        // remove the peasant from the clone's tile and verify the
        // original still has it.
        var grid = new HexGrid();
        grid.Add(new HexTile(HexCoord.FromOffset(0, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(1, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(2, 0), Red));
        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = new Unit(Red);
        GameState state = BuildState(grid, new Player("Red", Red), new Player("Blue", Blue));

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
        GameState state = BuildState(grid, new Player("Red", Red), new Player("Blue", Blue));
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
        GameState stateA = BuildState(gridA, new Player("Red", Red), new Player("Blue", Blue));

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
        GameState stateB = BuildState(gridB, new Player("Red", Red), new Player("Blue", Blue));

        double scoreA = AiStateScorer.Score(stateA, Red);
        double scoreB = AiStateScorer.Score(stateB, Red);

        // Same total tile count, but one territory (A) pays the
        // fragmentation penalty once, two territories (B) pay it
        // twice → A scores higher.
        Assert.True(scoreA > scoreB,
            $"expected merged (A={scoreA}) to beat fragmented (B={scoreB})");
    }

    // --- AiStateScorer: orphaning enemies ---------------------------------

    [Fact]
    public void Score_RewardsOrphanedEnemyTerritory()
    {
        // Scenario A: Red has 3 tiles, Blue has a healthy 4-tile
        // territory with a capital and zero upkeep (no units).
        var gridA = new HexGrid();
        gridA.Add(new HexTile(HexCoord.FromOffset(0, 0), Red));
        gridA.Add(new HexTile(HexCoord.FromOffset(1, 0), Red));
        gridA.Add(new HexTile(HexCoord.FromOffset(2, 0), Red));
        for (int col = 4; col < 8; col++)
        {
            gridA.Add(new HexTile(HexCoord.FromOffset(col, 0), Blue));
        }
        GameState stateA = BuildState(gridA, new Player("Red", Red), new Player("Blue", Blue));

        // Scenario B: same Red 3-tile territory, but Blue is
        // fragmented into two singletons plus one larger
        // territory. Singletons have no capital → TerritoryFinder
        // won't give them one → CapitalReconciler won't either →
        // they're effectively dead tiles.
        var gridB = new HexGrid();
        gridB.Add(new HexTile(HexCoord.FromOffset(0, 0), Red));
        gridB.Add(new HexTile(HexCoord.FromOffset(1, 0), Red));
        gridB.Add(new HexTile(HexCoord.FromOffset(2, 0), Red));
        // Isolated Blue singleton with no Blue neighbors — red
        // tiles all around it.
        gridB.Add(new HexTile(HexCoord.FromOffset(4, 0), Blue));
        gridB.Add(new HexTile(HexCoord.FromOffset(5, 0), Red));
        gridB.Add(new HexTile(HexCoord.FromOffset(6, 0), Blue));
        gridB.Add(new HexTile(HexCoord.FromOffset(7, 0), Red));
        GameState stateB = BuildState(gridB, new Player("Red", Red), new Player("Blue", Blue));

        double scoreA = AiStateScorer.Score(stateA, Red);
        double scoreB = AiStateScorer.Score(stateB, Red);

        // Red gained tiles in B (6 vs 3) AND Blue's remaining 2
        // tiles are orphaned (singletons, no capital → bankrupt).
        // Both effects push B's score above A's.
        Assert.True(scoreB > scoreA,
            $"expected fragmented-enemy (B={scoreB}) to beat healthy-enemy (A={scoreA})");
    }

    // --- HeuristicAi: action selection ------------------------------------

    [Fact]
    public void ChooseNextAction_PrefersCaptureOverCombine()
    {
        // Two adjacent Red peasants with both a combine target
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
        GameState state = BuildState(grid, new Player("Red", Red), new Player("Blue", Blue));

        AiAction? result = HeuristicAi.ChooseNextAction(
            state, Red, new HashSet<HexCoord>(), new Random(0));

        AiMoveAction move = Assert.IsType<AiMoveAction>(result);
        HexTile? dst = state.Grid.Get(move.Destination);
        Assert.NotNull(dst);
        Assert.Equal(Blue, dst!.Color); // destination is the captured Blue tile
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
        GameState cleanState = BuildState(gridA, new Player("Red", Red), new Player("Blue", Blue));

        var gridB = new HexGrid();
        for (int col = 0; col < 4; col++)
            gridB.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        gridB.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Tree();
        GameState treedState = BuildState(gridB, new Player("Red", Red), new Player("Blue", Blue));

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
        GameState treeState = BuildState(gridTree, new Player("Red", Red), new Player("Blue", Blue));

        var gridGrave = new HexGrid();
        for (int col = 0; col < 4; col++)
            gridGrave.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        gridGrave.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Grave();
        GameState graveState = BuildState(gridGrave, new Player("Red", Red), new Player("Blue", Blue));

        double treeScore = AiStateScorer.Score(treeState, Red);
        double graveScore = AiStateScorer.Score(graveState, Red);

        Assert.Equal(treeScore, graveScore);
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
            gridRedGrave.Get(HexCoord.FromOffset(col, 0))!.Color = Red;
        gridRedGrave.Get(HexCoord.FromOffset(1, 0))!.Occupant = new Grave();
        GameState redGraveState = BuildState(gridRedGrave, new Player("Red", Red), new Player("Blue", Blue));

        var gridBlueGrave = TestHelpers.BuildRectGrid(6, 1, Blue);
        for (int col = 0; col < 3; col++)
            gridBlueGrave.Get(HexCoord.FromOffset(col, 0))!.Color = Red;
        gridBlueGrave.Get(HexCoord.FromOffset(4, 0))!.Occupant = new Grave();
        GameState blueGraveState = BuildState(gridBlueGrave, new Player("Red", Red), new Player("Blue", Blue));

        double redGraveScore = AiStateScorer.Score(redGraveState, Red);
        double blueGraveScore = AiStateScorer.Score(blueGraveState, Red);

        Assert.True(redGraveScore < blueGraveScore,
            $"expected Red grave ({redGraveScore}) to score below Blue grave ({blueGraveScore})");
    }

    [Fact]
    public void Score_AddsBonusForTowerDefendedBorderTile()
    {
        // 6 Red tiles cols 0-5 plus a Blue tile at col 6. Border
        // tile = col 5 (only Red tile adjacent to enemy). Without a
        // tower the border is undefended (penalty applies). Placing
        // a tower at col 4 covers col 5 via radiation, removes the
        // undefended-border penalty AND adds the new
        // tower-defense bonus on top — so the score gap must exceed
        // the bare penalty alone.
        var gridNoTower = new HexGrid();
        for (int col = 0; col <= 5; col++)
            gridNoTower.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        gridNoTower.Add(new HexTile(HexCoord.FromOffset(6, 0), Blue));
        GameState noTower = BuildState(gridNoTower, new Player("Red", Red), new Player("Blue", Blue));

        var gridWithTower = new HexGrid();
        for (int col = 0; col <= 5; col++)
            gridWithTower.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        gridWithTower.Add(new HexTile(HexCoord.FromOffset(6, 0), Blue));
        gridWithTower.Get(HexCoord.FromOffset(4, 0))!.Occupant = new Tower();
        GameState withTower = BuildState(gridWithTower, new Player("Red", Red), new Player("Blue", Blue));

        double scoreNoTower = AiStateScorer.Score(noTower, Red);
        double scoreWithTower = AiStateScorer.Score(withTower, Red);

        // UndefendedBorderPenalty alone is 10. Without a tower
        // bonus, the gap is exactly 10. With the bonus, it must be
        // strictly greater. Threshold 12 leaves room for the
        // constant to be tuned without false-failing this test.
        Assert.True(scoreWithTower - scoreNoTower > 12.0,
            $"expected tower-defense bonus on top of penalty avoidance; gap was {scoreWithTower - scoreNoTower}");
    }

    [Fact]
    public void Score_TowerDefenseBonus_DoesNotStack()
    {
        // Same Red strip + Blue. State A: one tower at col 4
        // covering the single border tile (col 5). State B: towers
        // at col 4 AND col 5 — both cover col 5. The bonus is per
        // border tile, not per tower, so the score must be
        // identical. Capital placement is forced to col 0 in both
        // cases (lex-min empty by (R, Q)) so no other terms drift.
        var gridA = new HexGrid();
        for (int col = 0; col <= 5; col++)
            gridA.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        gridA.Add(new HexTile(HexCoord.FromOffset(6, 0), Blue));
        gridA.Get(HexCoord.FromOffset(4, 0))!.Occupant = new Tower();
        GameState stateA = BuildState(gridA, new Player("Red", Red), new Player("Blue", Blue));

        var gridB = new HexGrid();
        for (int col = 0; col <= 5; col++)
            gridB.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        gridB.Add(new HexTile(HexCoord.FromOffset(6, 0), Blue));
        gridB.Get(HexCoord.FromOffset(4, 0))!.Occupant = new Tower();
        gridB.Get(HexCoord.FromOffset(5, 0))!.Occupant = new Tower();
        GameState stateB = BuildState(gridB, new Player("Red", Red), new Player("Blue", Blue));

        Assert.Equal(
            AiStateScorer.Score(stateA, Red),
            AiStateScorer.Score(stateB, Red));
    }

    [Fact]
    public void ChooseNextAction_PrefersChopOverCombine()
    {
        // 10-tile Red territory with two adjacent peasants and a
        // tree the peasants can reach. 10 tiles / 1 tree /
        // 2 peasants → net income 9 - 4 = 5, which is enough for a
        // P+P→S combine (upkeep delta +2) AND for a chop; both
        // are legal. With the own-tree penalty, chopping must
        // outrank combining.
        var grid = new HexGrid();
        for (int col = 0; col < 10; col++)
            grid.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Tree();
        grid.Get(HexCoord.FromOffset(0, 0))!.Occupant = new Unit(Red);
        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = new Unit(Red);
        GameState state = BuildState(grid, new Player("Red", Red), new Player("Blue", Blue));

        AiAction? result = HeuristicAi.ChooseNextAction(
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
                gridCompact.Get(HexCoord.FromOffset(col, row))!.Color = Red;
        GameState compact = BuildState(gridCompact, new Player("Red", Red), new Player("Blue", Blue));

        var gridStrip = TestHelpers.BuildRectGrid(6, 3, Blue);
        for (int col = 0; col < 6; col++)
            gridStrip.Get(HexCoord.FromOffset(col, 0))!.Color = Red;
        GameState strip = BuildState(gridStrip, new Player("Red", Red), new Player("Blue", Blue));

        double compactScore = AiStateScorer.Score(compact, Red);
        double stripScore = AiStateScorer.Score(strip, Red);

        Assert.True(compactScore > stripScore,
            $"expected compact blob ({compactScore}) to outscore strip ({stripScore})");
    }

    [Fact]
    public void Score_PenalizesUndefendedBorderTiles()
    {
        // Same 2x3 Red blob in a Blue field. In state A a peasant
        // sits on a border tile (providing defense). In state B
        // there's no unit at all → every border tile is
        // undefended. A should score strictly higher.
        var gridDefended = TestHelpers.BuildRectGrid(6, 3, Blue);
        for (int col = 0; col < 2; col++)
            for (int row = 0; row < 3; row++)
                gridDefended.Get(HexCoord.FromOffset(col, row))!.Color = Red;
        gridDefended.Get(HexCoord.FromOffset(1, 1))!.Occupant = new Unit(Red);
        GameState defended = BuildState(gridDefended, new Player("Red", Red), new Player("Blue", Blue));

        var gridUndefended = TestHelpers.BuildRectGrid(6, 3, Blue);
        for (int col = 0; col < 2; col++)
            for (int row = 0; row < 3; row++)
                gridUndefended.Get(HexCoord.FromOffset(col, row))!.Color = Red;
        GameState undefended = BuildState(gridUndefended, new Player("Red", Red), new Player("Blue", Blue));

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
        GameState state = BuildState(grid, new Player("Red", Red), new Player("Blue", Blue));

        AiAction? result = HeuristicAi.ChooseNextAction(
            state, Red, new HashSet<HexCoord>(), new Random(0));

        Assert.Null(result);
    }

    // --- Reposition: defensive moves into undefended border tiles -------

    [Fact]
    public void ChooseNextAction_PicksDefensiveReposition_ToCoverUndefendedBorder()
    {
        // Red 2x3 blob in a Blue field, no captures available
        // (peasant adjacent only to friendly tiles). Place the
        // peasant on an interior Red tile, leaving the border tiles
        // undefended. The heuristic must pick a reposition that
        // moves the peasant onto a border tile, reducing the
        // undefended-border penalty and improving its score.
        var grid = TestHelpers.BuildRectGrid(6, 3, Blue);
        for (int col = 0; col < 2; col++)
            for (int row = 0; row < 3; row++)
                grid.Get(HexCoord.FromOffset(col, row))!.Color = Red;
        // Peasant on interior Red tile (0,1) — its enemy-color
        // neighbors all sit OUTSIDE its current tile, so it gains
        // no defense by staying put.
        grid.Get(HexCoord.FromOffset(0, 1))!.Occupant = new Unit(Red);
        GameState state = BuildState(grid, new Player("Red", Red), new Player("Blue", Blue));

        AiAction? result = HeuristicAi.ChooseNextAction(
            state, Red, new HashSet<HexCoord>(), new Random(0));

        AiMoveAction move = Assert.IsType<AiMoveAction>(result);
        Assert.Equal(HexCoord.FromOffset(0, 1), move.Source);
        Assert.True(AiCommon.IsBorderTile(move.Destination, state.Grid, Red),
            $"peasant should reposition onto a border tile (got {move.Destination})");
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
        GameState state = BuildState(grid, new Player("Red", Red), new Player("Blue", Blue));
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
        grid.Get(HexCoord.FromOffset(4, 0))!.Occupant = new Unit(Blue, UnitLevel.Spearman);
        GameState state = BuildState(grid, new Player("Red", Red), new Player("Blue", Blue));
        HexCoord cap = state.Territories.First(t => t.Owner == Red).Capital!.Value;
        state.Treasury.SetGold(cap, 10);
        IReadOnlyList<Territory> beforeTerritories = state.Territories;

        var buy = new AiBuyUnitAction(cap, HexCoord.FromOffset(3, 0), UnitLevel.Peasant);
        AiSimulator.Apply(buy, state);

        Assert.Equal(10 - PurchaseRules.CostFor(UnitLevel.Peasant), state.Treasury.GetGold(cap));
        Unit placed = Assert.IsType<Unit>(state.Grid.Get(HexCoord.FromOffset(3, 0))!.Occupant);
        Assert.Equal(Red, placed.Owner);
        Assert.Equal(UnitLevel.Peasant, placed.Level);
        Assert.True(placed.HasMovedThisTurn);
        Assert.Same(beforeTerritories, state.Territories);
    }

    [Fact]
    public void HeuristicAi_DoesNotPingPongRepositions()
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
                grid.Get(HexCoord.FromOffset(col, row))!.Color = Red;
        grid.Get(HexCoord.FromOffset(0, 1))!.Occupant = new Unit(Red);
        GameState state = BuildState(grid, new Player("Red", Red), new Player("Blue", Blue));

        AiAction? first = HeuristicAi.ChooseNextAction(
            state, Red, new HashSet<HexCoord>(), new Random(0));
        AiMoveAction firstMove = Assert.IsType<AiMoveAction>(first);
        AiSimulator.Apply(firstMove, state);

        AiAction? second = HeuristicAi.ChooseNextAction(
            state, Red, new HashSet<HexCoord>(), new Random(0));
        if (second is AiMoveAction secondMove)
        {
            Assert.NotEqual(firstMove.Destination, secondMove.Source);
        }
    }
}
