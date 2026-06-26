using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

public class RisingTidesRulesTests
{
    private static readonly PlayerId Red = PlayerId.FromIndex(0);
    private static readonly PlayerId Blue = PlayerId.FromIndex(1);

    private static GameState MakeState(HexGrid grid, IReadOnlyList<Territory> territories)
    {
        var players = new List<Player>
        {
            new Player("Red", Red),
            new Player("Blue", Blue),
        };
        return new GameState(
            grid, territories, players, new TurnState(players), new Treasury(),
            waterCoords: null, mode: GameMode.RisingTides);
    }

    // --- ShoreTilesOf ----------------------------------------------------

    [Fact]
    public void ShoreTilesOf_InteriorTileExcluded_PerimeterIncluded()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(5, 5, Red);
        HexCoord center = HexCoord.FromOffset(2, 2);
        HexCoord corner = HexCoord.FromOffset(0, 0);

        // Precondition: the center genuinely has all six neighbours in-grid,
        // so the "fewer than 6 neighbours = shore" rule must exclude it.
        Assert.Equal(6, grid.NeighborsOf(center).Count());

        IReadOnlyList<HexCoord> shore = RisingTidesRules.ShoreTilesOf(grid, Red);

        Assert.DoesNotContain(center, shore);
        Assert.Contains(corner, shore);
    }

    [Fact]
    public void ShoreTilesOf_ReturnsOnlyOwnersTilesInAscendingOrder()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(4, 4, Blue);
        // Carve out a Red corner so the grid has two owners.
        grid.Get(HexCoord.FromOffset(0, 0))!.Owner = Red;
        grid.Get(HexCoord.FromOffset(1, 0))!.Owner = Red;

        IReadOnlyList<HexCoord> shore = RisingTidesRules.ShoreTilesOf(grid, Red);

        Assert.All(shore, c => Assert.Equal(Red, grid.Get(c)!.Owner));
        Assert.Equal(shore.OrderBy(c => c).ToList(), shore); // deterministic order
    }

    // --- SubmergeStep ----------------------------------------------------

    [Fact]
    public void SubmergeStep_NonMountainShore_RemovedFromGridAndWatered()
    {
        // An adjacent 2-tile Red territory (one capital). Both tiles are
        // shores; budget 1 sinks one, leaving a singleton — the reconciler
        // strips the now-orphaned capital, so Red becomes eliminated.
        HexGrid grid = TestHelpers.BuildRectGrid(2, 1, Red);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        Assert.False(WinConditionRules.IsEliminated(Red, grid)); // had a capital
        GameState state = MakeState(grid, territories);

        bool changed = RisingTidesRules.SubmergeStep(state, Red, new Random(1), budget: 1);

        Assert.True(changed);
        Assert.Equal(1, state.Grid.Count);
        Assert.Single(state.WaterCoords);
        Assert.True(WinConditionRules.IsEliminated(Red, state.Grid));
    }

    [Fact]
    public void SubmergeStep_NoShoreTiles_IsNoOp()
    {
        // Red owns only the fully-surrounded centre tile of a Blue board, so
        // it has no shore — nothing should erode.
        HexGrid grid = TestHelpers.BuildRectGrid(5, 5, Blue);
        HexCoord center = HexCoord.FromOffset(2, 2);
        grid.Get(center)!.Owner = Red;
        Assert.Equal(6, grid.NeighborsOf(center).Count());
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        GameState state = MakeState(grid, territories);

        bool changed = RisingTidesRules.SubmergeStep(state, Red, new Random(7), budget: 1);

        Assert.False(changed);
        Assert.Equal(25, state.Grid.Count);
        Assert.Empty(state.WaterCoords);
    }

    [Fact]
    public void SubmergeStep_MountainShore_DemotesThenSubmergesOnLaterStep()
    {
        // Red owns the interior centre (keeps it alive) plus a single shore
        // tile that is a mountain. With exactly one shore the RNG pick is
        // forced, so this is deterministic regardless of seed.
        HexGrid grid = TestHelpers.BuildRectGrid(5, 5, Blue);
        HexCoord center = HexCoord.FromOffset(2, 2);
        HexCoord shoreMtn = HexCoord.FromOffset(0, 2);
        grid.Get(center)!.Owner = Red;
        grid.Get(shoreMtn)!.Owner = Red;
        grid.Get(shoreMtn)!.IsMountain = true;
        Assert.Equal(6, grid.NeighborsOf(center).Count());
        Assert.Equal(new[] { shoreMtn }, RisingTidesRules.ShoreTilesOf(grid, Red).ToArray());
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        GameState state = MakeState(grid, territories);

        // Step 1: mountain demotes, spends the budget, nothing submerges.
        bool changed1 = RisingTidesRules.SubmergeStep(state, Red, new Random(99), budget: 1);
        Assert.True(changed1);
        Assert.Equal(25, state.Grid.Count);
        Assert.True(state.Grid.Contains(shoreMtn));
        Assert.False(state.Grid.Get(shoreMtn)!.IsMountain);
        Assert.Empty(state.WaterCoords);

        // Step 2: now a plain shore, it submerges.
        bool changed2 = RisingTidesRules.SubmergeStep(state, Red, new Random(99), budget: 1);
        Assert.True(changed2);
        Assert.Equal(24, state.Grid.Count);
        Assert.False(state.Grid.Contains(shoreMtn));
        Assert.Contains(shoreMtn, state.WaterCoords);
    }

    [Fact]
    public void SubmergeStep_SameSeed_PicksSameTile()
    {
        HexGrid GridA() => TestHelpers.BuildRectGrid(2, 2, Red);

        HexGrid gridA = GridA();
        var origCoords = gridA.Tiles.Select(t => t.Coord).ToHashSet();
        GameState stateA = MakeState(gridA, TestHelpers.BuildTerritoriesFromGrid(gridA));
        RisingTidesRules.SubmergeStep(stateA, Red, new Random(42), budget: 1);
        HexCoord removedA = origCoords.Single(c => !stateA.Grid.Contains(c));

        HexGrid gridB = GridA();
        GameState stateB = MakeState(gridB, TestHelpers.BuildTerritoriesFromGrid(gridB));
        RisingTidesRules.SubmergeStep(stateB, Red, new Random(42), budget: 1);
        HexCoord removedB = origCoords.Single(c => !stateB.Grid.Contains(c));

        Assert.Equal(removedA, removedB);
    }
}
