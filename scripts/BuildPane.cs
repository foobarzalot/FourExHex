using System.Collections.Generic;
using Godot;

/// <summary>
/// Build mode chrome. Phase 3b: right strip with "Add EndTurn" button +
/// selected-beat inspector, plus bottom timeline of beat chips.
/// Phase 4+ extends with more beat-add buttons (BuyPeasant / Move /
/// BuildTower); Phase 7+ adds overlay-beat editors (Prompt / Highlight
/// / CameraFocus); Phase 11 adds editing / reorder / delete; Phase 12
/// adds validation banners.
///
/// In-memory <see cref="Tutorial"/> ownership lives here for the
/// session. <see cref="TutorialBuilderScene"/> reads it via
/// <see cref="CurrentTutorial"/> on save and writes it via
/// <see cref="SetTutorial"/> on load.
/// </summary>
public sealed partial class BuildPane : Control
{
    private const float TopbarHeight = 60f;          // matches HudView.HudHeight
    private const float TimelineHeight = 80f;
    private const float RightPanelWidth = 240f;

    private MapEditorPanel _panel = null!;
    private Tutorial _tutorial = new Tutorial();
    private int _selectedBeatIndex = -1;             // -1 = none

    private HBoxContainer _timelineHbox = null!;
    private VBoxContainer _inspectorBox = null!;
    private Label _inspectorTurn = null!;
    private Label _inspectorActor = null!;

    /// <summary>Current authored Tutorial. Read by save flow.</summary>
    public Tutorial CurrentTutorial => _tutorial;

    /// <summary>Replace the in-memory Tutorial (used by load flow).</summary>
    public void SetTutorial(Tutorial tutorial)
    {
        _tutorial = tutorial;
        _selectedBeatIndex = -1;
        if (IsInsideTree()) RefreshUI();
    }

    /// <summary>Called once by TutorialBuilderScene before AddChild. Phase 3b
    /// stores the reference for future use (Phase 11 state-after-beat-N
    /// cache); doesn't read it yet.</summary>
    public void SetPanel(MapEditorPanel panel)
    {
        _panel = panel;
        _ = _panel; // suppress "unused" until Phase 11 consumes it
    }

    public override void _Ready()
    {
        // Root: full-viewport, click-pass-through (children opt in to
        // clicks via MouseFilter = Stop on the strip / timeline).
        // A Control direct child of a Node2D does NOT auto-fill the
        // viewport (parent has no rect size to anchor against), so we
        // set Size explicitly. Subscribe to viewport resize so the
        // layout reflows when the window changes.
        AnchorLeft = 0f;
        AnchorTop = 0f;
        AnchorRight = 1f;
        AnchorBottom = 1f;
        MouseFilter = MouseFilterEnum.Ignore;
        Size = GetViewport().GetVisibleRect().Size;
        GetViewport().SizeChanged += OnViewportResized;

        BuildRightStrip();
        BuildTimeline();
        RefreshUI();
    }

    private void OnViewportResized()
    {
        Size = GetViewport().GetVisibleRect().Size;
    }

    private void BuildRightStrip()
    {
        // Strip extends full height to the viewport bottom (not stopping
        // above the timeline) so the bottom-right corner is covered. The
        // timeline's OffsetRight = -RightPanelWidth keeps it from
        // overlapping the strip.
        var strip = new Control
        {
            AnchorLeft = 1f,
            AnchorRight = 1f,
            AnchorTop = 0f,
            AnchorBottom = 1f,
            OffsetLeft = -RightPanelWidth,
            OffsetRight = 0f,
            OffsetTop = TopbarHeight,
            OffsetBottom = 0f,
            MouseFilter = MouseFilterEnum.Stop,
        };
        AddChild(strip);

        var bg = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.85f),
            AnchorLeft = 0f,
            AnchorRight = 1f,
            AnchorTop = 0f,
            AnchorBottom = 1f,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        strip.AddChild(bg);

        var content = new VBoxContainer
        {
            AnchorLeft = 0f,
            AnchorRight = 1f,
            AnchorTop = 0f,
            AnchorBottom = 1f,
            OffsetLeft = 12f,
            OffsetRight = -12f,
            OffsetTop = 12f,
            OffsetBottom = -12f,
        };
        content.AddThemeConstantOverride("separation", 12);
        strip.AddChild(content);

        var addEndTurnBtn = new Button
        {
            Text = "Add EndTurn",
            FocusMode = FocusModeEnum.None,
        };
        addEndTurnBtn.AddThemeFontSizeOverride("font_size", 18);
        addEndTurnBtn.Pressed += OnAddEndTurnPressed;
        AudioBus.AttachClick(addEndTurnBtn);
        content.AddChild(addEndTurnBtn);

        // Spacer expands to fill the gap so the inspector pins to the
        // bottom of the strip — separates the "add a beat" action area
        // (top) from the "selected-beat data" area (bottom).
        var spacer = new Control
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        content.AddChild(spacer);

        // Inspector: shown only when a beat is selected.
        _inspectorBox = new VBoxContainer { Visible = false };
        _inspectorBox.AddThemeConstantOverride("separation", 4);
        content.AddChild(_inspectorBox);

        var inspectorTitle = new Label
        {
            Text = "Selected beat",
        };
        inspectorTitle.AddThemeFontSizeOverride("font_size", 16);
        inspectorTitle.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.7f));
        _inspectorBox.AddChild(inspectorTitle);

        _inspectorTurn = new Label();
        _inspectorTurn.AddThemeFontSizeOverride("font_size", 14);
        _inspectorBox.AddChild(_inspectorTurn);

        _inspectorActor = new Label();
        _inspectorActor.AddThemeFontSizeOverride("font_size", 14);
        _inspectorBox.AddChild(_inspectorActor);
    }

    private void BuildTimeline()
    {
        var strip = new Control
        {
            AnchorLeft = 0f,
            AnchorRight = 1f,
            AnchorTop = 1f,
            AnchorBottom = 1f,
            OffsetLeft = 0f,
            OffsetRight = -RightPanelWidth,
            OffsetTop = -TimelineHeight,
            OffsetBottom = 0f,
            MouseFilter = MouseFilterEnum.Stop,
        };
        AddChild(strip);

        var bg = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.85f),
            AnchorLeft = 0f,
            AnchorRight = 1f,
            AnchorTop = 0f,
            AnchorBottom = 1f,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        strip.AddChild(bg);

        var scroll = new ScrollContainer
        {
            AnchorLeft = 0f,
            AnchorRight = 1f,
            AnchorTop = 0f,
            AnchorBottom = 1f,
            OffsetLeft = 12f,
            OffsetRight = -12f,
            OffsetTop = 12f,
            OffsetBottom = -12f,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
            VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        strip.AddChild(scroll);

        _timelineHbox = new HBoxContainer();
        _timelineHbox.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(_timelineHbox);
    }

    private void RefreshUI()
    {
        // Repopulate timeline.
        foreach (Node child in _timelineHbox.GetChildren())
        {
            child.QueueFree();
        }
        for (int i = 0; i < _tutorial.Beats.Count; i++)
        {
            int captured = i;
            Beat beat = _tutorial.Beats[i];
            string label = $"#{beat.Index} T{beat.Turn} A{beat.Actor} {beat.Kind}";
            var chip = new Button
            {
                Text = label,
                FocusMode = FocusModeEnum.None,
                Disabled = (i == _selectedBeatIndex),  // visual selection
            };
            chip.AddThemeFontSizeOverride("font_size", 14);
            chip.Pressed += () => OnBeatChipPressed(captured);
            AudioBus.AttachClick(chip);
            _timelineHbox.AddChild(chip);
        }

        // Update inspector.
        if (_selectedBeatIndex >= 0 && _selectedBeatIndex < _tutorial.Beats.Count)
        {
            Beat beat = _tutorial.Beats[_selectedBeatIndex];
            _inspectorTurn.Text = $"Turn: {beat.Turn}";
            _inspectorActor.Text = $"Actor: {beat.Actor}";
            _inspectorBox.Visible = true;
        }
        else
        {
            _inspectorBox.Visible = false;
        }
    }

    private void OnAddEndTurnPressed()
    {
        // Phase 3b hardcodes (Turn=1, Actor=0). Phase 10 introduces
        // the multi-turn lane state machine that picks the right
        // (Turn, Actor) based on the current authoring lane.
        var beats = new List<Beat>(_tutorial.Beats)
        {
            new EndTurnBeat
            {
                Index = _tutorial.Beats.Count,
                Turn = 1,
                Actor = 0,
            },
        };
        _tutorial = new Tutorial
        {
            Title = _tutorial.Title,
            StartTurn = _tutorial.StartTurn,
            StartPlayer = _tutorial.StartPlayer,
            Beats = beats,
        };
        RefreshUI();
    }

    private void OnBeatChipPressed(int index)
    {
        _selectedBeatIndex = (index == _selectedBeatIndex) ? -1 : index;
        RefreshUI();
    }
}
