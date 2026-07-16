// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

public partial class GameControllerTests
{
    // --- HUD refresh reflects state ---------------------------------------

    [Fact]
    public void RefreshViews_ReportsHasActionable_WhenPlayerHasUnmovedUnit()
    {
        var g = new TestGame();
        // Red has an affordable capital (10 gold, exactly recruit cost),
        // so actionable is already true right after StartGame.
        Assert.True(g.Hud.LastHasActionableRemaining);
    }

    [Fact]
    public void Click_InvalidTargetDuringBuyingMode_FlashesThenClearsMode()
    {
        // Rejected buy click flashes feedback then cancels the buy mode
        // (just like pressing Escape), and re-processes the tap as a
        // normal selection click — here a non-adjacent enemy tile, so it
        // deselects.
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        g.Hud.ClickBuyRecruit();
        Assert.Equal(SessionState.ActionMode.BuyingRecruit, g.Session.Mode);

        // (3, 0) is Blue, not adjacent to Red's territory, so not a valid
        // target. The buy mode cancels; rejection feedback still fires.
        g.Map.SimulateClick(g.Tile(3, 0));

        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
        Assert.Null(g.Session.SelectedTerritory);
        Assert.Single(g.Map.Rejections);
    }

    [Fact]
    public void Click_InvalidTargetDuringMovingMode_FlashesThenClearsMode()
    {
        // Rejected move click flashes feedback then drops out of
        // MovingUnit mode (like Escape) and re-processes the tap as a
        // selection click (a non-adjacent enemy tile deselects).
        var g = new TestGame();
        var unit = new Unit(g.Red.Id);
        g.Tile(1, 1).Occupant = unit;
        g.Map.SimulateClick(g.Tile(1, 1));
        Assert.Equal(SessionState.ActionMode.MovingUnit, g.Session.Mode);

        // Click a non-adjacent Blue tile — invalid move target.
        g.Map.SimulateClick(g.Tile(4, 0));

        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
        Assert.Null(g.Session.MoveSource);
        Assert.Single(g.Map.Rejections);
    }

    [Fact]
    public void Click_InvalidInTerritoryTowerSite_FlashesAndStaysInMode()
    {
        // In-range invalid clicks now flash + stay so the user can adjust
        // without losing their mode. The capital tile sits inside the
        // selected territory (the tower's allowed range) but is occupied,
        // so it's an in-range near-miss → keep BuildingTower.
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 25);
        g.Hud.ClickBuildTower();
        Assert.Equal(SessionState.ActionMode.BuildingTower, g.Session.Mode);

        g.Map.SimulateClick(g.Tile(0, 1)); // capital — in-territory but occupied

        Assert.Equal(SessionState.ActionMode.BuildingTower, g.Session.Mode);
        Assert.NotNull(g.Session.SelectedTerritory);
        Assert.Single(g.Map.Rejections);
        Assert.Equal(RejectionShape.Tower, g.Map.LastRejection!.Value.Shape);
    }

    [Fact]
    public void Click_TowerSiteOutsideTerritory_FlashesAndCancelsMode()
    {
        // A tower site outside the selected territory is genuinely out of
        // range (tower can only build on own land), so the mode cancels
        // and the fall-through reselects the clicked territory or
        // deselects.
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.RedTerritory.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 25);
        g.Hud.ClickBuildTower();
        Assert.Equal(SessionState.ActionMode.BuildingTower, g.Session.Mode);

        // (2,0) is adjacent Blue land — out of tower's allowed range.
        g.Map.SimulateClick(g.Tile(2, 0));

        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
        Assert.Single(g.Map.Rejections);
        Assert.Equal(RejectionShape.Tower, g.Map.LastRejection!.Value.Shape);
    }

    [Fact]
    public void Click_InvalidBuyTargetOnOwnUnit_FlashesAndStaysInMode()
    {
        // Stacking on a friendly Commander in own territory is an
        // in-range near-miss (in selected territory, just blocked by a
        // non-combinable occupant). Stay in BuyingRecruit — the user
        // probably mis-clicked one tile.
        var g = new TestGame();
        g.Tile(1, 1).Occupant = new Unit(g.Red.Id, UnitLevel.Commander);
        HexCoord redCapital = g.RedTerritory.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 20);
        g.Map.SimulateClick(g.Tile(0, 1));
        g.Hud.ClickBuyRecruit();
        Assert.Equal(SessionState.ActionMode.BuyingRecruit, g.Session.Mode);

        g.Map.SimulateClick(g.Tile(1, 1)); // own Commander — recruit can't combine

        Assert.Equal(SessionState.ActionMode.BuyingRecruit, g.Session.Mode);
        Assert.NotNull(g.Session.SelectedTerritory);
        Assert.Single(g.Map.Rejections);
    }

    [Fact]
    public void Click_BuyAdjacentDefendedEnemy_FlashesAndStaysInMode()
    {
        // Clicking an adjacent enemy tile that's too well defended is the
        // canonical "in range but blocked" case. Stay in BuyingRecruit.
        var g = new TestGame();
        HexCoord redCapital = g.RedTerritory.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 20);
        g.Map.SimulateClick(g.Tile(0, 1));
        g.Hud.ClickBuyRecruit();

        // Blue capital sits at (0,0) — adjacent to Red's territory but
        // defends itself with strength 1 (Recruit can't capture).
        Territory blueT = g.State.Territories.First(t => t.Owner == g.Blue.Id);
        g.Map.SimulateClick(g.State.Grid.Get(blueT.Capital!.Value));

        Assert.Equal(SessionState.ActionMode.BuyingRecruit, g.Session.Mode);
        Assert.Single(g.Map.Rejections);
    }

    [Fact]
    public void Click_MoveInvalidInRangeTarget_FlashesAndStaysInMode()
    {
        // Move mode + click on the territory's own capital (in-territory
        // but unenterable). Stay in MovingUnit on the same source.
        var g = new TestGame();
        g.Tile(1, 1).Occupant = new Unit(g.Red.Id);
        g.Map.SimulateClick(g.Tile(1, 1)); // pick up the unit
        Assert.Equal(SessionState.ActionMode.MovingUnit, g.Session.Mode);

        g.Map.SimulateClick(g.Tile(0, 1)); // capital — invalid landing but in-territory

        Assert.Equal(SessionState.ActionMode.MovingUnit, g.Session.Mode);
        Assert.Equal(HexCoord.FromOffset(1, 1), g.Session.MoveSource);
        Assert.Single(g.Map.Rejections);
    }

    [Fact]
    public void BuyRecruit_OntoCapturableEnemyTile_CapturesImmediately()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        g.Hud.ClickBuyRecruit();

        // (2, 1) is Blue, not its capital, adjacent to Red's (1, 1).
        // Capturable by a new recruit.
        g.Map.SimulateClick(g.Tile(2, 1));

        Assert.Equal(g.Red.Id, g.Tile(2, 1).Owner);
        Assert.NotNull(g.Tile(2, 1).Unit);
        Assert.True(g.Tile(2, 1).Unit!.HasMovedThisTurn);
        Assert.True(g.Map.RebuildCount >= 1);
    }

    [Fact]
    public void UndoTurn_AfterBuy_RestoresToStartOfTurn()
    {
        var g = new TestGame();
        HexCoord redCapital = g.RedTerritory.Capital!.Value;
        int goldBefore = g.State.Treasury.GetGold(redCapital);

        g.Map.SimulateClick(g.Tile(0, 1));
        g.Hud.ClickBuyRecruit();
        g.Map.SimulateClick(g.Tile(1, 1));
        Assert.NotNull(g.Tile(1, 1).Unit);

        g.Hud.ClickUndoTurn();

        Assert.Null(g.Tile(1, 1).Unit);
        Assert.Equal(goldBefore, g.State.Treasury.GetGold(redCapital));
    }

    [Fact]
    public void RedoAll_AfterUndoTurn_RestoresAllActions()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        g.Hud.ClickBuyRecruit();
        g.Map.SimulateClick(g.Tile(1, 1));
        g.Hud.ClickUndoTurn();
        Assert.Null(g.Tile(1, 1).Unit);

        g.Hud.ClickRedoAll();

        Assert.NotNull(g.Tile(1, 1).Unit);
    }

    [Fact]
    public void RefreshViews_ReportsNoActionable_WhenCapitalCantAffordAndNoUnits()
    {
        var g = new TestGame();
        // Drain Red's treasury so no recruit can be bought.
        HexCoord redCapital = g.RedTerritory.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 0);

        // Trigger a refresh by selecting nothing — SetSelection(null)
        // calls RefreshViews.
        g.Map.SimulateClick(null);

        Assert.False(g.Hud.LastHasActionableRemaining);
    }

    // --- Cancel pending action (Escape) ----------------------------------

    [Fact]
    public void CancelAction_WhileBuyingRecruit_ClearsMode()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 25);
        g.Hud.ClickBuyRecruit();
        Assert.Equal(SessionState.ActionMode.BuyingRecruit, g.Session.Mode);

        g.Hud.PressCancelAction();

        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
    }

    [Fact]
    public void CancelAction_WhileBuildingTower_ClearsMode()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 25);
        g.Hud.ClickBuildTower();
        Assert.Equal(SessionState.ActionMode.BuildingTower, g.Session.Mode);

        g.Hud.PressCancelAction();

        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
    }

    [Fact]
    public void ClickingUnit_RefreshesHudWithMovingUnitMode()
    {
        // Real HudView caches session.Mode at Refresh time to decide
        // whether Escape cancels the pending action or opens the pause
        // menu. If OnTileClickedBody enters MovingUnit mode without a
        // trailing refresh, the cached flag stays None and Escape
        // wrongly opens the pause menu instead of cancelling the move.
        var g = new TestGame();
        g.Tile(1, 1).Occupant = new Unit(g.Red.Id);

        g.Map.SimulateClick(g.Tile(1, 1));

        Assert.Equal(SessionState.ActionMode.MovingUnit, g.Session.Mode);
        Assert.Equal(SessionState.ActionMode.MovingUnit, g.Hud.LastSeenMode);
    }

    [Fact]
    public void CancelAction_WhileMovingUnit_ClearsModeAndMapOverlays()
    {
        var g = new TestGame();
        g.Tile(1, 1).Occupant = new Unit(g.Red.Id);
        g.Map.SimulateClick(g.Tile(1, 1));
        Assert.Equal(SessionState.ActionMode.MovingUnit, g.Session.Mode);
        Assert.NotEmpty(g.Map.LastMoveTargets);
        Assert.NotNull(g.Map.LastMoveSource);

        g.Hud.PressCancelAction();

        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
        Assert.Empty(g.Map.LastMoveTargets);
        Assert.Null(g.Map.LastMoveSource);
    }

    // --- RefreshViews tail: End Turn auto-CTA + onAfterRefresh callback ---

    [Fact]
    public void RefreshViews_SetsEndTurnCtaFalse_WhenPlayerHasActionable()
    {
        var g = new TestGame();
        // Red is starting fresh — unmoved units? No, but they can afford
        // a recruit (10g). HasAnyActionableForCurrentPlayer therefore
        // returns true → End Turn CTA cleared.
        Assert.False(g.Hud.EndTurnCtaActive);
    }

    [Fact]
    public void RefreshViews_SetsEndTurnCtaTrue_WhenPlayerHasNothingActionable()
    {
        var g = new TestGame();
        // Drain Red's treasury so they can't afford a recruit. They
        // also own no unmoved units (none built yet).
        HexCoord redCapital = g.RedTerritory.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 0);
        // Trigger a refresh by clicking the capital tile (selection).
        g.Map.SimulateClick(g.Tile(g.RedTerritory.Capital!.Value.ToOffset().Col,
                                    g.RedTerritory.Capital!.Value.ToOffset().Row));

        Assert.True(g.Hud.EndTurnCtaActive);
        // Game-side auto CTA is steady, not pulsing — only Tutorial
        // Preview's scripted End Turn beat passes pulse: true.
        Assert.False(g.Hud.EndTurnCtaPulse);
    }

    [Fact]
    public void OnAfterRefresh_FiresAtHandlerTail_AfterBodyOverwritesViewSinks()
    {
        // Regression: OnTileClickedBody calls SetSelection (which fires
        // RefreshViews → onAfterRefresh) and THEN paints
        // ShowMoveTargets with all valid targets. A Tutorial Preview
        // cue applied during the mid-body RefreshViews gets clobbered.
        // The handler tail must fire onAfterRefresh again so the cue
        // (or any post-handler observer) sees the final state with the
        // body's overwrites applied.
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        var players = new List<Player> { red, blue };
        var grid = TestHelpers.BuildRectGrid(5, 2, blue.Id);
        grid.Get(HexCoord.FromOffset(0, 0))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(0, 1))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(1, 1))!.Owner = red.Id;
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players,
            new TurnState(players), new Treasury());
        var map = new MockHexMapView();
        var session = new SessionState();
        // Suppress claim-victory prompt.
        session.ClaimVictoryPromptedHighestThreshold[red.Id] = 90;
        session.ClaimVictoryPromptedHighestThreshold[blue.Id] = 90;
        // Place an actionable unit on Red's territory so clicking it
        // puts the controller in MovingUnit mode and paints all valid
        // targets.
        grid.Get(HexCoord.FromOffset(0, 0))!.Occupant = new Unit(red.Id);

        var snapshotCalls = new List<int>(); // record map.LastMoveTargets.Count at each onAfterRefresh
        GameController? controllerRef = null;
        var controller = new GameController(state, session, map, new MockHudView(),
            onAfterRefresh: () => snapshotCalls.Add(map.LastMoveTargets.Count));
        controllerRef = controller;
        controller.StartGame();
        snapshotCalls.Clear();

        // Click the unit → OnTileClickedBody enters MovingUnit mode and
        // paints ShowMoveTargets with multiple valid attack tiles.
        map.SimulateClick(grid.Get(HexCoord.FromOffset(0, 0)));

        // We must observe at least one onAfterRefresh call AFTER the
        // body painted its full-target set (snapshotCalls.Last() should
        // be > 0). Without the tail invocation, the last call records 0
        // (the SetSelection-induced RefreshViews fires before
        // ShowMoveTargets paints).
        Assert.NotEmpty(snapshotCalls);
        Assert.True(snapshotCalls[snapshotCalls.Count - 1] > 0,
            $"Expected last onAfterRefresh to see body's targets, "
            + $"but saw {snapshotCalls[snapshotCalls.Count - 1]} targets.");
    }

    [Fact]
    public void RefreshViews_InvokesOnAfterRefreshCallback()
    {
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        var players = new List<Player> { red, blue };
        var grid = TestHelpers.BuildRectGrid(2, 2, red.Id);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var map = new MockHexMapView();
        int callbackCount = 0;
        var controller = new GameController(
            state, new SessionState(), map, new MockHudView(),
            onAfterRefresh: () => callbackCount++);
        controller.StartGame();
        int afterStart = callbackCount;

        // Click a tile to force another refresh.
        map.SimulateClick(state.Grid.Get(HexCoord.FromOffset(0, 0)));

        Assert.True(afterStart > 0); // StartGame triggered at least one refresh.
        Assert.True(callbackCount > afterStart);
    }

    // --- Rejection feedback (red-pulse + sound) -------------------------

    [Fact]
    public void BuyRecruitRejected_OnNonAdjacentEnemy_FlashesGenericRecruitShape_NoDefenders()
    {
        // Non-adjacent enemy hex: invalid placement (out of placement
        // frontier) but no defending tower. Defender set should be empty
        // → generic sound, recruit-shaped overlay.
        var g = new TestGame();
        HexCoord redCapital = g.RedTerritory.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 20);
        g.Map.SimulateClick(g.Tile(0, 1));   // select Red
        g.Hud.ClickBuyRecruit();
        Assert.Empty(g.Map.Rejections);

        // (4,0) is Blue and non-adjacent to Red — fails the frontier check,
        // not a defense block.
        g.Map.SimulateClick(g.Tile(4, 0));

        Assert.Single(g.Map.Rejections);
        Assert.Equal(HexCoord.FromOffset(4, 0), g.Map.LastRejection!.Value.Target);
        Assert.Equal(RejectionShape.Recruit, g.Map.LastRejection.Value.Shape);
        Assert.Empty(g.Map.LastRejection.Value.Defenders);
    }

    [Fact]
    public void BuySoldierRejected_OnDefendedTile_FlashesWithDefenders_ExcludesWeakerOccupant()
    {
        // The exact user-spec scenario: Soldier buy aimed at an enemy
        // tile that holds a recruit AND is adjacent to a Blue tower.
        // The recruit alone wouldn't block (1 < 2), but the tower (2)
        // does — only the tower coord should appear in Defenders.
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        var players = new List<Player> { red, blue };

        // 5x2 grid; Red owns (0,0),(0,1); Blue owns the rest. Target the
        // (1,0) Blue tile (recruit on it); plant a Blue tower on (2,0) so
        // it radiates into (1,0). Confirm only the tower flashes.
        var grid = TestHelpers.BuildRectGrid(5, 2, blue.Id);
        grid.Get(HexCoord.FromOffset(0, 0))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(0, 1))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = new Unit(blue.Id); // recruit
        grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Tower();          // blue tower

        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var session = new SessionState();
        session.ClaimVictoryPromptedHighestThreshold[red.Id] = 90;
        session.ClaimVictoryPromptedHighestThreshold[blue.Id] = 90;
        var map = new MockHexMapView();
        var controller = new GameController(state, session, map, new MockHudView());
        controller.StartGame();

        Territory redT = state.Territories.First(t => t.Owner == red.Id);
        state.Treasury.SetGold(redT.Capital!.Value, 50); // afford a Soldier (20)
        map.SimulateClick(grid.Get(HexCoord.FromOffset(0, 0)));   // select Red

        // BuyingSoldier: enter the mode via session mutation since there's
        // no dedicated Hud button for soldier (recruits combine up).
        // Click Buy Recruit and verify mode; then forcibly switch into
        // BuyingSoldier via the documented mode enum used in other tests.
        session.Mode = SessionState.ActionMode.BuyingSoldier;

        map.SimulateClick(grid.Get(HexCoord.FromOffset(1, 0))); // click the defended Blue tile

        Assert.Single(map.Rejections);
        var rejection = map.LastRejection!.Value;
        Assert.Equal(HexCoord.FromOffset(1, 0), rejection.Target);
        Assert.Equal(RejectionShape.Soldier, rejection.Shape);
        Assert.Equal(new[] { HexCoord.FromOffset(2, 0) }, rejection.Defenders);
    }

    [Fact]
    public void MoveRejected_OnDefendedTile_FlashesWithDefenders_ShapeMatchesSourceLevel()
    {
        // Pick up a Red Soldier, click an enemy hex defended by an
        // adjacent Blue tower. Rejection shape = Soldier; defenders =
        // [tower coord].
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        var players = new List<Player> { red, blue };

        var grid = TestHelpers.BuildRectGrid(5, 2, blue.Id);
        grid.Get(HexCoord.FromOffset(0, 0))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(0, 1))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(0, 1))!.Occupant = new Unit(red.Id, UnitLevel.Soldier);
        grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Tower(); // Blue tower radiates into (1,0)/(1,1)

        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var session = new SessionState();
        session.ClaimVictoryPromptedHighestThreshold[red.Id] = 90;
        session.ClaimVictoryPromptedHighestThreshold[blue.Id] = 90;
        var map = new MockHexMapView();
        var controller = new GameController(state, session, map, new MockHudView());
        controller.StartGame();

        map.SimulateClick(grid.Get(HexCoord.FromOffset(0, 1))); // pick up soldier
        Assert.Equal(SessionState.ActionMode.MovingUnit, session.Mode);

        map.SimulateClick(grid.Get(HexCoord.FromOffset(1, 1))); // adjacent Blue tile defended by tower

        Assert.Single(map.Rejections);
        var rejection = map.LastRejection!.Value;
        Assert.Equal(HexCoord.FromOffset(1, 1), rejection.Target);
        Assert.Equal(RejectionShape.Soldier, rejection.Shape);
        Assert.Equal(new[] { HexCoord.FromOffset(2, 0) }, rejection.Defenders);
    }

    [Fact]
    public void BuyRejected_OnNonAdjacentDefendedTile_TreatsAsTooFar_NoDefenders()
    {
        // A non-adjacent click — even if that tile happens to have or be
        // adjacent to a strong defender — should be reported as "too far"
        // (empty defenders, generic sound) rather than "blocked by
        // defenders". The defender list shouldn't surface for tiles the
        // player couldn't reach at all.
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        var players = new List<Player> { red, blue };

        // 5x2 grid; Red owns (0,0) + (0,1). A Blue tower sits on (4,0) —
        // far from Red's territory. Clicking (4,0) is invalid because
        // it's non-adjacent to Red, not because of defense.
        var grid = TestHelpers.BuildRectGrid(5, 2, blue.Id);
        grid.Get(HexCoord.FromOffset(0, 0))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(0, 1))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(4, 0))!.Occupant = new Tower();

        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var session = new SessionState();
        session.ClaimVictoryPromptedHighestThreshold[red.Id] = 90;
        session.ClaimVictoryPromptedHighestThreshold[blue.Id] = 90;
        var map = new MockHexMapView();
        var controller = new GameController(state, session, map, new MockHudView());
        controller.StartGame();

        Territory redT = state.Territories.First(t => t.Owner == red.Id);
        state.Treasury.SetGold(redT.Capital!.Value, 50);
        map.SimulateClick(grid.Get(HexCoord.FromOffset(0, 0)));   // select Red
        session.Mode = SessionState.ActionMode.BuyingRecruit;

        map.SimulateClick(grid.Get(HexCoord.FromOffset(4, 0))); // too-far + defended

        Assert.Single(map.Rejections);
        var rejection = map.LastRejection!.Value;
        Assert.Equal(HexCoord.FromOffset(4, 0), rejection.Target);
        Assert.Empty(rejection.Defenders);
    }

    [Fact]
    public void BuyRejected_OnOffGridWaterCoord_FlashesGenericRecruitShape_ThenClearsMode()
    {
        // Off-grid clicks (water, edge of viewport) during placement
        // mode flash a generic-rejection recruit ghost on the off-grid
        // coord, then cancel the buy mode and deselect (off-grid taps
        // re-process as the long-standing click-off-to-deselect UX).
        var g = new TestGame();
        HexCoord redCapital = g.RedTerritory.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 20);
        g.Map.SimulateClick(g.Tile(0, 1));
        g.Hud.ClickBuyRecruit();
        Assert.Equal(SessionState.ActionMode.BuyingRecruit, g.Session.Mode);

        // Pick an off-grid coord (far outside the 5x2 test grid).
        HexCoord offGrid = new HexCoord(20, 20);
        g.Map.SimulateOffGridClick(offGrid);

        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
        Assert.Null(g.Session.SelectedTerritory);
        Assert.Single(g.Map.Rejections);
        var rejection = g.Map.LastRejection!.Value;
        Assert.Equal(offGrid, rejection.Target);
        Assert.Equal(RejectionShape.Recruit, rejection.Shape);
        Assert.Empty(rejection.Defenders);
    }

    [Fact]
    public void OffGridClick_OutsidePlacementMode_DeselectsAsToday()
    {
        // No placement mode → clicking off-grid still clears selection
        // (existing UX: a place to "click to deselect"). No rejection
        // flash since the player wasn't trying to place anything.
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        Assert.NotNull(g.Session.SelectedTerritory);

        g.Map.SimulateOffGridClick(new HexCoord(20, 20));

        Assert.Null(g.Session.SelectedTerritory);
        Assert.Empty(g.Map.Rejections);
    }

    [Fact]
    public void BuildTowerRejected_FlashesGenericTowerShape_NoDefenders()
    {
        // Enter BuildingTower mode, click the capital tile (invalid — capital
        // already occupies it). Should record a Tower-shaped rejection with
        // no defenders.
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 20);
        g.Hud.ClickBuildTower();
        Assert.Empty(g.Map.Rejections);

        g.Map.SimulateClick(g.Tile(0, 1)); // click the capital tile — invalid for tower

        Assert.Single(g.Map.Rejections);
        var rejection = g.Map.LastRejection!.Value;
        Assert.Equal(HexCoord.FromOffset(0, 1), rejection.Target);
        Assert.Equal(RejectionShape.Tower, rejection.Shape);
        Assert.Empty(rejection.Defenders);
    }
}
