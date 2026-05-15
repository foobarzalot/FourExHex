using System;
using System.Collections.Generic;

namespace FourExHex.Tests;

/// <summary>
/// Test <see cref="IAiBackgroundRunner"/> that stores deferred
/// (work, onMain) pairs instead of firing them. Tests drive delivery
/// manually via <see cref="DrainOne"/> or <see cref="DrainAll"/> so they
/// can poke at the controller between beats (e.g. verify human input
/// is rejected while a worker is in flight). Mirrors the manual-pacer
/// pattern <c>QueuedAiPacer</c> uses for the existing pacer contract.
/// </summary>
public sealed class ManualAiBackgroundRunner : IAiBackgroundRunner
{
    private readonly Queue<(Func<AiAction?> Work, Action<AiAction?> OnMain)> _pending = new();
    public int PendingCount => _pending.Count;
    public bool HasPending => _pending.Count > 0;

    public void Run(Func<AiAction?> work, Action<AiAction?> onMain) =>
        _pending.Enqueue((work, onMain));

    /// <summary>Pop and execute the oldest deferred call. Equivalent
    /// to "the worker thread completed" for one beat.</summary>
    public void DrainOne()
    {
        if (_pending.Count == 0) return;
        var (work, onMain) = _pending.Dequeue();
        AiAction? result = work();
        onMain(result);
    }

    /// <summary>Drain every pending call, including any that get
    /// enqueued by the onMain callbacks themselves (next-beat chooser
    /// dispatch). Snapshot+loop so newly-enqueued items also drain
    /// inside the same call.</summary>
    public void DrainAll()
    {
        while (_pending.Count > 0)
        {
            DrainOne();
        }
    }

    public void Cancel() => _pending.Clear();
}
