using System;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Production <see cref="IAiBackgroundRunner"/>. Runs the chooser on
/// a thread-pool worker via <see cref="Task.Run"/> and marshals the
/// continuation back to the main thread with
/// <see cref="Callable"/><c>.From(...).CallDeferred()</c>, which Godot
/// guarantees is safe to invoke from any thread. The main thread is
/// free between dispatch and continuation, so a Godot frame can paint
/// and the renderer stays responsive during a silent AI batch.
///
/// <para>
/// Cancellation: a generation counter (same pattern as
/// <see cref="GodotAiPacer"/>) lets <c>AbandonGame</c> drop any
/// in-flight continuation. The worker thread still runs to completion
/// — we can't safely interrupt arbitrary code mid-call — but its
/// onMain wrapper checks the generation before invoking, so the user-
/// supplied continuation never reaches a torn-down controller.
/// </para>
///
/// <para>
/// Test-excluded: depends on Godot's <see cref="Callable"/> /
/// <see cref="GodotObject.CallDeferred"/> marshaling, which requires a
/// running scene tree. Tests inject <c>SynchronousAiBackgroundRunner</c>
/// (runs work inline + invokes onMain inline) or
/// <c>ManualAiBackgroundRunner</c> (queues + drains on demand). The
/// thread-safety + cancellation contract is verified against those
/// stand-ins; this class is the production wiring only.
/// </para>
/// </summary>
public sealed class GodotAiBackgroundRunner : IAiBackgroundRunner
{
    private int _generation;

    public void Run(Func<AiAction?> work, Action<AiAction?> onMain)
    {
        int scheduledGen = _generation;
        Task.Run(() =>
        {
            AiAction? result;
            try
            {
                result = work();
            }
            catch (Exception ex)
            {
                // Surface the failure to the main thread so Godot's
                // standard error reporting kicks in; swallowing here
                // would leave the AI step machine stalled forever.
                Callable.From(() =>
                {
                    if (scheduledGen != _generation) return;
                    throw new InvalidOperationException(
                        "AI chooser threw on background worker.", ex);
                }).CallDeferred();
                return;
            }
            Callable.From(() =>
            {
                if (scheduledGen != _generation) return;
                onMain(result);
            }).CallDeferred();
        });
    }

    public void Cancel() => _generation++;
}
