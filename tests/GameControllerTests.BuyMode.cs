using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

public partial class GameControllerTests
{
    // --- Buy button cycle (Recruit → Soldier → Captain → Commander → Recruit) ---

    [Fact]
    public void BuyPressed_FromNoneMode_EntersBuyingRecruit_WhenAllAffordable()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 100);

        g.Hud.ClickBuyRecruit();

        Assert.Equal(SessionState.ActionMode.BuyingRecruit, g.Session.Mode);
    }

    [Fact]
    public void BuyPressed_WhileBuyingRecruit_CyclesToBuyingSoldier()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 100);

        g.Hud.ClickBuyRecruit();
        Assert.Equal(SessionState.ActionMode.BuyingRecruit, g.Session.Mode);

        g.Hud.ClickBuyRecruit();

        Assert.Equal(SessionState.ActionMode.BuyingSoldier, g.Session.Mode);
    }

    [Fact]
    public void BuyPressed_WhileBuyingSoldier_CyclesToBuyingCaptain()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 100);

        g.Hud.ClickBuyRecruit();
        g.Hud.ClickBuyRecruit();
        Assert.Equal(SessionState.ActionMode.BuyingSoldier, g.Session.Mode);

        g.Hud.ClickBuyRecruit();

        Assert.Equal(SessionState.ActionMode.BuyingCaptain, g.Session.Mode);
    }

    [Fact]
    public void BuyPressed_WhileBuyingCaptain_CyclesToBuyingCommander()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 100);

        g.Hud.ClickBuyRecruit();
        g.Hud.ClickBuyRecruit();
        g.Hud.ClickBuyRecruit();
        Assert.Equal(SessionState.ActionMode.BuyingCaptain, g.Session.Mode);

        g.Hud.ClickBuyRecruit();

        Assert.Equal(SessionState.ActionMode.BuyingCommander, g.Session.Mode);
    }

    [Fact]
    public void BuyPressed_SkipsUnaffordableLevels()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        // 25g: Recruit (10) ✓, Soldier (20) ✓, Captain (30) ✗, Commander (40) ✗.
        g.State.Treasury.SetGold(redCapital, 25);

        g.Hud.ClickBuyRecruit();
        Assert.Equal(SessionState.ActionMode.BuyingRecruit, g.Session.Mode);

        // Skips no unaffordable levels here — Soldier is next affordable.
        g.Hud.ClickBuyRecruit();
        Assert.Equal(SessionState.ActionMode.BuyingSoldier, g.Session.Mode);
    }

    [Fact]
    public void BuyPressed_NothingAffordable_IsNoOp()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 5);

        g.Hud.ClickBuyRecruit();

        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
    }

    [Fact]
    public void BuySoldier_OnOwnEmptyTile_DeductsTwentyGoldAndPlacesSoldier()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 30);

        // Cycle to Soldier.
        g.Hud.ClickBuyRecruit();
        g.Hud.ClickBuyRecruit();
        Assert.Equal(SessionState.ActionMode.BuyingSoldier, g.Session.Mode);

        g.Map.SimulateClick(g.Tile(1, 1));

        Assert.NotNull(g.Tile(1, 1).Unit);
        Assert.Equal(UnitLevel.Soldier, g.Tile(1, 1).Unit!.Level);
        Assert.Equal(g.Red.Id, g.Tile(1, 1).Unit!.Owner);
        // 30 - 20 = 10. Cannot afford another Soldier, but CAN afford
        // a Recruit → drop down to BuyingRecruit.
        Assert.Equal(10, g.State.Treasury.GetGold(redCapital));
        Assert.Equal(SessionState.ActionMode.BuyingRecruit, g.Session.Mode);
    }

    [Fact]
    public void BuyCaptain_OntoCapturableEnemySoldierTile_CapturesImmediately()
    {
        var g = new TestGame();
        // Plant an enemy Soldier on (2,1) — Blue, adjacent to Red's (1,1).
        // Defense = 2 (the soldier itself); a Captain (3) > 2 → captures.
        g.Tile(2, 1).Occupant = new Unit(g.Blue.Id, UnitLevel.Soldier);

        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 50);

        // Cycle to Captain.
        g.Hud.ClickBuyRecruit();
        g.Hud.ClickBuyRecruit();
        g.Hud.ClickBuyRecruit();
        Assert.Equal(SessionState.ActionMode.BuyingCaptain, g.Session.Mode);

        g.Map.SimulateClick(g.Tile(2, 1));

        Assert.Equal(g.Red.Id, g.Tile(2, 1).Owner);
        Assert.NotNull(g.Tile(2, 1).Unit);
        Assert.Equal(UnitLevel.Captain, g.Tile(2, 1).Unit!.Level);
        Assert.True(g.Tile(2, 1).Unit!.HasMovedThisTurn);
        // 50 - 30 = 20.
        Assert.Equal(20, g.State.Treasury.GetGold(g.Session.SelectedTerritory!.Capital!.Value));
    }

    [Fact]
    public void BuyCaptain_AfterPurchase_FallsBackToSoldier_IfCaptainUnaffordableButSoldierIs()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 50);

        // Cycle to Captain.
        g.Hud.ClickBuyRecruit();
        g.Hud.ClickBuyRecruit();
        g.Hud.ClickBuyRecruit();
        Assert.Equal(SessionState.ActionMode.BuyingCaptain, g.Session.Mode);

        g.Map.SimulateClick(g.Tile(1, 1));

        // 50 - 30 = 20. Can't afford another Captain (need 30), but CAN
        // afford a Soldier (20) → drop down to BuyingSoldier.
        Assert.Equal(20, g.State.Treasury.GetGold(redCapital));
        Assert.Equal(SessionState.ActionMode.BuyingSoldier, g.Session.Mode);
    }

    [Fact]
    public void BuyCommander_AfterPurchase_FallsBackToRecruit_IfOnlyRecruitStillAffordable()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 55);

        // Cycle to Commander.
        g.Hud.ClickBuyRecruit();
        g.Hud.ClickBuyRecruit();
        g.Hud.ClickBuyRecruit();
        g.Hud.ClickBuyRecruit();
        Assert.Equal(SessionState.ActionMode.BuyingCommander, g.Session.Mode);

        g.Map.SimulateClick(g.Tile(1, 1));

        // 55 - 40 = 15. Captain (30) and Soldier (20) unaffordable; only
        // Recruit (10) affordable → drop to BuyingRecruit.
        Assert.Equal(15, g.State.Treasury.GetGold(redCapital));
        Assert.Equal(SessionState.ActionMode.BuyingRecruit, g.Session.Mode);
    }

    [Fact]
    public void BuyCaptain_AfterPurchase_ExitsMode_IfNothingAffordable()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 35);

        // Cycle to Captain.
        g.Hud.ClickBuyRecruit();
        g.Hud.ClickBuyRecruit();
        g.Hud.ClickBuyRecruit();
        Assert.Equal(SessionState.ActionMode.BuyingCaptain, g.Session.Mode);

        g.Map.SimulateClick(g.Tile(1, 1));

        // 35 - 30 = 5. Nothing affordable → exit to None.
        Assert.Equal(5, g.State.Treasury.GetGold(redCapital));
        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
    }

    [Fact]
    public void BuyCommander_StaysInBuyingCommanderMode_IfStillAffordable()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 80);

        // Cycle to Commander.
        g.Hud.ClickBuyRecruit();
        g.Hud.ClickBuyRecruit();
        g.Hud.ClickBuyRecruit();
        g.Hud.ClickBuyRecruit();
        Assert.Equal(SessionState.ActionMode.BuyingCommander, g.Session.Mode);

        g.Map.SimulateClick(g.Tile(1, 1));

        // 80 - 40 = 40, still ≥ 40 → stay in BuyingCommander (does NOT cycle).
        Assert.Equal(UnitLevel.Commander, g.Tile(1, 1).Unit!.Level);
        Assert.Equal(SessionState.ActionMode.BuyingCommander, g.Session.Mode);
        Assert.Equal(40, g.State.Treasury.GetGold(redCapital));
    }

    // --- Cycle exits at top instead of wrapping ---------------------------

    [Fact]
    public void BuyPressed_WhileBuyingCommander_ExitsToNone()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 100);

        // Cycle to Commander (top of the affordable subset).
        g.Hud.ClickBuyRecruit();
        g.Hud.ClickBuyRecruit();
        g.Hud.ClickBuyRecruit();
        g.Hud.ClickBuyRecruit();
        Assert.Equal(SessionState.ActionMode.BuyingCommander, g.Session.Mode);

        g.Hud.ClickBuyRecruit();

        // From the most-expensive selectable unit, cycle exits instead
        // of wrapping back to Recruit.
        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
    }

    [Fact]
    public void BuyPressed_WhileBuyingHighestAffordable_ExitsToNone_WhenHigherUnaffordable()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        // 25g: Recruit (10) ✓, Soldier (20) ✓, Captain (30) ✗, Commander (40) ✗.
        g.State.Treasury.SetGold(redCapital, 25);

        g.Hud.ClickBuyRecruit();
        g.Hud.ClickBuyRecruit();
        Assert.Equal(SessionState.ActionMode.BuyingSoldier, g.Session.Mode);

        g.Hud.ClickBuyRecruit();

        // Soldier is the most-expensive selectable; cycling past exits.
        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
    }

    [Fact]
    public void BuyPressed_WhileBuyingRecruit_ExitsToNone_WhenOnlyRecruitAffordable()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 15); // only Recruit affordable

        g.Hud.ClickBuyRecruit();
        Assert.Equal(SessionState.ActionMode.BuyingRecruit, g.Session.Mode);

        g.Hud.ClickBuyRecruit();

        // Recruit is both cheapest and most-expensive selectable; cycling
        // past it exits to None.
        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
    }

    [Fact]
    public void BuyPressed_FromNone_AfterExit_EntersCheapestAffordable()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 100);

        // Cycle all the way up and then exit.
        g.Hud.ClickBuyRecruit();
        g.Hud.ClickBuyRecruit();
        g.Hud.ClickBuyRecruit();
        g.Hud.ClickBuyRecruit();
        g.Hud.ClickBuyRecruit();  // exit to None
        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);

        g.Hud.ClickBuyRecruit();

        // Re-entry from None starts at the cheapest affordable level.
        Assert.Equal(SessionState.ActionMode.BuyingRecruit, g.Session.Mode);
    }

    // --- Direct per-level buy clicks --------------------------------------

    [Fact]
    public void BuyUnitClicked_WithCaptain_EntersBuyingCaptain_DirectlyFromNone()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 100);

        g.Hud.ClickBuyUnit(UnitLevel.Captain);

        // No cycling — goes straight to Captain.
        Assert.Equal(SessionState.ActionMode.BuyingCaptain, g.Session.Mode);
    }

    [Fact]
    public void BuyUnitClicked_WithSoldier_SwitchesFromBuyingRecruitToBuyingSoldier()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 100);
        g.Hud.ClickBuyRecruit();
        Assert.Equal(SessionState.ActionMode.BuyingRecruit, g.Session.Mode);

        g.Hud.ClickBuyUnit(UnitLevel.Soldier);

        Assert.Equal(SessionState.ActionMode.BuyingSoldier, g.Session.Mode);
    }

    [Fact]
    public void BuyUnitClicked_WhenAlreadyInThatMode_TogglesModeOff()
    {
        // Radio-button toggle: clicking the already-active buy button a
        // second time cancels the mode (like Escape). A third click
        // re-enters it.
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 100);
        g.Hud.ClickBuyUnit(UnitLevel.Soldier);
        Assert.Equal(SessionState.ActionMode.BuyingSoldier, g.Session.Mode);

        g.Hud.ClickBuyUnit(UnitLevel.Soldier); // toggle off
        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);

        g.Hud.ClickBuyUnit(UnitLevel.Soldier); // re-enter
        Assert.Equal(SessionState.ActionMode.BuyingSoldier, g.Session.Mode);
    }

    [Fact]
    public void BuyUnitClicked_TogglingOff_ClearsMoveTargetsOverlay()
    {
        // Toggling a buy mode off clears the placement-target preview,
        // mirroring Escape/Cancel.
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 100);
        g.Hud.ClickBuyUnit(UnitLevel.Recruit);
        Assert.NotEmpty(g.Map.LastMoveTargets);

        g.Hud.ClickBuyUnit(UnitLevel.Recruit); // toggle off

        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
        Assert.Empty(g.Map.LastMoveTargets);
    }

    [Fact]
    public void BuyUnitClicked_WhenInDifferentBuyMode_SwitchesNotToggles()
    {
        // Clicking a different buy level switches to it (only a click on
        // the *active* level toggles off).
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 100);
        g.Hud.ClickBuyUnit(UnitLevel.Soldier);
        Assert.Equal(SessionState.ActionMode.BuyingSoldier, g.Session.Mode);

        g.Hud.ClickBuyUnit(UnitLevel.Captain);

        Assert.Equal(SessionState.ActionMode.BuyingCaptain, g.Session.Mode);
    }

    [Fact]
    public void BuildTowerClicked_WhenAlreadyBuilding_TogglesModeOff()
    {
        // Clicking Build Tower a second time while already in
        // BuildingTower mode cancels the mode (like Escape), clearing
        // the tower-target preview.
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 20);
        g.Hud.ClickBuildTower();
        Assert.Equal(SessionState.ActionMode.BuildingTower, g.Session.Mode);
        Assert.NotEmpty(g.Map.LastTowerTargets);

        g.Hud.ClickBuildTower(); // toggle off

        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
        Assert.Empty(g.Map.LastTowerTargets);
    }

    [Fact]
    public void BuyUnitClicked_WithUnaffordableLevel_IsNoOp()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 15); // only Recruit affordable

        g.Hud.ClickBuyUnit(UnitLevel.Captain);

        // Can't afford Captain → no mode change, no push.
        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
    }

    [Fact]
    public void BuyUnitClicked_WithoutSelection_IsNoOp()
    {
        var g = new TestGame();
        // No SimulateClick — no selection.

        g.Hud.ClickBuyUnit(UnitLevel.Recruit);

        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
    }
}
