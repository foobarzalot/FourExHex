// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

public partial class GameControllerTests
{
    // --- Startup ----------------------------------------------------------

    [Fact]
    public void StartGame_SeedsFiveTimesGoldEarningCellsPerTerritory()
    {
        var g = new TestGame();

        HexCoord redCapital = g.RedTerritory.Capital!.Value;
        // 2-hex Red territory, no trees: 5 × 2 = 10.
        Assert.Equal(10, g.State.Treasury.GetGold(redCapital));

        // Blue also seeded at 5 × (tree-free cells). Fixture has no trees.
        Territory blue = g.State.Territories.First(t => t.Owner == g.Blue.Id);
        Assert.Equal(5 * blue.Size, g.State.Treasury.GetGold(blue.Capital!.Value));
    }

    [Fact]
    public void StartGame_SeedExcludesTreeTilesFromGoldEarningCount()
    {
        // Plant a tree on one Blue tile BEFORE StartGame runs. That tile
        // stops earning income, so Blue's seed drops by 5 (one tree × 5
        // gold/earning-cell).
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        var players = new List<Player> { red, blue };

        var grid = TestHelpers.BuildRectGrid(5, 2, blue.Id);
        grid.Get(HexCoord.FromOffset(0, 1))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(1, 1))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(3, 0))!.Occupant = new Tree();

        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var map = new MockHexMapView();
        var controller = new GameController(state, new SessionState(), map, new MockHudView());
        controller.StartGame();

        Territory blueT = state.Territories.First(t => t.Owner == blue.Id);
        // Blue has 8 tiles total, 1 tree → 7 earning → 35 gold.
        Assert.Equal(5 * (blueT.Size - 1), state.Treasury.GetGold(blueT.Capital!.Value));
    }

    [Fact]
    public void StartTurn_CreditsIncomeToStartingPlayer_NotEndingPlayer()
    {
        // Income is credited at the START of a player's turn (after
        // tree growth, before upkeep), not at the END of the turn that
        // earned it. Round 1 is the exception — see
        // StartTurn_NoIncomeCreditedDuringFirstRound below — so we
        // need to advance into round 2 to observe the credit.
        var g = new TestGame();
        HexCoord redCapital = g.RedTerritory.Capital!.Value;
        HexCoord blueCapital = g.State.Territories
            .First(t => t.Owner == g.Blue.Id).Capital!.Value;
        int redSeed = g.State.Treasury.GetGold(redCapital);
        int blueSeed = g.State.Treasury.GetGold(blueCapital);

        g.Hud.ClickEndTurn(); // Red T1 → Blue T1 (no income, first round).
        g.Hud.ClickEndTurn(); // Blue T1 → Red T2 (Red's start-of-turn credits income).

        // Red just started turn 2 → income credited.
        int redIncome = g.RedTerritory.Size;
        Assert.Equal(redSeed + redIncome, g.State.Treasury.GetGold(redCapital));
        // Blue has not yet started turn 2 → no income for Blue yet.
        Assert.Equal(blueSeed, g.State.Treasury.GetGold(blueCapital));
    }

    [Fact]
    public void StartTurn_NoIncomeCreditedDuringFirstRound()
    {
        // No money is earned on the first turn for each player. After
        // Red ends T1 and Blue's T1 starts, neither treasury changes
        // from income (Blue has no units, so no upkeep either).
        var g = new TestGame();
        HexCoord redCapital = g.RedTerritory.Capital!.Value;
        HexCoord blueCapital = g.State.Territories
            .First(t => t.Owner == g.Blue.Id).Capital!.Value;
        int redSeed = g.State.Treasury.GetGold(redCapital);
        int blueSeed = g.State.Treasury.GetGold(blueCapital);

        g.Hud.ClickEndTurn(); // Red T1 ends; Blue T1 begins.

        Assert.Equal(redSeed, g.State.Treasury.GetGold(redCapital));
        Assert.Equal(blueSeed, g.State.Treasury.GetGold(blueCapital));
    }

    [Fact]
    public void StartGame_RefreshesBothViews()
    {
        var g = new TestGame();

        Assert.True(g.Hud.RefreshCount >= 1);
        Assert.True(g.Map.RefreshOccupantCount >= 1);
    }

    // --- Click to select --------------------------------------------------

    [Fact]
    public void Click_OwnTerritory_SelectsAndHighlights()
    {
        var g = new TestGame();

        g.Map.SimulateClick(g.Tile(0, 1));

        Assert.NotNull(g.Session.SelectedTerritory);
        Assert.Equal(g.Red.Id, g.Session.SelectedTerritory!.Owner);
        Assert.Same(g.Session.SelectedTerritory, g.Map.LastHighlight);
    }

    [Fact]
    public void Click_EnemyTerritory_ClearsSelection()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        Assert.NotNull(g.Session.SelectedTerritory);

        g.Map.SimulateClick(g.Tile(3, 0));

        Assert.Null(g.Session.SelectedTerritory);
        Assert.True(g.Map.HighlightWasCleared);
    }

    [Fact]
    public void Click_OutsideGrid_ClearsSelection()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        Assert.NotNull(g.Session.SelectedTerritory);

        g.Map.SimulateClick(null);

        Assert.Null(g.Session.SelectedTerritory);
    }

    // --- Pick up units ----------------------------------------------------

    [Fact]
    public void Click_OwnUnit_EntersMovingMode_AndShowsTargets()
    {
        var g = new TestGame();
        // Manually place a Red recruit on (1,1) — the non-capital Red tile.
        g.Tile(1, 1).Occupant = new Unit(g.Red.Id);

        g.Map.SimulateClick(g.Tile(1, 1));

        Assert.Equal(SessionState.ActionMode.MovingUnit, g.Session.Mode);
        Assert.Equal(HexCoord.FromOffset(1, 1), g.Session.MoveSource);
        // Move targets should have been shown (at least one capture target
        // exists — e.g., (2,1) is a non-capital Blue tile adjacent to red).
        Assert.NotEmpty(g.Map.LastMoveTargets);
    }

    [Fact]
    public void Click_OwnUnit_SetsMoveSource_OnMapView()
    {
        var g = new TestGame();
        g.Tile(1, 1).Occupant = new Unit(g.Red.Id);

        g.Map.SimulateClick(g.Tile(1, 1));

        Assert.Equal(HexCoord.FromOffset(1, 1), g.Map.LastMoveSource);
    }

    [Fact]
    public void Click_OwnUnit_PassesUnitLevelToMoveTargetPreview()
    {
        // The destination preview rings need to know the source unit's
        // level so the view can render a Soldier/Captain/Commander preview
        // with the correct number of concentric rings (and Commander dot)
        // instead of always drawing a recruit-sized single ring.
        var g = new TestGame();
        g.Tile(1, 1).Occupant = new Unit(g.Red.Id, UnitLevel.Soldier);

        g.Map.SimulateClick(g.Tile(1, 1));

        Assert.Equal(UnitLevel.Soldier, g.Map.LastMoveTargetsLevel);
    }

    [Fact]
    public void Click_OwnUnit_HighlightsTreeInOwnTerritory_AsTarget()
    {
        // Trees in own territory consume the unit's action when cleared,
        // so they get the same green ring as captures.
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        var players = new List<Player> { red, blue };

        // 5x2 grid, Red owns (0,1)/(1,1)/(2,1) so we have room for both
        // a unit and a tree on non-capital own-territory tiles.
        var grid = TestHelpers.BuildRectGrid(5, 2, blue.Id);
        grid.Get(HexCoord.FromOffset(0, 1))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(1, 1))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(2, 1))!.Owner = red.Id;

        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var session = new SessionState();
        var map = new MockHexMapView();
        var controller = new GameController(state, session, map, new MockHudView());
        controller.StartGame();

        // Capital is on (0,1) (lex-min empty). Drop a tree on (1,1) and
        // a unit on (2,1), then pick up the unit.
        Territory redT = state.Territories.First(t => t.Owner == red.Id);
        HexCoord treeCoord = redT.Coords.First(
            c => c != redT.Capital!.Value && c != HexCoord.FromOffset(2, 1));
        grid.Get(treeCoord)!.Occupant = new Tree();
        grid.Get(HexCoord.FromOffset(2, 1))!.Occupant = new Unit(red.Id);

        map.SimulateClick(grid.Get(HexCoord.FromOffset(2, 1)));

        Assert.Contains(treeCoord, map.LastMoveTargets);
    }

    [Fact]
    public void BuyRecruit_HighlightsTreeInOwnTerritory_AsTarget()
    {
        // The buy-and-place flow uses the same target ring logic — a
        // tree in own territory is a legal placement that consumes the
        // unit's action, so it should ring up alongside captures.
        var g = new TestGame();
        // Drop a tree on (1,1) (Red's empty non-capital tile).
        g.Tile(1, 1).Occupant = new Tree();
        g.Map.SimulateClick(g.Tile(0, 1));

        g.Hud.ClickBuyRecruit();

        Assert.Contains(HexCoord.FromOffset(1, 1), g.Map.LastMoveTargets);
    }

    [Fact]
    public void Click_OwnUnit_HighlightsGraveInOwnTerritory_AsTarget()
    {
        // Same logic as the tree test: burying a grave in own territory
        // consumes the unit's action (see MovementRules.ResolveArrival's
        // clearedObstacle branch), so the grave tile must ring up as a
        // valid move target alongside captures and tree-chops.
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        var players = new List<Player> { red, blue };

        var grid = TestHelpers.BuildRectGrid(5, 2, blue.Id);
        grid.Get(HexCoord.FromOffset(0, 1))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(1, 1))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(2, 1))!.Owner = red.Id;

        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var session = new SessionState();
        var map = new MockHexMapView();
        var controller = new GameController(state, session, map, new MockHudView());
        controller.StartGame();

        // Capital is on (0,1). Drop a grave on the other non-capital tile
        // and a unit on (2,1), then pick up the unit.
        Territory redT = state.Territories.First(t => t.Owner == red.Id);
        HexCoord graveCoord = redT.Coords.First(
            c => c != redT.Capital!.Value && c != HexCoord.FromOffset(2, 1));
        grid.Get(graveCoord)!.Occupant = new Grave();
        grid.Get(HexCoord.FromOffset(2, 1))!.Occupant = new Unit(red.Id);

        map.SimulateClick(grid.Get(HexCoord.FromOffset(2, 1)));

        Assert.Contains(graveCoord, map.LastMoveTargets);
    }

    [Fact]
    public void BuyRecruit_HighlightsGraveInOwnTerritory_AsTarget()
    {
        // Buying a recruit onto a grave is already legal (PurchaseRules
        // accepts grave tiles) and consumes the action — so the grave
        // must show up in the placement preview ring.
        var g = new TestGame();
        g.Tile(1, 1).Occupant = new Grave();
        g.Map.SimulateClick(g.Tile(0, 1));

        g.Hud.ClickBuyRecruit();

        Assert.Contains(HexCoord.FromOffset(1, 1), g.Map.LastMoveTargets);
    }

    [Fact]
    public void Move_AfterCapture_ClearsMoveSource_OnMapView()
    {
        var g = new TestGame();
        g.Tile(1, 1).Occupant = new Unit(g.Red.Id);

        g.Map.SimulateClick(g.Tile(1, 1)); // pick up
        Assert.NotNull(g.Map.LastMoveSource);

        g.Map.SimulateClick(g.Tile(2, 1)); // capture

        Assert.Null(g.Map.LastMoveSource);
    }

    [Fact]
    public void Click_InvalidTargetDuringMovingMode_ClearsMoveSourceOverlay()
    {
        // A rejected move click flashes feedback then drops the unit:
        // mode clears and the move-source pickup overlay is cleared
        // (like pressing Escape).
        var g = new TestGame();
        g.Tile(1, 1).Occupant = new Unit(g.Red.Id);

        g.Map.SimulateClick(g.Tile(1, 1)); // pick up
        Assert.NotNull(g.Map.LastMoveSource);

        g.Map.SimulateClick(g.Tile(4, 0)); // invalid (non-adjacent enemy)

        Assert.Null(g.Map.LastMoveSource);
        Assert.Empty(g.Map.LastMoveTargets);
        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
    }

    [Fact]
    public void BuyRecruit_WhileUnitPickedUp_ClearsMoveSource()
    {
        // If the user picked up a unit and then presses U/click Buy,
        // the pulse should clear — we're no longer in MovingUnit mode.
        var g = new TestGame();
        g.Tile(1, 1).Occupant = new Unit(g.Red.Id);

        g.Map.SimulateClick(g.Tile(1, 1));
        Assert.NotNull(g.Map.LastMoveSource);

        g.Hud.ClickBuyRecruit();

        Assert.Null(g.Map.LastMoveSource);
    }

    [Fact]
    public void Click_OwnAlreadyMovedUnit_DoesNotEnterMoveMode()
    {
        var g = new TestGame();
        var unit = new Unit(g.Red.Id) { HasMovedThisTurn = true };
        g.Tile(1, 1).Occupant = unit;

        g.Map.SimulateClick(g.Tile(1, 1));

        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
    }
}
