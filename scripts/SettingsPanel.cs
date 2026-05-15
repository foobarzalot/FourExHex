using System;
using Godot;

/// <summary>
/// Reusable Settings modal — backdrop + centered panel with SFX/VFX
/// toggles and a Back button. Used by the main menu and by the in-game
/// pause menu (where it sits on top of a paused scene tree, hence
/// <see cref="ProcessMode"/> = WhenPaused so the toggles still respond
/// while <c>GetTree().Paused == true</c>).
///
/// Open()/Close() control visibility; the panel fires
/// <see cref="Closed"/> on every close (Back button or Escape) so the
/// host can react (e.g., the pause coordinator re-shows the pause
/// menu when settings closes).
/// </summary>
public sealed partial class SettingsPanel : CanvasLayer
{
    public event Action? Closed;

    public bool IsOpen { get; private set; }

    private ColorRect _backdrop = null!;
    private PanelContainer _panel = null!;
    private CheckBox _sfxCheckBox = null!;
    private CheckBox _vfxCheckBox = null!;

    public override void _Ready()
    {
        Layer = 100;
        Visible = false;
        // Always — works in both the unpaused main menu and the paused
        // in-game pause flow. See EscMenu for the same reasoning.
        ProcessMode = ProcessModeEnum.Always;

        Vector2 viewport = GetViewport().GetVisibleRect().Size;

        _backdrop = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.5f),
            Position = Vector2.Zero,
            Size = viewport,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        AddChild(_backdrop);

        _panel = new PanelContainer
        {
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            AnchorTop = 0.5f,
            AnchorBottom = 0.5f,
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.Both,
        };
        var panelStyle = new StyleBoxFlat
        {
            BgColor = new Color(0f, 0f, 0f, 0.85f),
            ContentMarginLeft = 24,
            ContentMarginRight = 24,
            ContentMarginTop = 20,
            ContentMarginBottom = 20,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
        };
        _panel.AddThemeStyleboxOverride("panel", panelStyle);
        AddChild(_panel);

        var vbox = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(320, 0),
        };
        vbox.AddThemeConstantOverride("separation", 16);
        _panel.AddChild(vbox);

        var title = new Label
        {
            Text = "Settings",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", 28);
        vbox.AddChild(title);

        _sfxCheckBox = new CheckBox
        {
            Text = "Sound Effects",
            ButtonPressed = UserSettings.SfxEnabled,
        };
        _sfxCheckBox.AddThemeFontSizeOverride("font_size", 22);
        _sfxCheckBox.Toggled += OnSfxToggled;
        AudioBus.AttachClick(_sfxCheckBox);
        vbox.AddChild(_sfxCheckBox);

        _vfxCheckBox = new CheckBox
        {
            Text = "Visual Effects",
            ButtonPressed = UserSettings.VfxEnabled,
        };
        _vfxCheckBox.AddThemeFontSizeOverride("font_size", 22);
        _vfxCheckBox.Toggled += OnVfxToggled;
        AudioBus.AttachClick(_vfxCheckBox);
        vbox.AddChild(_vfxCheckBox);

        var backButton = new Button
        {
            Text = "Back",
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        backButton.AddThemeFontSizeOverride("font_size", 22);
        backButton.Pressed += Close;
        AudioBus.AttachClick(backButton);
        vbox.AddChild(backButton);
    }

    /// <summary>Show the panel. Re-syncs toggles from
    /// <see cref="UserSettings"/> so external changes are reflected.</summary>
    public void Open()
    {
        if (IsOpen) return;
        _sfxCheckBox.ButtonPressed = UserSettings.SfxEnabled;
        _vfxCheckBox.ButtonPressed = UserSettings.VfxEnabled;
        IsOpen = true;
        Visible = true;
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        Visible = false;
        Closed?.Invoke();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!IsOpen) return;
        if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo) return;
        if (keyEvent.Keycode != Key.Escape) return;
        Close();
        GetViewport().SetInputAsHandled();
    }

    private void OnSfxToggled(bool pressed)
    {
        UserSettings.SfxEnabled = pressed;
    }

    private void OnVfxToggled(bool pressed)
    {
        UserSettings.VfxEnabled = pressed;
    }
}
