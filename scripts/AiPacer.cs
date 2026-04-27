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
    /// Drop any pending callbacks and ignore future deliveries from
    /// already-scheduled timers. Called when the game is being
    /// abandoned (End Game button) so a stale AI step doesn't fire
    /// after the scene has been torn down.
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
