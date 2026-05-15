using System;
using System.Collections.Generic;

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
/// first replay step. An earlier impl used a sticky
/// <c>_cancelled = true</c> flag that gated every future callback
/// and broke Replay; the generation counter fixes that without losing
/// the AbandonGame use case (the controller is gone after Abandon,
/// so Schedule is never called again anyway).
/// </para>
///
/// <para>
/// Delay scaling: the optional <c>delayMultiplier</c> ctor parameter
/// (consulted on every <see cref="Schedule"/> call so mid-game speed
/// changes take effect immediately) scales the requested delay before
/// it's handed to the timer factory. A multiplier of <c>0</c> switches
/// into a trampoline mode that mirrors <see cref="SynchronousAiPacer"/>:
/// the callback runs inline and any callbacks it itself schedules
/// drain in the same loop instead of recursing. Without the trampoline
/// a busy six-AI Instant run would overflow the stack.
/// </para>
/// </summary>
public sealed class GodotAiPacer : IAiPacer
{
    private readonly ITimerFactory _timers;
    private readonly Func<float> _delayMultiplier;
    private readonly Queue<Action> _inlineQueue = new();
    private bool _draining;
    private int _generation;

    public GodotAiPacer(ITimerFactory timers, Func<float>? delayMultiplier = null)
    {
        _timers = timers;
        _delayMultiplier = delayMultiplier ?? (() => 1f);
    }

    public void Schedule(Action callback, int delayMs)
    {
        float mult = _delayMultiplier();
        if (mult <= 0f)
        {
            // Instant: run inline via the same trampoline shape
            // SynchronousAiPacer uses. Cancel() during draining drops
            // anything still queued.
            _inlineQueue.Enqueue(callback);
            if (_draining) return;
            _draining = true;
            try
            {
                while (_inlineQueue.Count > 0) _inlineQueue.Dequeue()();
            }
            finally
            {
                _draining = false;
            }
            return;
        }

        int scaledDelay = (int)(delayMs * mult);
        int scheduledGen = _generation;
        // A timer in flight when Cancel() runs sees its captured
        // generation no longer matches and no-ops, so a stale callback
        // can't reach the GameController. New Schedule calls after
        // Cancel use the bumped generation and fire normally.
        _timers.After(scaledDelay, () =>
        {
            if (scheduledGen == _generation) callback();
        });
    }

    public void Cancel()
    {
        _generation++;
        _inlineQueue.Clear();
    }
}
