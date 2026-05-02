using Godot;
using Xunit;

namespace FourExHex.Tests;

public class PurchaseRulesTests
{
    private static readonly Color Red = new Color(1f, 0f, 0f);

    private static Territory MakeTerritory(Color owner, HexCoord capital, params HexCoord[] coords) =>
        new Territory(owner, coords, capital);

    // --- Unit + HexTile basics --------------------------------------------

    [Fact]
    public void Unit_Constructor_StoresOwner()
    {
        var unit = new Unit(Red);

        Assert.Equal(Red, unit.Owner);
    }

    [Fact]
    public void Unit_HasMovedThisTurn_DefaultsFalse()
    {
        var unit = new Unit(Red);

        Assert.False(unit.HasMovedThisTurn);
    }

    [Fact]
    public void HexTile_Unit_DefaultsToNull()
    {
        var tile = new HexTile(new HexCoord(0, 0), Red);

        Assert.Null(tile.Unit);
    }

    // --- CanAffordPeasant -------------------------------------------------

    [Theory]
    [InlineData(0, false)]
    [InlineData(5, false)]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(11, true)]
    [InlineData(100, true)]
    public void CanAffordPeasant_GoldThreshold(int gold, bool expected)
    {
        var capital = new HexCoord(0, 0);
        Territory territory = MakeTerritory(Red, capital, new HexCoord(0, 0), new HexCoord(1, 0));
        var treasury = new Treasury();
        treasury.SetGold(capital, gold);

        bool result = PurchaseRules.CanAffordPeasant(territory, treasury);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void CanAffordPeasant_SingletonTerritory_ReturnsFalse()
    {
        // A singleton has no capital so the treasury can't hold gold for it;
        // CanAfford must not blow up trying to dereference a null Capital.
        var singleton = new Territory(Red, new[] { new HexCoord(0, 0) }, capital: null);
        var treasury = new Treasury();

        bool result = PurchaseRules.CanAffordPeasant(singleton, treasury);

        Assert.False(result);
    }

    // --- CanAffordTower ---------------------------------------------------

    [Theory]
    [InlineData(0, false)]
    [InlineData(14, false)]
    [InlineData(15, true)]
    [InlineData(30, true)]
    public void CanAffordTower_GoldThreshold(int gold, bool expected)
    {
        var capital = new HexCoord(0, 0);
        Territory territory = MakeTerritory(Red, capital, new HexCoord(0, 0), new HexCoord(1, 0));
        var treasury = new Treasury();
        treasury.SetGold(capital, gold);

        Assert.Equal(expected, PurchaseRules.CanAffordTower(territory, treasury));
    }

    [Fact]
    public void CanAffordTower_SingletonTerritory_ReturnsFalse()
    {
        var singleton = new Territory(Red, new[] { new HexCoord(0, 0) }, capital: null);
        var treasury = new Treasury();

        Assert.False(PurchaseRules.CanAffordTower(singleton, treasury));
    }

    // --- IsValidPeasantTarget ---------------------------------------------

    [Fact]
    public void IsValidPeasantTarget_TileInTerritoryAndEmpty_ReturnsTrue()
    {
        var capital = new HexCoord(0, 0);
        Territory territory = MakeTerritory(
            Red, capital,
            new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(0, 1));
        var tile = new HexTile(new HexCoord(1, 0), Red);

        Assert.True(PurchaseRules.IsValidPeasantTarget(tile, territory));
    }

    [Fact]
    public void IsValidPeasantTarget_TileInTerritoryButOccupied_ReturnsFalse()
    {
        var capital = new HexCoord(0, 0);
        Territory territory = MakeTerritory(
            Red, capital,
            new HexCoord(0, 0), new HexCoord(1, 0));
        var tile = new HexTile(new HexCoord(1, 0), Red)
        {
            Occupant = new Unit(Red),
        };

        Assert.False(PurchaseRules.IsValidPeasantTarget(tile, territory));
    }

    [Fact]
    public void IsValidPeasantTarget_TileNotInTerritory_ReturnsFalse()
    {
        var capital = new HexCoord(0, 0);
        Territory territory = MakeTerritory(
            Red, capital,
            new HexCoord(0, 0), new HexCoord(1, 0));
        var tile = new HexTile(new HexCoord(5, 5), Red);

        Assert.False(PurchaseRules.IsValidPeasantTarget(tile, territory));
    }

    [Fact]
    public void IsValidPeasantTarget_OnOwnGrave_ReturnsTrue()
    {
        // Graves don't block placement — a new peasant can be dropped
        // onto a grave tile and bury it.
        var capital = new HexCoord(0, 0);
        Territory territory = MakeTerritory(
            Red, capital,
            new HexCoord(0, 0), new HexCoord(1, 0));
        var graveTile = new HexTile(new HexCoord(1, 0), Red)
        {
            Occupant = new Grave(),
        };

        Assert.True(PurchaseRules.IsValidPeasantTarget(graveTile, territory));
    }

    [Fact]
    public void IsValidPeasantTarget_OnOwnCapital_ReturnsFalse()
    {
        // Can't stand on top of your own capital. With the occupant model,
        // the capital hex has a Capital occupant, so the tile is "occupied"
        // by the same general check that excludes unit-held tiles.
        var capital = new HexCoord(0, 0);
        Territory territory = MakeTerritory(
            Red, capital,
            new HexCoord(0, 0), new HexCoord(1, 0));
        var capitalTile = new HexTile(new HexCoord(0, 0), Red)
        {
            Occupant = new Capital(),
        };

        Assert.False(PurchaseRules.IsValidPeasantTarget(capitalTile, territory));
    }

    // --- IsValidTowerLocation ---------------------------------------------

    private static (HexGrid grid, Territory territory) BuildLinearStrip(int length, HexCoord capital)
    {
        var grid = new HexGrid();
        var coords = new HexCoord[length];
        for (int i = 0; i < length; i++)
        {
            coords[i] = new HexCoord(i, 0);
            grid.Add(new HexTile(coords[i], Red));
        }
        return (grid, MakeTerritory(Red, capital, coords));
    }

    [Fact]
    public void IsValidTowerLocation_AcceptsEmptyTileWithNoNearbyTowers()
    {
        (HexGrid grid, Territory territory) = BuildLinearStrip(4, new HexCoord(3, 0));
        HexTile tile = grid.Get(new HexCoord(1, 0))!;

        Assert.True(PurchaseRules.IsValidTowerLocation(tile, territory, grid));
    }

    [Fact]
    public void IsValidTowerLocation_RejectsOccupiedTile()
    {
        (HexGrid grid, Territory territory) = BuildLinearStrip(4, new HexCoord(3, 0));
        grid.Get(new HexCoord(1, 0))!.Occupant = new Tree();
        HexTile tile = grid.Get(new HexCoord(1, 0))!;

        Assert.False(PurchaseRules.IsValidTowerLocation(tile, territory, grid));
    }

    [Fact]
    public void IsValidTowerLocation_RejectsTileOutsideTerritory()
    {
        (HexGrid grid, _) = BuildLinearStrip(4, new HexCoord(3, 0));
        grid.Add(new HexTile(new HexCoord(7, 0), Red));
        Territory smaller = MakeTerritory(Red, new HexCoord(3, 0),
            new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(2, 0), new HexCoord(3, 0));

        HexTile tile = grid.Get(new HexCoord(7, 0))!;
        Assert.False(PurchaseRules.IsValidTowerLocation(tile, smaller, grid));
    }

    [Fact]
    public void IsValidTowerLocation_AcceptsAdjacentToFriendlyTower()
    {
        // Tower at (0,0). Proposed at (1,0). Humans can cluster towers
        // freely — the spacing rule is an AI-only heuristic, not a
        // gameplay constraint.
        (HexGrid grid, Territory territory) = BuildLinearStrip(5, new HexCoord(4, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Tower();
        HexTile tile = grid.Get(new HexCoord(1, 0))!;

        Assert.True(PurchaseRules.IsValidTowerLocation(tile, territory, grid));
    }

    [Fact]
    public void IsValidTowerLocation_AcceptsTileAtDistance2FromFriendlyTower()
    {
        // Tower at (0,0). Proposed at (2,0). Same as the adjacent case —
        // humans aren't bound by the AI's spacing heuristic.
        (HexGrid grid, Territory territory) = BuildLinearStrip(5, new HexCoord(4, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Tower();
        HexTile tile = grid.Get(new HexCoord(2, 0))!;

        Assert.True(PurchaseRules.IsValidTowerLocation(tile, territory, grid));
    }

    [Fact]
    public void IsValidTowerLocation_AcceptsTileAtDistance3FromFriendlyTower()
    {
        // Sanity: a comfortably distant placement is still accepted.
        (HexGrid grid, Territory territory) = BuildLinearStrip(5, new HexCoord(4, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Tower();
        HexTile tile = grid.Get(new HexCoord(3, 0))!;

        Assert.True(PurchaseRules.IsValidTowerLocation(tile, territory, grid));
    }

    // --- BuyPeasant --------------------------------------------------------

    [Fact]
    public void BuyPeasant_DeductsTenGold()
    {
        var capital = new HexCoord(0, 0);
        Territory territory = MakeTerritory(
            Red, capital,
            new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(0, 1));
        var tile = new HexTile(new HexCoord(1, 0), Red);
        var treasury = new Treasury();
        treasury.SetGold(capital, 42);

        PurchaseRules.BuyPeasant(tile, territory, treasury);

        Assert.Equal(32, treasury.GetGold(capital));
    }

    [Fact]
    public void BuyPeasant_PlacesPeasantOwnedByTerritoryOwner()
    {
        var capital = new HexCoord(0, 0);
        Territory territory = MakeTerritory(
            Red, capital,
            new HexCoord(0, 0), new HexCoord(1, 0));
        var tile = new HexTile(new HexCoord(1, 0), Red);
        var treasury = new Treasury();
        treasury.SetGold(capital, 20);

        PurchaseRules.BuyPeasant(tile, territory, treasury);

        Assert.NotNull(tile.Unit);
        Assert.Equal(Red, tile.Unit!.Owner);
    }
}
