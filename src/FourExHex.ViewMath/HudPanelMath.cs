// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;

/// <summary>
/// Sizing math for the floating HUD panels (tutorial narration box,
/// bankruptcy toast, endgame overlays): clamp a fixed design width to the
/// viewport, and grow a bottom-anchored panel's height to fit its wrapped
/// text. Godot-free so it is unit-testable, mirroring <see cref="ScreenLayout"/>;
/// the view measures the actual wrapped text and passes the height in.
/// </summary>
public static class HudPanelMath
{
    /// <summary>Cap a centered panel's design width to the viewport, keeping
    /// <paramref name="sideMargin"/> clear on each side so a narrow (portrait
    /// phone) viewport shrinks the panel instead of clipping both edges.</summary>
    public static float ClampWidth(float designW, float viewportW, float sideMargin) =>
        MathF.Min(designW, viewportW - sideMargin * 2f);

    /// <summary>Panel height that fits <paramref name="wrappedTextH"/> of
    /// wrapped label text plus <paramref name="verticalInset"/> above and
    /// below it, never shrinking under the design height
    /// <paramref name="minH"/> (short messages keep the familiar box).</summary>
    public static float FitHeight(float wrappedTextH, float verticalInset, float minH) =>
        MathF.Max(minH, wrappedTextH + verticalInset * 2f);
}
