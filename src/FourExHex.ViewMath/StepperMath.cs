using System;
using System.Text;

/// <summary>
/// Pure value logic for the numeric −/value/+ stepper rows (<c>UiStepper</c>):
/// snap-to-step / nearest-explicit-stop clamping, neighbour-stop selection, and
/// digit parsing of typed text. Godot-free (plain ints + an optional
/// <c>int[]</c> stops list) so it's unit-testable; the view (<c>UiStepper</c>)
/// pulls min/max/step/stops from its <c>LineEdit</c> metadata and delegates the
/// arithmetic here. Mirrors the former inline logic byte-for-byte.
/// </summary>
public static class StepperMath
{
    /// <summary>
    /// Snap <paramref name="value"/> to a legal value and clamp it: the nearest
    /// explicit stop when <paramref name="stops"/> is non-null (ties go to the
    /// lower stop; stops must be ascending), otherwise the nearest multiple of
    /// <paramref name="step"/> clamped into <c>[min, max]</c>. A
    /// <paramref name="step"/> of 0 disables snapping (value only clamps).
    /// Negatives are floored to 0 first.
    /// </summary>
    public static int Clamp(int value, int min, int max, int step, int[]? stops)
    {
        if (value < 0) value = 0;
        if (stops != null) return stops[NearestStopIndex(stops, value)];
        int snapped = step > 0 ? ((value + step / 2) / step) * step : value;
        return Math.Clamp(snapped, min, max);
    }

    /// <summary>Index of the stop closest to <paramref name="value"/>; ties go
    /// to the lower stop. Stops are ascending.</summary>
    public static int NearestStopIndex(int[] stops, int value)
    {
        int best = 0;
        for (int i = 1; i < stops.Length; i++)
        {
            if (Math.Abs(stops[i] - value) < Math.Abs(stops[best] - value)) best = i;
        }
        return best;
    }

    /// <summary>
    /// The value one step in direction <paramref name="dir"/> (−1 down, +1 up):
    /// the adjacent stop for a stops row (clamped at the ends), else
    /// <c>cur + dir·step</c>. Linear results are not bounded here — the caller
    /// re-<see cref="Clamp"/>s.
    /// </summary>
    public static int Neighbor(int cur, int dir, int step, int[]? stops)
    {
        if (stops == null) return cur + dir * step;
        int idx = NearestStopIndex(stops, cur);
        return stops[Math.Clamp(idx + dir, 0, stops.Length - 1)];
    }

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
