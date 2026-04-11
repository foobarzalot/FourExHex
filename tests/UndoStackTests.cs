using System;
using Godot;
using Xunit;

namespace FourExHex.Tests;

public class UndoStackTests
{
    private static GameStateSnapshot MakeSnapshot(int goldValue)
    {
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), new Color(1f, 0f, 0f)));
        grid.Add(new HexTile(new HexCoord(1, 0), new Color(1f, 0f, 0f)));
        var treasury = new Treasury();
        var territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        treasury.SetGold(territories[0].Capital!.Value, goldValue);
        return GameStateSnapshot.Capture(grid, treasury, territories);
    }

    [Fact]
    public void Initially_CannotUndoOrRedo()
    {
        var stack = new UndoStack();

        Assert.False(stack.CanUndo);
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void PushBefore_EnablesUndo_NotRedo()
    {
        var stack = new UndoStack();

        stack.PushBefore(MakeSnapshot(10));

        Assert.True(stack.CanUndo);
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void UndoLast_ReturnsPushedSnapshot_AndPushesCurrentToRedo()
    {
        var stack = new UndoStack();
        GameStateSnapshot pre = MakeSnapshot(10);
        GameStateSnapshot current = MakeSnapshot(20);

        stack.PushBefore(pre);
        GameStateSnapshot restored = stack.UndoLast(current);

        Assert.Same(pre, restored);
        Assert.False(stack.CanUndo);
        Assert.True(stack.CanRedo);
    }

    [Fact]
    public void RedoLast_ReturnsUndoneState_AndPushesBackToUndo()
    {
        var stack = new UndoStack();
        GameStateSnapshot pre = MakeSnapshot(10);
        GameStateSnapshot current = MakeSnapshot(20);

        stack.PushBefore(pre);
        stack.UndoLast(current);

        GameStateSnapshot redone = stack.RedoLast(pre);

        Assert.Same(current, redone);
        Assert.True(stack.CanUndo);
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void MultiUndoThenMultiRedo_FullyWalksHistory()
    {
        // Starting state S0; three actions A1, A2, A3 take us through
        // S1, S2, S3. Undo stack after all actions: [S0, S1, S2], current S3.
        var stack = new UndoStack();
        GameStateSnapshot s0 = MakeSnapshot(0);
        GameStateSnapshot s1 = MakeSnapshot(1);
        GameStateSnapshot s2 = MakeSnapshot(2);
        GameStateSnapshot s3 = MakeSnapshot(3);

        stack.PushBefore(s0); // before action 1
        stack.PushBefore(s1); // before action 2
        stack.PushBefore(s2); // before action 3
        GameStateSnapshot current = s3;

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
    public void UndoTurn_WithMultiplePushes_EndsAtFirstSnapshot_AndRedoAllWalksBack()
    {
        var stack = new UndoStack();
        GameStateSnapshot s0 = MakeSnapshot(0);
        GameStateSnapshot s1 = MakeSnapshot(1);
        GameStateSnapshot s2 = MakeSnapshot(2);

        stack.PushBefore(s0);
        stack.PushBefore(s1);
        stack.PushBefore(s2);

        GameStateSnapshot restored = stack.UndoTurn(MakeSnapshot(3));

        Assert.Same(s0, restored);
        Assert.False(stack.CanUndo);
        Assert.True(stack.CanRedo);
    }

    [Fact]
    public void RedoAll_AfterUndoTurn_ReturnsFinalState()
    {
        var stack = new UndoStack();
        GameStateSnapshot s0 = MakeSnapshot(0);
        GameStateSnapshot s1 = MakeSnapshot(1);
        GameStateSnapshot s2 = MakeSnapshot(2);
        GameStateSnapshot finalState = MakeSnapshot(3);

        stack.PushBefore(s0);
        stack.PushBefore(s1);
        stack.PushBefore(s2);
        stack.UndoTurn(finalState);

        GameStateSnapshot redone = stack.RedoAll(s0);

        Assert.Same(finalState, redone);
        Assert.False(stack.CanRedo);
        Assert.True(stack.CanUndo);
    }

    [Fact]
    public void PushBefore_AfterUndo_ClearsRedoStack()
    {
        // Standard undo/redo invariant: doing a new action after an undo
        // invalidates the forward history.
        var stack = new UndoStack();
        GameStateSnapshot s0 = MakeSnapshot(0);
        GameStateSnapshot s1 = MakeSnapshot(1);

        stack.PushBefore(s0);
        stack.UndoLast(s1);
        Assert.True(stack.CanRedo);

        stack.PushBefore(MakeSnapshot(99));

        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void Clear_EmptiesBothStacks()
    {
        var stack = new UndoStack();
        stack.PushBefore(MakeSnapshot(1));
        stack.UndoLast(MakeSnapshot(2));

        stack.Clear();

        Assert.False(stack.CanUndo);
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void UndoLast_OnEmptyUndoStack_Throws()
    {
        var stack = new UndoStack();

        Assert.Throws<InvalidOperationException>(() => stack.UndoLast(MakeSnapshot(0)));
    }

    [Fact]
    public void UndoTurn_OnEmptyUndoStack_Throws()
    {
        var stack = new UndoStack();

        Assert.Throws<InvalidOperationException>(() => stack.UndoTurn(MakeSnapshot(0)));
    }

    [Fact]
    public void RedoLast_OnEmptyRedoStack_Throws()
    {
        var stack = new UndoStack();

        Assert.Throws<InvalidOperationException>(() => stack.RedoLast(MakeSnapshot(0)));
    }

    [Fact]
    public void RedoAll_OnEmptyRedoStack_Throws()
    {
        var stack = new UndoStack();

        Assert.Throws<InvalidOperationException>(() => stack.RedoAll(MakeSnapshot(0)));
    }
}
