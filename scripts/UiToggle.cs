// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using Godot;

/// <summary>
/// Shared boolean-toggle widgets. A stock Godot <see cref="CheckBox"/> bundles
/// its indicator icon and caption into one control whose per-state content
/// margins shift the caption on hover/toggle, and whose indicator glyph reads
/// tiny next to large UI fonts. These helpers instead pair a fixed-size square
/// toggle <see cref="Button"/> (gold with a dark ✓ when on, dark with a light
/// border when off) with a separate caption <see cref="Label"/>, so nothing
/// shifts and the box reads at a deliberate size. Shared by the settings
/// screen, the New Game map-setup page, and the map editor so they all
/// share one look.
/// </summary>
public static class UiToggle
{
    /// <summary>
    /// A fixed-size square toggle button. Wires Toggled → restyle + callback and
    /// applies the initial style; the caller positions it.
    /// </summary>
    public static Button BuildToggleBox(bool initial, Action<bool> onToggled, float size = 32f)
    {
        var box = new Button
        {
            ToggleMode = true,
            ButtonPressed = initial,
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(size, size),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        box.AddThemeFontSizeOverride("font_size", 22);
        box.Toggled += pressed =>
        {
            ApplyStyle(box, pressed);
            onToggled(pressed);
        };
        AudioBus.AttachClick(box);
        ApplyStyle(box, initial);
        return box;
    }

    /// <summary>
    /// One boolean-setting row: a left-aligned caption Label that fills the row
    /// plus a fixed-size square toggle box on the right. Splitting caption and
    /// box into two sibling controls (rather than a stock CheckBox) means the
    /// caption can never shift on hover/toggle. Hands back the box via
    /// <paramref name="box"/> so callers can re-sync its pressed state.
    /// </summary>
    public static HBoxContainer BuildCheckRow(
        string label,
        bool initial,
        Action<bool> onToggled,
        out Button box,
        int captionFontSize = 24,
        Color? captionColor = null)
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

        box = BuildToggleBox(initial, onToggled);
        row.AddChild(box);
        return row;
    }

    /// <summary>
    /// Repaint the square toggle: gold filled with a dark check when on, dark
    /// with a light border when off. Hover only brightens the border/fill —
    /// because the box holds nothing but a centered glyph, no caption shifts.
    /// </summary>
    public static void ApplyStyle(Button box, bool pressed)
    {
        box.Text = pressed ? "✓" : "";

        StyleBoxFlat Build(Color bg, Color border) => new StyleBoxFlat
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

        StyleBoxFlat normal = pressed
            ? Build(UiPalette.Gold, UiPalette.GoldDeep)
            : Build(UiPalette.BgElev, UiPalette.LineHard);
        StyleBoxFlat hover = pressed
            ? Build(UiPalette.Gold, UiPalette.Ink)
            : Build(UiPalette.BgElev, UiPalette.Ink);

        box.AddThemeStyleboxOverride("normal", normal);
        box.AddThemeStyleboxOverride("pressed", normal);
        box.AddThemeStyleboxOverride("hover", hover);
        box.AddThemeStyleboxOverride("hover_pressed", hover);

        Color tick = pressed ? UiPalette.BgDeep : UiPalette.InkSoft;
        box.AddThemeColorOverride("font_color", tick);
        box.AddThemeColorOverride("font_hover_color", tick);
        box.AddThemeColorOverride("font_pressed_color", tick);
        box.AddThemeColorOverride("font_hover_pressed_color", tick);
    }
}
