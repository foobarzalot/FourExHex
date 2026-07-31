// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;

/// <summary>
/// Pure shrink-to-fit / cap-and-center math for centered menu panels and
/// modals. Godot-free (plain floats + <see cref="LogicalSafeInsets"/>) so it's
/// unit-testable; the view (MainMenuScene, SlotPickerDialog, SettingsPanel,
/// CreditsPanel, LandscapeMenuChrome) reads viewport/safe-area/design size from
/// Godot, gets the numbers here, and applies the resulting
/// <c>Scale</c>/<c>PivotOffset</c>/offsets to its nodes. Consolidates the
/// "never upscale, pivot-centered fit" computation that was reimplemented
/// inline at each call site.
/// </summary>
public static class PanelFitMath
{
    /// <summary>
    /// The content box available to a centered panel: the viewport minus the
    /// safe-area insets minus a symmetric <paramref name="marginPerSide"/>
    /// (subtracted on both edges, i.e. <c>2·margin</c> per axis).
    /// </summary>
    public static (float availW, float availH) AvailableBox(
        float vpWidth, float vpHeight, LogicalSafeInsets safe, float marginPerSide)
    {
        return (
            vpWidth - safe.Left - safe.Right - marginPerSide * 2f,
            vpHeight - safe.Top - safe.Bottom - marginPerSide * 2f);
    }

    /// <summary>
    /// Uniform scale that fits a <paramref name="designW"/>×<paramref name="designH"/>
    /// panel into <paramref name="availW"/>×<paramref name="availH"/> — the smaller
    /// of the two axis ratios, clamped to ≤ 1 so the panel never upscales. A
    /// degenerate (≤ 0) design returns 1 (nothing to fit).
    /// </summary>
    public static float ScaleToFit(float designW, float designH, float availW, float availH)
    {
        if (designW <= 0f || designH <= 0f) return 1f;
        return MathF.Min(1f, MathF.Min(availW / designW, availH / designH));
    }

    /// <summary>
    /// Width-only fit: scale is driven by width alone (clamped ≤ 1) so the panel
    /// keeps its font sizes in a short viewport; the pre-scale height is instead
    /// capped so the scaled height fits <paramref name="availH"/> (a scroll body
    /// absorbs the reduction). Returns <c>(scale, panelH)</c>. When the scale
    /// collapses to 0 the height cap falls back to the design height.
    /// </summary>
    public static (float scale, float panelH) WidthFitWithHeightCap(
        float designW, float designH, float availW, float availH)
    {
        float scale = MathF.Min(1f, availW / designW);
        float maxLogicalH = scale > 0f ? availH / scale : designH;
        float panelH = MathF.Min(designH, maxLogicalH);
        return (scale, panelH);
    }

    /// <summary>
    /// Cap-and-fill size for a reflowing landscape surface: it grows to fill the
    /// available box (viewport minus insets minus <c>2·edge</c>, floored at 0)
    /// up to <paramref name="maxW"/>×<paramref name="maxH"/>, then stays a tidy
    /// centered panel. Returns the surface <c>(w, h)</c>; the view applies the
    /// centering offsets.
    /// </summary>
    public static (float w, float h) CappedFill(
        float vpWidth, float vpHeight, LogicalSafeInsets s, float edge, float maxW, float maxH)
    {
        float availW = MathF.Max(0f, vpWidth - s.Left - s.Right - edge * 2f);
        float availH = MathF.Max(0f, vpHeight - s.Top - s.Bottom - edge * 2f);
        return (MathF.Min(availW, maxW), MathF.Min(availH, maxH));
    }

    /// <summary>
    /// Interior content width for a centered card that reflows rather than
    /// clips: the authored <paramref name="designInteriorW"/>, reduced to
    /// whatever the available box leaves once the panel's own
    /// <paramref name="chromeW"/> (stylebox content margins, both sides) is
    /// paid, floored at <paramref name="minInteriorW"/> so a degenerate
    /// viewport can't produce a zero/negative width.
    /// </summary>
    public static float CardInteriorWidth(
        float designInteriorW, float availW, float chromeW, float minInteriorW = 200f)
    {
        return MathF.Max(minInteriorW, MathF.Min(designInteriorW, availW - chromeW));
    }

    /// <summary>
    /// Shrink-only content scale for a panel whose content is rebuilt at the
    /// returned scale (fonts / row heights / separations multiplied through),
    /// rather than transform-scaled. <paramref name="measuredH"/> is the
    /// content's measured minimum height at the scale it was last built at
    /// (<paramref name="measuredAtScale"/>); dividing recovers the scale-1
    /// design height so repeated measure→rebuild passes converge instead of
    /// compounding. Returns 1 when the design fits (never upscales); otherwise
    /// the fit ratio floor-quantized to <paramref name="step"/> (stability
    /// across re-measurement and drag-resize) and clamped to
    /// <paramref name="minScale"/> (legibility floor). Growing back toward 1
    /// requires the design to fit with a <paramref name="growMargin"/> of
    /// headroom: scaled content measures slightly smaller than
    /// scale × design (per-metric flooring), so an on-boundary recovered
    /// design would otherwise oscillate grow→doesn't-fit→shrink forever.
    /// Inside the margin band the built scale holds.
    /// </summary>
    public static float ContentShrinkScale(
        float measuredH, float measuredAtScale, float availH,
        float step = 0.05f, float minScale = 0.65f, float growMargin = 1.02f)
    {
        if (measuredH <= 0f) return 1f;
        float designH = measuredAtScale > 0f ? measuredH / measuredAtScale : measuredH;
        if (designH <= availH)
        {
            if (measuredAtScale >= 1f || designH * growMargin <= availH) return 1f;
            return measuredAtScale;
        }
        // Epsilon before flooring so an exactly-on-boundary ratio (e.g. 0.85)
        // doesn't float-floor to the next quantum down.
        float quantized = MathF.Floor((availH / designH + 1e-4f) / step) * step;
        return MathF.Max(minScale, quantized);
    }
}
