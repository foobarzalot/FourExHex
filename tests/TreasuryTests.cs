using System.Collections.Generic;
using Godot;
using Xunit;

namespace FourExHex.Tests;

public class TreasuryTests
{
    private static readonly Color Red = new Color(1f, 0f, 0f);
    private static readonly Color Blue = new Color(0f, 0f, 1f);

    private static Player RedPlayer => new Player("Red", Red);
    private static Player BluePlayer => new Player("Blue", Blue);

    private static Territory MakeTerritory(Color owner, HexCoord capital, params HexCoord[] coords) =>
        new Territory(owner, coords, capital);

    private static Territory Singleton(Color owner, HexCoord coord) =>
        new Territory(owner, new[] { coord }, capital: null);

    [Fact]
    public void GetGold_UnknownCapital_ReturnsZero()
    {
        var treasury = new Treasury();

        int gold = treasury.GetGold(new HexCoord(3, 5));

        Assert.Equal(0, gold);
    }

    [Fact]
    public void SetGold_ThenGet_ReturnsValue()
    {
        var treasury = new Treasury();
        var capital = new HexCoord(1, 2);

        treasury.SetGold(capital, 42);

        Assert.Equal(42, treasury.GetGold(capital));
    }

    [Fact]
    public void CollectIncomeFor_SingleTerritory_AddsSizeGoldToCapital()
    {
        var capital = new HexCoord(5, 5);
        var territory = MakeTerritory(
            Red, capital,
            new HexCoord(5, 5),
            new HexCoord(6, 5),
            new HexCoord(5, 6),
            new HexCoord(6, 6),
            new HexCoord(4, 6));
        var treasury = new Treasury();

        treasury.CollectIncomeFor(RedPlayer, new[] { territory });

        Assert.Equal(5, treasury.GetGold(capital));
    }

    [Fact]
    public void CollectIncomeFor_IgnoresSingletons()
    {
        // Size-1 territories have no capital (CapitalAssigner returns null),
        // so there's nothing to credit. The big territory should still get paid.
        Territory singleton = Singleton(Red, new HexCoord(0, 0));
        var bigCapital = new HexCoord(5, 5);
        Territory big = MakeTerritory(
            Red, bigCapital,
            new HexCoord(5, 5),
            new HexCoord(6, 5),
            new HexCoord(5, 6));
        var treasury = new Treasury();

        treasury.CollectIncomeFor(RedPlayer, new[] { singleton, big });

        Assert.Equal(3, treasury.GetGold(bigCapital));
        Assert.Equal(0, treasury.GetGold(new HexCoord(0, 0)));
    }

    [Fact]
    public void CollectIncomeFor_OnlyMatchesPlayerColor()
    {
        var redCapital = new HexCoord(1, 1);
        var blueCapital = new HexCoord(7, 7);
        Territory red = MakeTerritory(Red, redCapital, new HexCoord(1, 1), new HexCoord(2, 1));
        Territory blue = MakeTerritory(Blue, blueCapital, new HexCoord(7, 7), new HexCoord(8, 7));
        var treasury = new Treasury();

        treasury.CollectIncomeFor(RedPlayer, new[] { red, blue });

        Assert.Equal(2, treasury.GetGold(redCapital));
        Assert.Equal(0, treasury.GetGold(blueCapital));
    }

    [Fact]
    public void CollectIncomeFor_MultipleTerritoriesSamePlayer_EachAccumulates()
    {
        var capitalA = new HexCoord(0, 0);
        var capitalB = new HexCoord(10, 10);
        Territory a = MakeTerritory(
            Red, capitalA,
            new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(0, 1));
        Territory b = MakeTerritory(
            Red, capitalB,
            new HexCoord(10, 10), new HexCoord(11, 10),
            new HexCoord(10, 11), new HexCoord(11, 11));
        var treasury = new Treasury();

        treasury.CollectIncomeFor(RedPlayer, new[] { a, b });

        Assert.Equal(3, treasury.GetGold(capitalA));
        Assert.Equal(4, treasury.GetGold(capitalB));
    }

    [Fact]
    public void CollectIncomeFor_CalledTwice_AccumulatesCorrectly()
    {
        var capital = new HexCoord(0, 0);
        Territory t = MakeTerritory(
            Red, capital,
            new HexCoord(0, 0), new HexCoord(1, 0));
        var treasury = new Treasury();

        treasury.CollectIncomeFor(RedPlayer, new[] { t });
        treasury.CollectIncomeFor(RedPlayer, new[] { t });

        Assert.Equal(4, treasury.GetGold(capital));
    }

    [Fact]
    public void CollectIncomeFor_DoesNotTouchOtherPlayerCapitals()
    {
        // Pre-seed Blue's capital with gold. Collecting Red's income must
        // leave it alone.
        var redCapital = new HexCoord(1, 1);
        var blueCapital = new HexCoord(7, 7);
        Territory red = MakeTerritory(Red, redCapital, new HexCoord(1, 1), new HexCoord(2, 1));
        Territory blue = MakeTerritory(Blue, blueCapital, new HexCoord(7, 7), new HexCoord(8, 7));
        var treasury = new Treasury();
        treasury.SetGold(blueCapital, 99);

        treasury.CollectIncomeFor(RedPlayer, new[] { red, blue });

        Assert.Equal(99, treasury.GetGold(blueCapital));
    }
}
