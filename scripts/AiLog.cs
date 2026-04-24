using Godot;

/// <summary>
/// Process-wide logging toggle for AI + turn diagnostics. Off by
/// default so normal play stays quiet; flip <see cref="Enabled"/>
/// on in <see cref="Main"/> when a diagnostic launch flag is
/// present. Messages go to <see cref="GD.Print"/>, which routes to
/// stdout in headless mode — useful for the 6-AI stasis runs
/// Claude does offline.
/// </summary>
public static class AiLog
{
    public static bool Enabled { get; set; } = false;

    public static void Print(string message)
    {
        if (!Enabled) return;
        GD.Print(message);
    }
}
