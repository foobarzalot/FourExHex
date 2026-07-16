// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Behavior tests for <see cref="UndoStack{T}"/> via the editor's
/// <see cref="EditorSnapshot"/> instantiation. Most generic-stack
/// behavior is covered by <see cref="UndoStackTests"/> against
/// <see cref="UndoEntry"/>; the cases here exist mainly as a
/// regression net for the editor's undo wiring.
/// </summary>
public class EditorUndoStackTests
{
    private static readonly PlayerId Red = PlayerId.FromIndex(0);
    private static readonly PlayerId Blue = PlayerId.FromIndex(1);

    /// <summary>
    /// Build a snapshot with a single tile of the given color at (0,0) so
    /// each test snapshot is distinguishable on apply.
    /// </summary>
    private static EditorSnapshot SnapWithSingleTile(PlayerId color)
    {
        var grid = new HexGrid();
        grid.Add(new HexTile(HexCoord.FromOffset(0, 0), color));
        return EditorSnapshot.Capture(grid, new HashSet<HexCoord>(), new List<Territory>());
    }

    [Fact]
    public void NewStack_HasNoUndoOrRedo()
    {
        var stack = new UndoStack<EditorSnapshot>();
        Assert.False(stack.CanUndo);
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void PushBefore_EnablesUndo_AndClearsRedo()
    {
        var stack = new UndoStack<EditorSnapshot>();
        EditorSnapshot a = SnapWithSingleTile(Red);
        EditorSnapshot b = SnapWithSingleTile(Blue);
        stack.PushBefore(a);
        stack.UndoLast(b);  // a is now on top of redo
        Assert.True(stack.CanRedo);

        stack.PushBefore(SnapWithSingleTile(Red));

        Assert.True(stack.CanUndo);
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void UndoLast_ReturnsPreActionEntry_AndPushesCurrentToRedo()
    {
        var stack = new UndoStack<EditorSnapshot>();
        EditorSnapshot pre = SnapWithSingleTile(Red);
        EditorSnapshot current = SnapWithSingleTile(Blue);
        stack.PushBefore(pre);

        EditorSnapshot returned = stack.UndoLast(current);

        Assert.Same(pre, returned);
        Assert.False(stack.CanUndo);
        Assert.True(stack.CanRedo);
    }

    [Fact]
    public void UndoLast_OnEmptyStack_Throws()
    {
        var stack = new UndoStack<EditorSnapshot>();
        Assert.Throws<InvalidOperationException>(
            () => stack.UndoLast(SnapWithSingleTile(Red)));
    }

    [Fact]
    public void RedoLast_OnEmptyStack_Throws()
    {
        var stack = new UndoStack<EditorSnapshot>();
        Assert.Throws<InvalidOperationException>(
            () => stack.RedoLast(SnapWithSingleTile(Red)));
    }

    [Fact]
    public void UndoAll_WalksBackToOldestPreAction()
    {
        var stack = new UndoStack<EditorSnapshot>();
        EditorSnapshot oldest = SnapWithSingleTile(Red);
        EditorSnapshot middle = SnapWithSingleTile(Blue);
        EditorSnapshot current = SnapWithSingleTile(Red);
        stack.PushBefore(oldest);
        stack.PushBefore(middle);

        EditorSnapshot returned = stack.UndoAll(current);

        Assert.Same(oldest, returned);
        Assert.False(stack.CanUndo);
        Assert.Equal(2, stack.RedoCount);
    }

    [Fact]
    public void RedoAll_WalksForwardToNewest()
    {
        var stack = new UndoStack<EditorSnapshot>();
        EditorSnapshot a = SnapWithSingleTile(Red);
        EditorSnapshot b = SnapWithSingleTile(Blue);
        EditorSnapshot c = SnapWithSingleTile(Red);
        stack.PushBefore(a);
        stack.PushBefore(b);
        // Two undos populate the redo stack.
        EditorSnapshot afterUndoB = stack.UndoLast(c);  // returns b's pre = b — wait, b was pushed on top so undo-last returns b
        EditorSnapshot afterUndoA = stack.UndoLast(afterUndoB);

        // Now both undone — redo all should return c (the most-forward state).
        EditorSnapshot returned = stack.RedoAll(afterUndoA);

        Assert.Same(c, returned);
        Assert.False(stack.CanRedo);
        Assert.Equal(2, stack.UndoCount);
    }

    [Fact]
    public void Clear_DropsBothStacks()
    {
        var stack = new UndoStack<EditorSnapshot>();
        stack.PushBefore(SnapWithSingleTile(Red));
        stack.UndoLast(SnapWithSingleTile(Blue));
        Assert.True(stack.CanRedo);

        stack.Clear();

        Assert.False(stack.CanUndo);
        Assert.False(stack.CanRedo);
    }
}
