// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using Godot;

/// <summary>
/// Walks a resolved Control subtree and reports geometry that broke its
/// container: rects past the viewport edge, tappable/readable content under the
/// notch or home indicator, and degenerate (negative or collapsed) sizes. One
/// <c>Layout:Warn</c> per violation, one <c>Layout:Debug</c> summary per sweep —
/// silent when nothing is wrong, so it stays on permanently and a player's bug
/// report carries layout evidence.
///
/// The decision math is <see cref="LayoutAssert"/> (ViewMath, unit-tested); this
/// file owns only traversal, the skip rules, and logging. Sibling
/// <see cref="LayoutDump"/> reports the same geometry verbatim at
/// <c>Render:Trace</c> without judging it — reach for that when comparing
/// before/after subtrees, and for this when asking "did anything break".
///
/// Sweeps run deferred: <c>PivotOffset</c> is assigned before <c>Scale</c>
/// within a frame and every fit re-check in this codebase is itself deferred, so
/// an immediate walk would read half-applied transforms.
/// </summary>
public static class LayoutAudit
{
    /// <summary>Guards against a pathological or cyclic tree; real screens here
    /// nest well under this.</summary>
    private const int MaxDepth = 24;

    /// <summary>Skip a node and its subtree by tagging it
    /// <c>SetMeta(LayoutAudit.SkipMeta, true)</c> — the escape hatch for
    /// something that legitimately parks off-screen (an animating panel mid
    /// slide-in). Preferred over a name allowlist: it lives at the offending
    /// node, greps cleanly, and survives renames.</summary>
    public const string SkipMeta = "layout_audit_skip";

    /// <summary>Bumped by every completed layout pass. The view-matrix harness
    /// waits for this to hold still rather than counting frames — one window
    /// resize fans out into DisplayScale → SizeChanged → SafeArea → ApplyLayout
    /// → deferred fit rebuilds, and a fixed frame count cannot see that
    /// settle.</summary>
    public static long Epoch { get; private set; }

    public static void BumpEpoch() => Epoch++;

    private static readonly HashSet<string> PendingSweeps = new();

    /// <summary>Queue a sweep for the start of the next frame. Self-dedupes per
    /// (root, tag), so the several triggers that can fire in one frame produce
    /// one sweep.
    ///
    /// A full frame, not <c>CallDeferred</c>: <c>Container.QueueSort</c>
    /// schedules the child sort for the next frame's pre-draw, so a same-frame
    /// deferred callback reads the *previous* layout's sizes and reports a
    /// shrinking window's bars as overflowing when they are merely not yet
    /// re-sorted. Waiting for <c>ProcessFrame</c> puts the sweep after the sort
    /// that the triggering relayout asked for.</summary>
    public static void SweepDeferred(Node root, string tag)
    {
        BumpEpoch();
        if (!Log.IsEnabled(Log.LogCategory.Layout, Log.LogLevel.Warn)) return;
        if (!GodotObject.IsInstanceValid(root)) return;

        // Panels lay themselves out before being added to the tree; asking such
        // a node for its SceneTree logs a Godot error, so check first rather
        // than relying on the null return.
        if (!root.IsInsideTree()) return;

        SceneTree? tree = root.GetTree();
        if (tree == null) return;

        string key = $"{root.GetInstanceId()}::{tag}";
        if (!PendingSweeps.Add(key)) return;

        Callable handler = default;
        handler = Callable.From(() =>
        {
            tree.Disconnect(SceneTree.SignalName.ProcessFrame, handler);
            PendingSweeps.Remove(key);
            if (GodotObject.IsInstanceValid(root)) Sweep(root, tag);
        });
        tree.Connect(SceneTree.SignalName.ProcessFrame, handler);
    }

    /// <summary>Walk <paramref name="root"/>'s visible Control subtree now and
    /// log every violation. Returns the violation count. Costs one array read
    /// when the Layout category is off.</summary>
    public static int Sweep(Node root, string tag)
    {
        if (!Log.IsEnabled(Log.LogCategory.Layout, Log.LogLevel.Warn)) return 0;
        if (!GodotObject.IsInstanceValid(root)) return 0;

        Viewport? viewport = root.GetViewport();
        if (viewport == null) return 0;

        Rect2 vp = viewport.GetVisibleRect();
        var viewportRect = new LayoutRect(vp.Position.X, vp.Position.Y, vp.Size.X, vp.Size.Y);
        LogicalSafeInsets safe = SafeArea.Current;

        Control? injected = LayoutClipInjector.Apply(root);

        int violations = 0;
        int walked = 0;
        Walk(root, root, viewportRect, safe, tag, clipped: false, depth: 0,
            ref violations, ref walked);

        LayoutClipInjector.Restore(injected);

        Log.Debug(Log.LogCategory.Layout,
            $"[layout-audit] {tag}: {violations} violation(s) / {walked} control(s) " +
            $"vp={vp.Size.X:0}x{vp.Size.Y:0} " +
            $"safe=(t={safe.Top:0.#} b={safe.Bottom:0.#} l={safe.Left:0.#} r={safe.Right:0.#})");

        return violations;
    }

    private static void Walk(
        Node node, Node root, in LayoutRect viewport, LogicalSafeInsets safe, string tag,
        bool clipped, int depth, ref int violations, ref int walked)
    {
        if (depth > MaxDepth) return;
        if (node.HasMeta(SkipMeta)) return;

        // A hidden CanvasLayer's Controls report IsVisibleInTree() true, so the
        // layer has to be checked on its own — every closed modal in this
        // codebase is a hidden CanvasLayer full of stale geometry.
        if (node is CanvasLayer layer && !layer.Visible) return;

        if (node is Control control)
        {
            if (!control.IsVisibleInTree()) return;

            walked++;
            if (!clipped && CheckControl(control, root, viewport, safe, tag)) violations++;

            // Descendants of a clipping container are exempt: PageCarousel and
            // the menu's page stack deliberately park off-box content that the
            // clipper guarantees never shows. The clipper itself is still
            // checked — that is the rect that actually matters.
            clipped |= control.ClipContents;
        }

        foreach (Node child in node.GetChildren())
        {
            Walk(child, root, viewport, safe, tag, clipped, depth + 1, ref violations, ref walked);
        }
    }

    private static bool CheckControl(
        Control control, Node root, in LayoutRect viewport, LogicalSafeInsets safe, string tag)
    {
        Rect2 r = control.GetGlobalRect();
        Vector2 min = control.GetCombinedMinimumSize();

        bool found = LayoutAssert.TryFindViolation(
            new LayoutRect(r.Position.X, r.Position.Y, r.Size.X, r.Size.Y),
            viewport, safe,
            visible: true,
            minWidth: min.X, minHeight: min.Y,
            enforceSafeArea: EnforcesSafeArea(control),
            tolerance: LayoutAssert.TolerancePx,
            out LayoutViolation violation);

        if (!found) return false;

        Log.Warn(Log.LogCategory.Layout,
            $"[layout-audit] {tag} {root.GetPathTo(control)}({control.GetType().Name}) " +
            $"{LayoutAssert.Describe(violation)} " +
            $"rect=({r.Position.X:0},{r.Position.Y:0} {r.Size.X:0}x{r.Size.Y:0}) " +
            $"vp=({viewport.Width:0}x{viewport.Height:0})");
        return true;
    }

    /// <summary>Safe-area intrusion is a violation only for content a player
    /// taps or reads. Scrims, backgrounds and panel chrome legitimately extend
    /// under the notch — the design rule is that nothing interactive or legible
    /// sits there.</summary>
    private static bool EnforcesSafeArea(Control control) =>
        control is BaseButton or LineEdit or TextEdit or Range or Label or RichTextLabel;
}

/// <summary>
/// Fault injection that proves the audit actually detects — set
/// <c>FOUREXHEX_LAYOUT_INJECT_CLIP=1</c> (first visible Button in the swept
/// tree) or to a node-name substring, and that node is shoved off-screen for the
/// duration of one sweep, then restored. Exercises the whole chain — traversal,
/// the LayoutAssert math, log formatting, the harness verdict, the runner's grep
/// — without any broken layout ever being committed.
/// </summary>
internal static class LayoutClipInjector
{
    private const float OffscreenShiftPx = 9999f;

    private static readonly string Target = OS.GetEnvironment("FOUREXHEX_LAYOUT_INJECT_CLIP");

    private static Vector2 _savedPosition;

    internal static Control? Apply(Node root)
    {
        if (Target.Length == 0) return null;

        Control? victim = Find(root, matchAnyButton: Target == "1", depth: 0);
        if (victim == null)
        {
            // Debug, not Warn: most sweeps cover a hidden or empty surface with
            // no eligible victim, and a spurious Warn here would fail the very
            // harness run this injection exists to validate.
            Log.Debug(Log.LogCategory.Layout,
                $"[layout-audit] clip injection found no node matching '{Target}'");
            return null;
        }

        _savedPosition = victim.Position;
        victim.Position += new Vector2(OffscreenShiftPx, 0f);
        Log.Debug(Log.LogCategory.Layout,
            $"[layout-audit] clip injected into {root.GetPathTo(victim)}({victim.GetType().Name})");
        return victim;
    }

    internal static void Restore(Control? victim)
    {
        if (victim != null && GodotObject.IsInstanceValid(victim)) victim.Position = _savedPosition;
    }

    private static Control? Find(Node node, bool matchAnyButton, int depth)
    {
        if (depth > 24) return null;
        if (node is CanvasLayer layer && !layer.Visible) return null;

        if (node is Control c && c.IsVisibleInTree())
        {
            bool hit = matchAnyButton
                ? c is BaseButton
                : c.Name.ToString().Contains(Target, System.StringComparison.OrdinalIgnoreCase);
            if (hit) return c;
        }

        foreach (Node child in node.GetChildren())
        {
            Control? found = Find(child, matchAnyButton, depth + 1);
            if (found != null) return found;
        }
        return null;
    }
}
