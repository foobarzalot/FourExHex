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
/// normally. A generation-counter guard backs this: Cancel drops only
/// already-pending callbacks, never the freshly scheduled ones.
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
        // BeginReplay calls Cancel() to drop any straggling AI step,
        // then Schedule() to kick off replay's first step. Cancel()
        // must drop stragglers but not gate the freshly scheduled step.
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

    // --- Delay multiplier (Slow/Normal/Fast pacing) ---------------------
    // UserSettings feeds a Func<float> multiplier into GodotAiPacer:
    // Slow=2x, Normal=1x, Fast=0.5x. Instant is NOT a multiplier — it
    // routes to the chunked frame-yielded driver via ScheduleUnscaled
    // (see the ScheduleUnscaled tests below). These pin the scaling
    // contract independently of UserSettings (Godot-test-excluded).

    [Fact]
    public void GodotAiPacer_DefaultMultiplier_PassesDelayUnchanged()
    {
        // Baseline: ctor without an explicit multiplier passes delays
        // through unchanged.
        var timers = new ManualTimerFactory();
        var pacer = new GodotAiPacer(timers);
        pacer.Schedule(() => { }, 300);
        Assert.Equal(new[] { 300 }, timers.ReceivedDelays);
    }

    [Fact]
    public void GodotAiPacer_MultiplierTwo_DoublesDelay()
    {
        // "Slow" preset: each delay constant is scaled by 200% before
        // being handed to the timer factory.
        var timers = new ManualTimerFactory();
        var pacer = new GodotAiPacer(timers, () => 200);
        pacer.Schedule(() => { }, 300);
        Assert.Equal(new[] { 600 }, timers.ReceivedDelays);
    }

    [Fact]
    public void GodotAiPacer_MultiplierHalf_HalvesDelay()
    {
        // "Fast" preset: each delay scaled by 50%.
        var timers = new ManualTimerFactory();
        var pacer = new GodotAiPacer(timers, () => 50);
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
        int mult = 100;
        var pacer = new GodotAiPacer(timers, () => mult);
        pacer.Schedule(() => { }, 300);
        mult = 200;
        pacer.Schedule(() => { }, 300);
        Assert.Equal(new[] { 300, 600 }, timers.ReceivedDelays);
    }

    // --- ScheduleUnscaled (instant fast-forward driver path) ------------
    // The chunked instant driver owns its own cadence and passes exact
    // delays. ScheduleUnscaled must (a) frame-yield like Schedule —
    // never run inline, even at the multiplier value that used to
    // trampoline — (b) NOT scale the delay, and (c) honour the same
    // Cancel generation guard.

    [Fact]
    public void GodotAiPacer_ScheduleUnscaled_FrameYields_NotInline()
    {
        // Even at multiplier 0, ScheduleUnscaled must defer to the timer
        // factory, not run inline — that responsiveness is the whole point.
        var timers = new ManualTimerFactory();
        var pacer = new GodotAiPacer(timers, () => 0);
        int fired = 0;
        pacer.ScheduleUnscaled(() => fired++, 200);
        Assert.Equal(0, fired);              // not inline
        Assert.Equal(1, timers.PendingCount);
        timers.FireAll();
        Assert.Equal(1, fired);
    }

    [Fact]
    public void GodotAiPacer_ScheduleUnscaled_DoesNotScaleDelay()
    {
        // The driver's 200ms turn cadence must survive regardless of
        // the speed multiplier — ScheduleUnscaled passes delays through
        // untouched (200, not 400 under a 2x multiplier).
        var timers = new ManualTimerFactory();
        var pacer = new GodotAiPacer(timers, () => 200);
        pacer.ScheduleUnscaled(() => { }, 200);
        pacer.ScheduleUnscaled(() => { }, 0);
        Assert.Equal(new[] { 200, 0 }, timers.ReceivedDelays);
    }

    [Fact]
    public void GodotAiPacer_ScheduleUnscaled_CancelledStraggler_DoesNotFire()
    {
        // Cancellation half of the contract: an unscaled callback in
        // flight when Cancel runs must not fire (AbandonGame/BeginReplay).
        var timers = new ManualTimerFactory();
        var pacer = new GodotAiPacer(timers, () => 100);
        int fired = 0;
        pacer.ScheduleUnscaled(() => fired++, 50);
        pacer.Cancel();
        timers.FireAll();
        Assert.Equal(0, fired);
    }

    [Fact]
    public void GodotAiPacer_ScheduleUnscaled_AfterCancel_NewScheduleFires()
    {
        // Survives Cancel-then-reuse, like Schedule: BeginReplay cancels
        // stragglers then ScheduleUnscaled(InstantReplayTick).
        var timers = new ManualTimerFactory();
        var pacer = new GodotAiPacer(timers, () => 100);
        bool staleFired = false;
        pacer.ScheduleUnscaled(() => staleFired = true, 50);
        pacer.Cancel();
        int freshFired = 0;
        pacer.ScheduleUnscaled(() => freshFired++, 50);
        timers.FireAll();
        Assert.False(staleFired, "Cancelled stale callback must not fire");
        Assert.Equal(1, freshFired);
    }

    [Fact]
    public void Synchronous_ScheduleUnscaled_RunsInline()
    {
        // The synchronous test pacer drains ScheduleUnscaled inline,
        // same as Schedule, so DrainAll-style tests of the instant
        // driver work without a real clock.
        var pacer = new SynchronousAiPacer();
        int fired = 0;
        pacer.ScheduleUnscaled(() => fired++, 200);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Synchronous_ScheduleUnscaled_AfterCancel_StillFires()
    {
        var pacer = new SynchronousAiPacer();
        pacer.Cancel();
        int fired = 0;
        pacer.ScheduleUnscaled(() => fired++, 0);
        Assert.Equal(1, fired);
    }
}
