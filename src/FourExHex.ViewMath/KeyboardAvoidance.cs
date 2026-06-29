/// <summary>
/// Pure math for shifting a UI panel up while the mobile on-screen keyboard
/// is visible, so a focused text field stays readable above it (the
/// main-menu Map Seed field). Kept Godot-free in FourExHex.ViewMath so
/// the math is unit-testable; called from <c>scripts/MainMenuScene.cs</c>.
/// All values are logical pixels (physical keyboard height ÷ ContentScaleFactor).
/// </summary>
public static class KeyboardAvoidance
{
    /// <summary>
    /// How many logical px to lift the panel so <paramref name="fieldBottomY"/>
    /// (the field's global bottom edge) clears the keyboard top with
    /// <paramref name="margin"/> spare. 0 when no keyboard is showing or the
    /// field is already fully visible.
    /// </summary>
    public static float LiftFor(
        float fieldBottomY, float viewportHeight,
        float keyboardLogicalHeight, float margin)
    {
        if (keyboardLogicalHeight <= 0f) return 0f;
        float keyboardTopY = viewportHeight - keyboardLogicalHeight;
        return System.Math.Max(0f, fieldBottomY + margin - keyboardTopY);
    }
}
