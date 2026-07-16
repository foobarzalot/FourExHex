// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

public class VisibilityRulesTests
{
    private static readonly PlayerId Red = PlayerId.FromIndex(0);
    private static readonly PlayerId Blue = PlayerId.FromIndex(1);

    private static GameState MakeState(HexGrid grid, IReadOnlyList<Territory> territories)
    {
        var players = new List<Player>
        {
            new Player("Red", Red, PlayerKind.Human),
            new Player("Blue", Blue, PlayerKind.Computer),
        };
        return new GameState(
            grid, territories, players, new TurnState(players), new Treasury(),
            waterCoords: null, mode: GameMode.FogOfWar);
    }

    // Give Red a 2-tile (capital-bearing) territory: an anchor + its east
    // neighbour (same-row adjacent columns are always hex-neighbours). A 2-tile
    // group gets a capital, so it grants sight — unlike a singleton.
    private static (HexCoord A, HexCoord B) GiveRedTerritory(HexGrid grid, int col, int row)
    {
        HexCoord a = HexCoord.FromOffset(col, row);
        HexCoord b = HexCoord.FromOffset(col + 1, row);
        grid.Get(a)!.Owner = Red;
        grid.Get(b)!.Owner = Red;
        return (a, b);
    }

    // --- ComputeVisible --------------------------------------------------

    [Fact]
    public void ComputeVisible_OwnedTerritory_TilesAndRingVisible()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(5, 5, Blue);
        (HexCoord a, HexCoord b) = GiveRedTerritory(grid, 1, 2);

        HashSet<HexCoord> visible = VisibilityRules.ComputeVisible(MakeState(grid, BuildTerr(grid)), Red);

        Assert.Contains(a, visible);
        Assert.Contains(b, visible);
        foreach (HexCoord n in a.Neighbors())
            Assert.Contains(n, visible);
        Assert.DoesNotContain(HexCoord.FromOffset(4, 4), visible); // far tile fogged
    }

    [Fact]
    public void ComputeVisible_Singleton_GrantsNoVisibility()
    {
        // A lone owned tile is a size-1 territory with no capital — "part of no
        // territory" — so it (and its ring) generate no sight at all.
        HexGrid grid = TestHelpers.BuildRectGrid(5, 5, Blue);
        HexCoord lone = HexCoord.FromOffset(2, 2);
        grid.Get(lone)!.Owner = Red;

        HashSet<HexCoord> visible = VisibilityRules.ComputeVisible(MakeState(grid, BuildTerr(grid)), Red);

        Assert.Empty(visible);
        Assert.DoesNotContain(lone, visible);
    }

    [Fact]
    public void ComputeVisible_TileTwoRingsAway_IsNotVisible()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(7, 7, Blue);
        (HexCoord a, HexCoord b) = GiveRedTerritory(grid, 3, 3);

        HashSet<HexCoord> visible = VisibilityRules.ComputeVisible(MakeState(grid, BuildTerr(grid)), Red);

        HexCoord far = HexCoord.FromOffset(6, 3); // >= 2 from both owned tiles
        Assert.True(HexCoord.Distance(a, far) >= 2 && HexCoord.Distance(b, far) >= 2);
        Assert.DoesNotContain(far, visible);
    }

    [Fact]
    public void ComputeVisible_EdgeOwnedTerritory_IncludesOffGridWaterNeighbors()
    {
        // Water and off-map cells in the one-hex ring are in sight too, so the
        // coastline around the human's land is revealed (then remembered).
        HexGrid grid = TestHelpers.BuildRectGrid(3, 3, Blue);
        (HexCoord corner, _) = GiveRedTerritory(grid, 0, 0);

        HashSet<HexCoord> visible = VisibilityRules.ComputeVisible(MakeState(grid, BuildTerr(grid)), Red);

        Assert.Contains(corner, visible);
        Assert.Contains(visible, c => !grid.Contains(c)); // at least one off-grid (water) coord
    }

    // --- UpdateSeen + TierOf --------------------------------------------

    [Fact]
    public void UpdateSeen_MarksVisible_StaleAfterOwnershipLost()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(5, 5, Blue);
        (HexCoord a, _) = GiveRedTerritory(grid, 1, 2);
        HexCoord neighbor = a.Neighbors().First(c => grid.Contains(c) && grid.Get(c)!.Owner == Blue);
        GameState state = MakeState(grid, BuildTerr(grid));

        // First sight: the neighbour is visible and now marked seen.
        VisibilityRules.UpdateSeen(state, Red);
        Assert.True(state.IsSeen(neighbor));

        // Red loses the whole territory, so the neighbour leaves sight — but it
        // stays seen, so it degrades to Stale, not back to Fog. Territories are
        // recomputed (as the controller does after a capture).
        PlayerId green = PlayerId.FromIndex(2);
        foreach (HexTile t in grid.Tiles)
            if (t.Owner == Red) t.Owner = green;
        state.Territories = BuildTerr(grid);
        HashSet<HexCoord> visibleNow = VisibilityRules.ComputeVisible(state, Red);

        Assert.DoesNotContain(neighbor, visibleNow);
        Assert.Equal(VisibilityTier.Stale, VisibilityRules.TierOf(neighbor, visibleNow, state));
    }

    [Fact]
    public void TierOf_NeverSeenTile_IsFog()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(5, 5, Blue);
        GiveRedTerritory(grid, 0, 0);
        GameState state = MakeState(grid, BuildTerr(grid));
        VisibilityRules.UpdateSeen(state, Red);

        HexCoord far = HexCoord.FromOffset(4, 4);
        HashSet<HexCoord> visible = VisibilityRules.ComputeVisible(state, Red);
        Assert.Equal(VisibilityTier.Fog, VisibilityRules.TierOf(far, visible, state));
    }

    // --- Determinism guard ----------------------------------------------

    [Fact]
    public void UpdateSeen_DoesNotMutateTreasuryOrTerritories()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(5, 5, Blue);
        GiveRedTerritory(grid, 2, 2);
        IReadOnlyList<Territory> territories = BuildTerr(grid);
        GameState state = MakeState(grid, territories);

        VisibilityRules.UpdateSeen(state, Red);

        Assert.Same(territories, state.Territories); // territory list untouched
    }

    [Fact]
    public void UpdateSeen_DoesNotChangeGameStateChecksum()
    {
        // Fog memory lives outside the checksummed game state, so enabling fog
        // can't perturb AI decisions, RNG, or replay/determinism: same seed,
        // fog on vs off, produces the same game.
        HexGrid grid = TestHelpers.BuildRectGrid(5, 5, Blue);
        GiveRedTerritory(grid, 2, 2);
        GameState state = MakeState(grid, BuildTerr(grid));

        string before = GameStateChecksum.Compute(state);
        VisibilityRules.UpdateSeen(state, Red);
        Assert.NotEmpty(state.Seen); // memory was actually written
        Assert.Equal(before, GameStateChecksum.Compute(state));
    }

    // --- BuildProjection (reveal on defeat) -----------------------------

    [Fact]
    public void BuildProjection_ActiveFogGame_ReturnsProjection()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(5, 5, Blue);
        GiveRedTerritory(grid, 1, 2); // capital-bearing → Red is in the game
        GameState state = MakeState(grid, BuildTerr(grid));

        FogView? fog = VisibilityRules.BuildProjection(state);
        Assert.NotNull(fog);
        Assert.NotEmpty(fog!.Visible);
    }

    [Fact]
    public void BuildProjection_HumanEliminated_ReturnsNull()
    {
        // Red holds only a singleton (no capital) → eliminated → defeat reveals
        // the whole map (BuildProjection returns null).
        HexGrid grid = TestHelpers.BuildRectGrid(5, 5, Blue);
        grid.Get(HexCoord.FromOffset(2, 2))!.Owner = Red; // lone tile, no capital
        GameState state = MakeState(grid, BuildTerr(grid));
        Assert.True(WinConditionRules.IsEliminated(Red, grid));

        Assert.Null(VisibilityRules.BuildProjection(state));
    }

    private static IReadOnlyList<Territory> BuildTerr(HexGrid grid) =>
        TestHelpers.BuildTerritoriesFromGrid(grid);
}
