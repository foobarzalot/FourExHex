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

    /// <summary>
    /// Update every label, button disabled state, and the End Turn CTA
    /// styling from the current game + session state.
    /// </summary>
    void Refresh(GameState state, SessionState session, bool hasActionableRemaining);
}
