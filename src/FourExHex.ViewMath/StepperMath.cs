// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Text;

/// <summary>
/// Pure value logic for the numeric −/value/+ stepper rows (<c>UiStepper</c>):
/// snap-to-step clamping, neighbour selection, and digit parsing of typed text.
/// Godot-free (plain ints) so it's unit-testable; the view (<c>UiStepper</c>)
/// pulls min/max/step from its <c>LineEdit</c> metadata and delegates the
/// arithmetic here.
/// </summary>
public static class StepperMath
{
    /// <summary>
    /// Snap <paramref name="value"/> to the nearest multiple of
    /// <paramref name="step"/> and clamp into <c>[min, max]</c>. A
    /// <paramref name="step"/> of 0 disables snapping (value only clamps).
    /// Negatives are floored to 0 first.
    /// </summary>
    public static int Clamp(int value, int min, int max, int step)
    {
        if (value < 0) value = 0;
        int snapped = step > 0 ? ((value + step / 2) / step) * step : value;
        return Math.Clamp(snapped, min, max);
    }

    /// <summary>
    /// The value one step in direction <paramref name="dir"/> (−1 down, +1 up):
    /// <c>cur + dir·step</c>. Not bounded here — the caller re-<see cref="Clamp"/>s.
    /// </summary>
    public static int Neighbor(int cur, int dir, int step) => cur + dir * step;

    /// <summary>
    /// Pull the digits out of arbitrary typed text ("12", "12%", "x12" → 12).
    /// Empty / no-digit input → 0; an overflowing run of digits → int.MaxValue
    /// so it clamps to max.
    /// </summary>
    public static int ParseDigits(string text)
    {
        var sb = new StringBuilder();
        foreach (char c in text)
        {
            if (char.IsDigit(c)) sb.Append(c);
        }
        if (sb.Length == 0) return 0;
        return int.TryParse(sb.ToString(), out int v) ? v : int.MaxValue;
    }
}
