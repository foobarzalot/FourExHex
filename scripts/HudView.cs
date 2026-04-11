using System;
using Godot;

/// <summary>
/// Top-strip heads-up display. A passive view: builds its widgets once in
/// <see cref="_Ready"/>, raises C# events for each button press, and
/// updates label text / button disabled state when the controller calls
/// <see cref="Refresh"/>. Owns no game data.
/// </summary>
public partial class HudView : CanvasLayer
{
    public const float HudHeight = 60f;

    public event Action? BuyPeasantClicked;
    public event Action? UndoLastClicked;
    public event Action? UndoTurnClicked;
    public event Action? RedoLastClicked;
    public event Action? RedoAllClicked;
    public event Action? EndTurnClicked;

    private Label _turnLabel = null!;
    private Label _playerLabel = null!;
    private Label _goldLabel = null!;
    private Button _buyPeasantButton = null!;
    private Button _undoLastButton = null!;
    private Button _undoTurnButton = null!;
    private Button _redoLastButton = null!;
    private Button _redoAllButton = null!;
    private Button _endTurnButton = null!;

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

        _buyPeasantButton = new Button { Text = "Buy Peasant (10g)", Visible = false };
        _buyPeasantButton.AddThemeFontSizeOverride("font_size", 20);
        _buyPeasantButton.Pressed += () => BuyPeasantClicked?.Invoke();
        leftHbox.AddChild(_buyPeasantButton);

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

        _undoTurnButton = new Button { Text = "Undo Turn", Disabled = true };
        _undoTurnButton.AddThemeFontSizeOverride("font_size", 18);
        _undoTurnButton.Pressed += () => UndoTurnClicked?.Invoke();
        rightHbox.AddChild(_undoTurnButton);

        _undoLastButton = new Button { Text = "Undo Last", Disabled = true };
        _undoLastButton.AddThemeFontSizeOverride("font_size", 18);
        _undoLastButton.Pressed += () => UndoLastClicked?.Invoke();
        rightHbox.AddChild(_undoLastButton);

        _redoLastButton = new Button { Text = "Redo Last", Disabled = true };
        _redoLastButton.AddThemeFontSizeOverride("font_size", 18);
        _redoLastButton.Pressed += () => RedoLastClicked?.Invoke();
        rightHbox.AddChild(_redoLastButton);

        _redoAllButton = new Button { Text = "Redo All", Disabled = true };
        _redoAllButton.AddThemeFontSizeOverride("font_size", 18);
        _redoAllButton.Pressed += () => RedoAllClicked?.Invoke();
        rightHbox.AddChild(_redoAllButton);

        _endTurnButton = new Button { Text = "End Turn" };
        _endTurnButton.AddThemeFontSizeOverride("font_size", 18);
        _endTurnButton.Pressed += () => EndTurnClicked?.Invoke();
        rightHbox.AddChild(_endTurnButton);
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

        // Gold label + buy button depend on the selection.
        Territory? selected = session.SelectedTerritory;
        if (selected == null || !selected.HasCapital)
        {
            _goldLabel.Text = "";
            _buyPeasantButton.Visible = false;
            _buyPeasantButton.Text = "Buy Peasant (10g)";
        }
        else
        {
            int gold = state.Treasury.GetGold(selected.Capital!.Value);
            _goldLabel.Text = $"Gold: {gold} (size {selected.Size})";
            _buyPeasantButton.Visible = PurchaseRules.CanAffordPeasant(selected, state.Treasury);
            _buyPeasantButton.Text = session.Mode == SessionState.ActionMode.BuyingPeasant
                ? "Click a tile..."
                : "Buy Peasant (10g)";
        }

        _undoLastButton.Disabled = !session.Undo.CanUndo;
        _undoTurnButton.Disabled = !session.Undo.CanUndo;
        _redoLastButton.Disabled = !session.Undo.CanRedo;
        _redoAllButton.Disabled = !session.Undo.CanRedo;

        SetEndTurnCta(!hasActionableRemaining);
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
