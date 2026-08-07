// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using Godot;

/// <summary>
/// Autoload that reads the OS-reported display safe area (notch, Dynamic
/// Island, home indicator) and exposes it as <see cref="LogicalSafeInsets"/> in
/// the same logical-pixel space the HUD lays out against. Pure math lives in
/// <see cref="SafeAreaMath"/> in the FourExHex.ViewMath assembly; this is the
/// thin Godot-side
/// adapter that reads <c>DisplayServer.GetDisplaySafeArea</c> and divides by the
/// running <c>Window.ContentScaleFactor</c>.
///
/// Updated on startup and on viewport resize (rotation / window move). Stays at
/// <see cref="LogicalSafeInsets.Zero"/> on desktop / non-iOS, where the safe rect
/// equals the full window. The HUD subscribes to <see cref="Changed"/> and
/// reflows its bars; map insets fold in <see cref="Current"/> so the rendered
/// map reserves room for the unsafe zones.
///
/// Registered as an autoload in project.godot under the name "SafeArea", after
/// LogBootstrap and DisplayScale so <see cref="Log"/> is wired and ContentScaleFactor
/// is settled before insets are computed.
/// </summary>
public partial class SafeArea : Node
{
    /// <summary>Most recently computed logical insets. Read this any time after
    /// _Ready; defaults to <see cref="LogicalSafeInsets.Zero"/> before the first
    /// recompute.</summary>
    public static LogicalSafeInsets Current { get; private set; } = LogicalSafeInsets.Zero;

    /// <summary>Fires when <see cref="Current"/> changes. Subscribers should
    /// trigger their layout pass — e.g. <see cref="OrientationHud"/> rebuilds
    /// its bars so they sit inside the safe zone.</summary>
    public static event Action<LogicalSafeInsets>? Changed;

    /// <summary>Forced insets, or null for the OS-derived path. Seeded from
    /// <c>FOUREXHEX_FAKE_SAFE_AREA="t,b,l,r"</c> (logical px) and settable at
    /// runtime by the view-matrix harness so one process can sweep several
    /// device shapes — the same "fake a mobile-only input" shape as
    /// <c>FOUREXHEX_FAKE_KB</c> in KeyboardLiftController.</summary>
    private static LogicalSafeInsets? _override;

    /// <summary>Force (or, with null, release) the insets and re-run the
    /// layout. Consulted inside <see cref="Apply"/> rather than assigned into
    /// <see cref="Current"/>: Apply re-runs on every viewport resize, so a
    /// one-shot assignment would be wiped by the first window change.</summary>
    internal static void SetOverrideForHarness(LogicalSafeInsets? insets)
    {
        _override = insets;
        Instance?.Apply();
    }

    private static SafeArea? Instance;

    public override void _Ready()
    {
        Instance = this;
        _override = ParseOverride(OS.GetEnvironment("FOUREXHEX_FAKE_SAFE_AREA"));
        Apply();
        // Rotation / monitor move / OS chrome show-hide all fire SizeChanged.
        GetViewport().SizeChanged += Apply;
    }

    private void Apply()
    {
        Window window = GetWindow();
        Vector2I windowSize = window.Size;
        Rect2I safeRect = DisplayServer.GetDisplaySafeArea();
        float factor = window.ContentScaleFactor;

        // Mobile-only: on desktop Godot's GetDisplaySafeArea returns the screen
        // safe area (e.g. excluding the macOS menu bar), in screen — not window
        // — coordinates. That's not a useful inset for a sub-screen window, and
        // desktops have no notch / home indicator to compensate for. Keep this
        // gated to mobile to mirror the LogBootstrap mobile flag.
        bool isMobile = PlatformFlags.IsMobile;

        LogicalSafeInsets next = _override ?? (isMobile
            ? SafeAreaMath.InsetsFor(
                physicalWindowWidth: windowSize.X, physicalWindowHeight: windowSize.Y,
                physicalSafeX: safeRect.Position.X, physicalSafeY: safeRect.Position.Y,
                physicalSafeWidth: safeRect.Size.X, physicalSafeHeight: safeRect.Size.Y,
                contentScaleFactor: factor)
            : LogicalSafeInsets.Zero);

        bool changed = next != Current;
        Current = next;

        string msg = $"SafeArea: window={windowSize.X}x{windowSize.Y} safe={safeRect} " +
            $"factor={factor} fake={(_override.HasValue ? "yes" : "none")} " +
            $"insets=(t={next.Top:0.##} b={next.Bottom:0.##} " +
            $"l={next.Left:0.##} r={next.Right:0.##}) changed={changed}";
        // An inset change is the noteworthy event (first launch on a notched
        // device, rotation crossing portrait/landscape on the notch axis); the
        // no-op path fires on every resize tick, so keep it at Debug.
        if (changed) Log.Info(Log.LogCategory.Display, msg);
        else Log.Debug(Log.LogCategory.Display, msg);

        if (changed) Changed?.Invoke(next);
    }

    /// <summary>Parse "t,b,l,r" logical px. Anything malformed yields null (the
    /// OS-derived path) rather than throwing — a bad env var should not stop the
    /// game booting.</summary>
    private static LogicalSafeInsets? ParseOverride(string raw)
    {
        if (raw.Length == 0) return null;

        string[] parts = raw.Split(',');
        if (parts.Length != 4) return null;

        var values = new float[4];
        for (int i = 0; i < 4; i++)
        {
            if (!float.TryParse(parts[i].Trim(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out values[i])
                || values[i] < 0f)
            {
                Log.Warn(Log.LogCategory.Display,
                    $"SafeArea: ignoring malformed FOUREXHEX_FAKE_SAFE_AREA='{raw}'");
                return null;
            }
        }
        return new LogicalSafeInsets(values[0], values[1], values[2], values[3]);
    }
}
