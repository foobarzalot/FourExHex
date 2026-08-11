// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;

/// <summary>
/// FIFO policy for the achievement unlock toasts: one toast in flight at a
/// time, the rest queued and delivered in order, so simultaneous unlocks
/// (routine at game end with a 21-row catalog) each get their own banner.
/// Pure policy with no timing — the Godot-side <c>HudView</c> owns the
/// tween and calls <see cref="OnToastFinished"/> from its completion
/// callback. Mirrors the enqueue-then-drain shape of
/// <see cref="SynchronousAiPacer"/>.
/// </summary>
public sealed class AchievementToastQueue
{
    private readonly Queue<string> _pending = new();

    /// <summary>True while a toast is on screen (between the value being
    /// handed out and its <see cref="OnToastFinished"/>).</summary>
    public bool IsShowing { get; private set; }

    /// <summary>Toasts waiting behind the one on screen.</summary>
    public int PendingCount => _pending.Count;

    /// <summary>
    /// Offer a toast. Idle: marks it in flight and returns it — show it
    /// now. Busy: queues it and returns null — it will come back from a
    /// later <see cref="OnToastFinished"/>.
    /// </summary>
    public string? Enqueue(string text)
    {
        if (IsShowing)
        {
            _pending.Enqueue(text);
            return null;
        }
        IsShowing = true;
        return text;
    }

    /// <summary>
    /// The on-screen toast finished. Returns the next toast to show (still
    /// in flight), or null when the queue has drained.
    /// </summary>
    public string? OnToastFinished()
    {
        if (_pending.Count > 0) return _pending.Dequeue();
        IsShowing = false;
        return null;
    }

    /// <summary>Drop everything — pending toasts and the in-flight mark.
    /// Used when the banner is force-hidden (recording chrome).</summary>
    public void Clear()
    {
        _pending.Clear();
        IsShowing = false;
    }
}
