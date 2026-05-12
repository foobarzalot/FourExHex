using System;
using Godot;

namespace FourExHex.Tests;

/// <summary>
/// In-memory <see cref="IHudView"/> for controller tests. Records the
/// last Refresh call and exposes Click* methods that raise each button's
/// event so tests can simulate HUD interaction.
/// </summary>
public class MockHudView : IHudView
{
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
    public event Action? PreviousTerritoryClicked;
    public event Action? NextUnitClicked;
    public event Action? PreviousUnitClicked;
    public event Action? CancelActionPressed;
    public event Action? SaveGameClicked;
    public event Action? DefeatContinueClicked;
    public event Action? ClaimVictoryWinNowClicked;
    public event Action? ClaimVictoryContinueClicked;
    public event Action? ReplayClicked;

    public int RefreshCount { get; private set; }
    public GameState? LastState { get; private set; }
    public SessionState? LastSession { get; private set; }
    public bool LastHasActionableRemaining { get; private set; }

    /// <summary>
    /// Snapshot of <see cref="SessionState.Winner"/> as observed at
    /// the most recent Refresh call. Tracking the value at refresh
    /// time (not the live SessionState reference) lets tests detect
    /// "winner was set but no view refresh followed" bugs — under
    /// the real HudView, the victory overlay is gated on this value
    /// during Refresh, so a missing post-winner refresh is what
    /// makes the on-screen dialog stay hidden even though session
    /// state thinks the game is over.
    /// </summary>
    public Color? LastSeenWinner { get; private set; }

    public void Refresh(GameState state, SessionState session, bool hasActionableRemaining)
    {
        RefreshCount++;
        LastState = state;
        LastSession = session;
        LastHasActionableRemaining = hasActionableRemaining;
        LastSeenWinner = session.Winner;
    }

    public string? LastSetMapLabel { get; private set; }
    public void SetMapLabel(string text) => LastSetMapLabel = text;

    public string? CurrentTutorialMessage { get; private set; }
    public void ShowTutorialMessage(string text) => CurrentTutorialMessage = text;
    public void HideTutorialMessage() => CurrentTutorialMessage = null;

    public bool BuyPeasantCtaActive { get; private set; }
    public void SetBuyPeasantCta(bool isCta) => BuyPeasantCtaActive = isCta;

    public bool ReplayAvailable { get; private set; }
    public void SetReplayAvailable(bool available) => ReplayAvailable = available;

    public void ClickReplay() => ReplayClicked?.Invoke();

    public void ClickBuyPeasant() => BuyPeasantClicked?.Invoke();
    public void ClickBuildTower() => BuildTowerClicked?.Invoke();
    public void PressNextTerritory() => NextTerritoryClicked?.Invoke();
    public void PressPreviousTerritory() => PreviousTerritoryClicked?.Invoke();
    public void PressNextUnit() => NextUnitClicked?.Invoke();
    public void PressPreviousUnit() => PreviousUnitClicked?.Invoke();
    public void ClickUndoLast() => UndoLastClicked?.Invoke();
    public void ClickUndoTurn() => UndoTurnClicked?.Invoke();
    public void ClickRedoLast() => RedoLastClicked?.Invoke();
    public void ClickRedoAll() => RedoAllClicked?.Invoke();
    public void ClickEndTurn() => EndTurnClicked?.Invoke();
    public void ClickNewGame() => NewGameClicked?.Invoke();
    public void ClickMainMenu() => MainMenuClicked?.Invoke();
    public void PressCancelAction() => CancelActionPressed?.Invoke();
    public void ClickSaveGame() => SaveGameClicked?.Invoke();
    public void ClickDefeatContinue() => DefeatContinueClicked?.Invoke();
    public void ClickClaimVictoryWinNow() => ClaimVictoryWinNowClicked?.Invoke();
    public void ClickClaimVictoryContinue() => ClaimVictoryContinueClicked?.Invoke();
}
