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
    /// <summary>Delays passed to <see cref="After"/>, in arrival order.
    /// Lets multiplier tests verify the pacer scaled correctly before
    /// the callback ran.</summary>
    public List<int> ReceivedDelays { get; } = new();
    public void After(int delayMs, Action callback)
    {
        ReceivedDelays.Add(delayMs);
        _pending.Add(callback);
    }
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

    // --- Delay multiplier (AI Speed setting) ----------------------------
    // The UserSettings.AiSpeed setting feeds a Func<float> multiplier
    // into GodotAiPacer. Slow=2x, Normal=1x, Fast=0.5x, Instant=0
    // (run inline). These tests pin that contract independently of
    // the UserSettings layer, which is Godot-test-excluded.

    [Fact]
    public void GodotAiPacer_DefaultMultiplier_PassesDelayUnchanged()
    {
        // Baseline: ctor without an explicit multiplier behaves
        // identically to the old single-arg ctor — the new param
        // must be additive, not breaking.
        var timers = new ManualTimerFactory();
        var pacer = new GodotAiPacer(timers);
        pacer.Schedule(() => { }, 300);
        Assert.Equal(new[] { 300 }, timers.ReceivedDelays);
    }

    [Fact]
    public void GodotAiPacer_MultiplierTwo_DoublesDelay()
    {
        // "Slow" preset: each delay constant is scaled by 2.0 before
        // being handed to the timer factory.
        var timers = new ManualTimerFactory();
        var pacer = new GodotAiPacer(timers, () => 2f);
        pacer.Schedule(() => { }, 300);
        Assert.Equal(new[] { 600 }, timers.ReceivedDelays);
    }

    [Fact]
    public void GodotAiPacer_MultiplierHalf_HalvesDelay()
    {
        // "Fast" preset: each delay scaled by 0.5.
        var timers = new ManualTimerFactory();
        var pacer = new GodotAiPacer(timers, () => 0.5f);
        pacer.Schedule(() => { }, 300);
        Assert.Equal(new[] { 150 }, timers.ReceivedDelays);
    }

    [Fact]
    public void GodotAiPacer_MultiplierReadEverySchedule_NotCached()
    {
        // The user can change AI Speed mid-game, and the next AI
        // beat must reflect the new value — so the pacer reads the
        // multiplier on every Schedule call, not just at construction.
        var timers = new ManualTimerFactory();
        float mult = 1f;
        var pacer = new GodotAiPacer(timers, () => mult);
        pacer.Schedule(() => { }, 300);
        mult = 2f;
        pacer.Schedule(() => { }, 300);
        Assert.Equal(new[] { 300, 600 }, timers.ReceivedDelays);
    }

    [Fact]
    public void GodotAiPacer_MultiplierZero_FiresInlineWithoutTimer()
    {
        // "Instant" preset: callback runs synchronously, never reaches
        // the timer factory. The full AI batch then collapses into one
        // frame (no animations or sounds get a chance to spawn between
        // callbacks).
        var timers = new ManualTimerFactory();
        var pacer = new GodotAiPacer(timers, () => 0f);
        int fired = 0;
        pacer.Schedule(() => fired++, 300);
        Assert.Equal(1, fired);
        Assert.Equal(0, timers.PendingCount);
        Assert.Empty(timers.ReceivedDelays);
    }

    [Fact]
    public void GodotAiPacer_MultiplierZero_TrampolinesNestedSchedules()
    {
        // Mirrors SynchronousAiPacer's trampoline contract: a callback
        // that schedules another callback must not grow the call stack.
        // Without this, a long chain of AI steps in Instant mode could
        // stack-overflow on busy six-AI maps.
        var timers = new ManualTimerFactory();
        var pacer = new GodotAiPacer(timers, () => 0f);
        int depth = 0;
        const int totalSteps = 1000;
        Action? next = null;
        next = () =>
        {
            depth++;
            if (depth < totalSteps) pacer.Schedule(next!, 100);
        };
        pacer.Schedule(next, 100);
        Assert.Equal(totalSteps, depth);
        Assert.Equal(0, timers.PendingCount);
    }

    [Fact]
    public void GodotAiPacer_MultiplierZero_CancelClearsInlineQueue()
    {
        // Cancel called from inside an inline callback must drop
        // anything that callback (or earlier callbacks in the same
        // drain) had already enqueued — matches AbandonGame semantics
        // for the existing async path.
        var timers = new ManualTimerFactory();
        var pacer = new GodotAiPacer(timers, () => 0f);
        int firedAfterCancel = 0;
        bool cancellingCallbackRan = false;
        pacer.Schedule(() =>
        {
            cancellingCallbackRan = true;
            pacer.Schedule(() => firedAfterCancel++, 100);
            pacer.Cancel();
        }, 100);
        Assert.True(cancellingCallbackRan);
        Assert.Equal(0, firedAfterCancel);
    }
}
