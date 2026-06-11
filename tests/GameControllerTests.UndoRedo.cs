using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

public partial class GameControllerTests
{
    // --- Undo / redo ------------------------------------------------------

    [Fact]
    public void UndoLast_AfterBuy_RemovesTheUnitAndRefundsGold()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        int goldBefore = g.State.Treasury.GetGold(redCapital);

        g.Hud.ClickBuyRecruit();
        g.Map.SimulateClick(g.Tile(1, 1));
        Assert.NotNull(g.Tile(1, 1).Unit);
        Assert.Equal(goldBefore - 10, g.State.Treasury.GetGold(redCapital));

        g.Hud.ClickUndoLast();

        Assert.Null(g.Tile(1, 1).Unit);
        Assert.Equal(goldBefore, g.State.Treasury.GetGold(redCapital));
    }

    [Fact]
    public void RedoLast_AfterUndo_RestoresTheAction()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        g.Hud.ClickBuyRecruit();
        g.Map.SimulateClick(g.Tile(1, 1));
        g.Hud.ClickUndoLast();
        Assert.Null(g.Tile(1, 1).Unit);

        g.Hud.ClickRedoLast();

        Assert.NotNull(g.Tile(1, 1).Unit);
    }

    // --- Per-UI-change undo: restore selection + mode ---------------------

    [Fact]
    public void UndoLast_AfterBuy_RestoresSelectedTerritoryAndBuyMode()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        Territory redBefore = g.Session.SelectedTerritory!;
        g.Hud.ClickBuyRecruit();
        Assert.Equal(SessionState.ActionMode.BuyingRecruit, g.Session.Mode);

        g.Map.SimulateClick(g.Tile(1, 1));  // place recruit

        g.Hud.ClickUndoLast();

        Assert.NotNull(g.Session.SelectedTerritory);
        Assert.Equal(redBefore.Capital, g.Session.SelectedTerritory!.Capital);
        Assert.Equal(SessionState.ActionMode.BuyingRecruit, g.Session.Mode);
    }

    [Fact]
    public void UndoLast_AfterBuildTower_RestoresBuildingTowerMode()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 30);  // afford two towers
        g.Hud.ClickBuildTower();
        Assert.Equal(SessionState.ActionMode.BuildingTower, g.Session.Mode);

        // (0, 1) holds the capital; (1, 1) is empty own territory.
        g.Map.SimulateClick(g.Tile(1, 1));

        g.Hud.ClickUndoLast();

        Assert.NotNull(g.Session.SelectedTerritory);
        Assert.Equal(SessionState.ActionMode.BuildingTower, g.Session.Mode);
    }

    [Fact]
    public void UndoLast_AfterMove_RestoresMovingModeAndMoveSource()
    {
        var g = new TestGame();
        g.Tile(1, 1).Occupant = new Unit(g.Red.Id);
        g.Map.SimulateClick(g.Tile(1, 1));  // selects + enters MovingUnit
        Assert.Equal(SessionState.ActionMode.MovingUnit, g.Session.Mode);
        Assert.Equal(HexCoord.FromOffset(1, 1), g.Session.MoveSource);

        // Move the unit onto an adjacent Blue tile — captures it.
        g.Map.SimulateClick(g.Tile(2, 1));

        g.Hud.ClickUndoLast();

        Assert.Equal(SessionState.ActionMode.MovingUnit, g.Session.Mode);
        Assert.Equal(HexCoord.FromOffset(1, 1), g.Session.MoveSource);
    }

    [Fact]
    public void UndoLast_AfterSelectingTerritory_RestoresPreviousSelection()
    {
        // Select Red, then click an enemy tile (clears selection). Undo
        // should put selection back to Red.
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        Territory red = g.Session.SelectedTerritory!;
        Assert.NotNull(red);

        g.Map.SimulateClick(g.Tile(3, 0));  // Blue tile → clears selection
        Assert.Null(g.Session.SelectedTerritory);

        g.Hud.ClickUndoLast();

        Assert.NotNull(g.Session.SelectedTerritory);
        Assert.Equal(red.Capital, g.Session.SelectedTerritory!.Capital);
    }

    [Fact]
    public void UndoLast_AfterEnteringBuyMode_ClearsMode()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
        g.Hud.ClickBuyRecruit();
        Assert.Equal(SessionState.ActionMode.BuyingRecruit, g.Session.Mode);

        g.Hud.ClickUndoLast();

        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
        // Selection is preserved — we only undid the mode entry.
        Assert.NotNull(g.Session.SelectedTerritory);
    }

    [Fact]
    public void UndoLast_AfterCancelingMode_RestoresMode()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        g.Hud.ClickBuyRecruit();
        Assert.Equal(SessionState.ActionMode.BuyingRecruit, g.Session.Mode);
        g.Hud.PressCancelAction();
        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);

        g.Hud.ClickUndoLast();

        Assert.Equal(SessionState.ActionMode.BuyingRecruit, g.Session.Mode);
    }

    [Fact]
    public void UndoLast_AfterCaptureBuy_RestoresPreCaptureSelection()
    {
        // Buy a recruit onto an enemy tile (capture), then undo.
        // Selection should snap back to the pre-capture Red territory.
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapitalBefore = g.Session.SelectedTerritory!.Capital!.Value;
        g.Hud.ClickBuyRecruit();
        g.Map.SimulateClick(g.Tile(2, 1));  // Blue, adjacent → captures

        g.Hud.ClickUndoLast();

        Assert.NotNull(g.Session.SelectedTerritory);
        Assert.Equal(redCapitalBefore, g.Session.SelectedTerritory!.Capital);
    }

    [Fact]
    public void UndoLast_RestoresMapOverlays_HighlightAndTargets()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        Territory red = g.Session.SelectedTerritory!;
        g.Hud.ClickBuyRecruit();
        // Place to advance state and push undo entry.
        g.Map.SimulateClick(g.Tile(1, 1));

        g.Hud.ClickUndoLast();

        // Highlight and move-target overlays should reflect the restored
        // BuyingRecruit + Red selection.
        Assert.NotNull(g.Map.LastHighlight);
        Assert.Equal(red.Capital, g.Map.LastHighlight!.Capital);
        Assert.NotEmpty(g.Map.LastMoveTargets);
    }

    [Fact]
    public void UndoLast_OnClickThatSelectsAndPicksUnit_RevertsBothInOneStep()
    {
        // Click an own-unit on a previously-unselected territory: a
        // single click both selects and enters MovingUnit. Undo once
        // should revert both.
        var g = new TestGame();
        g.Tile(1, 1).Occupant = new Unit(g.Red.Id);
        Assert.Null(g.Session.SelectedTerritory);
        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);

        g.Map.SimulateClick(g.Tile(1, 1));
        Assert.NotNull(g.Session.SelectedTerritory);
        Assert.Equal(SessionState.ActionMode.MovingUnit, g.Session.Mode);

        g.Hud.ClickUndoLast();

        Assert.Null(g.Session.SelectedTerritory);
        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
    }

    [Fact]
    public void RedoLast_AfterSelectionUndo_RestoresPostUndoState()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        Territory red = g.Session.SelectedTerritory!;
        g.Map.SimulateClick(g.Tile(3, 0));  // clears
        Assert.Null(g.Session.SelectedTerritory);

        g.Hud.ClickUndoLast();
        Assert.NotNull(g.Session.SelectedTerritory);

        g.Hud.ClickRedoLast();
        Assert.Null(g.Session.SelectedTerritory);
    }

    [Fact]
    public void UndoTurn_RestoresStartOfTurnSelectionAndMode()
    {
        var g = new TestGame();
        // Pre-condition: turn just started, nothing selected.
        Assert.Null(g.Session.SelectedTerritory);
        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);

        g.Map.SimulateClick(g.Tile(0, 1));
        g.Hud.ClickBuyRecruit();
        g.Map.SimulateClick(g.Tile(1, 1));

        g.Hud.ClickUndoTurn();

        Assert.Null(g.Session.SelectedTerritory);
        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
    }

    // --- Per-UI-change undo: de-dup no-op handlers ------------------------

    [Fact]
    public void OnBuyPressed_WhenOnlyRecruitAffordable_TogglesBuyingRecruitAndNone()
    {
        // The cycle is "enter cheapest affordable" → "advance" → "exit
        // at top". With only Recruit affordable, Recruit IS the top, so
        // each press toggles between BuyingRecruit and None. Each
        // transition is a real state change and pushes a fresh undo
        // entry (no de-dup applies because state actually changes).
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 10);  // exactly one recruit
        g.Hud.ClickBuyRecruit();  // None → BuyingRecruit
        Assert.Equal(SessionState.ActionMode.BuyingRecruit, g.Session.Mode);
        int countAfterFirst = g.Session.Undo.UndoCount;

        g.Hud.ClickBuyRecruit();  // BuyingRecruit → None (exit at top)
        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
        Assert.Equal(countAfterFirst + 1, g.Session.Undo.UndoCount);

        g.Hud.ClickBuyRecruit();  // None → BuyingRecruit
        Assert.Equal(SessionState.ActionMode.BuyingRecruit, g.Session.Mode);
        Assert.Equal(countAfterFirst + 2, g.Session.Undo.UndoCount);
    }

    [Fact]
    public void OnTileClicked_OnAlreadySelectedTerritory_DoesNotPushUndo()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));  // first click: selects (push)
        int countAfterFirst = g.Session.Undo.UndoCount;

        g.Map.SimulateClick(g.Tile(0, 1));  // re-click same tile: no-op

        Assert.Equal(countAfterFirst, g.Session.Undo.UndoCount);
    }

    [Fact]
    public void OnCancelActionPressed_WhenAlreadyNoPendingAction_DoesNotPushUndo()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        int baseline = g.Session.Undo.UndoCount;
        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);

        g.Hud.PressCancelAction();
        g.Hud.PressCancelAction();

        Assert.Equal(baseline, g.Session.Undo.UndoCount);
    }

    [Fact]
    public void OnNextTerritoryPressed_WithOneOwnedTerritory_DoesNotPushUndoIfSelectionUnchanged()
    {
        // Red owns exactly one territory in the test fixture, so pressing
        // Next Territory while it's selected wraps back to itself —
        // selection unchanged, no push.
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        Territory before = g.Session.SelectedTerritory!;
        int baseline = g.Session.Undo.UndoCount;

        g.Hud.PressNextTerritory();

        Assert.Same(before, g.Session.SelectedTerritory);
        Assert.Equal(baseline, g.Session.Undo.UndoCount);
    }

    // --- Per-UI-change undo: exception propagation ------------------------

    [Fact]
    public void Handler_WhenWorkThrows_DoesNotPushUndo_AndExceptionPropagates()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        int baseline = g.Session.Undo.UndoCount;

        // Configure the map to throw on the next ShowMoveTargets call.
        // OnBuyPressed calls ShowMoveTargets right after setting Mode,
        // so the throw happens after the session mutation but before
        // the push code in TrackHandler can run.
        g.Map.ThrowOnNextShowMoveTargets =
            () => throw new InvalidOperationException("boom");

        Assert.Throws<InvalidOperationException>(() => g.Hud.ClickBuyRecruit());

        Assert.Equal(baseline, g.Session.Undo.UndoCount);
    }
}
