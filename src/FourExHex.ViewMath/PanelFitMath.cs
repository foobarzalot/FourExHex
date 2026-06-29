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
}
