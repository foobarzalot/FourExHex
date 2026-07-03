using System;
using System.Diagnostics;

/// <summary>
/// Process-wide structured logging for FourExHex diagnostics.
/// Godot-free: messages route through
/// the injectable <see cref="Sink"/> (wired to <c>GD.Print</c> by
/// <see cref="Main"/>), so this lives in the engine-free model
/// assembly while still reaching stdout in headless 6-AI runs.
///
/// Two independent gates decide whether a message emits:
///   1. Compile-time strip — <see cref="Trace"/>, <see cref="Debug"/>
///      and <see cref="Info"/> are <c>[Conditional("DEBUG")]</c>, so
///      the C# compiler removes the call AND its argument evaluation
///      (interpolated strings included) from Release/exported builds.
///      <see cref="Warn"/> and <see cref="Error"/> always compile and
///      so survive into shipping builds.
///   2. Runtime level gate — each <see cref="LogCategory"/> has an
///      independent minimum <see cref="LogLevel"/>; a message emits
///      only if its level is at least the category's threshold.
///
/// Defaults: every category starts at <see cref="LogLevel.Off"/>, so
/// normal dev play is silent until <see cref="Configure"/> or
/// <see cref="SetLevel"/> raises a category.
/// </summary>
public static class Log
{
    /// <summary>Subsystems that emit diagnostics. Derived strictly
    /// from current call sites — no speculative categories.</summary>
    public enum LogCategory
    {
        Ai = 0,       // ComputerAi candidate diag + GameController AI turn/action logs
        Turn = 1,     // turn begin/end, end-of-turn winner, phantom turn, game-end, stasis
        Capture = 2,  // post-capture domination winner + capture capital/gold diff
        Tutorial = 3, // RecordPane / PreviewPane / TutorialBuilderScene dev traces
        Render = 4,   // HexMapView "rendering N tiles" line
        Input = 5,    // BuildTower click-rejection diagnostic
        Display = 6,  // DisplayScale autoload: DPI → ContentScaleFactor
        Hud = 7,      // HUD CTA / button-state transitions
        Undo = 8,     // undo/redo ↔ replay-beat bookkeeping coordinator
        Cheat = 9,    // debug cheat menu: attach/toggle/button presses
        Campaign = 10, // campaign ladder: store load/save, status marks, level launch, panel
        MapGen = 11,  // procedural map generation: mountain/gold scatter passes
        Replay = 12,  // replay playback: recorded-vs-replayed end-state divergence
        Tide = 13,    // Rising Tides mode: per-turn shore submerge / mountain demote
        Fog = 14,     // Fog Of War mode: per-refresh human visibility recompute
        Tree = 15,    // per-turn whole-map tree/grave incidence census (treepocalypse)
        Automate = 16, // human-turn Automate loop: start / per-move / stop-with-reason
        Viking = 17,  // Viking Raiders mode: wave spawn / disembark / perish / phase census
    }

    /// <summary>Severity, ascending. <see cref="Off"/> disables a
    /// category entirely and is the per-category default.</summary>
    public enum LogLevel
    {
        Trace = 0,
        Debug = 1,
        Info = 2,
        Warn = 3,
        Error = 4,
        Off = 5,
    }

    // Per-category minimum level, indexed by (int)LogCategory. The
    // enum is contiguous 0..N so an array is alloc-free and faster
    // than a dictionary. Every category defaults to Off — nothing
    // prints until configured.
    private static readonly LogLevel[] _minLevel = NewDefaultLevels();

    private static LogLevel[] NewDefaultLevels()
    {
        int n = Enum.GetValues(typeof(LogCategory)).Length;
        var a = new LogLevel[n];
        for (int i = 0; i < n; i++)
        {
            a[i] = LogLevel.Off;
        }
        return a;
    }

    /// <summary>Where surviving messages go. Null = drop. Wired to
    /// <c>GD.Print</c> by <see cref="Main"/>.</summary>
    public static Action<string>? Sink { get; set; }

    /// <summary>Set one category's minimum level.</summary>
    public static void SetLevel(LogCategory category, LogLevel level)
        => _minLevel[(int)category] = level;

    /// <summary>True if a message at <paramref name="level"/> in
    /// <paramref name="category"/> passes the runtime gate. Does NOT
    /// model the compile-time strip — that is a compiler fact.</summary>
    public static bool IsEnabled(LogCategory category, LogLevel level)
        => level >= _minLevel[(int)category];

    /// <summary>Reset every category to <see cref="LogLevel.Off"/>.
    /// Explicit-reset / test support.</summary>
    public static void ResetLevels()
    {
        for (int i = 0; i < _minLevel.Length; i++)
        {
            _minLevel[i] = LogLevel.Off;
        }
    }

    /// <summary>
    /// Parse a spec like <c>"Ai:Debug,Turn:Info,*:Warn"</c> and apply
    /// it via <see cref="SetLevel"/>. Comma-separated
    /// <c>category:level</c> pairs; <c>*</c> sets the level for every
    /// category. Whitespace is trimmed, matching is case-insensitive,
    /// and unknown category/level tokens are silently skipped (best
    /// effort, never throws). Null/empty/whitespace = no-op. Pairs
    /// apply left to right, so a later token overrides an earlier one.
    /// </summary>
    public static void Configure(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
        {
            return;
        }

        foreach (string raw in spec.Split(','))
        {
            string pair = raw.Trim();
            if (pair.Length == 0)
            {
                continue;
            }

            int colon = pair.IndexOf(':');
            if (colon <= 0 || colon == pair.Length - 1)
            {
                continue;
            }

            string cat = pair.Substring(0, colon).Trim();
            string lvl = pair.Substring(colon + 1).Trim();

            if (!Enum.TryParse<LogLevel>(lvl, ignoreCase: true, out LogLevel level))
            {
                continue;
            }

            if (cat == "*")
            {
                for (int i = 0; i < _minLevel.Length; i++)
                {
                    _minLevel[i] = level;
                }
            }
            else if (Enum.TryParse<LogCategory>(cat, ignoreCase: true, out LogCategory c)
                     && Enum.IsDefined(typeof(LogCategory), c))
            {
                _minLevel[(int)c] = level;
            }
            // Unknown category/level token: silently skip.
        }
    }

    [Conditional("DEBUG")]
    public static void Trace(LogCategory category, string message)
        => Emit(category, LogLevel.Trace, message);

    [Conditional("DEBUG")]
    public static void Debug(LogCategory category, string message)
        => Emit(category, LogLevel.Debug, message);

    [Conditional("DEBUG")]
    public static void Info(LogCategory category, string message)
        => Emit(category, LogLevel.Info, message);

    // Warn/Error are NOT [Conditional] — they compile into shipping
    // builds so genuine anomalies and the headless-run terminators
    // still print.
    public static void Warn(LogCategory category, string message)
        => Emit(category, LogLevel.Warn, message);

    public static void Error(LogCategory category, string message)
        => Emit(category, LogLevel.Error, message);

    // Timing helpers. Stamp() always compiles (a cheap timestamp read,
    // portable across the Godot-free libraries that can't use Godot's
    // Time). Since() is [Conditional("DEBUG")] like Debug — the whole
    // call (subtraction, formatting, log) is stripped from Release, so
    // it's free to leave in permanently and live in the debug APK.
    public static long Stamp() => Stopwatch.GetTimestamp();

    [Conditional("DEBUG")]
    public static void Since(LogCategory category, string label, long stamp)
        => Debug(category, $"{label} {(Stamp() - stamp) * 1000.0 / Stopwatch.Frequency:F1}ms");

    private static void Emit(LogCategory category, LogLevel level, string message)
    {
        if (level < _minLevel[(int)category])
        {
            return;
        }
        Sink?.Invoke(message);
    }
}
