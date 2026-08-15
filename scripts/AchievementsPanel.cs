// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Achievements modal — backdrop + centered panel listing every catalog
/// entry with its earned state, opened from the main menu. Orientation-
/// split like <see cref="SettingsPanel"/>: portrait is a fixed-width
/// centered column that fills the safe height (title, count, gold rule,
/// scroll body, Back); landscape reflows into a wide
/// <see cref="LandscapeMenuChrome"/> surface with an inline title row and
/// a two-column scroll body, groups balanced by
/// <see cref="AchievementPanelLayout.SplitTwoColumns"/>. A
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
    // Node RebuildBody frees on an orientation flip (currently == _panel).
    private Node _panelRoot = null!;
    private Label _headerCount = null!;
    // Portrait single column; null while the landscape body is built.
    private VBoxContainer? _rows;
    // Landscape columns; null while the portrait body is built.
    private VBoxContainer? _colLeft;
    private VBoxContainer? _colRight;
    private ScreenOrientation _orientation;
    private bool _viewportResizeHooked;

    private static readonly Font _serifFont =
        GD.Load<FontFile>("res://fonts/DMSerifDisplay-Regular.ttf");

    // Matches the Settings / Credits width so switching between modals
    // doesn't jump the frame; portrait height fills the safe viewport.
    private const float DesignWidth = 456f;
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

        BuildBody();

        GetViewport().SizeChanged += OnViewportResized;
        _viewportResizeHooked = true;
        SafeArea.Changed += OnSafeAreaChanged;
    }

    public override void _ExitTree()
    {
        SafeArea.Changed -= OnSafeAreaChanged;
        if (!_viewportResizeHooked) return;
        GetViewport().SizeChanged -= OnViewportResized;
        _viewportResizeHooked = false;
    }

    private void OnSafeAreaChanged(LogicalSafeInsets _) => RefitOrRelayout();

    private void OnViewportResized()
    {
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        ScreenOrientation next = ScreenLayout.Resolve(viewport.X, viewport.Y);
        if (next != _orientation) { RebuildBody(); return; }
        RefitOrRelayout();
    }

    /// <summary>Portrait scales/fills the fixed-width panel; landscape
    /// re-centers and re-caps the fill surface.</summary>
    private void RefitOrRelayout()
    {
        if (_orientation == ScreenOrientation.Portrait) FitPanel();
        else LandscapeMenuChrome.ApplyLayout(
            _panel, GetViewport().GetVisibleRect().Size, SafeArea.Current);
    }

    /// <summary>Build the panel subtree for the current orientation.</summary>
    private void BuildBody()
    {
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        _orientation = ScreenLayout.Resolve(viewport.X, viewport.Y);
        if (_orientation == ScreenOrientation.Landscape) BuildLandscapeBody();
        else BuildPortraitBody();
        Log.Info(Log.LogCategory.Render,
            $"AchievementsPanel: built {_orientation} body " +
            $"(viewport {viewport.X:0}x{viewport.Y:0})");
    }

    /// <summary>Free the current body and rebuild it for the new orientation
    /// (mirrors SettingsPanel). Rows re-populate from AchievementStore, so a
    /// rebuild while open loses nothing.</summary>
    private void RebuildBody()
    {
        Log.Debug(Log.LogCategory.Render,
            $"AchievementsPanel: orientation flip from {_orientation}; rebuilding body");
        _panelRoot.QueueFree();
        BuildBody();
        if (IsOpen) Rebuild();
    }

    /// <summary>Single-column portrait layout: fixed-width centered panel
    /// whose height fills the safe viewport (FitPanel).</summary>
    private void BuildPortraitBody()
    {
        _colLeft = null;
        _colRight = null;

        _panel = ModalChrome.BuildCenteredPanel(DesignWidth, 0f);
        _panelRoot = _panel;
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

        vbox.AddChild(MakeBackButton());

        FitPanel();
    }

    /// <summary>Landscape layout: wide LandscapeMenuChrome surface — inline
    /// title row (title, count, gold rule), a scroller holding two balanced
    /// columns of category sections, and a full-width Back footer.</summary>
    private void BuildLandscapeBody()
    {
        _rows = null;

        _panel = LandscapeMenuChrome.Build();
        _panelRoot = _panel;
        AddChild(_panel);

        var outer = new VBoxContainer();
        outer.AddThemeConstantOverride("separation", 14);
        _panel.AddChild(outer);

        // Title row: serif title + count + an expanding gold rule.
        var titleRow = new HBoxContainer();
        titleRow.AddThemeConstantOverride("separation", 18);
        var title = new Label { Text = Strings.Get(StringKeys.AchieveTitle) };
        title.AddThemeFontOverride("font", _serifFont);
        title.AddThemeFontSizeOverride("font_size", 36);
        title.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        titleRow.AddChild(title);
        _headerCount = new Label { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        _headerCount.AddThemeFontSizeOverride("font_size", 20);
        _headerCount.AddThemeColorOverride("font_color", UiPalette.InkSoft);
        titleRow.AddChild(_headerCount);
        titleRow.AddChild(new ColorRect
        {
            Color = UiPalette.GoldDim,
            CustomMinimumSize = new Vector2(0, 2),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        });
        outer.AddChild(titleRow);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        outer.AddChild(scroll);

        var columns = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        columns.AddThemeConstantOverride("separation", 24);
        scroll.AddChild(columns);

        _colLeft = MakeColumn();
        columns.AddChild(_colLeft);
        _colRight = MakeColumn();
        columns.AddChild(_colRight);

        outer.AddChild(MakeBackButton());

        LandscapeMenuChrome.ApplyLayout(
            _panel, GetViewport().GetVisibleRect().Size, SafeArea.Current);
    }

    private static VBoxContainer MakeColumn()
    {
        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            // Ignore so a touch-drag in the gaps between rows reaches the
            // scroller instead of stopping here.
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        column.AddThemeConstantOverride("separation", 8);
        return column;
    }

    private Button MakeBackButton()
    {
        var backButton = new Button
        {
            Text = Strings.Get(StringKeys.MenuBack),
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        backButton.AddThemeFontSizeOverride("font_size", 24);
        backButton.Pressed += Close;
        AudioBus.AttachClick(backButton);
        return backButton;
    }

    /// <summary>Rebuild the category sections + rows from the current
    /// record into whichever body is built. Grouping comes from
    /// <see cref="AchievementPanelLayout"/> (Controller, unit-tested);
    /// this only renders it.</summary>
    private void Rebuild()
    {
        AchievementRecord record = AchievementStore.Record;
        IReadOnlyList<AchievementPanelLayout.Group> groups = AchievementPanelLayout.Groups();
        int unlocked;
        if (_orientation == ScreenOrientation.Landscape)
        {
            var (left, right) = AchievementPanelLayout.SplitTwoColumns(groups);
            unlocked = PopulateColumn(_colLeft!, left, record)
                + PopulateColumn(_colRight!, right, record);
            Log.Debug(Log.LogCategory.Render,
                "AchievementsPanel: landscape split " +
                $"left={SumRows(left)} rows right={SumRows(right)} rows");
        }
        else
        {
            unlocked = PopulateColumn(_rows!, groups, record);
        }

        _headerCount.Text = Strings.Get(
            StringKeys.AchieveHeaderCount,
            ("unlocked", unlocked.ToString()),
            ("total", AchievementCatalog.All.Count.ToString()));
    }

    private static int SumRows(IReadOnlyList<AchievementPanelLayout.Group> groups)
    {
        int rows = 0;
        foreach (AchievementPanelLayout.Group group in groups) rows += group.Rows.Count;
        return rows;
    }

    /// <summary>Clear <paramref name="into"/> and fill it with the given
    /// groups' headers + rows; returns how many of them are unlocked.</summary>
    private static int PopulateColumn(
        VBoxContainer into,
        IReadOnlyList<AchievementPanelLayout.Group> groups,
        AchievementRecord record)
    {
        foreach (Node child in into.GetChildren()) child.QueueFree();

        int unlocked = 0;
        foreach (AchievementPanelLayout.Group group in groups)
        {
            into.AddChild(BuildCategoryHeader(Strings.Get(group.TitleKey)));
            foreach (AchievementDefinition def in group.Rows)
            {
                bool earned = record.IsUnlocked(def.Id);
                if (earned) unlocked++;
                into.AddChild(BuildRow(def, earned, record.ProgressFor(def.Id)));
            }
        }
        return unlocked;
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

    /// <summary>Portrait fit: constant width and font size, height filling
    /// the safe viewport so the list shows as many rows as the screen
    /// allows (issue #240's taller-portrait goal).</summary>
    private void FitPanel()
    {
        Vector2 vp = GetViewport().GetVisibleRect().Size;
        LogicalSafeInsets safe = SafeArea.Current;
        (float availW, float availH) = PanelFitMath.AvailableBox(vp.X, vp.Y, safe, ViewportMargin);

        (float scale, float panelH) =
            PanelFitMath.WidthFitWithHeightCap(DesignWidth, float.MaxValue, availW, availH);
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
        RefitOrRelayout();
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
