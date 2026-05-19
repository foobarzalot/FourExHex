using System;
using System.Collections.Generic;

/// <summary>
/// Two-sided history of generic snapshot entries. Callers push the state
/// BEFORE each action via <see cref="PushBefore"/>; <see cref="UndoLast"/>
/// pops the most recent pre-action entry and pushes the supplied current
/// state onto the redo stack. <see cref="RedoLast"/> is the symmetric
/// operation. Doing a fresh action (PushBefore) invalidates redo history.
///
/// Both pop methods throw on an empty stack — UI is responsible for
/// gating its buttons. Used by the play scene with <typeparamref name="T"/>
/// = <see cref="UndoEntry"/>, and by the map editor with
/// <typeparamref name="T"/> = <see cref="EditorSnapshot"/>.
/// </summary>
public class UndoStack<T>
{
    private readonly List<T> _undo = new();
    private readonly List<T> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public int UndoCount => _undo.Count;
    public int RedoCount => _redo.Count;

    /// <summary>
    /// Record <paramref name="entry"/> as the state to return to on
    /// <see cref="UndoLast"/>. Clears the redo stack — a new action
    /// invalidates forward history.
    /// </summary>
    public void PushBefore(T entry)
    {
        _undo.Add(entry);
        _redo.Clear();
    }

    /// <summary>
    /// Pop and return the most recent pre-action entry; push
    /// <paramref name="currentState"/> onto the redo stack so the action
    /// can be redone later.
    /// </summary>
    public T UndoLast(T currentState)
    {
        if (_undo.Count == 0)
        {
            throw new InvalidOperationException("UndoLast called on empty undo stack");
        }
        _redo.Add(currentState);
        T top = _undo[_undo.Count - 1];
        _undo.RemoveAt(_undo.Count - 1);
        return top;
    }

    /// <summary>
    /// Walk back to the oldest pre-action entry, pushing every intermediate
    /// state onto the redo stack along the way. The play scene wires its
    /// "Undo Turn" button to this (the stack is cleared at end-of-turn so
    /// the bottom of the stack is the start of the current turn); the
    /// editor wires "Undo All" to it.
    /// </summary>
    public T UndoAll(T currentState)
    {
        if (_undo.Count == 0)
        {
            throw new InvalidOperationException("UndoAll called on empty undo stack");
        }
        T restored = UndoLast(currentState);
        while (_undo.Count > 0)
        {
            restored = UndoLast(restored);
        }
        return restored;
    }

    public T RedoLast(T currentState)
    {
        if (_redo.Count == 0)
        {
            throw new InvalidOperationException("RedoLast called on empty redo stack");
        }
        _undo.Add(currentState);
        T top = _redo[_redo.Count - 1];
        _redo.RemoveAt(_redo.Count - 1);
        return top;
    }

    public T RedoAll(T currentState)
    {
        if (_redo.Count == 0)
        {
            throw new InvalidOperationException("RedoAll called on empty redo stack");
        }
        T restored = RedoLast(currentState);
        while (_redo.Count > 0)
        {
            restored = RedoLast(restored);
        }
        return restored;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
