// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using Godot;

/// <summary>
/// Shared builders for CanvasLayer-based modal dialogs (Settings, Credits,
/// the ESC menu, the save/map/tutorial slot picker). Gives every modal the
/// same dim-backdrop + centered slate-panel shell so chrome changes land in
/// one place and new modals don't drift from the established look.
/// </summary>
public static class ModalChrome
{
    private static readonly Font SerifFont =
        GD.Load<FontFile>("res://fonts/DMSerifDisplay-Regular.ttf");

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

    /// <summary>
    /// Build the shared "save/load failed" error overlay — a dim backdrop plus
    /// a fixed-size centered panel holding a left-aligned title, a word-wrapped
    /// body label, and a bottom-right OK button (wired to
    /// <paramref name="onOk"/>). Returns the pieces so the caller can stash the
    /// references it needs and add the backdrop + panel to its own tree (both
    /// start hidden). Shared by SlotPickerDialog and SaveNameModal.
    /// </summary>
    public static (ColorRect backdrop, PanelContainer panel, Label title, Label body) BuildErrorOverlay(
        Vector2 viewport, float panelW, float panelH, string titleText, System.Action onOk)
    {
        ColorRect backdrop = BuildBackdrop(viewport);
        backdrop.Visible = false;

        PanelContainer panel = BuildCenteredPanel(panelW: panelW, panelH: panelH);
        panel.Visible = false;

        var vbox = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        vbox.AddThemeConstantOverride("separation", 14);
        panel.AddChild(vbox);

        var title = new Label
        {
            Text = titleText,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        title.AddThemeFontSizeOverride("font_size", 22);
        title.AddThemeColorOverride("font_color", UiPalette.InkSoft);
        vbox.AddChild(title);

        var body = new Label
        {
            Text = "",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        body.AddThemeFontSizeOverride("font_size", 16);
        vbox.AddChild(body);

        var actions = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            Alignment = BoxContainer.AlignmentMode.End,
        };
        var okButton = new Button { Text = Strings.Get(StringKeys.ButtonOk) };
        okButton.AddThemeFontSizeOverride("font_size", 16);
        okButton.Pressed += onOk;
        AudioBus.AttachClick(okButton);
        actions.AddChild(okButton);
        vbox.AddChild(actions);

        return (backdrop, panel, title, body);
    }

    // Large centered serif title with a decorative gold rule beneath — the
    // title block shared by the save/load modal family.
    public static Control BuildSerifTitle(string title)
    {
        var head = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        head.AddThemeConstantOverride("separation", 18);

        var titleLabel = new Label
        {
            Text = title,
            HorizontalAlignment = HorizontalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        titleLabel.AddThemeFontOverride("font", SerifFont);
        titleLabel.AddThemeFontSizeOverride("font_size", 36);
        head.AddChild(titleLabel);
        head.AddChild(GoldRule());

        return head;
    }

    // Decorative gold rule under a panel title — the shared divider used by
    // the menu/modal panel family.
    public static ColorRect GoldRule() => new ColorRect
    {
        Color = UiPalette.GoldDim,
        CustomMinimumSize = new Vector2(200, 1),
        SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
    };

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
