// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;

/// <summary>
/// Default production <see cref="IAiPacer"/>. Schedules each step
/// through an <see cref="ITimerFactory"/>; the production app wires
/// in <c>SceneTreeTimerFactory</c> (Godot's <c>SceneTreeTimer</c>),
/// tests wire in a manual factory so the cancellation semantics are
/// reachable from xUnit.
///
/// <para>
/// Cancellation semantics: <see cref="Cancel"/> drops pending
/// callbacks (timers already in flight see their captured generation
/// no longer matches and no-op) but does NOT poison future
/// <see cref="Schedule"/> calls — the same pacer instance survives
/// multiple Cancel-then-reuse cycles. <c>BeginReplay</c> depends on
/// this: it cancels any straggling AI step before scheduling its
/// first replay step.
/// </para>
///
/// <para>
/// Delay scaling: the optional <c>delayMultiplierPercent</c> ctor
/// parameter (consulted on every <see cref="Schedule"/> call so
/// mid-game speed changes take effect immediately) scales the
/// requested delay before it's handed to the timer factory —
/// Slow=200, Normal=100, Fast=50 (integer percent so the controller
/// layer stays float-free; see "No floating-point in Model or
/// Controller" in ARCHITECTURE.md). "Instant" is NOT a multiplier:
/// the controller routes it to a chunked frame-yielded driver that
/// schedules via <see cref="ScheduleUnscaled"/>, whose delays bypass
/// the multiplier entirely. Every path here frame-yields through the
/// timer factory; nothing runs inline (the chunked driver owns stack
/// depth by returning between ticks, so no trampoline is needed).
/// </para>
/// </summary>
public sealed class GodotAiPacer : IAiPacer
{
    private readonly ITimerFactory _timers;
    private readonly Func<int> _delayMultiplierPercent;
    private int _generation;

    public GodotAiPacer(ITimerFactory timers, Func<int>? delayMultiplierPercent = null)
    {
        _timers = timers;
        _delayMultiplierPercent = delayMultiplierPercent ?? (() => 100);
    }

    public void Schedule(Action callback, int delayMs) =>
        ScheduleTimer(delayMs * _delayMultiplierPercent() / 100, callback);

    public void ScheduleUnscaled(Action callback, int delayMs) =>
        ScheduleTimer(delayMs, callback);

    private void ScheduleTimer(int delay, Action callback)
    {
        int scheduledGen = _generation;
        // A timer in flight when Cancel() runs sees its captured
        // generation no longer match and no-ops, so a stale callback
        // can't reach the GameController. New schedules after Cancel
        // use the bumped generation and fire normally.
        _timers.After(delay, () =>
        {
            if (scheduledGen == _generation) callback();
        });
    }

    public void Cancel() => _generation++;
}
