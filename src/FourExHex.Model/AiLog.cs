using System;

/// <summary>
/// Process-wide logging toggle for AI + turn diagnostics. Off by
/// default so normal play stays quiet; flip <see cref="Enabled"/>
/// on in <see cref="Main"/> when a diagnostic launch flag is
/// present. Godot-free: messages go to the injectable
/// <see cref="Sink"/> (wired to <c>GD.Print</c> from the Godot
/// layer), so this stays in the engine-free model assembly while
/// still routing to stdout in headless 6-AI stasis runs.
/// </summary>
public static class AiLog
{
    public static bool Enabled { get; set; } = false;

    /// <summary>Where enabled messages go. Null = drop. Wired to
    /// <c>GD.Print</c> by <see cref="Main"/>.</summary>
    public static Action<string>? Sink { get; set; }

    public static void Print(string message)
    {
        if (!Enabled) return;
        Sink?.Invoke(message);
    }
}
