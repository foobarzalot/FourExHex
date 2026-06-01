using System;
using Xunit;

namespace FourExHex.Tests;

public class UndoStackTests
{
    private static UndoEntry MakeEntry(int goldValue)
    {
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), PlayerId.FromIndex(0)));
        grid.Add(new HexTile(new HexCoord(1, 0), PlayerId.FromIndex(0)));
        var treasury = new Treasury();
        var territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        treasury.SetGold(territories[0].Capital!.Value, goldValue);
        GameStateSnapshot game = GameStateSnapshot.Capture(grid, treasury, territories);
        var session = new SessionStateSnapshot(null, SessionState.ActionMode.None, null, false);
        return new UndoEntry(game, session);
    }

    [Fact]
    public void Initially_CannotUndoOrRedo()
    {
        var stack = new UndoStack<UndoEntry>();

        Assert.False(stack.CanUndo);
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void PushBefore_EnablesUndo_NotRedo()
    {
        var stack = new UndoStack<UndoEntry>();

        stack.PushBefore(MakeEntry(10));

        Assert.True(stack.CanUndo);
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void UndoLast_ReturnsPushedSnapshot_AndPushesCurrentToRedo()
    {
        var stack = new UndoStack<UndoEntry>();
        UndoEntry pre = MakeEntry(10);
        UndoEntry current = MakeEntry(20);

        stack.PushBefore(pre);
        UndoEntry restored = stack.UndoLast(current);

        Assert.Same(pre, restored);
        Assert.False(stack.CanUndo);
        Assert.True(stack.CanRedo);
    }

    [Fact]
    public void RedoLast_ReturnsUndoneState_AndPushesBackToUndo()
    {
        var stack = new UndoStack<UndoEntry>();
        UndoEntry pre = MakeEntry(10);
        UndoEntry current = MakeEntry(20);

        stack.PushBefore(pre);
        stack.UndoLast(current);

        UndoEntry redone = stack.RedoLast(pre);

        Assert.Same(current, redone);
        Assert.True(stack.CanUndo);
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void MultiUndoThenMultiRedo_FullyWalksHistory()
    {
        // Starting state S0; three actions A1, A2, A3 take us through
        // S1, S2, S3. Undo stack after all actions: [S0, S1, S2], current S3.
        var stack = new UndoStack<UndoEntry>();
        UndoEntry s0 = MakeEntry(0);
        UndoEntry s1 = MakeEntry(1);
        UndoEntry s2 = MakeEntry(2);
        UndoEntry s3 = MakeEntry(3);

        stack.PushBefore(s0); // before action 1
        stack.PushBefore(s1); // before action 2
        stack.PushBefore(s2); // before action 3
        UndoEntry current = s3;

        // Undo three times -> current should end up as s0.
        current = stack.UndoLast(current); Assert.Same(s2, current);
        current = stack.UndoLast(current); Assert.Same(s1, current);
        current = stack.UndoLast(current); Assert.Same(s0, current);
        Assert.False(stack.CanUndo);
        Assert.True(stack.CanRedo);

        // Redo three times -> current should walk back to s3.
        current = stack.RedoLast(current); Assert.Same(s1, current);
        current = stack.RedoLast(current); Assert.Same(s2, current);
        current = stack.RedoLast(current); Assert.Same(s3, current);
        Assert.False(stack.CanRedo);
        Assert.True(stack.CanUndo);
    }

    [Fact]
    public void UndoAll_WithMultiplePushes_EndsAtFirstSnapshot_AndRedoAllWalksBack()
    {
        var stack = new UndoStack<UndoEntry>();
        UndoEntry s0 = MakeEntry(0);
        UndoEntry s1 = MakeEntry(1);
        UndoEntry s2 = MakeEntry(2);

        stack.PushBefore(s0);
        stack.PushBefore(s1);
        stack.PushBefore(s2);

        UndoEntry restored = stack.UndoAll(MakeEntry(3));

        Assert.Same(s0, restored);
        Assert.False(stack.CanUndo);
        Assert.True(stack.CanRedo);
    }

    [Fact]
    public void RedoAll_AfterUndoAll_ReturnsFinalState()
    {
        var stack = new UndoStack<UndoEntry>();
        UndoEntry s0 = MakeEntry(0);
        UndoEntry s1 = MakeEntry(1);
        UndoEntry s2 = MakeEntry(2);
        UndoEntry finalState = MakeEntry(3);

        stack.PushBefore(s0);
        stack.PushBefore(s1);
        stack.PushBefore(s2);
        stack.UndoAll(finalState);

        UndoEntry redone = stack.RedoAll(s0);

        Assert.Same(finalState, redone);
        Assert.False(stack.CanRedo);
        Assert.True(stack.CanUndo);
    }

    [Fact]
    public void PushBefore_AfterUndo_ClearsRedoStack()
    {
        // Standard undo/redo invariant: doing a new action after an undo
        // invalidates the forward history.
        var stack = new UndoStack<UndoEntry>();
        UndoEntry s0 = MakeEntry(0);
        UndoEntry s1 = MakeEntry(1);

        stack.PushBefore(s0);
        stack.UndoLast(s1);
        Assert.True(stack.CanRedo);

        stack.PushBefore(MakeEntry(99));

        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void Clear_EmptiesBothStacks()
    {
        var stack = new UndoStack<UndoEntry>();
        stack.PushBefore(MakeEntry(1));
        stack.UndoLast(MakeEntry(2));

        stack.Clear();

        Assert.False(stack.CanUndo);
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void UndoLast_OnEmptyUndoStack_Throws()
    {
        var stack = new UndoStack<UndoEntry>();

        Assert.Throws<InvalidOperationException>(() => stack.UndoLast(MakeEntry(0)));
    }

    [Fact]
    public void UndoAll_OnEmptyUndoStack_Throws()
    {
        var stack = new UndoStack<UndoEntry>();

        Assert.Throws<InvalidOperationException>(() => stack.UndoAll(MakeEntry(0)));
    }

    [Fact]
    public void RedoLast_OnEmptyRedoStack_Throws()
    {
        var stack = new UndoStack<UndoEntry>();

        Assert.Throws<InvalidOperationException>(() => stack.RedoLast(MakeEntry(0)));
    }

    [Fact]
    public void RedoAll_OnEmptyRedoStack_Throws()
    {
        var stack = new UndoStack<UndoEntry>();

        Assert.Throws<InvalidOperationException>(() => stack.RedoAll(MakeEntry(0)));
    }
}
