// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Text;

/// <summary>What is wrong with a Control's resolved rect. Ordered by
/// specificity — a degenerate rect also overflows, and reporting one node once
/// beats emitting four lines for the same underlying break.</summary>
public enum LayoutViolationKind
{
    None = 0,
    /// <summary>Width or height is negative — an inverted rect.
    /// <c>HudPanelMath.ClampWidth</c> has no lower clamp and
    /// <c>PanelFitMath.AvailableBox</c> subtracts without flooring, so a narrow
    /// viewport really produces these.</summary>
    NegativeSize,
    /// <summary>Visible, asked for space (a non-zero combined minimum size on
    /// that axis), and got ~none. An empty container measuring zero is ordinary;
    /// a button that asked for 44px and got 0 is the bug signature.</summary>
    ZeroSizeButVisible,
    /// <summary>Extends past a viewport edge.</summary>
    OverflowsViewport,
    /// <summary>Overlaps the notch / Dynamic Island / home-indicator band.
    /// Enforced only for nodes the caller marks as interactive or readable.</summary>
    IntrudesSafeArea,
    /// <summary>The control's rect fits its container, but its text does not fit
    /// the rect — Godot ellipsizes or clips inside the control without changing
    /// its geometry, so rect-only checks read this as clean while the player
    /// sees "Confirm Purchas…".</summary>
    TextTruncated,
}

/// <summary>An axis-aligned rect in logical pixels — the same space
/// <c>Control.GetGlobalRect()</c> and <see cref="LogicalSafeInsets"/> use.</summary>
public readonly record struct LayoutRect(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;
}

/// <summary>One failure. The overflow components are per-edge positive
/// px — 0 means that edge is fine — so a log line can name the direction the
/// node broke out in.</summary>
public readonly record struct LayoutViolation(
    LayoutViolationKind Kind,
    float OverflowLeft, float OverflowTop, float OverflowRight, float OverflowBottom)
{
    public static LayoutViolation None => new(LayoutViolationKind.None, 0f, 0f, 0f, 0f);
}

/// <summary>
/// Pure geometry behind the layout audit: decide whether a Control's resolved
/// rect is a violation, and which kind. Godot-free so it is unit-testable; the
/// Godot side (<c>scripts/LayoutAudit.cs</c>) contributes only scene-tree
/// traversal, the skip rules, and logging.
/// </summary>
public static class LayoutAssert
{
    /// <summary>Sub-pixel slack. Godot container sorts land on half-pixel
    /// boundaries and Scale-based fits accumulate float error, so anything
    /// under this is not a bug.</summary>
    public const float TolerancePx = 0.5f;

    /// <summary>The viewport deflated by the safe-area insets — the box
    /// interactive and readable content must stay inside of.</summary>
    public static LayoutRect SafeBox(in LayoutRect viewport, LogicalSafeInsets safe) =>
        new(viewport.X + safe.Left,
            viewport.Y + safe.Top,
            viewport.Width - safe.Left - safe.Right,
            viewport.Height - safe.Top - safe.Bottom);

    /// <summary>Per-edge positive overshoot of <paramref name="inner"/> past
    /// <paramref name="outer"/>; all zero when contained.</summary>
    public static (float left, float top, float right, float bottom) Overflow(
        in LayoutRect inner, in LayoutRect outer) =>
        (System.MathF.Max(0f, outer.X - inner.X),
         System.MathF.Max(0f, outer.Y - inner.Y),
         System.MathF.Max(0f, inner.Right - outer.Right),
         System.MathF.Max(0f, inner.Bottom - outer.Bottom));

    /// <summary>True when <paramref name="outer"/> is wholly inside
    /// <paramref name="inner"/> — i.e. inner covers the whole of outer.</summary>
    private static bool Contains(in LayoutRect inner, in LayoutRect outer, float tolerance) =>
        inner.X <= outer.X + tolerance
        && inner.Y <= outer.Y + tolerance
        && inner.Right >= outer.Right - tolerance
        && inner.Bottom >= outer.Bottom - tolerance;

    /// <summary>True when <paramref name="inner"/> sits inside
    /// <paramref name="outer"/> to within <paramref name="tolerance"/>.</summary>
    public static bool FitsWithin(
        in LayoutRect inner, in LayoutRect outer, float tolerance = TolerancePx)
    {
        (float left, float top, float right, float bottom) = Overflow(inner, outer);
        return left <= tolerance && top <= tolerance
            && right <= tolerance && bottom <= tolerance;
    }

    /// <summary>The single most specific violation for this rect, or
    /// <see cref="LayoutViolationKind.None"/>. Kinds are checked in declaration
    /// order so one broken node yields one line, not four.</summary>
    public static bool TryFindViolation(
        in LayoutRect rect, in LayoutRect viewport, LogicalSafeInsets safe,
        bool visible, float minWidth, float minHeight,
        bool enforceSafeArea, float tolerance,
        out LayoutViolation violation)
    {
        violation = LayoutViolation.None;

        // A hidden node legitimately parks at stale or off-screen geometry —
        // every closed modal in this codebase does exactly that.
        if (!visible) return false;

        if (rect.Width < -tolerance || rect.Height < -tolerance)
        {
            violation = new LayoutViolation(LayoutViolationKind.NegativeSize, 0f, 0f, 0f, 0f);
            return true;
        }

        bool collapsedX = rect.Width <= tolerance && minWidth > tolerance;
        bool collapsedY = rect.Height <= tolerance && minHeight > tolerance;
        if (collapsedX || collapsedY)
        {
            violation = new LayoutViolation(
                LayoutViolationKind.ZeroSizeButVisible, 0f, 0f, 0f, 0f);
            return true;
        }

        // Modal scrims and full-bleed backgrounds are deliberately sized to
        // cover everything, often with slack. A control that fully contains the
        // viewport is a backdrop, not content that escaped its container.
        bool isBackdrop = Contains(rect, viewport, tolerance);

        (float l, float t, float r, float b) = Overflow(rect, viewport);
        if (!isBackdrop && (l > tolerance || t > tolerance || r > tolerance || b > tolerance))
        {
            violation = new LayoutViolation(LayoutViolationKind.OverflowsViewport, l, t, r, b);
            return true;
        }

        // Full-rect scrims and panel chrome legitimately extend under the notch;
        // the rule is "nothing tappable or readable there", which the caller
        // decides per node.
        if (enforceSafeArea && !isBackdrop)
        {
            (float sl, float st, float sr, float sb) = Overflow(rect, SafeBox(viewport, safe));
            if (sl > tolerance || st > tolerance || sr > tolerance || sb > tolerance)
            {
                violation = new LayoutViolation(
                    LayoutViolationKind.IntrudesSafeArea, sl, st, sr, sb);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Does this control's text fit inside it? Callers pass the control's rect
    /// size and the size the text actually wants (Godot's
    /// <c>GetMultilineStringSize</c> / <c>GetCombinedMinimumSize</c>).
    ///
    /// Separate from <see cref="TryFindViolation"/> because it needs measured
    /// text rather than geometry, and only text-bearing controls can supply it.
    ///
    /// <b>Width only.</b> Horizontal is where Godot actually ellipsizes, and it
    /// is what the player sees as "Confirm Purchas…". A control shorter than its
    /// measured height is almost always theme content-margin accounting rather
    /// than lost text: on this codebase the height axis produced three times the
    /// findings of the width axis and none of them were real.
    /// </summary>
    public static bool TryFindTextTruncation(
        float controlWidth, float controlHeight,
        float desiredWidth, float desiredHeight,
        float tolerance,
        out LayoutViolation violation)
    {
        float over = MathF.Max(0f, desiredWidth - controlWidth);

        if (over <= tolerance)
        {
            violation = LayoutViolation.None;
            return false;
        }

        violation = new LayoutViolation(LayoutViolationKind.TextTruncated, 0f, 0f, over, 0f);
        return true;
    }

    /// <summary>Stable one-line rendering for the log. Clean edges are omitted
    /// so the line names only the directions the node actually broke out in.</summary>
    public static string Describe(in LayoutViolation violation)
    {
        if (violation.Kind == LayoutViolationKind.None) return "None";

        var sb = new StringBuilder(violation.Kind.ToString());
        bool any = violation.OverflowLeft > 0f || violation.OverflowTop > 0f
            || violation.OverflowRight > 0f || violation.OverflowBottom > 0f;
        if (!any) return sb.ToString();

        sb.Append(" over(");
        bool first = true;
        Append(sb, "l", violation.OverflowLeft, ref first);
        Append(sb, "t", violation.OverflowTop, ref first);
        Append(sb, "r", violation.OverflowRight, ref first);
        Append(sb, "b", violation.OverflowBottom, ref first);
        sb.Append(')');
        return sb.ToString();

        static void Append(StringBuilder sb, string edge, float px, ref bool first)
        {
            if (px <= 0f) return;
            if (!first) sb.Append(' ');
            sb.Append(edge).Append('=').Append(px.ToString("0.#"));
            first = false;
        }
    }
}
