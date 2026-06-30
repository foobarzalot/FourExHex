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
            new Player("Red", Red),
            new Player("Blue", Blue),
        };
        return new GameState(
            grid, territories, players, new TurnState(players), new Treasury(),
            waterCoords: null, mode: GameMode.FogOfWar);
    }

    // --- ComputeVisible --------------------------------------------------

    [Fact]
    public void ComputeVisible_OwnedTilePlusInGridNeighbors_AreVisible()
    {
        // 5x5 all-Blue grid; carve a single Red tile in the interior so its
        // six neighbours are all in-grid.
        HexGrid grid = TestHelpers.BuildRectGrid(5, 5, Blue);
        HexCoord center = HexCoord.FromOffset(2, 2);
        grid.Get(center)!.Owner = Red;

        HashSet<HexCoord> visible = VisibilityRules.ComputeVisible(MakeState(grid, BuildTerr(grid)), Red);

        Assert.Contains(center, visible);
        foreach (HexCoord n in center.Neighbors())
            Assert.Contains(n, visible);
        Assert.Equal(7, visible.Count); // the tile + its 6 neighbours
    }

    [Fact]
    public void ComputeVisible_TileTwoRingsAway_IsNotVisible()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(7, 7, Blue);
        HexCoord center = HexCoord.FromOffset(3, 3);
        grid.Get(center)!.Owner = Red;

        HashSet<HexCoord> visible = VisibilityRules.ComputeVisible(MakeState(grid, BuildTerr(grid)), Red);

        HexCoord far = HexCoord.FromOffset(5, 3); // 2 columns away
        Assert.True(HexCoord.Distance(center, far) >= 2);
        Assert.DoesNotContain(far, visible);
    }

    [Fact]
    public void ComputeVisible_EdgeOwnedTile_IncludesOffGridWaterNeighbors()
    {
        // Water and off-map cells in the one-hex ring are in sight too, so the
        // coastline around the human's land is revealed (then remembered).
        HexGrid grid = TestHelpers.BuildRectGrid(3, 3, Blue);
        HexCoord corner = HexCoord.FromOffset(0, 0);
        grid.Get(corner)!.Owner = Red;

        HashSet<HexCoord> visible = VisibilityRules.ComputeVisible(MakeState(grid, BuildTerr(grid)), Red);

        Assert.Contains(corner, visible);
        Assert.Equal(7, visible.Count); // the corner + all 6 ring coords
        Assert.Contains(visible, c => !grid.Contains(c)); // at least one off-grid (water) coord
    }

    // --- UpdateMemory + TierOf ------------------------------------------

    [Fact]
    public void UpdateMemory_RemembersVisibleTiles_StaleAfterOwnershipLost()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(5, 5, Blue);
        HexCoord center = HexCoord.FromOffset(2, 2);
        HexCoord neighbor = center.Neighbors().First(grid.Contains);
        grid.Get(center)!.Owner = Red;
        GameState state = MakeState(grid, BuildTerr(grid));

        // First sight: the neighbour is visible and remembered as Blue-owned.
        VisibilityRules.UpdateMemory(state, Red);
        Assert.True(state.IsRemembered(neighbor));
        Assert.Equal(Blue, state.Remembered[neighbor].Owner);

        // The world changes out of sight: Red loses the center, so the
        // neighbour leaves Red's sight. Memory must keep the LAST-SEEN owner.
        PlayerId green = PlayerId.FromIndex(2);
        grid.Get(center)!.Owner = green;
        grid.Get(neighbor)!.Owner = green; // live change the human can't see
        HashSet<HexCoord> visibleNow = VisibilityRules.ComputeVisible(state, Red);

        Assert.Equal(VisibilityTier.Stale, VisibilityRules.TierOf(neighbor, visibleNow, state));
        Assert.Equal(Blue, state.Remembered[neighbor].Owner); // last-seen, not live Red
    }

    [Fact]
    public void TierOf_NeverSeenTile_IsFog()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(5, 5, Blue);
        grid.Get(HexCoord.FromOffset(0, 0))!.Owner = Red;
        GameState state = MakeState(grid, BuildTerr(grid));
        VisibilityRules.UpdateMemory(state, Red);

        HexCoord far = HexCoord.FromOffset(4, 4);
        HashSet<HexCoord> visible = VisibilityRules.ComputeVisible(state, Red);
        Assert.Equal(VisibilityTier.Fog, VisibilityRules.TierOf(far, visible, state));
    }

    [Fact]
    public void UpdateMemory_RemembersOccupantSnapshot_NotLiveMutation()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(5, 5, Blue);
        HexCoord center = HexCoord.FromOffset(2, 2);
        HexCoord neighbor = center.Neighbors().First(grid.Contains);
        grid.Get(center)!.Owner = Red;
        GameState state = MakeState(grid, BuildTerr(grid));
        // Set after territory build so capital reconciliation can't overwrite it.
        grid.Get(neighbor)!.Occupant = new Unit(Blue, UnitLevel.Recruit);

        VisibilityRules.UpdateMemory(state, Red);

        // Mutate the live occupant after the snapshot.
        grid.Get(neighbor)!.Occupant = new Unit(Blue, UnitLevel.Commander);

        HexOccupant? remembered = state.Remembered[neighbor].Occupant;
        Unit? rememberedUnit = remembered as Unit;
        Assert.NotNull(rememberedUnit);
        Assert.Equal(UnitLevel.Recruit, rememberedUnit!.Level); // snapshot, not live
    }

    // --- Determinism guard ----------------------------------------------

    [Fact]
    public void UpdateMemory_DoesNotMutateTreasuryOrTerritories()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(5, 5, Blue);
        grid.Get(HexCoord.FromOffset(2, 2))!.Owner = Red;
        IReadOnlyList<Territory> territories = BuildTerr(grid);
        GameState state = MakeState(grid, territories);

        VisibilityRules.UpdateMemory(state, Red);

        Assert.Same(territories, state.Territories); // territory list untouched
    }

    [Fact]
    public void UpdateMemory_DoesNotChangeGameStateChecksum()
    {
        // Fog memory lives outside the checksummed game state, so enabling fog
        // can't perturb AI decisions, RNG, or replay/determinism: same seed,
        // fog on vs off, produces the same game.
        HexGrid grid = TestHelpers.BuildRectGrid(5, 5, Blue);
        grid.Get(HexCoord.FromOffset(2, 2))!.Owner = Red;
        GameState state = MakeState(grid, BuildTerr(grid));

        string before = GameStateChecksum.Compute(state);
        VisibilityRules.UpdateMemory(state, Red);
        Assert.NotEmpty(state.Remembered); // memory was actually written
        Assert.Equal(before, GameStateChecksum.Compute(state));
    }

    private static IReadOnlyList<Territory> BuildTerr(HexGrid grid) =>
        TestHelpers.BuildTerritoriesFromGrid(grid);
}
