using System;
using Godot;

/// <summary>
/// Top strip for the TutorialBuilder scene. 60px tall, anchored to the
/// top of the viewport. Hosts the 3-mode segmented control (Map Edit /
/// Build / Preview) on the left and the Save Tutorial / Load Tutorial /
/// Exit buttons on the right. Emits events for every interaction; owns
/// only the visual "current mode" indication (the current mode's button
/// is rendered Disabled = true).
///
/// Phase 2 leaves Save Tutorial and Load Tutorial disabled (the button
/// plumbing arrives in Phase 3 with the Tutorial POCO + SaveStore
/// extensions). Setting <see cref="SaveEnabled"/> / <see cref="LoadEnabled"/>
/// to true at any time after _Ready re-enables those buttons.
/// </summary>
public sealed partial class TutorialBuilderTopBar : CanvasLayer
{
    public event Action<TutorialMode>? ModeRequested;
    public event Action? SaveTutorialPressed;
    public event Action? LoadTutorialPressed;
    public event Action? ExitPressed;

    public bool SaveEnabled
    {
        get => _saveEnabled;
        set
        {
            _saveEnabled = value;
            if (_saveButton != null) _saveButton.Disabled = !value;
        }
    }
    private bool _saveEnabled;

    public bool LoadEnabled
    {
        get => _loadEnabled;
        set
        {
            _loadEnabled = value;
            if (_loadButton != null) _loadButton.Disabled = !value;
        }
    }
    private bool _loadEnabled;

    private Button _mapEditButton = null!;
    private Button _buildButton = null!;
    private Button _previewButton = null!;
    private Button _saveButton = null!;
    private Button _loadButton = null!;

    private TutorialMode _currentMode = TutorialMode.MapEdit;

    public override void _Ready()
    {
        Vector2 viewport = GetViewport().GetVisibleRect().Size;

        // Same dark-strip styling and height as MapEditorHudView so the
        // two stacked HUDs read as a single 120px chrome zone.
        var background = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.85f),
            Position = Vector2.Zero,
            Size = new Vector2(viewport.X, HudView.HudHeight),
        };
        AddChild(background);

        // Left cluster: 3 mode buttons.
        var leftHbox = new HBoxContainer
        {
            Position = new Vector2(16, 12),
        };
        leftHbox.AddThemeConstantOverride("separation", 8);
        AddChild(leftHbox);

        _mapEditButton = MakeModeButton("Map Edit  (1)", TutorialMode.MapEdit);
        leftHbox.AddChild(_mapEditButton);
        _buildButton = MakeModeButton("Record  (2)", TutorialMode.Record);
        leftHbox.AddChild(_buildButton);
        _previewButton = MakeModeButton("Preview  (3)", TutorialMode.Preview);
        leftHbox.AddChild(_previewButton);

        // Right cluster: Save Tutorial / Load Tutorial / Exit.
        var rightHbox = new HBoxContainer
        {
            AnchorLeft = 0f,
            AnchorRight = 1f,
            AnchorTop = 0f,
            AnchorBottom = 0f,
            OffsetLeft = 0f,
            OffsetRight = -16f,
            OffsetTop = 12f,
            OffsetBottom = 48f,
            Alignment = BoxContainer.AlignmentMode.End,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        rightHbox.AddThemeConstantOverride("separation", 8);
        AddChild(rightHbox);

        _saveButton = new Button
        {
            Text = "Save Tutorial",
            FocusMode = Control.FocusModeEnum.None,
            Disabled = !_saveEnabled,
        };
        _saveButton.AddThemeFontSizeOverride("font_size", 18);
        _saveButton.Pressed += () => SaveTutorialPressed?.Invoke();
        AudioBus.AttachClick(_saveButton);
        rightHbox.AddChild(_saveButton);

        _loadButton = new Button
        {
            Text = "Load Tutorial",
            FocusMode = Control.FocusModeEnum.None,
            Disabled = !_loadEnabled,
        };
        _loadButton.AddThemeFontSizeOverride("font_size", 18);
        _loadButton.Pressed += () => LoadTutorialPressed?.Invoke();
        AudioBus.AttachClick(_loadButton);
        rightHbox.AddChild(_loadButton);

        var exitButton = new Button
        {
            Text = "Exit",
            FocusMode = Control.FocusModeEnum.None,
        };
        exitButton.AddThemeFontSizeOverride("font_size", 18);
        exitButton.Pressed += () => ExitPressed?.Invoke();
        AudioBus.AttachClick(exitButton);
        rightHbox.AddChild(exitButton);

        SetCurrentMode(_currentMode);
    }

    /// <summary>
    /// Update the visual "current mode" indication. The current mode's
    /// button is Disabled = true (greyed out, not clickable — clicking
    /// the already-current mode is a no-op anyway). Other mode buttons
    /// re-enable.
    /// </summary>
    public void SetCurrentMode(TutorialMode mode)
    {
        _currentMode = mode;
        if (_mapEditButton == null) return;
        _mapEditButton.Disabled = mode == TutorialMode.MapEdit;
        _buildButton.Disabled   = mode == TutorialMode.Record;
        _previewButton.Disabled = mode == TutorialMode.Preview;
    }

    private Button MakeModeButton(string label, TutorialMode mode)
    {
        var b = new Button
        {
            Text = label,
            FocusMode = Control.FocusModeEnum.None,
        };
        b.AddThemeFontSizeOverride("font_size", 18);
        b.Pressed += () => ModeRequested?.Invoke(mode);
        AudioBus.AttachClick(b);
        return b;
    }
}
