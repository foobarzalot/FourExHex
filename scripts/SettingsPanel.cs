// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
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
    // The surface the body lives in: portrait = a centered PanelContainer that
    // FitPanel scales down; landscape = the rounded fill surface from
    // LandscapeMenuChrome (never scaled).
    private PanelContainer _panel = null!;
    // Node RebuildBody frees on an orientation flip (currently == _panel).
    private Control _panelRoot = null!;
    // Which layout the body was last built for; a resize that flips it rebuilds
    // the body (two-zones landscape ↔ single-column portrait).
    private ScreenOrientation _orientation;
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

    // Item order for both speed dropdowns (AI Turn Speed and Replay Speed
    // are independent settings but share the preset list). Open() re-syncs
    // each dropdown's selection from UserSettings against this.
    private static readonly PlaybackSpeed[] SpeedOrder =
        { PlaybackSpeed.Slow, PlaybackSpeed.Normal, PlaybackSpeed.Fast, PlaybackSpeed.Instant };
    private OptionButton _aiSpeedDropdown = null!;
    private OptionButton _replaySpeedDropdown = null!;
    private OptionButton _automateSpeedDropdown = null!;
    private static readonly Font _serifFont =
        GD.Load<FontFile>("res://fonts/DMSerifDisplay-Regular.ttf");

    // Single fixed-size centered panel (one column, both orientations). When
    // the safe viewport is smaller than the panel's design size — landscape on
    // a short phone — FitPanel scales the whole panel down uniformly to fit,
    // the same shrink-to-fit the main menu uses for its panels.
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

        // Build the layout for the current orientation (portrait single column
        // vs landscape two-zones). The toggle/speed controls are the same
        // instances either way — only the container tree differs.
        BuildBody();

        // Credits is its own modal layered one above this panel (Layer
        // 101 vs 100), so it draws on top while Settings stays visible.
        // It persists across body rebuilds (RebuildBody frees only _panelRoot).
        _creditsPanel = new CreditsPanel();
        AddChild(_creditsPanel);

        // React now and on every later change. A resize that flips orientation
        // rebuilds the body; a same-orientation resize re-fits (portrait) or
        // re-insets (landscape). A notch/status-bar toggle that shifts the safe
        // rect without a resize fires SafeArea.Changed.
        GetViewport().SizeChanged += OnViewportResized;
        _viewportResizeHooked = true;
        SafeArea.Changed += OnSafeAreaChanged;
    }

    public override void _ExitTree()
    {
        SafeArea.Changed -= OnSafeAreaChanged;
        // Guarded: disconnecting a never-connected Godot signal errors.
        if (!_viewportResizeHooked) return;
        GetViewport().SizeChanged -= OnViewportResized;
        _viewportResizeHooked = false;
        Log.Debug(Log.LogCategory.Display,
            "SettingsPanel: viewport SizeChanged unsubscribed on exit");
    }

    /// <summary>Build the panel subtree for the current orientation, populating
    /// <see cref="_panel"/> (the surface) and <see cref="_panelRoot"/> (the node
    /// to free on a flip).</summary>
    private void BuildBody()
    {
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        _orientation = ScreenLayout.Resolve(viewport.X, viewport.Y);
        if (_orientation == ScreenOrientation.Landscape) BuildLandscapeBody();
        else BuildPortraitBody();
        Log.Info(Log.LogCategory.Render,
            $"SettingsPanel: built {_orientation} body (viewport {viewport.X:0}x{viewport.Y:0})");
    }

    /// <summary>Single-column layout: a content-sized centered panel
    /// that FitPanel scales down (never up) to fit a short/narrow viewport.</summary>
    private void BuildPortraitBody()
    {
        _panel = ModalChrome.BuildCenteredPanel();
        _panelRoot = _panel;
        AddChild(_panel);

        var vbox = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(ContentWidth, 0),
        };
        vbox.AddThemeConstantOverride("separation", 18);
        _panel.AddChild(vbox);

        vbox.AddChild(MakeTitle());
        vbox.AddChild(MakeGoldRule());

        vbox.AddChild(BuildCheckRow(Strings.Get(StringKeys.SettingsSoundEffects), UserSettings.SfxEnabled, OnSfxToggled, out _sfxCheckBox));
        vbox.AddChild(BuildCheckRow(Strings.Get(StringKeys.SettingsVisualEffects), UserSettings.VfxEnabled, OnVfxToggled, out _vfxCheckBox));

        vbox.AddChild(BuildSpeedRow(
            Strings.Get(StringKeys.SettingsAiSpeed), UserSettings.AiSpeed, OnAiSpeedPressed, out _aiSpeedDropdown));
        vbox.AddChild(BuildSpeedRow(
            Strings.Get(StringKeys.SettingsAutomateSpeed), UserSettings.AutomateSpeed, OnAutomateSpeedPressed, out _automateSpeedDropdown));
        vbox.AddChild(BuildSpeedRow(
            Strings.Get(StringKeys.SettingsReplaySpeed), UserSettings.ReplaySpeed, OnReplaySpeedPressed, out _replaySpeedDropdown));

        vbox.AddChild(MakeNavButton(Strings.Get(StringKeys.SettingsCredits), OnCreditsPressed));
        vbox.AddChild(MakeNavButton(Strings.Get(StringKeys.MenuBack), Close));

        // Unobtrusive build stamp at the foot of the panel so testers can
        // report which version they're on.
        vbox.AddChild(MakeVersionLabel());

        FitPanel();
    }

    /// <summary>"Two zones" landscape layout: a title row over a
    /// two-column body — toggles left, the two speed segmented controls right —
    /// with a Credits / Back / version footer, filling the safe rect instead of
    /// downscaling a portrait stack.</summary>
    private void BuildLandscapeBody()
    {
        _panel = LandscapeMenuChrome.Build();
        _panelRoot = _panel;
        AddChild(_panel);

        var outer = new VBoxContainer();
        outer.AddThemeConstantOverride("separation", 18);
        _panel.AddChild(outer);

        // Title row: serif title + an expanding gold rule to its right.
        var titleRow = new HBoxContainer();
        titleRow.AddThemeConstantOverride("separation", 18);
        Label title = MakeTitle();
        title.HorizontalAlignment = HorizontalAlignment.Left;
        title.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        titleRow.AddChild(title);
        var titleRule = new ColorRect
        {
            Color = UiPalette.GoldDim,
            CustomMinimumSize = new Vector2(0, 2),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        titleRow.AddChild(titleRule);
        outer.AddChild(titleRow);

        // Body: left toggle zone | hairline | right speed zone.
        var body = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 28);
        outer.AddChild(body);

        var leftZone = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.Fill,
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        leftZone.AddThemeConstantOverride("separation", 14);
        leftZone.AddChild(MakeToggleCard(
            BuildCheckRow(Strings.Get(StringKeys.SettingsSoundEffects), UserSettings.SfxEnabled, OnSfxToggled, out _sfxCheckBox)));
        leftZone.AddChild(MakeToggleCard(
            BuildCheckRow(Strings.Get(StringKeys.SettingsVisualEffects), UserSettings.VfxEnabled, OnVfxToggled, out _vfxCheckBox)));
        body.AddChild(leftZone);

        body.AddChild(new ColorRect
        {
            Color = UiPalette.LineSoft,
            CustomMinimumSize = new Vector2(1, 0),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        });

        var rightZone = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.Fill,
            // Slightly wider than the left zone (design: 1.12 : 1).
            SizeFlagsStretchRatio = 1.12f,
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        rightZone.AddThemeConstantOverride("separation", 14);
        rightZone.AddChild(MakeToggleCard(BuildSpeedRow(
            Strings.Get(StringKeys.SettingsAiSpeed), UserSettings.AiSpeed, OnAiSpeedPressed, out _aiSpeedDropdown)));
        rightZone.AddChild(MakeToggleCard(BuildSpeedRow(
            Strings.Get(StringKeys.SettingsAutomateSpeed), UserSettings.AutomateSpeed, OnAutomateSpeedPressed, out _automateSpeedDropdown)));
        rightZone.AddChild(MakeToggleCard(BuildSpeedRow(
            Strings.Get(StringKeys.SettingsReplaySpeed), UserSettings.ReplaySpeed, OnReplaySpeedPressed, out _replaySpeedDropdown)));
        body.AddChild(rightZone);

        // Footer: Credits | Back (equal width) + version pinned far right.
        var footer = new HBoxContainer();
        footer.AddThemeConstantOverride("separation", 14);
        footer.AddChild(MakeNavButton(Strings.Get(StringKeys.SettingsCredits), OnCreditsPressed));
        footer.AddChild(MakeNavButton(Strings.Get(StringKeys.MenuBack), Close));
        Label version = MakeVersionLabel();
        version.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        footer.AddChild(version);
        outer.AddChild(footer);

        LandscapeMenuChrome.ApplyLayout(_panel, GetViewport().GetVisibleRect().Size, SafeArea.Current);
    }

    private Label MakeTitle()
    {
        var title = new Label
        {
            Text = Strings.Get(StringKeys.MenuSettings),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontOverride("font", _serifFont);
        title.AddThemeFontSizeOverride("font_size", 36);
        return title;
    }

    // Decorative gold rule under the title — matches the menu panels.
    private static ColorRect MakeGoldRule() => new ColorRect
    {
        Color = UiPalette.GoldDim,
        CustomMinimumSize = new Vector2(200, 1),
        SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
    };

    private static Label MakeSpeedLabel(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", 24);
        label.AddThemeColorOverride("font_color", UiPalette.InkSoft);
        return label;
    }

    private Button MakeNavButton(string text, Action onPressed)
    {
        var button = new Button
        {
            Text = text,
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        button.AddThemeFontSizeOverride("font_size", 24);
        button.Pressed += onPressed;
        AudioBus.AttachClick(button);
        return button;
    }

    // Static text (AppVersion is a compile-time constant), so Open() never
    // needs to re-sync it.
    private static Label MakeVersionLabel()
    {
        var label = new Label
        {
            Text = AppVersion.Display,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        label.AddThemeFontSizeOverride("font_size", 16);
        label.AddThemeColorOverride("font_color", UiPalette.InkMute);
        return label;
    }

    /// <summary>Landscape only: wrap a toggle row in a raised rounded card
    /// (design: full-width #3b362e row, ~62px tall) so the two toggles read as
    /// distinct controls filling the left zone.</summary>
    private static PanelContainer MakeToggleCard(HBoxContainer row)
    {
        var style = new StyleBoxFlat { BgColor = UiPalette.BgElev };
        style.SetCornerRadiusAll(10);
        style.ContentMarginLeft = 18;
        style.ContentMarginRight = 18;
        style.ContentMarginTop = 6;
        style.ContentMarginBottom = 6;
        var card = new PanelContainer
        {
            CustomMinimumSize = new Vector2(0, 62),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        card.AddThemeStyleboxOverride("panel", style);
        card.AddChild(row);
        return card;
    }

    /// <summary>Free the current body and rebuild it for the new orientation
    /// (mirrors MainMenuScene's play-config flip rebuild). State lives in
    /// UserSettings, so a re-sync after the rebuild loses nothing.</summary>
    private void RebuildBody()
    {
        Log.Debug(Log.LogCategory.Render,
            $"SettingsPanel: orientation flip from {_orientation}; rebuilding body");
        _panelRoot.QueueFree();
        BuildBody();
        if (IsOpen) SyncControls();
    }

    private void OnViewportResized()
    {
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        ScreenOrientation next = ScreenLayout.Resolve(viewport.X, viewport.Y);
        if (next != _orientation) { RebuildBody(); return; }
        RefitOrRelayout();
    }

    private void OnSafeAreaChanged(LogicalSafeInsets _) => RefitOrRelayout();

    /// <summary>Portrait scales the fixed panel to fit; landscape re-centers and
    /// re-caps the fill surface against the current viewport / safe area.</summary>
    private void RefitOrRelayout()
    {
        if (_orientation == ScreenOrientation.Portrait) FitPanel();
        else LandscapeMenuChrome.ApplyLayout(_panel, GetViewport().GetVisibleRect().Size, SafeArea.Current);
    }

    /// <summary>Scale the centered panel down (never up) so its single-column
    /// layout fits within the safe viewport — the same shrink-to-fit the main
    /// menu's <c>ScaleToFit</c> uses. In a short landscape safe area the whole
    /// panel scales uniformly instead of scrolling or clipping.</summary>
    private void FitPanel()
    {
        Vector2 vp = GetViewport().GetVisibleRect().Size;
        LogicalSafeInsets safe = SafeArea.Current;
        (float availW, float availH) = PanelFitMath.AvailableBox(vp.X, vp.Y, safe, ViewportMargin);

        Vector2 design = _panel.GetCombinedMinimumSize();
        float scale = PanelFitMath.ScaleToFit(design.X, design.Y, availW, availH);
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
        // Re-fit / re-layout in case the viewport / safe area changed while closed.
        RefitOrRelayout();
        SyncControls();
        IsOpen = true;
        Visible = true;
    }

    /// <summary>Re-sync every control from <see cref="UserSettings"/> so external
    /// changes (or a body rebuild on orientation flip) are reflected.</summary>
    private void SyncControls()
    {
        _sfxCheckBox.ButtonPressed = UserSettings.SfxEnabled;
        ApplyCheckBoxStyle(_sfxCheckBox, UserSettings.SfxEnabled);
        _vfxCheckBox.ButtonPressed = UserSettings.VfxEnabled;
        ApplyCheckBoxStyle(_vfxCheckBox, UserSettings.VfxEnabled);
        UiDropdown.SelectItemById(_aiSpeedDropdown, (int)UserSettings.AiSpeed);
        UiDropdown.SelectItemById(_automateSpeedDropdown, (int)UserSettings.AutomateSpeed);
        UiDropdown.SelectItemById(_replaySpeedDropdown, (int)UserSettings.ReplaySpeed);
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
        Log.Debug(Log.LogCategory.Hud, $"[settings] AiSpeed -> {speed}");
    }

    private void OnReplaySpeedPressed(PlaybackSpeed speed)
    {
        UserSettings.ReplaySpeed = speed;
        Log.Debug(Log.LogCategory.Hud, $"[settings] ReplaySpeed -> {speed}");
    }

    private void OnAutomateSpeedPressed(PlaybackSpeed speed)
    {
        UserSettings.AutomateSpeed = speed;
        Log.Debug(Log.LogCategory.Hud, $"[settings] AutomateSpeed -> {speed}");
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
    private HBoxContainer BuildCheckRow(string label, bool initial, Action<bool> onToggled, out Button box) =>
        UiToggle.BuildCheckRow(label, initial, onToggled, out box);

    /// <summary>
    /// Build one parentless speed setting row: a left-aligned caption Label that
    /// fills the row, plus a speed dropdown (Slow / Normal / Fast / Instant) on
    /// the right. <paramref name="dropdown"/> hands back the dropdown so Open()
    /// can re-sync its selection. Used for both the AI-turn and replay settings.
    /// </summary>
    private HBoxContainer BuildSpeedRow(
        string caption, PlaybackSpeed current, Action<PlaybackSpeed> onSelected, out OptionButton dropdown)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);

        Label label = MakeSpeedLabel(caption);
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        label.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(label);

        dropdown = BuildSpeedDropdown(current, onSelected);
        dropdown.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(dropdown);
        return row;
    }

    /// <summary>
    /// Build the speed dropdown (Slow / Normal / Fast / Instant). Item ids are
    /// the <see cref="PlaybackSpeed"/> enum int, so selection round-trips via
    /// <c>GetSelectedId()</c> independent of item order.
    /// </summary>
    private OptionButton BuildSpeedDropdown(PlaybackSpeed current, Action<PlaybackSpeed> onSelected)
    {
        var dropdown = new OptionButton
        {
            // 32px tall to match the SFX/VFX toggle box, so a speed row is the
            // same height as a checkbox row.
            CustomMinimumSize = new Vector2(160, 32),
        };
        dropdown.AddThemeFontSizeOverride("font_size", 22);
        // The popup list is themed separately from the button; without this
        // override the expanded items render at the tiny default size.
        dropdown.GetPopup().AddThemeFontSizeOverride("font_size", 22);
        foreach (PlaybackSpeed speed in SpeedOrder)
        {
            dropdown.AddItem(SpeedLabel(speed), (int)speed);
        }
        UiDropdown.SelectItemById(dropdown, (int)current);
        dropdown.ItemSelected += _ => onSelected((PlaybackSpeed)dropdown.GetSelectedId());
        AudioBus.AttachClick(dropdown);
        return dropdown;
    }

    private static void ApplyCheckBoxStyle(Button box, bool pressed) =>
        UiToggle.ApplyStyle(box, pressed);

    private static string SpeedLabel(PlaybackSpeed speed) => speed switch
    {
        PlaybackSpeed.Slow => Strings.Get(StringKeys.SpeedSlow),
        PlaybackSpeed.Normal => Strings.Get(StringKeys.SpeedNormal),
        PlaybackSpeed.Fast => Strings.Get(StringKeys.SpeedFast),
        PlaybackSpeed.Instant => Strings.Get(StringKeys.SpeedInstant),
        _ => speed.ToString(),
    };

}
