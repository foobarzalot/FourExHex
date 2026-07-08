using System;
using Godot;

/// <summary>
/// Shared numeric −/value/+ stepper widgets. Godot's stock
/// <see cref="SpinBox"/> bundles tiny up/down arrows and a fussy default look that
/// clashes with the large gold/dark UI; instead this pairs two fixed-size square
/// step buttons with an editable <see cref="LineEdit"/> showing the value as a
/// percent, matching the theme of <see cref="UiToggle"/>. The value is snapped to a
/// multiple of <c>step</c> and clamped to <c>[min, max]</c> on every commit (a step
/// button, Enter, or focus loss), so typed input can never escape the range. Built
/// for the Map Generation density rows but kept generic.
///
/// The committed value lives in the field's metadata (not a captured local), so
/// re-syncing from the model (<see cref="Resync"/>) and user edits stay consistent.
/// </summary>
public static class UiStepper
{
    /// <summary>
    /// One numeric-setting row: a left-aligned caption that fills the row, then a
    /// [−] button, an editable value field (shown as "N%"), and a [+] button. The
    /// value field is handed back via <paramref name="field"/> so callers can
    /// re-sync it from their model on open (see <see cref="Resync"/>).
    ///
    /// Linear mode: values snap to a multiple of <paramref name="step"/> within
    /// <c>[min, max]</c>, and [−]/[+] move by one <paramref name="step"/>.
    /// </summary>
    public static HBoxContainer BuildStepperRow(
        string label,
        int initial,
        int min,
        int max,
        int step,
        Action<int> onChanged,
        out LineEdit field,
        int captionFontSize = 24,
        Color? captionColor = null)
        => BuildRow(label, initial, min, max, step,
            onChanged, out field, captionFontSize, captionColor);

    private static HBoxContainer BuildRow(
        string label,
        int initial,
        int min,
        int max,
        int step,
        Action<int> onChanged,
        out LineEdit field,
        int captionFontSize,
        Color? captionColor)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);

        var caption = new Label
        {
            Text = label,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
        };
        caption.AddThemeFontSizeOverride("font_size", captionFontSize);
        caption.AddThemeColorOverride("font_color", captionColor ?? UiPalette.InkSoft);
        row.AddChild(caption);

        LineEdit valueField = BuildValueField();
        valueField.SetMeta("min", min);
        valueField.SetMeta("max", max);
        valueField.SetMeta("step", step);
        field = valueField;

        void Commit(int raw) => CommitValue(valueField, raw, onChanged);

        Button minus = BuildStepButton("−", () => Commit(Neighbor(valueField, -1)));
        Button plus = BuildStepButton("+", () => Commit(Neighbor(valueField, +1)));

        // Commit on Enter (release focus so the new text "sticks") and on focus loss
        // so a typed-then-tapped-away value is honored, not silently discarded.
        valueField.TextSubmitted += text => { Commit(StepperMath.ParseDigits(text)); valueField.ReleaseFocus(); };
        valueField.FocusExited += () => Commit(StepperMath.ParseDigits(valueField.Text));

        row.AddChild(minus);
        row.AddChild(valueField);
        row.AddChild(plus);

        // Seed the initial value + text without firing the callback (not a user edit).
        SetValue(valueField, initial);
        return row;
    }

    /// <summary>Re-sync the field to a model value without firing the change
    /// callback (mirrors how <see cref="UiToggle"/> re-syncs its box on open).</summary>
    public static void Resync(LineEdit field, int value) => SetValue(field, value);

    /// <summary>The field's current committed value (held in metadata).</summary>
    public static int CurrentValue(LineEdit field) => (int)field.GetMeta("value", 0);

    private static void CommitValue(LineEdit field, int raw, Action<int> onChanged)
    {
        int prev = CurrentValue(field);
        int next = Clamp(field, raw);
        SetValue(field, next);
        Log.Debug(Log.LogCategory.Hud,
            $"UiStepper commit: raw={raw} -> {next} (prev={prev}{(next != prev ? ", changed" : "")})");
        if (next != prev) onChanged(next);
    }

    private static void SetValue(LineEdit field, int value)
    {
        int clamped = Clamp(field, value);
        field.SetMeta("value", clamped);
        field.Text = $"{clamped}%";
    }

    // Snap + clamp a value against this row's config — the math lives in the pure,
    // unit-tested StepperMath; this only pulls min/max/step from metadata.
    private static int Clamp(LineEdit field, int value) =>
        StepperMath.Clamp(value, (int)field.GetMeta("min", 0), (int)field.GetMeta("max", 100),
            (int)field.GetMeta("step", 1));

    // The committed value moved one step in direction dir (−1 down, +1 up).
    // Clamp() bounds the result on commit.
    private static int Neighbor(LineEdit field, int dir) =>
        StepperMath.Neighbor(CurrentValue(field), dir, (int)field.GetMeta("step", 1));

    private static LineEdit BuildValueField()
    {
        var field = new LineEdit
        {
            Alignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(72, 36),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            FocusMode = Control.FocusModeEnum.All,
            ContextMenuEnabled = false,
            MaxLength = 4,
        };
        field.AddThemeFontSizeOverride("font_size", 22);
        field.AddThemeColorOverride("font_color", UiPalette.Ink);
        field.AddThemeColorOverride("caret_color", UiPalette.Gold);

        StyleBoxFlat Box(Color border) => new StyleBoxFlat
        {
            BgColor = UiPalette.BgDeep,
            BorderColor = border,
            BorderWidthLeft = 2,
            BorderWidthRight = 2,
            BorderWidthTop = 2,
            BorderWidthBottom = 2,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
            ContentMarginLeft = 6,
            ContentMarginRight = 6,
        };
        field.AddThemeStyleboxOverride("normal", Box(UiPalette.LineHard));
        field.AddThemeStyleboxOverride("focus", Box(UiPalette.Gold));
        return field;
    }

    // A fixed-size square step button: dark fill, light border, gold border on hover
    // — same visual family as UiToggle's box but with a persistent +/− glyph.
    private static Button BuildStepButton(string glyph, Action onPressed, float size = 36f)
    {
        var button = new Button
        {
            Text = glyph,
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(size, size),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        button.AddThemeFontSizeOverride("font_size", 24);

        StyleBoxFlat Box(Color bg, Color border) => new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = border,
            BorderWidthLeft = 2,
            BorderWidthRight = 2,
            BorderWidthTop = 2,
            BorderWidthBottom = 2,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
        };
        StyleBoxFlat normal = Box(UiPalette.BgElev, UiPalette.LineHard);
        StyleBoxFlat hover = Box(UiPalette.BgElev, UiPalette.Gold);
        button.AddThemeStyleboxOverride("normal", normal);
        button.AddThemeStyleboxOverride("pressed", hover);
        button.AddThemeStyleboxOverride("hover", hover);
        button.AddThemeColorOverride("font_color", UiPalette.InkSoft);
        button.AddThemeColorOverride("font_hover_color", UiPalette.Ink);
        button.AddThemeColorOverride("font_pressed_color", UiPalette.Ink);

        button.Pressed += () => onPressed();
        AudioBus.AttachClick(button);
        return button;
    }
}
