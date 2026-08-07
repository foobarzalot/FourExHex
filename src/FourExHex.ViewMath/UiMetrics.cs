// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
/// <summary>
/// Cross-cutting UI feel tokens shared by the view layer — the numbers that
/// must agree across otherwise-unrelated controls, each previously declared
/// independently at every use site. Sizes are logical px (pre-DPI-scale,
/// see <see cref="DisplayScaleMath"/>). Godot-free so the values are visible
/// to unit tests; `scripts/` consumes them directly.
/// </summary>
public static class UiMetrics
{
    /// <summary>
    /// Square touch-target edge for icon/palette buttons (HUD action
    /// buttons, editor paint buttons). One size everywhere so rails and
    /// bars computed from it (<see cref="EditorPaletteLayout"/>, HudBars)
    /// stay consistent with the buttons they hold.
    /// </summary>
    public const float TouchButtonSizePx = 68f;

    /// <summary>Standard gutter between adjacent buttons/controls in a
    /// cluster, and the edge pad of the HUD rails.</summary>
    public const float GutterPx = 8f;

    /// <summary>
    /// Margin a centered panel/sheet keeps from the viewport (or safe-area)
    /// edge on every side — the shared inset for modal panels, dialogs, and
    /// the HUD's clamped panels.
    /// </summary>
    public const float ViewportMarginPx = 24f;

    /// <summary>
    /// Press-and-hold duration that distinguishes a long-press from a tap,
    /// in milliseconds — the board's rally gesture and the HUD icon
    /// buttons' hold action share this threshold so the two gestures feel
    /// identical. Integer ms; consumers needing seconds divide by 1000.
    /// </summary>
    public const int LongPressMs = 400;
}
