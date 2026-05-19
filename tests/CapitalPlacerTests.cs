using System.Linq;
using Xunit;

namespace FourExHex.Tests;

public class CapitalPlacerTests
{
    private static readonly PlayerId Red = PlayerId.FromIndex(0);

    private static HexGrid BuildGrid(params HexCoord[] coords) =>
        TestHelpers.BuildSpotGrid(Red, coords);

    [Fact]
    public void Choose_SingletonCoords_ReturnsNull()
    {
        HexGrid grid = BuildGrid(new HexCoord(0, 0));

        HexCoord? result = CapitalPlacer.Choose(new[] { new HexCoord(0, 0) }, grid);

        Assert.Null(result);
    }

    [Fact]
    public void Choose_TwoEmptyTiles_PicksLexMin()
    {
        HexGrid grid = BuildGrid(new HexCoord(0, 0), new HexCoord(1, 0));

        HexCoord? result = CapitalPlacer.Choose(grid.Tiles.Select(t => t.Coord).ToList(), grid);

        Assert.Equal(new HexCoord(0, 0), result);
    }

    [Fact]
    public void Choose_EmptyAvailable_PrefersEmptyOverUnit()
    {
        // (0,0) has a unit (so lex-min but occupied), (1,0) is empty.
        // Placer should pick (1,0) — empty beats stomping a unit.
        HexGrid grid = BuildGrid(new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Unit(Red);

        HexCoord? result = CapitalPlacer.Choose(
            new[] { new HexCoord(0, 0), new HexCoord(1, 0) }, grid);

        Assert.Equal(new HexCoord(1, 0), result);
    }

    [Fact]
    public void Choose_AllUnitOccupied_StompsLexMinUnit()
    {
        HexGrid grid = BuildGrid(new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Unit(Red);
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red);

        HexCoord? result = CapitalPlacer.Choose(
            new[] { new HexCoord(0, 0), new HexCoord(1, 0) }, grid);

        Assert.Equal(new HexCoord(0, 0), result);
    }

    // --- Tree / grave fallback tiers -------------------------------------

    [Fact]
    public void Choose_AllTreesNoEmptyOrUnit_StompsLexMinTree()
    {
        // No empty or unit tiles left — the placer must stomp a tree
        // so the 2+ contiguous-same-color invariant is preserved.
        HexGrid grid = BuildGrid(new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Tree();
        grid.Get(new HexCoord(1, 0))!.Occupant = new Tree();

        HexCoord? result = CapitalPlacer.Choose(
            new[] { new HexCoord(0, 0), new HexCoord(1, 0) }, grid);

        Assert.Equal(new HexCoord(0, 0), result);
    }

    [Fact]
    public void Choose_AllGravesNoEmptyOrUnit_StompsLexMinGrave()
    {
        HexGrid grid = BuildGrid(new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Grave();
        grid.Get(new HexCoord(1, 0))!.Occupant = new Grave();

        HexCoord? result = CapitalPlacer.Choose(
            new[] { new HexCoord(0, 0), new HexCoord(1, 0) }, grid);

        Assert.Equal(new HexCoord(0, 0), result);
    }

    [Fact]
    public void Choose_MixedTreeAndUnit_PrefersUnitOverTree()
    {
        // Unit (tier 2) beats tree (tier 4) even if the tree is lex-min.
        HexGrid grid = BuildGrid(new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Tree();
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red);

        HexCoord? result = CapitalPlacer.Choose(
            new[] { new HexCoord(0, 0), new HexCoord(1, 0) }, grid);

        Assert.Equal(new HexCoord(1, 0), result);
    }

    [Fact]
    public void Choose_MixedGraveAndTree_PrefersGraveOverTree()
    {
        // Grave (tier 3) beats tree (tier 4): graves are already ephemeral
        // and would have become trees next turn anyway, so stomping a
        // grave is cheaper than destroying a real tree.
        HexGrid grid = BuildGrid(new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Tree();
        grid.Get(new HexCoord(1, 0))!.Occupant = new Grave();

        HexCoord? result = CapitalPlacer.Choose(
            new[] { new HexCoord(0, 0), new HexCoord(1, 0) }, grid);

        Assert.Equal(new HexCoord(1, 0), result);
    }

    [Fact]
    public void Choose_MixedEmptyAndTree_PrefersEmpty()
    {
        HexGrid grid = BuildGrid(new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Tree();
        // (1,0) stays empty.

        HexCoord? result = CapitalPlacer.Choose(
            new[] { new HexCoord(0, 0), new HexCoord(1, 0) }, grid);

        Assert.Equal(new HexCoord(1, 0), result);
    }

    [Fact]
    public void Choose_AllTowersNoOtherOptions_StompsLexMinTower()
    {
        HexGrid grid = BuildGrid(new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Tower();
        grid.Get(new HexCoord(1, 0))!.Occupant = new Tower();

        HexCoord? result = CapitalPlacer.Choose(
            new[] { new HexCoord(0, 0), new HexCoord(1, 0) }, grid);

        Assert.Equal(new HexCoord(0, 0), result);
    }

    [Fact]
    public void Choose_MixedTreeAndTower_PrefersTreeOverTower()
    {
        // Tree (tier 4) beats tower (tier 5). Tower is the last resort.
        HexGrid grid = BuildGrid(new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Tower();
        grid.Get(new HexCoord(1, 0))!.Occupant = new Tree();

        HexCoord? result = CapitalPlacer.Choose(
            new[] { new HexCoord(0, 0), new HexCoord(1, 0) }, grid);

        Assert.Equal(new HexCoord(1, 0), result);
    }

    [Fact]
    public void Choose_MixedTowerAndUnit_PrefersUnitOverTower()
    {
        HexGrid grid = BuildGrid(new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Tower();
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red);

        HexCoord? result = CapitalPlacer.Choose(
            new[] { new HexCoord(0, 0), new HexCoord(1, 0) }, grid);

        Assert.Equal(new HexCoord(1, 0), result);
    }

    [Fact]
    public void Choose_ExistingCapitalOccupant_IsIgnored()
    {
        // If a tile already has a Capital occupant, CapitalPlacer must not
        // pick it (would be a no-op at best, overwrite at worst). It should
        // only consider empty or unit-occupied tiles.
        HexGrid grid = BuildGrid(new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(0, 1));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Capital();
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red);

        // (0,1) is the only empty tile. Placer should pick it.
        HexCoord? result = CapitalPlacer.Choose(
            new[] { new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(0, 1) }, grid);

        Assert.Equal(new HexCoord(0, 1), result);
    }
}
