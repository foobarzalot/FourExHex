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
    /// Separation between the top-level children of a HUD corner zone
    /// (<c>HudBars.MakeCornerZone</c>) — looser than <see cref="GutterPx"/> so
    /// the chrome buttons read as separate affordances rather than one
    /// cluster. Feeds <see cref="HudCornerLayout"/>'s width budget.
    /// </summary>
    public const float CornerZoneSeparationPx = 10f;

    /// <summary>
    /// Distance a HUD corner zone keeps from its viewport edge, per side.
    /// Corner zones take no horizontal safe-area inset (rails take it
    /// instead), so this is the whole horizontal margin.
    /// </summary>
    public const float CornerZoneEdgePadPx = 10f;

    /// <summary>Per-side content padding inside a HUD readout chip (the
    /// status and gold pills) — <c>ContentMarginLeft/Right</c> of the chip
    /// stylebox.</summary>
    public const float ChipPaddingPx = 12f;

    /// <summary>Edge of the current player's enlarged swatch in the
    /// turn-order bar; also the compact bar's whole width, since compact
    /// draws the active swatch alone.</summary>
    public const float CurrentSwatchSizePx = 38f;

    /// <summary>Width floor of the status chip's mono turn-number label —
    /// wide enough that a three-digit turn never reflows the chip.</summary>
    public const float TurnLabelMinWidthPx = 70f;

    /// <summary>Width floor of the gold chip's mono readout (total + income
    /// breakdown). The widest thing in the top-left corner, so it sets the
    /// corner-zone width budget.</summary>
    public const float GoldLabelMinWidthPx = 220f;

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

    /// <summary>
    /// Half-period of the call-to-action pulse, in seconds per leg (sine,
    /// dim→bright). The HUD's CTA button pulse, the board's select cue, and
    /// the HUD-tour ring all breathe at this cadence so simultaneous cues
    /// read as one system.
    /// </summary>
    public const float CtaPulseHalfPeriodSec = 0.55f;
}
