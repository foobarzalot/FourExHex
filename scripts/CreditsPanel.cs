// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using Godot;

/// <summary>
/// Credits modal — backdrop + centered panel with a scrollable credits
/// blurb and a Back button. Opened from <see cref="SettingsPanel"/>'s
/// Credits button and layered one above it (Layer 101 vs 100) so it
/// renders on top of the still-visible Settings panel; closing it
/// returns to Settings.
///
/// <see cref="ProcessMode"/> = Always for the same reason as
/// <see cref="SettingsPanel"/>: Settings (and therefore this) can be
/// reached from the in-game pause flow where <c>GetTree().Paused</c> is
/// true, as well as the unpaused main menu.
/// </summary>
public sealed partial class CreditsPanel : CanvasLayer
{
    private const string RepoUrl = "https://github.com/foobarzalot/FourExHex";

    // BBCode (RichTextLabel) so the author name can be a clickable link
    // to the repo. The [url] meta is the URL itself; OnMetaClicked hands
    // it to OS.ShellOpen. The markup lives in the string store with the
    // repo link injected as {url}.
    private static string CreditsText
        => Strings.Get(StringKeys.CreditsBody, ("url", RepoUrl));

    public event Action? Closed;

    public bool IsOpen { get; private set; }

    private ColorRect _backdrop = null!;
    private PanelContainer _panel = null!;
    // True once _Ready hooked the viewport's SizeChanged (_ExitTree must
    // not disconnect a never-connected signal).
    private bool _viewportResizeHooked;
    private static readonly Font _serifFont =
        GD.Load<FontFile>("res://fonts/DMSerifDisplay-Regular.ttf");

    // Fixed-size centered panel (matches SettingsPanel so switching
    // Settings↔Credits doesn't jump the box). FitPanel scales it down (never
    // up) to fit a short/narrow safe viewport, the same shrink-to-fit the main
    // menu uses; the long credits text scrolls within the design-size panel.
    private const float DesignWidth = 456f;
    private const float DesignHeight = 540f;
    private const float ViewportMargin = 24f;

    public override void _Ready()
    {
        // One layer above SettingsPanel (100) so it draws on top.
        Layer = 101;
        Visible = false;
        ProcessMode = ProcessModeEnum.Always;

        Vector2 viewport = GetViewport().GetVisibleRect().Size;

        _backdrop = ModalChrome.BuildBackdrop(viewport);
        AddChild(_backdrop);

        // Anchor-centered panel clamped to the safe viewport by FitToViewport
        // (same rect as SettingsPanel), matching the Settings / Load Game
        // modal family. The explicit rect bounds the scroll body's height so a
        // short landscape safe area scrolls instead of clipping Back.
        _panel = ModalChrome.BuildCenteredPanel(DesignWidth, DesignHeight);
        AddChild(_panel);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 18);
        _panel.AddChild(vbox);

        var title = new Label
        {
            Text = Strings.Get(StringKeys.SettingsCredits),
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

        // Scrollable body so long credits don't blow out the panel.
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        vbox.AddChild(scroll);

        var body = new RichTextLabel
        {
            BbcodeEnabled = true,
            Text = CreditsText,
            FitContent = true,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            // Pass (not the default Stop) so a touch-drag propagates up to the
            // ScrollContainer for panning instead of being swallowed here; a
            // plain tap still reaches the [url] meta link.
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        body.AddThemeFontSizeOverride("normal_font_size", 22);
        body.AddThemeColorOverride("default_color", UiPalette.InkSoft);
        body.MetaClicked += OnMetaClicked;
        scroll.AddChild(body);

        var backButton = new Button
        {
            Text = Strings.Get(StringKeys.MenuBack),
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        backButton.AddThemeFontSizeOverride("font_size", 24);
        backButton.Pressed += Close;
        AudioBus.AttachClick(backButton);
        vbox.AddChild(backButton);

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
            "CreditsPanel: viewport SizeChanged unsubscribed on exit");
    }

    private void OnSafeAreaChanged(LogicalSafeInsets _) => FitPanel();

    /// <summary>Fit the panel to the safe viewport while keeping the credits at a
    /// constant width and font size across orientations. Scale is driven by
    /// <b>width only</b> (clamped ≤ 1), so a short landscape viewport doesn't
    /// shrink the text — instead the panel's height is capped to fit and the
    /// inner <see cref="ScrollContainer"/> simply scrolls further.</summary>
    private void FitPanel()
    {
        Vector2 vp = GetViewport().GetVisibleRect().Size;
        LogicalSafeInsets safe = SafeArea.Current;
        (float availW, float availH) = PanelFitMath.AvailableBox(vp.X, vp.Y, safe, ViewportMargin);

        // Width-only scale keeps the portrait width + font sizes in landscape; the
        // pre-scale height is capped so the scaled height fits and the scroll body
        // absorbs the reduction by scrolling further.
        (float scale, float panelH) =
            PanelFitMath.WidthFitWithHeightCap(DesignWidth, DesignHeight, availW, availH);
        _panel.OffsetTop = -panelH * 0.5f;
        _panel.OffsetBottom = panelH * 0.5f;

        _panel.PivotOffset = new Vector2(DesignWidth, panelH) * 0.5f;
        _panel.Scale = new Vector2(scale, scale);

        Log.Debug(Log.LogCategory.Render,
            $"CreditsPanel: fit viewport={vp.X:0}x{vp.Y:0} " +
            $"safe=(t{safe.Top:0},b{safe.Bottom:0},l{safe.Left:0},r{safe.Right:0}) " +
            $"scale={scale:0.00} panelH={panelH:0}");
    }

    private void OnMetaClicked(Variant meta)
    {
        string url = meta.AsString();
        Log.Info(Log.LogCategory.Input, $"CreditsPanel meta clicked — opening {url}");
        OS.ShellOpen(url);
    }

    public void Open()
    {
        if (IsOpen) return;
        // Re-fit in case the viewport / safe area changed while closed.
        FitPanel();
        IsOpen = true;
        Visible = true;
        Log.Debug(Log.LogCategory.Input, "CreditsPanel.Open");
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        Visible = false;
        Log.Debug(Log.LogCategory.Input, "CreditsPanel.Close");
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
