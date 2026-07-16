// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using Godot;

/// <summary>
/// Shared lifecycle for the floating, orientation- and breakpoint-aware HUDs
/// (<see cref="HudView"/>, <see cref="MapEditorHudView"/>). Both are CanvasLayers
/// that reflow between:
///  - landscape (status top-left + undo/options top-right, action clusters in
///    two side rails) and
///  - portrait  (same corner zones, action clusters in a full-width bottom bar)
/// and between compact (phone) and expanded (tablet / desktop) variants.
///
/// This base owns the five zone containers (TopLeftZone, TopRightZone,
/// BottomBar, LeftRail, RightRail), the viewport-resize subscription, the safe-
/// area subscription, and the orientation+compact transition lifecycle.
/// Subclasses parent their persistent clusters into whichever zones exist for
/// the current orientation; they do NOT build the zone containers themselves.
///
/// The shared low-level zone chrome lives in <see cref="HudBars"/>.
/// </summary>
public abstract partial class OrientationHud : CanvasLayer
{
    /// <summary>Fires (topInset, bottomInset) — the pixels the map must reserve
    /// for the HUD bars — on orientation flips, compact transitions, and safe-
    /// area changes. The scene root relays it to
    /// <c>HexMapView.SetMapInsets</c>. With the D1 floating layout these are
    /// usually (0, 0) — the HUD floats over the map and only the buttons /
    /// chips block clicks on their own footprint.</summary>
    public event Action<float, float>? MapInsetsChanged;

    /// <summary>Floating corner zones — present in BOTH orientations. Each is
    /// a content-sized HBox anchored to its corner with safe-area insets. The
    /// chips parented inside block clicks only over their own footprint, so
    /// the empty space between the corners stays map-clickable.</summary>
    protected HBoxContainer TopLeftZone { get; private set; } = null!;
    protected HBoxContainer TopRightZone { get; private set; } = null!;

    /// <summary>Full-width strip pinned to the bottom of the viewport in
    /// portrait — blocks clicks across the bottom so taps in the gap between
    /// hero buttons don't fall through to the obscured map. Subclasses
    /// populate it with their action layout. <c>null</c> in landscape
    /// (rails handle the action zone there).</summary>
    protected Panel? BottomBar { get; private set; }

    /// <summary>Landscape side rails — 78-px wide panels anchored to the left
    /// / right viewport edges. They block clicks in their columns. Vertical
    /// alignment of the inner button group is Center in compact (phone) and
    /// End/bottom in expanded (tablet / desktop) — the spec's lower-corner
    /// thumb zone. <c>null</c> in portrait.</summary>
    protected Panel? LeftRail { get; private set; }
    protected Panel? RightRail { get; private set; }

    /// <summary>The button-group VBox inside <see cref="LeftRail"/> /
    /// <see cref="RightRail"/>. Subclasses parent their action clusters here
    /// rather than directly into the rail Panel.</summary>
    protected VBoxContainer? LeftRailGroup { get; private set; }
    protected VBoxContainer? RightRailGroup { get; private set; }

    /// <summary>Phone↔tablet split: true when the shorter viewport edge is
    /// below ~600 logical px (with ±dead-band hysteresis — see
    /// <see cref="ScreenLayout.IsCompact"/>). Drives palette/roster collapse
    /// and the landscape-rail vertical alignment (centered → bottom-
    /// anchored).</summary>
    public bool Compact { get; private set; }

    protected ScreenOrientation Orientation { get; private set; } = ScreenOrientation.Landscape;

    // True once InitOrientation hooked the viewport's SizeChanged
    // (_ExitTree must not disconnect a never-connected signal).
    private bool _viewportResizeHooked;

    /// <summary>Call at the end of <c>_Ready</c>, once the clusters are built:
    /// resolve orientation + compact, lay out the zones, track resizes, and
    /// publish the initial insets.</summary>
    protected void InitOrientation()
    {
        Vector2 vp = GetViewport().GetVisibleRect().Size;
        Orientation = ScreenLayout.Resolve(vp.X, vp.Y);
        Compact = ScreenLayout.IsCompact(vp.X, vp.Y, prevWasCompact: false);
        ApplyLayout();
        GetViewport().SizeChanged += OnViewportResized;
        _viewportResizeHooked = true;
        // Notch / Dynamic Island / home-indicator insets can change without a
        // viewport resize (e.g. status-bar show/hide). Rebuild the zones when
        // the safe area shifts so the corner chips stay inside the safe zone.
        SafeArea.Changed += OnSafeAreaChanged;
        PublishInsets();
        OnViewportMetricsChanged();
    }

    public override void _ExitTree()
    {
        // The root Window outlives this node across scene swaps; without
        // the unsubscribe a later resize invokes a handler on a freed node.
        // Guarded: disconnecting a never-connected Godot signal errors, and
        // a subclass may exit without having called InitOrientation.
        SafeArea.Changed -= OnSafeAreaChanged;
        if (!_viewportResizeHooked) return;
        GetViewport().SizeChanged -= OnViewportResized;
        _viewportResizeHooked = false;
        Log.Debug(Log.LogCategory.Display,
            $"{GetType().Name}: viewport SizeChanged unsubscribed on exit");
    }

    private void OnSafeAreaChanged(LogicalSafeInsets _)
    {
        ApplyLayout();
        PublishInsets();
        OnViewportMetricsChanged();
    }

    /// <summary>Tear down the previous zones and build fresh ones for the
    /// current (Orientation, Compact) pair, then ask the subclass to parent
    /// its clusters into them. Detaches clusters first so freeing the old
    /// zones can't free them.</summary>
    private void ApplyLayout()
    {
        DetachClusters();
        TopLeftZone?.QueueFree();
        TopRightZone?.QueueFree();
        BottomBar?.QueueFree();
        LeftRail?.QueueFree();
        RightRail?.QueueFree();
        TopLeftZone = null!;
        TopRightZone = null!;
        BottomBar = null;
        LeftRail = null;
        RightRail = null;
        LeftRailGroup = null;
        RightRailGroup = null;

        // Build rails / bottom bar FIRST so the corner zones (added next)
        // sit on top — otherwise the landscape rails (full-height Panels
        // with MouseFilter.Stop) overlap the right-corner column and
        // intercept clicks meant for the Options / undo / redo buttons.
        if (Orientation == ScreenOrientation.Landscape)
        {
            (LeftRail, LeftRailGroup) = HudBars.MakeRail(left: true, alignBottom: !Compact, width: LeftRailWidth);
            (RightRail, RightRailGroup) = HudBars.MakeRail(left: false, alignBottom: !Compact, width: RightRailWidth);
            AddChild(LeftRail);
            AddChild(RightRail);
        }
        else
        {
            BottomBar = HudBars.MakeBottomBar();
            AddChild(BottomBar);
        }

        TopLeftZone = HudBars.MakeCornerZone(left: true);
        TopRightZone = HudBars.MakeCornerZone(left: false);
        AddChild(TopLeftZone);
        AddChild(TopRightZone);

        if (Orientation == ScreenOrientation.Landscape)
        {
            BuildLandscapeBars();
        }
        else
        {
            BuildPortraitBars();
        }

        Log.Debug(Log.LogCategory.Render,
            $"{GetType().Name}: layout applied orient={Orientation} compact={Compact} " +
            $"zones=TL+TR+{(Orientation == ScreenOrientation.Landscape ? "LR+RR" : "BB")}.");

        OnLayoutApplied();
    }

    private void OnViewportResized()
    {
        ulong frame = Engine.GetProcessFrames();
        ulong t0 = Time.GetTicksMsec();
        Vector2 vp0 = GetViewport().GetVisibleRect().Size;
        Log.Debug(Log.LogCategory.Render,
            $"{GetType().Name}: resize@frame={frame} t={t0}ms vp={vp0.X}x{vp0.Y}.");

        ScreenOrientation newOrient = ScreenLayout.Resolve(vp0.X, vp0.Y);
        bool newCompact = ScreenLayout.IsCompact(vp0.X, vp0.Y, Compact);
        bool changed = false;
        if (newOrient != Orientation)
        {
            Orientation = newOrient;
            changed = true;
            Log.Info(Log.LogCategory.Render, $"{GetType().Name}: orientation → {newOrient} at {vp0.X}x{vp0.Y}.");
        }
        if (newCompact != Compact)
        {
            Compact = newCompact;
            changed = true;
            Log.Info(Log.LogCategory.Render,
                $"{GetType().Name}: compact → {newCompact} at {vp0.X}x{vp0.Y}.");
        }
        if (changed) ApplyLayout();
        PublishInsets();
        // Width-responsive tweaks that don't depend on a layout flip
        // (e.g. swapping collapsed↔expanded palette variants).
        OnViewportMetricsChanged();

        Log.Debug(Log.LogCategory.Render,
            $"{GetType().Name}: resize settled@frame={Engine.GetProcessFrames()} " +
            $"dt={Time.GetTicksMsec() - t0}ms ({Orientation}, compact={Compact}).");
    }

    /// <summary>Recompute the map insets via <see cref="ComputeInsets"/> and
    /// raise <see cref="MapInsetsChanged"/>. Subclasses call this when
    /// something other than a resize changes the insets.</summary>
    protected void PublishInsets()
    {
        MapInsets insets = ComputeInsets();
        Log.Debug(Log.LogCategory.Render,
            $"{GetType().Name}: insets top={insets.Top} bottom={insets.Bottom} ({Orientation}).");
        MapInsetsChanged?.Invoke(insets.Top, insets.Bottom);
    }

    // ---- Subclass hooks --------------------------------------------------

    /// <summary>Detach the persistent clusters from the previous zones so a
    /// rebuild won't free them.</summary>
    protected abstract void DetachClusters();

    /// <summary>Parent the persistent clusters into <see cref="TopLeftZone"/>,
    /// <see cref="TopRightZone"/>, <see cref="LeftRailGroup"/>,
    /// <see cref="RightRailGroup"/> for the landscape variant.</summary>
    protected abstract void BuildLandscapeBars();

    /// <summary>Parent the persistent clusters into <see cref="TopLeftZone"/>,
    /// <see cref="TopRightZone"/>, and <see cref="BottomBar"/> for the
    /// portrait variant.</summary>
    protected abstract void BuildPortraitBars();

    /// <summary>The reserved map insets for the current orientation. Floating
    /// D1 normally returns (0, 0) — the HUD doesn't push the map around — but
    /// subclasses can override (e.g. to clear a hosted toolbar above).</summary>
    protected abstract MapInsets ComputeInsets();

    /// <summary>Width of the landscape side rails, logical px. Defaults to
    /// <see cref="HudBars.RailWidth"/>; a subclass overrides (e.g. the map
    /// editor on compact, where the paint tools wrap to a second column)
    /// to widen one rail. Read each <see cref="ApplyLayout"/>, so
    /// a compact flip rebuilds the rails at the new width.</summary>
    protected virtual float LeftRailWidth => HudBars.RailWidth;
    protected virtual float RightRailWidth => HudBars.RailWidth;

    /// <summary>Optional post-layout step. Runs after the zones are
    /// (re)built. Default: nothing.</summary>
    protected virtual void OnLayoutApplied() { }

    /// <summary>Runs on every viewport resize (and at init), AFTER any
    /// orientation / compact relayout — for width-responsive tweaks that
    /// don't require rebuilding the zones (e.g. swapping collapsed↔expanded
    /// palette variants). Default: nothing.</summary>
    protected virtual void OnViewportMetricsChanged() { }
}
