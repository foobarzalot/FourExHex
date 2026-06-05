using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Top-strip heads-up display. A passive view: builds its widgets once in
/// <see cref="_Ready"/>, raises C# events for each button press, and
/// updates label text / button disabled state when the controller calls
/// <see cref="Refresh"/>. Owns no game data.
/// </summary>
public partial class HudView : OrientationHud, IHudView
{
    // Kept for external callers (e.g. tutorial builder chrome heights).
    // The D1 floating layout no longer renders an opaque slate bar at this
    // height; the value persists as a layout token only.
    public const float HudHeight = 96f;

    public event Action? BuyRecruitClicked;
    public event Action<UnitLevel>? BuyUnitClicked;
    public event Action? BuildTowerClicked;
    public event Action? UndoLastClicked;
    public event Action? UndoTurnClicked;
    public event Action? RedoLastClicked;
    public event Action? RedoAllClicked;
    public event Action? EndTurnClicked;
    public event Action? NewGameClicked;
    public event Action? MainMenuClicked;
    public event Action? NextTerritoryClicked;
    public event Action? PreviousTerritoryClicked;
    public event Action? NextUnitClicked;
    public event Action? PreviousUnitClicked;
    public event Action? CancelActionPressed;
    public event Action? EscRequested;
    public event Action? DefeatContinueClicked;
    public event Action? ClaimVictoryWinNowClicked;
    public event Action? ClaimVictoryContinueClicked;
    public event Action? ReplayClicked;
    // Tutorial-recorder only: the Add Text button (speech-bubble glyph)
    // is hidden by default and revealed via SetAddTextButtonVisible, so
    // it never shows in normal play or preview.
    public event Action? AddTextClicked;

    private Label _turnLabel = null!;       // numeric mono turn number
    private PlayerSwatchBar _playerSwatchBar = null!; // players in turn order, current highlighted
    private Label _turnEyebrow = null!;     // "TURN" caption (hidden in portrait)
    private Label _goldLabel = null!;       // gold + income breakdown
    private PanelContainer _goldChip = null!;
    private Label _seedLabel = null!;
    private static readonly Font GeistFont =
        GD.Load<FontFile>("res://fonts/Geist-VariableFont.ttf");
    private static readonly Font MonoFont =
        GD.Load<FontFile>("res://fonts/JetBrainsMono-VariableFont.ttf");
    private static readonly Font SerifFont =
        GD.Load<FontFile>("res://fonts/DMSerifDisplay-Regular.ttf");
    // One radio button per buy level (Recruit / Soldier / Captain / Commander),
    // in cycle order. _buyUnitButtons[(int)level] gives the button for
    // a given UnitLevel. _buyUnitButtons[0] = Recruit (the legacy
    // CtaButton.BuyRecruit target).
    private HudIconButton[] _buyUnitButtons = null!;
    // The four-button row and a single collapsed cycling button live side by
    // side in the palette panel; exactly one is visible (width-driven, set in
    // OnViewportMetricsChanged). The collapsed button fires the same
    // BuyRecruitClicked cycle event as the U hotkey.
    private BoxContainer _paletteRow = null!;
    private HudIconButton _collapsedBuyButton = null!;
    private HudIconButton _buildTowerButton = null!;
    private HudIconButton _undoLastButton = null!;
    private HudIconButton _redoLastButton = null!;
    private bool _undoRedoLocked;
    private bool _victoryOverlaySuppressed;
    private HudIconButton _nextUnitButton = null!;
    private HudIconButton _nextTerritoryButton = null!;
    private HudIconButton _endTurnButton = null!;
    private HudIconButton _optionsButton = null!;
    private HudIconButton _addTextButton = null!;
    private Control _victoryOverlay = null!;
    private Label _victoryLabel = null!;
    private Control _defeatOverlay = null!;
    private Label _defeatLabel = null!;
    private Control _claimVictoryOverlay = null!;
    private Button _defeatContinueButton = null!;
    private Button _claimWinNowButton = null!;
    private Button _claimContinueButton = null!;
    private Panel _tutorialPanel = null!;
    private Label _tutorialLabel = null!;
    private Label _continueHint = null!;
    private Tween? _continueHintTween;
    private Panel _bankruptToast = null!;
    private Label _bankruptTitleLabel = null!;
    private Label _bankruptSubLabel = null!;

    // Persistent clusters, built once and reparented between the bars
    // (TopBar/BottomBar, owned by OrientationHud) on a landscape↔portrait flip.
    private Control _statusCluster = null!;       // TURN # + swatch bar (raw cluster)
    private PanelContainer _statusChip = null!;   // chip-styled wrapper around _statusCluster (matches gold chip)
    private BoxContainer _actionCluster = null!;  // buy palette + Build Tower + Add Text (flips H↔V)
    private Control _undoCluster = null!;         // undo + redo
    private BoxContainer _controlsCluster = null!;// next unit + next territory (flips H↔V)

    // Snapshot of session.Mode != None at the last Refresh, so the Escape
    // handler can decide between cancel-action (pending) and End Game (idle)
    // without holding a SessionState reference.
    private bool _hasPendingAction;

    public override void _Ready()
    {
        // Build the three persistent widget clusters as parentless HBoxes.
        // ApplyLayout parents them (plus the gold chip) into orientation-
        // specific bars; on a landscape↔portrait flip they're reparented,
        // never rebuilt, so their event wiring and disabled/CTA state survive.
        // MouseFilter Pass keeps the clusters click-through to leaf children
        // only. (The slate bar background + click-blocking now live on the
        // bar Panels created in ApplyLayout, not a standalone background.)
        // All clusters use 8-px separation so the horizontal gaps within a
        // row match the vertical gap between rows (the bottom-bar VBox uses
        // 8 too). Mixing 14 + 8 looked irregular in the bottom-left grid.
        // MouseFilter Ignore on the read-only status cluster (and on every
        // descendant) so taps in the chip area fall through to the map
        // below — the chip is a display, not a button.
        _statusCluster = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _statusCluster.AddThemeConstantOverride("separation", 8);
        // Action + controls clusters use plain BoxContainer so they can
        // flip Vertical/horizontal between portrait rows and landscape rails.
        _actionCluster = new BoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        _actionCluster.AddThemeConstantOverride("separation", 8);
        _undoCluster = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        _undoCluster.AddThemeConstantOverride("separation", 8);
        _controlsCluster = new BoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        _controlsCluster.AddThemeConstantOverride("separation", 8);

        // 1) Current-player block — a row of color swatches, one per
        // player in movement order, with the current player's swatch
        // enlarged + white-outlined and eliminated players dimmed in
        // place. Placed first so it leads the status row.
        _playerSwatchBar = new PlayerSwatchBar
        {
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _statusCluster.AddChild(_playerSwatchBar);

        // 2) Turn block — small "TURN" eyebrow over a mono number. Sits
        // immediately right of the swatch row (no divider) so the turn
        // counter reads as part of the active-player display.
        Control turnBlock = BuildEyebrowBlock("TURN", out _turnLabel, mono: true, valueColor: UiPalette.Ink, out _turnEyebrow);
        SetClickThrough(turnBlock);
        _statusCluster.AddChild(turnBlock);
        _turnLabel.Text = "1";
        _turnLabel.CustomMinimumSize = new Vector2(70, 0);
        _turnLabel.AddThemeFontSizeOverride("font_size", 36);

        // Wrap the status cluster in a black-pill chip matching the gold
        // chip's style. MouseFilter = Ignore so the chip is click-
        // through (taps in its footprint reach the map below).
        _statusChip = new PanelContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _statusChip.AddThemeStyleboxOverride("panel", BuildHudChipStyle());
        _statusChip.AddChild(_statusCluster);

        // 3) Gold chip — bg-deep pill containing the gold value + the
        // income breakdown. The font matches the turn counter (36) so the
        // two chips read at the same heading scale; the chip auto-sizes
        // to the larger label.
        var goldChip = new PanelContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            // Click-through — gold chip is a read-only readout, not a
            // button; taps fall through to the map.
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        goldChip.AddThemeStyleboxOverride("panel", BuildHudChipStyle());
        _goldLabel = new Label
        {
            Text = "",
            CustomMinimumSize = new Vector2(220, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _goldLabel.AddThemeFontOverride("font", MonoFont);
        _goldLabel.AddThemeFontSizeOverride("font_size", 36);
        goldChip.AddChild(_goldLabel);
        _goldChip = goldChip;

        // 4) Unit palette — D1 floating layout: no rounded slate backdrop
        // around the buy buttons. The four buy radios (Recruit/Soldier/
        // Captain/Commander) live in `paletteRow`; the single collapsed
        // cycle button (`_collapsedBuyButton`) is added as a sibling of
        // paletteRow inside `_actionCluster`. Exactly one is visible at a
        // time — hidden children take no space in the BoxContainer — so
        // paletteRow and the cycle button are mutually exclusive without a
        // shared wrapper. paletteRow is a BoxContainer (not HBox) so it
        // can flip Vertical in a landscape rail.
        var paletteRow = new BoxContainer();
        paletteRow.AddThemeConstantOverride("separation", 2);
        _paletteRow = paletteRow;
        _actionCluster.AddChild(paletteRow);

        UnitLevel[] buyLevels = { UnitLevel.Recruit, UnitLevel.Soldier, UnitLevel.Captain, UnitLevel.Commander };
        _buyUnitButtons = new HudIconButton[buyLevels.Length];
        for (int i = 0; i < buyLevels.Length; i++)
        {
            UnitLevel level = buyLevels[i];
            var button = new HudIconButton(HudIcon.Recruit)
            {
                Disabled = true,
                BuyLevel = level,
            };
            button.Pressed += () => BuyUnitClicked?.Invoke(level);
            AudioBus.AttachClick(button);
            paletteRow.AddChild(button);
            _buyUnitButtons[i] = button;
        }

        // Collapsed-mode counterpart: a single unit button that fires the same
        // BuyRecruitClicked cycle event as the U hotkey (select-lowest-affordable
        // → advance → exit, owned by GameController.OnBuyPressed). Built hidden —
        // OnViewportMetricsChanged flips it on when the viewport is narrow. Its
        // BuyLevel/Selected are synced to the active buy mode in Refresh().
        _collapsedBuyButton = new HudIconButton(HudIcon.Recruit)
        {
            Disabled = true,
            BuyLevel = UnitLevel.Recruit,
            TooltipText = "Buy unit — cycles affordable levels (U)",
            Visible = false,
        };
        _collapsedBuyButton.Pressed += () => BuyRecruitClicked?.Invoke();
        AudioBus.AttachClick(_collapsedBuyButton);
        // Sibling of paletteRow inside _actionCluster (the rounded slate
        // wrapper that used to group them is gone). One is visible at a
        // time — OnViewportMetricsChanged toggles paletteRow vs cycle
        // button based on Compact.
        _actionCluster.AddChild(_collapsedBuyButton);

        _buildTowerButton = new HudIconButton(HudIcon.Tower) { Disabled = true };
        _buildTowerButton.Pressed += () => BuildTowerClicked?.Invoke();
        AudioBus.AttachClick(_buildTowerButton);
        _actionCluster.AddChild(_buildTowerButton);

        // Tutorial-recorder authoring affordance, parked just right of
        // Build Tower. Hidden by default (an invisible Control takes no
        // space in the HBox), so normal play and preview never see it;
        // RecordPane reveals it via SetAddTextButtonVisible. Refresh()
        // deliberately never touches its visibility, so the reveal sticks.
        // Because the center group is anchored to the bar midpoint and grows
        // both ways, revealing this button nudges the group's left edge out
        // a little but keeps it centered — it never displaces the right
        // controls.
        _addTextButton = new HudIconButton(HudIcon.AddText) { Visible = false };
        _addTextButton.Pressed += () => AddTextClicked?.Invoke();
        AudioBus.AttachClick(_addTextButton);
        _actionCluster.AddChild(_addTextButton);

        // 5) Undo / Redo — two ghost icon buttons. A short click is
        // Undo/Redo Last; holding past the long-press threshold fires
        // Undo All / Redo All (the same actions Shift+Z / Shift+Y reach).
        _undoLastButton = new HudIconButton(HudIcon.UndoLast)
        {
            Disabled = true,
            TooltipText = "Undo — Z (hold for Undo All)",
        };
        _undoLastButton.Pressed += () =>
        {
            if (_undoLastButton.ConsumeLongPress()) return;
            UndoLastClicked?.Invoke();
        };
        _undoLastButton.LongPressed += () =>
        {
            Log.Debug(Log.LogCategory.Input, "Undo button long-press -> Undo All");
            UndoTurnClicked?.Invoke();
        };
        AudioBus.AttachClick(_undoLastButton);
        _undoCluster.AddChild(_undoLastButton);

        _redoLastButton = new HudIconButton(HudIcon.RedoLast)
        {
            Disabled = true,
            TooltipText = "Redo — Y (hold for Redo All)",
        };
        _redoLastButton.Pressed += () =>
        {
            if (_redoLastButton.ConsumeLongPress()) return;
            RedoLastClicked?.Invoke();
        };
        _redoLastButton.LongPressed += () =>
        {
            Log.Debug(Log.LogCategory.Input, "Redo button long-press -> Redo All");
            RedoAllClicked?.Invoke();
        };
        AudioBus.AttachClick(_redoLastButton);
        _undoCluster.AddChild(_redoLastButton);

        // Next unmoved unit in the selected territory — same action as
        // the N hotkey. Sits to the LEFT of Next Territory so the two
        // "next ..." buttons read as a stepped pair. Disabled when no
        // unmoved units remain in the selection; highlighted while
        // SessionState.RepeatedMovement is on.
        _nextUnitButton = new HudIconButton(HudIcon.NextUnit);
        _nextUnitButton.Pressed += () => NextUnitClicked?.Invoke();
        AudioBus.AttachClick(_nextUnitButton);
        _controlsCluster.AddChild(_nextUnitButton);

        // Next active territory — same action as the Tab hotkey. Disabled
        // exactly when End Turn is the CTA (no actionable territory left).
        _nextTerritoryButton = new HudIconButton(HudIcon.NextTerritory);
        _nextTerritoryButton.Pressed += () => NextTerritoryClicked?.Invoke();
        AudioBus.AttachClick(_nextTerritoryButton);
        _controlsCluster.AddChild(_nextTerritoryButton);

        // End Turn uses the default Button theme — the SetCta() white
        // pulse remains the only "this is the current CTA" signal.
        // End Turn is reparented per orientation (end of the controls cluster in
        // landscape; the top display bar's right side in portrait, just left of
        // Options), so it isn't added to a cluster here — the Build*Bars
        // methods place it.
        // End Turn now matches the nav buttons (Next Unit / Next Territory) —
        // dark slate chrome with the shared black border. The white CTA pulse
        // still kicks in when the controller flags it as the current CTA
        // (no actionable territories remain).
        _endTurnButton = new HudIconButton(HudIcon.EndTurn);
        _endTurnButton.Pressed += () => EndTurnClicked?.Invoke();
        AudioBus.AttachClick(_endTurnButton);

        // Single Options button — raises the same EscRequested event
        // the Escape key fires, so the scene root's pause coordinator
        // drives both paths. Save Game and Settings live inside that
        // pause menu now rather than as standalone HUD buttons.
        // Options is reparented per orientation (end of the controls cluster in
        // landscape; the top display bar's right side in portrait), so it isn't
        // added to a cluster here — the Build*Bars methods place it.
        _optionsButton = new HudIconButton(HudIcon.Options);
        _optionsButton.Pressed += () => EscRequested?.Invoke();
        AudioBus.AttachClick(_optionsButton);

        // Read-only seed / map-name display tucked in the bottom-left
        // safe-area strip — below the action buttons, sharing the iOS
        // home-indicator strip on notched devices. Click-through so taps
        // in its footprint reach the map.
        _seedLabel = new Label
        {
            Text = "",
            AnchorLeft = 0f,
            AnchorRight = 0f,
            AnchorTop = 1f,
            AnchorBottom = 1f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _seedLabel.AddThemeFontSizeOverride("font_size", 18);
        _seedLabel.AddThemeColorOverride("font_color", UiPalette.InkSoft);
        AddChild(_seedLabel);

        // Arrange the clusters for the current orientation + track resize
        // (OrientationHud owns the bars + the flip/publish lifecycle).
        InitOrientation();

        BuildVictoryOverlay();
        BuildDefeatOverlay();
        BuildClaimVictoryOverlay();
        BuildTutorialOverlay();
        BuildBankruptToast();
    }

    // ---- Orientation-aware layout (OrientationHud hooks) -----------------

    protected override void DetachClusters()
    {
        // _statusChip wraps _statusCluster — detach the chip (the parented
        // node), not the inner cluster.
        HudBars.Detach(_statusChip);
        HudBars.Detach(_goldChip);
        HudBars.Detach(_actionCluster);
        HudBars.Detach(_undoCluster);
        HudBars.Detach(_controlsCluster);
        // Options + End Turn migrate between zones per orientation; detach
        // them so freeing the old zones can't free them.
        HudBars.Detach(_optionsButton);
        HudBars.Detach(_endTurnButton);
    }

    /// <summary>Landscape — D1 zones: TopLeftZone holds status + gold;
    /// TopRightZone holds undo + options (always); LeftRail holds the
    /// create/paint cluster (buy palette + Build Tower); RightRail holds
    /// the command cluster (nav) + End Turn (hero) stacked vertically.
    /// Rails align Center (compact) or End (expanded) — set by
    /// HudBars.MakeRail.</summary>
    protected override void BuildLandscapeBars()
    {
        // Flip the action + controls clusters to vertical for the rails.
        SetClusterVertical(true);

        // Top-left: status chip (turn + swatches) + gold chip, inline.
        _statusChip.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _goldChip.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        TopLeftZone.AddChild(_statusChip);
        TopLeftZone.AddChild(_goldChip);

        // Top-right: undo cluster + options gear.
        TopRightZone.AddChild(_undoCluster);
        TopRightZone.AddChild(_optionsButton);

        // Left rail: buy palette + Build Tower, vertically centered.
        LeftRailGroup!.AddChild(_actionCluster);

        // Right rail: nav (next unit + next territory), vertically centered
        // — mirrors the left rail's pair. End Turn is NOT in the rail.
        RightRailGroup!.AddChild(_controlsCluster);

        // End Turn floats anchored to the bottom-right corner of the
        // viewport, inside the safe-area inset.
        PinEndTurnBottomRight();

        // In expanded landscape the right rail bottom-anchors its content
        // (alignBottom = true). End Turn pinned to the bottom-right corner
        // would then collide with the bottom-most nav button. Push the
        // rail group's bottom edge UP by End Turn's footprint so the nav
        // cluster sits just above the End Turn slot. Compact landscape
        // centers the group vertically and doesn't need this clearance.
        if (!Compact && RightRailGroup != null)
        {
            const float endTurnClearance = 68f + 20f;   // button + spacing
            RightRailGroup.OffsetBottom -= endTurnClearance;
        }

        Log.Debug(Log.LogCategory.Render,
            "HudView: landscape cluster placement — statusChip+goldChip → TopLeft, " +
            "undoCluster+optionsButton → TopRight, actionCluster → LeftRail, " +
            "controlsCluster → RightRail (centered), endTurnButton → bottom-right corner.");
    }

    /// <summary>Place End Turn at the bottom-right corner of the viewport,
    /// inside the safe-area inset, as a direct child of this CanvasLayer
    /// (anchored — no container layout interference).</summary>
    private void PinEndTurnBottomRight()
    {
        // End Turn sits at the literal bottom-right corner — does NOT
        // respect safe-area insets (it claims the corner real estate the
        // rails leave behind). On iPhone landscape it'll overlap the
        // home-indicator strip; iOS still routes taps through.
        float pad = 10f;
        _endTurnButton.AnchorLeft = 1f;
        _endTurnButton.AnchorRight = 1f;
        _endTurnButton.AnchorTop = 1f;
        _endTurnButton.AnchorBottom = 1f;
        _endTurnButton.GrowHorizontal = Control.GrowDirection.Begin;
        _endTurnButton.GrowVertical = Control.GrowDirection.Begin;
        _endTurnButton.OffsetLeft = -pad;
        _endTurnButton.OffsetRight = -pad;
        _endTurnButton.OffsetTop = -pad;
        _endTurnButton.OffsetBottom = -pad;
        AddChild(_endTurnButton);
    }

    /// <summary>Undo the corner-anchoring landscape applied, so the next
    /// portrait Build*Bars can drop End Turn into a Container without the
    /// anchors fighting the container's layout.</summary>
    private void ResetEndTurnAnchors()
    {
        _endTurnButton.AnchorLeft = 0f;
        _endTurnButton.AnchorRight = 0f;
        _endTurnButton.AnchorTop = 0f;
        _endTurnButton.AnchorBottom = 0f;
        _endTurnButton.OffsetLeft = 0f;
        _endTurnButton.OffsetRight = 0f;
        _endTurnButton.OffsetTop = 0f;
        _endTurnButton.OffsetBottom = 0f;
        _endTurnButton.GrowHorizontal = Control.GrowDirection.End;
        _endTurnButton.GrowVertical = Control.GrowDirection.End;
    }

    /// <summary>Portrait — D1 zones (wireframe variant A): TopLeftZone holds
    /// status above gold; TopRightZone holds undo + options; BottomBar
    /// holds a VBox of two full-width rows:
    ///  - row1 = `[nextUnit · nextTerritory] ←→ [End Turn (hero)]` (space-between)
    ///  - row2 = `[Buy (hero) · Build Tower]` (left-aligned)
    /// Mirrors `hud-d1.jsx` variant A precisely — hero buttons sit at
    /// opposite corners (End Turn top-right of the bar, Buy at the bottom-
    /// left thumb spot).</summary>
    protected override void BuildPortraitBars()
    {
        // Action + controls clusters render horizontally in the bottom-bar rows.
        SetClusterVertical(false);
        // Wipe any anchor state landscape applied to End Turn (corner pin)
        // so the bottom-bar Container can size/place it cleanly.
        ResetEndTurnAnchors();

        // Top-left: status chip above gold chip. Status sticks to its
        // natural width via ShrinkBegin — without it, the VBox stretches
        // status horizontally to match the wider gold chip when a
        // territory is selected, which the player reads as the status
        // chip "growing" on selection.
        var tlStack = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        tlStack.AddThemeConstantOverride("separation", 4);
        _statusChip.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        tlStack.AddChild(_statusChip);
        _goldChip.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        _goldChip.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        tlStack.AddChild(_goldChip);
        TopLeftZone.AddChild(tlStack);

        // Top-right: undo + options.
        TopRightZone.AddChild(_undoCluster);
        TopRightZone.AddChild(_optionsButton);

        // Bottom bar — full-width transparent strip; two rows of buttons,
        // each row fills the width so internal justify works as designed.
        var inner = new VBoxContainer
        {
            AnchorLeft = 0f, AnchorRight = 1f,
            AnchorTop = 0f, AnchorBottom = 1f,
            OffsetLeft = 16f, OffsetRight = -16f,
            OffsetTop = 10f, OffsetBottom = -(10f + SafeArea.Current.Bottom),
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        inner.AddThemeConstantOverride("separation", 8);
        BottomBar!.AddChild(inner);

        // Row 1 — left-aligned nav: [next unit · next territory].
        var row1 = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.Fill,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        row1.AddChild(_controlsCluster);
        inner.AddChild(row1);

        // Row 2 — space-between: [Buy · Build Tower] at left, End Turn at right.
        // Hero actions cluster at the bottom thumb spot; End Turn anchored
        // to the right edge as a hero too.
        var row2 = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.Fill,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        row2.AddThemeConstantOverride("separation", 8);
        row2.AddChild(_actionCluster);
        var row2Spacer = new Control
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        row2.AddChild(row2Spacer);
        row2.AddChild(_endTurnButton);
        inner.AddChild(row2);

        Log.Debug(Log.LogCategory.Render,
            "HudView: portrait cluster placement — statusCluster+goldChip → TopLeft, " +
            "undoCluster+optionsButton → TopRight, controlsCluster → BottomBar.row1 (nav, left), " +
            "actionCluster + endTurnButton → BottomBar.row2 (space-between).");
    }

    /// <summary>Post-layout (orientation / compact flip): reposition the seed
    /// label so it doesn't tuck under the floating corner zones; reposition
    /// the tutorial overlay over the bottom bar (portrait) or near the
    /// bottom (landscape, no bottom bar).</summary>
    protected override void OnLayoutApplied()
    {
        _seedLabel.Visible = true;
        // Bottom-left, INSIDE the safe-area-bottom strip — below the
        // button rows so it sits where the iPhone home indicator lives.
        // OffsetLeft matches the bottom-bar's inner-VBox left padding
        // (16 px) so the label lines up under the leftmost button column.
        // Bottom margin sits just inside the safe-area; nudged up so the
        // label doesn't graze the indicator on iPhone.
        float seedBottom = -10f;
        float seedTop = seedBottom - 22f;
        _seedLabel.OffsetTop = seedTop;
        _seedLabel.OffsetBottom = seedBottom;
        // Nudged right of the bottom-bar inner padding so the label
        // tucks under the right edge of the leftmost button column
        // rather than hugging the viewport edge.
        _seedLabel.OffsetLeft = 36f;
        _seedLabel.OffsetRight = 316f;
        Log.Debug(Log.LogCategory.Render,
            $"HudView: seed label in safe-area bottom-left ({Orientation}).");
        PositionTutorialOverlay();
    }

    /// <summary>Swap collapsed↔expanded variants of the palette + roster
    /// based on the unified Compact state from the base class. The TURN
    /// eyebrow is hidden when compact (no room beside the swatches /
    /// number).</summary>
    protected override void OnViewportMetricsChanged()
    {
        bool compact = Compact;
        _turnEyebrow.Visible = !compact;
        _playerSwatchBar.SetCompact(compact);
        _paletteRow.Visible = !compact;
        _collapsedBuyButton.Visible = compact;
        Log.Debug(Log.LogCategory.Render,
            $"HudView: metrics orient={Orientation} compact={compact} " +
            $"swatch={(compact ? "single" : "roster")} buy={(compact ? "cycle" : "1x4")}");

        // Re-fit width-capped overlays.
        PositionTutorialOverlay();
        PositionBankruptToast();
    }

    protected override MapInsets ComputeInsets()
    {
        // D1 is a true floating HUD: the map fills the viewport and the
        // corner chips / bottom bar / rails sit on top. The map reserves
        // no vertical inset, so a landscape window now shows tiles edge-
        // to-edge top and bottom (rails are horizontal-only insets,
        // which the map view doesn't track).
        return new MapInsets(0f, 0f);
    }

    /// <summary>Flip the action / controls / palette-row BoxContainers
    /// between horizontal (portrait bottom-bar rows) and vertical
    /// (landscape side rails). The visible variant inside the buy palette
    /// (collapsed cycle button vs 1×4 paletteRow) is governed separately
    /// by Compact in <see cref="OnViewportMetricsChanged"/>.</summary>
    private void SetClusterVertical(bool vertical)
    {
        _actionCluster.Vertical = vertical;
        _controlsCluster.Vertical = vertical;
        _paletteRow.Vertical = vertical;
    }

    /// <summary>Recursively set <c>MouseFilter = Ignore</c> on a node and
    /// every descendant Control, so taps in its footprint fall through to
    /// whatever sits behind it (the map). Used for read-only display
    /// chips that have no interactive children.</summary>
    private static void SetClickThrough(Node node)
    {
        if (node is Control c) c.MouseFilter = Control.MouseFilterEnum.Ignore;
        foreach (Node child in node.GetChildren())
        {
            SetClickThrough(child);
        }
    }

    /// <summary>Shared chip-pill stylebox used by both the status chip
    /// (turn # + swatch row) and the gold chip (treasury readout). Black
    /// fill, line-soft border, 8-px radius, generous content margins to
    /// accommodate the 36-pt heading text the chips wrap.</summary>
    private static StyleBoxFlat BuildHudChipStyle()
    {
        return new StyleBoxFlat
        {
            BgColor = UiPalette.BgDeep,
            BorderColor = UiPalette.LineSoft,
            BorderWidthLeft = 1, BorderWidthRight = 1,
            BorderWidthTop = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
            ContentMarginLeft = 12, ContentMarginRight = 12,
            ContentMarginTop = 10, ContentMarginBottom = 10,
        };
    }

    public void SetMapLabel(string text)
    {
        _seedLabel.Text = text;
    }

    /// <summary>
    /// Reveal (or hide) the tutorial-recorder Add Text button. Only the
    /// tutorial RecordPane calls this; normal play leaves it hidden.
    /// </summary>
    public void SetAddTextButtonVisible(bool visible)
    {
        _addTextButton.Visible = visible;
        Log.Debug(Log.LogCategory.Tutorial, $"[HudView] Add Text button visible={visible}");
    }

    // Small uppercase "TURN" / "TO PLAY" eyebrow label sitting side-by-
    // side with the value (eyebrow on the left, big value on the right,
    // both center-aligned vertically). The value label is handed back
    // via out so the caller can poke at its text/font/color after the
    // block is in the tree. Caller sets the value text/size/color/font
    // (so the mono numeric and the player-name treatments stay specific).
    private Control BuildEyebrowBlock(string eyebrow, out Label value, bool mono, Color valueColor, out Label eyebrowLabel)
    {
        var block = new HBoxContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        block.AddThemeConstantOverride("separation", 10);

        eyebrowLabel = new Label
        {
            Text = eyebrow,
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        eyebrowLabel.AddThemeFontOverride("font", GeistFont);
        eyebrowLabel.AddThemeFontSizeOverride("font_size", 20);
        eyebrowLabel.AddThemeColorOverride("font_color", UiPalette.Gold);
        block.AddChild(eyebrowLabel);

        value = new Label
        {
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        if (mono) value.AddThemeFontOverride("font", MonoFont);
        else      value.AddThemeFontOverride("font", GeistFont);
        value.AddThemeColorOverride("font_color", valueColor);
        block.AddChild(value);

        return block;
    }

    // 1×24 vertical divider in line-soft, used between the three regions
    // of the top bar (status / palette / controls) and inside the unit
    // palette panel to split Buy from Build.
    private static Control BuildVerticalDivider()
    {
        return new ColorRect
        {
            Color = UiPalette.LineSoft,
            CustomMinimumSize = new Vector2(1, 24),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
    }

    public event Action? TutorialMessageTapped;

    /// <summary>
    /// Full-viewport overlay used while a tappable tutorial (display-text)
    /// beat is showing. Built lazily on first tappable-show. Two jobs:
    /// (1) a semi-transparent black fill that dims the rest of the screen
    /// so the narration reads as a modal — this is specific to the
    /// display-text beat, not the other (click-through) uses of the
    /// tutorial-message panel; (2) a click catcher (MouseFilter=Stop) so a
    /// tap anywhere advances the beat and the player can't hit
    /// Buy / End Turn / select a tile while it's gated. It sits BELOW the
    /// narration panel + continue-hint (which are MouseFilter=Ignore, so
    /// taps on them fall through to this catcher) but ABOVE the map/HUD,
    /// so only the board and buttons are dimmed.
    /// </summary>
    private ColorRect? _tutorialTapCatcher;

    // Dim level applied to the rest of the screen behind a display-text
    // beat. Black at this alpha reads as a clear modal scrim without
    // hiding the board state the narration refers to.
    private static readonly Color TutorialDimColor = new(0f, 0f, 0f, 0.5f);

    // True while an external caller (tutorial system, AI-batch indicator)
    // owns the tutorial-message panel. Refresh()'s gameplay action hint
    // ("Click to place a Recruit", "Click to move the Captain") only
    // writes to the panel when this is false, so the tutorial step text
    // and the AI batch announcement aren't clobbered by the per-Refresh
    // hint pass.
    private bool _externalMessageActive;

    public void ShowTutorialMessage(string text)
    {
        _externalMessageActive = true;
        _tutorialLabel.Text = text;
        _tutorialPanel.Visible = true;
        SetTutorialTapCatcherEnabled(false);
    }

    public void ShowTappableTutorialMessage(string text)
    {
        _externalMessageActive = true;
        _tutorialLabel.Text = text;
        _tutorialPanel.Visible = true;
        SetTutorialTapCatcherEnabled(true);
        ShowContinueHint(true);
        Log.Debug(Log.LogCategory.Tutorial,
            $"[HudView] tappable tutorial message shown; screen dimmed; continue-hint scheduled in {ContinueHintDelaySeconds}s");
    }

    public void HideTutorialMessage()
    {
        _externalMessageActive = false;
        _tutorialPanel.Visible = false;
        SetTutorialTapCatcherEnabled(false);
        ShowContinueHint(false);
    }

    // Short beat between the narration text appearing and the flashing
    // continue-hint surfacing, so the player reads the message first.
    private const double ContinueHintDelaySeconds = 0.8;

    // Bumped on every show/hide so a pending delayed reveal whose beat was
    // already dismissed (player tapped fast) is invalidated and no-ops.
    private int _continueHintGen;

    private void ShowContinueHint(bool show)
    {
        _continueHintGen++;
        if (show)
        {
            int gen = _continueHintGen;
            SceneTreeTimer timer = GetTree().CreateTimer(ContinueHintDelaySeconds);
            timer.Timeout += () => RevealContinueHint(gen);
        }
        else
        {
            StopContinueHintPulse();
            _continueHint.Visible = false;
        }
    }

    private void RevealContinueHint(int gen)
    {
        if (gen != _continueHintGen) return; // beat already dismissed
        Log.Debug(Log.LogCategory.Tutorial, "[HudView] continue-hint revealed; flashing");
        _continueHint.Visible = true;
        // Keep the hint above the invisible tap catcher so it's not
        // visually occluded; the catcher still owns the click.
        MoveChild(_continueHint, GetChildCount() - 1);
        StartContinueHintPulse();
    }

    private void StartContinueHintPulse()
    {
        if (_continueHintTween != null && _continueHintTween.IsValid())
        {
            return; // already pulsing
        }
        _continueHintTween = _continueHint.CreateTween();
        _continueHintTween.SetLoops();
        _continueHintTween.TweenProperty(_continueHint, "modulate:a", 0.25f, 0.6).SetTrans(Tween.TransitionType.Sine);
        _continueHintTween.TweenProperty(_continueHint, "modulate:a", 1.0f, 0.6).SetTrans(Tween.TransitionType.Sine);
    }

    private void StopContinueHintPulse()
    {
        if (_continueHintTween != null && _continueHintTween.IsValid())
        {
            _continueHintTween.Kill();
        }
        _continueHintTween = null;
        _continueHint.Modulate = new Color(1f, 1f, 1f, 1f);
    }

    private void SetTutorialTapCatcherEnabled(bool enabled)
    {
        if (enabled)
        {
            if (_tutorialTapCatcher == null)
            {
                Vector2 viewport = GetViewport().GetVisibleRect().Size;
                _tutorialTapCatcher = new ColorRect
                {
                    Color = TutorialDimColor,
                    Position = Vector2.Zero,
                    Size = viewport,
                    AnchorLeft = 0f, AnchorRight = 1f,
                    AnchorTop = 0f, AnchorBottom = 1f,
                    MouseFilter = Control.MouseFilterEnum.Stop,
                };
                _tutorialTapCatcher.GuiInput += OnTutorialTapCatcherInput;
                AddChild(_tutorialTapCatcher);
            }
            // Order (back→front): catcher (dim) → panel → continue-hint.
            // The catcher goes topmost first so it's above HUD buttons and
            // the map (which it dims); the panel is then lifted above it so
            // the narration text stays bright and un-dimmed. The hint is
            // lifted above the panel when it reveals. Panel + hint are
            // MouseFilter=Ignore, so taps on them fall through to the
            // catcher and still advance the beat.
            MoveChild(_tutorialTapCatcher, GetChildCount() - 1);
            MoveChild(_tutorialPanel, GetChildCount() - 1);
            _tutorialTapCatcher.Visible = true;
            _tutorialTapCatcher.MouseFilter = Control.MouseFilterEnum.Stop;
            // Diagnostic for the consecutive-narration rapid-click desync:
            // pairs with the "caught"/"DISARMED" lines so a leaked click
            // (one that reaches the map → "REJECTED … actor -1") can be
            // traced against the catcher's arm timing.
            Log.Debug(Log.LogCategory.Tutorial, "[HudView] tap-catcher ARMED (topmost, Stop)");
        }
        else if (_tutorialTapCatcher != null)
        {
            _tutorialTapCatcher.Visible = false;
            _tutorialTapCatcher.MouseFilter = Control.MouseFilterEnum.Ignore;
            Log.Debug(Log.LogCategory.Tutorial, "[HudView] tap-catcher DISARMED");
        }
    }

    private void OnTutorialTapCatcherInput(InputEvent ev)
    {
        if (ev is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            Log.Debug(Log.LogCategory.Tutorial, "[HudView] tap-catcher CAUGHT click → TutorialMessageTapped");
            _tutorialTapCatcher!.AcceptEvent();
            TutorialMessageTapped?.Invoke();
        }
    }

    /// <summary>
    /// Bottom-anchored, click-through informational panel for tutorial
    /// narration. Not interactive — MouseFilter=Ignore on both the
    /// panel and label so a click anywhere on it falls through to the
    /// map underneath. Sized as a fixed strip horizontally centered
    /// near the bottom of the viewport.
    /// </summary>
    // Tutorial narration box: bottom-anchored, fixed size. The vertical
    // offsets are set by PositionTutorialOverlay so they can lift above the
    // portrait bottom HUD bar; only the left/right (width) offsets are inline.
    private const float TutorialPanelW = 720f;
    private const float TutorialPanelH = 120f;
    private const float TutorialMarginBottom = 60f;
    // Minimum gap kept on each side when the viewport is too narrow for a
    // centered fixed-width HUD panel (tutorial box, bankruptcy toast), so the
    // panel shrinks to fit instead of clipping off both edges.
    private const float HudPanelSideMargin = 24f;
    private bool _tutorialOverlayBuilt;

    private void BuildTutorialOverlay()
    {
        // Width and height grew to fit the longer instruction strings
        // ("Move the selected X onto the highlighted tile to destroy
        // the tower and capture it.") and to give the autowrapped label
        // room for two or three lines without bleeding out the panel.
        _tutorialPanel = new Panel
        {
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            AnchorTop = 1f,
            AnchorBottom = 1f,
            OffsetLeft = -TutorialPanelW * 0.5f,
            OffsetRight = TutorialPanelW * 0.5f,
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        // Local stylebox override that restores the pre-redesign look:
        // a flat, square-cornered dark rectangle with a thin gray
        // border (so it reads as a plain text box, not the rounded
        // slate panel the project theme paints by default). Alpha is
        // low enough that the map and territory borders underneath
        // stay legible when the panel sits over them.
        var tutorialPanelStyle = new StyleBoxFlat
        {
            BgColor = new Color(0f, 0f, 0f, 0.55f),
            BorderColor = new Color(0.3f, 0.3f, 0.3f, 0.7f),
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
        };
        _tutorialPanel.AddThemeStyleboxOverride("panel", tutorialPanelStyle);
        AddChild(_tutorialPanel);

        _tutorialLabel = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            // Small horizontal inset so wrapped lines don't kiss the
            // panel's left/right border.
            AnchorLeft = 0f,
            AnchorRight = 1f,
            AnchorTop = 0f,
            AnchorBottom = 1f,
            OffsetLeft = 12f,
            OffsetRight = -12f,
            OffsetTop = 8f,
            OffsetBottom = -8f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _tutorialLabel.AddThemeFontSizeOverride("font_size", 22);
        _tutorialPanel.AddChild(_tutorialLabel);

        // Flashing "Click anywhere to continue" prompt shown only while a
        // tappable (display-text) tutorial beat is gating input. Horizontally
        // centered and sitting in the gap just below the narration panel
        // (which is bottom-anchored panelH tall, marginBottom off the bottom).
        // Click-through (MouseFilter=Ignore) so the tap catcher still receives
        // the dismissing click; purely a visual cue.
        _continueHint = new Label
        {
            Text = "Click anywhere to continue",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AnchorLeft = 0f,
            AnchorRight = 1f,
            AnchorTop = 1f,
            AnchorBottom = 1f,
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _continueHint.AddThemeFontSizeOverride("font_size", 24);
        AddChild(_continueHint);

        _tutorialOverlayBuilt = true;
        PositionTutorialOverlay();
    }

    /// <summary>Place the tutorial narration box (and its continue-hint) above
    /// the bottom edge — lifted by the portrait bottom HUD bar so it doesn't
    /// overlap it. Re-run on every orientation flip (OnLayoutApplied).</summary>
    private void PositionTutorialOverlay()
    {
        if (!_tutorialOverlayBuilt) return;
        // Cap the panel width to the viewport (logical size already reflects the
        // content-scale factor) so the fixed design width can't clip off both
        // sides on a narrow or scaled-up viewport (portrait phones especially).
        float viewportW = GetViewport().GetVisibleRect().Size.X;
        float width = Mathf.Min(TutorialPanelW, viewportW - HudPanelSideMargin * 2f);
        _tutorialPanel.OffsetLeft = -width * 0.5f;
        _tutorialPanel.OffsetRight = width * 0.5f;
        // Portrait has a bottom bar (lift above its top edge); landscape is
        // pure rails (no bottom bar — let the tutorial box settle near the
        // viewport bottom with just the safe-area inset).
        float lift = Orientation == ScreenOrientation.Portrait
            ? HudBars.PortraitBottomBarHeight
            : SafeArea.Current.Bottom;
        float bottom = TutorialMarginBottom + lift;
        _tutorialPanel.OffsetTop = -bottom - TutorialPanelH;
        _tutorialPanel.OffsetBottom = -bottom;
        // Continue hint sits in the gap between the panel's bottom and the
        // bottom-bar top (or the viewport bottom in landscape).
        _continueHint.OffsetTop = -bottom + 4f;
        _continueHint.OffsetBottom = -lift - 4f;
    }

    // Red-pill bankruptcy warning. The redesign §8 toast spec called
    // for a circular badge, but the in-map warning on a doomed
    // capital is an upward-pointing equilateral triangle (white-
    // bordered red, white "!" inside) — so the toast uses the same
    // triangle to keep the warning glyph consistent between the
    // capital tile and the toast. Spec colors otherwise stand: dark-
    // red bg (oklch 0.30 0.10 25 ≈ #4a2620) at 92% alpha, 1px
    // brighter-red border, 8px radius. Two-line text block: title
    // in Geist 600 ink, subtitle in Geist ink-mute. Shown by
    // Refresh() while the currently-selected territory is doomed for
    // the human's next turn; otherwise hidden.
    private static readonly Color BankruptToastBg = new Color(0.290f, 0.149f, 0.125f, 0.92f);
    private static readonly Color BankruptToastBorder = new Color(0.722f, 0.314f, 0.251f, 1f);

    // 1.5x larger than the spec's reference so the toast reads at the heavier
    // scale the rest of the redesign settled on. Lives top-center, just below
    // the HUD bar, so it doesn't fight the tutorial action-hint panel (which
    // lives bottom-center).
    private const float BankruptToastW = 660f;
    private const float BankruptToastH = 96f;
    private const float BankruptToastMarginTop = 16f;
    private bool _bankruptToastBuilt;

    private void BuildBankruptToast()
    {
        _bankruptToast = new Panel
        {
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            AnchorTop = 0f,
            AnchorBottom = 0f,
            // Vertical offsets are set in PositionBankruptToast (orientation-
            // aware: clears the portrait top bar; flush to the top in landscape).
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        var toastStyle = new StyleBoxFlat
        {
            BgColor = BankruptToastBg,
            BorderColor = BankruptToastBorder,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8,
        };
        _bankruptToast.AddThemeStyleboxOverride("panel", toastStyle);
        AddChild(_bankruptToast);

        var row = new HBoxContainer
        {
            AnchorLeft = 0f, AnchorRight = 1f,
            AnchorTop = 0f, AnchorBottom = 1f,
            OffsetLeft = 21f, OffsetRight = -21f,
            OffsetTop = 0f, OffsetBottom = 0f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        row.AddThemeConstantOverride("separation", 18);
        _bankruptToast.AddChild(row);

        // Triangle badge: same equilateral-up shape as the in-map
        // capital warning (DrawWarningBadgeAt), in a 48-px Control box.
        var badge = new TriangleWarningBadge { CustomMinimumSize = new Vector2(48, 48) };
        badge.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        badge.MouseFilter = Control.MouseFilterEnum.Ignore;
        row.AddChild(badge);

        var textBlock = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        textBlock.AddThemeConstantOverride("separation", 3);

        _bankruptTitleLabel = new Label
        {
            Text = "Bankrupt next turn",
            // Wrap rather than overflow the box when the toast is width-capped
            // on a narrow/scaled viewport (see PositionBankruptToast).
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _bankruptTitleLabel.AddThemeFontOverride("font", GeistFont);
        _bankruptTitleLabel.AddThemeFontSizeOverride("font_size", 24);
        _bankruptTitleLabel.AddThemeColorOverride("font_color", UiPalette.Ink);
        textBlock.AddChild(_bankruptTitleLabel);

        _bankruptSubLabel = new Label
        {
            Text = "All units in this territory will die",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _bankruptSubLabel.AddThemeFontOverride("font", GeistFont);
        _bankruptSubLabel.AddThemeFontSizeOverride("font_size", 21);
        _bankruptSubLabel.AddThemeColorOverride("font_color", UiPalette.InkMute);
        textBlock.AddChild(_bankruptSubLabel);

        row.AddChild(textBlock);

        _bankruptToastBuilt = true;
        PositionBankruptToast();
    }

    /// <summary>Cap the bankruptcy toast width to the viewport (same rationale
    /// as <see cref="PositionTutorialOverlay"/>) so its fixed design width can't
    /// clip off both sides on a narrow or scaled-up viewport. Re-run on resize
    /// via <see cref="OnViewportMetricsChanged"/>.</summary>
    private void PositionBankruptToast()
    {
        if (!_bankruptToastBuilt) return;
        float viewportW = GetViewport().GetVisibleRect().Size.X;
        float width = Mathf.Min(BankruptToastW, viewportW - HudPanelSideMargin * 2f);
        _bankruptToast.OffsetLeft = -width * 0.5f;
        _bankruptToast.OffsetRight = width * 0.5f;
        // Clear the floating TopLeft / TopRight corner chips (status, gold,
        // undo, options) in both orientations — they hug the top edge inside
        // the safe-area inset. ~90 px below the safe-area top reliably puts
        // the toast under the chips on every device.
        float top = SafeArea.Current.Top + 90f + BankruptToastMarginTop;
        _bankruptToast.OffsetTop = top;
        _bankruptToast.OffsetBottom = top + BankruptToastH;
    }

    // Tiny self-drawing Control that paints the same upward-pointing
    // equilateral triangle as HexMapView.DrawWarningBadgeAt — red
    // fill, 2-px white stroke, white "!" exclamation glyph (a
    // vertical bar + dot). Lives in HudView because the toast (this
    // class) is the only consumer; the in-map badge keeps drawing
    // its triangle inline so it can size relative to HexSize.
    private sealed partial class TriangleWarningBadge : Control
    {
        public override void _Draw()
        {
            Color fill = new Color(0.95f, 0.10f, 0.10f, 1f);
            Color accent = new Color(1f, 1f, 1f, 1f);

            const float Sqrt3Over2 = 0.8660254f;
            float r = Mathf.Min(Size.X, Size.Y) * 0.45f;
            Vector2 c = Size * 0.5f;
            Vector2 vTop = c + new Vector2(0f, -r);
            Vector2 vBR  = c + new Vector2( r * Sqrt3Over2, r * 0.5f);
            Vector2 vBL  = c + new Vector2(-r * Sqrt3Over2, r * 0.5f);

            DrawColoredPolygon(new[] { vTop, vBR, vBL }, fill);
            DrawLine(vTop, vBR, accent, 2f, true);
            DrawLine(vBR, vBL, accent, 2f, true);
            DrawLine(vBL, vTop, accent, 2f, true);

            // Exclamation: vertical bar + dot, white. Geometry matches
            // DrawWarningBadgeAt's per-HexSize ratios so the two badges
            // read identically.
            float barHalf = r * 0.11f;
            float barTop = c.Y - r * 0.40f;
            float barBottom = c.Y + r * 0.05f;
            DrawColoredPolygon(new[]
            {
                new Vector2(c.X - barHalf, barTop),
                new Vector2(c.X + barHalf, barTop),
                new Vector2(c.X + barHalf, barBottom),
                new Vector2(c.X - barHalf, barBottom),
            }, accent);
            DrawCircle(new Vector2(c.X, c.Y + r * 0.28f), r * 0.11f, accent);
        }
    }

    /// <summary>
    /// Build a centered, click-blocking panel with "Player wins!" and a
    /// New Game button. Hidden by default; <see cref="Refresh"/> toggles
    /// visibility based on <see cref="SessionState.Winner"/>.
    /// </summary>
    private Button _replayButton = null!;
    private bool _replayAvailable;

    private void BuildVictoryOverlay()
    {
        // Full-screen semi-transparent scrim that blocks clicks through
        // to the map.
        _victoryOverlay = new Control
        {
            AnchorLeft = 0f,
            AnchorRight = 1f,
            AnchorTop = 0f,
            AnchorBottom = 1f,
            MouseFilter = Control.MouseFilterEnum.Stop,
            Visible = false,
        };
        AddChild(_victoryOverlay);

        var scrim = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.6f),
            AnchorLeft = 0f,
            AnchorRight = 1f,
            AnchorTop = 0f,
            AnchorBottom = 1f,
        };
        _victoryOverlay.AddChild(scrim);

        // Centered panel: VICTORY eyebrow + DM Serif "<Player> wins!" in
        // the player's fill color + thin gold rule + three-button action
        // row (Play Again / Replay / Main Menu). Bumped from 540×220 →
        // 580×280 so the eyebrow + rule fit above the win text without
        // crowding the buttons.
        const float panelW = 580f;
        const float panelH = 280f;
        var panel = new Panel
        {
            // Anchor-centered (not absolute Position) so it stays centered
            // across viewport changes — content-scale factor, window resize,
            // orientation flip — without a reposition pass.
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            AnchorTop = 0.5f,
            AnchorBottom = 0.5f,
            OffsetLeft = -panelW * 0.5f,
            OffsetRight = panelW * 0.5f,
            OffsetTop = -panelH * 0.5f,
            OffsetBottom = panelH * 0.5f,
        };
        _victoryOverlay.AddChild(panel);

        var eyebrow = new Label
        {
            Text = "VICTORY",
            HorizontalAlignment = HorizontalAlignment.Center,
            Position = new Vector2(0, 32),
            Size = new Vector2(panelW, 22),
        };
        eyebrow.AddThemeFontSizeOverride("font_size", 18);
        eyebrow.AddThemeColorOverride("font_color", UiPalette.Gold);
        panel.AddChild(eyebrow);

        _victoryLabel = new Label
        {
            Text = "Victory!",
            HorizontalAlignment = HorizontalAlignment.Center,
            Position = new Vector2(0, 58),
            Size = new Vector2(panelW, 70),
        };
        _victoryLabel.AddThemeFontOverride("font", SerifFont);
        _victoryLabel.AddThemeFontSizeOverride("font_size", 52);
        panel.AddChild(_victoryLabel);

        var rule = new ColorRect
        {
            Color = UiPalette.GoldDim,
            Position = new Vector2(panelW * 0.5f - 110f, 144f),
            Size = new Vector2(220f, 1f),
        };
        panel.AddChild(rule);

        const float buttonW = 150f;
        const float buttonH = 48f;
        const float gap = 12f;
        float rowY = 200f;
        float rowX = (panelW - (buttonW * 3f + gap * 2f)) * 0.5f;

        var playAgainButton = new Button { Text = "Play Again" };
        playAgainButton.AddThemeFontSizeOverride("font_size", 22);
        playAgainButton.Position = new Vector2(rowX, rowY);
        playAgainButton.Size = new Vector2(buttonW, buttonH);
        playAgainButton.Pressed += () => NewGameClicked?.Invoke();
        AudioBus.AttachClick(playAgainButton);
        panel.AddChild(playAgainButton);

        _replayButton = new Button { Text = "Replay" };
        _replayButton.AddThemeFontSizeOverride("font_size", 22);
        _replayButton.Position = new Vector2(rowX + buttonW + gap, rowY);
        _replayButton.Size = new Vector2(buttonW, buttonH);
        _replayButton.Pressed += () => ReplayClicked?.Invoke();
        _replayButton.Disabled = true;  // gated by SetReplayAvailable
        AudioBus.AttachClick(_replayButton);
        panel.AddChild(_replayButton);

        var mainMenuButton = new Button { Text = "Main Menu" };
        mainMenuButton.AddThemeFontSizeOverride("font_size", 22);
        mainMenuButton.Position = new Vector2(rowX + (buttonW + gap) * 2f, rowY);
        mainMenuButton.Size = new Vector2(buttonW, buttonH);
        mainMenuButton.Pressed += () => MainMenuClicked?.Invoke();
        AudioBus.AttachClick(mainMenuButton);
        panel.AddChild(mainMenuButton);
    }

    public void SetReplayAvailable(bool available)
    {
        _replayAvailable = available;
        if (_replayButton != null) _replayButton.Disabled = !available;
    }

    /// <summary>
    /// Build a centered, click-blocking panel with "<Player> defeated"
    /// and Continue / Main Menu buttons. Hidden by default;
    /// <see cref="Refresh"/> toggles visibility based on
    /// <see cref="SessionState.PendingDefeatScreen"/>. Continue dismisses
    /// the overlay (controller resumes the paused AI loop); Main Menu
    /// reuses the existing <see cref="MainMenuClicked"/> event.
    /// </summary>
    private void BuildDefeatOverlay()
    {
        _defeatOverlay = new Control
        {
            AnchorLeft = 0f,
            AnchorRight = 1f,
            AnchorTop = 0f,
            AnchorBottom = 1f,
            MouseFilter = Control.MouseFilterEnum.Stop,
            Visible = false,
        };
        AddChild(_defeatOverlay);

        var scrim = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.6f),
            AnchorLeft = 0f,
            AnchorRight = 1f,
            AnchorTop = 0f,
            AnchorBottom = 1f,
        };
        _defeatOverlay.AddChild(scrim);

        // DEFEAT eyebrow + DM Serif "<Player> defeated" in player color +
        // gold rule + three-button row. Same shell as the Victory panel
        // so the two read as a family.
        const float panelW = 540f;
        const float panelH = 280f;
        var panel = new Panel
        {
            // Anchor-centered (see BuildVictoryOverlay): stays centered across
            // viewport/content-scale changes with no reposition pass.
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            AnchorTop = 0.5f,
            AnchorBottom = 0.5f,
            OffsetLeft = -panelW * 0.5f,
            OffsetRight = panelW * 0.5f,
            OffsetTop = -panelH * 0.5f,
            OffsetBottom = panelH * 0.5f,
        };
        _defeatOverlay.AddChild(panel);

        var eyebrow = new Label
        {
            Text = "DEFEAT",
            HorizontalAlignment = HorizontalAlignment.Center,
            Position = new Vector2(0, 32),
            Size = new Vector2(panelW, 22),
        };
        eyebrow.AddThemeFontSizeOverride("font_size", 18);
        eyebrow.AddThemeColorOverride("font_color", UiPalette.Gold);
        panel.AddChild(eyebrow);

        _defeatLabel = new Label
        {
            Text = "Defeated",
            HorizontalAlignment = HorizontalAlignment.Center,
            Position = new Vector2(0, 58),
            Size = new Vector2(panelW, 70),
        };
        _defeatLabel.AddThemeFontOverride("font", SerifFont);
        _defeatLabel.AddThemeFontSizeOverride("font_size", 48);
        panel.AddChild(_defeatLabel);

        var rule = new ColorRect
        {
            Color = UiPalette.GoldDim,
            Position = new Vector2(panelW * 0.5f - 110f, 144f),
            Size = new Vector2(220f, 1f),
        };
        panel.AddChild(rule);

        const float buttonW = 140f;
        const float buttonH = 48f;
        const float gap = 20f;
        float rowY = 200f;
        float rowX = (panelW - (buttonW * 3f + gap * 2f)) * 0.5f;

        _defeatContinueButton = new Button { Text = "Continue" };
        _defeatContinueButton.AddThemeFontSizeOverride("font_size", 22);
        _defeatContinueButton.Position = new Vector2(rowX, rowY);
        _defeatContinueButton.Size = new Vector2(buttonW, buttonH);
        _defeatContinueButton.Pressed += () => DefeatContinueClicked?.Invoke();
        AudioBus.AttachClick(_defeatContinueButton);
        panel.AddChild(_defeatContinueButton);

        var playAgainButton = new Button { Text = "Play Again" };
        playAgainButton.AddThemeFontSizeOverride("font_size", 22);
        playAgainButton.Position = new Vector2(rowX + buttonW + gap, rowY);
        playAgainButton.Size = new Vector2(buttonW, buttonH);
        playAgainButton.Pressed += () => NewGameClicked?.Invoke();
        AudioBus.AttachClick(playAgainButton);
        panel.AddChild(playAgainButton);

        var mainMenuButton = new Button { Text = "Main Menu" };
        mainMenuButton.AddThemeFontSizeOverride("font_size", 22);
        mainMenuButton.Position = new Vector2(rowX + (buttonW + gap) * 2f, rowY);
        mainMenuButton.Size = new Vector2(buttonW, buttonH);
        mainMenuButton.Pressed += () => MainMenuClicked?.Invoke();
        AudioBus.AttachClick(mainMenuButton);
        panel.AddChild(mainMenuButton);
    }

    /// <summary>
    /// Build a centered, click-blocking panel offering an early win when
    /// a human ends their turn while owning >50% of the map. Two buttons:
    /// Win Now (declare victory immediately) and Continue Playing (proceed
    /// with the End Turn). Hidden by default; <see cref="Refresh"/>
    /// toggles visibility based on
    /// <see cref="SessionState.PendingClaimVictory"/>.
    /// </summary>
    private void BuildClaimVictoryOverlay()
    {
        _claimVictoryOverlay = new Control
        {
            AnchorLeft = 0f,
            AnchorRight = 1f,
            AnchorTop = 0f,
            AnchorBottom = 1f,
            MouseFilter = Control.MouseFilterEnum.Stop,
            Visible = false,
        };
        AddChild(_claimVictoryOverlay);

        var scrim = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.6f),
            AnchorLeft = 0f,
            AnchorRight = 1f,
            AnchorTop = 0f,
            AnchorBottom = 1f,
        };
        _claimVictoryOverlay.AddChild(scrim);

        // CHECKPOINT eyebrow + DM Serif "Claim Victory?" + gold rule +
        // two-button row (Win Now / Continue). Matches the Victory /
        // Defeat shell so all three overlays read as one design family.
        const float panelW = 540f;
        const float panelH = 300f;
        var panel = new Panel
        {
            // Anchor-centered (see BuildVictoryOverlay): stays centered across
            // viewport/content-scale changes with no reposition pass.
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            AnchorTop = 0.5f,
            AnchorBottom = 0.5f,
            OffsetLeft = -panelW * 0.5f,
            OffsetRight = panelW * 0.5f,
            OffsetTop = -panelH * 0.5f,
            OffsetBottom = panelH * 0.5f,
        };
        _claimVictoryOverlay.AddChild(panel);

        var eyebrow = new Label
        {
            Text = "CHECKPOINT",
            HorizontalAlignment = HorizontalAlignment.Center,
            Position = new Vector2(0, 32),
            Size = new Vector2(panelW, 22),
        };
        eyebrow.AddThemeFontSizeOverride("font_size", 18);
        eyebrow.AddThemeColorOverride("font_color", UiPalette.Gold);
        panel.AddChild(eyebrow);

        var headerLabel = new Label
        {
            Text = "Claim Victory?",
            HorizontalAlignment = HorizontalAlignment.Center,
            Position = new Vector2(0, 58),
            Size = new Vector2(panelW, 70),
        };
        headerLabel.AddThemeFontOverride("font", SerifFont);
        headerLabel.AddThemeFontSizeOverride("font_size", 44);
        panel.AddChild(headerLabel);

        var rule = new ColorRect
        {
            Color = UiPalette.GoldDim,
            Position = new Vector2(panelW * 0.5f - 110f, 144f),
            Size = new Vector2(220f, 1f),
        };
        panel.AddChild(rule);

        const float buttonW = 200f;
        const float buttonH = 48f;
        const float gap = 20f;
        float rowY = 220f;
        float rowX = (panelW - (buttonW * 2f + gap)) * 0.5f;

        _claimWinNowButton = new Button { Text = "Win Now" };
        _claimWinNowButton.AddThemeFontSizeOverride("font_size", 22);
        _claimWinNowButton.Position = new Vector2(rowX, rowY);
        _claimWinNowButton.Size = new Vector2(buttonW, buttonH);
        _claimWinNowButton.Pressed += () => ClaimVictoryWinNowClicked?.Invoke();
        AudioBus.AttachClick(_claimWinNowButton);
        panel.AddChild(_claimWinNowButton);

        _claimContinueButton = new Button { Text = "Continue Playing" };
        _claimContinueButton.AddThemeFontSizeOverride("font_size", 22);
        _claimContinueButton.Position = new Vector2(rowX + buttonW + gap, rowY);
        _claimContinueButton.Size = new Vector2(buttonW, buttonH);
        _claimContinueButton.Pressed += () => ClaimVictoryContinueClicked?.Invoke();
        AudioBus.AttachClick(_claimContinueButton);
        panel.AddChild(_claimContinueButton);
    }

    /// <summary>
    /// Global keyboard shortcuts. The HUD's buttons have
    /// <c>FocusMode = None</c> so keyboard events flow past them into
    /// <see cref="Node._UnhandledInput"/>. <c>!keyEvent.Echo</c>
    /// prevents held-down keys from repeating. Each shortcut raises
    /// the matching click event so the controller doesn't need a
    /// separate code path for keyboard vs. mouse triggers.
    /// </summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey keyEvent) return;
        if (!keyEvent.Pressed || keyEvent.Echo) return;

        // Tappable tutorial messages are dismissed by a click anywhere
        // (the tap catcher) — let any keypress dismiss them too, with
        // the gameplay shortcut suppressed so e.g. Enter doesn't both
        // ack the narration and immediately end the turn.
        if (_tutorialTapCatcher != null && _tutorialTapCatcher.Visible)
        {
            // Keyboard dismiss path — bypasses the tap-catcher's mouse
            // routing entirely (so it's immune to the consecutive-narration
            // rapid-click desync). Logged distinctly from the catcher's
            // "CAUGHT click" so a trace shows whether a narration advanced
            // via key or click.
            Log.Debug(Log.LogCategory.Tutorial,
                $"[HudView] keypress ({keyEvent.Keycode}) advanced tutorial narration → TutorialMessageTapped");
            GetViewport().SetInputAsHandled();
            TutorialMessageTapped?.Invoke();
            return;
        }

        switch (keyEvent.Keycode)
        {
            case Key.Enter:
            case Key.KpEnter:
                EndTurnClicked?.Invoke();
                GetViewport().SetInputAsHandled();
                break;
            case Key.Tab:
                if (keyEvent.ShiftPressed) PreviousTerritoryClicked?.Invoke();
                else NextTerritoryClicked?.Invoke();
                GetViewport().SetInputAsHandled();
                break;
            case Key.Backtab:
                // Some platforms send Shift+Tab as Key.Backtab instead of
                // Key.Tab + Shift; handle both so the binding is reliable.
                PreviousTerritoryClicked?.Invoke();
                GetViewport().SetInputAsHandled();
                break;
            case Key.N:
                if (keyEvent.ShiftPressed) PreviousUnitClicked?.Invoke();
                else NextUnitClicked?.Invoke();
                GetViewport().SetInputAsHandled();
                break;
            case Key.Escape:
                // Layered: if a Buy/Build/Move is pending, Escape cancels it
                // (preserves the existing keyboard shortcut). Otherwise Escape
                // raises EscRequested so the scene root pops its EscMenu.
                if (_hasPendingAction)
                {
                    CancelActionPressed?.Invoke();
                }
                else
                {
                    EscRequested?.Invoke();
                }
                GetViewport().SetInputAsHandled();
                break;
            case Key.U:
                BuyRecruitClicked?.Invoke();
                GetViewport().SetInputAsHandled();
                break;
            case Key.T:
                BuildTowerClicked?.Invoke();
                GetViewport().SetInputAsHandled();
                break;
            case Key.Z:
                if (keyEvent.ShiftPressed)
                {
                    UndoTurnClicked?.Invoke();
                }
                else
                {
                    UndoLastClicked?.Invoke();
                }
                GetViewport().SetInputAsHandled();
                break;
            case Key.Y:
                if (keyEvent.ShiftPressed)
                {
                    RedoAllClicked?.Invoke();
                }
                else
                {
                    RedoLastClicked?.Invoke();
                }
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    /// <summary>
    /// Update every label, button disabled state, and the End Turn CTA
    /// styling from the current game + session state.
    /// <paramref name="hasActionableRemaining"/> is computed by the
    /// controller (it's a pure predicate over game state, not HUD state)
    /// and passed in so the HUD can style the End Turn button.
    /// </summary>
    public void Refresh(GameState state, SessionState session, bool hasActionableRemaining)
    {
        _hasPendingAction = session.Mode != SessionState.ActionMode.None;
        _turnLabel.Text = state.Turns.TurnNumber.ToString();
        IReadOnlyList<Player> roster = state.Turns.Players;
        var swatchColors = new Color[roster.Count];
        var swatchEliminated = new bool[roster.Count];
        for (int i = 0; i < roster.Count; i++)
        {
            swatchColors[i] = PlayerPalette.ColorFor(roster[i].Id);
            swatchEliminated[i] = WinConditionRules.IsEliminated(roster[i].Id, state.Grid);
        }
        int currentIndex = state.Turns.CurrentPlayerIndex;
        _playerSwatchBar.SetPlayers(swatchColors, swatchEliminated, currentIndex);
        if (Log.IsEnabled(Log.LogCategory.Render, Log.LogLevel.Debug))
        {
            var parts = new List<string>(roster.Count);
            for (int i = 0; i < roster.Count; i++)
            {
                parts.Add($"{i}:{roster[i].Name}{(swatchEliminated[i] ? "(elim)" : "")}{(i == currentIndex ? "*" : "")}");
            }
            Log.Debug(Log.LogCategory.Render, $"[SwatchBar] current={currentIndex} [{string.Join(", ", parts)}]");
        }

        // Buy / Build buttons are always visible; the tooltip explains
        // *why* the button is disabled (no selection / no capital / can't
        // afford) so the player isn't left guessing. The contextual cost
        // and mode-hint live in the tooltip; the in-progress mode is
        // signalled with the Selected outline.
        Territory? selected = session.SelectedTerritory;
        bool hasCapital = selected?.HasCapital ?? false;

        _goldChip.Visible = hasCapital;
        if (hasCapital)
        {
            int gold = state.Treasury.GetGold(selected!.Capital!.Value);
            int income = TreeRules.CountIncomeProducingTiles(selected, state.Grid);
            int upkeep = UpkeepRules.TotalUpkeepFor(selected, state.Grid);
            int net = income - upkeep;
            string sign = net >= 0 ? "+" : "";
            _goldLabel.Text = $"{gold}g ({income}-{upkeep}={sign}{net})";

            // Economic-report severity for a human-owned territory:
            //   red    — forecast bankrupt next turn (every unit dies at
            //            the owner's next turn-start);
            //   yellow — solvent next turn but the per-turn delta is
            //            negative, so reserves are bleeding down toward
            //            an eventual bankruptcy;
            //   default — net >= 0, or not the human's territory.
            EconomyOutlook outlook = IsHumanOwned(state, selected)
                ? UpkeepRules.Classify(selected, state.Grid, state.Treasury)
                : EconomyOutlook.Healthy;
            switch (outlook)
            {
                case EconomyOutlook.BankruptNextTurn:
                    _goldLabel.AddThemeColorOverride("font_color", BoardPalette.WarnRed);
                    Log.Debug(Log.LogCategory.Turn,
                        $"[economy] {selected.Owner} territory @ {selected.Capital!.Value} " +
                        $"bankrupt next turn (gold {gold} + income {income} < upkeep {upkeep})");
                    break;
                case EconomyOutlook.NegativeDelta:
                    _goldLabel.AddThemeColorOverride("font_color", BoardPalette.WarnYellow);
                    Log.Debug(Log.LogCategory.Turn,
                        $"[economy] {selected.Owner} territory @ {selected.Capital!.Value} " +
                        $"net {net} < 0 but solvent next turn (gold {gold} + income {income} >= upkeep {upkeep})");
                    break;
                default:
                    _goldLabel.RemoveThemeColorOverride("font_color");
                    break;
            }
        }
        else
        {
            _goldLabel.Text = "";
            _goldLabel.RemoveThemeColorOverride("font_color");
        }

        UnitLevel? currentBuyLevel = SessionState.BuyModeLevel(session.Mode);
        foreach (HudIconButton button in _buyUnitButtons)
        {
            UnitLevel level = button.BuyLevel;
            bool canAffordThis = hasCapital && PurchaseRules.CanAfford(selected!, state.Treasury, level);
            bool isActive = currentBuyLevel == level;
            button.Disabled = !canAffordThis;
            button.Selected = isActive;
            int cost = PurchaseRules.CostFor(level);
            // Active button: tooltip is cleared because the placement
            // instruction lives in the bottom-anchored panel.
            button.TooltipText = isActive
                ? ""
                : canAffordThis
                    ? $"Buy {level} ({cost}g) — U"
                    : DisabledBuyReason(selected, hasCapital, $"a {level.ToString().ToLowerInvariant()}", cost);
        }

        // Collapsed single button (shown when narrow): mirror the active buy
        // mode's level on its glyph and its outline. Recruit is the cheapest
        // level, so if it's unaffordable nothing is — disable the whole button.
        bool anyBuyAffordable = hasCapital
            && PurchaseRules.CanAfford(selected!, state.Treasury, UnitLevel.Recruit);
        _collapsedBuyButton.BuyLevel = currentBuyLevel ?? UnitLevel.Recruit;
        _collapsedBuyButton.Selected = currentBuyLevel != null;
        _collapsedBuyButton.Disabled = !anyBuyAffordable;
        _collapsedBuyButton.TooltipText = currentBuyLevel != null
            ? ""
            : anyBuyAffordable
                ? "Buy unit — cycles affordable levels (U)"
                : DisabledBuyReason(selected, hasCapital, "a unit", PurchaseRules.CostFor(UnitLevel.Recruit));

        bool building = session.Mode == SessionState.ActionMode.BuildingTower;
        bool canAffordTower = hasCapital && PurchaseRules.CanAffordTower(selected!, state.Treasury);
        _buildTowerButton.Visible = true;
        _buildTowerButton.Disabled = !building && !canAffordTower;
        _buildTowerButton.Selected = building;
        _buildTowerButton.TooltipText = building
            ? "Click a tile... — T"
            : canAffordTower
                ? HudIconButton.DefaultTooltip(HudIcon.Tower)
                : DisabledBuyReason(selected, hasCapital, "a tower", PurchaseRules.TowerCost);

        _undoLastButton.Disabled = _undoRedoLocked || !session.Undo.CanUndo;
        _redoLastButton.Disabled = _undoRedoLocked || !session.Undo.CanRedo;
        // Mirrors the End Turn CTA: disabled exactly when the current player
        // has no actionable territory left (the same flag that lights End Turn).
        _nextTerritoryButton.Disabled = !hasActionableRemaining;

        // Next Unit: enabled iff the selected territory has at least one
        // unmoved current-player unit (mirrors the N hotkey's no-op
        // semantic). Selected lights up while repeated-movement is on
        // (parallel to Buy/Build buttons mirroring their Mode), but
        // only if the button is also enabled — a disabled button must
        // never show the white active ring, even if the underlying
        // RepeatedMovement bit is still set (e.g. after Tab-cycling to
        // a territory with no movables).
        bool hasMovableInSelection = selected != null
            && MovementRules.HasUnmovedUnitsOwnedBy(selected, state.Turns.CurrentPlayer.Id, state.Grid);
        _nextUnitButton.Disabled = !hasMovableInSelection;
        _nextUnitButton.Selected = hasMovableInSelection && session.RepeatedMovement;
        _nextUnitButton.TooltipText = hasMovableInSelection
            ? HudIconButton.DefaultTooltip(HudIcon.NextUnit)
            : "No unmoved units to cycle";
        // End Turn CTA styling is driven by GameController.RefreshViews
        // post-Refresh so Tutorial Preview's onAfterRefresh callback can
        // overwrite it (e.g. light it for an EndTurn scripted beat even
        // when the player still has actionable territories).

        // Victory overlay: show iff a winner has been declared and the
        // overlay isn't suppressed (Tutorial Preview / Record set the
        // suppress flag; the tutorial-message panel handles game-over
        // signaling in those modes).
        if (session.Winner.HasValue && !_victoryOverlaySuppressed)
        {
            PlayerId winId = session.Winner.Value;
            Player? winner = state.Turns.Players
                .FirstOrDefault(p => p.Id == winId);
            string name = winner?.Name ?? "Unknown";
            _victoryLabel.Text = $"{name} wins!";
            _victoryLabel.AddThemeColorOverride("font_color", PlayerPalette.ColorFor(winId));
            _victoryOverlay.Visible = true;
        }
        else
        {
            _victoryOverlay.Visible = false;
        }

        // Defeat overlay: show iff a human just lost their last capital
        // and hasn't dismissed the screen yet. Suppressed when Winner
        // is set so the game-over screen takes precedence.
        if (session.PendingDefeatScreen.HasValue && !session.Winner.HasValue)
        {
            PlayerId loseId = session.PendingDefeatScreen.Value;
            Player? loser = state.Turns.Players
                .FirstOrDefault(p => p.Id == loseId);
            string name = loser?.Name ?? "Unknown";
            _defeatLabel.Text = $"{name} defeated";
            _defeatLabel.AddThemeColorOverride("font_color", PlayerPalette.ColorFor(loseId));
            _defeatOverlay.Visible = true;
        }
        else
        {
            _defeatOverlay.Visible = false;
        }

        // Claim-victory overlay: show iff a human pressed End Turn and
        // crossed an unseen tier (50/75/90) and hasn't dismissed yet.
        // Suppressed when Winner OR PendingDefeatScreen is set so the
        // higher-priority overlays take precedence. The threshold tier
        // is intentionally not surfaced in the wording — every tier
        // shows the same prompt.
        _claimVictoryOverlay.Visible =
            session.PendingClaimVictory.HasValue
            && !session.Winner.HasValue
            && !session.PendingDefeatScreen.HasValue;

        // Gameplay action hint — surfaces "Click to place / move" prompts
        // through the bottom-anchored tutorial-message panel during buy
        // and move modes. Skipped if an external caller (tutorial step
        // text, AI-batch announcement) currently owns the panel.
        if (!_externalMessageActive)
        {
            string? hint = ComputeActionHint(state, session);
            if (hint != null)
            {
                _tutorialLabel.Text = hint;
                _tutorialPanel.Visible = true;
            }
            else
            {
                _tutorialPanel.Visible = false;
            }
        }

        // Bankruptcy toast — shows whenever the selected human territory
        // is forecast to bankrupt next turn. Lives top-center (just
        // below the HUD), so it can coexist with the bottom-center
        // tutorial-message panel during a buy/move mode without either
        // covering the other.
        _bankruptToast.Visible = ForecastHumanBankrupt(state, session.SelectedTerritory);
    }

    /// <summary>
    /// True iff <paramref name="selected"/> is a human-owned capital
    /// territory that <see cref="UpkeepRules.ForecastBankruptNextTurn"/>
    /// predicts will lose all its units at its owner's next turn-start.
    /// Shared by the red report-label styling and the warning panel text
    /// so both key off one decision.
    /// </summary>
    private static bool IsHumanOwned(GameState state, Territory? selected)
    {
        if (selected == null) return false;
        Player? owner = state.Turns.Players.FirstOrDefault(p => p.Id == selected.Owner);
        return owner != null && !owner.IsAi;
    }

    private static bool ForecastHumanBankrupt(GameState state, Territory? selected)
    {
        if (selected == null) return false;
        if (!IsHumanOwned(state, selected)) return false;
        return UpkeepRules.Classify(selected, state.Grid, state.Treasury) == EconomyOutlook.BankruptNextTurn;
    }

    private static string? ComputeActionHint(GameState state, SessionState session)
    {
        UnitLevel? buyLevel = SessionState.BuyModeLevel(session.Mode);
        if (buyLevel.HasValue)
        {
            return $"Click to place a {buyLevel.Value}";
        }
        if (session.Mode == SessionState.ActionMode.MovingUnit && session.MoveSource.HasValue)
        {
            HexTile? src = state.Grid.Get(session.MoveSource.Value);
            UnitLevel level = (src?.Unit?.Level) ?? UnitLevel.Recruit;
            return $"Click to move the {level}";
        }
        // Bankruptcy warning now flows through the red bankruptcy-toast
        // widget (built by BuildBankruptToast / toggled in Refresh) —
        // no longer competes for the tutorial panel.
        return null;
    }

    /// <summary>
    /// Explain why the Buy / Build button is disabled. Walks the
    /// preconditions in player-facing priority order — selection first,
    /// then capital ownership, then affordability — so the tooltip names
    /// the most actionable thing the player can fix. Includes the cost
    /// in the affordability message so the player knows what they need.
    /// </summary>
    private static string DisabledBuyReason(Territory? selected, bool hasCapital, string actionLabel, int cost)
    {
        if (selected == null) return "No territory selected";
        if (!hasCapital) return "Selected territory has no capital";
        return $"Selected territory can't afford {actionLabel} ({cost}g)";
    }

    public void SetCta(CtaButton button, bool isCta, bool pulse = true)
    {
        if (button == CtaButton.BuyRecruit)
        {
            // The buy control is either the four-button row (Recruit button) or
            // the single collapsed cycle button, depending on viewport width.
            // Style both so the CTA shows on whichever is currently visible and
            // survives a collapse flip; the hidden one's pulse is just unseen.
            ApplyCtaStyle(_buyUnitButtons.First(b => b.BuyLevel == UnitLevel.Recruit), isCta, pulse);
            ApplyCtaStyle(_collapsedBuyButton, isCta, pulse);
            return;
        }
        Button target = button switch
        {
            CtaButton.EndTurn => _endTurnButton,
            CtaButton.BuildTower => _buildTowerButton,
            CtaButton.ClaimVictoryWinNow => _claimWinNowButton,
            CtaButton.ClaimVictoryContinue => _claimContinueButton,
            CtaButton.DefeatContinue => _defeatContinueButton,
            CtaButton.NextTerritory => _nextTerritoryButton,
            _ => throw new System.ArgumentOutOfRangeException(nameof(button)),
        };
        ApplyCtaStyle(target, isCta, pulse);
    }

    public void SetVictoryOverlaySuppressed(bool suppressed)
    {
        _victoryOverlaySuppressed = suppressed;
        if (suppressed)
        {
            // Refresh() may have already painted the overlay before the
            // caller flipped this; force the hide immediately.
            _victoryOverlay.Visible = false;
        }
    }

    public void SetUndoRedoLocked(bool locked)
    {
        _undoRedoLocked = locked;
        if (locked)
        {
            // Refresh() may not be imminent (Tutorial Preview calls this
            // before the first refresh) — disable immediately so the
            // first frame doesn't paint them clickable.
            _undoLastButton.Disabled = true;
            _redoLastButton.Disabled = true;
        }
    }

    // Active CTA pulse animations, keyed by their target button. The
    // Tween instances loop until StopPulse kills them; we hold the
    // reference so we can stop them on CTA-off transitions.
    private readonly System.Collections.Generic.Dictionary<Button, Tween> _ctaPulseTweens = new();

    private void ApplyCtaStyle(Button button, bool isCta, bool pulse)
    {
        // Stroke-only HUD glyphs flip white↔black with the CTA bg; tell
        // the icon button so its next _Draw uses the right palette.
        if (button is HudIconButton iconButton)
        {
            iconButton.CtaActive = isCta;
        }
        if (isCta)
        {
            var style = new StyleBoxFlat
            {
                BgColor = new Color(1f, 1f, 1f, 1f),
                BorderColor = new Color(0f, 0f, 0f, 1f),
                BorderWidthLeft = 2,
                BorderWidthRight = 2,
                BorderWidthTop = 2,
                BorderWidthBottom = 2,
                CornerRadiusTopLeft = 4,
                CornerRadiusTopRight = 4,
                CornerRadiusBottomLeft = 4,
                CornerRadiusBottomRight = 4,
                ContentMarginLeft = 12,
                ContentMarginRight = 12,
                ContentMarginTop = 6,
                ContentMarginBottom = 6,
            };
            button.AddThemeStyleboxOverride("normal", style);
            button.AddThemeStyleboxOverride("hover", style);
            button.AddThemeStyleboxOverride("pressed", style);
            button.AddThemeColorOverride("font_color", new Color(0f, 0f, 0f));
            button.AddThemeColorOverride("font_hover_color", new Color(0f, 0f, 0f));
            button.AddThemeColorOverride("font_pressed_color", new Color(0f, 0f, 0f));
            if (pulse)
            {
                StartCtaPulse(button);
            }
            else
            {
                StopCtaPulse(button);
            }
        }
        else
        {
            button.RemoveThemeStyleboxOverride("normal");
            button.RemoveThemeStyleboxOverride("hover");
            button.RemoveThemeStyleboxOverride("pressed");
            button.RemoveThemeColorOverride("font_color");
            button.RemoveThemeColorOverride("font_hover_color");
            button.RemoveThemeColorOverride("font_pressed_color");
            StopCtaPulse(button);
            // Hero (terracotta accent) survives the CTA toggle — re-apply
            // it so End Turn / active Buy stay accented when not the CTA.
            if (button is HudIconButton heroish) heroish.ReapplyHero();
        }
    }

    private void StartCtaPulse(Button button)
    {
        if (_ctaPulseTweens.TryGetValue(button, out Tween? existing)
            && existing != null && existing.IsValid())
        {
            return; // already pulsing — leave the animation running
        }
        Tween tween = button.CreateTween();
        tween.SetLoops();
        tween.TweenProperty(button, "modulate:a", 0.55f, 0.55).SetTrans(Tween.TransitionType.Sine);
        tween.TweenProperty(button, "modulate:a", 1.0f, 0.55).SetTrans(Tween.TransitionType.Sine);
        _ctaPulseTweens[button] = tween;
    }

    private void StopCtaPulse(Button button)
    {
        if (_ctaPulseTweens.TryGetValue(button, out Tween? tween)
            && tween != null && tween.IsValid())
        {
            tween.Kill();
        }
        _ctaPulseTweens.Remove(button);
        button.Modulate = new Color(1f, 1f, 1f, 1f);
    }
}
