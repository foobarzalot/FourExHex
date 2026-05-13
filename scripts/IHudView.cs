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
    /// Replay button on the victory overlay. Rewinds the controller to
    /// its captured initial snapshot and steps through the recorded
    /// beats; player input is ignored during playback. Visible only
    /// when there is replay data available — see <see cref="SetReplayAvailable"/>.
    /// </summary>
    event Action? ReplayClicked;

    /// <summary>
    /// Toggle visibility/enablement of the victory overlay's Replay
    /// button. Disabled when the current game has no replay data
    /// (e.g., loaded from a pre-feature save).
    /// </summary>
    void SetReplayAvailable(bool available);

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

    /// <summary>
    /// Apply (or clear) tutorial-driven CTA styling to the Buy Peasant
    /// button — same white-bg + black-text + border the End Turn button
    /// already uses when no actions remain. Tutorial Preview only;
    /// ordinary play never calls this and the styling clears
    /// automatically when the player commits the buy (via the wrapper
    /// dropping back to <c>SetBuyPeasantCta(false)</c>).
    /// </summary>
    void SetBuyPeasantCta(bool isCta);

    /// <summary>
    /// Apply (or clear) CTA styling to the End Turn button. Called from
    /// <see cref="GameController.RefreshViews"/> to indicate the human
    /// has no actionable territories left, and from Tutorial Preview to
    /// indicate the next scripted beat is End Turn.
    /// </summary>
    void SetEndTurnCta(bool isCta);

    /// <summary>
    /// Apply (or clear) tutorial-driven CTA styling to the Build Tower
    /// button. Tutorial Preview only.
    /// </summary>
    void SetBuildTowerCta(bool isCta);

    /// <summary>
    /// Apply (or clear) CTA styling to the claim-victory overlay's
    /// "Win Now" button. Tutorial Preview only.
    /// </summary>
    void SetClaimVictoryWinNowCta(bool isCta);

    /// <summary>
    /// Apply (or clear) CTA styling to the claim-victory overlay's
    /// "Continue Playing" button. Tutorial Preview only.
    /// </summary>
    void SetClaimVictoryContinueCta(bool isCta);

    /// <summary>
    /// Apply (or clear) CTA styling to the defeat overlay's "Continue"
    /// button. Tutorial Preview only.
    /// </summary>
    void SetDefeatContinueCta(bool isCta);
}
