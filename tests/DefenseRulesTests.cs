using Godot;
using Xunit;

namespace FourExHex.Tests;

public class DefenseRulesTests
{
    private static readonly Color Red = new Color(1f, 0f, 0f);

    [Fact]
    public void Defense_EmptyNonCapitalTile_IsZero()
    {
        var tile = new HexTile(new HexCoord(0, 0), Red);

        Assert.Equal(0, DefenseRules.Defense(tile, isCapital: false));
    }

    [Fact]
    public void Defense_CapitalTile_IsOne()
    {
        var tile = new HexTile(new HexCoord(0, 0), Red);

        Assert.Equal(1, DefenseRules.Defense(tile, isCapital: true));
    }

    [Fact]
    public void Defense_TileWithPeasant_IsOne()
    {
        var tile = new HexTile(new HexCoord(0, 0), Red)
        {
            Unit = new Unit(UnitLevel.Peasant, Red),
        };

        Assert.Equal(1, DefenseRules.Defense(tile, isCapital: false));
    }

    [Fact]
    public void Defense_TileWithSpearman_IsTwo()
    {
        // Locks in that the defense function honors unit level, not just
        // "there's a unit here".
        var tile = new HexTile(new HexCoord(0, 0), Red)
        {
            Unit = new Unit(UnitLevel.Spearman, Red),
        };

        Assert.Equal(2, DefenseRules.Defense(tile, isCapital: false));
    }
}
