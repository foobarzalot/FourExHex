using Godot;

/// <summary>
/// Autoload that keeps on-screen UI at a roughly constant physical size across
/// resolutions/densities by driving the root <see cref="Window"/>'s
/// <c>ContentScaleFactor</c> from the active screen's DPI. The factor uniformly
/// enlarges all 2D content (the HUD <c>CanvasLayer</c>s and the <c>Node2D</c>
/// map) and sets the GUI's logical layout size to <c>window / factor</c>, so the
/// existing anchor-based HUD layout reflows with no per-widget changes.
///
/// The factor is floored at 1.0 (see <see cref="DisplayScaleMath"/>) and desktop
/// windows report a low logical DPI, so desktop rendering is unchanged — the
/// floor is the implicit desktop gate, no platform check needed. The pure clamp
/// math lives in the model assembly (<see cref="DisplayScaleMath"/>); this is the
/// thin Godot-side adapter that reads DPI and applies it.
///
/// Window-level, so set once at startup and re-applied on viewport resize
/// (device rotation, or window moving to a different-DPI monitor). It persists
/// across scene swaps because every scene shares the one root Window. Registered
/// as an autoload in project.godot under the name "DisplayScale", after
/// LogBootstrap so <see cref="Log"/> is already wired.
/// </summary>
public partial class DisplayScale : Node
{
    public override void _Ready()
    {
        Apply();
        // Catches rotation / monitor moves that could change the active DPI.
        GetViewport().SizeChanged += Apply;
    }

    private void Apply()
    {
        int screen = DisplayServer.WindowGetCurrentScreen();
        float dpi = DisplayServer.ScreenGetDpi(screen);
        // ScreenGetDpi reports physical density, but platforms like macOS
        // already render in OS-scaled logical points (a 2x retina screen
        // presents ~half its physical DPI). Divide by the OS display scale to
        // recover the logical DPI the layout was authored against, so retina
        // desktops floor to 1.0 instead of double-counting. Android reports
        // scale 1.0, so its raw density still drives a scale-up.
        float osScale = DisplayServer.ScreenGetScale(screen);
        float logicalDpi = dpi / Mathf.Max(osScale, 1f);
        float factor = DisplayScaleMath.FactorForDpi(logicalDpi);

        Window window = GetWindow();
        // Setting ContentScaleFactor shrinks the logical viewport, which re-fires
        // SizeChanged → Apply. Skip the write (and the resulting recursion) when
        // unchanged, but still log so the path is observable on every call.
        bool changed = !Mathf.IsEqualApprox(window.ContentScaleFactor, factor);
        if (changed) window.ContentScaleFactor = factor;

        string msg = $"DisplayScale: dpi={dpi} osScale={osScale} logicalDpi={logicalDpi} " +
            $"screen={screen} window={window.Size} factor={factor} changed={changed} " +
            $"logicalViewport={GetViewport().GetVisibleRect().Size}";
        // A factor change is the noteworthy event (startup, monitor move); the
        // no-op path fires on every resize tick, so keep it at Debug to avoid
        // flooding the log while still being observable when chasing issues.
        if (changed) Log.Info(Log.LogCategory.Display, msg);
        else Log.Debug(Log.LogCategory.Display, msg);
    }
}
