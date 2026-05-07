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
}
