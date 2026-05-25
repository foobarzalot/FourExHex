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
    public const float HudHeight = 96f;
    // Portrait split-bar heights. Top bar carries territory-specific content
    // (gold + buy/build) and only shows when a territory is selected; the
    // bottom bar carries turn/player status + turn controls and is always up.
    private const float PortraitTopBarHeight = 96f;
    private const float PortraitBottomBarHeight = 96f;

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
    private HudIconButton _buildTowerButton = null!;
    private HudIconButton _undoLastButton = null!;
    private HudIconButton _redoLastButton = null!;
    private bool _undoRedoLocked;
    private bool _victoryOverlaySuppressed;
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
    private Control _statusCluster = null!;   // TURN # + TO PLAY name
    private Control _actionCluster = null!;   // buy palette + Build Tower + Add Text
    private Control _controlsCluster = null!; // undo cluster + End Turn + Options
    private bool _selectionPresent;           // a territory is currently selected

    // Snapshot of session.Mode != None at the last Refresh, so the Escape
    // handler can decide between cancel-action (pending) and End Game (idle)
    // without holding a SessionState reference.
    private bool _hasPendingAction;

    public override void _Ready()
    {
        Vector2 viewport = GetViewport().GetVisibleRect().Size;

        // Build the three persistent widget clusters as parentless HBoxes.
        // ApplyLayout parents them (plus the gold chip) into orientation-
        // specific bars; on a landscape↔portrait flip they're reparented,
        // never rebuilt, so their event wiring and disabled/CTA state survive.
        // MouseFilter Pass keeps the clusters click-through to leaf children
        // only. (The slate bar background + click-blocking now live on the
        // bar Panels created in ApplyLayout, not a standalone background.)
        _statusCluster = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        _statusCluster.AddThemeConstantOverride("separation", 14);
        _actionCluster = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        _actionCluster.AddThemeConstantOverride("separation", 14);
        _controlsCluster = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        _controlsCluster.AddThemeConstantOverride("separation", 14);

        // 1) Turn block — small "TURN" eyebrow over a mono number.
        _statusCluster.AddChild(BuildEyebrowBlock("TURN", out _turnLabel, mono: true, valueColor: UiPalette.Ink, out _turnEyebrow));
        _turnLabel.Text = "1";
        _turnLabel.CustomMinimumSize = new Vector2(70, 0);
        _turnLabel.AddThemeFontSizeOverride("font_size", 36);

        _statusCluster.AddChild(BuildVerticalDivider());

        // 2) Current-player block — a row of color swatches, one per
        // player in movement order, with the current player's swatch
        // enlarged + white-outlined and eliminated players dimmed in
        // place. Replaces the old colored name label.
        _playerSwatchBar = new PlayerSwatchBar { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        _statusCluster.AddChild(_playerSwatchBar);

        // 3) Gold chip — bg-deep pill containing the gold value + the
        // income breakdown. We keep the existing "value (income-upkeep=net)"
        // format inside one label so the rich economy-outlook color
        // logic in Refresh() still applies wholesale.
        var goldChip = new PanelContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        var goldChipStyle = new StyleBoxFlat
        {
            BgColor = UiPalette.BgDeep,
            BorderColor = UiPalette.LineSoft,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8,
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 6,
            ContentMarginBottom = 6,
        };
        goldChip.AddThemeStyleboxOverride("panel", goldChipStyle);
        _goldLabel = new Label
        {
            Text = "",
            CustomMinimumSize = new Vector2(190, 0),
        };
        _goldLabel.AddThemeFontOverride("font", MonoFont);
        _goldLabel.AddThemeFontSizeOverride("font_size", 26);
        goldChip.AddChild(_goldLabel);
        // Hide the chip entirely when there's nothing to display (no
        // territory selected, no capital) — an empty bg-deep pill reads as a
        // missing widget. ApplyLayout places it (left region in landscape,
        // top bar in portrait); it grows/vanishes without shifting buttons.
        _goldChip = goldChip;

        // 4) Unit palette — the four buy buttons (Recruit/Soldier/
        // Captain/Commander) live inside one rounded bg-deep PanelContainer
        // so they read as one grouped widget. The Build Tower button
        // sits OUTSIDE the panel as a separate sibling in the center group;
        // the visual gap between them is the group's own 14-px separation,
        // so Build Tower has its own anchor point distinct from the
        // unit-placement group.
        var palettePanel = new PanelContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        palettePanel.AddThemeStyleboxOverride("panel", ModalChrome.PalettePanelStyle());
        var paletteRow = new HBoxContainer();
        paletteRow.AddThemeConstantOverride("separation", 2);
        palettePanel.AddChild(paletteRow);
        _actionCluster.AddChild(palettePanel);

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
        _controlsCluster.AddChild(_undoLastButton);

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
        _controlsCluster.AddChild(_redoLastButton);

        _controlsCluster.AddChild(BuildVerticalDivider());

        // Next active territory — same action as the Tab hotkey. Disabled
        // exactly when End Turn is the CTA (no actionable territory left).
        _nextTerritoryButton = new HudIconButton(HudIcon.NextTerritory);
        _nextTerritoryButton.Pressed += () => NextTerritoryClicked?.Invoke();
        AudioBus.AttachClick(_nextTerritoryButton);
        _controlsCluster.AddChild(_nextTerritoryButton);

        // End Turn uses the default Button theme — the SetCta() white
        // pulse remains the only "this is the current CTA" signal.
        _endTurnButton = new HudIconButton(HudIcon.EndTurn);
        _endTurnButton.Pressed += () => EndTurnClicked?.Invoke();
        AudioBus.AttachClick(_endTurnButton);
        _controlsCluster.AddChild(_endTurnButton);

        // Single Options button — raises the same EscRequested event
        // the Escape key fires, so the scene root's pause coordinator
        // drives both paths. Save Game and Settings live inside that
        // pause menu now rather than as standalone HUD buttons.
        _optionsButton = new HudIconButton(HudIcon.Options);
        _optionsButton.Pressed += () => EscRequested?.Invoke();
        AudioBus.AttachClick(_optionsButton);
        _controlsCluster.AddChild(_optionsButton);

        // Read-only seed display anchored to the bottom-left so a player
        // can recall or share the seed mid-game without crowding the
        // top-bar action UI. Small font + dim color so it sits in the
        // visual background.
        _seedLabel = new Label
        {
            Text = "",
            AnchorLeft = 0f,
            AnchorRight = 0f,
            AnchorTop = 1f,
            AnchorBottom = 1f,
            OffsetLeft = 12f,
            OffsetRight = 280f,
            OffsetTop = -48f,
            OffsetBottom = -8f,
        };
        _seedLabel.AddThemeFontSizeOverride("font_size", 28);
        _seedLabel.AddThemeColorOverride("font_color", new Color(0f, 0f, 0f, 1f));
        AddChild(_seedLabel);

        // Arrange the clusters for the current orientation + track resize
        // (OrientationHud owns the bars + the flip/publish lifecycle).
        InitOrientation();

        BuildVictoryOverlay(viewport);
        BuildDefeatOverlay(viewport);
        BuildClaimVictoryOverlay(viewport);
        BuildTutorialOverlay();
        BuildBankruptToast();
    }

    // ---- Orientation-aware layout (OrientationHud hooks) -----------------

    protected override void DetachClusters()
    {
        HudBars.Detach(_statusCluster);
        HudBars.Detach(_goldChip);
        HudBars.Detach(_actionCluster);
        HudBars.Detach(_controlsCluster);
    }

    /// <summary>Legacy single top strip: status + gold (left), actions
    /// (center), turn controls (right).</summary>
    protected override void BuildLandscapeBars()
    {
        TopBar = HudBars.MakeBarPanel(top: true, height: HudHeight);
        AddChild(TopBar);
        Control frame = HudBars.MakeBarFrame();
        TopBar.AddChild(frame);

        HBoxContainer left = HudBars.MakeAnchoredGroup(0f, Control.GrowDirection.End);
        frame.AddChild(left);
        left.AddChild(_statusCluster);
        left.AddChild(BuildVerticalDivider());
        left.AddChild(_goldChip);

        HBoxContainer center = HudBars.MakeAnchoredGroup(0.5f, Control.GrowDirection.Both);
        frame.AddChild(center);
        center.AddChild(_actionCluster);

        HBoxContainer right = HudBars.MakeAnchoredGroup(1f, Control.GrowDirection.Begin);
        frame.AddChild(right);
        right.AddChild(_controlsCluster);
    }

    /// <summary>Portrait split bars: top = territory content (gold + actions),
    /// shown only when a territory is selected; bottom = status (left) + turn
    /// controls (right), always up.</summary>
    protected override void BuildPortraitBars()
    {
        TopBar = HudBars.MakeBarPanel(top: true, height: PortraitTopBarHeight);
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
        topRow.AddChild(_goldChip);
        topRow.AddChild(_actionCluster);

        BottomBar = HudBars.MakeBarPanel(top: false, height: PortraitBottomBarHeight);
        AddChild(BottomBar);
        Control frame = HudBars.MakeBarFrame();
        BottomBar.AddChild(frame);

        HBoxContainer left = HudBars.MakeAnchoredGroup(0f, Control.GrowDirection.End);
        frame.AddChild(left);
        left.AddChild(_statusCluster);

        HBoxContainer right = HudBars.MakeAnchoredGroup(1f, Control.GrowDirection.Begin);
        frame.AddChild(right);
        right.AddChild(_controlsCluster);
    }

    // Below this viewport width the "TURN" / "TO PLAY" eyebrow captions are
    // dropped even in landscape: the status group would otherwise grow wide
    // enough (long economy report) to crowd the centered unit buttons. The
    // design target is 1600 wide; common laptop/narrow widths go compact.
    private const float CompactLandscapeWidth = 1500f;

    // Below these widths the player-swatch bar collapses to the current
    // player's swatch only — the full turn-order row doesn't fit. Two
    // thresholds because landscape also spends width on the centered
    // action cluster, so it needs more room before the full row fits.
    private const float FullSwatchRowWidthPortrait = 820f;
    private const float FullSwatchRowWidthLandscape = 1100f;

    /// <summary>Post-layout (orientation flip): the seed label is landscape-
    /// only, and the top bar follows the selection. Eyebrow visibility is
    /// width-driven, handled in OnViewportMetricsChanged.</summary>
    protected override void OnLayoutApplied()
    {
        _seedLabel.Visible = Orientation == ScreenOrientation.Landscape;
        UpdateTopBarVisibility();
        // Lift the tutorial box above the portrait bottom bar (no-op until the
        // overlay is built later in _Ready, and in landscape).
        PositionTutorialOverlay();
    }

    /// <summary>Drop the "TURN" / "TO PLAY" captions in portrait (no room) and
    /// in a narrow landscape window (they'd crowd the centered unit buttons).
    /// The turn number and player name always stay.</summary>
    protected override void OnViewportMetricsChanged()
    {
        float width = GetViewport().GetVisibleRect().Size.X;
        bool showEyebrows = Orientation == ScreenOrientation.Landscape
            && width >= CompactLandscapeWidth;
        _turnEyebrow.Visible = showEyebrows;

        float fullRowMin = Orientation == ScreenOrientation.Landscape
            ? FullSwatchRowWidthLandscape
            : FullSwatchRowWidthPortrait;
        bool compactSwatches = width < fullRowMin;
        _playerSwatchBar.SetCompact(compactSwatches);
        Log.Debug(Log.LogCategory.Render,
            $"[SwatchBar] metrics: width={width:0} orient={Orientation} compact={compactSwatches}");
    }

    protected override MapInsets ComputeInsets()
    {
        bool topVisible = Orientation == ScreenOrientation.Landscape || _selectionPresent;
        return ScreenLayout.ComputeInsets(
            Orientation, topVisible, HudHeight, PortraitTopBarHeight, PortraitBottomBarHeight);
    }

    /// <summary>Top bar shows always in landscape; in portrait only when a
    /// territory is selected (per the split-bar design).</summary>
    private void UpdateTopBarVisibility()
    {
        if (TopBar == null) return;
        TopBar.Visible = Orientation == ScreenOrientation.Landscape || _selectionPresent;
    }

    /// <summary>Called from Refresh with whether a territory is selected.
    /// In portrait this drives the top bar's visibility and the map's top
    /// inset; landscape ignores it (bar always up).</summary>
    private void SetSelectionPresent(bool present)
    {
        if (present == _selectionPresent) return;
        _selectionPresent = present;
        if (Orientation != ScreenOrientation.Portrait || TopBar == null) return;
        TopBar.Visible = present;
        Log.Debug(Log.LogCategory.Render, $"HudView: portrait top bar visible={present}.");
        PublishInsets();
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
        float lift = Orientation == ScreenOrientation.Portrait ? PortraitBottomBarHeight : 0f;
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

    private void BuildBankruptToast()
    {
        // 1.5x larger than the spec's reference so the toast reads at
        // the heavier scale the rest of the redesign settled on. Lives
        // top-center, just below the HUD bar, so it doesn't fight the
        // tutorial action-hint panel (which lives bottom-center).
        const float panelW = 660f;
        const float panelH = 96f;
        const float marginTop = 16f;

        _bankruptToast = new Panel
        {
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            AnchorTop = 0f,
            AnchorBottom = 0f,
            OffsetLeft = -panelW * 0.5f,
            OffsetRight = panelW * 0.5f,
            OffsetTop = HudHeight + marginTop,
            OffsetBottom = HudHeight + marginTop + panelH,
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
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _bankruptTitleLabel.AddThemeFontOverride("font", GeistFont);
        _bankruptTitleLabel.AddThemeFontSizeOverride("font_size", 24);
        _bankruptTitleLabel.AddThemeColorOverride("font_color", UiPalette.Ink);
        textBlock.AddChild(_bankruptTitleLabel);

        _bankruptSubLabel = new Label
        {
            Text = "All units in this territory will die",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _bankruptSubLabel.AddThemeFontOverride("font", GeistFont);
        _bankruptSubLabel.AddThemeFontSizeOverride("font_size", 21);
        _bankruptSubLabel.AddThemeColorOverride("font_color", UiPalette.InkMute);
        textBlock.AddChild(_bankruptSubLabel);

        row.AddChild(textBlock);
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

    private void BuildVictoryOverlay(Vector2 viewport)
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
            Position = new Vector2((viewport.X - panelW) * 0.5f, (viewport.Y - panelH) * 0.5f),
            Size = new Vector2(panelW, panelH),
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
    private void BuildDefeatOverlay(Vector2 viewport)
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
            Position = new Vector2((viewport.X - panelW) * 0.5f, (viewport.Y - panelH) * 0.5f),
            Size = new Vector2(panelW, panelH),
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
    private void BuildClaimVictoryOverlay(Vector2 viewport)
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
            Position = new Vector2((viewport.X - panelW) * 0.5f, (viewport.Y - panelH) * 0.5f),
            Size = new Vector2(panelW, panelH),
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

        // Portrait: the territory-content top bar shows whenever a territory
        // is selected (no-op in landscape, where the bar is always up).
        SetSelectionPresent(selected != null);

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
        Button target = button switch
        {
            CtaButton.BuyRecruit => _buyUnitButtons.First(b => b.BuyLevel == UnitLevel.Recruit),
            CtaButton.EndTurn => _endTurnButton,
            CtaButton.BuildTower => _buildTowerButton,
            CtaButton.ClaimVictoryWinNow => _claimWinNowButton,
            CtaButton.ClaimVictoryContinue => _claimContinueButton,
            CtaButton.DefeatContinue => _defeatContinueButton,
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
