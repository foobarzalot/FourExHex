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

    public void ScheduleUnscaled(Action callback, int delayMs) => _queue.Enqueue(callback);

    public void Cancel() => _queue.Clear();

    /// <summary>True iff there's at least one pending callback.</summary>
    public bool HasPending => _queue.Count > 0;

    /// <summary>Number of pending callbacks.</summary>
    public int PendingCount => _queue.Count;

    /// <summary>
    /// Run exactly one queued callback (FIFO), if any. Lets a test
    /// pump the step machine one beat at a time and mutate state (e.g.
    /// flip a speed setting) between beats — the lever for mid-flight
    /// speed-switch tests, which <see cref="DrainAll"/> collapses past.
    /// </summary>
    public void StepOne()
    {
        if (_queue.Count > 0) _queue.Dequeue().Invoke();
    }

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
