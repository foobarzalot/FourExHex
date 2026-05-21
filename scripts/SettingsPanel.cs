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

    // Display order for both speed radio rows (AI Turn Speed and Replay
    // Speed are independent settings but share the preset list). Open()
    // re-syncs each row's pressed state from UserSettings against this.
    private static readonly PlaybackSpeed[] SpeedOrder =
        { PlaybackSpeed.Slow, PlaybackSpeed.Normal, PlaybackSpeed.Fast, PlaybackSpeed.Instant };
    private Button[] _aiSpeedButtons = null!;
    private Button[] _replaySpeedButtons = null!;
    private static readonly Font _serifFont =
        GD.Load<FontFile>("res://fonts/DMSerifDisplay-Regular.ttf");

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

        // Centered panel — picks up the theme's slate Panel stylebox.
        // No custom panelStyle override; the dialog reads as part of the
        // same family as the Load Game / New Game modals.
        _panel = new PanelContainer
        {
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            AnchorTop = 0.5f,
            AnchorBottom = 0.5f,
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.Both,
        };
        AddChild(_panel);

        var vbox = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(420, 0),
        };
        vbox.AddThemeConstantOverride("separation", 18);
        _panel.AddChild(vbox);

        var title = new Label
        {
            Text = "Settings",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontOverride("font", _serifFont);
        title.AddThemeFontSizeOverride("font_size", 36);
        vbox.AddChild(title);

        // Decorative gold rule under the title — matches the menu panels.
        var goldRule = new ColorRect
        {
            Color = UiPalette.GoldDim,
            CustomMinimumSize = new Vector2(0, 1),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        };
        goldRule.CustomMinimumSize = new Vector2(200, 1);
        vbox.AddChild(goldRule);

        _sfxCheckBox = new CheckBox
        {
            Text = "Sound Effects",
            ButtonPressed = UserSettings.SfxEnabled,
        };
        _sfxCheckBox.AddThemeFontSizeOverride("font_size", 24);
        _sfxCheckBox.Toggled += OnSfxToggled;
        AudioBus.AttachClick(_sfxCheckBox);
        vbox.AddChild(_sfxCheckBox);

        _vfxCheckBox = new CheckBox
        {
            Text = "Visual Effects",
            ButtonPressed = UserSettings.VfxEnabled,
        };
        _vfxCheckBox.AddThemeFontSizeOverride("font_size", 24);
        _vfxCheckBox.Toggled += OnVfxToggled;
        AudioBus.AttachClick(_vfxCheckBox);
        vbox.AddChild(_vfxCheckBox);

        var aiSpeedLabel = new Label { Text = "AI Turn Speed" };
        aiSpeedLabel.AddThemeFontSizeOverride("font_size", 24);
        aiSpeedLabel.AddThemeColorOverride("font_color", UiPalette.InkSoft);
        vbox.AddChild(aiSpeedLabel);

        // Shared ButtonGroup turns the four toggles into a radio set:
        // pressing one un-presses the others. Each button captures its
        // own PlaybackSpeed via the closure so the handler doesn't need
        // to parse the label back into an enum. Godot's default toggle
        // visuals are subtle (a slight shading shift); we paint our
        // own selected/unselected stylebox so the active speed is
        // obvious at a glance.
        var aiSpeedRow = new HBoxContainer();
        aiSpeedRow.AddThemeConstantOverride("separation", 8);
        var aiSpeedGroup = new ButtonGroup();
        PlaybackSpeed currentSpeed = UserSettings.AiSpeed;
        _aiSpeedButtons = new Button[SpeedOrder.Length];
        for (int i = 0; i < SpeedOrder.Length; i++)
        {
            PlaybackSpeed speed = SpeedOrder[i];
            var btn = new Button
            {
                Text = SpeedLabel(speed),
                ToggleMode = true,
                ButtonGroup = aiSpeedGroup,
                ButtonPressed = speed == currentSpeed,
                FocusMode = Control.FocusModeEnum.None,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            btn.AddThemeFontSizeOverride("font_size", 20);
            btn.Pressed += () => OnAiSpeedPressed(speed);
            // Toggled fires when this button's pressed state flips —
            // including the auto-unpress the ButtonGroup triggers on
            // siblings — so restyling here keeps every button in sync
            // without polling.
            btn.Toggled += pressed => ApplySpeedButtonStyle(btn, pressed);
            AudioBus.AttachClick(btn);
            aiSpeedRow.AddChild(btn);
            _aiSpeedButtons[i] = btn;
            ApplySpeedButtonStyle(btn, btn.ButtonPressed);
        }
        vbox.AddChild(aiSpeedRow);

        var replaySpeedLabel = new Label { Text = "Replay Speed" };
        replaySpeedLabel.AddThemeFontSizeOverride("font_size", 24);
        replaySpeedLabel.AddThemeColorOverride("font_color", UiPalette.InkSoft);
        vbox.AddChild(replaySpeedLabel);

        var replaySpeedRow = new HBoxContainer();
        replaySpeedRow.AddThemeConstantOverride("separation", 8);
        var replaySpeedGroup = new ButtonGroup();
        PlaybackSpeed currentReplaySpeed = UserSettings.ReplaySpeed;
        _replaySpeedButtons = new Button[SpeedOrder.Length];
        for (int i = 0; i < SpeedOrder.Length; i++)
        {
            PlaybackSpeed speed = SpeedOrder[i];
            var btn = new Button
            {
                Text = SpeedLabel(speed),
                ToggleMode = true,
                ButtonGroup = replaySpeedGroup,
                ButtonPressed = speed == currentReplaySpeed,
                FocusMode = Control.FocusModeEnum.None,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            btn.AddThemeFontSizeOverride("font_size", 20);
            btn.Pressed += () => OnReplaySpeedPressed(speed);
            btn.Toggled += pressed => ApplySpeedButtonStyle(btn, pressed);
            AudioBus.AttachClick(btn);
            replaySpeedRow.AddChild(btn);
            _replaySpeedButtons[i] = btn;
            ApplySpeedButtonStyle(btn, btn.ButtonPressed);
        }
        vbox.AddChild(replaySpeedRow);

        var backButton = new Button
        {
            Text = "Back",
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        backButton.AddThemeFontSizeOverride("font_size", 24);
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
        PlaybackSpeed currentSpeed = UserSettings.AiSpeed;
        for (int i = 0; i < SpeedOrder.Length; i++)
        {
            Button btn = _aiSpeedButtons[i];
            bool pressed = SpeedOrder[i] == currentSpeed;
            btn.ButtonPressed = pressed;
            // Setting ButtonPressed programmatically does NOT raise
            // Toggled, so refresh the stylebox by hand.
            ApplySpeedButtonStyle(btn, pressed);
        }
        PlaybackSpeed currentReplaySpeed = UserSettings.ReplaySpeed;
        for (int i = 0; i < SpeedOrder.Length; i++)
        {
            Button btn = _replaySpeedButtons[i];
            bool pressed = SpeedOrder[i] == currentReplaySpeed;
            btn.ButtonPressed = pressed;
            ApplySpeedButtonStyle(btn, pressed);
        }
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

    private void OnAiSpeedPressed(PlaybackSpeed speed)
    {
        UserSettings.AiSpeed = speed;
    }

    private void OnReplaySpeedPressed(PlaybackSpeed speed)
    {
        UserSettings.ReplaySpeed = speed;
    }

    private static string SpeedLabel(PlaybackSpeed speed) => speed switch
    {
        PlaybackSpeed.Slow => "Slow",
        PlaybackSpeed.Normal => "Normal",
        PlaybackSpeed.Fast => "Fast",
        PlaybackSpeed.Instant => "Instant",
        _ => speed.ToString(),
    };

    /// <summary>
    /// Repaint a speed button so the selected one reads as solid white
    /// with dark text (mirrors the CTA accent in HudView) and the
    /// unselected ones read as dim, transparent dark with light text.
    /// Both states keep a visible border so the four-button row reads
    /// as a single control rather than free-floating glyphs.
    /// </summary>
    private static void ApplySpeedButtonStyle(Button btn, bool pressed)
    {
        var style = new StyleBoxFlat
        {
            BgColor = pressed
                ? new Color(1f, 1f, 1f, 1f)
                : new Color(0.15f, 0.15f, 0.18f, 1f),
            BorderColor = pressed
                ? new Color(0f, 0f, 0f, 1f)
                : new Color(0.55f, 0.55f, 0.6f, 1f),
            BorderWidthLeft = 2,
            BorderWidthRight = 2,
            BorderWidthTop = 2,
            BorderWidthBottom = 2,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 6,
            ContentMarginBottom = 6,
        };
        btn.AddThemeStyleboxOverride("normal", style);
        btn.AddThemeStyleboxOverride("hover", style);
        btn.AddThemeStyleboxOverride("pressed", style);
        btn.AddThemeStyleboxOverride("hover_pressed", style);
        Color textColor = pressed
            ? new Color(0f, 0f, 0f, 1f)
            : new Color(0.9f, 0.9f, 0.95f, 1f);
        btn.AddThemeColorOverride("font_color", textColor);
        btn.AddThemeColorOverride("font_hover_color", textColor);
        btn.AddThemeColorOverride("font_pressed_color", textColor);
        btn.AddThemeColorOverride("font_hover_pressed_color", textColor);
    }
}
