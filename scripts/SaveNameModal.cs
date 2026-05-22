using System;
using Godot;

/// <summary>
/// Reusable "name this save" modal in the ModalChrome dialog family
/// (dim backdrop + centered slate panel, serif title, gold rule) so it
/// matches Settings / Credits / the slot picker rather than Godot's
/// unstyled <see cref="AcceptDialog"/>. The caller supplies a default
/// name to <see cref="Open"/>; pressing Save or Enter raises
/// <see cref="Confirmed"/> with the entered text but does NOT close the
/// modal — the host runs the save and then either calls <see cref="Close"/>
/// (success) or <see cref="ShowError"/> (failure, e.g. a reserved slot
/// name) so the user can fix the name without retyping. Cancel / × /
/// Escape / backdrop-click close the modal and raise <see cref="Closed"/>.
/// </summary>
public sealed partial class SaveNameModal : CanvasLayer
{
    // Raised on Save / Enter with the raw (un-sanitized) text. The host
    // sanitizes + validates and decides whether to Close() or ShowError().
    public event Action<string>? Confirmed;
    // Raised whenever the modal fully closes (Cancel / × / Escape /
    // backdrop / a host Close() after a successful save).
    public event Action? Closed;

    public bool IsOpen { get; private set; }

    private LineEdit _lineEdit = null!;
    private ColorRect _backdrop = null!;

    private Label _errorBodyLabel = null!;
    private PanelContainer _errorPanel = null!;
    private ColorRect _errorBackdrop = null!;

    private static readonly Font _serifFont =
        GD.Load<FontFile>("res://fonts/DMSerifDisplay-Regular.ttf");

    public override void _Ready()
    {
        // Same layer as the rest of the modal family.
        Layer = 100;
        Visible = false;
        // Always — Save Game is reached from the pause menu where
        // GetTree().Paused is true; default Inherit would freeze the modal.
        ProcessMode = ProcessModeEnum.Always;

        Vector2 viewport = GetViewport().GetVisibleRect().Size;

        _backdrop = ModalChrome.BuildBackdrop(viewport);
        _backdrop.GuiInput += OnBackdropInput;
        AddChild(_backdrop);

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
            Text = "Save Game",
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

        var label = new Label
        {
            Text = "Slot name:",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        label.AddThemeFontSizeOverride("font_size", 22);
        label.AddThemeColorOverride("font_color", UiPalette.InkSoft);
        vbox.AddChild(label);

        _lineEdit = new LineEdit
        {
            CustomMinimumSize = new Vector2(0, 36),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _lineEdit.AddThemeFontSizeOverride("font_size", 22);
        _lineEdit.TextSubmitted += _ => Confirm();
        vbox.AddChild(_lineEdit);

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
        cancelButton.Pressed += Close;
        AudioBus.AttachClick(cancelButton);
        buttonRow.AddChild(cancelButton);

        var saveButton = new Button
        {
            Text = "Save",
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        saveButton.AddThemeFontSizeOverride("font_size", 24);
        saveButton.Pressed += Confirm;
        AudioBus.AttachClick(saveButton);
        buttonRow.AddChild(saveButton);

        BuildErrorOverlay(viewport);
    }

    /// <summary>
    /// Pop the modal with <paramref name="defaultName"/> pre-filled and
    /// selected so the user can overtype or accept it.
    /// </summary>
    public void Open(string defaultName)
    {
        if (IsOpen) return;
        IsOpen = true;
        Visible = true;
        HideError();
        _lineEdit.Text = defaultName;
        _lineEdit.GrabFocus();
        _lineEdit.SelectAll();
        Log.Debug(Log.LogCategory.Input, $"SaveNameModal.Open default='{defaultName}'");
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        Visible = false;
        HideError();
        Log.Debug(Log.LogCategory.Input, "SaveNameModal.Close");
        Closed?.Invoke();
    }

    /// <summary>
    /// Show an inline error over the modal (e.g. reserved slot name or a
    /// write failure). The modal stays open so the user can fix the name.
    /// </summary>
    public void ShowError(string message)
    {
        _errorBodyLabel.Text = message;
        _errorBackdrop.Visible = true;
        _errorPanel.Visible = true;
        Log.Debug(Log.LogCategory.Input, $"SaveNameModal.ShowError '{message}'");
    }

    private void Confirm()
    {
        Log.Debug(Log.LogCategory.Input, $"SaveNameModal.Confirm text='{_lineEdit.Text}'");
        Confirmed?.Invoke(_lineEdit.Text);
    }

    private void HideError()
    {
        _errorBackdrop.Visible = false;
        _errorPanel.Visible = false;
    }

    private void BuildErrorOverlay(Vector2 viewport)
    {
        _errorBackdrop = ModalChrome.BuildBackdrop(viewport);
        _errorBackdrop.Visible = false;
        AddChild(_errorBackdrop);

        _errorPanel = ModalChrome.BuildCenteredPanel(panelW: 420, panelH: 200);
        _errorPanel.Visible = false;
        AddChild(_errorPanel);

        var vbox = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        vbox.AddThemeConstantOverride("separation", 14);
        _errorPanel.AddChild(vbox);

        var errorTitle = new Label
        {
            Text = "Save failed",
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        errorTitle.AddThemeFontSizeOverride("font_size", 22);
        errorTitle.AddThemeColorOverride("font_color", UiPalette.InkSoft);
        vbox.AddChild(errorTitle);

        _errorBodyLabel = new Label
        {
            Text = "",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        _errorBodyLabel.AddThemeFontSizeOverride("font_size", 16);
        vbox.AddChild(_errorBodyLabel);

        var actions = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            Alignment = BoxContainer.AlignmentMode.End,
        };
        var okButton = new Button { Text = "OK" };
        okButton.AddThemeFontSizeOverride("font_size", 16);
        okButton.Pressed += HideError;
        AudioBus.AttachClick(okButton);
        actions.AddChild(okButton);
        vbox.AddChild(actions);
    }

    private void OnBackdropInput(InputEvent @event)
    {
        // Backdrop click closes the modal (modal contract) — but only
        // when no error overlay is stacked on top of it.
        if (_errorPanel.Visible) return;
        if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            _backdrop.AcceptEvent();
            Close();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!IsOpen) return;
        if (@event is not InputEventKey key || !key.Pressed || key.Echo) return;
        if (key.Keycode != Key.Escape) return;
        if (_errorPanel.Visible)
        {
            HideError();
        }
        else
        {
            Close();
        }
        GetViewport().SetInputAsHandled();
    }
}
