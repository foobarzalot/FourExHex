using System;

/// <summary>
/// Off-main-thread executor for the AI chooser. <see cref="GameController"/>
/// hands one <c>aiChooser</c> invocation to <see cref="Run"/> per AI beat
/// while a silent batch is in progress; the runner is responsible for
/// running the <c>work</c> delegate somewhere (a worker thread in
/// production, inline in tests) and then invoking <c>onMain</c> on the
/// main thread once the result is ready. Keeping this seam separate
/// from <see cref="IAiPacer"/> means tests with <c>SynchronousAiPacer</c>
/// still drain deterministically — the runner just decides WHERE the
/// chooser executes, not WHEN the next beat fires.
///
/// Cancellation contract mirrors <see cref="IAiPacer.Cancel"/>: dropping
/// a pending continuation is fine; future <c>Run</c> calls on the same
/// runner instance must work. Production wires the production runner
/// once per <see cref="GameController"/> instance, so this matters when
/// <c>BeginReplay</c> swaps the controller's mode mid-game.
/// </summary>
public interface IAiBackgroundRunner
{
    /// <summary>
    /// Invoke <paramref name="work"/> off the main thread (or inline
    /// for the synchronous impl). When the work returns, schedule
    /// <paramref name="onMain"/> to run on the main thread with the
    /// result. Implementations MUST guarantee that <c>onMain</c> never
    /// runs concurrently with itself or with the main game loop —
    /// <c>GameController</c> mutates its own state from inside the
    /// continuation.
    /// </summary>
    void Run(Func<AiAction?> work, Action<AiAction?> onMain);

    /// <summary>
    /// Drop any in-flight continuations: a worker that has already
    /// completed or is mid-flight when <see cref="Cancel"/> runs must
    /// not invoke its <c>onMain</c>. Subsequent <see cref="Run"/> calls
    /// fire normally. Used by <c>AbandonGame</c> so a stale onMain
    /// can't reach a controller whose views have been disposed.
    /// </summary>
    void Cancel();
}

/// <summary>
/// Default runner: invokes <c>work</c> inline and immediately calls
/// <c>onMain</c> with the result. Identical wall behavior to "call the
/// chooser directly" — used for tests and for the
/// <c>SynchronousAiPacer</c>-driven diagnostic mode where the entire
/// AI chain must complete before <c>Schedule</c> returns.
/// </summary>
public sealed class SynchronousAiBackgroundRunner : IAiBackgroundRunner
{
    public void Run(Func<AiAction?> work, Action<AiAction?> onMain)
    {
        AiAction? result = work();
        onMain(result);
    }

    public void Cancel() { }
}
