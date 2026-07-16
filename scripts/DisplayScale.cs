// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using Godot;

/// <summary>
/// Autoload that keeps on-screen UI at a roughly constant physical size across
/// resolutions/densities by driving the root <see cref="Window"/>'s
/// <c>ContentScaleFactor</c> from the active screen's DPI. The factor uniformly
/// enlarges all 2D content (the HUD <c>CanvasLayer</c>s and the <c>Node2D</c>
/// map) and sets the GUI's logical layout size to <c>window / factor</c>, so the
/// existing anchor-based HUD layout reflows with no per-widget changes.
///
/// On mobile platforms (<c>OS.HasFeature("mobile")</c>) the minimum factor is
/// lifted to <see cref="DisplayScaleMath.MobileMinFactor"/> (currently tuned to
/// S9-portrait parity ≈2.22) so devices whose natural DPI factor floors to 1.0
/// — notably iPhones, whose Apple-points system lands at ~158 logical dpi, just
/// under our 160 baseline — render UI at the same physical size as the S9 in
/// portrait. The S9-portrait natural factor coincides with the floor and is
/// unaffected; S9-landscape's natural ≈1.67 is lifted by the floor, matching
/// iPhone landscape. Desktop is non-mobile and unaffected. See
/// <see cref="DisplayScaleMath"/> for the pure clamp math.
///
/// Window-level, so set once at startup and re-applied on viewport resize
/// (device rotation, or window moving to a different-DPI monitor). It persists
/// across scene swaps because every scene shares the one root Window. Registered
/// as an autoload in project.godot under the name "DisplayScale", after
/// LogBootstrap so <see cref="Log"/> is already wired.
///
/// The <c>FOUREXHEX_UI_SCALE</c> env var bypasses the DPI computation entirely
/// and forces a specific factor — used to reproduce a device's scale locally
/// (see RELEASE.md §6 Option B). Honored on all platforms; takes precedence
/// over both the DPI path and the mobile floor.
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
        Window window = GetWindow();
        bool isMobile = OS.HasFeature("mobile");
        int screen = DisplayServer.WindowGetCurrentScreen();
        float dpi = DisplayServer.ScreenGetDpi(screen);
        // ScreenGetDpi reports physical density, but platforms like macOS already
        // render in OS-scaled logical points (a 2x retina screen presents ~half
        // its physical DPI). Divide by the OS display scale to recover the
        // logical DPI the layout was authored against, so retina desktops floor
        // to 1.0 instead of double-counting.
        float osScale = DisplayServer.ScreenGetScale(screen);
        // Desktop divides by osScale to recover Apple's logical-points DPI
        // (so a 2× retina laptop floors to 1.0). Mobile does NOT — iOS's
        // retina pixel doubling doesn't change the physical size our
        // buttons render at, and dividing by osScale there just makes
        // iPhones come up small relative to mid-DPI Androids.
        float logicalDpi = dpi / Mathf.Max(osScale, 1f);
        float minFactor = isMobile ? DisplayScaleMath.MobileMinFactor : DisplayScaleMath.MinFactor;

        // Env-var override wins. Lets a dev Mac reproduce an iPhone/Android
        // factor locally without touching code (see RELEASE.md §6 Option B).
        string overrideRaw = OS.GetEnvironment("FOUREXHEX_UI_SCALE");
        float factor;
        bool overrideActive;
        if (!string.IsNullOrEmpty(overrideRaw)
            && float.TryParse(overrideRaw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float overrideFactor)
            && overrideFactor > 0f)
        {
            factor = overrideFactor;
            overrideActive = true;
        }
        else
        {
            // iOS-only: raw DPI / 180 (Apple's ScreenGetScale is the retina
            // multiplier, not a system density choice — dividing by it
            // mis-counts the actual physical density and makes iPhones come
            // up small). Android's ScreenGetScale represents the system's
            // density bucket (xhdpi / xxhdpi / etc.) so logicalDpi / 160 is
            // the right input there; this preserves the S9-portrait 2.22
            // calibration that's been shipping. Desktop unchanged.
            bool isIos = OS.HasFeature("ios");
            factor = isIos
                ? DisplayScaleMath.FactorForRawMobileDpi(dpi, minFactor)
                : DisplayScaleMath.FactorForDpi(logicalDpi, minFactor);
            overrideActive = false;
        }

        // Setting ContentScaleFactor shrinks the logical viewport, which re-fires
        // SizeChanged → Apply. Skip the write (and the resulting recursion) when
        // unchanged, but still log so the path is observable on every call.
        bool changed = !Mathf.IsEqualApprox(window.ContentScaleFactor, factor);
        if (changed) window.ContentScaleFactor = factor;

        string msg = $"DisplayScale: dpi={dpi} osScale={osScale} logicalDpi={logicalDpi} " +
            $"isMobile={isMobile} minFactor={minFactor} " +
            $"override={(overrideActive ? overrideRaw : "<none>")} " +
            $"screen={screen} window={window.Size} factor={factor} changed={changed} " +
            $"logicalViewport={GetViewport().GetVisibleRect().Size}";
        // A factor change is the noteworthy event (startup, monitor move); the
        // no-op path fires on every resize tick, so keep it at Debug to avoid
        // flooding the log while still being observable when chasing issues.
        if (changed) Log.Info(Log.LogCategory.Display, msg);
        else Log.Debug(Log.LogCategory.Display, msg);
    }
}
