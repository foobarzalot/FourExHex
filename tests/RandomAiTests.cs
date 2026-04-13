using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Xunit;

namespace FourExHex.Tests;

public class RandomAiTests
{
    private static readonly Color Red = new Color(1f, 0f, 0f);
    private static readonly Color Blue = new Color(0f, 0f, 1f);

    /// <summary>
    /// Build a GameState from a rect grid, overlaying Red tiles via
    /// <paramref name="redCoords"/>. Turns are initialized with both
    /// players; Red is the starting player. Treasury is empty by
    /// default — tests that need gold call SetGold themselves.
    /// </summary>
    private static GameState BuildState(int cols, int rows, params HexCoord[] redCoords)
    {
        var grid = TestHelpers.BuildRectGrid(cols, rows, Blue);
        foreach (HexCoord c in redCoords)
        {
            HexTile? tile = grid.Get(c);
            if (tile != null) tile.Color = Red;
        }
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var players = new List<Player> { new("Red", Red), new("Blue", Blue) };
        return new GameState(grid, territories, players, new TurnState(players), new Treasury());
    }

    private static Territory RedTerritory(GameState state) =>
        state.Territories.First(t => t.Owner == Red);

    private static HashSet<HexCoord> EmptyVisited() => new();

    private static Random Seed(int s = 42) => new(s);

    // --- Basic empty / no-op cases --------------------------------------

    [Fact]
    public void ChooseNextAction_NoOwnedTerritories_ReturnsNull()
    {
        // Red has no tiles on this grid.
        var grid = TestHelpers.BuildRectGrid(3, 1, Blue);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var players = new List<Player> { new("Red", Red), new("Blue", Blue) };
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());

        AiAction? result = RandomAi.ChooseNextAction(state, Red, EmptyVisited(), Seed());

        Assert.Null(result);
    }

    [Fact]
    public void ChooseNextAction_SingletonOnly_ReturnsNull()
    {
        // Singletons have no capital and are never visited by the AI.
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Add(new HexTile(new HexCoord(5, 5), Blue));
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var players = new List<Player> { new("Red", Red), new("Blue", Blue) };
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());

        AiAction? result = RandomAi.ChooseNextAction(state, Red, EmptyVisited(), Seed());

        Assert.Null(result);
    }

    // --- Move-capture ----------------------------------------------------

    [Fact]
    public void ChooseNextAction_UnitCanCaptureEmptyEnemy_ReturnsMoveAction()
    {
        // Red at (0,1),(1,1). Unit on (1,1). Several Blue tiles are
        // adjacent and capturable by a peasant. The AI picks one at
        // random, so assert only that it's a Move from (1,1) to a
        // Blue tile that ValidTargets would accept.
        GameState state = BuildState(5, 2, HexCoord.FromOffset(0, 1), HexCoord.FromOffset(1, 1));
        state.Grid.Get(HexCoord.FromOffset(1, 1))!.Occupant = new Unit(Red);
        List<HexCoord> expectedTargets = MovementRules.ValidTargets(
            UnitLevel.Peasant, RedTerritory(state), state.Grid, state.Territories);

        AiAction? result = RandomAi.ChooseNextAction(state, Red, EmptyVisited(), Seed());

        var move = Assert.IsType<AiMoveAction>(result);
        Assert.Equal(HexCoord.FromOffset(1, 1), move.Source);
        Assert.Contains(move.Destination, expectedTargets);
        // And specifically, the destination must be an enemy tile
        // (the AI only emits capture moves from units, not repositions).
        Assert.NotEqual(Red, state.Grid.Get(move.Destination)!.Color);
    }

    [Fact]
    public void ChooseNextAction_UnitAlreadyMoved_NotReturned()
    {
        GameState state = BuildState(5, 2, HexCoord.FromOffset(0, 1), HexCoord.FromOffset(1, 1));
        state.Grid.Get(HexCoord.FromOffset(1, 1))!.Occupant = new Unit(Red) { HasMovedThisTurn = true };

        AiAction? result = RandomAi.ChooseNextAction(state, Red, EmptyVisited(), Seed());

        // Unit can't move; no buy option either (empty treasury); skip entirely.
        Assert.Null(result);
    }

    [Fact]
    public void ChooseNextAction_MoveCaptureFromNegativeOne_Recovers_Allowed()
    {
        // 3-tile Red territory (income 3) with a spearman (upkeep 6) →
        // net = -3. Adding +1 via capture lifts to -2. Per the rule
        // "current_net >= -1" this is BLOCKED. Let me rebuild with
        // current_net exactly -1 so the +1 lift is exactly break-even.
        // Red: 5 tiles, one spearman (upkeep 6). Net = 5 - 6 = -1.
        GameState state = BuildState(
            8, 2,
            HexCoord.FromOffset(0, 1),
            HexCoord.FromOffset(1, 1),
            HexCoord.FromOffset(2, 1),
            HexCoord.FromOffset(3, 1),
            HexCoord.FromOffset(4, 1));
        state.Grid.Get(HexCoord.FromOffset(4, 1))!.Occupant =
            new Unit(Red, UnitLevel.Spearman);

        AiAction? result = RandomAi.ChooseNextAction(state, Red, EmptyVisited(), Seed());

        // Spearman can capture (5,0) or (5,1). Post-net = -1 + 1 = 0. Allowed.
        Assert.NotNull(result);
        Assert.IsType<AiMoveAction>(result);
    }

    [Fact]
    public void ChooseNextAction_MoveCaptureFromMinusTwoNet_Blocked()
    {
        // Red: 4 tiles, one spearman (upkeep 6). Net = 4 - 6 = -2.
        // Post-capture net = -2 + 1 = -1. Rule "post_net >= 0" blocks.
        GameState state = BuildState(
            8, 2,
            HexCoord.FromOffset(0, 1),
            HexCoord.FromOffset(1, 1),
            HexCoord.FromOffset(2, 1),
            HexCoord.FromOffset(3, 1));
        state.Grid.Get(HexCoord.FromOffset(3, 1))!.Occupant =
            new Unit(Red, UnitLevel.Spearman);

        AiAction? result = RandomAi.ChooseNextAction(state, Red, EmptyVisited(), Seed());

        Assert.Null(result);
    }

    // --- Buy-capture -----------------------------------------------------

    [Fact]
    public void ChooseNextAction_BuyCaptureAffordableAndProfitable_Returned()
    {
        // Red: 2 tiles, no units. Net = 2. Post-buy-capture net = 2 + 1 - 2 = 1 ≥ 0.
        // Seed the treasury with exactly 10g — enough for a peasant but
        // NOT for a tower (15g). That leaves buy-capture as the only
        // possible action type, so the random pick is forced.
        GameState state = BuildState(5, 2, HexCoord.FromOffset(0, 1), HexCoord.FromOffset(1, 1));
        HexCoord cap = RedTerritory(state).Capital!.Value;
        state.Treasury.SetGold(cap, 10);

        AiAction? result = RandomAi.ChooseNextAction(state, Red, EmptyVisited(), Seed());

        var buy = Assert.IsType<AiBuyUnitAction>(result);
        Assert.Equal(cap, buy.Capital);
    }

    [Fact]
    public void ChooseNextAction_BuyCaptureUnaffordable_NotReturned()
    {
        GameState state = BuildState(5, 2, HexCoord.FromOffset(0, 1), HexCoord.FromOffset(1, 1));
        HexCoord cap = RedTerritory(state).Capital!.Value;
        state.Treasury.SetGold(cap, 5); // < 10g

        AiAction? result = RandomAi.ChooseNextAction(state, Red, EmptyVisited(), Seed());

        Assert.Null(result);
    }

    [Fact]
    public void ChooseNextAction_BuyCaptureNetZero_NotReturned()
    {
        // Red: 2 tiles, one peasant (upkeep 2). Net = 2 - 2 = 0.
        // Post-buy-capture net = 0 - 1 = -1. Buy blocked.
        // The existing peasant also can't capture anything capturable because
        // we'll put it deep inside a safe test fixture — wait, it CAN capture
        // via move. We want to test ONLY the buy-capture path is blocked.
        // To isolate the buy path: give the peasant HasMovedThisTurn=true so
        // move-capture isn't a candidate.
        GameState state = BuildState(5, 2, HexCoord.FromOffset(0, 1), HexCoord.FromOffset(1, 1));
        HexCoord cap = RedTerritory(state).Capital!.Value;
        state.Treasury.SetGold(cap, 100); // plenty of gold
        // Place a peasant (upkeep 2) in the territory so net = 0.
        state.Grid.Get(HexCoord.FromOffset(1, 1))!.Occupant =
            new Unit(Red) { HasMovedThisTurn = true };

        AiAction? result = RandomAi.ChooseNextAction(state, Red, EmptyVisited(), Seed());

        // Peasant is moved; buy blocked by net rule. No actions.
        Assert.Null(result);
    }

    [Fact]
    public void ChooseNextAction_BuyCaptureNetOne_Returned()
    {
        // Red: 3 tiles, one peasant (upkeep 2). Net = 3 - 2 = 1.
        // Post-buy-capture net = 1 - 1 = 0 ≥ 0. Allowed.
        GameState state = BuildState(
            6, 2,
            HexCoord.FromOffset(0, 1),
            HexCoord.FromOffset(1, 1),
            HexCoord.FromOffset(2, 1));
        HexCoord cap = RedTerritory(state).Capital!.Value;
        state.Treasury.SetGold(cap, 100);
        state.Grid.Get(HexCoord.FromOffset(2, 1))!.Occupant =
            new Unit(Red) { HasMovedThisTurn = true };

        AiAction? result = RandomAi.ChooseNextAction(state, Red, EmptyVisited(), Seed());

        // Peasant is moved; only option is buy-capture. Allowed here.
        Assert.NotNull(result);
        Assert.IsType<AiBuyUnitAction>(result);
    }

    // --- Move chop + buy chop -------------------------------------------

    [Fact]
    public void ChooseNextAction_MoveChop_OwnTreeTile_Returned()
    {
        // Red territory with a tree tile and a peasant. Only action is
        // move-chop (no adjacent capturable enemy tiles — unit is at (0,1)
        // surrounded by Red tiles on (1,1), (2,1) and trees ahead).
        // Actually let me simplify: a 3-tile all-red row with a tree on
        // the middle tile and a unit on the end. The unit's only valid
        // move is onto the tree (reposition-to-empty is filtered out by
        // the AI's "must capture or chop" rule).
        // But wait — there may also be an enemy tile adjacent. To isolate
        // move-chop, use a small isolated fixture.
        var grid = new HexGrid();
        grid.Add(new HexTile(HexCoord.FromOffset(0, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(1, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(2, 0), Red));
        // No enemy tiles at all. Isolated Red island.
        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = new Tree();
        grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Unit(Red);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var players = new List<Player> { new("Red", Red), new("Blue", Blue) };
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());

        AiAction? result = RandomAi.ChooseNextAction(state, Red, EmptyVisited(), Seed());

        var move = Assert.IsType<AiMoveAction>(result);
        Assert.Equal(HexCoord.FromOffset(2, 0), move.Source);
        Assert.Equal(HexCoord.FromOffset(1, 0), move.Destination);
    }

    [Fact]
    public void ChooseNextAction_BuyChop_OwnTreeTile_Returned()
    {
        // Red 3-tile isolated island with a tree on the middle and NO
        // units. Only action should be buy-chop (no enemy tiles to
        // capture). Note: with a 3-tile territory and one tree, income
        // is 2 and upkeep is 0 → current net 2. Post-buy-chop net =
        // (2 + 1) - (0 + 2) = 1 ≥ 0. Allowed.
        var grid = new HexGrid();
        grid.Add(new HexTile(HexCoord.FromOffset(0, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(1, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(2, 0), Red));
        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = new Tree();
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var players = new List<Player> { new("Red", Red), new("Blue", Blue) };
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        HexCoord cap = state.Territories.First(t => t.Owner == Red).Capital!.Value;
        // Exactly 10g — affordable for peasant, NOT for tower. Forces
        // the AI's pool down to just the buy-chop action.
        state.Treasury.SetGold(cap, 10);

        AiAction? result = RandomAi.ChooseNextAction(state, Red, EmptyVisited(), Seed());

        var buy = Assert.IsType<AiBuyUnitAction>(result);
        Assert.Equal(HexCoord.FromOffset(1, 0), buy.Destination);
    }

    // --- Build tower ----------------------------------------------------

    [Fact]
    public void ChooseNextAction_BuildTower_AffordableAndProfitable_Returned()
    {
        // Red 3-tile isolated island, 20g, no units, no enemies.
        // Only available action: build tower on an empty tile.
        var grid = new HexGrid();
        grid.Add(new HexTile(HexCoord.FromOffset(0, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(1, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(2, 0), Red));
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var players = new List<Player> { new("Red", Red), new("Blue", Blue) };
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        HexCoord cap = state.Territories.First(t => t.Owner == Red).Capital!.Value;
        state.Treasury.SetGold(cap, 20);

        AiAction? result = RandomAi.ChooseNextAction(state, Red, EmptyVisited(), Seed());

        Assert.IsType<AiBuildTowerAction>(result);
    }

    [Fact]
    public void ChooseNextAction_BuildTower_Unaffordable_NotReturned()
    {
        // Same fixture but only 14g (below tower cost).
        var grid = new HexGrid();
        grid.Add(new HexTile(HexCoord.FromOffset(0, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(1, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(2, 0), Red));
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var players = new List<Player> { new("Red", Red), new("Blue", Blue) };
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        HexCoord cap = state.Territories.First(t => t.Owner == Red).Capital!.Value;
        state.Treasury.SetGold(cap, 14);

        // 14g can buy a peasant (10g), but there's nothing to capture or
        // chop on this isolated island. No valid actions.
        AiAction? result = RandomAi.ChooseNextAction(state, Red, EmptyVisited(), Seed());

        Assert.Null(result);
    }

    [Fact]
    public void ChooseNextAction_BuildTower_NetNegative_NotReturned()
    {
        // Red: 2 tiles, one spearman (upkeep 6). Net = 2 - 6 = -4 < 0.
        // Build-tower rule requires current_net >= 0 → blocked.
        var grid = new HexGrid();
        grid.Add(new HexTile(HexCoord.FromOffset(0, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(1, 0), Red));
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var players = new List<Player> { new("Red", Red), new("Blue", Blue) };
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        HexCoord cap = state.Territories.First(t => t.Owner == Red).Capital!.Value;
        state.Treasury.SetGold(cap, 100);
        state.Grid.Get(HexCoord.FromOffset(1, 0))!.Occupant =
            new Unit(Red, UnitLevel.Spearman) { HasMovedThisTurn = true };

        AiAction? result = RandomAi.ChooseNextAction(state, Red, EmptyVisited(), Seed());

        // Spearman is moved; buy blocked by net; tower blocked by net. Null.
        Assert.Null(result);
    }

    [Fact]
    public void ChooseNextAction_BuildTower_NoEmptyOwnTile_NotReturned()
    {
        // 2-tile Red island where both tiles are occupied (capital +
        // a moved peasant) → no empty tile → no tower placement.
        var grid = new HexGrid();
        grid.Add(new HexTile(HexCoord.FromOffset(0, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(1, 0), Red));
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var players = new List<Player> { new("Red", Red), new("Blue", Blue) };
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        HexCoord cap = state.Territories.First(t => t.Owner == Red).Capital!.Value;
        state.Treasury.SetGold(cap, 100);
        // Place a moved peasant on the non-capital tile.
        HexCoord other = cap == HexCoord.FromOffset(0, 0)
            ? HexCoord.FromOffset(1, 0)
            : HexCoord.FromOffset(0, 0);
        state.Grid.Get(other)!.Occupant = new Unit(Red) { HasMovedThisTurn = true };

        AiAction? result = RandomAi.ChooseNextAction(state, Red, EmptyVisited(), Seed());

        // No capture target (isolated), no tree, no empty tile for tower.
        // Buy is also blocked because there's no valid placement:
        // PlaceNew would have nowhere to put the peasant (all tiles
        // occupied by capital or a combinable unit — combines are
        // excluded by the AI rule). Null expected.
        Assert.Null(result);
    }

    // --- Filtering out non-capturing non-chopping actions ----------------

    [Fact]
    public void ChooseNextAction_PureReposition_NotReturned()
    {
        // Isolated 3-tile Red territory with a unit and an empty
        // reposition target (not a tree). No capture, no chop, no buy
        // (no gold). Expect null.
        var grid = new HexGrid();
        grid.Add(new HexTile(HexCoord.FromOffset(0, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(1, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(2, 0), Red));
        grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Unit(Red);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var players = new List<Player> { new("Red", Red), new("Blue", Blue) };
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());

        AiAction? result = RandomAi.ChooseNextAction(state, Red, EmptyVisited(), Seed());

        Assert.Null(result);
    }

    [Fact]
    public void ChooseNextAction_CombineWithFriendly_NetInsufficient_NotReturned()
    {
        // Two adjacent peasants on a 3-tile island. Income = 3,
        // upkeep = 4, net = -1. A P+P→Spearman combine bumps upkeep
        // by +2 (4 → 6), so it needs net >= 2 to stay solvent. Not
        // satisfied → combine is blocked. No captures/chops either,
        // so the AI returns null.
        var grid = new HexGrid();
        grid.Add(new HexTile(HexCoord.FromOffset(0, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(1, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(2, 0), Red));
        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = new Unit(Red);
        grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Unit(Red);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var players = new List<Player> { new("Red", Red), new("Blue", Blue) };
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());

        AiAction? result = RandomAi.ChooseNextAction(state, Red, EmptyVisited(), Seed());

        Assert.Null(result);
    }

    [Fact]
    public void ChooseNextAction_CombineWithFriendly_NetSufficient_Returned()
    {
        // 6-tile isolated Red island with two adjacent peasants.
        // Income = 6, upkeep = 4, net = 2 — exactly the requirement
        // for a P+P→Spearman combine (+2 upkeep). No enemies, no
        // trees, no empty tiles to tower (empty tiles exist but no
        // gold for a tower), so the only valid action is a combine
        // move between the two peasants.
        var grid = new HexGrid();
        for (int col = 0; col < 6; col++)
        {
            grid.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        }
        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = new Unit(Red);
        grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Unit(Red);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var players = new List<Player> { new("Red", Red), new("Blue", Blue) };
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());

        AiAction? result = RandomAi.ChooseNextAction(state, Red, EmptyVisited(), Seed());

        AiMoveAction move = Assert.IsType<AiMoveAction>(result);
        // Both endpoints are Red-owned units (i.e. a combine, not a capture).
        HexTile? src = state.Grid.Get(move.Source);
        HexTile? dst = state.Grid.Get(move.Destination);
        Assert.NotNull(src?.Unit);
        Assert.Equal(Red, src!.Unit!.Owner);
        Assert.NotNull(dst?.Unit);
        Assert.Equal(Red, dst!.Unit!.Owner);
    }

    [Fact]
    public void ChooseNextAction_CombinePeasantIntoSpearman_NetSufficient_Returned()
    {
        // Isolated Red island with a Peasant and a Spearman adjacent
        // to each other. A P+S→Knight combine changes upkeep from
        // (2 + 6) = 8 to 18, delta = +10. Net must be >= 10, so
        // income - 8 >= 10 → income >= 18 → need an 18-tile island.
        // Build a 9x2 block (18 tiles) of Red, drop the units in the
        // middle, and assert the AI returns a combine.
        var grid = new HexGrid();
        for (int row = 0; row < 2; row++)
        {
            for (int col = 0; col < 9; col++)
            {
                grid.Add(new HexTile(HexCoord.FromOffset(col, row), Red));
            }
        }
        grid.Get(HexCoord.FromOffset(0, 0))!.Occupant = new Unit(Red, UnitLevel.Peasant);
        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = new Unit(Red, UnitLevel.Spearman);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var players = new List<Player> { new("Red", Red), new("Blue", Blue) };
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());

        AiAction? result = RandomAi.ChooseNextAction(state, Red, EmptyVisited(), Seed());

        // With only the two friendly units and no enemies/trees, the
        // only move action available is a combine. (The AI may also
        // pick a tower build if it had gold — treasury is empty, so
        // it doesn't.) Expect a move action whose endpoints are both
        // Red-owned units.
        AiMoveAction move = Assert.IsType<AiMoveAction>(result);
        HexTile? src = state.Grid.Get(move.Source);
        HexTile? dst = state.Grid.Get(move.Destination);
        Assert.NotNull(src?.Unit);
        Assert.Equal(Red, src!.Unit!.Owner);
        Assert.NotNull(dst?.Unit);
        Assert.Equal(Red, dst!.Unit!.Owner);
    }

    [Fact]
    public void ChooseNextAction_CombinePeasantIntoSpearman_NetInsufficient_NotReturned()
    {
        // Same P + Spearman pair on a 17-tile island → net = 17 - 8
        // = 9 < 10 = required delta. Combine blocked by solvency,
        // no other valid actions → null.
        var grid = new HexGrid();
        int placed = 0;
        for (int row = 0; row < 2 && placed < 17; row++)
        {
            for (int col = 0; col < 9 && placed < 17; col++)
            {
                grid.Add(new HexTile(HexCoord.FromOffset(col, row), Red));
                placed++;
            }
        }
        grid.Get(HexCoord.FromOffset(0, 0))!.Occupant = new Unit(Red, UnitLevel.Peasant);
        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = new Unit(Red, UnitLevel.Spearman);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var players = new List<Player> { new("Red", Red), new("Blue", Blue) };
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());

        AiAction? result = RandomAi.ChooseNextAction(state, Red, EmptyVisited(), Seed());

        Assert.Null(result);
    }

    [Fact]
    public void ChooseNextAction_DefendedEnemyTile_NotReturned_ForPeasant()
    {
        // Red peasant adjacent to a Blue-defended Blue tile (defense 1).
        // Peasant (level 1) can't break equal defense → MovementRules
        // excludes it from ValidTargets → AI has nothing.
        GameState state = BuildState(5, 2, HexCoord.FromOffset(0, 1), HexCoord.FromOffset(1, 1));
        state.Grid.Get(HexCoord.FromOffset(1, 1))!.Occupant = new Unit(Red);
        // Place a Blue peasant on (2,1) as the defender.
        state.Grid.Get(HexCoord.FromOffset(2, 1))!.Occupant = new Unit(Blue);

        AiAction? result = RandomAi.ChooseNextAction(state, Red, EmptyVisited(), Seed());

        Assert.Null(result);
    }

    // --- Visited tracking ------------------------------------------------

    [Fact]
    public void ChooseNextAction_MarksTerritoryVisited_WhenActionReturned()
    {
        GameState state = BuildState(5, 2, HexCoord.FromOffset(0, 1), HexCoord.FromOffset(1, 1));
        state.Grid.Get(HexCoord.FromOffset(1, 1))!.Occupant = new Unit(Red);
        var visited = EmptyVisited();
        HexCoord cap = RedTerritory(state).Capital!.Value;

        AiAction? first = RandomAi.ChooseNextAction(state, Red, visited, Seed());

        Assert.NotNull(first);
        Assert.Contains(cap, visited);
    }

    [Fact]
    public void ChooseNextAction_AlreadyVisitedTerritory_Skipped()
    {
        // Single Red territory; mark it visited → expect null even though
        // there's a valid capture.
        GameState state = BuildState(5, 2, HexCoord.FromOffset(0, 1), HexCoord.FromOffset(1, 1));
        state.Grid.Get(HexCoord.FromOffset(1, 1))!.Occupant = new Unit(Red);
        HexCoord cap = RedTerritory(state).Capital!.Value;
        var visited = new HashSet<HexCoord> { cap };

        AiAction? result = RandomAi.ChooseNextAction(state, Red, visited, Seed());

        Assert.Null(result);
    }

    [Fact]
    public void ChooseNextAction_MarksTerritoryVisited_EvenWhenNoValidActions()
    {
        // Red singleton territory with nothing to do. Actually singletons
        // don't count — let me use a 2-tile territory with no unit and
        // no gold: no actions possible → territory should still be marked
        // visited so the caller terminates.
        GameState state = BuildState(5, 2, HexCoord.FromOffset(0, 1), HexCoord.FromOffset(1, 1));
        // No unit, no gold. Build tower needs 15g; peasant buy needs 10g.
        var visited = EmptyVisited();
        HexCoord cap = RedTerritory(state).Capital!.Value;

        AiAction? result = RandomAi.ChooseNextAction(state, Red, visited, Seed());

        Assert.Null(result);
        // Even though result is null, the territory was looked at and
        // should be marked visited so the caller knows not to retry.
        Assert.Contains(cap, visited);
    }

    // --- Determinism -----------------------------------------------------

    [Fact]
    public void ChooseNextAction_SameSeed_PicksSameAction()
    {
        // Set up a state with multiple valid captures and verify that
        // repeating with the same seeded RNG produces the same choice.
        GameState state = BuildState(
            5, 3,
            HexCoord.FromOffset(0, 1),
            HexCoord.FromOffset(1, 1),
            HexCoord.FromOffset(2, 1));
        state.Grid.Get(HexCoord.FromOffset(1, 1))!.Occupant = new Unit(Red, UnitLevel.Knight);

        // Multiple capturable targets: (2,0), (3,1), etc.
        AiAction? a = RandomAi.ChooseNextAction(state, Red, EmptyVisited(), new Random(123));

        // Rebuild an identical state.
        GameState state2 = BuildState(
            5, 3,
            HexCoord.FromOffset(0, 1),
            HexCoord.FromOffset(1, 1),
            HexCoord.FromOffset(2, 1));
        state2.Grid.Get(HexCoord.FromOffset(1, 1))!.Occupant = new Unit(Red, UnitLevel.Knight);
        AiAction? b = RandomAi.ChooseNextAction(state2, Red, EmptyVisited(), new Random(123));

        Assert.Equal(a, b);
    }
}
