using System.Collections.Generic;
using Godot;
using Xunit;

namespace FourExHex.Tests;

public class DefenseRulesTests
{
    private static readonly Color Red = new Color(1f, 0f, 0f);
    private static readonly Color Blue = new Color(0f, 0f, 1f);

    /// <summary>
    /// Build a small grid consisting of the given coords, all colored
    /// <paramref name="color"/>. Returns the grid and the single territory
    /// covering all of them.
    /// </summary>
    private static (HexGrid grid, Territory territory) BuildBlob(
        Color color,
        HexCoord? capital,
        params HexCoord[] coords)
    {
        var grid = new HexGrid();
        foreach (HexCoord c in coords)
        {
            grid.Add(new HexTile(c, color));
        }
        var territory = new Territory(color, coords, capital);
        return (grid, territory);
    }

    // --- Baseline --------------------------------------------------------

    [Fact]
    public void Defense_SingleEmptyTile_IsZero()
    {
        (HexGrid grid, Territory territory) = BuildBlob(Red, null, new HexCoord(0, 0));

        Assert.Equal(0, DefenseRules.Defense(new HexCoord(0, 0), grid, territory));
    }

    [Fact]
    public void Defense_TileWithOwnCapital_IsOne()
    {
        (HexGrid grid, Territory territory) = BuildBlob(
            Red, new HexCoord(0, 0),
            new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Capital();

        Assert.Equal(1, DefenseRules.Defense(new HexCoord(0, 0), grid, territory));
    }

    [Fact]
    public void Defense_TileWithOwnPeasant_IsOne()
    {
        (HexGrid grid, Territory territory) = BuildBlob(
            Red, null,
            new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Unit(Red);

        Assert.Equal(1, DefenseRules.Defense(new HexCoord(0, 0), grid, territory));
    }

    [Fact]
    public void Defense_TileWithOwnSpearman_IsTwo()
    {
        (HexGrid grid, Territory territory) = BuildBlob(
            Red, null,
            new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Unit(Red, UnitLevel.Spearman);

        Assert.Equal(2, DefenseRules.Defense(new HexCoord(0, 0), grid, territory));
    }

    [Fact]
    public void Defense_TileWithOwnBaron_IsFour()
    {
        (HexGrid grid, Territory territory) = BuildBlob(
            Red, null,
            new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Unit(Red, UnitLevel.Baron);

        Assert.Equal(4, DefenseRules.Defense(new HexCoord(0, 0), grid, territory));
    }

    // --- Radiation -------------------------------------------------------

    [Fact]
    public void Defense_TileAdjacentToOwnCapital_RadiatesOne()
    {
        // Two-tile red territory; capital on (0,0). Defense of (1,0) should
        // be 1 (radiated from the capital), even though (1,0) is empty.
        (HexGrid grid, Territory territory) = BuildBlob(
            Red, new HexCoord(0, 0),
            new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Capital();

        Assert.Equal(1, DefenseRules.Defense(new HexCoord(1, 0), grid, territory));
    }

    [Fact]
    public void Defense_TileAdjacentToOwnPeasant_RadiatesOne()
    {
        (HexGrid grid, Territory territory) = BuildBlob(
            Red, null,
            new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Unit(Red);

        Assert.Equal(1, DefenseRules.Defense(new HexCoord(1, 0), grid, territory));
    }

    [Fact]
    public void Defense_TileAdjacentToOwnKnight_RadiatesThree()
    {
        (HexGrid grid, Territory territory) = BuildBlob(
            Red, null,
            new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Unit(Red, UnitLevel.Knight);

        Assert.Equal(3, DefenseRules.Defense(new HexCoord(1, 0), grid, territory));
    }

    [Fact]
    public void Defense_MaxOverPeasantAndBaron_IsFour()
    {
        // Three-tile territory where (1,0) is between a peasant-held and
        // a baron-held tile. Defense of (1,0) = max(1, 4) = 4.
        var coords = new[]
        {
            new HexCoord(0, 0),   // W neighbor
            new HexCoord(1, 0),   // target
            new HexCoord(2, -1),  // NE neighbor
        };
        (HexGrid grid, Territory territory) = BuildBlob(Red, null, coords);
        grid.Get(new HexCoord(0, 0))!.Occupant = new Unit(Red);
        grid.Get(new HexCoord(2, -1))!.Occupant = new Unit(Red, UnitLevel.Baron);

        Assert.Equal(4, DefenseRules.Defense(new HexCoord(1, 0), grid, territory));
    }

    [Fact]
    public void Defense_AdjacentPeasantAndCapital_Max_IsOne()
    {
        // Every contributor is level 1 right now, so max(1, 1) = 1. The
        // point of this test is that the max function is applied at all
        // (and not, e.g., summed to 2).
        var coords = new[]
        {
            new HexCoord(0, 0),   // W neighbor of (1, 0)
            new HexCoord(1, 0),   // target
            new HexCoord(2, -1),  // NE neighbor of (1, 0)
        };
        (HexGrid grid, Territory territory) = BuildBlob(Red, new HexCoord(0, 0), coords);
        grid.Get(new HexCoord(0, 0))!.Occupant = new Capital();
        grid.Get(new HexCoord(2, -1))!.Occupant = new Unit(Red);

        Assert.Equal(1, DefenseRules.Defense(new HexCoord(1, 0), grid, territory));
    }

    // --- Territory scope -------------------------------------------------

    [Fact]
    public void Defense_AdjacentEnemyUnit_IsIgnored()
    {
        // Red tile at (0,0), Blue peasant at (1,0). The Blue unit does not
        // contribute to Red's defense of (0,0) because it's not in Red's
        // territory.
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Add(new HexTile(new HexCoord(1, 0), Blue));
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Blue);

        var redTerritory = new Territory(Red, new[] { new HexCoord(0, 0) }, capital: null);

        Assert.Equal(0, DefenseRules.Defense(new HexCoord(0, 0), grid, redTerritory));
    }

    [Fact]
    public void Defense_AdjacentSameColorSiblingTerritory_IsIgnored()
    {
        // Same color, different territory. The sibling's unit doesn't
        // radiate because it isn't in leftRed.Coords.
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Add(new HexTile(new HexCoord(1, 0), Blue));
        grid.Add(new HexTile(new HexCoord(2, 0), Red));
        grid.Add(new HexTile(new HexCoord(3, 0), Red));
        grid.Get(new HexCoord(2, 0))!.Occupant = new Unit(Red);

        var leftRed = new Territory(Red, new[] { new HexCoord(0, 0) }, capital: null);

        Assert.Equal(0, DefenseRules.Defense(new HexCoord(0, 0), grid, leftRed));
    }

    // --- Towers ----------------------------------------------------------

    [Fact]
    public void Defense_TileWithOwnTower_IsTwo()
    {
        (HexGrid grid, Territory territory) = BuildBlob(
            Red, null,
            new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Tower();

        Assert.Equal(2, DefenseRules.Defense(new HexCoord(0, 0), grid, territory));
    }

    [Fact]
    public void Defense_TileAdjacentToOwnTower_IsTwo_ViaRadiation()
    {
        // Tower at (0,0) radiates to same-territory neighbors, so the
        // empty (1,0) tile inherits defense 2 from its neighbor.
        (HexGrid grid, Territory territory) = BuildBlob(
            Red, null,
            new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Tower();

        Assert.Equal(2, DefenseRules.Defense(new HexCoord(1, 0), grid, territory));
    }

    [Fact]
    public void Defense_AdjacentEnemyTower_DoesNotRadiateAcrossTerritories()
    {
        // Red at (0,0). Blue at (1,0) (adjacent, enemy). Blue tile has a
        // tower. A capture target computation treats (0,0)'s defense from
        // Red's perspective — the enemy tower on (1,0) MUST NOT radiate
        // into Red's territory, so Red's (0,0) is defense 0.
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Add(new HexTile(new HexCoord(1, 0), Blue));
        grid.Get(new HexCoord(1, 0))!.Occupant = new Tower();

        var redTerritory = new Territory(Red, new[] { new HexCoord(0, 0) }, capital: null);

        Assert.Equal(0, DefenseRules.Defense(new HexCoord(0, 0), grid, redTerritory));
    }

    [Fact]
    public void Defense_TowerPlusPeasantOnSameTile_IsTwo_NotThree()
    {
        // Contributions don't stack — the max wins. A peasant (1) plus a
        // tower (2) on overlapping coverage is still 2.
        (HexGrid grid, Territory territory) = BuildBlob(
            Red, null,
            new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Tower();
        // Adjacent peasant that radiates 1 into (0,0) — tower already
        // gives 2 so we expect 2, not 3.
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red);

        Assert.Equal(2, DefenseRules.Defense(new HexCoord(0, 0), grid, territory));
    }

    [Fact]
    public void Defense_KnightNextToTower_TileIsThree_ViaKnightMaxWins()
    {
        // A knight (3) on an adjacent same-territory tile beats the
        // tower's radiated 2 on the subject tile.
        (HexGrid grid, Territory territory) = BuildBlob(
            Red, null,
            new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Tower();
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red, UnitLevel.Knight);

        Assert.Equal(3, DefenseRules.Defense(new HexCoord(0, 0), grid, territory));
    }

    [Fact]
    public void Defense_TwoAdjacentTowers_DoNotStackBeyondTwo()
    {
        (HexGrid grid, Territory territory) = BuildBlob(
            Red, null,
            new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Tower();
        grid.Get(new HexCoord(1, 0))!.Occupant = new Tower();

        Assert.Equal(2, DefenseRules.Defense(new HexCoord(0, 0), grid, territory));
        Assert.Equal(2, DefenseRules.Defense(new HexCoord(1, 0), grid, territory));
    }
}
