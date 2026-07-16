// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

public partial class GameControllerTests
{
    // --- Long-click rally -------------------------------------------------

    /// <summary>
    /// Build a fixture with a wider Red strip suitable for rally tests:
    /// Red owns (0,1)..(width-1, 1); Blue owns the rest. Capital is on
    /// the lex-min Red tile (0,1).
    /// </summary>
    private static (GameController Controller, GameState State, MockHexMapView Map,
        MockHudView Hud, SessionState Session, Player Red, Player Blue)
        BuildRallyFixture(int redWidth)
    {
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        var players = new List<Player> { red, blue };

        HexGrid grid = TestHelpers.BuildRectGrid(redWidth + 1, 2, blue.Id);
        for (int c = 0; c < redWidth; c++)
        {
            grid.Get(HexCoord.FromOffset(c, 1))!.Owner = red.Id;
        }
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var session = new SessionState();
        var map = new MockHexMapView();
        var hud = new MockHudView();
        var controller = new GameController(state, session, map, hud);
        controller.StartGame();
        return (controller, state, map, hud, session, red, blue);
    }

    [Fact]
    public void LongClick_OnFriendlyEmptyTile_RalliesUnitToTarget_AndDoesNotConsumeMove()
    {
        var (_, state, map, _, _, red, _) = BuildRallyFixture(redWidth: 5);
        // Capital is at (0,1). Unit at (1,1). Long-click on (4,1) (empty,
        // friendly): unit should move to (4,1) itself (closest empty to
        // target = the target). The reposition is into an own-empty cell,
        // so HasMovedThisTurn must remain false.
        state.Grid.Get(HexCoord.FromOffset(1, 1))!.Occupant = new Unit(red.Id);

        map.SimulateLongClick(state.Grid.Get(HexCoord.FromOffset(4, 1)));

        Assert.Null(state.Grid.Get(HexCoord.FromOffset(1, 1))!.Occupant);
        Unit? moved = state.Grid.Get(HexCoord.FromOffset(4, 1))!.Unit;
        Assert.NotNull(moved);
        Assert.Equal(red.Id, moved!.Owner);
        Assert.False(moved.HasMovedThisTurn);
    }

    [Fact]
    public void LongClick_OnFriendlyTowerTile_RalliesUnitToClosestEmptyAdjacentToTower()
    {
        var (_, state, map, _, _, red, _) = BuildRallyFixture(redWidth: 4);
        // Capital (0,1). Tower at (3,1). Unit at (1,1). Long-click on
        // (3,1): tower-occupied, so the closest legal empty cell to the
        // target is (2,1).
        state.Grid.Get(HexCoord.FromOffset(3, 1))!.Occupant = new Tower();
        state.Grid.Get(HexCoord.FromOffset(1, 1))!.Occupant = new Unit(red.Id);

        map.SimulateLongClick(state.Grid.Get(HexCoord.FromOffset(3, 1)));

        Assert.Null(state.Grid.Get(HexCoord.FromOffset(1, 1))!.Occupant);
        Unit? moved = state.Grid.Get(HexCoord.FromOffset(2, 1))!.Unit;
        Assert.NotNull(moved);
        Assert.False(moved!.HasMovedThisTurn);
        // Tower untouched.
        Assert.IsType<Tower>(state.Grid.Get(HexCoord.FromOffset(3, 1))!.Occupant);
    }

    [Fact]
    public void LongClick_OnEnemyTile_NoOp()
    {
        var (_, state, map, _, _, red, _) = BuildRallyFixture(redWidth: 4);
        state.Grid.Get(HexCoord.FromOffset(1, 1))!.Occupant = new Unit(red.Id);

        // Long-click on a Blue tile.
        map.SimulateLongClick(state.Grid.Get(HexCoord.FromOffset(0, 0)));

        // Unit didn't move.
        Assert.NotNull(state.Grid.Get(HexCoord.FromOffset(1, 1))!.Unit);
    }

    [Fact]
    public void LongClick_OnNullTile_NoOp()
    {
        var (_, state, map, _, _, red, _) = BuildRallyFixture(redWidth: 4);
        state.Grid.Get(HexCoord.FromOffset(1, 1))!.Occupant = new Unit(red.Id);

        map.SimulateLongClick(null);

        Assert.NotNull(state.Grid.Get(HexCoord.FromOffset(1, 1))!.Unit);
    }

    [Fact]
    public void LongClick_DuringPendingBuyMode_NoOp()
    {
        // The user wants the long-click rally to be ignored entirely
        // when a purchase / build / move action is pending. Otherwise
        // the player's mid-action context would silently disappear.
        var (_, state, map, hud, session, red, _) = BuildRallyFixture(redWidth: 4);
        state.Grid.Get(HexCoord.FromOffset(1, 1))!.Occupant = new Unit(red.Id);
        // Select Red's territory and enter BuyingRecruit mode.
        map.SimulateClick(state.Grid.Get(HexCoord.FromOffset(0, 1)));
        hud.ClickBuyRecruit();
        Assert.Equal(SessionState.ActionMode.BuyingRecruit, session.Mode);

        map.SimulateLongClick(state.Grid.Get(HexCoord.FromOffset(3, 1)));

        // Unit didn't move; mode preserved.
        Assert.NotNull(state.Grid.Get(HexCoord.FromOffset(1, 1))!.Unit);
        Assert.Equal(SessionState.ActionMode.BuyingRecruit, session.Mode);
    }

    [Fact]
    public void LongClick_RalliesMultipleUnits_GreedilyClosestFirst()
    {
        var (_, state, map, _, _, red, _) = BuildRallyFixture(redWidth: 5);
        // Capital (0,1). Units at (1,1) and (2,1). Long-click on (4,1):
        // (2,1) is closer, processed first → moves to (4,1) (target,
        // empty). Then (1,1) processed → empties to (3,1) (now closest
        // empty to target).
        state.Grid.Get(HexCoord.FromOffset(1, 1))!.Occupant = new Unit(red.Id);
        state.Grid.Get(HexCoord.FromOffset(2, 1))!.Occupant = new Unit(red.Id);

        map.SimulateLongClick(state.Grid.Get(HexCoord.FromOffset(4, 1)));

        Assert.Null(state.Grid.Get(HexCoord.FromOffset(1, 1))!.Occupant);
        Assert.Null(state.Grid.Get(HexCoord.FromOffset(2, 1))!.Occupant);
        Assert.NotNull(state.Grid.Get(HexCoord.FromOffset(3, 1))!.Unit);
        Assert.NotNull(state.Grid.Get(HexCoord.FromOffset(4, 1))!.Unit);
        Assert.False(state.Grid.Get(HexCoord.FromOffset(3, 1))!.Unit!.HasMovedThisTurn);
        Assert.False(state.Grid.Get(HexCoord.FromOffset(4, 1))!.Unit!.HasMovedThisTurn);
    }

    [Fact]
    public void LongClick_DoesNotMoveAlreadyMovedUnits()
    {
        var (_, state, map, _, _, red, _) = BuildRallyFixture(redWidth: 5);
        // Already-moved unit at (1,1) (the closer one); fresh unit at (2,1).
        var spent = new Unit(red.Id) { HasMovedThisTurn = true };
        state.Grid.Get(HexCoord.FromOffset(1, 1))!.Occupant = spent;
        state.Grid.Get(HexCoord.FromOffset(2, 1))!.Occupant = new Unit(red.Id);

        map.SimulateLongClick(state.Grid.Get(HexCoord.FromOffset(4, 1)));

        // Spent unit unchanged at (1,1); fresh unit rallies to (4,1).
        Assert.Same(spent, state.Grid.Get(HexCoord.FromOffset(1, 1))!.Occupant);
        Assert.Null(state.Grid.Get(HexCoord.FromOffset(2, 1))!.Occupant);
        Assert.NotNull(state.Grid.Get(HexCoord.FromOffset(4, 1))!.Unit);
    }

    [Fact]
    public void LongClick_UnitAtTarget_DoesNotMove()
    {
        var (_, state, map, _, _, red, _) = BuildRallyFixture(redWidth: 5);
        // Unit already on the target tile — no closer cell exists.
        var unit = new Unit(red.Id);
        state.Grid.Get(HexCoord.FromOffset(4, 1))!.Occupant = unit;

        map.SimulateLongClick(state.Grid.Get(HexCoord.FromOffset(4, 1)));

        Assert.Same(unit, state.Grid.Get(HexCoord.FromOffset(4, 1))!.Occupant);
        Assert.False(unit.HasMovedThisTurn);
    }

    [Fact]
    public void LongClick_RallyIsSingleUndoStep()
    {
        var (_, state, map, hud, _, red, _) = BuildRallyFixture(redWidth: 5);
        state.Grid.Get(HexCoord.FromOffset(1, 1))!.Occupant = new Unit(red.Id);
        state.Grid.Get(HexCoord.FromOffset(2, 1))!.Occupant = new Unit(red.Id);

        map.SimulateLongClick(state.Grid.Get(HexCoord.FromOffset(4, 1)));

        // Sanity: rally happened.
        Assert.NotNull(state.Grid.Get(HexCoord.FromOffset(3, 1))!.Unit);
        Assert.NotNull(state.Grid.Get(HexCoord.FromOffset(4, 1))!.Unit);

        hud.ClickUndoLast();

        // One undo restores BOTH units to their original positions.
        Assert.NotNull(state.Grid.Get(HexCoord.FromOffset(1, 1))!.Unit);
        Assert.NotNull(state.Grid.Get(HexCoord.FromOffset(2, 1))!.Unit);
        Assert.Null(state.Grid.Get(HexCoord.FromOffset(3, 1))!.Occupant);
        Assert.Null(state.Grid.Get(HexCoord.FromOffset(4, 1))!.Occupant);
    }

    [Fact]
    public void LongClick_PlaysRallySound_WhenAtLeastOneUnitMoves()
    {
        var (_, state, map, _, _, red, _) = BuildRallyFixture(redWidth: 5);
        state.Grid.Get(HexCoord.FromOffset(1, 1))!.Occupant = new Unit(red.Id);
        state.Grid.Get(HexCoord.FromOffset(2, 1))!.Occupant = new Unit(red.Id);

        map.SimulateLongClick(state.Grid.Get(HexCoord.FromOffset(4, 1)));

        // One whoosh per rally, regardless of how many units moved.
        Assert.Equal(1, map.RallySoundCount);
    }

    [Fact]
    public void LongClick_DoesNotPlayRallySound_WhenNoUnitsMove()
    {
        // Long-click on own territory with a unit already at the target —
        // nothing moves, so nothing should sound.
        var (_, state, map, _, _, red, _) = BuildRallyFixture(redWidth: 5);
        state.Grid.Get(HexCoord.FromOffset(4, 1))!.Occupant = new Unit(red.Id);

        map.SimulateLongClick(state.Grid.Get(HexCoord.FromOffset(4, 1)));

        Assert.Equal(0, map.RallySoundCount);
    }

    [Fact]
    public void LongClick_NoOpRally_DoesNotPushUndoEntry()
    {
        // No unmoved units → nothing to rally → no undo entry pushed.
        var (_, state, map, _, session, _, _) = BuildRallyFixture(redWidth: 4);
        int baseline = session.Undo.UndoCount;

        map.SimulateLongClick(state.Grid.Get(HexCoord.FromOffset(3, 1)));

        Assert.Equal(baseline, session.Undo.UndoCount);
    }
}
