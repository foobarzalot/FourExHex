using System.Linq;
using Godot;
using Xunit;

namespace FourExHex.Tests;

public class CapitalPlacerTests
{
    private static readonly Color Red = new Color(1f, 0f, 0f);

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
