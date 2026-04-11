using System.Collections.Generic;
using Godot;
using Xunit;

namespace FourExHex.Tests;

public class UpkeepRulesTests
{
    private static readonly Color Red = new Color(1f, 0f, 0f);

    private static HexGrid BuildGridOf(params HexCoord[] coords) =>
        TestHelpers.BuildSpotGrid(Red, coords);

    private static Territory BuildTerritory(HexCoord? capital, params HexCoord[] coords) =>
        new Territory(Red, coords, capital);

    // --- UpkeepFor --------------------------------------------------------

    [Theory]
    [InlineData(UnitLevel.Peasant,  2)]
    [InlineData(UnitLevel.Spearman, 6)]
    [InlineData(UnitLevel.Knight,   18)]
    [InlineData(UnitLevel.Baron,    54)]
    public void UpkeepFor_KnownLevels(UnitLevel level, int expected)
    {
        Assert.Equal(expected, UpkeepRules.UpkeepFor(level));
    }

    // --- TotalUpkeepFor ---------------------------------------------------

    [Fact]
    public void TotalUpkeepFor_EmptyTerritory_IsZero()
    {
        HexGrid grid = BuildGridOf(new HexCoord(0, 0), new HexCoord(1, 0));
        Territory t = BuildTerritory(new HexCoord(0, 0), new HexCoord(0, 0), new HexCoord(1, 0));

        Assert.Equal(0, UpkeepRules.TotalUpkeepFor(t, grid));
    }

    [Fact]
    public void TotalUpkeepFor_OnePeasant_IsTwo()
    {
        HexGrid grid = BuildGridOf(new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red);
        Territory t = BuildTerritory(new HexCoord(0, 0), new HexCoord(0, 0), new HexCoord(1, 0));

        Assert.Equal(2, UpkeepRules.TotalUpkeepFor(t, grid));
    }

    [Fact]
    public void TotalUpkeepFor_MixedLevels_SumsCorrectly()
    {
        // Peasant (2) + Knight (18) = 20
        HexGrid grid = BuildGridOf(
            new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(0, 1));
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red);
        grid.Get(new HexCoord(0, 1))!.Occupant = new Unit(Red, UnitLevel.Knight);
        Territory t = BuildTerritory(
            new HexCoord(0, 0),
            new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(0, 1));

        Assert.Equal(20, UpkeepRules.TotalUpkeepFor(t, grid));
    }

    // --- ApplyUpkeep ------------------------------------------------------

    [Fact]
    public void ApplyUpkeep_SufficientGold_DeductsAndKeepsUnits()
    {
        HexGrid grid = BuildGridOf(new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red);
        Territory t = BuildTerritory(new HexCoord(0, 0), new HexCoord(0, 0), new HexCoord(1, 0));
        var treasury = new Treasury();
        treasury.SetGold(new HexCoord(0, 0), 10);

        bool paid = UpkeepRules.ApplyUpkeep(t, grid, treasury);

        Assert.True(paid);
        Assert.Equal(8, treasury.GetGold(new HexCoord(0, 0))); // 10 - 2
        Assert.NotNull(grid.Get(new HexCoord(1, 0))!.Unit);
    }

    [Fact]
    public void ApplyUpkeep_InsufficientGold_ReplacesUnitsWithGraves_AndLeavesGoldAlone()
    {
        HexGrid grid = BuildGridOf(new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(0, 1));
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red, UnitLevel.Knight); // upkeep 18
        grid.Get(new HexCoord(0, 1))!.Occupant = new Unit(Red); // upkeep 2
        Territory t = BuildTerritory(
            new HexCoord(0, 0),
            new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(0, 1));
        var treasury = new Treasury();
        treasury.SetGold(new HexCoord(0, 0), 5); // far less than 20 owed

        bool paid = UpkeepRules.ApplyUpkeep(t, grid, treasury);

        Assert.False(paid);
        Assert.IsType<Grave>(grid.Get(new HexCoord(1, 0))!.Occupant);
        Assert.IsType<Grave>(grid.Get(new HexCoord(0, 1))!.Occupant);
        // Gold untouched.
        Assert.Equal(5, treasury.GetGold(new HexCoord(0, 0)));
    }

    [Fact]
    public void ApplyUpkeep_NoUnits_NoOp()
    {
        HexGrid grid = BuildGridOf(new HexCoord(0, 0), new HexCoord(1, 0));
        Territory t = BuildTerritory(new HexCoord(0, 0), new HexCoord(0, 0), new HexCoord(1, 0));
        var treasury = new Treasury();
        treasury.SetGold(new HexCoord(0, 0), 7);

        bool paid = UpkeepRules.ApplyUpkeep(t, grid, treasury);

        Assert.True(paid);
        Assert.Equal(7, treasury.GetGold(new HexCoord(0, 0)));
    }

    [Fact]
    public void ApplyUpkeep_SingletonTerritoryWithUnit_UnitBecomesGrave()
    {
        // A singleton has no capital and therefore no treasury. Any unit
        // on it has 0 gold available < its upkeep, so it dies and leaves
        // a grave.
        HexGrid grid = BuildGridOf(new HexCoord(0, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Unit(Red);
        Territory singleton = BuildTerritory(capital: null, new HexCoord(0, 0));
        var treasury = new Treasury();

        bool paid = UpkeepRules.ApplyUpkeep(singleton, grid, treasury);

        Assert.False(paid);
        Assert.IsType<Grave>(grid.Get(new HexCoord(0, 0))!.Occupant);
    }

    [Fact]
    public void ApplyUpkeep_BankruptcyKeepsCapitalOccupant()
    {
        // Territory with a Capital on one tile and a Knight on the other;
        // bankrupt. Only the knight should die — the capital stays.
        HexGrid grid = BuildGridOf(new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Capital();
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red, UnitLevel.Knight);
        Territory t = BuildTerritory(new HexCoord(0, 0), new HexCoord(0, 0), new HexCoord(1, 0));
        var treasury = new Treasury();
        treasury.SetGold(new HexCoord(0, 0), 0);

        bool paid = UpkeepRules.ApplyUpkeep(t, grid, treasury);

        Assert.False(paid);
        Assert.IsType<Capital>(grid.Get(new HexCoord(0, 0))!.Occupant);
        Assert.Null(grid.Get(new HexCoord(1, 0))!.Unit);
    }

    // --- ApplyUpkeepFor ---------------------------------------------------

    [Fact]
    public void ApplyUpkeepFor_OnlyAffectsMatchingPlayer()
    {
        var blue = new Color(0f, 0f, 1f);
        var redPlayer = new Player("Red", Red);

        HexGrid grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Add(new HexTile(new HexCoord(1, 0), Red));
        grid.Add(new HexTile(new HexCoord(5, 0), blue));
        grid.Add(new HexTile(new HexCoord(6, 0), blue));
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red);
        grid.Get(new HexCoord(6, 0))!.Occupant = new Unit(blue);

        Territory redT = new Territory(Red, new[] { new HexCoord(0, 0), new HexCoord(1, 0) }, new HexCoord(0, 0));
        Territory blueT = new Territory(blue, new[] { new HexCoord(5, 0), new HexCoord(6, 0) }, new HexCoord(5, 0));
        var treasury = new Treasury();
        treasury.SetGold(new HexCoord(0, 0), 10);
        treasury.SetGold(new HexCoord(5, 0), 10);

        UpkeepRules.ApplyUpkeepFor(redPlayer, new[] { redT, blueT }, grid, treasury);

        Assert.Equal(8, treasury.GetGold(new HexCoord(0, 0)));  // Red paid 2
        Assert.Equal(10, treasury.GetGold(new HexCoord(5, 0))); // Blue untouched
    }
}
