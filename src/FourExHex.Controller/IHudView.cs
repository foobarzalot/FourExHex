// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;

/// <summary>
/// The contract the game controller uses to talk to the HUD view.
/// Extracted from <see cref="HudView"/> so the controller can be
/// unit-tested with a mock HUD that captures Refresh calls and lets
/// tests raise button events.
/// </summary>
public interface IHudView
{
    /// <summary>
    /// "Cycle to next buy mode" — fired by the U hotkey. From None,
    /// enters the lowest affordable level; from a buy mode, advances
    /// to the next higher affordable level; from the top of the
    /// affordable subset, exits back to None.
    /// </summary>
    event Action? BuyRecruitClicked;

    /// <summary>
    /// "Enter a specific buy mode" — fired by clicking one of the four
    /// per-level radio buttons on the HUD. Idempotent: clicking the
    /// already-active level is a no-op (no undo push).
    /// </summary>
    event Action<UnitLevel>? BuyUnitClicked;

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
    /// <summary>Raised by the next-unit button's long-press: skip to the
    /// first movable unit of the next-higher power tier (wrapping).</summary>
    event Action? NextUnitTierClicked;
    event Action? PreviousUnitClicked;
    event Action? CancelActionPressed;

    /// <summary>
    /// Toggle button: on a human turn, starts AI-driven automation of
    /// the player's remaining moves; pressed again while running, stops
    /// it between moves. See <see cref="SetAutomateState"/> for the
    /// button state the controller pushes back.
    /// </summary>
    event Action? AutomateClicked;
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
    /// Show a transient, non-modal announcement banner: fades in, holds a
    /// few seconds, fades out on its own; never blocks input (fully
    /// click-through) and independent of the tutorial-message slot below.
    /// Used for the Viking Raiders wave countdown at human turn start;
    /// reusable for future announcements. Re-showing restarts the fade.
    /// </summary>
    void ShowTransientBanner(string text);

    /// <summary>
    /// Show a non-interactive informational popup at the bottom of the
    /// screen with the given text. Used by tutorial scripting to display
    /// instructions / narration. The HUD does not auto-dismiss; the
    /// scene root drives <see cref="HideTutorialMessage"/> in response
    /// to whatever input it considers acknowledgement.
    /// MouseFilter on the panel is left as Ignore so clicks pass through
    /// to the map below — use <see cref="ShowTappableTutorialMessage"/>
    /// for a tap-to-dismiss variant.
    /// </summary>
    void ShowTutorialMessage(string text);

    /// <summary>Hide the tutorial popup if it's currently showing.
    /// Also resets the panel's MouseFilter to Ignore so any prior
    /// tappable-mode capture is cleared.</summary>
    void HideTutorialMessage();

    /// <summary>
    /// Show the same bottom-anchored tutorial popup as
    /// <see cref="ShowTutorialMessage"/>, but with the panel set to
    /// capture clicks (MouseFilter = Stop). Tapping the panel fires
    /// <see cref="TutorialMessageTapped"/>. Used by
    /// <c>TutorialNarrationDriver</c> for display-text beats that
    /// block until the player acknowledges. The HUD does not auto-hide
    /// after the tap — the caller (driver) is expected to advance state
    /// and call <see cref="HideTutorialMessage"/>.
    /// </summary>
    void ShowTappableTutorialMessage(string text);

    /// <summary>
    /// Fires when the player taps the tutorial-message panel while it's
    /// in tappable mode (most recent call was <see cref="ShowTappableTutorialMessage"/>
    /// and <see cref="HideTutorialMessage"/> hasn't been called since).
    /// </summary>
    event Action? TutorialMessageTapped;

    /// <summary>
    /// Apply (or clear) CTA styling to a specific HUD button. Called by
    /// <see cref="GameController.RefreshViews"/> with
    /// <see cref="CtaButton.EndTurn"/> + <paramref name="pulse"/> = false
    /// when the human has no actionable territories left (steady glow);
    /// otherwise by Tutorial Preview to highlight the next scripted beat
    /// (pulse = true, animated, distinguishes from the game-side auto-CTA).
    /// All Tutorial buttons except EndTurn always pulse — they're only
    /// fired during tutorial scripting.
    /// </summary>
    void SetCta(CtaButton button, bool isCta, bool pulse = true);

    /// <summary>
    /// Lock (force-disable) the Undo / Redo button row. While locked,
    /// the four undo/redo buttons stay disabled regardless of
    /// <see cref="SessionState.Undo"/> state — Tutorial Preview uses
    /// this because undo/redo would desync the script cursor from the
    /// player's actions (those operations aren't recorded as beats).
    /// </summary>
    void SetUndoRedoLocked(bool locked);

    /// <summary>
    /// Suppress the full-win victory overlay even when
    /// <see cref="SessionState.Winner"/> is set. Tutorial Preview and
    /// Record latch this true — the scripted / recorded flow handles
    /// game-over through the tutorial-message panel ("Tutorial
    /// complete.") instead of a click-blocking modal that would freeze
    /// further author input.
    /// </summary>
    void SetVictoryOverlaySuppressed(bool suppressed);

    /// <summary>
    /// Hold (true) or release (false) the victory AND defeat overlays
    /// even when their session flags are set. The controller latches
    /// this for one settle delay after a game-ending / defeating MOVE so
    /// the overlay doesn't pop while the unit's travel tween is still in
    /// flight; the scheduled reveal releases it and refreshes. Distinct
    /// from <see cref="SetVictoryOverlaySuppressed"/>, which is owned by
    /// the Tutorial Preview/Record flow and latches for a whole session.
    /// </summary>
    void SetEndgameOverlaysHeld(bool held);

    /// <summary>
    /// Coord of the capital whose tap-summoned alert notice is
    /// currently visible, or null when no notice is showing. Read by
    /// the controller's tap handler to implement toggle-off-on-re-tap
    /// of the same capital. View-only state — never reflected in
    /// <see cref="GameState"/> or <see cref="SessionState"/>, so the
    /// notice is not snapshotted into undo entries.
    /// </summary>
    HexCoord? SummonedCapitalAlertCoord { get; }

    /// <summary>
    /// Show the tap-summoned alert notice anchored to
    /// <paramref name="capital"/> with content/palette chosen by
    /// <paramref name="outlook"/> (red for
    /// <see cref="EconomyOutlook.BankruptNextTurn"/>, yellow for
    /// <see cref="EconomyOutlook.NegativeDelta"/>). Replaces any
    /// previously summoned notice. The controller is expected to gate
    /// on human ownership and a non-Healthy outlook before calling.
    /// </summary>
    void SummonCapitalAlertNotice(HexCoord capital, EconomyOutlook outlook);

    /// <summary>
    /// Hide the tap-summoned alert notice if visible. Safe to call
    /// when nothing is summoned. Called by the controller from every
    /// top-level human handler so any non-tap action dismisses.
    /// </summary>
    void DismissCapitalAlertNotice();

    /// <summary>
    /// Push the Automate toggle button's state. Called from the single
    /// RefreshViews path: <paramref name="enabled"/> when it's a human
    /// turn with actions remaining (or automation is running, so Stop
    /// stays reachable); <paramref name="running"/> while the automate
    /// loop is active — the button renders pressed-in with a pause
    /// glyph and clears automatically when automation stops.
    /// <paramref name="visible"/> is false in the tutorial Preview and
    /// Record modes — the button isn't drawn at all there, not merely
    /// disabled.
    /// </summary>
    void SetAutomateState(bool enabled, bool running, bool visible);
}

/// <summary>
/// Identifies which HUD button <see cref="IHudView.SetCta"/> targets.
/// <see cref="EndTurn"/> and <see cref="NextTerritory"/> are driven by the
/// game itself (steady glow during normal play) and may also be triggered
/// by Tutorial Preview (pulsed, scripted beat). The rest are Tutorial
/// Preview only.
/// </summary>
public enum CtaButton
{
    BuyRecruit,
    EndTurn,
    BuildTower,
    ClaimVictoryWinNow,
    ClaimVictoryContinue,
    DefeatContinue,
    NextTerritory,
}
