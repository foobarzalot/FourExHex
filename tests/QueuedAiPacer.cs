using System;
using System.Collections.Generic;

namespace FourExHex.Tests;

/// <summary>
/// Test pacer that queues scheduled callbacks instead of running
/// them inline. Mirrors the real <c>GodotAiPacer</c>'s deferred
/// semantics for cases where the timing of mid-AI-turn view
/// refreshes matters — the synchronous pacer collapses the whole
/// step machine into one call and Resume's trailing RefreshViews
/// hides bugs where the step machine itself failed to refresh.
/// Tests drive the queue manually via <see cref="DrainAll"/>.
/// </summary>
public sealed class QueuedAiPacer : IAiPacer
{
    private readonly Queue<Action> _queue = new();

    public void Schedule(Action callback, int delayMs) => _queue.Enqueue(callback);

    public void Cancel() => _queue.Clear();

    /// <summary>
    /// Run every queued callback in FIFO order, including any new
    /// ones scheduled by callbacks already running. Returns when
    /// the queue is empty.
    /// </summary>
    public void DrainAll()
    {
        while (_queue.Count > 0)
        {
            _queue.Dequeue().Invoke();
        }
    }
}
