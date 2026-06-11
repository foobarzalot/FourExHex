using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

public partial class GameControllerTests
{
    // --- Buy recruit ------------------------------------------------------

    [Fact]
    public void BuyRecruit_OnOwnEmptyTile_DeductsGoldAndPlacesUnit()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1)); // select Red
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        int goldBefore = g.State.Treasury.GetGold(redCapital);

        g.Hud.ClickBuyRecruit();
        Assert.Equal(SessionState.ActionMode.BuyingRecruit, g.Session.Mode);

        // (1,1) is in Red but not Red's capital ((0,1) is) and is empty.
        g.Map.SimulateClick(g.Tile(1, 1));

        Assert.NotNull(g.Tile(1, 1).Unit);
        Assert.Equal(g.Red.Id, g.Tile(1, 1).Unit!.Owner);
        Assert.Equal(goldBefore - PurchaseRules.CostFor(UnitLevel.Recruit, Difficulty.Soldier), g.State.Treasury.GetGold(redCapital));
        // Buy-on-own-tile doesn't consume the unit's action.
        Assert.False(g.Tile(1, 1).Unit!.HasMovedThisTurn);
        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
    }

    // --- Move + capture ---------------------------------------------------

    [Fact]
    public void Move_CaptureEnemyTile_ChangesOwnershipAndMarksMoved()
    {
        var g = new TestGame();
        var unit = new Unit(g.Red.Id);
        g.Tile(1, 1).Occupant = unit;

        g.Map.SimulateClick(g.Tile(1, 1));
        Assert.Equal(SessionState.ActionMode.MovingUnit, g.Session.Mode);

        // (2,1) is Blue, not Blue's capital, empty → capturable by recruit.
        g.Map.SimulateClick(g.Tile(2, 1));

        Assert.Equal(g.Red.Id, g.Tile(2, 1).Owner);
        Assert.Same(unit, g.Tile(2, 1).Unit);
        Assert.Null(g.Tile(1, 1).Unit);
        Assert.True(unit.HasMovedThisTurn);
        // After a capture the reconciler rebuilds — rebuild count should
        // be at least 1.
        Assert.True(g.Map.RebuildCount >= 1);
    }

    [Fact]
    public void Move_Capture_KeepsSelection_OnAttackerNewTerritory()
    {
        // QoL: after a capture the selection should track over to
        // whichever new territory contains the captured tile, instead
        // of being cleared. The user clicked on a Red territory, moved
        // a unit into Blue space, and should still see Red selected.
        var g = new TestGame();
        var unit = new Unit(g.Red.Id);
        g.Tile(1, 1).Occupant = unit;

        g.Map.SimulateClick(g.Tile(1, 1));
        g.Map.SimulateClick(g.Tile(2, 1)); // capture Blue hex

        Assert.NotNull(g.Session.SelectedTerritory);
        Assert.Equal(g.Red.Id, g.Session.SelectedTerritory!.Owner);
        // The new territory should contain the captured tile.
        Assert.Contains(HexCoord.FromOffset(2, 1), g.Session.SelectedTerritory.Coords);
        // And the highlight on the map should point to the same territory.
        Assert.Same(g.Session.SelectedTerritory, g.Map.LastHighlight);
    }

    [Fact]
    public void Move_CaptureEnemyUnit_FiresDestructionEffectOnView()
    {
        // Move-capture onto an enemy unit dispatches a single
        // PlayDestructionEffect for the displaced defender.
        var g = new TestGame();
        var attacker = new Unit(g.Red.Id, UnitLevel.Soldier);
        g.Tile(1, 1).Occupant = attacker;
        var defender = new Unit(g.Blue.Id, UnitLevel.Recruit);
        g.Tile(2, 1).Occupant = defender;

        g.Map.SimulateClick(g.Tile(1, 1));
        g.Map.SimulateClick(g.Tile(2, 1));

        Assert.Single(g.Map.DestructionEffects);
        Assert.Equal(HexCoord.FromOffset(2, 1), g.Map.DestructionEffects[0].Coord);
        Assert.Same(defender, g.Map.DestructionEffects[0].Destroyed);
    }

    [Fact]
    public void Move_CaptureEmptyEnemyTile_DoesNotFireDestructionEffect()
    {
        // Capturing an empty enemy tile flips its color but destroys
        // nothing — no FX should fire.
        var g = new TestGame();
        g.Tile(1, 1).Occupant = new Unit(g.Red.Id);

        g.Map.SimulateClick(g.Tile(1, 1));
        g.Map.SimulateClick(g.Tile(2, 1));

        Assert.Empty(g.Map.DestructionEffects);
    }

    [Fact]
    public void BuyRecruit_CaptureEmptyEnemyTile_DoesNotFireDestructionEffect()
    {
        var g = new TestGame();
        HexCoord redCapital = g.RedTerritory.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 25);
        g.Map.SimulateClick(g.Tile(0, 1));
        g.Hud.ClickBuyRecruit();
        g.Map.SimulateClick(g.Tile(2, 1));

        Assert.Empty(g.Map.DestructionEffects);
    }

    // --- Build tower ------------------------------------------------------

    [Fact]
    public void BuildTower_OnOwnEmptyTile_DeductsFifteenGoldAndPlacesTower()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1)); // select Red
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        // Red starts with 10g; give it enough to build.
        g.State.Treasury.SetGold(redCapital, 20);
        int before = g.State.Treasury.GetGold(redCapital);

        g.Hud.ClickBuildTower();
        Assert.Equal(SessionState.ActionMode.BuildingTower, g.Session.Mode);

        // (1,1) is an empty tile in Red's territory ((0,1) is capital).
        g.Map.SimulateClick(g.Tile(1, 1));

        Assert.IsType<Tower>(g.Tile(1, 1).Occupant);
        Assert.Equal(before - PurchaseRules.TowerCostFor(Difficulty.Soldier), g.State.Treasury.GetGold(redCapital));
        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
    }

    [Fact]
    public void BuildTower_CantAfford_ButtonIsNoOp()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 5); // not enough for 15g tower

        g.Hud.ClickBuildTower();

        // Should not have entered BuildingTower mode.
        Assert.NotEqual(SessionState.ActionMode.BuildingTower, g.Session.Mode);
    }

    [Fact]
    public void BuildTower_OnOccupiedTile_ClearsMode()
    {
        // Rejected tower placement on an in-territory occupied tile
        // flashes feedback but stays in BuildingTower mode (in-range
        // near-miss). Gold unchanged (no build).
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 20);

        g.Hud.ClickBuildTower();
        Assert.Equal(SessionState.ActionMode.BuildingTower, g.Session.Mode);

        // (0,1) is Red's capital — occupied — not a valid tower target.
        g.Map.SimulateClick(g.Tile(0, 1));

        Assert.Equal(SessionState.ActionMode.BuildingTower, g.Session.Mode);
        Assert.Equal(20, g.State.Treasury.GetGold(redCapital));
    }

    [Fact]
    public void BuildTower_OnEnemyTile_ClearsModeAndDeselects()
    {
        // Rejected tower placement on enemy territory flashes feedback
        // then cancels BuildingTower mode (like Escape) and re-processes
        // the tap as a selection click — an enemy tile deselects. Gold
        // unchanged.
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 20);

        g.Hud.ClickBuildTower();

        // (3,0) is Blue. Can't build a tower on enemy territory.
        g.Map.SimulateClick(g.Tile(3, 0));

        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
        Assert.Null(g.Session.SelectedTerritory);
        Assert.Equal(20, g.State.Treasury.GetGold(redCapital));
    }

    [Fact]
    public void Undo_AfterBuildTower_RemovesTowerAndRefundsGold()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 20);
        int before = g.State.Treasury.GetGold(redCapital);

        g.Hud.ClickBuildTower();
        g.Map.SimulateClick(g.Tile(1, 1));
        Assert.IsType<Tower>(g.Tile(1, 1).Occupant);

        g.Hud.ClickUndoLast();

        Assert.Null(g.Tile(1, 1).Occupant);
        Assert.Equal(before, g.State.Treasury.GetGold(redCapital));
    }

    [Fact]
    public void BuildTower_AfterPlacement_RadiatesDefenseToAdjacentOwnTile()
    {
        // Build a tower on (1,1). Its adjacent same-territory tile
        // (0,1) should now have defense 2 — even though (0,1) has the
        // Capital which would otherwise only contribute 1. Verify via
        // DefenseRules directly.
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 20);

        g.Hud.ClickBuildTower();
        g.Map.SimulateClick(g.Tile(1, 1));

        // The selection was preserved (ExecuteBuildTower passes
        // clearSelection: false), so SelectedTerritory is still Red.
        Territory red = g.RedTerritory;
        // (0,1) is Red's capital, adjacent to (1,1)'s tower: defense 2.
        Assert.Equal(2, DefenseRules.Defense(HexCoord.FromOffset(0, 1), g.State.Grid, red));
    }

    [Fact]
    public void Bankruptcy_KillsUnits_ButNotTower()
    {
        // A bankrupt territory's UNITS become graves, but its TOWER
        // survives — towers have no upkeep.
        var g = new TestGame();
        g.Tile(3, 0).Occupant = new Unit(g.Blue.Id, UnitLevel.Captain);
        g.Tile(4, 0).Occupant = new Tower();
        HexCoord blueCapital = g.State.Territories
            .First(t => t.Owner == g.Blue.Id).Capital!.Value;
        g.State.Treasury.SetGold(blueCapital, 0);

        g.Hud.ClickEndTurn(); // advance to Blue: income then upkeep

        // Captain went bankrupt → grave.
        Assert.IsType<Grave>(g.Tile(3, 0).Occupant);
        // Tower untouched.
        Assert.IsType<Tower>(g.Tile(4, 0).Occupant);
    }

    [Fact]
    public void Tower_SurvivesEndTurn_AndTreeConversionPass()
    {
        // Build a tower, end turn twice, tower should still be there.
        // Confirms that the start-of-turn tree-growth phase does not
        // mutate tower tiles.
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 20);

        g.Hud.ClickBuildTower();
        g.Map.SimulateClick(g.Tile(1, 1));

        g.Hud.ClickEndTurn(); // Red → Blue
        g.Hud.ClickEndTurn(); // Blue → Red

        Assert.IsType<Tower>(g.Tile(1, 1).Occupant);
    }
}
