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
    // True once _Ready hooked the viewport's SizeChanged (_ExitTree must
    // not disconnect a never-connected signal).
    private bool _viewportResizeHooked;
    // Each toggle is a square Button laid out beside a separate Label
    // (see BuildCheckRow) rather than a stock CheckBox: keeping the box
    // and its caption as independent controls means the label can never
    // shift on hover (the bug stock CheckBox's per-state content margins
    // produce) and the box can be sized however we like.
    private Button _sfxCheckBox = null!;
    private Button _vfxCheckBox = null!;
    private CreditsPanel _creditsPanel = null!;

    // Display order for both speed radio rows (AI Turn Speed and Replay
    // Speed are independent settings but share the preset list). Open()
    // re-syncs each row's pressed state from UserSettings against this.
    private static readonly PlaybackSpeed[] SpeedOrder =
        { PlaybackSpeed.Slow, PlaybackSpeed.Normal, PlaybackSpeed.Fast, PlaybackSpeed.Instant };
    private Button[] _aiSpeedButtons = null!;
    private Button[] _replaySpeedButtons = null!;
    private static readonly Font _serifFont =
        GD.Load<FontFile>("res://fonts/DMSerifDisplay-Regular.ttf");

    // Single fixed-size centered panel (one column, both orientations). When
    // the safe viewport is smaller than the panel's design size — landscape on
    // a short phone — FitPanel scales the whole panel down uniformly to fit,
    // the same shrink-to-fit the main menu uses for its panels (issue #17).
    private const float ContentWidth = 420f;   // inner VBox min width
    private const float ViewportMargin = 24f;

    public override void _Ready()
    {
        Layer = 100;
        Visible = false;
        // Always — works in both the unpaused main menu and the paused
        // in-game pause flow. See EscMenu for the same reasoning.
        ProcessMode = ProcessModeEnum.Always;

        Vector2 viewport = GetViewport().GetVisibleRect().Size;

        _backdrop = ModalChrome.BuildBackdrop(viewport);
        AddChild(_backdrop);

        // Content-sized centered panel — a single column in both orientations.
        // FitPanel scales it down (never up) to fit a short/narrow viewport,
        // the same shrink-to-fit the main menu uses for its panels.
        _panel = ModalChrome.BuildCenteredPanel();
        AddChild(_panel);

        var vbox = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(ContentWidth, 0),
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
            CustomMinimumSize = new Vector2(200, 1),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        };
        vbox.AddChild(goldRule);

        vbox.AddChild(BuildCheckRow("Sound Effects", UserSettings.SfxEnabled, OnSfxToggled, out _sfxCheckBox));
        vbox.AddChild(BuildCheckRow("Visual Effects", UserSettings.VfxEnabled, OnVfxToggled, out _vfxCheckBox));

        var aiSpeedLabel = new Label { Text = "Computer Player Speed" };
        aiSpeedLabel.AddThemeFontSizeOverride("font_size", 24);
        aiSpeedLabel.AddThemeColorOverride("font_color", UiPalette.InkSoft);
        vbox.AddChild(aiSpeedLabel);
        vbox.AddChild(BuildSpeedRow(UserSettings.AiSpeed, OnAiSpeedPressed, out _aiSpeedButtons));

        var replaySpeedLabel = new Label { Text = "Replay Speed" };
        replaySpeedLabel.AddThemeFontSizeOverride("font_size", 24);
        replaySpeedLabel.AddThemeColorOverride("font_color", UiPalette.InkSoft);
        vbox.AddChild(replaySpeedLabel);
        vbox.AddChild(BuildSpeedRow(UserSettings.ReplaySpeed, OnReplaySpeedPressed, out _replaySpeedButtons));

        var creditsButton = new Button
        {
            Text = "Credits",
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        creditsButton.AddThemeFontSizeOverride("font_size", 24);
        creditsButton.Pressed += OnCreditsPressed;
        AudioBus.AttachClick(creditsButton);
        vbox.AddChild(creditsButton);

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

        // Unobtrusive build stamp at the foot of the panel so testers can
        // report which version they're on. Static text (AppVersion is a
        // compile-time constant), so Open() never needs to re-sync it.
        var versionLabel = new Label
        {
            Text = AppVersion.Display,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        versionLabel.AddThemeFontSizeOverride("font_size", 16);
        versionLabel.AddThemeColorOverride("font_color", UiPalette.InkMute);
        vbox.AddChild(versionLabel);

        // Credits is its own modal layered one above this panel (Layer
        // 101 vs 100), so it draws on top while Settings stays visible.
        _creditsPanel = new CreditsPanel();
        AddChild(_creditsPanel);

        // Scale-to-fit now and on every later change. Rotation / window move
        // fires SizeChanged; a notch/status-bar toggle that shifts the safe
        // rect without a resize fires SafeArea.Changed.
        FitPanel();
        GetViewport().SizeChanged += FitPanel;
        _viewportResizeHooked = true;
        SafeArea.Changed += OnSafeAreaChanged;
    }

    public override void _ExitTree()
    {
        SafeArea.Changed -= OnSafeAreaChanged;
        // Guarded: disconnecting a never-connected Godot signal errors.
        if (!_viewportResizeHooked) return;
        GetViewport().SizeChanged -= FitPanel;
        _viewportResizeHooked = false;
        Log.Debug(Log.LogCategory.Display,
            "SettingsPanel: viewport SizeChanged unsubscribed on exit");
    }

    private void OnSafeAreaChanged(LogicalSafeInsets _) => FitPanel();

    /// <summary>Scale the centered panel down (never up) so its single-column
    /// layout fits within the safe viewport — the same shrink-to-fit the main
    /// menu's <c>ScaleToFit</c> uses. In a short landscape safe area the whole
    /// panel scales uniformly instead of scrolling or clipping (issue #17).</summary>
    private void FitPanel()
    {
        Vector2 vp = GetViewport().GetVisibleRect().Size;
        LogicalSafeInsets safe = SafeArea.Current;
        float availW = vp.X - safe.Left - safe.Right - ViewportMargin * 2f;
        float availH = vp.Y - safe.Top - safe.Bottom - ViewportMargin * 2f;

        Vector2 design = _panel.GetCombinedMinimumSize();
        float scale = design.X > 0f && design.Y > 0f
            ? Mathf.Min(1f, Mathf.Min(availW / design.X, availH / design.Y))
            : 1f;
        _panel.PivotOffset = design * 0.5f;
        _panel.Scale = new Vector2(scale, scale);

        Log.Debug(Log.LogCategory.Render,
            $"SettingsPanel: fit viewport={vp.X:0}x{vp.Y:0} " +
            $"safe=(t{safe.Top:0},b{safe.Bottom:0},l{safe.Left:0},r{safe.Right:0}) " +
            $"design={design.X:0}x{design.Y:0} scale={scale:0.00}");
    }

    private void OnCreditsPressed()
    {
        _creditsPanel.Open();
    }

    /// <summary>Show the panel. Re-syncs toggles from
    /// <see cref="UserSettings"/> so external changes are reflected.</summary>
    public void Open()
    {
        if (IsOpen) return;
        // Re-fit in case the viewport / safe area changed while closed.
        FitPanel();
        _sfxCheckBox.ButtonPressed = UserSettings.SfxEnabled;
        ApplyCheckBoxStyle(_sfxCheckBox, UserSettings.SfxEnabled);
        _vfxCheckBox.ButtonPressed = UserSettings.VfxEnabled;
        ApplyCheckBoxStyle(_vfxCheckBox, UserSettings.VfxEnabled);
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
        // Tear down the credits modal too — it's a separate CanvasLayer,
        // so hiding this panel wouldn't hide it on its own.
        _creditsPanel.Close();
        IsOpen = false;
        Visible = false;
        Closed?.Invoke();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!IsOpen) return;
        // While Credits is open it owns Escape (closes credits only, not
        // settings); don't double-handle the key here.
        if (_creditsPanel.IsOpen) return;
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

    /// <summary>
    /// Build one boolean-setting row: a left-aligned caption Label that
    /// fills the row, plus a fixed-size square toggle Button on the right.
    /// Splitting caption and box into two sibling controls (rather than a
    /// stock CheckBox, which bundles icon+text into one control with
    /// per-state content margins) means the caption can never shift on
    /// hover. Returns the parentless row; <paramref name="box"/> hands back the
    /// toggle Button so Open() can re-sync its pressed state. RebuildBody
    /// parents the row into the current single/two-column arrangement.
    /// </summary>
    private HBoxContainer BuildCheckRow(string label, bool initial, Action<bool> onToggled, out Button box)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);

        var caption = new Label
        {
            Text = label,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
        };
        caption.AddThemeFontSizeOverride("font_size", 24);
        caption.AddThemeColorOverride("font_color", UiPalette.InkSoft);
        row.AddChild(caption);

        box = new Button
        {
            ToggleMode = true,
            ButtonPressed = initial,
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(32, 32),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        Button boxLocal = box;
        boxLocal.AddThemeFontSizeOverride("font_size", 22);
        boxLocal.Toggled += pressed =>
        {
            ApplyCheckBoxStyle(boxLocal, pressed);
            onToggled(pressed);
        };
        AudioBus.AttachClick(boxLocal);
        ApplyCheckBoxStyle(boxLocal, initial);
        row.AddChild(boxLocal);

        return row;
    }

    /// <summary>
    /// Build one parentless four-button speed radio row (Slow / Normal / Fast /
    /// Instant) sharing a ButtonGroup. <paramref name="buttons"/> hands back the
    /// buttons so Open() can re-sync pressed state. Used for both the AI-turn
    /// and replay speed rows.
    /// </summary>
    private HBoxContainer BuildSpeedRow(PlaybackSpeed current, Action<PlaybackSpeed> onPressed, out Button[] buttons)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        var group = new ButtonGroup();
        buttons = new Button[SpeedOrder.Length];
        for (int i = 0; i < SpeedOrder.Length; i++)
        {
            PlaybackSpeed speed = SpeedOrder[i];
            var btn = new Button
            {
                Text = SpeedLabel(speed),
                ToggleMode = true,
                ButtonGroup = group,
                ButtonPressed = speed == current,
                FocusMode = Control.FocusModeEnum.None,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            btn.AddThemeFontSizeOverride("font_size", 20);
            btn.Pressed += () => onPressed(speed);
            // Toggled fires on every pressed-state flip — including the
            // auto-unpress the ButtonGroup triggers on siblings — so restyling
            // here keeps every button in sync without polling.
            btn.Toggled += pressed => ApplySpeedButtonStyle(btn, pressed);
            AudioBus.AttachClick(btn);
            row.AddChild(btn);
            buttons[i] = btn;
            ApplySpeedButtonStyle(btn, btn.ButtonPressed);
        }
        return row;
    }

    /// <summary>
    /// Repaint the square toggle: gold filled with a dark check when on,
    /// dark with a light border when off. Hover only brightens the
    /// border/fill — because the box holds nothing but a centered glyph,
    /// no caption or content shifts under the cursor.
    /// </summary>
    private static void ApplyCheckBoxStyle(Button box, bool pressed)
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
