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

    private static GameState MakeState(
        HexGrid grid, IReadOnlyList<Territory> territories,
        GameMode mode = GameMode.FogOfWar,
        PlayerKind redKind = PlayerKind.Human,
        PlayerKind blueKind = PlayerKind.Computer,
        IReadOnlySet<HexCoord>? waterCoords = null)
    {
        var players = new List<Player>
        {
            new Player("Red", Red, redKind),
            new Player("Blue", Blue, blueKind),
        };
        return new GameState(
            grid, territories, players, new TurnState(players), new Treasury(),
            waterCoords: waterCoords, mode: mode);
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

    // --- HiddenLandRemains (win gating) ---------------------------------

    [Fact]
    public void HiddenLandRemains_TileNeverSeen_True()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(5, 5, Blue);
        GiveRedTerritory(grid, 0, 0);
        GameState state = MakeState(grid, BuildTerr(grid));
        VisibilityRules.UpdateSeen(state, Red);

        // The far corner is out of sight and was never seen — the map is not
        // fully revealed, so no win may be declared or claimed.
        Assert.Equal(VisibilityTier.Fog, VisibilityRules.TierOf(
            HexCoord.FromOffset(4, 4), VisibilityRules.ComputeVisible(state, Red), state));
        Assert.True(VisibilityRules.HiddenLandRemains(state));
    }

    [Fact]
    public void HiddenLandRemains_WholeGridSeen_False()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(5, 5, Blue);
        GiveRedTerritory(grid, 0, 0);
        GameState state = MakeState(grid, BuildTerr(grid));

        TestHelpers.RevealWholeGrid(state);

        Assert.False(VisibilityRules.HiddenLandRemains(state));
        Assert.Equal(0, VisibilityRules.HiddenLandCount(state));
    }

    [Fact]
    public void HiddenLandCount_CountsOnlyFullyHiddenLand()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(5, 5, Blue);
        GiveRedTerritory(grid, 0, 0);
        GameState state = MakeState(grid, BuildTerr(grid));

        // Reveal everything except three far tiles, all well out of Red's sight.
        var withheld = new HashSet<HexCoord>
        {
            HexCoord.FromOffset(4, 3), HexCoord.FromOffset(3, 4), HexCoord.FromOffset(4, 4),
        };
        foreach (HexTile tile in grid.Tiles)
            if (!withheld.Contains(tile.Coord)) state.MarkSeen(tile.Coord);

        Assert.Equal(3, VisibilityRules.HiddenLandCount(state));
    }

    [Fact]
    public void HiddenLandRemains_OutsideFogOfWar_False()
    {
        // No fog, nothing hidden: the other modes keep full-grid win semantics.
        HexGrid grid = TestHelpers.BuildRectGrid(5, 5, Blue);
        GiveRedTerritory(grid, 0, 0);

        foreach (GameMode mode in new[]
                 { GameMode.Freeform, GameMode.RisingTides, GameMode.VikingRaiders })
        {
            GameState state = MakeState(grid, BuildTerr(grid), mode: mode);
            Assert.False(VisibilityRules.HiddenLandRemains(state));
        }
    }

    [Fact]
    public void HiddenLandRemains_NoHumanPerspective_False()
    {
        // The three cases where BuildProjection declines to pick a perspective
        // must all leave the win checks ungated — an all-AI fog run (the
        // FOUREXHEX_6AI diagnostic mode, the campaign winner sweep) never marks
        // anything seen, so a gate here would stall it until the turn cap.
        HexGrid noHumans = TestHelpers.BuildRectGrid(5, 5, Blue);
        GiveRedTerritory(noHumans, 0, 0);
        Assert.False(VisibilityRules.HiddenLandRemains(
            MakeState(noHumans, BuildTerr(noHumans), redKind: PlayerKind.Computer)));

        HexGrid twoHumans = TestHelpers.BuildRectGrid(5, 5, Blue);
        GiveRedTerritory(twoHumans, 0, 0);
        Assert.False(VisibilityRules.HiddenLandRemains(
            MakeState(twoHumans, BuildTerr(twoHumans), blueKind: PlayerKind.Human)));

        // Eliminated human: defeat already reveals the whole map, and the
        // surviving AIs must still be able to win.
        HexGrid eliminated = TestHelpers.BuildRectGrid(5, 5, Blue);
        eliminated.Get(HexCoord.FromOffset(2, 2))!.Owner = Red; // singleton, no capital
        GameState state = MakeState(eliminated, BuildTerr(eliminated));
        Assert.True(WinConditionRules.IsEliminated(Red, eliminated));
        Assert.False(VisibilityRules.HiddenLandRemains(state));
    }

    [Fact]
    public void HiddenLandRemains_UnseenWater_DoesNotCount()
    {
        // Only land gates a win. Water coords are not in the grid at all, so an
        // unexplored ocean can never block victory.
        HexGrid grid = TestHelpers.BuildSpotGrid(
            Blue,
            HexCoord.FromOffset(0, 0), HexCoord.FromOffset(1, 0), HexCoord.FromOffset(2, 0));
        grid.Get(HexCoord.FromOffset(0, 0))!.Owner = Red;
        grid.Get(HexCoord.FromOffset(1, 0))!.Owner = Red;
        var water = new HashSet<HexCoord> { HexCoord.FromOffset(9, 9) };
        GameState state = MakeState(grid, BuildTerr(grid), waterCoords: water);

        TestHelpers.RevealWholeGrid(state);

        Assert.False(state.IsSeen(HexCoord.FromOffset(9, 9))); // ocean still dark
        Assert.False(VisibilityRules.HiddenLandRemains(state));
    }

    [Fact]
    public void HiddenLandRemains_UnseenTileOwnedByHuman_DoesNotCount()
    {
        // You cannot have unknown land you own. Sight comes only from
        // capital-bearing territories, so a human's isolated singleton is owned
        // but never seen — it must not lock them out of victory forever.
        HexGrid grid = TestHelpers.BuildRectGrid(5, 5, Blue);
        GiveRedTerritory(grid, 0, 0);
        HexCoord island = HexCoord.FromOffset(4, 4);
        grid.Get(island)!.Owner = Red;
        GameState state = MakeState(grid, BuildTerr(grid));
        foreach (HexTile tile in grid.Tiles)
            if (tile.Coord != island) state.MarkSeen(tile.Coord);

        Assert.False(state.IsSeen(island));
        Assert.False(VisibilityRules.HiddenLandRemains(state));

        // Control: the same never-seen tile in enemy hands does gate the win.
        grid.Get(island)!.Owner = Blue;
        state.Territories = BuildTerr(grid);
        Assert.True(VisibilityRules.HiddenLandRemains(state));
    }

    [Fact]
    public void HiddenLandRemains_DoesNotMarkAnythingSeen()
    {
        // A pure predicate: asking whether the map is revealed must not reveal
        // any of it (the win checks call this on every capture).
        HexGrid grid = TestHelpers.BuildRectGrid(5, 5, Blue);
        GiveRedTerritory(grid, 0, 0);
        GameState state = MakeState(grid, BuildTerr(grid));

        Assert.True(VisibilityRules.HiddenLandRemains(state));
        Assert.Empty(state.Seen);
    }

    private static IReadOnlyList<Territory> BuildTerr(HexGrid grid) =>
        TestHelpers.BuildTerritoriesFromGrid(grid);
}
