using System;
using Godot;

/// <summary>
/// Reusable confirm / cancel modal in the ModalChrome dialog family
/// (dim backdrop + centered slate panel, serif title, gold rule) so it
/// matches Settings / Credits / the slot picker rather than Godot's
/// unstyled <see cref="ConfirmationDialog"/>. Escape or the Cancel button
/// dismisses and raises <see cref="Canceled"/>; the confirm button raises
/// <see cref="Confirmed"/>. Title/message/confirm-label are supplied by
/// the caller so the same shell serves any yes/no prompt.
/// </summary>
public sealed partial class ConfirmModal : CanvasLayer
{
    public event Action? Confirmed;
    public event Action? Canceled;

    public bool IsOpen { get; private set; }

    private readonly string _title;
    private readonly string _message;
    private readonly string _confirmText;

    private static readonly Font _serifFont =
        GD.Load<FontFile>("res://fonts/DMSerifDisplay-Regular.ttf");

    public ConfirmModal(string title, string message, string confirmText)
    {
        _title = title;
        _message = message;
        _confirmText = confirmText;
    }

    public override void _Ready()
    {
        // Same layer as the rest of the modal family; drawn above the
        // plain-Control landing panel (layer 0).
        Layer = 100;
        Visible = false;
        ProcessMode = ProcessModeEnum.Always;

        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        AddChild(ModalChrome.BuildBackdrop(viewport));

        PanelContainer panel = ModalChrome.BuildCenteredPanel();
        AddChild(panel);

        var vbox = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(420, 0),
        };
        vbox.AddThemeConstantOverride("separation", 18);
        panel.AddChild(vbox);

        var title = new Label
        {
            Text = _title,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontOverride("font", _serifFont);
        title.AddThemeFontSizeOverride("font_size", 36);
        vbox.AddChild(title);

        // Decorative gold rule under the title — matches the menu panels.
        var goldRule = new ColorRect
        {
            Color = UiPalette.GoldDim,
            CustomMinimumSize = new Vector2(200, 1),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        };
        vbox.AddChild(goldRule);

        var body = new Label
        {
            Text = _message,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        body.AddThemeFontSizeOverride("font_size", 22);
        body.AddThemeColorOverride("font_color", UiPalette.InkSoft);
        vbox.AddChild(body);

        var buttonRow = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        buttonRow.AddThemeConstantOverride("separation", 12);
        vbox.AddChild(buttonRow);

        var cancelButton = new Button
        {
            Text = "Cancel",
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        cancelButton.AddThemeFontSizeOverride("font_size", 24);
        cancelButton.Pressed += Cancel;
        AudioBus.AttachClick(cancelButton);
        buttonRow.AddChild(cancelButton);

        var confirmButton = new Button
        {
            Text = _confirmText,
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        confirmButton.AddThemeFontSizeOverride("font_size", 24);
        confirmButton.Pressed += Confirm;
        AudioBus.AttachClick(confirmButton);
        buttonRow.AddChild(confirmButton);
    }

    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        Visible = true;
        Log.Debug(Log.LogCategory.Input, "ConfirmModal.Open");
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        Visible = false;
        Log.Debug(Log.LogCategory.Input, "ConfirmModal.Close");
    }

    private void Confirm()
    {
        Close();
        Confirmed?.Invoke();
    }

    private void Cancel()
    {
        Close();
        Canceled?.Invoke();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!IsOpen) return;
        if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo) return;
        if (keyEvent.Keycode == Key.Escape)
        {
            Cancel();
            GetViewport().SetInputAsHandled();
        }
        else if (keyEvent.Keycode == Key.Enter || keyEvent.Keycode == Key.KpEnter)
        {
            Confirm();
            GetViewport().SetInputAsHandled();
        }
    }
}
