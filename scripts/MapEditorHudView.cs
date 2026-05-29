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
    // Portrait bottom-bar height (paint palette). The portrait top bar
    // (seed + generate + undo/redo + options) reuses HudView.HudHeight.
    private const float PortraitBottomBarHeight = 96f;

    // Below these viewport widths the six individual land swatches collapse to
    // a single colored button that cycles the player color on each press —
    // mirrors HudView's player-swatch-bar compacting. Two thresholds because
    // landscape lays the palette beside the seed cluster, so it needs more
    // room before the full row fits. Tuned against the S9 portrait/landscape
    // widths (see RELEASE.md device playbook).
    private const float FullLandRowWidthPortrait = 760f;
    private const float FullLandRowWidthLandscape = 1040f;

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
    private HudIconButton _undoLastButton = null!;
    private HudIconButton _redoLastButton = null!;

    // The six land swatches (expanded) and the single cycling button
    // (collapsed) live side-by-side in the slate land group; exactly one is
    // visible, toggled by OnViewportMetricsChanged. _lastLandPaletteIndex is
    // the land color the cycle button shows / will paint with (1..N land
    // indices; defaults to Red).
    private Control _landRow = null!;
    private HexPaletteButton _landCycleButton = null!;
    private int _lastLandPaletteIndex = 1;

    // Persistent clusters, built once and reparented between the bars
    // (TopBar/BottomBar, owned by OrientationHud) on a landscape↔portrait flip.
    private Control _seedCluster = null!;         // SEED field + Generate die
    private Control _landCluster = null!;         // six land swatches (collapses to the cycle button)
    private Control _toolsCluster = null!;        // terrain tools (water/tree/capital/tower) + hand
    private Control _undoCluster = null!;         // undo/redo
    private HudIconButton? _optionsButton;        // gear → EscRequested (only when ShowSceneRootChrome)

    public override void _Ready()
    {
        Vector2 viewport = GetViewport().GetVisibleRect().Size;

        // Persistent clusters, built once and reparented between orientation-
        // specific bars by ApplyLayout. The slate bar background + click-
        // blocking now live on the bar Panels (created in ApplyLayout), not a
        // standalone background. The clusters carry no parent until then.
        _seedCluster = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        _seedCluster.AddThemeConstantOverride("separation", 14);
        _toolsCluster = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        _toolsCluster.AddThemeConstantOverride("separation", 14);
        _undoCluster = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        _undoCluster.AddThemeConstantOverride("separation", 8);

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

        // Three visually-distinct palette groups: a rounded slate "land colors"
        // panel (the six player fills, presented as a radio group à la the play
        // HUD's unit palette) forms the land cluster; the four terrain tools
        // (water/tree/capital/tower) and the hand tool form the tools cluster.
        // Landscape splits them (land at the left edge, tools beside the
        // controls on the right); portrait centers both together. Larger gaps
        // within the tools cluster are provided by explicit Control spacers.
        _palette = new HexPaletteButton[GameSettings.PlayerConfig.Length + 5];

        // Group 1: six land-color swatches inside a slate PanelContainer. The
        // panel wraps a single landGroup that holds both the full six-swatch
        // row and a single collapsed cycle button; exactly one is visible at a
        // time (hidden children are excluded from container layout, so the
        // panel sizes to whichever is shown). The collapse is driven by
        // OnViewportMetricsChanged, mirroring HudView's swatch-bar compacting.
        var landPanel = new PanelContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        landPanel.AddThemeStyleboxOverride("panel", ModalChrome.PalettePanelStyle());
        var landGroup = new HBoxContainer();
        landPanel.AddChild(landGroup);
        _landCluster = landPanel;

        var landRow = new HBoxContainer();
        landRow.AddThemeConstantOverride("separation", 4);
        landGroup.AddChild(landRow);
        _landRow = landRow;

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

        // Collapsed-mode counterpart: a single colored hex that cycles the
        // player color on each press (built hidden — the init
        // OnViewportMetricsChanged call flips it on if the viewport is narrow).
        _landCycleButton = new HexPaletteButton(new Color(GameSettings.PlayerConfig[0].Hex))
        {
            TooltipText = "Paint land — tap to cycle player color",
            Visible = false,
        };
        _landCycleButton.Pressed += _ => OnLandCyclePressed();
        AudioBus.AttachClick(_landCycleButton);
        landGroup.AddChild(_landCycleButton);

        // Group 2: terrain tools (water / tree / capital / tower) as
        // bare swatches, first in the tools cluster.
        var terrainRow = new HBoxContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        terrainRow.AddThemeConstantOverride("separation", 6);
        _toolsCluster.AddChild(terrainRow);

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

        // Group 3: hand (pan / no-paint) — sits just after the terrain tools
        // (only the tools-cluster separation apart). Dark neutral grey gives the
        // white selection outline contrast and lets the skin-tone hand
        // silhouette read.
        var handButton = new HexPaletteButton(
            new Color(0.32f, 0.34f, 0.38f, 1f), HexPaletteIcon.Hand);
        handButton.TooltipText = "Pan";
        handButton.Pressed += _ => SelectPalette(HandPaletteIndex);
        AudioBus.AttachClick(handButton);
        _toolsCluster.AddChild(handButton);
        _palette[HandPaletteIndex] = handButton;

        // Default selection: the hand (no-paint, pan-only) swatch. Visual
        // is set via SelectPalette so the IsSelected outline draws from
        // the start.
        SelectPalette(HandPaletteIndex, fireEvent: false);

        // Undo / redo cluster — two icon buttons matching the play HUD: a
        // short click is Undo/Redo Last, holding past the long-press
        // threshold fires Undo All / Redo All (same as Shift+Z / Shift+Y).
        _undoLastButton = MakeUndoButton(
            HudIcon.UndoLast, "Undo — Z (hold for Undo All)",
            () => UndoLastClicked?.Invoke(), () => UndoAllClicked?.Invoke());
        _undoCluster.AddChild(_undoLastButton);
        _redoLastButton = MakeUndoButton(
            HudIcon.RedoLast, "Redo — Y (hold for Redo All)",
            () => RedoLastClicked?.Invoke(), () => RedoAllClicked?.Invoke());
        _undoCluster.AddChild(_redoLastButton);

        if (ShowSceneRootChrome)
        {
            // Save Map and Load Map live inside the EscMenu now —
            // MapEditorScene wires them into the Resume / Save / Load /
            // Exit option list shown on Escape. The Options button is
            // just the entry point to that menu. Reparented per orientation by
            // the Build*Bars methods (far-right corner), so it's not added to a
            // cluster here.
            _optionsButton = new HudIconButton(HudIcon.Options);
            _optionsButton.Pressed += () => EscRequested?.Invoke();
            AudioBus.AttachClick(_optionsButton);
        }

        // Arrange the clusters for the current orientation + track resize
        // (OrientationHud owns the bars + the flip/publish lifecycle).
        InitOrientation();
    }

    // ---- Orientation-aware layout (OrientationHud hooks) -----------------

    protected override void DetachClusters()
    {
        HudBars.Detach(_seedCluster);
        HudBars.Detach(_landCluster);
        HudBars.Detach(_toolsCluster);
        HudBars.Detach(_undoCluster);
        if (_optionsButton != null) HudBars.Detach(_optionsButton);
    }

    /// <summary>Single bottom strip: seed + palette (left), undo/options (right)
    /// — moved to the bottom for thumb reach, matching the play HUD.</summary>
    protected override void BuildLandscapeBars()
    {
        BottomBar = HudBars.MakeBarPanel(top: false, height: HudView.HudHeight,
            bottomOffset: SafeArea.Current.Bottom);
        AddChild(BottomBar);
        Control frame = HudBars.MakeBarFrame();
        BottomBar.AddChild(frame);

        // Left edge: the six land-color swatches.
        HBoxContainer left = HudBars.MakeAnchoredGroup(0f, Control.GrowDirection.End);
        frame.AddChild(left);
        left.AddChild(_landCluster);

        // Centered: seed field + generate.
        HBoxContainer center = HudBars.MakeAnchoredGroup(0.5f, Control.GrowDirection.Both);
        frame.AddChild(center);
        center.AddChild(_seedCluster);

        // Right corner: undo/redo first (just left of the water tool), then the
        // terrain tools + hand, then the options gear at the far right.
        HBoxContainer right = HudBars.MakeAnchoredGroup(1f, Control.GrowDirection.Begin, separation: 14);
        frame.AddChild(right);
        right.AddChild(_undoCluster);
        right.AddChild(BuildVerticalDivider());
        right.AddChild(_toolsCluster);
        if (_optionsButton != null)
        {
            right.AddChild(BuildVerticalDivider());
            right.AddChild(_optionsButton);
        }
    }

    /// <summary>Portrait split: seed/generate + undo/redo/options on top; all
    /// paint options on the bottom for thumb reach.</summary>
    protected override void BuildPortraitBars()
    {
        // Top bar: seed + generate (left) and undo/redo + options (right).
        TopBar = HudBars.MakeBarPanel(top: true, height: HudView.HudHeight,
            topOffset: TopOffsetPx + SafeArea.Current.Top);
        AddChild(TopBar);
        Control frame = HudBars.MakeBarFrame();
        TopBar.AddChild(frame);

        HBoxContainer left = HudBars.MakeAnchoredGroup(0f, Control.GrowDirection.End);
        frame.AddChild(left);
        left.AddChild(_seedCluster);

        HBoxContainer right = HudBars.MakeAnchoredGroup(1f, Control.GrowDirection.Begin, separation: 8);
        frame.AddChild(right);
        right.AddChild(_undoCluster);
        if (_optionsButton != null) right.AddChild(_optionsButton);

        // Bottom bar: all paint options (always visible — the editor has no
        // selection concept).
        BottomBar = HudBars.MakeBarPanel(top: false, height: PortraitBottomBarHeight,
            bottomOffset: SafeArea.Current.Bottom);
        AddChild(BottomBar);
        var bottomRow = new HBoxContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0f, AnchorBottom = 1f,
            OffsetTop = 8f, OffsetBottom = -8f,
            GrowHorizontal = Control.GrowDirection.Both,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        bottomRow.AddThemeConstantOverride("separation", 14);
        BottomBar.AddChild(bottomRow);
        bottomRow.AddChild(_landCluster);
        bottomRow.AddChild(new Control { CustomMinimumSize = new Vector2(18, 0) });
        bottomRow.AddChild(_toolsCluster);
    }

    protected override MapInsets ComputeInsets()
    {
        // Landscape now reserves a bottom strip (ScreenLayout puts the
        // landscapeBarHeight at the bottom). The portrait top bar is always up
        // (no selection gating); its inset includes TopOffsetPx so a host
        // (tutorial builder) that slides the strip down is accounted for.
        return ScreenLayout.ComputeInsets(
            Orientation,
            topBarVisible: true,
            landscapeBarHeight: HudView.HudHeight,
            portraitTopBarHeight: TopOffsetPx + HudView.HudHeight,
            portraitBottomBarHeight: PortraitBottomBarHeight);
    }

    private static HudIconButton MakeUndoButton(HudIcon icon, string tooltip, Action onShort, Action onLong)
    {
        var b = new HudIconButton(icon) { Disabled = true, TooltipText = tooltip };
        b.Pressed += () =>
        {
            if (b.ConsumeLongPress()) return;
            onShort();
        };
        b.LongPressed += () =>
        {
            Log.Debug(Log.LogCategory.Input, $"Editor {icon} long-press");
            onLong();
        };
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
    /// Refresh the disabled state of the undo/redo buttons. Called by
    /// <see cref="MapEditorScene"/> after every state change.
    /// </summary>
    public void SetUndoState(bool canUndo, bool canRedo)
    {
        _undoLastButton.Disabled = !canUndo;
        _redoLastButton.Disabled = !canRedo;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo) return;

        switch (keyEvent.Keycode)
        {
            case Key.Z:
                if (keyEvent.ShiftPressed)
                {
                    if (!_undoLastButton.Disabled) UndoAllClicked?.Invoke();
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
                    if (!_redoLastButton.Disabled) RedoAllClicked?.Invoke();
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
        if (index == SelectedPaletteIndex && _palette[index].IsSelected)
        {
            RefreshLandCycleVisual();
            return;
        }

        _palette[SelectedPaletteIndex].IsSelected = false;
        SelectedPaletteIndex = index;
        _palette[index].IsSelected = true;
        // Remember the last land color so the collapsed cycle button shows it,
        // even when the swatch was picked directly from the expanded row.
        if (IsLandIndex(index)) _lastLandPaletteIndex = index;
        RefreshLandCycleVisual();
        if (fireEvent) PaletteSelectionChanged?.Invoke(index);
    }

    private static bool IsLandIndex(int index) =>
        index >= 1 && index <= GameSettings.PlayerConfig.Length;

    // Wrap forward through the land indices (1..N), 6 -> 1.
    private static int NextLandIndex(int index) =>
        index % GameSettings.PlayerConfig.Length + 1;

    /// <summary>
    /// Select-first-then-cycle: if land isn't the active tool, the first press
    /// just selects land at the last-used color; once land is active, each
    /// further press advances to the next color.
    /// </summary>
    private void OnLandCyclePressed()
    {
        bool wasLand = IsLandIndex(SelectedPaletteIndex);
        if (wasLand) _lastLandPaletteIndex = NextLandIndex(_lastLandPaletteIndex);
        Log.Debug(Log.LogCategory.Input,
            $"[LandCycle] press -> select {_lastLandPaletteIndex} (wasLand={wasLand})");
        SelectPalette(_lastLandPaletteIndex);
    }

    /// <summary>Keep the collapsed cycle button's fill + selection outline in
    /// sync with the remembered land color and the current tool.</summary>
    private void RefreshLandCycleVisual()
    {
        _landCycleButton.FillColor = new Color(GameSettings.PlayerConfig[_lastLandPaletteIndex - 1].Hex);
        _landCycleButton.IsSelected = IsLandIndex(SelectedPaletteIndex);
    }

    /// <summary>Collapse the six land swatches to the single cycling button in
    /// a narrow viewport (and restore the full row when there's room) — the
    /// editor analogue of HudView's player-swatch-bar compacting.</summary>
    protected override void OnViewportMetricsChanged()
    {
        float width = GetViewport().GetVisibleRect().Size.X;
        float threshold = Orientation == ScreenOrientation.Landscape
            ? FullLandRowWidthLandscape
            : FullLandRowWidthPortrait;
        bool collapse = width < threshold;
        _landRow.Visible = !collapse;
        _landCycleButton.Visible = collapse;
        RefreshLandCycleVisual();
        Log.Debug(Log.LogCategory.Render,
            $"[LandPalette] metrics: width={width:0} orient={Orientation} collapse={collapse}");
    }
}
