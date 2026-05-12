using System;

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
/// Default pacer: runs the callback immediately on the current call
/// stack. Used in tests so existing AI-turn assertions can call
/// <c>StartGame</c> / <c>ClickEndTurn</c> and inspect state as soon
/// as the method returns.
/// </summary>
public sealed class SynchronousAiPacer : IAiPacer
{
    public void Schedule(Action callback, int delayMs) => callback();
    public void Cancel() { /* nothing queued; runs are inline */ }
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
