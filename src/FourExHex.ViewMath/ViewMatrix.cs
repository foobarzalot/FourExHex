// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;

/// <summary>
/// One cell of the view-layer sweep: a window geometry the harness drives the
/// live scenes through.
///
/// Sizes are <b>logical</b> px — the space <c>ScreenLayout</c>, every panel, and
/// <c>Control.GetGlobalRect()</c> work in. The physical window is
/// logical × <see cref="UiScale"/>, and <see cref="UiScale"/> is always pinned
/// (never inferred from monitor DPI) so the same cell lands on the same side of
/// the compact thresholds on a retina Mac, an xvfb virtual screen, and a 4K
/// desktop alike.
/// </summary>
/// <param name="SoftFail">Violations here are reported but excluded from the
/// run's verdict — for geometry that is known-broken today. Promoted to
/// hard-fail once the underlying layout is fixed.</param>
public readonly record struct ViewMatrixCell(
    string Name,
    int LogicalWidth,
    int LogicalHeight,
    float UiScale,
    LogicalSafeInsets Insets,
    bool SoftFail,
    string Reason)
{
    public int PhysicalWidth => (int)(LogicalWidth * UiScale);
    public int PhysicalHeight => (int)(LogicalHeight * UiScale);
}

/// <summary>
/// The sweep's cell table. Each entry exists to reach a specific branch in
/// <see cref="ScreenLayout"/> / <see cref="PanelFitMath"/> / <see cref="SafeAreaMath"/>;
/// <c>tests/ViewMatrixTests.cs</c> asserts that walking the table actually does
/// reach them, so the justification is executable rather than a comment.
/// </summary>
public static class ViewMatrix
{
    private static ViewMatrixCell Cell(
        string name, int w, int h, string reason,
        float scale = 1f, LogicalSafeInsets insets = default, bool softFail = false) =>
        new(name, w, h, scale, insets, softFail, reason);

    public static IReadOnlyList<ViewMatrixCell> Default { get; } = new List<ViewMatrixCell>
    {
        Cell("desktop-default", 1600, 1080,
            "project.godot's viewport size — landscape, expanded"),

        // Cells 2-6 walk the compact hysteresis. Order is load-bearing: the two
        // 700-tall cells are identical geometry reached from opposite prior
        // states, which is the whole reason the sweep resizes in-process
        // instead of cold-starting per cell.
        Cell("expanded-baseline", 1280, 800,
            "min side 800 >= 732 — expanded, the starting state for the walk"),
        Cell("deadband-hold-expanded", 1280, 700,
            "min side 700 inside the 668..732 band, arriving expanded — must hold expanded"),
        Cell("compact-flip", 1280, 660,
            "min side 660 < 668 — flips to compact"),
        // Deliberately the same geometry as deadband-hold-expanded: identical
        // size, opposite prior state, opposite outcome. Odd heights are avoided
        // because a window manager may round them (1280x701 comes back as 700
        // and the cell reports UNACHIEVABLE).
        Cell("deadband-hold-compact", 1280, 700,
            "same size as deadband-hold-expanded but arriving compact — must hold compact"),
        Cell("expanded-flip-back", 1280, 760,
            "min side 760 > 732 — flips back to expanded"),

        Cell("square-tie", 800, 800,
            "w == h — ScreenLayout.Resolve must pick Landscape"),
        Cell("tablet-portrait", 820, 1180,
            "iPad Air points — expanded portrait"),
        Cell("landscape-short", 900, 360,
            "short landscape — presses ScaleToFit and ContentShrinkScale toward the 0.65 floor",
            softFail: true),
        Cell("narrow-portrait", 320, 900,
            "narrow — CardInteriorWidth hits its 200px floor and ClampWidth can invert",
            softFail: true),

        // Mobile-shaped geometry. Insets are synthetic but plausible; real
        // device capture is a separate concern.
        Cell("portrait-phone", 390, 844,
            "iPhone 14 points — compact portrait, no insets"),
        Cell("notch-portrait", 390, 844,
            "Dynamic Island + home indicator",
            insets: new LogicalSafeInsets(Top: 47f, Bottom: 34f, Left: 0f, Right: 0f)),
        Cell("notch-landscape", 844, 390,
            "notch rotated into landscape — the only shape with non-zero left/right insets",
            insets: new LogicalSafeInsets(Top: 0f, Bottom: 21f, Left: 47f, Right: 47f)),
        Cell("mobile-scale-portrait", 390, 844,
            "same logical geometry at the mobile ContentScaleFactor — exercises the scale "
            + "divide in SafeAreaMath; the cell most likely to be clamped on a laptop display",
            scale: DisplayScaleMath.MobileMinFactor,
            insets: new LogicalSafeInsets(47f, 34f, 0f, 0f)),
    };

    public static ViewMatrixCell ByName(string name)
    {
        foreach (ViewMatrixCell cell in Default)
        {
            if (cell.Name == name) return cell;
        }
        throw new KeyNotFoundException($"unknown view-matrix cell '{name}'");
    }

    /// <summary>Filter to a space-separated set of cell names, keeping matrix
    /// order (the hysteresis walk depends on it). Empty or null selects the
    /// whole matrix.</summary>
    public static IReadOnlyList<ViewMatrixCell> Parse(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return Default;

        var wanted = new HashSet<string>(spec.Split(
            new[] { ' ', ',' }, System.StringSplitOptions.RemoveEmptyEntries));

        var picked = new List<ViewMatrixCell>();
        foreach (ViewMatrixCell cell in Default)
        {
            if (wanted.Contains(cell.Name)) picked.Add(cell);
        }
        return picked;
    }

    /// <summary>The cells, then back down again — re-entering each size from the
    /// opposite direction, which is what makes a dead-band hold observable in
    /// both senses. The turn-around cell is not repeated.</summary>
    public static IReadOnlyList<ViewMatrixCell> WithReverseSweep(
        IReadOnlyList<ViewMatrixCell> cells)
    {
        var sweep = new List<ViewMatrixCell>(cells);
        for (int i = cells.Count - 2; i >= 0; i--) sweep.Add(cells[i]);
        return sweep;
    }
}
