// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
/// <summary>
/// Logical-pixel safe-area insets for the four edges. Top is non-zero on
/// devices with a notch or Dynamic Island; bottom is non-zero on devices with
/// a home indicator; left/right are non-zero when the notch is rotated into a
/// landscape orientation. Desktop windows (no unsafe zones) report
/// <see cref="Zero"/>.
/// </summary>
public readonly record struct LogicalSafeInsets(float Top, float Bottom, float Left, float Right)
{
    public static LogicalSafeInsets Zero => new(0f, 0f, 0f, 0f);
}

/// <summary>
/// Pure math mapping a device's physical-pixel safe-area rect to logical-pixel
/// insets the HUD layout consumes. Kept Godot-free and in the model assembly so
/// the math is unit-testable; called from <c>scripts/SafeArea.cs</c>.
///
/// The safe area is supplied as the physical rect Godot reports from
/// <c>DisplayServer.GetDisplaySafeArea()</c>; the content scale factor is the
/// running <c>Window.ContentScaleFactor</c>. Insets are computed as the gap
/// between the safe rect and each window edge, divided by the scale factor.
/// </summary>
public static class SafeAreaMath
{
    /// <summary>
    /// Compute logical-pixel insets given the physical window dimensions and
    /// the physical safe-area rect. A <paramref name="contentScaleFactor"/> of
    /// 0 or less, or a zero-sized safe rect, returns <see cref="LogicalSafeInsets.Zero"/>
    /// (interpreted as "no unsafe zones known").
    /// </summary>
    public static LogicalSafeInsets InsetsFor(
        int physicalWindowWidth, int physicalWindowHeight,
        int physicalSafeX, int physicalSafeY,
        int physicalSafeWidth, int physicalSafeHeight,
        float contentScaleFactor)
    {
        if (contentScaleFactor <= 0f) return LogicalSafeInsets.Zero;
        if (physicalSafeWidth <= 0 || physicalSafeHeight <= 0) return LogicalSafeInsets.Zero;

        int topPhys = System.Math.Max(0, physicalSafeY);
        int leftPhys = System.Math.Max(0, physicalSafeX);
        int bottomPhys = System.Math.Max(0, physicalWindowHeight - (physicalSafeY + physicalSafeHeight));
        int rightPhys = System.Math.Max(0, physicalWindowWidth - (physicalSafeX + physicalSafeWidth));

        return new LogicalSafeInsets(
            Top: topPhys / contentScaleFactor,
            Bottom: bottomPhys / contentScaleFactor,
            Left: leftPhys / contentScaleFactor,
            Right: rightPhys / contentScaleFactor);
    }

    /// <summary>
    /// Parse a forced-insets string in already-logical pixels, as supplied by
    /// the <c>FOUREXHEX_SAFE_INSETS</c> env var. Desktop has no notch or home
    /// indicator and so reports no unsafe zones, which makes reviewing a
    /// phone's layout on the dev Mac misleading; this lets the reviewer
    /// supply the device's real insets (see RELEASE.md §6).
    ///
    /// Accepts <c>"top,bottom"</c> — the common phone case, matching the
    /// <c>insets=(t= b= l= r=)</c> log order — or the full
    /// <c>"top,bottom,left,right"</c>. Returns null for anything malformed or
    /// negative so the caller falls through to the real OS value rather than
    /// laying out against garbage. <c>"0,0"</c> is a deliberate zero, not a
    /// parse failure.
    /// </summary>
    public static LogicalSafeInsets? ParseOverride(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        string[] parts = raw.Split(',');
        if (parts.Length != 2 && parts.Length != 4) return null;

        var values = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!float.TryParse(
                    parts[i].Trim(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out float value))
            {
                return null;
            }
            if (value < 0f || float.IsNaN(value) || float.IsInfinity(value)) return null;
            values[i] = value;
        }

        return parts.Length == 2
            ? new LogicalSafeInsets(Top: values[0], Bottom: values[1], Left: 0f, Right: 0f)
            : new LogicalSafeInsets(
                Top: values[0], Bottom: values[1], Left: values[2], Right: values[3]);
    }
}
