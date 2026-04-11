using System;
using System.Collections.Generic;

/// <summary>
/// Two-sided history for the current turn. Callers push a snapshot of the
/// state BEFORE each action. Undo pops the most recent pre-action snapshot
/// and shoves the current state onto the redo stack. Redo is the symmetric
/// operation. Doing a new action (PushBefore) invalidates the redo history,
/// matching standard undo/redo semantics. Both pop methods throw on an
/// empty stack — the HUD is responsible for gating the buttons.
/// </summary>
public class UndoStack
{
    private readonly List<GameStateSnapshot> _undo = new();
    private readonly List<GameStateSnapshot> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>
    /// Record <paramref name="snapshot"/> as the state to return to on
    /// <see cref="UndoLast"/>. Call before executing an action. Clears the
    /// redo stack — a new action invalidates forward history.
    /// </summary>
    public void PushBefore(GameStateSnapshot snapshot)
    {
        _undo.Add(snapshot);
        _redo.Clear();
    }

    /// <summary>
    /// Pop and return the most recent pre-action snapshot. The given
    /// <paramref name="currentState"/> is pushed to the redo stack so the
    /// action can be redone later. Throws if there's nothing to undo.
    /// </summary>
    public GameStateSnapshot UndoLast(GameStateSnapshot currentState)
    {
        if (_undo.Count == 0)
        {
            throw new InvalidOperationException("UndoLast called on empty undo stack");
        }
        _redo.Add(currentState);
        GameStateSnapshot top = _undo[_undo.Count - 1];
        _undo.RemoveAt(_undo.Count - 1);
        return top;
    }

    /// <summary>
    /// Repeatedly undo until the undo stack is empty. Returns the oldest
    /// pre-action snapshot (the state at the start of the turn). Throws if
    /// there's nothing to undo.
    /// </summary>
    public GameStateSnapshot UndoTurn(GameStateSnapshot currentState)
    {
        if (_undo.Count == 0)
        {
            throw new InvalidOperationException("UndoTurn called on empty undo stack");
        }
        GameStateSnapshot restored = UndoLast(currentState);
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
    public GameStateSnapshot RedoLast(GameStateSnapshot currentState)
    {
        if (_redo.Count == 0)
        {
            throw new InvalidOperationException("RedoLast called on empty redo stack");
        }
        _undo.Add(currentState);
        GameStateSnapshot top = _redo[_redo.Count - 1];
        _redo.RemoveAt(_redo.Count - 1);
        return top;
    }

    /// <summary>
    /// Repeatedly redo until the redo stack is empty. Returns the newest
    /// state (the one we were at before the first undo). Throws if there's
    /// nothing to redo.
    /// </summary>
    public GameStateSnapshot RedoAll(GameStateSnapshot currentState)
    {
        if (_redo.Count == 0)
        {
            throw new InvalidOperationException("RedoAll called on empty redo stack");
        }
        GameStateSnapshot restored = RedoLast(currentState);
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
