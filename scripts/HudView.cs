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
    public event Action? BuildTowerClicked;
    public event Action? UndoLastClicked;
    public event Action? UndoTurnClicked;
    public event Action? RedoLastClicked;
    public event Action? RedoAllClicked;
    public event Action? EndTurnClicked;
    public event Action? NewGameClicked;
    public event Action? MainMenuClicked;
    public event Action? NextTerritoryClicked;

    private Label _turnLabel = null!;
    private Label _playerLabel = null!;
    private Label _goldLabel = null!;
    private Button _buyPeasantButton = null!;
    private Button _buildTowerButton = null!;
    private Button _undoLastButton = null!;
    private Button _undoTurnButton = null!;
    private Button _redoLastButton = null!;
    private Button _redoAllButton = null!;
    private Button _endTurnButton = null!;
    private Button _endGameButton = null!;
    private ConfirmationDialog _endGameDialog = null!;
    private Control _victoryOverlay = null!;
    private Label _victoryLabel = null!;

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

        _turnLabel = new Label { Text = "Turn: 1" };
        _turnLabel.AddThemeFontSizeOverride("font_size", 24);
        leftHbox.AddChild(_turnLabel);

        _playerLabel = new Label { Text = "Current: Red" };
        _playerLabel.AddThemeFontSizeOverride("font_size", 24);
        leftHbox.AddChild(_playerLabel);

        _goldLabel = new Label { Text = "" };
        _goldLabel.AddThemeFontSizeOverride("font_size", 24);
        leftHbox.AddChild(_goldLabel);

        _buyPeasantButton = new Button
        {
            Text = "Buy Peasant (10g)",
            Visible = false,
            // Non-focusable: keyboard shortcuts like Tab/Enter must
            // reach _UnhandledInput rather than being consumed by a
            // focused Button's default key handlers.
            FocusMode = Control.FocusModeEnum.None,
        };
        _buyPeasantButton.AddThemeFontSizeOverride("font_size", 20);
        _buyPeasantButton.Pressed += () => BuyPeasantClicked?.Invoke();
        leftHbox.AddChild(_buyPeasantButton);

        _buildTowerButton = new Button
        {
            Text = "Build Tower (15g)",
            Visible = false,
            FocusMode = Control.FocusModeEnum.None,
        };
        _buildTowerButton.AddThemeFontSizeOverride("font_size", 20);
        _buildTowerButton.Pressed += () => BuildTowerClicked?.Invoke();
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

        _undoTurnButton = new Button
        {
            Text = "Undo Turn",
            Disabled = true,
            FocusMode = Control.FocusModeEnum.None,
        };
        _undoTurnButton.AddThemeFontSizeOverride("font_size", 18);
        _undoTurnButton.Pressed += () => UndoTurnClicked?.Invoke();
        rightHbox.AddChild(_undoTurnButton);

        _undoLastButton = new Button
        {
            Text = "Undo Last",
            Disabled = true,
            FocusMode = Control.FocusModeEnum.None,
        };
        _undoLastButton.AddThemeFontSizeOverride("font_size", 18);
        _undoLastButton.Pressed += () => UndoLastClicked?.Invoke();
        rightHbox.AddChild(_undoLastButton);

        _redoLastButton = new Button
        {
            Text = "Redo Last",
            Disabled = true,
            FocusMode = Control.FocusModeEnum.None,
        };
        _redoLastButton.AddThemeFontSizeOverride("font_size", 18);
        _redoLastButton.Pressed += () => RedoLastClicked?.Invoke();
        rightHbox.AddChild(_redoLastButton);

        _redoAllButton = new Button
        {
            Text = "Redo All",
            Disabled = true,
            FocusMode = Control.FocusModeEnum.None,
        };
        _redoAllButton.AddThemeFontSizeOverride("font_size", 18);
        _redoAllButton.Pressed += () => RedoAllClicked?.Invoke();
        rightHbox.AddChild(_redoAllButton);

        _endTurnButton = new Button
        {
            Text = "End Turn",
            FocusMode = Control.FocusModeEnum.None,
        };
        _endTurnButton.AddThemeFontSizeOverride("font_size", 18);
        _endTurnButton.Pressed += () => EndTurnClicked?.Invoke();
        rightHbox.AddChild(_endTurnButton);

        // Abandon-game button in the top-right corner. Always available;
        // prompts a confirmation dialog before returning to the main
        // menu (which rerandomizes the grid next time Start Game is
        // pressed because Main._Ready rebuilds the grid every scene
        // load).
        _endGameButton = new Button
        {
            Text = "End Game",
            FocusMode = Control.FocusModeEnum.None,
        };
        _endGameButton.AddThemeFontSizeOverride("font_size", 18);
        _endGameButton.Pressed += () => _endGameDialog.PopupCentered();
        rightHbox.AddChild(_endGameButton);

        _endGameDialog = new ConfirmationDialog
        {
            Title = "End Game",
            DialogText = "Return to the main menu? The current game will be lost.",
            OkButtonText = "End Game",
            // Exclusive so it can't be dismissed by clicking outside —
            // the user must pick End Game or Cancel.
            Exclusive = true,
        };
        _endGameDialog.Confirmed += () => MainMenuClicked?.Invoke();
        AddChild(_endGameDialog);

        BuildVictoryOverlay(viewport);
    }

    /// <summary>
    /// Build a centered, click-blocking panel with "Player wins!" and a
    /// New Game button. Hidden by default; <see cref="Refresh"/> toggles
    /// visibility based on <see cref="SessionState.Winner"/>.
    /// </summary>
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

        // Centered panel with the win text and two buttons: Play
        // Again (reload scene with same GameSettings) and Main Menu
        // (swap back to the menu scene to reassign roles).
        const float panelW = 460f;
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

        const float buttonW = 180f;
        const float buttonH = 44f;
        const float gap = 20f;
        float rowY = 130f;
        float rowX = (panelW - (buttonW * 2f + gap)) * 0.5f;

        var playAgainButton = new Button { Text = "Play Again" };
        playAgainButton.AddThemeFontSizeOverride("font_size", 22);
        playAgainButton.Position = new Vector2(rowX, rowY);
        playAgainButton.Size = new Vector2(buttonW, buttonH);
        playAgainButton.Pressed += () => NewGameClicked?.Invoke();
        panel.AddChild(playAgainButton);

        var mainMenuButton = new Button { Text = "Main Menu" };
        mainMenuButton.AddThemeFontSizeOverride("font_size", 22);
        mainMenuButton.Position = new Vector2(rowX + buttonW + gap, rowY);
        mainMenuButton.Size = new Vector2(buttonW, buttonH);
        mainMenuButton.Pressed += () => MainMenuClicked?.Invoke();
        panel.AddChild(mainMenuButton);
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

        switch (keyEvent.Keycode)
        {
            case Key.Enter:
            case Key.KpEnter:
                EndTurnClicked?.Invoke();
                GetViewport().SetInputAsHandled();
                break;
            case Key.Tab:
                NextTerritoryClicked?.Invoke();
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
        _turnLabel.Text = $"Turn: {state.Turns.TurnNumber}";
        Player current = state.Turns.CurrentPlayer;
        _playerLabel.Text = $"Current: {current.Name}";
        _playerLabel.AddThemeColorOverride("font_color", current.Color);

        // Gold label + buy/build buttons depend on the selection.
        Territory? selected = session.SelectedTerritory;
        if (selected == null || !selected.HasCapital)
        {
            _goldLabel.Text = "";
            _buyPeasantButton.Visible = false;
            _buyPeasantButton.Text = "Buy Peasant (10g)";
            _buildTowerButton.Visible = false;
            _buildTowerButton.Text = "Build Tower (15g)";
        }
        else
        {
            int gold = state.Treasury.GetGold(selected.Capital!.Value);
            int income = TreeRules.CountNonTreeTiles(selected, state.Grid);
            int upkeep = UpkeepRules.TotalUpkeepFor(selected, state.Grid);
            int net = income - upkeep;
            string sign = net >= 0 ? "+" : "";
            _goldLabel.Text = $"Gold: {gold}  (income {income}, upkeep {upkeep}, net {sign}{net})";
            _buyPeasantButton.Visible = PurchaseRules.CanAffordPeasant(selected, state.Treasury);
            _buyPeasantButton.Text = session.Mode == SessionState.ActionMode.BuyingPeasant
                ? "Click a tile..."
                : "Buy Peasant (10g)";
            _buildTowerButton.Visible = PurchaseRules.CanAffordTower(selected, state.Treasury);
            _buildTowerButton.Text = session.Mode == SessionState.ActionMode.BuildingTower
                ? "Click a tile..."
                : "Build Tower (15g)";
        }

        _undoLastButton.Disabled = !session.Undo.CanUndo;
        _undoTurnButton.Disabled = !session.Undo.CanUndo;
        _redoLastButton.Disabled = !session.Undo.CanRedo;
        _redoAllButton.Disabled = !session.Undo.CanRedo;

        SetEndTurnCta(!hasActionableRemaining);

        // Victory overlay: show iff a winner has been declared.
        if (session.Winner.HasValue)
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
    }

    private void SetEndTurnCta(bool isCta)
    {
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
            _endTurnButton.AddThemeStyleboxOverride("normal", style);
            _endTurnButton.AddThemeStyleboxOverride("hover", style);
            _endTurnButton.AddThemeStyleboxOverride("pressed", style);
            _endTurnButton.AddThemeColorOverride("font_color", new Color(0f, 0f, 0f));
            _endTurnButton.AddThemeColorOverride("font_hover_color", new Color(0f, 0f, 0f));
            _endTurnButton.AddThemeColorOverride("font_pressed_color", new Color(0f, 0f, 0f));
        }
        else
        {
            _endTurnButton.RemoveThemeStyleboxOverride("normal");
            _endTurnButton.RemoveThemeStyleboxOverride("hover");
            _endTurnButton.RemoveThemeStyleboxOverride("pressed");
            _endTurnButton.RemoveThemeColorOverride("font_color");
            _endTurnButton.RemoveThemeColorOverride("font_hover_color");
            _endTurnButton.RemoveThemeColorOverride("font_pressed_color");
        }
    }
}
