// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using Godot;

/// <summary>
/// Achievements modal — backdrop + centered panel listing every catalog
/// entry with its earned state, opened from the main menu. Mirrors
/// <see cref="CreditsPanel"/>'s skeleton (backdrop, centered panel, serif
/// title, gold rule, scroll body, Back) rather than
/// <see cref="CampaignPanel"/>'s viewport-filling scroller: a flat list
/// needs none of the campaign grid's geometry, and a
/// <see cref="CanvasLayer"/> overlay stays out of the landing/play-config
/// visibility triad.
///
/// Rows are rebuilt on every <see cref="Open"/> — all state lives in
/// <see cref="AchievementStore"/>, so a rebuild is cheap and always
/// current.
///
/// <see cref="ProcessMode"/> = Always to match the rest of the modal
/// family, which can be reached from the paused in-game flow.
/// </summary>
public sealed partial class AchievementsPanel : CanvasLayer
{
    public event Action? Closed;

    public bool IsOpen { get; private set; }

    private PanelContainer _panel = null!;
    private Label _headerCount = null!;
    private VBoxContainer _rows = null!;
    private bool _viewportResizeHooked;

    private static readonly Font _serifFont =
        GD.Load<FontFile>("res://fonts/DMSerifDisplay-Regular.ttf");

    // Matches the Settings / Credits box so switching between modals
    // doesn't jump the frame.
    private const float DesignWidth = 456f;
    private const float DesignHeight = 540f;
    private const float ViewportMargin = UiMetrics.ViewportMarginPx;
    private const float RowMinHeight = 64f;

    public override void _Ready()
    {
        // Same layer as SettingsPanel: only ever one of them is open.
        Layer = 100;
        Visible = false;
        ProcessMode = ProcessModeEnum.Always;

        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        AddChild(ModalChrome.BuildBackdrop(viewport));

        _panel = ModalChrome.BuildCenteredPanel(DesignWidth, DesignHeight);
        AddChild(_panel);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 14);
        _panel.AddChild(vbox);

        var title = new Label
        {
            Text = Strings.Get(StringKeys.AchieveTitle),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontOverride("font", _serifFont);
        title.AddThemeFontSizeOverride("font_size", 36);
        vbox.AddChild(title);

        _headerCount = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _headerCount.AddThemeFontSizeOverride("font_size", 20);
        _headerCount.AddThemeColorOverride("font_color", UiPalette.InkSoft);
        vbox.AddChild(_headerCount);

        vbox.AddChild(ModalChrome.GoldRule());

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        vbox.AddChild(scroll);

        _rows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _rows.AddThemeConstantOverride("separation", 8);
        // Ignore so a touch-drag in the gaps between rows reaches the
        // scroller instead of stopping here.
        _rows.MouseFilter = Control.MouseFilterEnum.Ignore;
        scroll.AddChild(_rows);

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
        if (!_viewportResizeHooked) return;
        GetViewport().SizeChanged -= FitPanel;
        _viewportResizeHooked = false;
    }

    private void OnSafeAreaChanged(LogicalSafeInsets _) => FitPanel();

    /// <summary>Rebuild the category sections + rows from the current
    /// record. Grouping comes from <see cref="AchievementPanelLayout"/>
    /// (Controller, unit-tested); this only renders it.</summary>
    private void Rebuild()
    {
        foreach (Node child in _rows.GetChildren()) child.QueueFree();

        AchievementRecord record = AchievementStore.Record;
        int unlocked = 0;
        foreach (AchievementPanelLayout.Group group in AchievementPanelLayout.Groups())
        {
            _rows.AddChild(BuildCategoryHeader(Strings.Get(group.TitleKey)));
            foreach (AchievementDefinition def in group.Rows)
            {
                bool earned = record.IsUnlocked(def.Id);
                if (earned) unlocked++;
                _rows.AddChild(BuildRow(def, earned, record.ProgressFor(def.Id)));
            }
        }

        _headerCount.Text = Strings.Get(
            StringKeys.AchieveHeaderCount,
            ("unlocked", unlocked.ToString()),
            ("total", AchievementCatalog.All.Count.ToString()));
    }

    /// <summary>Section header: category name over a gold rule.</summary>
    private static Control BuildCategoryHeader(string title)
    {
        var header = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        header.AddThemeConstantOverride("separation", 2);
        var label = new Label { Text = title };
        label.AddThemeFontSizeOverride("font_size", 22);
        label.AddThemeColorOverride("font_color", UiPalette.InkSoft);
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        header.AddChild(label);
        header.AddChild(ModalChrome.GoldRule());
        return header;
    }

    /// <summary>
    /// One row: title + description on the left, earned state or counter
    /// progress on the right. An earned row is inked gold and outlined;
    /// a locked one stays muted.
    /// </summary>
    private static Control BuildRow(AchievementDefinition def, bool earned, int progress)
    {
        var row = new PanelContainer
        {
            CustomMinimumSize = new Vector2(0f, RowMinHeight),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        row.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = UiPalette.BgRow,
            BorderColor = earned ? UiPalette.Gold : UiPalette.Line,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8,
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 8,
            ContentMarginBottom = 8,
        });

        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 12);
        row.AddChild(line);

        var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", 2);
        line.AddChild(text);

        var name = new Label
        {
            Text = Strings.Get(AchievementDisplay.TitleKeyFor(def, earned)),
        };
        name.AddThemeFontSizeOverride("font_size", 22);
        name.AddThemeColorOverride("font_color", earned ? UiPalette.Gold : UiPalette.Ink);
        text.AddChild(name);

        var desc = new Label
        {
            Text = Strings.Get(AchievementDisplay.DescriptionKeyFor(def, earned)),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        desc.AddThemeFontSizeOverride("font_size", 17);
        desc.AddThemeColorOverride("font_color", UiPalette.InkMute);
        text.AddChild(desc);

        var state = new Label
        {
            Text = StateTextFor(def, earned, progress),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        state.AddThemeFontSizeOverride("font_size", 18);
        state.AddThemeColorOverride("font_color", earned ? UiPalette.Gold : UiPalette.InkMute);
        line.AddChild(state);

        return row;
    }

    /// <summary>Earned, a counter's progress toward its target, or locked.</summary>
    private static string StateTextFor(AchievementDefinition def, bool earned, int progress)
    {
        if (earned) return Strings.Get(StringKeys.AchieveEarned);
        if (!def.IsCounter) return Strings.Get(StringKeys.AchieveLocked);
        return Strings.Get(
            StringKeys.AchieveProgress,
            ("current", progress.ToString()),
            ("target", def.Target.ToString()));
    }

    /// <summary>Fit to the safe viewport at a constant width and font size,
    /// capping height so the list scrolls further rather than shrinking —
    /// same rationale as <see cref="CreditsPanel.FitPanel"/>.</summary>
    private void FitPanel()
    {
        Vector2 vp = GetViewport().GetVisibleRect().Size;
        LogicalSafeInsets safe = SafeArea.Current;
        (float availW, float availH) = PanelFitMath.AvailableBox(vp.X, vp.Y, safe, ViewportMargin);

        (float scale, float panelH) =
            PanelFitMath.WidthFitWithHeightCap(DesignWidth, DesignHeight, availW, availH);
        _panel.OffsetTop = -panelH * 0.5f;
        _panel.OffsetBottom = panelH * 0.5f;

        _panel.PivotOffset = new Vector2(DesignWidth, panelH) * 0.5f;
        _panel.Scale = new Vector2(scale, scale);

        Log.Debug(Log.LogCategory.Render,
            $"AchievementsPanel: fit viewport={vp.X:0}x{vp.Y:0} " +
            $"safe=(t{safe.Top:0},b{safe.Bottom:0},l{safe.Left:0},r{safe.Right:0}) " +
            $"scale={scale:0.00} panelH={panelH:0}");
    }

    public void Open()
    {
        if (IsOpen) return;
        Rebuild();
        FitPanel();
        IsOpen = true;
        Visible = true;
        Log.Debug(Log.LogCategory.Achieve,
            $"[panel] open — {_headerCount.Text}");
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        Visible = false;
        Log.Debug(Log.LogCategory.Achieve, "[panel] close");
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
