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
/// first replay step. An earlier impl used a sticky
/// <c>_cancelled = true</c> flag that gated every future callback
/// and broke Replay; the generation counter fixes that without losing
/// the AbandonGame use case (the controller is gone after Abandon,
/// so Schedule is never called again anyway).
/// </para>
/// </summary>
public sealed class GodotAiPacer : IAiPacer
{
    private readonly ITimerFactory _timers;
    private int _generation;

    public GodotAiPacer(ITimerFactory timers)
    {
        _timers = timers;
    }

    public void Schedule(Action callback, int delayMs)
    {
        int scheduledGen = _generation;
        // A timer in flight when Cancel() runs sees its captured
        // generation no longer matches and no-ops, so a stale callback
        // can't reach the GameController. New Schedule calls after
        // Cancel use the bumped generation and fire normally.
        _timers.After(delayMs, () =>
        {
            if (scheduledGen == _generation) callback();
        });
    }

    public void Cancel() => _generation++;
}
