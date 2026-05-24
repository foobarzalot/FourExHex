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
public partial class MapEditorHudView : OrientationHud
{
    public const int SeedMin = 1;
    public const int SeedMax = 1000;
    // Portrait bottom-bar height (seed + generate + undo/redo + options). The
    // portrait top bar (paint palette) reuses HudView.HudHeight.
    private const float PortraitBottomBarHeight = 96f;

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

    // Persistent clusters, built once and reparented between the bars
    // (TopBar/BottomBar, owned by OrientationHud) on a landscape↔portrait flip.
    private Control _paletteCluster = null!;      // all paint swatches/tools
    private Control _seedCluster = null!;         // SEED field + Generate die
    private Control _editControlsCluster = null!; // undo/redo (+ Options)

    public override void _Ready()
    {
        Vector2 viewport = GetViewport().GetVisibleRect().Size;

        // Persistent clusters, built once and reparented between orientation-
        // specific bars by ApplyLayout. The slate bar background + click-
        // blocking now live on the bar Panels (created in ApplyLayout), not a
        // standalone background. The clusters carry no parent until then.
        _seedCluster = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        _seedCluster.AddThemeConstantOverride("separation", 14);
        _paletteCluster = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        _paletteCluster.AddThemeConstantOverride("separation", 14);
        _editControlsCluster = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        _editControlsCluster.AddThemeConstantOverride("separation", 8);

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
        _seedCluster.AddChild(seedBlock);

        // Six-sided die glyph in place of a "Generate" label — the
        // button rolls a fresh map seed, so the die reads as the
        // re-roll affordance. Tooltip carries the verbal meaning.
        _generateButton = new HudIconButton(HudIcon.Die);
        _generateButton.FocusMode = Control.FocusModeEnum.None;
        _generateButton.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _generateButton.Pressed += OnGeneratePressed;
        AudioBus.AttachClick(_generateButton);
        _seedCluster.AddChild(_generateButton);

        // Three visually-distinct palette groups, all in the paint-palette
        // cluster: a rounded slate "land colors" panel (the six player fills,
        // presented as a radio group à la the play HUD's unit palette), then
        // the four terrain tools (water/tree/capital/tower) as bare swatches,
        // then the hand tool at the right end. Larger gaps between groups are
        // provided by explicit Control spacers.
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
        _paletteCluster.AddChild(landPanel);

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
        _paletteCluster.AddChild(new Control { CustomMinimumSize = new Vector2(18, 0) });

        // Group 2: terrain tools (water / tree / capital / tower) as
        // bare swatches sitting outside the land panel.
        var terrainRow = new HBoxContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        terrainRow.AddThemeConstantOverride("separation", 6);
        _paletteCluster.AddChild(terrainRow);

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
        _paletteCluster.AddChild(new Control { CustomMinimumSize = new Vector2(18, 0) });

        // Group 3: hand (pan / no-paint) — its own slot at the right
        // end of the palette area so it doesn't read as a paintable
        // material. Dark neutral grey gives the white selection outline
        // contrast and lets the skin-tone hand silhouette read.
        var handButton = new HexPaletteButton(
            new Color(0.32f, 0.34f, 0.38f, 1f), HexPaletteIcon.Hand);
        handButton.TooltipText = "Pan";
        handButton.Pressed += _ => SelectPalette(HandPaletteIndex);
        AudioBus.AttachClick(handButton);
        _paletteCluster.AddChild(handButton);
        _palette[HandPaletteIndex] = handButton;

        // Default selection: the hand (no-paint, pan-only) swatch. Visual
        // is set via SelectPalette so the IsSelected outline draws from
        // the start.
        SelectPalette(HandPaletteIndex, fireEvent: false);

        // Undo / redo cluster — icon glyphs + tooltips matching the play
        // HUD so muscle memory and the visual language carry over.
        _undoAllButton = MakeUndoButton(HudIcon.UndoAll, () => UndoAllClicked?.Invoke());
        _editControlsCluster.AddChild(_undoAllButton);
        _undoLastButton = MakeUndoButton(HudIcon.UndoLast, () => UndoLastClicked?.Invoke());
        _editControlsCluster.AddChild(_undoLastButton);
        _redoLastButton = MakeUndoButton(HudIcon.RedoLast, () => RedoLastClicked?.Invoke());
        _editControlsCluster.AddChild(_redoLastButton);
        _redoAllButton = MakeUndoButton(HudIcon.RedoAll, () => RedoAllClicked?.Invoke());
        _editControlsCluster.AddChild(_redoAllButton);

        if (ShowSceneRootChrome)
        {
            // Save Map and Load Map live inside the EscMenu now —
            // MapEditorScene wires them into the Resume / Save / Load /
            // Exit option list shown on Escape. The Options button is
            // just the entry point to that menu.
            var optionsButton = new HudIconButton(HudIcon.Options);
            optionsButton.Pressed += () => EscRequested?.Invoke();
            AudioBus.AttachClick(optionsButton);
            _editControlsCluster.AddChild(optionsButton);
        }

        // Arrange the clusters for the current orientation + track resize
        // (OrientationHud owns the bars + the flip/publish lifecycle).
        InitOrientation();
    }

    // ---- Orientation-aware layout (OrientationHud hooks) -----------------

    protected override void DetachClusters()
    {
        HudBars.Detach(_seedCluster);
        HudBars.Detach(_paletteCluster);
        HudBars.Detach(_editControlsCluster);
    }

    /// <summary>Single top strip: seed + palette (left), undo/options (right).</summary>
    protected override void BuildLandscapeBars()
    {
        TopBar = HudBars.MakeBarPanel(top: true, height: HudView.HudHeight, topOffset: TopOffsetPx);
        AddChild(TopBar);
        Control frame = HudBars.MakeBarFrame();
        TopBar.AddChild(frame);

        HBoxContainer left = HudBars.MakeAnchoredGroup(0f, Control.GrowDirection.End);
        frame.AddChild(left);
        left.AddChild(_seedCluster);
        left.AddChild(BuildVerticalDivider());
        left.AddChild(_paletteCluster);

        HBoxContainer right = HudBars.MakeAnchoredGroup(1f, Control.GrowDirection.Begin, separation: 8);
        frame.AddChild(right);
        right.AddChild(_editControlsCluster);
    }

    /// <summary>Portrait split: all paint options on top; seed/generate +
    /// undo/redo/options on the bottom.</summary>
    protected override void BuildPortraitBars()
    {
        // Top bar: all paint options (always visible — the editor has no
        // selection concept).
        TopBar = HudBars.MakeBarPanel(top: true, height: HudView.HudHeight, topOffset: TopOffsetPx);
        AddChild(TopBar);
        var topRow = new HBoxContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0f, AnchorBottom = 1f,
            OffsetTop = 8f, OffsetBottom = -8f,
            GrowHorizontal = Control.GrowDirection.Both,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        topRow.AddThemeConstantOverride("separation", 14);
        TopBar.AddChild(topRow);
        topRow.AddChild(_paletteCluster);

        // Bottom bar: seed + generate (left) and undo/redo + options (right).
        BottomBar = HudBars.MakeBarPanel(top: false, height: PortraitBottomBarHeight);
        AddChild(BottomBar);
        Control frame = HudBars.MakeBarFrame();
        BottomBar.AddChild(frame);

        HBoxContainer left = HudBars.MakeAnchoredGroup(0f, Control.GrowDirection.End);
        frame.AddChild(left);
        left.AddChild(_seedCluster);

        HBoxContainer right = HudBars.MakeAnchoredGroup(1f, Control.GrowDirection.Begin, separation: 8);
        frame.AddChild(right);
        right.AddChild(_editControlsCluster);
    }

    protected override MapInsets ComputeInsets()
    {
        // Editor top bar is always up (no selection gating), so topBarVisible
        // is always true. The top inset includes TopOffsetPx so a host (tutorial
        // builder) that slides the strip down is accounted for.
        return ScreenLayout.ComputeInsets(
            Orientation,
            topBarVisible: true,
            landscapeBarHeight: TopOffsetPx + HudView.HudHeight,
            portraitTopBarHeight: TopOffsetPx + HudView.HudHeight,
            portraitBottomBarHeight: PortraitBottomBarHeight);
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
