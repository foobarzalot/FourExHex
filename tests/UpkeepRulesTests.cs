using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

public class UpkeepRulesTests
{
    private static readonly PlayerId Red = PlayerId.FromIndex(0);

    private static HexGrid BuildGridOf(params HexCoord[] coords) =>
        TestHelpers.BuildSpotGrid(Red, coords);

    private static Territory BuildTerritory(HexCoord? capital, params HexCoord[] coords) =>
        new Territory(Red, coords, capital);

    // --- UpkeepFor --------------------------------------------------------

    [Theory]
    [InlineData(UnitLevel.Recruit,  2)]
    [InlineData(UnitLevel.Soldier, 6)]
    [InlineData(UnitLevel.Captain,   18)]
    [InlineData(UnitLevel.Commander,    54)]
    public void UpkeepFor_KnownLevels(UnitLevel level, int expected)
    {
        Assert.Equal(expected, UpkeepRules.UpkeepFor(level, Difficulty.Soldier));
    }

    // --- TotalUpkeepFor ---------------------------------------------------

    [Fact]
    public void TotalUpkeepFor_EmptyTerritory_IsZero()
    {
        HexGrid grid = BuildGridOf(new HexCoord(0, 0), new HexCoord(1, 0));
        Territory t = BuildTerritory(new HexCoord(0, 0), new HexCoord(0, 0), new HexCoord(1, 0));

        Assert.Equal(0, UpkeepRules.TotalUpkeepFor(t, grid, Difficulty.Soldier));
    }

    [Fact]
    public void TotalUpkeepFor_OneRecruit_IsTwo()
    {
        HexGrid grid = BuildGridOf(new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red);
        Territory t = BuildTerritory(new HexCoord(0, 0), new HexCoord(0, 0), new HexCoord(1, 0));

        Assert.Equal(2, UpkeepRules.TotalUpkeepFor(t, grid, Difficulty.Soldier));
    }

    [Fact]
    public void TotalUpkeepFor_MixedLevels_SumsCorrectly()
    {
        // Recruit (2) + Captain (18) = 20
        HexGrid grid = BuildGridOf(
            new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(0, 1));
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red);
        grid.Get(new HexCoord(0, 1))!.Occupant = new Unit(Red, UnitLevel.Captain);
        Territory t = BuildTerritory(
            new HexCoord(0, 0),
            new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(0, 1));

        Assert.Equal(20, UpkeepRules.TotalUpkeepFor(t, grid, Difficulty.Soldier));
    }

    [Fact]
    public void TotalUpkeepFor_CommanderDifficulty_SumsScaledPerUnitValues()
    {
        // Commander (54→27) + Recruit (2→1) at Commander difficulty = 28
        // (vs 56 at Soldier).
        HexGrid grid = BuildGridOf(
            new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(0, 1));
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red, UnitLevel.Commander);
        grid.Get(new HexCoord(0, 1))!.Occupant = new Unit(Red);
        Territory t = BuildTerritory(
            new HexCoord(0, 0),
            new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(0, 1));

        Assert.Equal(28, UpkeepRules.TotalUpkeepFor(t, grid, Difficulty.Commander));
    }

    [Fact]
    public void ApplyUpkeep_CommanderDifficulty_ChargesScaledAmount()
    {
        // One Captain: owed 9 at Commander difficulty (vs 18 at Soldier).
        // Gold 10 covers 9 → pays, 1 left, unit survives.
        HexGrid grid = BuildGridOf(new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red, UnitLevel.Captain);
        Territory t = BuildTerritory(new HexCoord(0, 0), new HexCoord(0, 0), new HexCoord(1, 0));
        var treasury = new Treasury();
        treasury.SetGold(new HexCoord(0, 0), 10);

        Assert.True(UpkeepRules.ApplyUpkeep(t, grid, treasury, Difficulty.Commander));
        Assert.Equal(1, treasury.GetGold(new HexCoord(0, 0)));
        Assert.IsType<Unit>(grid.Get(new HexCoord(1, 0))!.Occupant);
    }

    [Fact]
    public void ApplyUpkeep_SameGold_BankruptAtSoldierButSolventAtCommander()
    {
        // The same 10 gold that sustains a Captain at Commander difficulty
        // (owed 9) is bankrupt at Soldier (owed 18) — units die.
        HexGrid grid = BuildGridOf(new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red, UnitLevel.Captain);
        Territory t = BuildTerritory(new HexCoord(0, 0), new HexCoord(0, 0), new HexCoord(1, 0));
        var treasury = new Treasury();
        treasury.SetGold(new HexCoord(0, 0), 10);

        Assert.False(UpkeepRules.ApplyUpkeep(t, grid, treasury, Difficulty.Soldier));
        Assert.IsType<Grave>(grid.Get(new HexCoord(1, 0))!.Occupant);
    }

    [Fact]
    public void ApplyUpkeepFor_UsesThePlayersDifficulty()
    {
        // A Commander-difficulty player's territory pays the scaled rate via
        // the Player overload — proves ApplyUpkeepFor reads player.Difficulty.
        HexGrid grid = BuildGridOf(new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red, UnitLevel.Captain);
        Territory t = BuildTerritory(new HexCoord(0, 0), new HexCoord(0, 0), new HexCoord(1, 0));
        var treasury = new Treasury();
        treasury.SetGold(new HexCoord(0, 0), 10);
        var commanderRed = new Player("Red", Red, PlayerKind.Computer, Difficulty.Commander);

        bool anyBankrupt = UpkeepRules.ApplyUpkeepFor(commanderRed, new[] { t }, grid, treasury);

        Assert.False(anyBankrupt);
        Assert.Equal(1, treasury.GetGold(new HexCoord(0, 0)));
    }

    // --- ForecastBankruptNextTurn ----------------------------------------

    [Fact]
    public void Forecast_GoldPlusIncomeCoversUpkeep_False()
    {
        // Captain upkeep 18, 2 income tiles (capital + captain, no trees),
        // gold 20 -> 20 + 2 = 22 >= 18, solvent next turn.
        HexGrid grid = BuildGridOf(new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red, UnitLevel.Captain);
        Territory t = BuildTerritory(new HexCoord(0, 0), new HexCoord(0, 0), new HexCoord(1, 0));
        var treasury = new Treasury();
        treasury.SetGold(new HexCoord(0, 0), 20);

        Assert.False(UpkeepRules.ForecastBankruptNextTurn(t, grid, treasury, Difficulty.Soldier));
    }

    [Fact]
    public void Forecast_GoldPlusIncomeShortOfUpkeep_True()
    {
        // Captain upkeep 18, income 2, gold 5 -> 5 + 2 = 7 < 18: bankrupt.
        HexGrid grid = BuildGridOf(new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red, UnitLevel.Captain);
        Territory t = BuildTerritory(new HexCoord(0, 0), new HexCoord(0, 0), new HexCoord(1, 0));
        var treasury = new Treasury();
        treasury.SetGold(new HexCoord(0, 0), 5);

        Assert.True(UpkeepRules.ForecastBankruptNextTurn(t, grid, treasury, Difficulty.Soldier));
    }

    [Fact]
    public void Forecast_GoldPlusIncomeExactlyEqualsUpkeep_False()
    {
        // Boundary: income 2, gold 16 -> 18 == owed 18; ApplyUpkeep pays
        // when available >= owed, so this is NOT bankrupt.
        HexGrid grid = BuildGridOf(new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red, UnitLevel.Captain);
        Territory t = BuildTerritory(new HexCoord(0, 0), new HexCoord(0, 0), new HexCoord(1, 0));
        var treasury = new Treasury();
        treasury.SetGold(new HexCoord(0, 0), 16);

        Assert.False(UpkeepRules.ForecastBankruptNextTurn(t, grid, treasury, Difficulty.Soldier));
    }

    [Fact]
    public void Forecast_NoUnits_ZeroGold_False()
    {
        // owed == 0 -> ApplyUpkeep returns true regardless of gold.
        HexGrid grid = BuildGridOf(new HexCoord(0, 0), new HexCoord(1, 0));
        Territory t = BuildTerritory(new HexCoord(0, 0), new HexCoord(0, 0), new HexCoord(1, 0));
        var treasury = new Treasury();
        treasury.SetGold(new HexCoord(0, 0), 0);

        Assert.False(UpkeepRules.ForecastBankruptNextTurn(t, grid, treasury, Difficulty.Soldier));
    }

    [Fact]
    public void Forecast_NoCapital_False()
    {
        // No capital -> no economic-report label is ever shown for it, so
        // the forecast is intentionally scoped out even though a real
        // upkeep step would wipe the unit.
        HexGrid grid = BuildGridOf(new HexCoord(0, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Unit(Red);
        Territory singleton = BuildTerritory(capital: null, new HexCoord(0, 0));
        var treasury = new Treasury();

        Assert.False(UpkeepRules.ForecastBankruptNextTurn(singleton, grid, treasury, Difficulty.Soldier));
    }

    // --- Classify ---------------------------------------------------------

    [Fact]
    public void Classify_NetPositive_Healthy()
    {
        // Recruit upkeep 2, 2 income tiles -> income 2 == upkeep 2, net 0.
        // Actually net positive: use a 3-tile territory with 1 recruit.
        HexGrid grid = BuildGridOf(new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(0, 1));
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red); // upkeep 2
        Territory t = BuildTerritory(new HexCoord(0, 0),
            new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(0, 1));
        var treasury = new Treasury();
        treasury.SetGold(new HexCoord(0, 0), 0);
        // income 3, upkeep 2 -> net +1 -> healthy.

        Assert.Equal(EconomyOutlook.Healthy, UpkeepRules.Classify(t, grid, treasury, Difficulty.Soldier));
    }

    [Fact]
    public void Classify_IncomeEqualsUpkeep_Healthy()
    {
        // 2-tile territory, 1 recruit: income 2, upkeep 2 -> net 0.
        // Pays exactly; not bleeding -> Healthy.
        HexGrid grid = BuildGridOf(new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red); // upkeep 2
        Territory t = BuildTerritory(new HexCoord(0, 0), new HexCoord(0, 0), new HexCoord(1, 0));
        var treasury = new Treasury();
        treasury.SetGold(new HexCoord(0, 0), 0);

        Assert.Equal(EconomyOutlook.Healthy, UpkeepRules.Classify(t, grid, treasury, Difficulty.Soldier));
    }

    [Fact]
    public void Classify_NetNegativeButReservesCover_NegativeDelta()
    {
        // Captain upkeep 18, 2 income tiles -> income 2 < upkeep 18, but
        // gold 20 -> 20 + 2 = 22 >= 18 covers next turn. Bleeding.
        HexGrid grid = BuildGridOf(new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red, UnitLevel.Captain);
        Territory t = BuildTerritory(new HexCoord(0, 0), new HexCoord(0, 0), new HexCoord(1, 0));
        var treasury = new Treasury();
        treasury.SetGold(new HexCoord(0, 0), 20);

        Assert.Equal(EconomyOutlook.NegativeDelta, UpkeepRules.Classify(t, grid, treasury, Difficulty.Soldier));
    }

    [Fact]
    public void Classify_NetNegativeReservesExactlyCover_NegativeDelta()
    {
        // Boundary: gold + income exactly equals upkeep (pays in full),
        // but income < upkeep -> still bleeding -> NegativeDelta.
        HexGrid grid = BuildGridOf(new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red, UnitLevel.Captain); // upkeep 18
        Territory t = BuildTerritory(new HexCoord(0, 0), new HexCoord(0, 0), new HexCoord(1, 0));
        var treasury = new Treasury();
        treasury.SetGold(new HexCoord(0, 0), 16); // 16 + 2 income = 18 == upkeep

        Assert.Equal(EconomyOutlook.NegativeDelta, UpkeepRules.Classify(t, grid, treasury, Difficulty.Soldier));
    }

    [Fact]
    public void Classify_ReservesShortOfUpkeep_BankruptNextTurn()
    {
        // gold 5, income 2, upkeep 18 -> 7 < 18 -> bankrupt next turn.
        HexGrid grid = BuildGridOf(new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red, UnitLevel.Captain);
        Territory t = BuildTerritory(new HexCoord(0, 0), new HexCoord(0, 0), new HexCoord(1, 0));
        var treasury = new Treasury();
        treasury.SetGold(new HexCoord(0, 0), 5);

        Assert.Equal(EconomyOutlook.BankruptNextTurn, UpkeepRules.Classify(t, grid, treasury, Difficulty.Soldier));
    }

    [Fact]
    public void Classify_NoUnits_Healthy()
    {
        HexGrid grid = BuildGridOf(new HexCoord(0, 0), new HexCoord(1, 0));
        Territory t = BuildTerritory(new HexCoord(0, 0), new HexCoord(0, 0), new HexCoord(1, 0));
        var treasury = new Treasury();
        treasury.SetGold(new HexCoord(0, 0), 0);

        Assert.Equal(EconomyOutlook.Healthy, UpkeepRules.Classify(t, grid, treasury, Difficulty.Soldier));
    }

    [Fact]
    public void Classify_NoCapital_Healthy()
    {
        // No capital -> no treasury, no label, scoped out. Same convention
        // as ForecastBankruptNextTurn.
        HexGrid grid = BuildGridOf(new HexCoord(0, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Unit(Red);
        Territory singleton = BuildTerritory(capital: null, new HexCoord(0, 0));
        var treasury = new Treasury();

        Assert.Equal(EconomyOutlook.Healthy, UpkeepRules.Classify(singleton, grid, treasury, Difficulty.Soldier));
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

        bool paid = UpkeepRules.ApplyUpkeep(t, grid, treasury, Difficulty.Soldier);

        Assert.True(paid);
        Assert.Equal(8, treasury.GetGold(new HexCoord(0, 0))); // 10 - 2
        Assert.NotNull(grid.Get(new HexCoord(1, 0))!.Unit);
    }

    [Fact]
    public void ApplyUpkeep_InsufficientGold_ReplacesUnitsWithGraves_AndLeavesGoldAlone()
    {
        HexGrid grid = BuildGridOf(new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(0, 1));
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red, UnitLevel.Captain); // upkeep 18
        grid.Get(new HexCoord(0, 1))!.Occupant = new Unit(Red); // upkeep 2
        Territory t = BuildTerritory(
            new HexCoord(0, 0),
            new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(0, 1));
        var treasury = new Treasury();
        treasury.SetGold(new HexCoord(0, 0), 5); // far less than 20 owed

        bool paid = UpkeepRules.ApplyUpkeep(t, grid, treasury, Difficulty.Soldier);

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

        bool paid = UpkeepRules.ApplyUpkeep(t, grid, treasury, Difficulty.Soldier);

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

        bool paid = UpkeepRules.ApplyUpkeep(singleton, grid, treasury, Difficulty.Soldier);

        Assert.False(paid);
        Assert.IsType<Grave>(grid.Get(new HexCoord(0, 0))!.Occupant);
    }

    [Fact]
    public void ApplyUpkeep_BankruptcyKeepsCapitalOccupant()
    {
        // Territory with a Capital on one tile and a Captain on the other;
        // bankrupt. Only the captain should die — the capital stays.
        HexGrid grid = BuildGridOf(new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Capital();
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red, UnitLevel.Captain);
        Territory t = BuildTerritory(new HexCoord(0, 0), new HexCoord(0, 0), new HexCoord(1, 0));
        var treasury = new Treasury();
        treasury.SetGold(new HexCoord(0, 0), 0);

        bool paid = UpkeepRules.ApplyUpkeep(t, grid, treasury, Difficulty.Soldier);

        Assert.False(paid);
        Assert.IsType<Capital>(grid.Get(new HexCoord(0, 0))!.Occupant);
        Assert.Null(grid.Get(new HexCoord(1, 0))!.Unit);
    }

    // --- ApplyUpkeepFor ---------------------------------------------------

    [Fact]
    public void ApplyUpkeepFor_ReturnsTrueWhenAnyTerritoryWentBankrupt()
    {
        var redPlayer = new Player("Red", PlayerId.FromIndex(0));
        HexGrid grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Add(new HexTile(new HexCoord(1, 0), Red));
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red);

        Territory redT = new Territory(Red, new[] { new HexCoord(0, 0), new HexCoord(1, 0) }, new HexCoord(0, 0));
        var treasury = new Treasury();
        treasury.SetGold(new HexCoord(0, 0), 0); // can't pay upkeep

        bool anyBankrupt = UpkeepRules.ApplyUpkeepFor(redPlayer, new[] { redT }, grid, treasury);

        Assert.True(anyBankrupt);
        Assert.IsType<Grave>(grid.Get(new HexCoord(1, 0))!.Occupant);
    }

    [Fact]
    public void ApplyUpkeepFor_ReturnsFalseWhenAllTerritoriesAffordUpkeep()
    {
        var redPlayer = new Player("Red", PlayerId.FromIndex(0));
        HexGrid grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Add(new HexTile(new HexCoord(1, 0), Red));
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red);

        Territory redT = new Territory(Red, new[] { new HexCoord(0, 0), new HexCoord(1, 0) }, new HexCoord(0, 0));
        var treasury = new Treasury();
        treasury.SetGold(new HexCoord(0, 0), 100); // plenty

        bool anyBankrupt = UpkeepRules.ApplyUpkeepFor(redPlayer, new[] { redT }, grid, treasury);

        Assert.False(anyBankrupt);
        Assert.IsType<Unit>(grid.Get(new HexCoord(1, 0))!.Occupant);
    }

    [Fact]
    public void ApplyUpkeepFor_OnlyAffectsMatchingPlayer()
    {
        var blue = PlayerId.FromIndex(1);
        var redPlayer = new Player("Red", PlayerId.FromIndex(0));

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
