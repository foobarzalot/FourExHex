using System;
using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Test <see cref="ITimerFactory"/> that stores scheduled callbacks
/// instead of firing them on a real clock. Tests drive delivery
/// manually via <see cref="FireAll"/>. Lets <c>GodotAiPacer</c> be
/// unit-tested even though its production timer source is
/// <c>SceneTreeTimer</c> (test-excluded).
/// </summary>
public sealed class ManualTimerFactory : ITimerFactory
{
    private readonly List<Action> _pending = new();
    public int PendingCount => _pending.Count;
    public void After(int delayMs, Action callback) => _pending.Add(callback);
    public void FireAll()
    {
        // Snapshot then clear so callbacks scheduling new callbacks
        // (which they will) don't get fired in the same drain.
        var snap = new List<Action>(_pending);
        _pending.Clear();
        foreach (Action cb in snap) cb();
    }
}

/// <summary>
/// Contract regression tests for <see cref="IAiPacer"/>. Every
/// production impl must support the Cancel-then-Schedule pattern that
/// <c>GameController.BeginReplay</c> relies on: cancelling drops
/// already-pending callbacks, but subsequent Schedule calls must fire
/// normally. <see cref="GodotAiPacer"/> originally had a sticky-cancel
/// bug (a single <c>_cancelled = true</c> flag that never reset) which
/// caused <c>BeginReplay</c> to stall at the initial state — playback
/// "started" but the first scheduled step's closure no-op'd. Fixed by
/// switching to a generation counter; this test documents the contract
/// so the bug pattern doesn't recur in a future pacer.
///
/// GodotAiPacer itself is test-excluded (CLAUDE.md) because it depends
/// on Godot's SceneTree.CreateTimer; the contract is verified here
/// against the testable impls (SynchronousAiPacer, QueuedAiPacer) and
/// the GodotAiPacer fix is identical-shape (generation counter).
/// </summary>
public class AiPacerContractTests
{
    [Fact]
    public void Synchronous_AfterCancel_ScheduleStillFires()
    {
        var pacer = new SynchronousAiPacer();
        pacer.Cancel();
        int fired = 0;
        pacer.Schedule(() => fired++, delayMs: 0);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Queued_AfterCancel_NewScheduleFiresOnDrain()
    {
        var pacer = new QueuedAiPacer();
        // Queue a stale callback, cancel it (drops the queue), then
        // schedule a fresh one. DrainAll must run the fresh one.
        bool staleFired = false;
        pacer.Schedule(() => staleFired = true, 0);
        pacer.Cancel();
        int freshFired = 0;
        pacer.Schedule(() => freshFired++, 0);
        pacer.DrainAll();

        Assert.False(staleFired, "Cancelled stale callback must not fire");
        Assert.Equal(1, freshFired);
    }

    [Fact]
    public void Queued_CancelChain_LeavesNothingInQueue()
    {
        // Defensive: repeated Cancel calls must not throw and must
        // leave the queue empty.
        var pacer = new QueuedAiPacer();
        pacer.Schedule(() => { }, 0);
        pacer.Cancel();
        pacer.Cancel();
        pacer.Cancel();
        Assert.False(pacer.HasPending);
    }

    // --- GodotAiPacer (regression for the sticky-cancel bug) -------------

    [Fact]
    public void GodotAiPacer_AfterCancel_NewScheduleFires()
    {
        // Regression: BeginReplay calls Cancel() to drop any straggling
        // AI step, then Schedule() to kick off replay's first step.
        // The original GodotAiPacer used a sticky `_cancelled = true`
        // flag that gated EVERY future callback, including the freshly
        // scheduled one — so Replay stalled at the initial board state.
        // This test must be red against the sticky-flag impl and green
        // after switching to a generation counter.
        var timers = new ManualTimerFactory();
        var pacer = new GodotAiPacer(timers);

        bool staleFired = false;
        pacer.Schedule(() => staleFired = true, 100);
        pacer.Cancel();
        int freshFired = 0;
        pacer.Schedule(() => freshFired++, 100);
        timers.FireAll();

        Assert.False(staleFired, "Cancelled stale callback must not fire");
        Assert.Equal(1, freshFired);
    }

    [Fact]
    public void GodotAiPacer_NoCancel_ScheduledCallbackFires()
    {
        // Baseline: without Cancel, a scheduled callback fires when
        // the timer factory delivers.
        var timers = new ManualTimerFactory();
        var pacer = new GodotAiPacer(timers);
        int fired = 0;
        pacer.Schedule(() => fired++, 50);
        timers.FireAll();
        Assert.Equal(1, fired);
    }

    [Fact]
    public void GodotAiPacer_CancelledStraggler_DoesNotFire()
    {
        // Verify the cancellation half of the contract: a callback
        // already in flight when Cancel runs must not fire.
        var timers = new ManualTimerFactory();
        var pacer = new GodotAiPacer(timers);
        int fired = 0;
        pacer.Schedule(() => fired++, 50);
        pacer.Cancel();
        timers.FireAll();
        Assert.Equal(0, fired);
    }
}
