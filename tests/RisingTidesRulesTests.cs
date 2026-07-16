// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
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

        bool changed = RisingTidesRules.SubmergeStep(state, Red, budget: 1);

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

        bool changed = RisingTidesRules.SubmergeStep(state, Red, budget: 1);

        Assert.False(changed);
        Assert.Equal(25, state.Grid.Count);
        Assert.Empty(state.WaterCoords);
    }

    [Fact]
    public void SubmergeStep_MountainShore_DemotesThenSubmergesOnLaterStep()
    {
        // Red owns the interior centre (keeps it alive) plus a single shore
        // tile that is a mountain. With exactly one shore the selection is
        // forced.
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
        bool changed1 = RisingTidesRules.SubmergeStep(state, Red, budget: 1);
        Assert.True(changed1);
        Assert.Equal(25, state.Grid.Count);
        Assert.True(state.Grid.Contains(shoreMtn));
        Assert.False(state.Grid.Get(shoreMtn)!.IsMountain);
        Assert.Empty(state.WaterCoords);

        // Step 2: now a plain shore, it submerges.
        bool changed2 = RisingTidesRules.SubmergeStep(state, Red, budget: 1);
        Assert.True(changed2);
        Assert.Equal(24, state.Grid.Count);
        Assert.False(state.Grid.Contains(shoreMtn));
        Assert.Contains(shoreMtn, state.WaterCoords);
    }

    [Fact]
    public void SubmergeStep_IsDeterministic_PicksSameTileEveryRun()
    {
        // Selection is structural (no RNG): two runs of identical
        // state must erode the same tile, every time.
        HexGrid GridA() => TestHelpers.BuildRectGrid(2, 2, Red);

        HexGrid gridA = GridA();
        var origCoords = gridA.Tiles.Select(t => t.Coord).ToHashSet();
        GameState stateA = MakeState(gridA, TestHelpers.BuildTerritoriesFromGrid(gridA));
        RisingTidesRules.SubmergeStep(stateA, Red, budget: 1);
        HexCoord removedA = origCoords.Single(c => !stateA.Grid.Contains(c));

        HexGrid gridB = GridA();
        GameState stateB = MakeState(gridB, TestHelpers.BuildTerritoriesFromGrid(gridB));
        RisingTidesRules.SubmergeStep(stateB, Red, budget: 1);
        HexCoord removedB = origCoords.Single(c => !stateB.Grid.Contains(c));

        Assert.Equal(removedA, removedB);
    }

    // --- ForecastSubmerge / ApplyForecast --------------------------------

    [Fact]
    public void ForecastSubmerge_DoesNotMutateGridOrWater()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(2, 1, Red);
        GameState state = MakeState(grid, TestHelpers.BuildTerritoriesFromGrid(grid));

        IReadOnlyList<TideStep> plan =
            RisingTidesRules.ForecastSubmerge(state, Red, budget: 1);

        Assert.Single(plan);
        Assert.False(plan[0].DemoteOnly);
        // Nothing has actually changed yet — the plan is just a forecast.
        Assert.Equal(2, state.Grid.Count);
        Assert.Empty(state.WaterCoords);
        Assert.True(state.Grid.Contains(plan[0].Coord));
    }

    [Fact]
    public void ForecastSubmerge_PicksSameTileAsSubmergeStep()
    {
        // SubmergeStep is ApplyForecast(ForecastSubmerge(...)), so asserting the two
        // pick the *same* coord is tautological — both come from one ForecastSubmerge
        // call. Instead anchor to the independently-known answer: on an even row of 3,
        // ends tie on exposure (weight 5) and the strict tie-break takes the smaller
        // coord (0,0) — see ForecastSubmerge_EqualExposure_TieBreaksByAscendingHexCoord.
        // This verifies (a) the forecast picks that literal and (b) ApplyForecast removes
        // exactly that literal — a wrong selection rule or a misapplied plan both fail.
        HexCoord expected = HexCoord.FromOffset(0, 0);
        HexGrid Grid() => TestHelpers.BuildRectGrid(3, 1, Red);

        HexGrid gridForecast = Grid();
        GameState forecastState = MakeState(gridForecast, TestHelpers.BuildTerritoriesFromGrid(gridForecast));
        IReadOnlyList<TideStep> plan =
            RisingTidesRules.ForecastSubmerge(forecastState, Red, budget: 1);
        Assert.Equal(expected, plan.Single().Coord);

        HexGrid gridSubmerge = Grid();
        var origCoords = gridSubmerge.Tiles.Select(t => t.Coord).ToHashSet();
        GameState submergeState = MakeState(gridSubmerge, TestHelpers.BuildTerritoriesFromGrid(gridSubmerge));
        RisingTidesRules.SubmergeStep(submergeState, Red, budget: 1);
        HexCoord removed = origCoords.Single(c => !submergeState.Grid.Contains(c));
        Assert.Equal(expected, removed);
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
            RisingTidesRules.ForecastSubmerge(state, Red, budget: 1);

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
            RisingTidesRules.ForecastSubmerge(state, Red, budget: 1);
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
            RisingTidesRules.ForecastSubmerge(state, Red, budget: 1);

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

    // --- WaterBorderWeight / weighted selection --------------------------
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
    public void ForecastSubmerge_PicksMostExposedTileFirst()
    {
        // A single even row (only E/W neighbours possible) with cols 0,1,2
        // contiguous plus an isolated tile at col 5 (gap at 3,4). Hand-derived
        // weights (sea-facing sides = 6 - in-grid neighbours): (0,0)=5, (1,0)=4,
        // (2,0)=5, (5,0)=6. The unique most-exposed tile is the *largest* coord
        // (5,0) — proving selection is by exposure, not by HexCoord order.
        var grid = new HexGrid();
        foreach (int col in new[] { 0, 1, 2, 5 })
            grid.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        GameState state = MakeState(grid, TestHelpers.BuildTerritoriesFromGrid(grid));

        IReadOnlyList<TideStep> plan =
            RisingTidesRules.ForecastSubmerge(state, Red, budget: 1);

        Assert.Equal(HexCoord.FromOffset(5, 0), plan.Single().Coord);
    }

    [Fact]
    public void ForecastSubmerge_EqualExposure_TieBreaksByAscendingHexCoord()
    {
        // Single even row of 3: ends (0,0) and (2,0) are both weight 5 (one
        // E/W neighbour each), middle (1,0) is weight 4. The two ends tie on
        // exposure, so the strict tie-break must take the smaller coord (0,0).
        HexGrid grid = TestHelpers.BuildRectGrid(3, 1, Red);
        GameState state = MakeState(grid, TestHelpers.BuildTerritoriesFromGrid(grid));

        IReadOnlyList<TideStep> plan =
            RisingTidesRules.ForecastSubmerge(state, Red, budget: 1);

        Assert.Equal(HexCoord.FromOffset(0, 0), plan.Single().Coord);
    }

    // --- Randomized tie-break (rng-supplied) -----------------------------

    [Fact]
    public void ForecastSubmerge_WithRng_NeverPicksLowerExposureTile()
    {
        // Single even row of 3: ends (0,0),(2,0) are weight 5, middle (1,0) is
        // weight 4. A randomized tie-break only reshuffles EQUAL-exposure tiles,
        // so it must never select the strictly-less-exposed middle.
        var ends = new[] { HexCoord.FromOffset(0, 0), HexCoord.FromOffset(2, 0) };
        HexCoord middle = HexCoord.FromOffset(1, 0);

        for (int seed = 0; seed < 30; seed++)
        {
            HexGrid grid = TestHelpers.BuildRectGrid(3, 1, Red);
            GameState state = MakeState(grid, TestHelpers.BuildTerritoriesFromGrid(grid));

            IReadOnlyList<TideStep> plan =
                RisingTidesRules.ForecastSubmerge(state, Red, budget: 1, rng: new Random(seed));

            Assert.Contains(plan.Single().Coord, ends);
            Assert.NotEqual(middle, plan.Single().Coord);
        }
    }

    [Fact]
    public void ForecastSubmerge_WithRng_TieBreakDeviatesFromLexMin()
    {
        // Across seeds the chosen end is sometimes the larger coord (2,0), not
        // always the lex-min (0,0) the deterministic path would always pick.
        HexCoord high = HexCoord.FromOffset(2, 0);
        bool sawHigh = false;
        for (int seed = 0; seed < 30 && !sawHigh; seed++)
        {
            HexGrid grid = TestHelpers.BuildRectGrid(3, 1, Red);
            GameState state = MakeState(grid, TestHelpers.BuildTerritoriesFromGrid(grid));
            IReadOnlyList<TideStep> plan =
                RisingTidesRules.ForecastSubmerge(state, Red, budget: 1, rng: new Random(seed));
            if (plan.Single().Coord == high) sawHigh = true;
        }
        Assert.True(sawHigh, "Randomized tie-break never picked the non-lex-min end across 30 seeds.");
    }

    [Fact]
    public void ForecastSubmerge_WithRng_IsDeterministicForSameSeed()
    {
        HexGrid gridA = TestHelpers.BuildRectGrid(3, 1, Red);
        GameState stateA = MakeState(gridA, TestHelpers.BuildTerritoriesFromGrid(gridA));
        IReadOnlyList<TideStep> planA =
            RisingTidesRules.ForecastSubmerge(stateA, Red, budget: 1, rng: new Random(99));

        HexGrid gridB = TestHelpers.BuildRectGrid(3, 1, Red);
        GameState stateB = MakeState(gridB, TestHelpers.BuildTerritoriesFromGrid(gridB));
        IReadOnlyList<TideStep> planB =
            RisingTidesRules.ForecastSubmerge(stateB, Red, budget: 1, rng: new Random(99));

        Assert.Equal(planA.Single().Coord, planB.Single().Coord);
    }

    [Fact]
    public void ForecastSubmerge_Budget_ReturnsTopNInStrictOrder()
    {
        // Single even row of 5: weights (0,0)=5, (1,0)=4, (2,0)=4, (3,0)=4,
        // (4,0)=5. Strict order is descending weight then ascending coord:
        // (0,0), (4,0) [both w5], then (1,0) [w4, smallest coord]. budget 3
        // returns exactly that prefix in that order.
        HexGrid grid = TestHelpers.BuildRectGrid(5, 1, Red);
        GameState state = MakeState(grid, TestHelpers.BuildTerritoriesFromGrid(grid));

        IReadOnlyList<TideStep> plan =
            RisingTidesRules.ForecastSubmerge(state, Red, budget: 3);

        Assert.Equal(
            new[]
            {
                HexCoord.FromOffset(0, 0),
                HexCoord.FromOffset(4, 0),
                HexCoord.FromOffset(1, 0),
            },
            plan.Select(s => s.Coord).ToArray());
    }
}
