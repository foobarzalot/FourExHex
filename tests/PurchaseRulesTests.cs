// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using Xunit;

namespace FourExHex.Tests;

public class PurchaseRulesTests
{
    private static readonly PlayerId Red = PlayerId.FromIndex(0);

    private static Territory MakeTerritory(PlayerId owner, HexCoord capital, params HexCoord[] coords) =>
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

    // --- CostFor: base × tier per difficulty -------------------------------

    // Unit cost = DifficultyRules.UnitBaseCost × tier (1/2/3/4). Soldier
    // base 10 reproduces the classic 10/20/30/40 ladder the AIs always pay;
    // Recruit (easy) base 8, Captain 13, Commander 15.
    [Theory]
    [InlineData(Difficulty.Recruit, UnitLevel.Recruit, 8)]
    [InlineData(Difficulty.Recruit, UnitLevel.Commander, 32)]
    [InlineData(Difficulty.Soldier, UnitLevel.Recruit, 10)]
    [InlineData(Difficulty.Soldier, UnitLevel.Soldier, 20)]
    [InlineData(Difficulty.Soldier, UnitLevel.Captain, 30)]
    [InlineData(Difficulty.Soldier, UnitLevel.Commander, 40)]
    [InlineData(Difficulty.Captain, UnitLevel.Recruit, 13)]
    [InlineData(Difficulty.Captain, UnitLevel.Commander, 52)]
    [InlineData(Difficulty.Commander, UnitLevel.Recruit, 15)]
    [InlineData(Difficulty.Commander, UnitLevel.Soldier, 30)]
    [InlineData(Difficulty.Commander, UnitLevel.Captain, 45)]
    [InlineData(Difficulty.Commander, UnitLevel.Commander, 60)]
    public void CostFor_BaseTimesTierPerDifficulty(Difficulty d, UnitLevel level, int expected)
    {
        Assert.Equal(expected, PurchaseRules.CostFor(level, d));
    }

    [Theory]
    [InlineData(Difficulty.Recruit, 12)]
    [InlineData(Difficulty.Soldier, 15)]
    [InlineData(Difficulty.Captain, 18)]
    [InlineData(Difficulty.Commander, 20)]
    public void TowerCostFor_PerDifficulty(Difficulty d, int expected)
    {
        Assert.Equal(expected, PurchaseRules.TowerCostFor(d));
    }

    [Fact]
    public void CanAfford_SameGold_FlipsBetweenSoldierAndCommander()
    {
        // 44 gold buys a Captain unit at Soldier difficulty (30) but not at
        // Commander (45) — affordability sees the buyer's difficulty.
        Territory territory = MakeTerritory(Red, new HexCoord(0, 0),
            new HexCoord(0, 0), new HexCoord(1, 0));
        var treasury = new Treasury();
        treasury.SetGold(territory.Capital!.Value, 44);

        Assert.True(PurchaseRules.CanAfford(territory, treasury, UnitLevel.Captain, Difficulty.Soldier));
        Assert.False(PurchaseRules.CanAfford(territory, treasury, UnitLevel.Captain, Difficulty.Commander));
    }

    [Fact]
    public void CanAffordTower_SameGold_FlipsBetweenSoldierAndCommander()
    {
        // 19 gold builds a tower at Soldier (15) but not at Commander (20).
        Territory territory = MakeTerritory(Red, new HexCoord(0, 0),
            new HexCoord(0, 0), new HexCoord(1, 0));
        var treasury = new Treasury();
        treasury.SetGold(territory.Capital!.Value, 19);

        Assert.True(PurchaseRules.CanAffordTower(territory, treasury, Difficulty.Soldier));
        Assert.False(PurchaseRules.CanAffordTower(territory, treasury, Difficulty.Commander));
    }

    // --- CanAffordRecruit -------------------------------------------------

    [Theory]
    [InlineData(0, false)]
    [InlineData(5, false)]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(11, true)]
    [InlineData(100, true)]
    public void CanAffordRecruit_GoldThreshold(int gold, bool expected)
    {
        var capital = new HexCoord(0, 0);
        Territory territory = MakeTerritory(Red, capital, new HexCoord(0, 0), new HexCoord(1, 0));
        var treasury = new Treasury();
        treasury.SetGold(capital, gold);

        bool result = PurchaseRules.CanAffordRecruit(territory, treasury, Difficulty.Soldier);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void CanAffordRecruit_SingletonTerritory_ReturnsFalse()
    {
        // A singleton has no capital so the treasury can't hold gold for it;
        // CanAfford must not blow up trying to dereference a null Capital.
        var singleton = new Territory(Red, new[] { new HexCoord(0, 0) }, capital: null);
        var treasury = new Treasury();

        bool result = PurchaseRules.CanAffordRecruit(singleton, treasury, Difficulty.Soldier);

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

        Assert.Equal(expected, PurchaseRules.CanAffordTower(territory, treasury, Difficulty.Soldier));
    }

    [Fact]
    public void CanAffordTower_SingletonTerritory_ReturnsFalse()
    {
        var singleton = new Territory(Red, new[] { new HexCoord(0, 0) }, capital: null);
        var treasury = new Treasury();

        Assert.False(PurchaseRules.CanAffordTower(singleton, treasury, Difficulty.Soldier));
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

    // --- IsValidTowerLocationWithPush / TowerPushDestination ---------------

    [Fact]
    public void IsValidTowerLocation_RejectsOwnFreeUnitTile()
    {
        // The strict rule is the human click/preview gate: even a unit
        // that could be pushed aside keeps the tile invalid for humans.
        (HexGrid grid, Territory territory) = BuildLinearStrip(4, new HexCoord(3, 0));
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red);
        HexTile tile = grid.Get(new HexCoord(1, 0))!;

        Assert.False(PurchaseRules.IsValidTowerLocation(tile, territory, grid));
    }

    [Fact]
    public void IsValidTowerLocationWithPush_AcceptsEmptyTile()
    {
        (HexGrid grid, Territory territory) = BuildLinearStrip(4, new HexCoord(3, 0));
        HexTile tile = grid.Get(new HexCoord(1, 0))!;

        Assert.True(PurchaseRules.IsValidTowerLocationWithPush(tile, territory, grid));
    }

    [Fact]
    public void IsValidTowerLocationWithPush_AcceptsOwnFreeUnitTileWithEscape()
    {
        (HexGrid grid, Territory territory) = BuildLinearStrip(4, new HexCoord(3, 0));
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red);
        HexTile tile = grid.Get(new HexCoord(1, 0))!;

        Assert.True(PurchaseRules.IsValidTowerLocationWithPush(tile, territory, grid));
    }

    [Fact]
    public void IsValidTowerLocationWithPush_RejectsMovedUnitTile()
    {
        (HexGrid grid, Territory territory) = BuildLinearStrip(4, new HexCoord(3, 0));
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red) { HasMovedThisTurn = true };
        HexTile tile = grid.Get(new HexCoord(1, 0))!;

        Assert.False(PurchaseRules.IsValidTowerLocationWithPush(tile, territory, grid));
    }

    [Fact]
    public void IsValidTowerLocationWithPush_RejectsFreeUnitTileWithNoEscape()
    {
        // Strip is one tile wide, so (1,0)'s only in-territory
        // neighbors are (0,0) and (2,0); block both.
        (HexGrid grid, Territory territory) = BuildLinearStrip(4, new HexCoord(3, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Tree();
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red);
        grid.Get(new HexCoord(2, 0))!.Occupant = new Tree();
        HexTile tile = grid.Get(new HexCoord(1, 0))!;

        Assert.False(PurchaseRules.IsValidTowerLocationWithPush(tile, territory, grid));
    }

    [Fact]
    public void IsValidTowerLocationWithPush_RejectsNonUnitOccupants()
    {
        (HexGrid grid, Territory territory) = BuildLinearStrip(4, new HexCoord(3, 0));
        HexTile tile = grid.Get(new HexCoord(1, 0))!;

        tile.Occupant = new Tree();
        Assert.False(PurchaseRules.IsValidTowerLocationWithPush(tile, territory, grid));
        tile.Occupant = new Grave();
        Assert.False(PurchaseRules.IsValidTowerLocationWithPush(tile, territory, grid));
        tile.Occupant = new Tower();
        Assert.False(PurchaseRules.IsValidTowerLocationWithPush(tile, territory, grid));
        tile.Occupant = new Capital();
        Assert.False(PurchaseRules.IsValidTowerLocationWithPush(tile, territory, grid));
    }

    [Fact]
    public void IsValidTowerLocationWithPush_RejectsForeignUnitTile()
    {
        // Defensive: a unit whose owner differs from the territory's
        // is never pushed (shouldn't arise on own-territory tiles).
        (HexGrid grid, Territory territory) = BuildLinearStrip(4, new HexCoord(3, 0));
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(PlayerId.FromIndex(1));
        HexTile tile = grid.Get(new HexCoord(1, 0))!;

        Assert.False(PurchaseRules.IsValidTowerLocationWithPush(tile, territory, grid));
    }

    [Fact]
    public void TowerPushDestination_PicksLexMinEligibleNeighbor()
    {
        // (1,0)'s in-territory empty neighbors are (0,0) and (2,0);
        // HexCoord orders by R then Q, so (0,0) wins.
        (HexGrid grid, Territory territory) = BuildLinearStrip(4, new HexCoord(3, 0));
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red);

        Assert.Equal(new HexCoord(0, 0),
            PurchaseRules.TowerPushDestination(new HexCoord(1, 0), territory, grid));
    }

    [Fact]
    public void TowerPushDestination_SkipsOccupiedNeighbors()
    {
        (HexGrid grid, Territory territory) = BuildLinearStrip(4, new HexCoord(3, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Tree();
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red);

        Assert.Equal(new HexCoord(2, 0),
            PurchaseRules.TowerPushDestination(new HexCoord(1, 0), territory, grid));
    }

    [Fact]
    public void TowerPushDestination_NullWhenBoxedIn()
    {
        (HexGrid grid, Territory territory) = BuildLinearStrip(4, new HexCoord(3, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Tree();
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red);
        grid.Get(new HexCoord(2, 0))!.Occupant = new Tree();

        Assert.Null(PurchaseRules.TowerPushDestination(new HexCoord(1, 0), territory, grid));
    }

}
