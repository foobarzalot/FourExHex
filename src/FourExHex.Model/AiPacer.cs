using System;
using System.Collections.Generic;

/// <summary>
/// Schedules deferred continuations for the AI turn step machine so
/// the game can insert a delay between consecutive AI actions and
/// between consecutive AI player turns. <see cref="GameController"/>
/// never talks to Godot directly; instead it calls into an
/// <see cref="IAiPacer"/>, which the scene root wires up to a
/// Godot-backed implementation and tests wire up to a synchronous
/// implementation that runs the callback immediately.
/// </summary>
public interface IAiPacer
{
    /// <summary>
    /// Invoke <paramref name="callback"/> after approximately
    /// <paramref name="delayMs"/> milliseconds have elapsed. The
    /// synchronous implementation ignores the delay and runs the
    /// callback inline.
    /// </summary>
    void Schedule(Action callback, int delayMs);

    /// <summary>
    /// Like <see cref="Schedule"/> but the delay is NOT scaled by the
    /// speed multiplier. For driver-owned cadences (the instant
    /// fast-forward tick) whose delays are exact by construction (0
    /// between budget-yields, a fixed per-turn delay at boundaries).
    /// Must still frame-yield (never run inline) and must honour the
    /// same <see cref="Cancel"/> generation guard so a queued tick is
    /// dropped on <c>AbandonGame</c> / <c>BeginReplay</c>.
    /// </summary>
    void ScheduleUnscaled(Action callback, int delayMs);

    /// <summary>
    /// Drop any pending callbacks: timers already in flight at the
    /// moment of the call must not invoke their callbacks. Subsequent
    /// <see cref="Schedule"/> calls, however, MUST fire normally —
    /// <c>BeginReplay</c> cancels any stragglers and immediately
    /// schedules its first replay step, so the pacer has to survive a
    /// Cancel-then-reuse cycle. Called in two scenarios:
    ///   • <c>AbandonGame</c>: scene tearing down, no further Schedule.
    ///   • <c>BeginReplay</c>: drop stragglers, then reschedule.
    /// </summary>
    void Cancel();
}

/// <summary>
/// Default pacer: drains all scheduled callbacks before the outermost
/// <see cref="Schedule"/> call returns. Used in tests so existing
/// AI-turn assertions can call <c>StartGame</c> / <c>ClickEndTurn</c>
/// and inspect state as soon as the method returns.
///
/// Internally a trampoline: the first <see cref="Schedule"/> call
/// enqueues its callback and runs a drain loop that pumps every
/// callback the loop enqueues (including from within callbacks).
/// Inner Schedule calls just enqueue and return immediately. This
/// keeps the stack flat across long AI chains — without it, a full
/// six-AI game runs <c>StepAiPreview</c> ↔ <c>StepAiExecute</c> on
/// the call stack and overflows on long runs.
///
/// Equivalent to the prior "<c>Schedule => callback()</c>" version
/// because every <c>_aiPacer.Schedule</c> call site in
/// <see cref="GameController"/> is a tail call; no callback does
/// work after its Schedule call, so FIFO drain order matches
/// recursive order.
/// </summary>
public sealed class SynchronousAiPacer : IAiPacer
{
    private readonly Queue<Action> _queue = new();
    private bool _draining;

    public void Schedule(Action callback, int delayMs) => Drain(callback);

    public void ScheduleUnscaled(Action callback, int delayMs) => Drain(callback);

    private void Drain(Action callback)
    {
        _queue.Enqueue(callback);
        if (_draining) return;
        _draining = true;
        try
        {
            while (_queue.Count > 0) _queue.Dequeue()();
        }
        finally
        {
            _draining = false;
        }
    }

    public void Cancel() => _queue.Clear();
}

/// <summary>
/// Abstraction over "fire <paramref name="callback"/> after
/// <paramref name="delayMs"/>" used by <see cref="GodotAiPacer"/>.
/// Production wires a <c>SceneTreeTimerFactory</c> (lives in a
/// Godot-dependent file, test-excluded); tests wire a manual factory
/// that stores callbacks for the test to fire on demand. Extracting
/// this lets <c>GodotAiPacer</c> be unit-tested even though it ships
/// with Godot's <c>SceneTreeTimer</c> in production.
/// </summary>
public interface ITimerFactory
{
    /// <summary>
    /// Arrange for <paramref name="callback"/> to be invoked after
    /// approximately <paramref name="delayMs"/> milliseconds.
    /// Implementations are not required to deliver in order or at
    /// any particular precision.
    /// </summary>
    void After(int delayMs, Action callback);
}
