using System;
using Godot;

/// <summary>
/// Shared lifecycle for the orientation-aware HUDs (<see cref="HudView"/>,
/// <see cref="MapEditorHudView"/>). Both are CanvasLayers that reflow between a
/// single landscape top bar and a portrait top/bottom split. This base owns the
/// bar containers, the viewport-resize subscription, and the orientation-flip →
/// relayout → inset-publish cycle; subclasses supply their cluster wiring and
/// inset policy via the abstract hooks. The shared low-level bar chrome lives in
/// <see cref="HudBars"/>; this owns the coordination on top of it.
/// </summary>
public abstract partial class OrientationHud : CanvasLayer
{
    /// <summary>Fires (topInset, bottomInset) — the pixels the map must reserve
    /// for the HUD bars — on orientation flips and (in <see cref="HudView"/>)
    /// top-bar visibility changes. The scene root relays it to
    /// <c>HexMapView.SetMapInsets</c>.</summary>
    public event Action<float, float>? MapInsetsChanged;

    /// <summary>Bar containers, rebuilt by ApplyLayout. <see cref="BottomBar"/>
    /// is null in landscape (single top strip). Subclasses read these while
    /// building their bars.</summary>
    protected Panel? TopBar;
    protected Panel? BottomBar;

    protected ScreenOrientation Orientation { get; private set; } = ScreenOrientation.Landscape;

    /// <summary>Call at the end of <c>_Ready</c>, once the clusters are built:
    /// resolve the orientation, lay out the bars, track resizes, and publish the
    /// initial insets.</summary>
    protected void InitOrientation()
    {
        Orientation = ResolveOrientation();
        ApplyLayout();
        GetViewport().SizeChanged += OnViewportResized;
        PublishInsets();
        OnViewportMetricsChanged();
    }

    private ScreenOrientation ResolveOrientation()
    {
        Vector2 vp = GetViewport().GetVisibleRect().Size;
        return ScreenLayout.Resolve(vp.X, vp.Y);
    }

    /// <summary>Reparent the clusters into freshly-built orientation-specific
    /// bars. The clusters are detached first so freeing the old bars can't free
    /// them.</summary>
    private void ApplyLayout()
    {
        DetachClusters();
        TopBar?.QueueFree();
        BottomBar?.QueueFree();
        TopBar = null;
        BottomBar = null;

        if (Orientation == ScreenOrientation.Landscape)
            BuildLandscapeBars();
        else
            BuildPortraitBars();

        OnLayoutApplied();
    }

    private void OnViewportResized()
    {
        ScreenOrientation o = ResolveOrientation();
        if (o != Orientation)
        {
            Orientation = o;
            ApplyLayout();
            Vector2 vp = GetViewport().GetVisibleRect().Size;
            Log.Info(Log.LogCategory.Render, $"{GetType().Name}: orientation → {o} at {vp.X}x{vp.Y}.");
        }
        PublishInsets();
        // Width-responsive tweaks that don't depend on an orientation flip
        // (e.g. dropping captions in a narrow landscape window).
        OnViewportMetricsChanged();
    }

    /// <summary>Recompute the map insets via <see cref="ComputeInsets"/> and
    /// raise <see cref="MapInsetsChanged"/>. Subclasses call this when something
    /// other than a resize changes the insets (e.g. HudView's top-bar
    /// visibility following the selection).</summary>
    protected void PublishInsets()
    {
        MapInsets insets = ComputeInsets();
        Log.Debug(Log.LogCategory.Render,
            $"{GetType().Name}: insets top={insets.Top} bottom={insets.Bottom} ({Orientation}).");
        MapInsetsChanged?.Invoke(insets.Top, insets.Bottom);
    }

    // ---- Subclass hooks --------------------------------------------------

    /// <summary>Detach the persistent clusters from the old bars.</summary>
    protected abstract void DetachClusters();

    /// <summary>Build <see cref="TopBar"/> (single strip) and parent the
    /// clusters into it.</summary>
    protected abstract void BuildLandscapeBars();

    /// <summary>Build <see cref="TopBar"/> + <see cref="BottomBar"/> and parent
    /// the clusters into them.</summary>
    protected abstract void BuildPortraitBars();

    /// <summary>The reserved map insets for the current orientation.</summary>
    protected abstract MapInsets ComputeInsets();

    /// <summary>Optional post-layout step (e.g. caption / seed-label
    /// visibility). Runs after the bars are (re)built. Default: nothing.</summary>
    protected virtual void OnLayoutApplied() { }

    /// <summary>Runs on every viewport resize (and at init), AFTER any
    /// orientation relayout — for width-responsive tweaks that don't require
    /// rebuilding the bars (e.g. hiding captions in a narrow window).
    /// Default: nothing.</summary>
    protected virtual void OnViewportMetricsChanged() { }
}
