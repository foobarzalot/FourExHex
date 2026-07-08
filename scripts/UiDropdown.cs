using System;
using Godot;

/// <summary>
/// Shared caption + <see cref="OptionButton"/> setting row — the dropdown sibling
/// of <see cref="UiStepper"/>, for a setting whose useful values are a small named
/// set rather than a numeric range (e.g. the Map Generation "Territories" control).
/// Matches the theme of <see cref="UiStepper"/>'s value field (dark fill, light
/// border, gold on hover/focus) so a dropdown row sits flush with the stepper rows
/// in the same modal.
///
/// Each item's <b>id is its underlying value</b>, so the selection round-trips via
/// <see cref="OptionButton.GetSelectedId"/> independent of item order, and
/// <see cref="Resync"/> can re-select by value without firing the change callback.
/// </summary>
public static class UiDropdown
{
    /// <summary>
    /// One dropdown-setting row: a left-aligned caption that fills the row, then an
    /// <see cref="OptionButton"/> whose entries are <paramref name="items"/> (each a
    /// display label paired with its integer value/id). The dropdown is handed back
    /// via <paramref name="dropdown"/> so callers can re-sync it from their model on
    /// open (see <see cref="Resync"/>). <paramref name="onSelected"/> fires with the
    /// selected item's id whenever the user picks an entry.
    /// </summary>
    public static HBoxContainer BuildDropdownRow(
        string label,
        int initialId,
        (string label, int id)[] items,
        Action<int> onSelected,
        out OptionButton dropdown,
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

        dropdown = BuildDropdown(items, initialId, onSelected);
        row.AddChild(dropdown);
        return row;
    }

    /// <summary>Re-select the item whose id matches <paramref name="id"/> without
    /// firing the change callback (mirrors <see cref="UiStepper.Resync"/>).</summary>
    public static void Resync(OptionButton dropdown, int id) => SelectItemById(dropdown, id);

    private static OptionButton BuildDropdown(
        (string label, int id)[] items, int initialId, Action<int> onSelected)
    {
        var dropdown = new OptionButton
        {
            CustomMinimumSize = new Vector2(150, 36),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            FocusMode = Control.FocusModeEnum.None,
        };
        dropdown.AddThemeFontSizeOverride("font_size", 22);
        // The popup list is themed separately from the button; without this
        // override the expanded items render at the tiny default size.
        dropdown.GetPopup().AddThemeFontSizeOverride("font_size", 22);

        foreach ((string label, int id) in items)
        {
            dropdown.AddItem(label, id);
        }
        SelectItemById(dropdown, initialId);

        // Fire with the selected item's id, not its index — order-independent.
        dropdown.ItemSelected += _ => onSelected(dropdown.GetSelectedId());

        StyleDropdown(dropdown);
        AudioBus.AttachClick(dropdown);
        return dropdown;
    }

    /// <summary>Select the item whose id matches <paramref name="id"/>
    /// (<see cref="OptionButton.Selected"/> is an index, not an id).</summary>
    private static void SelectItemById(OptionButton dropdown, int id)
    {
        for (int item = 0; item < dropdown.ItemCount; item++)
        {
            if (dropdown.GetItemId(item) == id)
            {
                dropdown.Selected = item;
                return;
            }
        }
    }

    // Match UiStepper.BuildValueField: dark fill, 2px border (light normal, gold on
    // hover/focus), corner radius 4, so the dropdown reads as the same widget family.
    private static void StyleDropdown(OptionButton dropdown)
    {
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
            ContentMarginLeft = 8,
            ContentMarginRight = 8,
        };
        dropdown.AddThemeStyleboxOverride("normal", Box(UiPalette.LineHard));
        dropdown.AddThemeStyleboxOverride("hover", Box(UiPalette.Gold));
        dropdown.AddThemeStyleboxOverride("pressed", Box(UiPalette.Gold));
        dropdown.AddThemeStyleboxOverride("focus", Box(UiPalette.Gold));
        dropdown.AddThemeColorOverride("font_color", UiPalette.Ink);
        dropdown.AddThemeColorOverride("font_hover_color", UiPalette.Ink);
        dropdown.AddThemeColorOverride("font_pressed_color", UiPalette.Ink);
        dropdown.AddThemeColorOverride("font_focus_color", UiPalette.Ink);
    }
}
