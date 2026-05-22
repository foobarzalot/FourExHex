using System;
using Godot;

/// <summary>
/// Shared builders for CanvasLayer-based modal dialogs (Settings, Credits,
/// the ESC menu, the save/map/tutorial slot picker). Gives every modal the
/// same dim-backdrop + centered slate-panel shell so chrome changes land in
/// one place and new modals don't drift from the established look.
/// </summary>
public static class ModalChrome
{
    // Full-screen dim scrim. MouseFilter=Stop so clicks don't bleed through
    // to whatever is behind the modal.
    public static ColorRect BuildBackdrop(Vector2 viewport)
    {
        Log.Trace(Log.LogCategory.Render, "ModalChrome.BuildBackdrop");
        return new ColorRect
        {
            Color = UiPalette.ModalBackdrop,
            Position = Vector2.Zero,
            Size = viewport,
            AnchorLeft = 0f, AnchorRight = 1f, AnchorTop = 0f, AnchorBottom = 1f,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
    }

    // Centered panel at a fixed pixel size — for modals that pin their own
    // dimensions (the slot picker). Picks up the theme's slate Panel stylebox.
    public static PanelContainer BuildCenteredPanel(float panelW, float panelH)
    {
        return new PanelContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = -panelW * 0.5f, OffsetRight = panelW * 0.5f,
            OffsetTop = -panelH * 0.5f, OffsetBottom = panelH * 0.5f,
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.Both,
        };
    }

    // Centered panel that sizes to its content — for modals whose inner vbox
    // CustomMinimumSize drives the dimensions (Settings, Credits, ESC menu).
    public static PanelContainer BuildCenteredPanel()
    {
        return new PanelContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.Both,
        };
    }

    // Small uppercase title + close (×) button row, with a 1px line-soft
    // divider beneath. The redesign's "panel-head" pattern.
    public static Control BuildPanelHead(string title, Action onClose)
    {
        var head = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        head.AddThemeConstantOverride("separation", 10);

        var row = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        var titleLabel = new Label
        {
            Text = title.ToUpperInvariant(),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        titleLabel.AddThemeFontSizeOverride("font_size", 16);
        titleLabel.AddThemeColorOverride("font_color", UiPalette.InkSoft);
        row.AddChild(titleLabel);

        var closeButton = new Button
        {
            Text = "×",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(32, 32),
        };
        closeButton.AddThemeFontSizeOverride("font_size", 22);
        closeButton.Pressed += () => onClose();
        AudioBus.AttachClick(closeButton);
        row.AddChild(closeButton);
        head.AddChild(row);

        var divider = new ColorRect
        {
            Color = UiPalette.LineSoft,
            CustomMinimumSize = new Vector2(0, 1),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        head.AddChild(divider);

        return head;
    }

    // Rounded slate panel stylebox for the in-game / map-editor palette
    // groups (HudView, MapEditorHudView). Border 1, radius 10, snug margins.
    public static StyleBoxFlat PalettePanelStyle()
    {
        var style = new StyleBoxFlat
        {
            BgColor = UiPalette.BgDeep,
            BorderColor = UiPalette.LineSoft,
        };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(10);
        style.ContentMarginLeft = 6;
        style.ContentMarginRight = 6;
        style.ContentMarginTop = 2;
        style.ContentMarginBottom = 2;
        return style;
    }
}
