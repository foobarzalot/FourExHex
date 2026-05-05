using System;
using System.Linq;
using Godot;

/// <summary>
/// Minimal HUD for the map editor scene. Dark top strip with a seed entry
/// + Generate button (mirrors the main menu's seed control style) and an
/// Exit button on the right. Matches <see cref="HudView.HudHeight"/> so
/// the reserved-strip math in <see cref="HexMapView.VisualCenter"/> /
/// ClampPan continues to work unchanged.
///
/// Deliberately does NOT implement <see cref="IHudView"/> — that interface
/// is the play-scene controller contract and includes events the editor
/// has no use for.
/// </summary>
public partial class MapEditorHudView : CanvasLayer
{
    public const int SeedMin = 1;
    public const int SeedMax = 1000;

    /// <summary>Palette index reserved for the water swatch.</summary>
    public static int WaterPaletteIndex => GameSettings.PlayerConfig.Length;
    /// <summary>Palette index reserved for the tree-toggle swatch.</summary>
    public static int TreePaletteIndex => GameSettings.PlayerConfig.Length + 1;
    /// <summary>Palette index reserved for the capital-placement swatch.</summary>
    public static int CapitalPaletteIndex => GameSettings.PlayerConfig.Length + 2;
    /// <summary>Palette index reserved for the tower-toggle swatch.</summary>
    public static int TowerPaletteIndex => GameSettings.PlayerConfig.Length + 3;

    public event Action? ExitClicked;
    public event Action<int>? GenerateRequested;
    public event Action<int>? PaletteSelectionChanged;
    public event Action? UndoLastClicked;
    public event Action? UndoAllClicked;
    public event Action? RedoLastClicked;
    public event Action? RedoAllClicked;
    public event Action? SaveMapClicked;
    public event Action? LoadMapClicked;

    public int SelectedPaletteIndex { get; private set; }

    private LineEdit _seedField = null!;
    private Button _generateButton = null!;
    private HexPaletteButton[] _palette = null!;
    private Button _undoAllButton = null!;
    private Button _undoLastButton = null!;
    private Button _redoLastButton = null!;
    private Button _redoAllButton = null!;

    public override void _Ready()
    {
        Vector2 viewport = GetViewport().GetVisibleRect().Size;

        var background = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.8f),
            Position = Vector2.Zero,
            Size = new Vector2(viewport.X, HudView.HudHeight),
        };
        AddChild(background);

        var leftHbox = new HBoxContainer
        {
            Position = new Vector2(16, 12),
        };
        leftHbox.AddThemeConstantOverride("separation", 12);
        AddChild(leftHbox);

        var seedLabel = new Label { Text = "Seed" };
        seedLabel.AddThemeFontSizeOverride("font_size", 20);
        leftHbox.AddChild(seedLabel);

        _seedField = new LineEdit
        {
            CustomMinimumSize = new Vector2(80, 32),
            MaxLength = 4,
            Alignment = HorizontalAlignment.Right,
            Text = new System.Random().Next(SeedMin, SeedMax + 1).ToString(),
        };
        _seedField.TextChanged += OnSeedTextChanged;
        // Intercept Enter while the seed field has focus — without
        // GuiInput, the LineEdit consumes the key before _UnhandledInput
        // sees it, so the user has no way to fire Generate from the
        // keyboard with the field focused. Mirrors the seed field in
        // MainMenuScene.
        _seedField.GuiInput += OnSeedFieldGuiInput;
        leftHbox.AddChild(_seedField);

        _generateButton = new Button
        {
            Text = "Generate",
            FocusMode = Control.FocusModeEnum.None,
        };
        _generateButton.AddThemeFontSizeOverride("font_size", 18);
        _generateButton.Pressed += OnGeneratePressed;
        leftHbox.AddChild(_generateButton);

        // Palette sits in the same left HBox as the seed/Generate cluster
        // so all the editor controls flow left-to-right without overlap.
        var paletteHbox = new HBoxContainer();
        paletteHbox.AddThemeConstantOverride("separation", 6);
        leftHbox.AddChild(paletteHbox);

        _palette = new HexPaletteButton[GameSettings.PlayerConfig.Length + 4];
        for (int i = 0; i < GameSettings.PlayerConfig.Length; i++)
        {
            (_, string hex) = GameSettings.PlayerConfig[i];
            var button = new HexPaletteButton(new Color(hex));
            int captured = i;
            button.Pressed += _ => SelectPalette(captured);
            paletteHbox.AddChild(button);
            _palette[i] = button;
        }
        // Water swatch — same blue HexMapView.CreateWaterHexVisual uses.
        int waterIndex = WaterPaletteIndex;
        var waterButton = new HexPaletteButton(new Color(0.20f, 0.42f, 0.65f, 1f));
        waterButton.Pressed += _ => SelectPalette(waterIndex);
        paletteHbox.AddChild(waterButton);
        _palette[waterIndex] = waterButton;
        // Tree swatch — empty-land background with the tree icon overlaid.
        // The background color is a soft earthy tan that doesn't collide
        // with any of the player colors so the button reads as "tree on
        // generic land" rather than a seventh player color.
        int treeIndex = TreePaletteIndex;
        var treeButton = new HexPaletteButton(
            new Color(0.82f, 0.74f, 0.55f, 1f), HexPaletteIcon.Tree);
        treeButton.Pressed += _ => SelectPalette(treeIndex);
        paletteHbox.AddChild(treeButton);
        _palette[treeIndex] = treeButton;
        // Capital swatch — light slate background with the star icon
        // overlaid. The background is distinct from the tree's tan so the
        // two action-icon buttons read as different at a glance.
        int capitalIndex = CapitalPaletteIndex;
        var capitalButton = new HexPaletteButton(
            new Color(0.72f, 0.72f, 0.78f, 1f), HexPaletteIcon.Capital);
        capitalButton.Pressed += _ => SelectPalette(capitalIndex);
        paletteHbox.AddChild(capitalButton);
        _palette[capitalIndex] = capitalButton;
        // Tower swatch — dark stone-grey background with the rook icon
        // overlaid. Distinct from the lighter capital slate so the two
        // grey-ish buttons read as different at a glance.
        int towerIndex = TowerPaletteIndex;
        var towerButton = new HexPaletteButton(
            new Color(0.45f, 0.45f, 0.50f, 1f), HexPaletteIcon.Tower);
        towerButton.Pressed += _ => SelectPalette(towerIndex);
        paletteHbox.AddChild(towerButton);
        _palette[towerIndex] = towerButton;

        // Default selection: first land color (Red). Visual is set via
        // SelectPalette so the IsSelected outline draws from the start.
        SelectPalette(0, fireEvent: false);

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

        // Undo / redo cluster — same order and labels as the play HUD's
        // right strip so the muscle memory carries over.
        _undoAllButton = MakeUndoButton("Undo All", () => UndoAllClicked?.Invoke());
        rightHbox.AddChild(_undoAllButton);
        _undoLastButton = MakeUndoButton("Undo Last", () => UndoLastClicked?.Invoke());
        rightHbox.AddChild(_undoLastButton);
        _redoLastButton = MakeUndoButton("Redo Last", () => RedoLastClicked?.Invoke());
        rightHbox.AddChild(_redoLastButton);
        _redoAllButton = MakeUndoButton("Redo All", () => RedoAllClicked?.Invoke());
        rightHbox.AddChild(_redoAllButton);

        var saveMapButton = new Button
        {
            Text = "Save Map",
            FocusMode = Control.FocusModeEnum.None,
        };
        saveMapButton.AddThemeFontSizeOverride("font_size", 18);
        saveMapButton.Pressed += () => SaveMapClicked?.Invoke();
        rightHbox.AddChild(saveMapButton);

        var loadMapButton = new Button
        {
            Text = "Load Map",
            FocusMode = Control.FocusModeEnum.None,
        };
        loadMapButton.AddThemeFontSizeOverride("font_size", 18);
        loadMapButton.Pressed += () => LoadMapClicked?.Invoke();
        rightHbox.AddChild(loadMapButton);

        var exitButton = new Button
        {
            Text = "Exit",
            FocusMode = Control.FocusModeEnum.None,
        };
        exitButton.AddThemeFontSizeOverride("font_size", 18);
        exitButton.Pressed += () => ExitClicked?.Invoke();
        rightHbox.AddChild(exitButton);
    }

    private static Button MakeUndoButton(string text, Action onPressed)
    {
        var b = new Button
        {
            Text = text,
            Disabled = true,
            FocusMode = Control.FocusModeEnum.None,
        };
        b.AddThemeFontSizeOverride("font_size", 18);
        b.Pressed += () => onPressed();
        return b;
    }

    /// <summary>
    /// Refresh the disabled state of the four undo/redo buttons. Called by
    /// <see cref="MapEditorScene"/> after every state change.
    /// </summary>
    public void SetUndoState(bool canUndo, bool canRedo)
    {
        _undoAllButton.Disabled = !canUndo;
        _undoLastButton.Disabled = !canUndo;
        _redoLastButton.Disabled = !canRedo;
        _redoAllButton.Disabled = !canRedo;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo) return;

        switch (keyEvent.Keycode)
        {
            case Key.Z:
                if (keyEvent.ShiftPressed)
                {
                    if (!_undoAllButton.Disabled) UndoAllClicked?.Invoke();
                }
                else
                {
                    if (!_undoLastButton.Disabled) UndoLastClicked?.Invoke();
                }
                GetViewport().SetInputAsHandled();
                break;
            case Key.Y:
                if (keyEvent.ShiftPressed)
                {
                    if (!_redoAllButton.Disabled) RedoAllClicked?.Invoke();
                }
                else
                {
                    if (!_redoLastButton.Disabled) RedoLastClicked?.Invoke();
                }
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    private void OnSeedTextChanged(string newText)
    {
        // Strip non-digits that slip past MaxLength (paste/IME), keep
        // caret at the same logical column. Mirrors MainMenuScene.
        string filtered = new string(newText.Where(char.IsAsciiDigit).ToArray());
        if (filtered != newText)
        {
            int caret = _seedField.CaretColumn;
            _seedField.Text = filtered;
            _seedField.CaretColumn = System.Math.Min(caret, filtered.Length);
        }
        _generateButton.Disabled = string.IsNullOrEmpty(_seedField.Text);
    }

    private void OnGeneratePressed()
    {
        if (string.IsNullOrEmpty(_seedField.Text)) return;
        int.TryParse(_seedField.Text, out int seed);
        seed = System.Math.Clamp(seed, SeedMin, SeedMax);
        GenerateRequested?.Invoke(seed);
        // Re-randomize the field so the next press generates a different
        // map without the user re-typing. Use a fresh Random per press —
        // the user controls timing, so a new seed each call gives them a
        // different roll without us holding RNG state across the HUD.
        _seedField.Text = new System.Random().Next(SeedMin, SeedMax + 1).ToString();
    }

    private void OnSeedFieldGuiInput(InputEvent @event)
    {
        if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo) return;
        if (keyEvent.Keycode != Key.Enter && keyEvent.Keycode != Key.KpEnter) return;
        _seedField.AcceptEvent();
        OnGeneratePressed();
    }

    private void SelectPalette(int index, bool fireEvent = true)
    {
        if (index < 0 || index >= _palette.Length) return;
        if (index == SelectedPaletteIndex && _palette[index].IsSelected) return;

        _palette[SelectedPaletteIndex].IsSelected = false;
        SelectedPaletteIndex = index;
        _palette[index].IsSelected = true;
        if (fireEvent) PaletteSelectionChanged?.Invoke(index);
    }
}
