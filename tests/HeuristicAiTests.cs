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
        GameState stateA = BuildState(gridA, new Player("Red", Red), new Player("Blue", Blue));

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
        GameState stateB = BuildState(gridB, new Player("Red", Red), new Player("Blue", Blue));

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
            grid.Get(HexCoord.FromOffset(col, 0))!.Color = Red;
        GameState state = BuildState(grid, new Player("Red", Red), new Player("Blue", Blue));

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
            grid.Get(HexCoord.FromOffset(col, 0))!.Color = Red;
        grid.Get(HexCoord.FromOffset(5, 0))!.Occupant = new Tower();
        GameState state = BuildState(grid, new Player("Red", Red), new Player("Blue", Blue));

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
        GameState state = BuildState(grid, new Player("Red", Red), new Player("Blue", Blue));

        double bonus = AiStateScorer.BuildTowerBonus(HexCoord.FromOffset(1, 1), state, Red);

        Assert.Equal(0.0, bonus);
    }

    [Fact]
    public void ChooseNextAction_BuildsTower_OnContestedBorderWithSpareGold()
    {
        // 3x3 Red blob facing 3x3 Blue blob in a 6x3 field, no
        // units anywhere, capital lands at (1,0) by lex-min so the
        // three Red border tiles ((2,0), (2,1), (2,2)) are all
        // undefended. With exactly TowerCost gold and no units the
        // AI's only candidates are buy-reposition and build-tower;
        // build-tower's combined undefended-border savings + action
        // bonus must beat the buy-reposition alternative.
        var grid = TestHelpers.BuildRectGrid(6, 3, Blue);
        for (int col = 0; col < 3; col++)
            for (int row = 0; row < 3; row++)
                grid.Get(HexCoord.FromOffset(col, row))!.Color = Red;
        GameState state = BuildState(grid, new Player("Red", Red), new Player("Blue", Blue));
        HexCoord cap = state.Territories.First(t => t.Owner == Red).Capital!.Value;
        state.Treasury.SetGold(cap, PurchaseRules.TowerCost);

        AiAction? result = HeuristicAi.ChooseNextAction(
            state, Red, new HashSet<HexCoord>(), new Random(0));

        Assert.IsType<AiBuildTowerAction>(result);
    }

    [Fact]
    public void ChooseNextAction_TakesEnclosedEnemyCapture_DespiteSurroundingTowers()
    {
        // Regression test for the static-tower-bonus bug: the AI
        // used to refuse captures that turned own border tiles into
        // interior because each such tile lost its static tower-
        // defense bonus (~10/tile). Six Red tiles ringing a Blue
        // singleton, with one Red tile holding a Tower covering 3
        // borders, plus a Red peasant adjacent to the enclave.
        // Under the old static-bonus model the capture's delta
        // came out negative (~−5) and the AI passed; under the
        // action-bonus model it scores ~+25 and the capture goes
        // through.
        var grid = new HexGrid();
        grid.Add(new HexTile(HexCoord.FromOffset(1, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(2, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(0, 1), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(2, 1), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(1, 2), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(2, 2), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(1, 1), Blue));
        grid.Get(HexCoord.FromOffset(2, 1))!.Occupant = new Tower();
        GameState state = BuildState(grid, new Player("Red", Red), new Player("Blue", Blue));
        // Capital lands at (1,0) by lex-min over the empty Red
        // tiles, so a peasant at (0,1) is the adjacent attacker.
        grid.Get(HexCoord.FromOffset(0, 1))!.Occupant = new Unit(Red);

        AiAction? result = HeuristicAi.ChooseNextAction(
            state, Red, new HashSet<HexCoord>(), new Random(0));

        AiMoveAction move = Assert.IsType<AiMoveAction>(result);
        Assert.Equal(HexCoord.FromOffset(0, 1), move.Source);
        Assert.Equal(HexCoord.FromOffset(1, 1), move.Destination);
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
                gridCompact.Get(HexCoord.FromOffset(col, row))!.Color = Red;
        GameState compact = BuildState(gridCompact, new Player("Red", Red), new Player("Blue", Blue));

        var gridStrip = TestHelpers.BuildRectGrid(6, 3, Blue);
        for (int col = 0; col < 6; col++)
            gridStrip.Get(HexCoord.FromOffset(col, 0))!.Color = Red;
        GameState strip = BuildState(gridStrip, new Player("Red", Red), new Player("Blue", Blue));

        double gap = AiStateScorer.Score(compact, Red) - AiStateScorer.Score(strip, Red);

        Assert.True(gap >= 35,
            $"shape-quality gap {gap} too small; surface-area term must " +
            "outweigh ~half a tile for the AI to value enclosure captures");
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
        GameState state = BuildState(grid, new Player("Red", Red), new Player("Blue", Blue));

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
