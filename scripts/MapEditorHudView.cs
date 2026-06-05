using System;
using Godot;

/// <summary>
/// HUD for the map editor scene. Follows the gameplay HUD's D1 "Roles
/// Split (floating)" layout: TopLeftZone empty (editor has no read-only
/// status), TopRightZone holds undo/redo + Options (when present), the
/// paint cluster (land palette + sea + tree + capital + tower) lives in
/// the LeftRail (landscape) or bottom row 2 (portrait), and the tools
/// cluster (hand + die) lives in the RightRail (landscape) or bottom row
/// 1 (portrait).
///
/// The die is the lone randomize trigger — pressing it rolls a fresh
/// random seed each time (the previous numeric seed LineEdit was removed
/// per the D1 redesign so the bar isn't crowded with a status display
/// the player never needs to read).
///
/// Deliberately does NOT implement <see cref="IHudView"/> — that interface
/// is the play-scene controller contract and includes events the editor
/// has no use for.
/// </summary>
public partial class MapEditorHudView : OrientationHud
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
    /// When false, the Options button is not built. Hosts that supply
    /// their own scene-root chrome (e.g. TutorialBuilderScene's topbar)
    /// set this to false. The standalone Map Editor scene leaves it true.
    /// Must be set before <see cref="_Ready"/> runs.
    /// </summary>
    public bool ShowSceneRootChrome { get; set; } = true;

    /// <summary>
    /// Vertical offset (in pixels) for the entire HUD strip. Default 0.
    /// TutorialBuilderScene sets this to 60 so the strip renders below
    /// its topbar. Must be set before <see cref="_Ready"/> runs.
    /// </summary>
    public int TopOffsetPx { get; set; } = 0;

    public int SelectedPaletteIndex { get; private set; }

    private HudIconButton _generateButton = null!;
    private HexPaletteButton[] _palette = null!;
    private HudIconButton _undoLastButton = null!;
    private HudIconButton _redoLastButton = null!;

    // Land palette state: the six swatches (expanded) and the single
    // cycling button (compact) live side-by-side in the slate land panel;
    // exactly one is visible (driven by Compact). _lastLandPaletteIndex
    // is the land color the cycle button shows / will paint with.
    private BoxContainer _landRow = null!;
    private HexPaletteButton _landCycleButton = null!;
    private int _lastLandPaletteIndex = 1;

    // Persistent clusters reparented per orientation. The two BoxContainers
    // flip Vertical/horizontal on landscape↔portrait so the same buttons
    // stack as a rail column or sit as a bar row.
    private PanelContainer _landCluster = null!;   // 6 land swatches OR 1 cycle button (chip chrome)
    private BoxContainer _paintCluster = null!;    // water + tree + capital + tower (terrain paint)
    private BoxContainer _toolsCluster = null!;    // hand (pan) + die (random)
    private Control _undoCluster = null!;          // undo / redo
    private HudIconButton? _optionsButton;         // gear → EscRequested (only when ShowSceneRootChrome)

    public override void _Ready()
    {
        // Persistent clusters, parentless until ApplyLayout reparents them.

        _paintCluster = new BoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        _paintCluster.AddThemeConstantOverride("separation", 8);

        _toolsCluster = new BoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        _toolsCluster.AddThemeConstantOverride("separation", 8);

        _undoCluster = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        _undoCluster.AddThemeConstantOverride("separation", 8);

        // Die — fires a fresh random seed each press; no numeric input
        // surfaced any more. Was previously paired with a LineEdit (seed
        // pill); the LineEdit was removed in the D1 redesign so the bar
        // isn't crowded with a status the player never reads.
        _generateButton = new HudIconButton(HudIcon.Die)
        {
            FocusMode = Control.FocusModeEnum.None,
        };
        _generateButton.Pressed += OnGeneratePressed;
        AudioBus.AttachClick(_generateButton);
        Log.Info(Log.LogCategory.Render,
            "MapEditorHudView: seed LineEdit removed; die-only randomize wired.");

        // Six-position palette array: 0 = hand, 1..N = land swatches, then
        // water, tree, capital, tower. _palette is indexed by these slots.
        _palette = new HexPaletteButton[GameSettings.PlayerConfig.Length + 5];

        // Land cluster — a PanelContainer (chip chrome) wrapping a flippable
        // row: full 1×6 land swatches OR a single cycle button (Compact).
        _landCluster = new PanelContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        };
        // Default land panel chrome; switched to a blue-border variant
        // when a land tool is the active brush (RefreshLandPanelSelectionStyle).
        _landCluster.AddThemeStyleboxOverride("panel", ModalChrome.PalettePanelStyle());
        var landGroup = new BoxContainer();
        _landCluster.AddChild(landGroup);

        _landRow = new BoxContainer();
        _landRow.AddThemeConstantOverride("separation", 4);
        landGroup.AddChild(_landRow);

        for (int i = 0; i < GameSettings.PlayerConfig.Length; i++)
        {
            (_, string hex) = GameSettings.PlayerConfig[i];
            var button = new HexPaletteButton(new Color(hex));
            int paletteIndex = i + 1;
            button.TooltipText = "Paint land for a player color";
            button.Pressed += _ => SelectPalette(paletteIndex);
            AudioBus.AttachClick(button);
            _landRow.AddChild(button);
            _palette[paletteIndex] = button;
        }

        // Compact counterpart: a single squared swatch button that cycles
        // the player color on each press. Squared (68×68) so it matches
        // the water-paint and other tool buttons in scale + chrome;
        // OnViewportMetricsChanged toggles visibility against _landRow
        // based on the base class's Compact bit.
        _landCycleButton = new HexPaletteButton(
            new Color(GameSettings.PlayerConfig[0].Hex), squared: true)
        {
            TooltipText = "Paint land — tap to cycle player color",
            Visible = false,
        };
        _landCycleButton.Pressed += _ => OnLandCyclePressed();
        AudioBus.AttachClick(_landCycleButton);
        // NOT a child of the slate PanelContainer — sits as a sibling
        // of _landCluster in the layout. When compact the cluster panel
        // hides and only the bare cycle button shows (no surrounding
        // frame chrome).

        // Paint cluster — the four terrain tools.
        int waterIndex = WaterPaletteIndex;
        var waterButton = new HexPaletteButton(UiPalette.WaterDeep, squared: true);
        waterButton.TooltipText = "Paint water";
        waterButton.Pressed += _ => SelectPalette(waterIndex);
        AudioBus.AttachClick(waterButton);
        _paintCluster.AddChild(waterButton);
        _palette[waterIndex] = waterButton;

        int treeIndex = TreePaletteIndex;
        var treeButton = new HexPaletteButton(
            new Color(0.42f, 0.30f, 0.18f, 1f), HexPaletteIcon.Tree, squared: true);
        treeButton.TooltipText = "Place / remove a tree";
        treeButton.Pressed += _ => SelectPalette(treeIndex);
        AudioBus.AttachClick(treeButton);
        _paintCluster.AddChild(treeButton);
        _palette[treeIndex] = treeButton;

        int capitalIndex = CapitalPaletteIndex;
        var capitalButton = new HexPaletteButton(
            new Color(0.36f, 0.32f, 0.50f, 1f), HexPaletteIcon.Capital, squared: true);
        capitalButton.TooltipText = "Place a capital";
        capitalButton.Pressed += _ => SelectPalette(capitalIndex);
        AudioBus.AttachClick(capitalButton);
        _paintCluster.AddChild(capitalButton);
        _palette[capitalIndex] = capitalButton;

        int towerIndex = TowerPaletteIndex;
        var towerButton = new HexPaletteButton(
            new Color(0.45f, 0.45f, 0.50f, 1f), HexPaletteIcon.Tower, squared: true);
        towerButton.TooltipText = "Place / remove a tower";
        towerButton.Pressed += _ => SelectPalette(towerIndex);
        AudioBus.AttachClick(towerButton);
        _paintCluster.AddChild(towerButton);
        _palette[towerIndex] = towerButton;

        // Tools cluster — hand (pan, no-paint) + die (random regenerate).
        var handButton = new HexPaletteButton(
            new Color(0.32f, 0.34f, 0.38f, 1f), HexPaletteIcon.Hand, squared: true);
        handButton.TooltipText = "Pan";
        handButton.Pressed += _ => SelectPalette(HandPaletteIndex);
        AudioBus.AttachClick(handButton);
        _toolsCluster.AddChild(handButton);
        _palette[HandPaletteIndex] = handButton;
        _toolsCluster.AddChild(_generateButton);

        SelectPalette(HandPaletteIndex, fireEvent: false);

        // Undo / redo cluster.
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
            _optionsButton = new HudIconButton(HudIcon.Options);
            _optionsButton.Pressed += () => EscRequested?.Invoke();
            AudioBus.AttachClick(_optionsButton);
        }

        InitOrientation();
    }

    // ---- Orientation-aware layout (OrientationHud hooks) -----------------

    protected override void DetachClusters()
    {
        HudBars.Detach(_landCluster);
        HudBars.Detach(_landCycleButton);
        HudBars.Detach(_paintCluster);
        HudBars.Detach(_toolsCluster);
        HudBars.Detach(_undoCluster);
        if (_optionsButton != null) HudBars.Detach(_optionsButton);
    }

    /// <summary>Landscape — D1 rails: paint cluster (land + 4 terrain) in
    /// LeftRail; tools cluster (hand + die) in RightRail; undo/options
    /// stay in TopRightZone. TopLeftZone is empty (editor has no read-only
    /// status to show).</summary>
    protected override void BuildLandscapeBars()
    {
        // Flip the inner BoxContainers to vertical so the buttons stack
        // as rail columns.
        SetClusterVertical(true);

        // Top-right: undo + options. (Top-left is empty — editor has no
        // read-only status block.)
        TopRightZone.AddChild(_undoCluster);
        if (_optionsButton != null) TopRightZone.AddChild(_optionsButton);

        // Left rail (create/paint): land palette panel + compact cycle
        // button (mutually exclusive) + terrain paint tools.
        LeftRailGroup!.AddChild(_landCluster);
        LeftRailGroup!.AddChild(_landCycleButton);
        LeftRailGroup!.AddChild(_paintCluster);

        // Right rail (command/tools): hand + die.
        RightRailGroup!.AddChild(_toolsCluster);

        Log.Debug(Log.LogCategory.Render,
            "MapEditorHudView: landscape cluster placement — undo+options → TopRight, " +
            "landCluster+paintCluster → LeftRail, toolsCluster → RightRail.");
    }

    /// <summary>Portrait — D1 bottom bar: TopRightZone holds undo +
    /// options; BottomBar holds row 1 (hand + die) and row 2
    /// (land palette + terrain paint tools). TopLeftZone is empty.</summary>
    protected override void BuildPortraitBars()
    {
        // Horizontal inside the bottom-bar rows.
        SetClusterVertical(false);

        // Top-right: undo + options.
        TopRightZone.AddChild(_undoCluster);
        if (_optionsButton != null) TopRightZone.AddChild(_optionsButton);

        // Bottom bar — two rows. Apply TopOffsetPx as a top inset (the
        // tutorial builder slides the editor HUD's bottom bar UP by that
        // many px to make room for its own topbar above; legacy compat).
        var inner = new VBoxContainer
        {
            AnchorLeft = 0f, AnchorRight = 1f,
            AnchorTop = 0f, AnchorBottom = 1f,
            OffsetLeft = 16f, OffsetRight = -16f,
            OffsetTop = 10f, OffsetBottom = -(10f + SafeArea.Current.Bottom),
            MouseFilter = Control.MouseFilterEnum.Pass,
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        inner.AddThemeConstantOverride("separation", 8);
        BottomBar!.AddChild(inner);

        var row1 = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        row1.AddThemeConstantOverride("separation", 14);
        row1.AddChild(_toolsCluster);
        inner.AddChild(row1);

        var row2 = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        row2.AddThemeConstantOverride("separation", 14);
        row2.AddChild(_landCluster);
        row2.AddChild(_landCycleButton);
        row2.AddChild(_paintCluster);
        inner.AddChild(row2);

        Log.Debug(Log.LogCategory.Render,
            "MapEditorHudView: portrait cluster placement — undo+options → TopRight, " +
            "toolsCluster → BottomBar.row1, landCluster+paintCluster → BottomBar.row2.");
    }

    protected override MapInsets ComputeInsets()
    {
        // Floating layout: the editor map fills the viewport. Rails /
        // bottom bar / corner chips overlay it. No vertical inset.
        return new MapInsets(0f, 0f);
    }

    /// <summary>Land palette: full row vs cycle button driven by Compact.
    /// Mirrors HudView's buy-palette collapse — same breakpoint, same
    /// signal.</summary>
    protected override void OnViewportMetricsChanged()
    {
        bool compact = Compact;
        // Hide the slate-framed land panel entirely in compact mode (the
        // bare cycle button takes its slot) so the cycle button doesn't
        // appear inside the panel's chrome frame.
        _landCluster.Visible = !compact;
        _landCycleButton.Visible = compact;
        RefreshLandCycleVisual();
        Log.Debug(Log.LogCategory.Render,
            $"MapEditorHudView: metrics orient={Orientation} compact={compact} " +
            $"land={(compact ? "cycle (bare)" : "1x6 panel")}");
    }

    private void SetClusterVertical(bool vertical)
    {
        _paintCluster.Vertical = vertical;
        _toolsCluster.Vertical = vertical;
        _landRow.Vertical = vertical;
        // The landGroup wrapper (parent of _landRow + _landCycleButton)
        // doesn't need to flip — only one child is visible at a time, so
        // its axis is irrelevant.
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

    private void OnGeneratePressed()
    {
        // Fresh random seed every press — the user no longer types a
        // specific seed; the die is the lone "give me a different map"
        // affordance. After regenerating, drop back to the hand tool
        // so the player can immediately pan / inspect without
        // accidentally repainting the fresh map.
        int seed = new System.Random().Next(SeedMin, SeedMax + 1);
        _generateButton.FlashPress();
        Log.Debug(Log.LogCategory.Input, $"[MapEditor] die press → seed={seed}");
        GenerateRequested?.Invoke(seed);
        SelectHand();
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
        if (IsLandIndex(index)) _lastLandPaletteIndex = index;
        RefreshLandCycleVisual();
        RefreshLandPanelSelectionStyle();
        if (fireEvent) PaletteSelectionChanged?.Invoke(index);
    }

    /// <summary>Swap the land palette panel's chrome between the neutral
    /// slate stylebox and a SelectionRing-bordered variant whenever the
    /// active brush is one of the six land colors. Mirrors the per-button
    /// cool-blue ring used by the squared tool buttons so the "this
    /// group is active" signal is consistent.</summary>
    private void RefreshLandPanelSelectionStyle()
    {
        StyleBoxFlat style = ModalChrome.PalettePanelStyle();
        if (IsLandIndex(SelectedPaletteIndex))
        {
            style.BorderColor = UiPalette.SelectionRing;
            style.SetBorderWidthAll(3);
        }
        _landCluster.AddThemeStyleboxOverride("panel", style);
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
}
