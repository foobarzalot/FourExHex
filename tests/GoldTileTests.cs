using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Gold hex tiles: a per-tile <see cref="HexTile.IsGold"/> flag
/// that pays a tile 5x the ordinary per-turn income (5 gp instead of 1) via the
/// single income chokepoint <see cref="IncomeRules.IncomeFor"/>.
/// </summary>
public class GoldTileTests
{
    private static readonly PlayerId Red = PlayerId.FromIndex(0);

    private static HexGrid BuildRow(int qMin, int qMax)
    {
        var grid = new HexGrid();
        for (int q = qMin; q <= qMax; q++)
        {
            grid.Add(new HexTile(new HexCoord(q, 0), Red));
        }
        return grid;
    }

    private static Territory RowTerritory(int qMin, int qMax)
    {
        var coords = new List<HexCoord>();
        for (int q = qMin; q <= qMax; q++) coords.Add(new HexCoord(q, 0));
        return new Territory(Red, coords, capital: new HexCoord(qMin, 0));
    }

    // --- CountGoldIncomeTiles --------------------------------------------

    [Fact]
    public void CountGoldIncomeTiles_CountsOnlyGoldIncomeProducingTiles()
    {
        HexGrid grid = BuildRow(0, 4);
        grid.Get(new HexCoord(0, 0))!.IsGold = true;                    // gold, empty → counts
        grid.Get(new HexCoord(1, 0))!.IsGold = true;
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red);         // gold + unit → counts
        grid.Get(new HexCoord(2, 0))!.IsGold = true;
        grid.Get(new HexCoord(2, 0))!.Occupant = new Tree();            // gold + tree → excluded
        // (3,0) gold-less; (4,0) gold-less
        var territory = RowTerritory(0, 4);

        Assert.Equal(2, TreeRules.CountGoldIncomeTiles(territory, grid));
    }

    [Fact]
    public void CountGoldIncomeTiles_ExcludesGravesOnGold()
    {
        HexGrid grid = BuildRow(0, 1);
        grid.Get(new HexCoord(0, 0))!.IsGold = true;
        grid.Get(new HexCoord(0, 0))!.Occupant = new Grave();
        grid.Get(new HexCoord(1, 0))!.IsGold = true;
        var territory = RowTerritory(0, 1);

        Assert.Equal(1, TreeRules.CountGoldIncomeTiles(territory, grid));
    }

    // --- IncomeFor: the 5x bonus -----------------------------------------

    [Fact]
    public void IncomeFor_AllOrdinaryTiles_Unchanged()
    {
        HexGrid grid = BuildRow(0, 2);
        var territory = RowTerritory(0, 2);

        Assert.Equal(3, IncomeRules.IncomeFor(territory, grid));
    }

    [Fact]
    public void IncomeFor_OneGoldTile_Pays5x()
    {
        // 3 tiles, one gold → 5 + 1 + 1 = 7.
        HexGrid grid = BuildRow(0, 2);
        grid.Get(new HexCoord(0, 0))!.IsGold = true;
        var territory = RowTerritory(0, 2);

        Assert.Equal(7, IncomeRules.IncomeFor(territory, grid));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("unit")]
    [InlineData("capital")]
    [InlineData("tower")]
    public void IncomeFor_GoldWithNonBlockingOccupant_Pays5(string? occupant)
    {
        HexGrid grid = BuildRow(0, 0);
        HexTile tile = grid.Get(new HexCoord(0, 0))!;
        tile.IsGold = true;
        tile.Occupant = occupant switch
        {
            "unit" => new Unit(Red),
            "capital" => new Capital(),
            "tower" => new Tower(),
            _ => null,
        };
        var territory = RowTerritory(0, 0);

        Assert.Equal(5, IncomeRules.IncomeFor(territory, grid));
    }

    [Theory]
    [InlineData("tree")]
    [InlineData("grave")]
    public void IncomeFor_GoldWithTreeOrGrave_Pays0(string occupant)
    {
        HexGrid grid = BuildRow(0, 0);
        HexTile tile = grid.Get(new HexCoord(0, 0))!;
        tile.IsGold = true;
        tile.Occupant = occupant == "tree" ? new Tree() : new Grave();
        var territory = RowTerritory(0, 0);

        Assert.Equal(0, IncomeRules.IncomeFor(territory, grid));
    }

    [Fact]
    public void IncomeFor_MixedTerritory_SumsBaseAndGoldBonus()
    {
        // 5 tiles: 2 gold (one empty, one with a unit), 1 gold+tree (excluded),
        // 2 ordinary. Base income-producing tiles = 4 (gold+tree excluded);
        // gold income tiles = 2 → total 4 + 2*4 = 12.
        HexGrid grid = BuildRow(0, 4);
        grid.Get(new HexCoord(0, 0))!.IsGold = true;
        grid.Get(new HexCoord(1, 0))!.IsGold = true;
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red);
        grid.Get(new HexCoord(2, 0))!.IsGold = true;
        grid.Get(new HexCoord(2, 0))!.Occupant = new Tree();
        var territory = RowTerritory(0, 4);

        Assert.Equal(12, IncomeRules.IncomeFor(territory, grid));
    }

    // --- Snapshot deep-copy (undo/redo) ----------------------------------

    [Fact]
    public void GameStateSnapshot_RoundTrips_IsGold()
    {
        HexGrid grid = BuildRow(0, 2);
        grid.Get(new HexCoord(0, 0))!.IsGold = true;
        var territories = new List<Territory> { RowTerritory(0, 2) };
        var treasury = new Treasury();

        GameStateSnapshot snap = GameStateSnapshot.Capture(grid, treasury, territories);

        // Mutate after capture: clear gold here, set gold there.
        grid.Get(new HexCoord(0, 0))!.IsGold = false;
        grid.Get(new HexCoord(2, 0))!.IsGold = true;

        snap.ApplyTo(grid, treasury);

        Assert.True(grid.Get(new HexCoord(0, 0))!.IsGold);
        Assert.False(grid.Get(new HexCoord(2, 0))!.IsGold);
    }

    [Fact]
    public void EditorSnapshot_RoundTrips_IsGold()
    {
        HexGrid grid = BuildRow(0, 2);
        grid.Get(new HexCoord(1, 0))!.IsGold = true;
        var water = new HashSet<HexCoord>();
        var territories = new List<Territory> { RowTerritory(0, 2) };

        EditorSnapshot snap = EditorSnapshot.Capture(grid, water, territories);

        grid.Get(new HexCoord(1, 0))!.IsGold = false;

        snap.ApplyTo(grid, water);

        Assert.True(grid.Get(new HexCoord(1, 0))!.IsGold);
    }
}
