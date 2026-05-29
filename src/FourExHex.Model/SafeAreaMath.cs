namespace FourExHex.Model;

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
}
