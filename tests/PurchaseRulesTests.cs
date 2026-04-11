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
    public void Unit_Constructor_StoresLevelAndOwner()
    {
        var unit = new Unit(UnitLevel.Peasant, Red);

        Assert.Equal(UnitLevel.Peasant, unit.Level);
        Assert.Equal(Red, unit.Owner);
    }

    [Fact]
    public void Unit_HasMovedThisTurn_DefaultsFalse()
    {
        var unit = new Unit(UnitLevel.Peasant, Red);

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
            Unit = new Unit(UnitLevel.Peasant, Red),
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
    public void IsValidPeasantTarget_OnOwnCapital_ReturnsFalse()
    {
        // Can't stand on top of your own capital in Slay.
        var capital = new HexCoord(0, 0);
        Territory territory = MakeTerritory(
            Red, capital,
            new HexCoord(0, 0), new HexCoord(1, 0));
        var capitalTile = new HexTile(new HexCoord(0, 0), Red);

        Assert.False(PurchaseRules.IsValidPeasantTarget(capitalTile, territory));
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
        Assert.Equal(UnitLevel.Peasant, tile.Unit!.Level);
        Assert.Equal(Red, tile.Unit.Owner);
    }
}
