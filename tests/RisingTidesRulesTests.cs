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

    // --- ForecastSubmerge / ApplyForecast (issue #85) --------------------

    [Fact]
    public void ForecastSubmerge_DoesNotMutateGridOrWater()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(2, 1, Red);
        GameState state = MakeState(grid, TestHelpers.BuildTerritoriesFromGrid(grid));

        IReadOnlyList<TideStep> plan =
            RisingTidesRules.ForecastSubmerge(state, Red, new Random(1), budget: 1);

        Assert.Single(plan);
        Assert.False(plan[0].DemoteOnly);
        // Nothing has actually changed yet — the plan is just a forecast.
        Assert.Equal(2, state.Grid.Count);
        Assert.Empty(state.WaterCoords);
        Assert.True(state.Grid.Contains(plan[0].Coord));
    }

    [Fact]
    public void ForecastSubmerge_SameSeed_PicksSameTileAsSubmergeStep()
    {
        HexGrid Grid() => TestHelpers.BuildRectGrid(2, 2, Red);

        HexGrid gridForecast = Grid();
        GameState forecastState = MakeState(gridForecast, TestHelpers.BuildTerritoriesFromGrid(gridForecast));
        IReadOnlyList<TideStep> plan =
            RisingTidesRules.ForecastSubmerge(forecastState, Red, new Random(42), budget: 1);

        HexGrid gridSubmerge = Grid();
        var origCoords = gridSubmerge.Tiles.Select(t => t.Coord).ToHashSet();
        GameState submergeState = MakeState(gridSubmerge, TestHelpers.BuildTerritoriesFromGrid(gridSubmerge));
        RisingTidesRules.SubmergeStep(submergeState, Red, new Random(42), budget: 1);
        HexCoord removed = origCoords.Single(c => !submergeState.Grid.Contains(c));

        Assert.Equal(removed, plan.Single().Coord);
    }

    [Fact]
    public void ForecastSubmerge_MountainShore_FlaggedDemoteOnly()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(5, 5, Blue);
        HexCoord center = HexCoord.FromOffset(2, 2);
        HexCoord shoreMtn = HexCoord.FromOffset(0, 2);
        grid.Get(center)!.Owner = Red;
        grid.Get(shoreMtn)!.Owner = Red;
        grid.Get(shoreMtn)!.IsMountain = true;
        GameState state = MakeState(grid, TestHelpers.BuildTerritoriesFromGrid(grid));

        IReadOnlyList<TideStep> plan =
            RisingTidesRules.ForecastSubmerge(state, Red, new Random(99), budget: 1);

        Assert.Equal(shoreMtn, plan.Single().Coord);
        Assert.True(plan.Single().DemoteOnly);
        // Still nothing mutated — the mountain is intact.
        Assert.True(state.Grid.Get(shoreMtn)!.IsMountain);
    }

    [Fact]
    public void ApplyForecast_SubmergesPlannedTileAndReconciles()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(2, 1, Red);
        GameState state = MakeState(grid, TestHelpers.BuildTerritoriesFromGrid(grid));
        IReadOnlyList<TideStep> plan =
            RisingTidesRules.ForecastSubmerge(state, Red, new Random(1), budget: 1);
        HexCoord doomed = plan.Single().Coord;

        bool changed = RisingTidesRules.ApplyForecast(state, Red, plan);

        Assert.True(changed);
        Assert.False(state.Grid.Contains(doomed));
        Assert.Contains(doomed, state.WaterCoords);
        Assert.Equal(1, state.Grid.Count);
        Assert.True(WinConditionRules.IsEliminated(Red, state.Grid));
    }

    [Fact]
    public void ApplyForecast_MountainPlan_DemotesNotSubmerges()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(5, 5, Blue);
        HexCoord center = HexCoord.FromOffset(2, 2);
        HexCoord shoreMtn = HexCoord.FromOffset(0, 2);
        grid.Get(center)!.Owner = Red;
        grid.Get(shoreMtn)!.Owner = Red;
        grid.Get(shoreMtn)!.IsMountain = true;
        GameState state = MakeState(grid, TestHelpers.BuildTerritoriesFromGrid(grid));
        IReadOnlyList<TideStep> plan =
            RisingTidesRules.ForecastSubmerge(state, Red, new Random(99), budget: 1);

        bool changed = RisingTidesRules.ApplyForecast(state, Red, plan);

        Assert.True(changed);
        Assert.True(state.Grid.Contains(shoreMtn));
        Assert.False(state.Grid.Get(shoreMtn)!.IsMountain);
        Assert.Empty(state.WaterCoords);
    }

    [Fact]
    public void ApplyForecast_EmptyPlan_IsNoOp()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(2, 2, Red);
        GameState state = MakeState(grid, TestHelpers.BuildTerritoriesFromGrid(grid));

        bool changed = RisingTidesRules.ApplyForecast(state, Red, System.Array.Empty<TideStep>());

        Assert.False(changed);
        Assert.Equal(4, state.Grid.Count);
        Assert.Empty(state.WaterCoords);
    }

    // --- WaterBorderWeight / weighted selection (issue #85 follow-on) ----
    //
    // The weight is "sea-facing sides" = 6 - (in-grid neighbours). These tests
    // assert independently hand-derived counts for layouts whose adjacency is
    // unambiguous — NOT counts re-derived from NeighborsOf (which would be
    // circular, since WaterBorderWeight is defined in terms of it).

    [Fact]
    public void WaterBorderWeight_IsolatedTile_IsSix()
    {
        // A lone tile has zero in-grid neighbours, so all six sides face sea.
        var grid = new HexGrid();
        grid.Add(new HexTile(HexCoord.FromOffset(0, 0), Red));

        Assert.Equal(6, RisingTidesRules.WaterBorderWeight(grid, HexCoord.FromOffset(0, 0)));
    }

    [Fact]
    public void WaterBorderWeight_SingleRowLine_EndIsFive_MiddleIsFour()
    {
        // A single even row (row 0 → no odd-row stagger) is a straight axial
        // line: each tile's only possible in-grid neighbours are due E and W.
        // An end therefore has exactly 1 land neighbour (weight 5); an interior
        // tile has exactly 2 (weight 4). No diagonal neighbours exist — the
        // rows above/below are empty.
        HexGrid grid = TestHelpers.BuildRectGrid(5, 1, Red);

        Assert.Equal(5, RisingTidesRules.WaterBorderWeight(grid, HexCoord.FromOffset(0, 0)));
        Assert.Equal(5, RisingTidesRules.WaterBorderWeight(grid, HexCoord.FromOffset(4, 0)));
        Assert.Equal(4, RisingTidesRules.WaterBorderWeight(grid, HexCoord.FromOffset(2, 0)));
    }

    [Fact]
    public void WaterBorderWeight_SurroundedTile_IsZero()
    {
        // In a 3x3 block the centre (1,1) has all six neighbours in-grid
        // (E,NE,NW,W,SW,SE all map to occupied cells), so no side faces sea.
        HexGrid grid = TestHelpers.BuildRectGrid(3, 3, Red);

        Assert.Equal(0, RisingTidesRules.WaterBorderWeight(grid, HexCoord.FromOffset(1, 1)));
    }

    [Fact]
    public void ForecastSubmerge_SkewsTowardMoreSeaExposedTiles()
    {
        // Single even row of 5: the two ends are weight 5, the three middles
        // weight 4 (hand-derived above — parity-free, only E/W neighbours). The
        // seeded weighted draw must pick the ends more per-tile than the
        // middles. (We assert against the KNOWN weights, not WaterBorderWeight.)
        HexGrid Build() => TestHelpers.BuildRectGrid(5, 1, Red);
        var ends = new HashSet<HexCoord> { HexCoord.FromOffset(0, 0), HexCoord.FromOffset(4, 0) };
        var middles = new HashSet<HexCoord>
        {
            HexCoord.FromOffset(1, 0), HexCoord.FromOffset(2, 0), HexCoord.FromOffset(3, 0),
        };

        int endPicks = 0, midPicks = 0;
        const int trials = 3000;
        for (int seed = 0; seed < trials; seed++)
        {
            HexGrid g = Build();
            GameState state = MakeState(g, TestHelpers.BuildTerritoriesFromGrid(g));
            HexCoord picked = RisingTidesRules
                .ForecastSubmerge(state, Red, new Random(seed), budget: 1).Single().Coord;
            if (ends.Contains(picked)) endPicks++;
            else if (middles.Contains(picked)) midPicks++;
        }

        // Per-tile probabilities: end 5/22≈0.227, middle 4/22≈0.182 — a 1.25×
        // edge. Over 3000 trials the gap is many sigma; assert a safe 1.1× per
        // tile. Uniform selection (the old behaviour) would make these equal.
        double endPerTile = (double)endPicks / ends.Count;
        double midPerTile = (double)midPicks / middles.Count;
        Assert.True(endPerTile > midPerTile * 1.1,
            $"weighting not skewing: end/tile={endPerTile:0}, middle/tile={midPerTile:0}");
    }
}
