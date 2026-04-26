using System;
using System.Collections.Generic;

/// <summary>
/// Two-sided history for the current turn. Callers push an
/// <see cref="UndoEntry"/> (game + session snapshot) representing the
/// state BEFORE each action. Undo pops the most recent pre-action entry
/// and shoves the current state onto the redo stack. Redo is the
/// symmetric operation. Doing a new action (PushBefore) invalidates the
/// redo history, matching standard undo/redo semantics. Both pop methods
/// throw on an empty stack — the HUD is responsible for gating the
/// buttons.
/// </summary>
public class UndoStack
{
    private readonly List<UndoEntry> _undo = new();
    private readonly List<UndoEntry> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public int UndoCount => _undo.Count;
    public int RedoCount => _redo.Count;

    /// <summary>
    /// Record <paramref name="entry"/> as the state to return to on
    /// <see cref="UndoLast"/>. Call before executing an action. Clears the
    /// redo stack — a new action invalidates forward history.
    /// </summary>
    public void PushBefore(UndoEntry entry)
    {
        _undo.Add(entry);
        _redo.Clear();
    }

    /// <summary>
    /// Pop and return the most recent pre-action entry. The given
    /// <paramref name="currentState"/> is pushed to the redo stack so the
    /// action can be redone later. Throws if there's nothing to undo.
    /// </summary>
    public UndoEntry UndoLast(UndoEntry currentState)
    {
        if (_undo.Count == 0)
        {
            throw new InvalidOperationException("UndoLast called on empty undo stack");
        }
        _redo.Add(currentState);
        UndoEntry top = _undo[_undo.Count - 1];
        _undo.RemoveAt(_undo.Count - 1);
        return top;
    }

    /// <summary>
    /// Repeatedly undo until the undo stack is empty. Returns the oldest
    /// pre-action entry (the state at the start of the turn). Throws if
    /// there's nothing to undo.
    /// </summary>
    public UndoEntry UndoTurn(UndoEntry currentState)
    {
        if (_undo.Count == 0)
        {
            throw new InvalidOperationException("UndoTurn called on empty undo stack");
        }
        UndoEntry restored = UndoLast(currentState);
        while (_undo.Count > 0)
        {
            restored = UndoLast(restored);
        }
        return restored;
    }

    /// <summary>
    /// Pop and return the most recently undone state. The given
    /// <paramref name="currentState"/> is pushed to the undo stack so the
    /// redo can be undone later. Throws if there's nothing to redo.
    /// </summary>
    public UndoEntry RedoLast(UndoEntry currentState)
    {
        if (_redo.Count == 0)
        {
            throw new InvalidOperationException("RedoLast called on empty redo stack");
        }
        _undo.Add(currentState);
        UndoEntry top = _redo[_redo.Count - 1];
        _redo.RemoveAt(_redo.Count - 1);
        return top;
    }

    /// <summary>
    /// Repeatedly redo until the redo stack is empty. Returns the newest
    /// state (the one we were at before the first undo). Throws if there's
    /// nothing to redo.
    /// </summary>
    public UndoEntry RedoAll(UndoEntry currentState)
    {
        if (_redo.Count == 0)
        {
            throw new InvalidOperationException("RedoAll called on empty redo stack");
        }
        UndoEntry restored = RedoLast(currentState);
        while (_redo.Count > 0)
        {
            restored = RedoLast(restored);
        }
        return restored;
    }

    /// <summary>Drop both undo and redo history. Called at <c>EndTurn</c>.</summary>
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
