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
    private HudIconButton _generateButton = null!;
    private HexPaletteButton[] _palette = null!;
    private HudIconButton _undoAllButton = null!;
    private HudIconButton _undoLastButton = null!;
    private HudIconButton _redoLastButton = null!;
    private HudIconButton _redoAllButton = null!;

    public override void _Ready()
    {
        Vector2 viewport = GetViewport().GetVisibleRect().Size;

        // Warm-slate top strip + 1px line-soft bottom border — same
        // chrome the play HUD uses so both scenes read as one design
        // system. Falls back to a ColorRect-position so the host's
        // TopOffsetPx still slides the whole strip down.
        var background = new Panel
        {
            Position = new Vector2(0, TopOffsetPx),
            Size = new Vector2(viewport.X, HudView.HudHeight),
            // Default MouseFilter = Stop — blocks the HexHoverTooltip
            // sensor underneath so the coord tooltip is suppressed
            // anywhere over the toolbar (between buttons too).
        };
        var barStyle = new StyleBoxFlat
        {
            BgColor = UiPalette.HudBar,
            BorderColor = UiPalette.LineSoft,
            BorderWidthBottom = 1,
        };
        background.AddThemeStyleboxOverride("panel", barStyle);
        AddChild(background);

        var leftHbox = new HBoxContainer
        {
            Position = new Vector2(16, 8 + TopOffsetPx),
            Size = new Vector2(0, HudView.HudHeight - 16),
        };
        leftHbox.AddThemeConstantOverride("separation", 14);
        AddChild(leftHbox);

        // SEED eyebrow + mono LineEdit laid out side-by-side, mirroring
        // the play HUD's TURN / TO PLAY treatment.
        var seedBlock = new HBoxContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        seedBlock.AddThemeConstantOverride("separation", 10);
        var seedEyebrow = new Label
        {
            Text = "SEED",
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        seedEyebrow.AddThemeFontSizeOverride("font_size", 20);
        seedEyebrow.AddThemeColorOverride("font_color", UiPalette.Gold);
        seedBlock.AddChild(seedEyebrow);

        _seedField = new LineEdit
        {
            CustomMinimumSize = new Vector2(120, 0),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MaxLength = 4,
            Alignment = HorizontalAlignment.Right,
            Text = new System.Random().Next(SeedMin, SeedMax + 1).ToString(),
        };
        _seedField.AddThemeFontSizeOverride("font_size", 26);
        _seedField.TextChanged += OnSeedTextChanged;
        // Intercept Enter while the seed field has focus — without
        // GuiInput, the LineEdit consumes the key before _UnhandledInput
        // sees it, so the user has no way to fire Generate from the
        // keyboard with the field focused. Mirrors the seed field in
        // MainMenuScene.
        _seedField.GuiInput += OnSeedFieldGuiInput;
        seedBlock.AddChild(_seedField);
        leftHbox.AddChild(seedBlock);

        // Six-sided die glyph in place of a "Generate" label — the
        // button rolls a fresh map seed, so the die reads as the
        // re-roll affordance. Tooltip carries the verbal meaning.
        _generateButton = new HudIconButton(HudIcon.Die);
        _generateButton.FocusMode = Control.FocusModeEnum.None;
        _generateButton.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _generateButton.Pressed += OnGeneratePressed;
        AudioBus.AttachClick(_generateButton);
        leftHbox.AddChild(_generateButton);

        leftHbox.AddChild(BuildVerticalDivider());

        // Three visually-distinct palette groups, all in the same left
        // HBox as the seed/Generate cluster: a rounded slate "land
        // colors" panel (the six player fills, presented as a radio
        // group à la the play HUD's unit palette), then the four
        // terrain tools (water/tree/capital/tower) as bare swatches,
        // then the hand tool at the right end. Larger gaps between
        // groups are provided by explicit Control spacers.
        _palette = new HexPaletteButton[GameSettings.PlayerConfig.Length + 5];

        // Group 1: six land-color swatches inside a slate PanelContainer.
        var landPanel = new PanelContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        landPanel.AddThemeStyleboxOverride("panel", ModalChrome.PalettePanelStyle());
        var landRow = new HBoxContainer();
        landRow.AddThemeConstantOverride("separation", 4);
        landPanel.AddChild(landRow);
        leftHbox.AddChild(landPanel);

        for (int i = 0; i < GameSettings.PlayerConfig.Length; i++)
        {
            (_, string hex) = GameSettings.PlayerConfig[i];
            var button = new HexPaletteButton(new Color(hex));
            int paletteIndex = i + 1;
            // Same tooltip on every land swatch so the group reads as
            // one widget — the player picks "which color" by clicking
            // a specific swatch, but the meaning is identical across
            // all six.
            button.TooltipText = "Paint land for a player color";
            button.Pressed += _ => SelectPalette(paletteIndex);
            AudioBus.AttachClick(button);
            landRow.AddChild(button);
            _palette[paletteIndex] = button;
        }

        // 18-px gap before the terrain-tool group.
        leftHbox.AddChild(new Control { CustomMinimumSize = new Vector2(18, 0) });

        // Group 2: terrain tools (water / tree / capital / tower) as
        // bare swatches sitting outside the land panel.
        var terrainRow = new HBoxContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        terrainRow.AddThemeConstantOverride("separation", 6);
        leftHbox.AddChild(terrainRow);

        int waterIndex = WaterPaletteIndex;
        var waterButton = new HexPaletteButton(UiPalette.WaterDeep);
        waterButton.TooltipText = "Paint water";
        waterButton.Pressed += _ => SelectPalette(waterIndex);
        AudioBus.AttachClick(waterButton);
        terrainRow.AddChild(waterButton);
        _palette[waterIndex] = waterButton;

        int treeIndex = TreePaletteIndex;
        var treeButton = new HexPaletteButton(
            new Color(0.42f, 0.30f, 0.18f, 1f), HexPaletteIcon.Tree);
        treeButton.TooltipText = "Place / remove a tree";
        treeButton.Pressed += _ => SelectPalette(treeIndex);
        AudioBus.AttachClick(treeButton);
        terrainRow.AddChild(treeButton);
        _palette[treeIndex] = treeButton;

        int capitalIndex = CapitalPaletteIndex;
        var capitalButton = new HexPaletteButton(
            new Color(0.36f, 0.32f, 0.50f, 1f), HexPaletteIcon.Capital);
        capitalButton.TooltipText = "Place a capital";
        capitalButton.Pressed += _ => SelectPalette(capitalIndex);
        AudioBus.AttachClick(capitalButton);
        terrainRow.AddChild(capitalButton);
        _palette[capitalIndex] = capitalButton;

        int towerIndex = TowerPaletteIndex;
        var towerButton = new HexPaletteButton(
            new Color(0.45f, 0.45f, 0.50f, 1f), HexPaletteIcon.Tower);
        towerButton.TooltipText = "Place / remove a tower";
        towerButton.Pressed += _ => SelectPalette(towerIndex);
        AudioBus.AttachClick(towerButton);
        terrainRow.AddChild(towerButton);
        _palette[towerIndex] = towerButton;

        // 18-px gap before the hand tool.
        leftHbox.AddChild(new Control { CustomMinimumSize = new Vector2(18, 0) });

        // Group 3: hand (pan / no-paint) — its own slot at the right
        // end of the palette area so it doesn't read as a paintable
        // material. Dark neutral grey gives the white selection outline
        // contrast and lets the skin-tone hand silhouette read.
        var handButton = new HexPaletteButton(
            new Color(0.32f, 0.34f, 0.38f, 1f), HexPaletteIcon.Hand);
        handButton.TooltipText = "Pan";
        handButton.Pressed += _ => SelectPalette(HandPaletteIndex);
        AudioBus.AttachClick(handButton);
        leftHbox.AddChild(handButton);
        _palette[HandPaletteIndex] = handButton;

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
            OffsetTop = 8f + TopOffsetPx,
            OffsetBottom = HudView.HudHeight - 8f + TopOffsetPx,
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

    // Same 1×24 line-soft divider HudView uses between the three regions
    // of its bar; kept as a static helper here so the editor doesn't
    // depend on the play HUD's internals.
    private static Control BuildVerticalDivider()
    {
        return new ColorRect
        {
            Color = UiPalette.LineSoft,
            CustomMinimumSize = new Vector2(1, 32),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
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
        _generateButton.FlashPress();
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
