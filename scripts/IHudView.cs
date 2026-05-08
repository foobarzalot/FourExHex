using System;

/// <summary>
/// The contract the game controller uses to talk to the HUD view.
/// Extracted from <see cref="HudView"/> so the controller can be
/// unit-tested with a mock HUD that captures Refresh calls and lets
/// tests raise button events.
/// </summary>
public interface IHudView
{
    event Action? BuyPeasantClicked;
    event Action? BuildTowerClicked;
    event Action? UndoLastClicked;
    event Action? UndoTurnClicked;
    event Action? RedoLastClicked;
    event Action? RedoAllClicked;
    event Action? EndTurnClicked;
    event Action? NewGameClicked;
    event Action? MainMenuClicked;
    event Action? NextTerritoryClicked;
    event Action? PreviousTerritoryClicked;
    event Action? NextUnitClicked;
    event Action? PreviousUnitClicked;
    event Action? CancelActionPressed;
    event Action? SaveGameClicked;
    /// <summary>
    /// Continue button on the defeat overlay. Dismisses the overlay
    /// and resumes play (paused AI loop picks up where it left off).
    /// The "Main Menu" button on the same overlay reuses the existing
    /// <see cref="MainMenuClicked"/> event.
    /// </summary>
    event Action? DefeatContinueClicked;

    /// <summary>
    /// Win Now button on the claim-victory overlay (shown when a human
    /// presses End Turn while owning >50% of all tiles). Declares the
    /// human as the winner and transitions to the victory screen.
    /// </summary>
    event Action? ClaimVictoryWinNowClicked;

    /// <summary>
    /// Continue Playing button on the claim-victory overlay. Dismisses
    /// the overlay and proceeds with the End Turn the player just
    /// pressed (advance + AI loop, exactly as if no prompt appeared).
    /// </summary>
    event Action? ClaimVictoryContinueClicked;

    /// <summary>
    /// Update every label, button disabled state, and the End Turn CTA
    /// styling from the current game + session state.
    /// </summary>
    void Refresh(GameState state, SessionState session, bool hasActionableRemaining);

    /// <summary>
    /// One-time announcement of the map identity to display in a small
    /// read-only label (bottom-left). For procedural maps this is the
    /// master seed; for loaded starting maps it is the map's name.
    /// Invariant per game; called once after game setup.
    /// </summary>
    void SetMapLabel(string text);

    /// <summary>
    /// Show a non-interactive informational popup at the bottom of the
    /// screen with the given text. Used by tutorial scripting to display
    /// instructions / narration. The HUD does not auto-dismiss; the
    /// scene root drives <see cref="HideTutorialMessage"/> in response
    /// to whatever input it considers acknowledgement.
    /// </summary>
    void ShowTutorialMessage(string text);

    /// <summary>Hide the tutorial popup if it's currently showing.</summary>
    void HideTutorialMessage();
}
