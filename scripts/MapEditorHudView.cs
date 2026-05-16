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

    /// <summary>
    /// Palette index reserved for the hand (no-op / pan-only) swatch.
    /// Always 0 — the hand sits first in the palette and is the default
    /// selection on scene entry.
    /// </summary>
    public const int HandPaletteIndex = 0;
    /// <summary>Palette index reserved for the water swatch.</summary>
    public static int WaterPaletteIndex => 1 + GameSettings.PlayerConfig.Length;
    /// <summary>Palette index reserved for the tree-toggle swatch.</summary>
    public static int TreePaletteIndex => 2 + GameSettings.PlayerConfig.Length;
    /// <summary>Palette index reserved for the capital-placement swatch.</summary>
    public static int CapitalPaletteIndex => 3 + GameSettings.PlayerConfig.Length;
    /// <summary>Palette index reserved for the tower-toggle swatch.</summary>
    public static int TowerPaletteIndex => 4 + GameSettings.PlayerConfig.Length;

    public event Action? EscRequested;
    public event Action<int>? GenerateRequested;
    public event Action<int>? PaletteSelectionChanged;
    public event Action? UndoLastClicked;
    public event Action? UndoAllClicked;
    public event Action? RedoLastClicked;
    public event Action? RedoAllClicked;

    /// <summary>
    /// When false, the right-side Save Map / Load Map / Exit buttons are
    /// not built. Hosts that supply their own scene-root chrome (e.g.
    /// TutorialBuilderScene's topbar) set this to false. The standalone
    /// Map Editor scene leaves it at the default true.
    ///
    /// Must be set before <see cref="_Ready"/> runs (i.e. before the
    /// host calls <c>AddChild(hud)</c>).
    /// </summary>
    public bool ShowSceneRootChrome { get; set; } = true;

    /// <summary>
    /// Vertical offset (in pixels) for the entire HUD strip. Default 0
    /// (the standalone editor sits at the top). TutorialBuilderScene
    /// sets this to 60 so the strip renders below its topbar.
    ///
    /// Must be set before <see cref="_Ready"/> runs.
    /// </summary>
    public int TopOffsetPx { get; set; } = 0;

    public int SelectedPaletteIndex { get; private set; }

    private LineEdit _seedField = null!;
    private Button _generateButton = null!;
    private HexPaletteButton[] _palette = null!;
    private HudIconButton _undoAllButton = null!;
    private HudIconButton _undoLastButton = null!;
    private HudIconButton _redoLastButton = null!;
    private HudIconButton _redoAllButton = null!;

    public override void _Ready()
    {
        Vector2 viewport = GetViewport().GetVisibleRect().Size;

        var background = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.8f),
            Position = new Vector2(0, TopOffsetPx),
            Size = new Vector2(viewport.X, HudView.HudHeight),
        };
        AddChild(background);

        var leftHbox = new HBoxContainer
        {
            Position = new Vector2(16, 12 + TopOffsetPx),
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
        AudioBus.AttachClick(_generateButton);
        leftHbox.AddChild(_generateButton);

        // Palette sits in the same left HBox as the seed/Generate cluster
        // so all the editor controls flow left-to-right without overlap.
        var paletteHbox = new HBoxContainer();
        paletteHbox.AddThemeConstantOverride("separation", 6);
        leftHbox.AddChild(paletteHbox);

        _palette = new HexPaletteButton[GameSettings.PlayerConfig.Length + 5];

        // Hand swatch — pan/no-paint mode. Default selection on scene
        // entry. Dark neutral grey: gives the white selection outline
        // enough contrast and lets the skin-tone hand silhouette read
        // against the background.
        var handButton = new HexPaletteButton(
            new Color(0.32f, 0.34f, 0.38f, 1f), HexPaletteIcon.Hand);
        handButton.Pressed += _ => SelectPalette(HandPaletteIndex);
        AudioBus.AttachClick(handButton);
        paletteHbox.AddChild(handButton);
        _palette[HandPaletteIndex] = handButton;

        for (int i = 0; i < GameSettings.PlayerConfig.Length; i++)
        {
            (_, string hex) = GameSettings.PlayerConfig[i];
            var button = new HexPaletteButton(new Color(hex));
            int paletteIndex = i + 1;
            button.Pressed += _ => SelectPalette(paletteIndex);
            AudioBus.AttachClick(button);
            paletteHbox.AddChild(button);
            _palette[paletteIndex] = button;
        }
        // Water swatch — same blue HexMapView.CreateWaterHexVisual uses.
        int waterIndex = WaterPaletteIndex;
        var waterButton = new HexPaletteButton(new Color(0.20f, 0.42f, 0.65f, 1f));
        waterButton.Pressed += _ => SelectPalette(waterIndex);
        AudioBus.AttachClick(waterButton);
        paletteHbox.AddChild(waterButton);
        _palette[waterIndex] = waterButton;
        // Tree swatch — earthy brown background with the tree icon
        // overlaid. Reads as "tree on dirt" rather than a seventh
        // player color, and dark enough that the white selection
        // outline reads at a glance.
        int treeIndex = TreePaletteIndex;
        var treeButton = new HexPaletteButton(
            new Color(0.42f, 0.30f, 0.18f, 1f), HexPaletteIcon.Tree);
        treeButton.Pressed += _ => SelectPalette(treeIndex);
        AudioBus.AttachClick(treeButton);
        paletteHbox.AddChild(treeButton);
        _palette[treeIndex] = treeButton;
        // Capital swatch — deep slate-violet so the white selection
        // outline reads, and so it's visually distinct from the
        // similarly grey-toned hand and tower swatches.
        int capitalIndex = CapitalPaletteIndex;
        var capitalButton = new HexPaletteButton(
            new Color(0.36f, 0.32f, 0.50f, 1f), HexPaletteIcon.Capital);
        capitalButton.Pressed += _ => SelectPalette(capitalIndex);
        AudioBus.AttachClick(capitalButton);
        paletteHbox.AddChild(capitalButton);
        _palette[capitalIndex] = capitalButton;
        // Tower swatch — dark stone-grey background with the rook icon
        // overlaid. Distinct from the lighter capital slate so the two
        // grey-ish buttons read as different at a glance.
        int towerIndex = TowerPaletteIndex;
        var towerButton = new HexPaletteButton(
            new Color(0.45f, 0.45f, 0.50f, 1f), HexPaletteIcon.Tower);
        towerButton.Pressed += _ => SelectPalette(towerIndex);
        AudioBus.AttachClick(towerButton);
        paletteHbox.AddChild(towerButton);
        _palette[towerIndex] = towerButton;

        // Default selection: the hand (no-paint, pan-only) swatch. Visual
        // is set via SelectPalette so the IsSelected outline draws from
        // the start.
        SelectPalette(HandPaletteIndex, fireEvent: false);

        var rightHbox = new HBoxContainer
        {
            AnchorLeft = 0f,
            AnchorRight = 1f,
            AnchorTop = 0f,
            AnchorBottom = 0f,
            OffsetLeft = 0f,
            OffsetRight = -16f,
            OffsetTop = 12f + TopOffsetPx,
            OffsetBottom = 48f + TopOffsetPx,
            Alignment = BoxContainer.AlignmentMode.End,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        rightHbox.AddThemeConstantOverride("separation", 8);
        AddChild(rightHbox);

        // Undo / redo cluster — icon glyphs + tooltips matching the play
        // HUD so muscle memory and the visual language carry over.
        _undoAllButton = MakeUndoButton(HudIcon.UndoAll, () => UndoAllClicked?.Invoke());
        rightHbox.AddChild(_undoAllButton);
        _undoLastButton = MakeUndoButton(HudIcon.UndoLast, () => UndoLastClicked?.Invoke());
        rightHbox.AddChild(_undoLastButton);
        _redoLastButton = MakeUndoButton(HudIcon.RedoLast, () => RedoLastClicked?.Invoke());
        rightHbox.AddChild(_redoLastButton);
        _redoAllButton = MakeUndoButton(HudIcon.RedoAll, () => RedoAllClicked?.Invoke());
        rightHbox.AddChild(_redoAllButton);

        if (ShowSceneRootChrome)
        {
            // Save Map and Load Map live inside the EscMenu now —
            // MapEditorScene wires them into the Resume / Save / Load /
            // Exit option list shown on Escape. The Options button is
            // just the entry point to that menu.
            var optionsButton = new HudIconButton(HudIcon.Options);
            optionsButton.Pressed += () => EscRequested?.Invoke();
            AudioBus.AttachClick(optionsButton);
            rightHbox.AddChild(optionsButton);
        }
    }

    private static HudIconButton MakeUndoButton(HudIcon icon, Action onPressed)
    {
        var b = new HudIconButton(icon) { Disabled = true };
        b.Pressed += () => onPressed();
        AudioBus.AttachClick(b);
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

    /// <summary>
    /// Programmatically reselect the hand swatch. Used by the Escape
    /// key handler to drop out of paint mode without exiting the
    /// editor. Fires <see cref="PaletteSelectionChanged"/> if the hand
    /// wasn't already selected.
    /// </summary>
    public void SelectHand() => SelectPalette(HandPaletteIndex);

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
