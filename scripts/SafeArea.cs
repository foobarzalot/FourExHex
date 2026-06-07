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

    public override void _Ready()
    {
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
        bool isMobile = OS.HasFeature("mobile");

        LogicalSafeInsets next = isMobile
            ? SafeAreaMath.InsetsFor(
                physicalWindowWidth: windowSize.X, physicalWindowHeight: windowSize.Y,
                physicalSafeX: safeRect.Position.X, physicalSafeY: safeRect.Position.Y,
                physicalSafeWidth: safeRect.Size.X, physicalSafeHeight: safeRect.Size.Y,
                contentScaleFactor: factor)
            : LogicalSafeInsets.Zero;

        bool changed = next != Current;
        Current = next;

        string msg = $"SafeArea: window={windowSize.X}x{windowSize.Y} safe={safeRect} " +
            $"factor={factor} insets=(t={next.Top:0.##} b={next.Bottom:0.##} " +
            $"l={next.Left:0.##} r={next.Right:0.##}) changed={changed}";
        // An inset change is the noteworthy event (first launch on a notched
        // device, rotation crossing portrait/landscape on the notch axis); the
        // no-op path fires on every resize tick, so keep it at Debug.
        if (changed) Log.Info(Log.LogCategory.Display, msg);
        else Log.Debug(Log.LogCategory.Display, msg);

        if (changed) Changed?.Invoke(next);
    }
}
