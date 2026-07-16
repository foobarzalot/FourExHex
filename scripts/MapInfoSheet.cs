// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// A "play this board?" confirm sheet: a dim backdrop plus a
/// centered dialog with a serif title, gold rule, status line, a "who you're
/// playing as" block, a large live <see cref="MapThumbnailView"/> preview, and
/// Cancel / confirm buttons. Generalized from the campaign confirm sheet so it
/// serves both the campaign ladder (one guaranteed human) and the New Game /
/// Map Editor "load starting map" flows, where a map can have multiple human
/// players, or none.
///
/// The caller supplies the title, an optional status line, the list of human
/// identities to surface, and a thumbnail-request delegate (the sheet owns no
/// knowledge of seeds vs. saved maps — campaign passes a procedural request,
/// the load flow a saved-map request). Layout/chrome mirror the New Game
/// map-config screen via <see cref="LandscapeMenuChrome"/>: fills the safe area
/// on a phone, caps to 920×520 (transposed in portrait) on desktop; an
/// orientation flip rebuilds the body and re-renders.
/// </summary>
public sealed partial class MapInfoSheet : CanvasLayer
{
    public event Action? Confirmed;
    public event Action? Canceled;

    public bool IsOpen { get; private set; }

    /// <summary>One human player to surface in the "playing as" block.</summary>
    public readonly record struct HumanIdentity(string Name, Color Color);

    private const float MaxLong = 920f;
    private const float MaxShort = 520f;

    private static readonly Font SerifFont =
        GD.Load<FontFile>("res://fonts/DMSerifDisplay-Regular.ttf");

    private readonly string _title;
    private readonly string _status;
    private readonly IReadOnlyList<HumanIdentity> _humans;
    private readonly string _confirmText;
    // Optional game-mode line: the campaign confirm sheet sets this
    // to tell the player which mode the level plays in; _gameModeEmphasis golds
    // the Rising Tides callout. Empty = no row (other callers unchanged).
    private readonly string _gameMode;
    private readonly bool _gameModeEmphasis;
    // Configure + kick off the preview render on the given thumbnail. Invoked on
    // Open and after an orientation rebuild (the thumbnail is recreated each
    // body build). Campaign passes RequestRandom(seed, opts); the load flow
    // wires the SaveStore and calls RequestMap(name).
    private readonly Action<MapThumbnailView> _requestThumbnail;

    private PanelContainer _surface = null!;
    private BoxContainer _body = null!;
    private MapThumbnailView _thumbnail = null!;
    private ScreenOrientation _orientation;
    private bool _resizeHooked;

    public MapInfoSheet(
        string title,
        string status,
        IReadOnlyList<HumanIdentity> humans,
        Action<MapThumbnailView> requestThumbnail,
        string? confirmText = null,
        string gameMode = "",
        bool gameModeEmphasis = false)
    {
        _title = title;
        _status = status;
        _humans = humans;
        _requestThumbnail = requestThumbnail;
        _confirmText = confirmText ?? Strings.Get(StringKeys.ButtonPlay);
        _gameMode = gameMode;
        _gameModeEmphasis = gameModeEmphasis;
    }

    public override void _Ready()
    {
        Layer = 100;
        Visible = false;
        ProcessMode = ProcessModeEnum.Always;

        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        _orientation = ScreenLayout.Resolve(viewport.X, viewport.Y);
        AddChild(ModalChrome.BuildBackdrop(viewport));

        _surface = LandscapeMenuChrome.Build();
        AddChild(_surface);
        BuildBody();

        GetViewport().SizeChanged += OnViewportResized;
        SafeArea.Changed += OnSafeAreaChanged;
        _resizeHooked = true;
        ApplyLayout();
    }

    public override void _ExitTree()
    {
        SafeArea.Changed -= OnSafeAreaChanged;
        if (!_resizeHooked) return;
        GetViewport().SizeChanged -= OnViewportResized;
        _resizeHooked = false;
    }

    private void BuildBody()
    {
        if (_orientation == ScreenOrientation.Portrait) BuildPortraitBody();
        else BuildLandscapeBody();
    }

    private void BuildPortraitBody()
    {
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 14);
        _surface.AddChild(col);
        _body = col;

        col.AddChild(MakeTitle(HorizontalAlignment.Center, 36));
        col.AddChild(MakeGoldRule(Control.SizeFlags.ShrinkCenter));
        if (_status.Length > 0) col.AddChild(MakeStatus(HorizontalAlignment.Center));
        if (_gameMode.Length > 0) col.AddChild(MakeGameMode(HorizontalAlignment.Center));
        col.AddChild(MakePlayingAs(HorizontalAlignment.Center));
        _thumbnail = MakeThumbnail();
        col.AddChild(_thumbnail);

        var buttonRow = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        buttonRow.AddThemeConstantOverride("separation", 12);
        col.AddChild(buttonRow);
        buttonRow.AddChild(MakeSheetButton(Strings.Get(StringKeys.ButtonCancel), Cancel));
        buttonRow.AddChild(MakeSheetButton(_confirmText, Confirm));
    }

    private void BuildLandscapeBody()
    {
        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 20);
        _surface.AddChild(hbox);
        _body = hbox;

        var rail = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(240, 0),
            SizeFlagsVertical = Control.SizeFlags.Fill,
        };
        rail.AddThemeConstantOverride("separation", 10);
        hbox.AddChild(rail);

        rail.AddChild(MakeTitle(HorizontalAlignment.Left, 32));
        rail.AddChild(MakeGoldRule(Control.SizeFlags.ShrinkBegin));
        if (_status.Length > 0) rail.AddChild(MakeStatus(HorizontalAlignment.Left));
        if (_gameMode.Length > 0) rail.AddChild(MakeGameMode(HorizontalAlignment.Left));
        rail.AddChild(MakePlayingAs(HorizontalAlignment.Left));
        rail.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        rail.AddChild(MakeSheetButton(Strings.Get(StringKeys.ButtonCancel), Cancel));
        rail.AddChild(MakeSheetButton(_confirmText, Confirm));

        hbox.AddChild(new ColorRect
        {
            Color = UiPalette.LineSoft,
            CustomMinimumSize = new Vector2(1, 0),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        });

        _thumbnail = MakeThumbnail();
        hbox.AddChild(_thumbnail);
    }

    private Label MakeTitle(HorizontalAlignment align, int fontSize)
    {
        var title = new Label { Text = _title, HorizontalAlignment = align };
        title.AddThemeFontOverride("font", SerifFont);
        title.AddThemeFontSizeOverride("font_size", fontSize);
        return title;
    }

    private static ColorRect MakeGoldRule(Control.SizeFlags horizontal) => new()
    {
        Color = UiPalette.GoldDim,
        CustomMinimumSize = new Vector2(200, 1),
        SizeFlagsHorizontal = horizontal,
    };

    private Label MakeStatus(HorizontalAlignment align)
    {
        var status = new Label
        {
            Text = _status,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = align,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        status.AddThemeFontSizeOverride("font_size", 22);
        status.AddThemeColorOverride("font_color", UiPalette.InkSoft);
        return status;
    }

    /// <summary>The game-mode line. Same wrapped style as the status
    /// row, but golded when it's the Rising Tides callout so it reads as a
    /// distinct, important note rather than ordinary metadata.</summary>
    private Label MakeGameMode(HorizontalAlignment align)
    {
        var mode = new Label
        {
            Text = _gameMode,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = align,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        mode.AddThemeFontSizeOverride("font_size", 22);
        mode.AddThemeColorOverride("font_color",
            _gameModeEmphasis ? UiPalette.Gold : UiPalette.InkSoft);
        return mode;
    }

    /// <summary>The "who you're playing as" block. Zero humans → a plain
    /// all-Computer note; exactly one → the campaign's tinted sentence (kept
    /// pixel-identical); two or more → a "You will be playing as:" lead-in over
    /// a wrapping row of color-swatch + name chips, one per human.</summary>
    private Control MakePlayingAs(HorizontalAlignment align)
    {
        if (_humans.Count == 0)
        {
            var none = new Label
            {
                Text = Strings.Get(StringKeys.MapInfoAllComputer),
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                HorizontalAlignment = align,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            none.AddThemeFontSizeOverride("font_size", 22);
            none.AddThemeColorOverride("font_color", UiPalette.InkSoft);
            return none;
        }

        if (_humans.Count == 1)
        {
            HumanIdentity h = _humans[0];
            var label = new Label
            {
                Text = Strings.Get(StringKeys.MapInfoPlayingAs, ("name", h.Name)),
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                HorizontalAlignment = align,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            label.AddThemeFontSizeOverride("font_size", 22);
            label.AddThemeColorOverride("font_color", h.Color);
            return label;
        }

        var col = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 6);
        var lead = new Label
        {
            Text = Strings.Get(StringKeys.MapInfoPlayingAsHeading),
            HorizontalAlignment = align,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        lead.AddThemeFontSizeOverride("font_size", 22);
        lead.AddThemeColorOverride("font_color", UiPalette.InkSoft);
        col.AddChild(lead);

        var chips = new HBoxContainer();
        chips.AddThemeConstantOverride("separation", 12);
        chips.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        if (align == HorizontalAlignment.Center)
            chips.Alignment = BoxContainer.AlignmentMode.Center;
        foreach (HumanIdentity h in _humans)
        {
            var chip = new HBoxContainer();
            chip.AddThemeConstantOverride("separation", 6);
            chip.AddChild(new ColorRect
            {
                Color = h.Color,
                CustomMinimumSize = new Vector2(20, 20),
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            });
            var name = new Label { Text = h.Name, VerticalAlignment = VerticalAlignment.Center };
            name.AddThemeFontSizeOverride("font_size", 22);
            name.AddThemeColorOverride("font_color", h.Color);
            chip.AddChild(name);
            chips.AddChild(chip);
        }
        col.AddChild(chips);
        return col;
    }

    private static MapThumbnailView MakeThumbnail() => new()
    {
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        SizeFlagsVertical = Control.SizeFlags.ExpandFill,
    };

    private static Button MakeSheetButton(string text, Action onPressed)
    {
        var button = new Button
        {
            Text = text,
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        button.AddThemeFontSizeOverride("font_size", 24);
        button.CustomMinimumSize = new Vector2(0, 52);
        button.Pressed += onPressed;
        AudioBus.AttachClick(button);
        return button;
    }

    private void ApplyLayout()
    {
        Vector2 vp = GetViewport().GetVisibleRect().Size;
        bool portrait = ScreenLayout.Resolve(vp.X, vp.Y) == ScreenOrientation.Portrait;
        LandscapeMenuChrome.ApplyLayout(_surface, vp, SafeArea.Current,
            maxW: portrait ? MaxShort : MaxLong,
            maxH: portrait ? MaxLong : MaxShort);
    }

    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        Visible = true;
        _requestThumbnail(_thumbnail);
        Log.Debug(Log.LogCategory.Display,
            $"MapInfoSheet.Open \"{_title}\" humans={_humans.Count} orient={_orientation}");
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        Visible = false;
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

    /// <summary>
    /// Close exactly as if the user pressed Escape on the open sheet:
    /// hide and fire <see cref="Canceled"/> so the owner's teardown
    /// (null-out + QueueFree) runs. Used by the Android system-back
    /// ladder. No-op when not open.
    /// </summary>
    public void CloseAsCancel()
    {
        if (!IsOpen) return;
        Cancel();
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

    private void OnViewportResized()
    {
        Vector2 vp = GetViewport().GetVisibleRect().Size;
        ScreenOrientation next = ScreenLayout.Resolve(vp.X, vp.Y);
        if (next != _orientation)
        {
            _orientation = next;
            _body.QueueFree();
            BuildBody();
            if (IsOpen) _requestThumbnail(_thumbnail);
        }
        ApplyLayout();
    }

    private void OnSafeAreaChanged(LogicalSafeInsets s) => ApplyLayout();
}
