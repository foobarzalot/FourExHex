// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using Godot;

/// <summary>
/// Bug-report modal — backdrop + centered panel stating exactly what the
/// report will contain, with Send and Back. Opened from
/// <see cref="SettingsPanel"/>'s Report a Bug button and layered one above it
/// (Layer 101 vs 100), the same arrangement as <see cref="CreditsPanel"/>.
///
/// Send stages the bundle (<see cref="BugReportBundle"/>) and hands it to
/// <see cref="MailBridge"/>. Which rung that takes decides what the panel
/// says afterwards: the share sheet cannot carry a recipient, so the panel
/// tells the player the address is on their clipboard; the desktop
/// <c>mailto:</c> rung cannot carry an attachment, so it names the staged
/// file instead. Either way the panel stays open with the follow-up
/// instruction visible rather than closing out from under it.
///
/// <see cref="ProcessMode"/> = Always for the same reason as
/// <see cref="SettingsPanel"/>: reachable from the paused in-game flow as
/// well as the unpaused main menu.
/// </summary>
public sealed partial class BugReportPanel : CanvasLayer
{
    public event Action? Closed;

    public bool IsOpen { get; private set; }

    /// <summary>Supplies the live game's facts, or null when there is no game
    /// (the main menu). Hosts that have a game also write a fresh autosave
    /// here, so the attached save is the moment the player pressed Send.</summary>
    public Func<BugReportGameFacts?>? GameFacts { get; set; }

    private ColorRect _backdrop = null!;
    private PanelContainer _panel = null!;
    private Label _outcome = null!;
    private bool _viewportResizeHooked;
    private static readonly Font _serifFont =
        GD.Load<FontFile>("res://fonts/DMSerifDisplay-Regular.ttf");

    // Matches CreditsPanel / SettingsPanel so switching between them doesn't
    // jump the box.
    private const float DesignWidth = 456f;
    private const float DesignHeight = 540f;
    private const float ViewportMargin = UiMetrics.ViewportMarginPx;

    public override void _Ready()
    {
        Layer = 101;
        Visible = false;
        ProcessMode = ProcessModeEnum.Always;

        Vector2 viewport = GetViewport().GetVisibleRect().Size;

        _backdrop = ModalChrome.BuildBackdrop(viewport);
        AddChild(_backdrop);

        _panel = ModalChrome.BuildCenteredPanel(DesignWidth, DesignHeight);
        AddChild(_panel);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 18);
        _panel.AddChild(vbox);

        var title = new Label
        {
            Text = Strings.Get(StringKeys.SettingsReportBug),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontOverride("font", _serifFont);
        title.AddThemeFontSizeOverride("font_size", 36);
        vbox.AddChild(title);

        vbox.AddChild(ModalChrome.GoldRule());

        var blurb = new Label
        {
            Text = Strings.Get(StringKeys.ReportBlurb),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        blurb.AddThemeFontSizeOverride("font_size", 22);
        blurb.AddThemeColorOverride("font_color", UiPalette.InkSoft);
        vbox.AddChild(blurb);

        // Filled in after Send with whatever the taken rung still needs from
        // the player (paste the address / attach the file), or the failure.
        // Expands so a long staged path scrolls the panel rather than
        // pushing the buttons off it.
        _outcome = new Label
        {
            Text = string.Empty,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Top,
        };
        _outcome.AddThemeFontSizeOverride("font_size", 20);
        vbox.AddChild(_outcome);

        vbox.AddChild(MakeNavButton(Strings.Get(StringKeys.ReportSend), OnSendPressed));
        vbox.AddChild(MakeNavButton(Strings.Get(StringKeys.MenuBack), Close));

        FitPanel();
        GetViewport().SizeChanged += FitPanel;
        _viewportResizeHooked = true;
        SafeArea.Changed += OnSafeAreaChanged;
    }

    public override void _ExitTree()
    {
        SafeArea.Changed -= OnSafeAreaChanged;
        if (!_viewportResizeHooked) return;
        GetViewport().SizeChanged -= FitPanel;
        _viewportResizeHooked = false;
        Log.Debug(Log.LogCategory.Display,
            "BugReportPanel: viewport SizeChanged unsubscribed on exit");
    }

    private void OnSafeAreaChanged(LogicalSafeInsets _) => FitPanel();

    private static Button MakeNavButton(string text, Action onPressed)
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

    private void OnSendPressed()
    {
        BugReportGameFacts? facts = GameFacts?.Invoke();
        Log.Debug(Log.LogCategory.Report,
            $"[report] send pressed (game: {(facts == null ? "none" : facts.Mode)})");

        StagedBugReport staged;
        try
        {
            staged = BugReportBundle.Stage(facts);
        }
        catch (Exception ex)
        {
            // Staging is the one step that can fail outright (no space, a
            // read-only user dir). Say so rather than opening an empty mail.
            Log.Warn(Log.LogCategory.Report, $"[report] staging failed: {ex.Message}");
            _outcome.AddThemeColorOverride("font_color", UiPalette.Accent);
            _outcome.Text = Strings.Get(StringKeys.ReportFailed, ("error", ex.Message));
            return;
        }

        MailBridge.Rung rung = MailBridge.Compose(staged);
        _outcome.AddThemeColorOverride("font_color", UiPalette.InkSoft);
        _outcome.Text = rung switch
        {
            MailBridge.Rung.ShareSheet =>
                Strings.Get(StringKeys.ReportPasteHint, ("address", MailBridge.Address)),
            MailBridge.Rung.Mailto =>
                Strings.Get(StringKeys.ReportAttachHint, ("path", staged.AbsolutePath)),
            _ => string.Empty,
        };
    }

    /// <summary>Width-driven shrink-to-fit, matching
    /// <see cref="CreditsPanel"/> so the box is stable across the family.</summary>
    private void FitPanel()
    {
        Vector2 vp = GetViewport().GetVisibleRect().Size;
        LogicalSafeInsets safe = SafeArea.Current;
        (float availW, float availH) =
            PanelFitMath.AvailableBox(vp.X, vp.Y, safe, ViewportMargin);

        (float scale, float panelH) =
            PanelFitMath.WidthFitWithHeightCap(DesignWidth, DesignHeight, availW, availH);
        _panel.OffsetTop = -panelH * 0.5f;
        _panel.OffsetBottom = panelH * 0.5f;

        _panel.PivotOffset = new Vector2(DesignWidth, panelH) * 0.5f;
        _panel.Scale = new Vector2(scale, scale);

        Log.Debug(Log.LogCategory.Render,
            $"BugReportPanel: fit viewport={vp.X:0}x{vp.Y:0} " +
            $"scale={scale:0.00} panelH={panelH:0}");
    }

    public void Open()
    {
        if (IsOpen) return;
        // A fresh visit starts without the previous send's instruction.
        _outcome.Text = string.Empty;
        FitPanel();
        IsOpen = true;
        Visible = true;
        Log.Debug(Log.LogCategory.Report, "[report] panel opened");
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        Visible = false;
        Log.Debug(Log.LogCategory.Report, "[report] panel closed");
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
}
