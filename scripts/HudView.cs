using System;
using System.Linq;
using Godot;

/// <summary>
/// Top-strip heads-up display. A passive view: builds its widgets once in
/// <see cref="_Ready"/>, raises C# events for each button press, and
/// updates label text / button disabled state when the controller calls
/// <see cref="Refresh"/>. Owns no game data.
/// </summary>
public partial class HudView : CanvasLayer, IHudView
{
    public const float HudHeight = 60f;

    public event Action? BuyPeasantClicked;
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

    private Label _turnLabel = null!;
    private Label _playerLabel = null!;
    private Label _goldLabel = null!;
    private Label _seedLabel = null!;
    // One radio button per buy level (Peasant / Spearman / Knight / Baron),
    // in cycle order. _buyUnitButtons[(int)level] gives the button for
    // a given UnitLevel. _buyUnitButtons[0] = Peasant (the legacy
    // CtaButton.BuyPeasant target).
    private HudIconButton[] _buyUnitButtons = null!;
    private HudIconButton _buildTowerButton = null!;
    private HudIconButton _undoLastButton = null!;
    private HudIconButton _undoTurnButton = null!;
    private HudIconButton _redoLastButton = null!;
    private HudIconButton _redoAllButton = null!;
    private bool _undoRedoLocked;
    private bool _victoryOverlaySuppressed;
    private HudIconButton _endTurnButton = null!;
    private HudIconButton _optionsButton = null!;
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

    // Snapshot of session.Mode != None at the last Refresh, so the Escape
    // handler can decide between cancel-action (pending) and End Game (idle)
    // without holding a SessionState reference.
    private bool _hasPendingAction;

    public override void _Ready()
    {
        Vector2 viewport = GetViewport().GetVisibleRect().Size;

        // Dark bar across the top so labels stay readable against the map.
        var background = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.8f),
            Position = Vector2.Zero,
            Size = new Vector2(viewport.X, HudHeight),
        };
        AddChild(background);

        // Left-aligned info labels + buy button.
        var leftHbox = new HBoxContainer
        {
            Position = new Vector2(16, 12),
        };
        leftHbox.AddThemeConstantOverride("separation", 24);
        AddChild(leftHbox);

        // Fixed minimum widths so the buy/build buttons that follow these
        // labels in the HBox don't slide left/right as the text changes
        // (player name length, turn rollover, gold/income string growing
        // and shrinking, gold blanking when no capital is selected). Sized
        // for worst-case content at font_size 24: "Turn: 999",
        // "Current: Orange", "9999g (99-99=+99)".
        _turnLabel = new Label
        {
            Text = "Turn: 1",
            CustomMinimumSize = new Vector2(130, 0),
        };
        _turnLabel.AddThemeFontSizeOverride("font_size", 24);
        leftHbox.AddChild(_turnLabel);

        _playerLabel = new Label
        {
            Text = "Current: Red",
            CustomMinimumSize = new Vector2(200, 0),
        };
        _playerLabel.AddThemeFontSizeOverride("font_size", 24);
        leftHbox.AddChild(_playerLabel);

        _goldLabel = new Label
        {
            Text = "",
            CustomMinimumSize = new Vector2(240, 0),
        };
        _goldLabel.AddThemeFontSizeOverride("font_size", 24);
        leftHbox.AddChild(_goldLabel);

        // Four always-visible radio buttons (Peasant / Spearman / Knight
        // / Baron) packed in a nested HBox so the row sits as one unit
        // in the parent layout. Per-button Disabled / Selected /
        // tooltip are set by Refresh(). Each click fires BuyUnitClicked
        // with its specific level — direct selection, no cycling. The
        // U-key fires BuyPeasantClicked which cycles in the controller.
        var buyRow = new HBoxContainer();
        buyRow.AddThemeConstantOverride("separation", 4);
        leftHbox.AddChild(buyRow);

        UnitLevel[] buyLevels = { UnitLevel.Peasant, UnitLevel.Spearman, UnitLevel.Knight, UnitLevel.Baron };
        _buyUnitButtons = new HudIconButton[buyLevels.Length];
        for (int i = 0; i < buyLevels.Length; i++)
        {
            UnitLevel level = buyLevels[i];
            var button = new HudIconButton(HudIcon.Peasant)
            {
                Disabled = true,
                BuyLevel = level,
            };
            button.Pressed += () => BuyUnitClicked?.Invoke(level);
            AudioBus.AttachClick(button);
            buyRow.AddChild(button);
            _buyUnitButtons[i] = button;
        }

        _buildTowerButton = new HudIconButton(HudIcon.Tower) { Disabled = true };
        _buildTowerButton.Pressed += () => BuildTowerClicked?.Invoke();
        AudioBus.AttachClick(_buildTowerButton);
        leftHbox.AddChild(_buildTowerButton);

        // Right-anchored action row: Undo Turn / Undo Last / Redo Last /
        // Redo All / End Turn. The HBoxContainer spans the HUD width with
        // End alignment so children pack to the right edge. MouseFilter
        // Ignore keeps clicks on the left-HBox (Buy Peasant) from being
        // swallowed by this full-width container.
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

        _undoTurnButton = new HudIconButton(HudIcon.UndoAll) { Disabled = true };
        _undoTurnButton.Pressed += () => UndoTurnClicked?.Invoke();
        AudioBus.AttachClick(_undoTurnButton);
        rightHbox.AddChild(_undoTurnButton);

        _undoLastButton = new HudIconButton(HudIcon.UndoLast) { Disabled = true };
        _undoLastButton.Pressed += () => UndoLastClicked?.Invoke();
        AudioBus.AttachClick(_undoLastButton);
        rightHbox.AddChild(_undoLastButton);

        _redoLastButton = new HudIconButton(HudIcon.RedoLast) { Disabled = true };
        _redoLastButton.Pressed += () => RedoLastClicked?.Invoke();
        AudioBus.AttachClick(_redoLastButton);
        rightHbox.AddChild(_redoLastButton);

        _redoAllButton = new HudIconButton(HudIcon.RedoAll) { Disabled = true };
        _redoAllButton.Pressed += () => RedoAllClicked?.Invoke();
        AudioBus.AttachClick(_redoAllButton);
        rightHbox.AddChild(_redoAllButton);

        _endTurnButton = new HudIconButton(HudIcon.EndTurn);
        _endTurnButton.Pressed += () => EndTurnClicked?.Invoke();
        AudioBus.AttachClick(_endTurnButton);
        rightHbox.AddChild(_endTurnButton);

        // Single Options button — raises the same EscRequested event
        // the Escape key fires, so the scene root's pause coordinator
        // drives both paths. Save Game and Settings live inside that
        // pause menu now rather than as standalone HUD buttons.
        _optionsButton = new HudIconButton(HudIcon.Options);
        _optionsButton.Pressed += () => EscRequested?.Invoke();
        AudioBus.AttachClick(_optionsButton);
        rightHbox.AddChild(_optionsButton);

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

        BuildVictoryOverlay(viewport);
        BuildDefeatOverlay(viewport);
        BuildClaimVictoryOverlay(viewport);
        BuildTutorialOverlay();
    }

    public void SetMapLabel(string text)
    {
        _seedLabel.Text = text;
    }

    public event Action? TutorialMessageTapped;

    /// <summary>
    /// Full-viewport invisible overlay used while a tappable tutorial
    /// message is showing. Built lazily on first tappable-show. Sits
    /// above every other HudView child so a click anywhere on the
    /// screen (HUD buttons, map tiles, the panel itself) is intercepted
    /// and advances the tutorial — the player can't accidentally hit
    /// Buy / End Turn / select a tile while a narration beat is gated.
    /// </summary>
    private Control? _tutorialTapCatcher;

    // True while an external caller (tutorial system, AI-batch indicator)
    // owns the tutorial-message panel. Refresh()'s gameplay action hint
    // ("Click to place a Peasant", "Click to move the Knight") only
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
    }

    public void HideTutorialMessage()
    {
        _externalMessageActive = false;
        _tutorialPanel.Visible = false;
        SetTutorialTapCatcherEnabled(false);
    }

    private void SetTutorialTapCatcherEnabled(bool enabled)
    {
        if (enabled)
        {
            if (_tutorialTapCatcher == null)
            {
                Vector2 viewport = GetViewport().GetVisibleRect().Size;
                _tutorialTapCatcher = new Control
                {
                    Position = Vector2.Zero,
                    Size = viewport,
                    AnchorLeft = 0f, AnchorRight = 1f,
                    AnchorTop = 0f, AnchorBottom = 1f,
                    MouseFilter = Control.MouseFilterEnum.Stop,
                };
                _tutorialTapCatcher.GuiInput += OnTutorialTapCatcherInput;
                AddChild(_tutorialTapCatcher);
            }
            // Re-parent so the catcher is the topmost child of HudView,
            // guaranteeing it receives clicks ahead of HUD buttons, the
            // map below, and the tutorial panel.
            MoveChild(_tutorialTapCatcher, GetChildCount() - 1);
            _tutorialTapCatcher.Visible = true;
            _tutorialTapCatcher.MouseFilter = Control.MouseFilterEnum.Stop;
        }
        else if (_tutorialTapCatcher != null)
        {
            _tutorialTapCatcher.Visible = false;
            _tutorialTapCatcher.MouseFilter = Control.MouseFilterEnum.Ignore;
        }
    }

    private void OnTutorialTapCatcherInput(InputEvent ev)
    {
        if (ev is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
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
    private void BuildTutorialOverlay()
    {
        // Width and height grew to fit the longer instruction strings
        // ("Move the selected X onto the highlighted tile to destroy
        // the tower and capture it.") and to give the autowrapped label
        // room for two or three lines without bleeding out the panel.
        const float panelW = 720f;
        const float panelH = 120f;
        const float marginBottom = 60f;

        _tutorialPanel = new Panel
        {
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            AnchorTop = 1f,
            AnchorBottom = 1f,
            OffsetLeft = -panelW * 0.5f,
            OffsetRight = panelW * 0.5f,
            OffsetTop = -marginBottom - panelH,
            OffsetBottom = -marginBottom,
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
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

        // Centered panel with the win text and three buttons: Play
        // Again, Replay, and Main Menu. Bumped from 460→540 wide when
        // the Replay button was added to fit three 150-wide buttons
        // with 12px gaps.
        const float panelW = 540f;
        const float panelH = 220f;
        var panel = new Panel
        {
            Position = new Vector2((viewport.X - panelW) * 0.5f, (viewport.Y - panelH) * 0.5f),
            Size = new Vector2(panelW, panelH),
        };
        _victoryOverlay.AddChild(panel);

        _victoryLabel = new Label
        {
            Text = "Victory!",
            HorizontalAlignment = HorizontalAlignment.Center,
            Position = new Vector2(0, 40),
            Size = new Vector2(panelW, 48),
        };
        _victoryLabel.AddThemeFontSizeOverride("font_size", 36);
        panel.AddChild(_victoryLabel);

        const float buttonW = 150f;
        const float buttonH = 44f;
        const float gap = 12f;
        float rowY = 130f;
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

        const float panelW = 460f;
        const float panelH = 220f;
        var panel = new Panel
        {
            Position = new Vector2((viewport.X - panelW) * 0.5f, (viewport.Y - panelH) * 0.5f),
            Size = new Vector2(panelW, panelH),
        };
        _defeatOverlay.AddChild(panel);

        _defeatLabel = new Label
        {
            Text = "Defeated",
            HorizontalAlignment = HorizontalAlignment.Center,
            Position = new Vector2(0, 40),
            Size = new Vector2(panelW, 48),
        };
        _defeatLabel.AddThemeFontSizeOverride("font_size", 36);
        panel.AddChild(_defeatLabel);

        const float buttonW = 130f;
        const float buttonH = 44f;
        const float gap = 20f;
        float rowY = 130f;
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

        const float panelW = 520f;
        const float panelH = 260f;
        var panel = new Panel
        {
            Position = new Vector2((viewport.X - panelW) * 0.5f, (viewport.Y - panelH) * 0.5f),
            Size = new Vector2(panelW, panelH),
        };
        _claimVictoryOverlay.AddChild(panel);

        var headerLabel = new Label
        {
            Text = "Claim Victory?",
            HorizontalAlignment = HorizontalAlignment.Center,
            Position = new Vector2(0, 64),
            Size = new Vector2(panelW, 48),
        };
        headerLabel.AddThemeFontSizeOverride("font_size", 32);
        panel.AddChild(headerLabel);

        const float buttonW = 200f;
        const float buttonH = 48f;
        const float gap = 20f;
        float rowY = 170f;
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
                BuyPeasantClicked?.Invoke();
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
        _turnLabel.Text = $"Turn: {state.Turns.TurnNumber}";
        Player current = state.Turns.CurrentPlayer;
        _playerLabel.Text = $"Current: {current.Name}";
        _playerLabel.AddThemeColorOverride("font_color", current.Color);

        // Buy / Build buttons are always visible; the tooltip explains
        // *why* the button is disabled (no selection / no capital / can't
        // afford) so the player isn't left guessing. The contextual cost
        // and mode-hint live in the tooltip; the in-progress mode is
        // signalled with the Selected outline.
        Territory? selected = session.SelectedTerritory;
        bool hasCapital = selected?.HasCapital ?? false;

        if (hasCapital)
        {
            int gold = state.Treasury.GetGold(selected!.Capital!.Value);
            int income = TreeRules.CountIncomeProducingTiles(selected, state.Grid);
            int upkeep = UpkeepRules.TotalUpkeepFor(selected, state.Grid);
            int net = income - upkeep;
            string sign = net >= 0 ? "+" : "";
            _goldLabel.Text = $"{gold}g ({income}-{upkeep}={sign}{net})";
        }
        else
        {
            _goldLabel.Text = "";
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
        _undoTurnButton.Disabled = _undoRedoLocked || !session.Undo.CanUndo;
        _redoLastButton.Disabled = _undoRedoLocked || !session.Undo.CanRedo;
        _redoAllButton.Disabled = _undoRedoLocked || !session.Undo.CanRedo;
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
            Color winColor = session.Winner.Value;
            Player? winner = state.Turns.Players
                .FirstOrDefault(p => p.Color == winColor);
            string name = winner?.Name ?? "Unknown";
            _victoryLabel.Text = $"{name} wins!";
            _victoryLabel.AddThemeColorOverride("font_color", winColor);
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
            Color loseColor = session.PendingDefeatScreen.Value;
            Player? loser = state.Turns.Players
                .FirstOrDefault(p => p.Color == loseColor);
            string name = loser?.Name ?? "Unknown";
            _defeatLabel.Text = $"{name} defeated";
            _defeatLabel.AddThemeColorOverride("font_color", loseColor);
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
            UnitLevel level = (src?.Unit?.Level) ?? UnitLevel.Peasant;
            return $"Click to move the {level}";
        }
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
            CtaButton.BuyPeasant => _buyUnitButtons.First(b => b.BuyLevel == UnitLevel.Peasant),
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
            _undoTurnButton.Disabled = true;
            _redoLastButton.Disabled = true;
            _redoAllButton.Disabled = true;
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
