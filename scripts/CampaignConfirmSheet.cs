using System;
using Godot;

/// <summary>
/// Campaign level "Play?" confirm sheet (issue #51): a dim backdrop over the
/// honeycomb plus a centered dialog with a serif title, gold rule, status line,
/// a large live <see cref="MapThumbnailView"/> preview of the level's board, and
/// Cancel / Play. Campaign maps are procedural (level N = seed N via
/// <see cref="CampaignProgress.SeedForLevel"/>), so the preview is the exact
/// board the level launches.
///
/// The dialog uses the same fill-to-cap surface as the New Game map-config
/// screen (<see cref="LandscapeMenuChrome"/>): it fills the safe area on a phone
/// — so the thumbnail is large and legible — but caps to the play-game dialog
/// footprint on desktop (920×520 landscape / 520×920 portrait). The thumbnail
/// ExpandFills the surface, so it stays big; <see cref="MapThumbnailView"/>
/// rotates the board −90° in portrait to match the in-game map. A flip while
/// open rebuilds the body for the new orientation and re-renders.
/// </summary>
public sealed partial class CampaignConfirmSheet : CanvasLayer
{
    public event Action? Confirmed;
    public event Action? Canceled;

    public bool IsOpen { get; private set; }

    // Dialog footprint cap on desktop — the 90° transpose pair the New Game
    // surface uses (LandscapeMenuChrome 920×520).
    private const float MaxLong = 920f;
    private const float MaxShort = 520f;

    private static readonly Font SerifFont =
        GD.Load<FontFile>("res://fonts/DMSerifDisplay-Regular.ttf");

    private readonly int _level;
    private readonly int _seed;
    private readonly string _title;
    private readonly string _status;
    private readonly string _playingAs;
    private readonly Color _playerColor;

    private PanelContainer _surface = null!;
    private BoxContainer _body = null!;
    private MapThumbnailView _thumbnail = null!;
    private ScreenOrientation _orientation;
    private bool _resizeHooked;

    public CampaignConfirmSheet(int level)
    {
        _level = level;
        _seed = CampaignProgress.SeedForLevel(level);
        _title = $"Level {CampaignProgress.LabelFor(level)}";
        string status = CampaignStore.Progress.StatusOf(level) switch
        {
            CampaignLevelStatus.Won => "Already won — replaying can't lose it.",
            CampaignLevelStatus.Lost => "Attempted, not yet won.",
            _ => "Not yet attempted.",
        };
        _status = $"{CampaignProgress.DifficultyForLevel(level)} tier · {status}";

        // Which color the human plays this level (issue #74): a deterministic
        // per-level slot, so the same level always hands the human the same
        // color. Surface it here so the player knows before launching.
        int humanSlot = CampaignProgress.HumanSlotForLevel(level, GameSettings.PlayerConfig.Length);
        (string colorName, string colorHex) = GameSettings.PlayerConfig[humanSlot];
        _playingAs = $"You will be playing as the {colorName} player.";
        _playerColor = new Color(colorHex);
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

    /// <summary>Build the dialog body for the current orientation: portrait is a
    /// single centered column (title · rule · status · big thumbnail · buttons);
    /// landscape mirrors the New Game map-config page — a left rail (title ·
    /// status · Play/Cancel) beside a large thumbnail — so the preview gets the
    /// surface's full height.</summary>
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
        col.AddChild(MakeStatus(HorizontalAlignment.Center));
        col.AddChild(MakePlayingAs(HorizontalAlignment.Center));
        _thumbnail = MakeThumbnail();
        col.AddChild(_thumbnail);

        var buttonRow = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        buttonRow.AddThemeConstantOverride("separation", 12);
        col.AddChild(buttonRow);
        buttonRow.AddChild(MakeSheetButton("Cancel", Cancel));
        buttonRow.AddChild(MakeSheetButton("Play", Confirm));
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
        rail.AddChild(MakeStatus(HorizontalAlignment.Left));
        rail.AddChild(MakePlayingAs(HorizontalAlignment.Left));
        rail.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        // Cancel above the forward action (Play) in the vertical rail.
        rail.AddChild(MakeSheetButton("Cancel", Cancel));
        rail.AddChild(MakeSheetButton("Play", Confirm));

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

    private Label MakePlayingAs(HorizontalAlignment align)
    {
        var label = new Label
        {
            Text = _playingAs,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = align,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        label.AddThemeFontSizeOverride("font_size", 22);
        // Tint the line with the player's own color so the color reads at a glance.
        label.AddThemeColorOverride("font_color", _playerColor);
        return label;
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

    /// <summary>Size + center the surface, filling the safe area up to the
    /// orientation-appropriate cap (same as the New Game map-config screen).</summary>
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
        // Preview the level's fixed terrain features (issue #48) — derived from
        // the level, not the freeform New Game toggles — so the preview matches
        // what the campaign game actually builds.
        _thumbnail.RequestRandom(_seed, CampaignProgress.MapGenOptionsForLevel(_level));
        Log.Debug(Log.LogCategory.Display,
            $"CampaignConfirmSheet.Open level={CampaignProgress.LabelFor(_level)} " +
            $"seed={_seed} orient={_orientation}");
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
            // Orientation flipped: rebuild the body and re-render the thumbnail
            // (its viewport aspect / board rotation is resolved at render time).
            _orientation = next;
            _body.QueueFree();
            BuildBody();
            if (IsOpen) _thumbnail.RequestRandom(_seed);
        }
        ApplyLayout();
    }

    private void OnSafeAreaChanged(LogicalSafeInsets s) => ApplyLayout();
}
