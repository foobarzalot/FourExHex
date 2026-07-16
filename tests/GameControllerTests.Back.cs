// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using Xunit;

namespace FourExHex.Tests;

// System-back ladder (#149): OnBackRequested unwinds ONE controller-side
// layer per call — pending action mode first, then the territory
// selection — and reports false when there is nothing controller-side to
// unwind, so the caller (Main) can escalate to its own ladder (pause
// menu / game-over handling).
public partial class GameControllerTests
{
    [Fact]
    public void Back_PendingAction_CancelsMode_KeepsSelection()
    {
        var g = new TestGame();
        g.Tile(1, 1).Occupant = new Unit(g.Red.Id);
        g.Map.SimulateClick(g.Tile(1, 1));
        Assert.Equal(SessionState.ActionMode.MovingUnit, g.Session.Mode);
        Assert.NotNull(g.Session.SelectedTerritory);

        bool handled = g.Controller.OnBackRequested();

        Assert.True(handled);
        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
        Assert.Null(g.Session.MoveSource);
        // One layer per press: the selection survives the cancel.
        Assert.NotNull(g.Session.SelectedTerritory);
        // Preview overlays are gone (ClearAllOverlays ran).
        Assert.Empty(g.Map.LastMoveTargets);
        Assert.Null(g.Map.LastMoveSource);
    }

    [Fact]
    public void Back_SelectionOnly_Deselects()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        Assert.NotNull(g.Session.SelectedTerritory);
        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);

        bool handled = g.Controller.OnBackRequested();

        Assert.True(handled);
        Assert.Null(g.Session.SelectedTerritory);
        Assert.True(g.Map.HighlightWasCleared);
    }

    [Fact]
    public void Back_TwoPresses_UnwindOneLayerEach()
    {
        var g = new TestGame();
        g.Tile(1, 1).Occupant = new Unit(g.Red.Id);
        g.Map.SimulateClick(g.Tile(1, 1));

        Assert.True(g.Controller.OnBackRequested());
        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
        Assert.NotNull(g.Session.SelectedTerritory);

        Assert.True(g.Controller.OnBackRequested());
        Assert.Null(g.Session.SelectedTerritory);
    }

    [Fact]
    public void Back_NothingToUnwind_ReturnsFalse_PushesNoUndoEntry()
    {
        var g = new TestGame();
        int baseline = g.Session.Undo.UndoCount;

        bool handled = g.Controller.OnBackRequested();

        Assert.False(handled);
        Assert.Equal(baseline, g.Session.Undo.UndoCount);
        Assert.Null(g.Session.SelectedTerritory);
        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
    }

    [Fact]
    public void Back_EachUnwindPushesOneUndoEntry()
    {
        var g = new TestGame();
        g.Tile(1, 1).Occupant = new Unit(g.Red.Id);
        g.Map.SimulateClick(g.Tile(1, 1));
        int baseline = g.Session.Undo.UndoCount;

        g.Controller.OnBackRequested(); // cancel mode
        Assert.Equal(baseline + 1, g.Session.Undo.UndoCount);

        g.Controller.OnBackRequested(); // deselect
        Assert.Equal(baseline + 2, g.Session.Undo.UndoCount);
    }

    [Fact]
    public void Back_GameOver_ReturnsFalse_StateUntouched()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        Assert.NotNull(g.Session.SelectedTerritory);
        g.Session.Winner = g.Blue.Id;

        bool handled = g.Controller.OnBackRequested();

        Assert.False(handled);
        // Selection untouched — Main owns game-over back (→ main menu).
        Assert.NotNull(g.Session.SelectedTerritory);
    }
}
