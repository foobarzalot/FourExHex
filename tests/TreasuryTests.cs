using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

public class TreasuryTests
{
    private static readonly PlayerId Red = PlayerId.FromIndex(0);
    private static readonly PlayerId Blue = PlayerId.FromIndex(1);

    private static Player RedPlayer => new Player("Red", PlayerId.FromIndex(0));
    private static Player BluePlayer => new Player("Blue", PlayerId.FromIndex(1));

    private static Territory MakeTerritory(PlayerId owner, HexCoord capital, params HexCoord[] coords) =>
        new Territory(owner, coords, capital);

    private static Territory Singleton(PlayerId owner, HexCoord coord) =>
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

    [Fact]
    public void CollectIncomeFor_WithGrid_SkipsTreeTiles()
    {
        // 4-tile territory with two trees. Income should be 2 not 4.
        var capital = new HexCoord(0, 0);
        Territory t = MakeTerritory(
            Red, capital,
            new HexCoord(0, 0), new HexCoord(1, 0),
            new HexCoord(2, 0), new HexCoord(3, 0));
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Add(new HexTile(new HexCoord(1, 0), Red));
        grid.Add(new HexTile(new HexCoord(2, 0), Red));
        grid.Add(new HexTile(new HexCoord(3, 0), Red));
        grid.Get(new HexCoord(1, 0))!.Occupant = new Tree();
        grid.Get(new HexCoord(2, 0))!.Occupant = new Tree();

        var treasury = new Treasury();
        treasury.CollectIncomeFor(RedPlayer, new[] { t }, grid);

        Assert.Equal(2, treasury.GetGold(capital));
    }

    [Fact]
    public void CollectIncomeFor_WithGrid_TowerTileStillCountsAsIncome()
    {
        // Trees and graves block income; capital, unit, and tower tiles
        // still pay out. This test locks the tower behavior in against
        // a possible future regression where trees and towers get
        // lumped together.
        var capital = new HexCoord(0, 0);
        Territory t = MakeTerritory(
            Red, capital,
            new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(2, 0));
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Add(new HexTile(new HexCoord(1, 0), Red));
        grid.Add(new HexTile(new HexCoord(2, 0), Red));
        grid.Get(new HexCoord(1, 0))!.Occupant = new Tower();

        var treasury = new Treasury();
        treasury.CollectIncomeFor(RedPlayer, new[] { t }, grid);

        Assert.Equal(3, treasury.GetGold(capital));
    }

    [Fact]
    public void CollectIncomeFor_WithGrid_SkipsGraveTiles()
    {
        // Graves block income just like trees. 4-tile territory with
        // two graves → 2 gold credited.
        var capital = new HexCoord(0, 0);
        Territory t = MakeTerritory(
            Red, capital,
            new HexCoord(0, 0), new HexCoord(1, 0),
            new HexCoord(2, 0), new HexCoord(3, 0));
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Add(new HexTile(new HexCoord(1, 0), Red));
        grid.Add(new HexTile(new HexCoord(2, 0), Red));
        grid.Add(new HexTile(new HexCoord(3, 0), Red));
        grid.Get(new HexCoord(1, 0))!.Occupant = new Grave();
        grid.Get(new HexCoord(2, 0))!.Occupant = new Grave();

        var treasury = new Treasury();
        treasury.CollectIncomeFor(RedPlayer, new[] { t }, grid);

        Assert.Equal(2, treasury.GetGold(capital));
    }

    [Fact]
    public void CollectIncomeFor_WithoutGrid_UsesTerritorySize()
    {
        // Back-compat: no grid → fall back to Size. This matches the
        // existing test expectations for the other CollectIncomeFor tests.
        var capital = new HexCoord(0, 0);
        Territory t = MakeTerritory(
            Red, capital,
            new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(2, 0));
        var treasury = new Treasury();

        treasury.CollectIncomeFor(RedPlayer, new[] { t });

        Assert.Equal(3, treasury.GetGold(capital));
    }

    [Fact]
    public void CollectIncomeFor_CommanderDifficulty_IncomeIsFlat100Percent_WithGrid()
    {
        // Hard levels don't boost income — their lever is the cheaper-upkeep
        // table. 8 income tiles at Commander difficulty earn exactly 8 gold.
        var capital = new HexCoord(0, 0);
        var coords = new HexCoord[8];
        var grid = new HexGrid();
        for (int c = 0; c < 8; c++)
        {
            coords[c] = new HexCoord(c, 0);
            grid.Add(new HexTile(coords[c], Red));
        }
        Territory t = new Territory(Red, coords, capital);
        var commanderRed = new Player("Red", Red, PlayerKind.Computer, Difficulty.Commander);

        var treasury = new Treasury();
        treasury.CollectIncomeFor(commanderRed, new[] { t }, grid);

        Assert.Equal(8, treasury.GetGold(capital));
    }

    [Fact]
    public void CollectIncomeFor_RecruitDifficulty_HalvesIncomeTruncating_WithGrid()
    {
        // 3 income tiles, Recruit → 3/2 = 1 (truncation, not rounding).
        var capital = new HexCoord(0, 0);
        Territory t = MakeTerritory(
            Red, capital,
            new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(2, 0));
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Add(new HexTile(new HexCoord(1, 0), Red));
        grid.Add(new HexTile(new HexCoord(2, 0), Red));
        var recruitRed = new Player("Red", Red, PlayerKind.Computer, Difficulty.Recruit);

        var treasury = new Treasury();
        treasury.CollectIncomeFor(recruitRed, new[] { t }, grid);

        Assert.Equal(1, treasury.GetGold(capital));
    }

    [Fact]
    public void CollectIncomeFor_RecruitDifficulty_HalvesSizeFallback_WithoutGrid()
    {
        // No grid → income falls back to Size (3), Recruit → 1.
        var capital = new HexCoord(0, 0);
        Territory t = MakeTerritory(
            Red, capital,
            new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(2, 0));
        var recruitRed = new Player("Red", Red, PlayerKind.Computer, Difficulty.Recruit);

        var treasury = new Treasury();
        treasury.CollectIncomeFor(recruitRed, new[] { t });

        Assert.Equal(1, treasury.GetGold(capital));
    }

    [Fact]
    public void CollectIncomeFor_DefaultDifficultyIsSoldier()
    {
        // A player built without an explicit difficulty earns 1× (unchanged).
        var capital = new HexCoord(0, 0);
        Territory t = MakeTerritory(
            Red, capital,
            new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(2, 0));

        var treasury = new Treasury();
        treasury.CollectIncomeFor(RedPlayer, new[] { t });

        Assert.Equal(3, treasury.GetGold(capital));
    }

    // --- ReconcileAfterCapture -------------------------------------------

    [Fact]
    public void Reconcile_UnchangedTerritory_KeepsGold()
    {
        var capital = new HexCoord(0, 0);
        Territory before = MakeTerritory(Red, capital, new HexCoord(0, 0), new HexCoord(1, 0));
        Territory after = MakeTerritory(Red, capital, new HexCoord(0, 0), new HexCoord(1, 0));
        var treasury = new Treasury();
        treasury.SetGold(capital, 50);

        treasury.ReconcileAfterCapture(new[] { before }, new[] { after });

        Assert.Equal(50, treasury.GetGold(capital));
    }

    [Fact]
    public void Reconcile_Split_PieceWithOldCapital_KeepsGold()
    {
        // Old: one territory with capital (0,0), size 3.
        // New: split into two pieces. Piece A contains the old capital and
        // gets it back; piece B is a new territory with its own capital
        // that starts at 0.
        var oldCap = new HexCoord(0, 0);
        Territory before = MakeTerritory(Red, oldCap,
            new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(5, 5));
        var treasury = new Treasury();
        treasury.SetGold(oldCap, 30);

        // After the split, two new territories. Piece A still contains
        // (0,0) so its capital is (0,0); piece B has capital (5,5).
        var pieceA = MakeTerritory(Red, oldCap, new HexCoord(0, 0), new HexCoord(1, 0));
        var pieceB = MakeTerritory(Red, new HexCoord(5, 5), new HexCoord(5, 5), new HexCoord(5, 6));

        treasury.ReconcileAfterCapture(new[] { before }, new[] { pieceA, pieceB });

        Assert.Equal(30, treasury.GetGold(oldCap));
        Assert.Equal(0, treasury.GetGold(new HexCoord(5, 5)));
    }

    [Fact]
    public void Reconcile_Merge_TwoOldCapitals_SumGoldIntoNewCapital()
    {
        // Old: two red territories with capitals (0,0) and (5,5), 20g and
        // 30g respectively.
        // New: they're merged (by a bridging capture). One new territory
        // with its capital wherever CapitalAssigner picks — say (0,0).
        var before1 = MakeTerritory(Red, new HexCoord(0, 0),
            new HexCoord(0, 0), new HexCoord(1, 0));
        var before2 = MakeTerritory(Red, new HexCoord(5, 5),
            new HexCoord(5, 5), new HexCoord(5, 6));
        var treasury = new Treasury();
        treasury.SetGold(new HexCoord(0, 0), 20);
        treasury.SetGold(new HexCoord(5, 5), 30);

        // New merged territory contains all five coords; capital is (0,0).
        var merged = MakeTerritory(Red, new HexCoord(0, 0),
            new HexCoord(0, 0), new HexCoord(1, 0),
            new HexCoord(2, 0),  // the bridging capture
            new HexCoord(5, 5), new HexCoord(5, 6));

        treasury.ReconcileAfterCapture(new[] { before1, before2 }, new[] { merged });

        Assert.Equal(50, treasury.GetGold(new HexCoord(0, 0)));
        // The old (5,5) capital's key is gone from the treasury.
        Assert.Equal(0, treasury.GetGold(new HexCoord(5, 5)));
    }

    [Fact]
    public void Reconcile_OldCapitalCapturedByEnemy_GoldIsForfeit()
    {
        // Old: red territory with capital (0,0) and 100g.
        // New: red territory shrunk (no longer includes (0,0) — enemy took
        // it). The remaining red piece gets a fresh capital at 0g.
        var before = MakeTerritory(Red, new HexCoord(0, 0),
            new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(2, 0));
        var treasury = new Treasury();
        treasury.SetGold(new HexCoord(0, 0), 100);

        // After: (0,0) is now owned by blue (not in this new-territory list
        // as a red territory). Red's remaining piece has a new capital at
        // (1,0).
        var afterRed = MakeTerritory(Red, new HexCoord(1, 0),
            new HexCoord(1, 0), new HexCoord(2, 0));

        treasury.ReconcileAfterCapture(new[] { before }, new[] { afterRed });

        // Red's new capital starts at 0 gold because the old one is gone
        // from the new partition entirely.
        Assert.Equal(0, treasury.GetGold(new HexCoord(1, 0)));
        // The old capital key no longer holds any gold.
        Assert.Equal(0, treasury.GetGold(new HexCoord(0, 0)));
    }

    [Fact]
    public void Reconcile_AttackerCapturesEnemyCapitalTile_DoesNotInheritEnemyGold()
    {
        // Real-world capture: TerritoryFinder returns BOTH sides, so the
        // attacker's expanded territory is in the new-territory list AND
        // contains the enemy capital coord. The attacker must NOT inherit
        // the enemy's treasury — taking an enemy capital destroys it
        // (Slay's rule), it doesn't transfer. Regression test for a bug
        // where ReconcileAfterCapture inherited gold by coord without
        // checking owner, so capturing an enemy capital tile credited
        // that enemy's gold to the attacker.
        var redCap = new HexCoord(0, 0);
        var blueCap = new HexCoord(1, 0);

        Territory redBefore = MakeTerritory(Red, redCap,
            new HexCoord(0, 0), new HexCoord(0, 1));
        Territory blueBefore = MakeTerritory(Blue, blueCap,
            new HexCoord(1, 0), new HexCoord(2, 0));

        var treasury = new Treasury();
        treasury.SetGold(redCap, 3);
        treasury.SetGold(blueCap, 12);

        // After: red expanded into (1,0); blue shrank to a singleton at (2,0).
        Territory redAfter = MakeTerritory(Red, redCap,
            new HexCoord(0, 0), new HexCoord(0, 1), new HexCoord(1, 0));
        Territory blueAfter = Singleton(Blue, new HexCoord(2, 0));

        treasury.ReconcileAfterCapture(
            new[] { redBefore, blueBefore },
            new[] { redAfter, blueAfter });

        // Red keeps exactly its own pre-capture gold — no transfer from blue.
        Assert.Equal(3, treasury.GetGold(redCap));
        // Blue's old capital coord no longer holds any gold.
        Assert.Equal(0, treasury.GetGold(blueCap));
    }
}
