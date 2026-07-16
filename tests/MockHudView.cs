// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;

namespace FourExHex.Tests;

/// <summary>
/// In-memory <see cref="IHudView"/> for controller tests. Records the
/// last Refresh call and exposes Click* methods that raise each button's
/// event so tests can simulate HUD interaction.
/// </summary>
public class MockHudView : IHudView
{
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
    public event Action? NextUnitTierClicked;
    public event Action? PreviousUnitClicked;
    public event Action? CancelActionPressed;
    public event Action? AutomateClicked;
    public event Action? DefeatContinueClicked;
    public event Action? ClaimVictoryWinNowClicked;
    public event Action? ClaimVictoryContinueClicked;
    public event Action? ReplayClicked;
    public event Action? TutorialMessageTapped;

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
    public PlayerId? LastSeenWinner { get; private set; }

    /// <summary>
    /// Snapshot of <see cref="SessionState.Mode"/> as observed at the
    /// most recent Refresh call. The real HudView caches this value at
    /// refresh time to gate Escape between cancel-action and pause, so
    /// tests need to verify the controller refreshes the HUD AFTER mode
    /// changes (not only before).
    /// </summary>
    public SessionState.ActionMode LastSeenMode { get; private set; }

    public void Refresh(GameState state, SessionState session, bool hasActionableRemaining)
    {
        RefreshCount++;
        LastState = state;
        LastSession = session;
        LastHasActionableRemaining = hasActionableRemaining;
        LastSeenWinner = session.Winner;
        LastSeenMode = session.Mode;
    }

    public string? LastSetMapLabel { get; private set; }
    public void SetMapLabel(string text) => LastSetMapLabel = text;

    /// <summary>Every transient banner the controller showed, in order.</summary>
    public List<string> TransientBanners { get; } = new();
    public void ShowTransientBanner(string text) => TransientBanners.Add(text);

    public string? CurrentTutorialMessage { get; private set; }
    public bool TutorialMessageTappable { get; private set; }
    public int ShowTappableTutorialMessageCount { get; private set; }
    public int HideTutorialMessageCount { get; private set; }
    public void ShowTutorialMessage(string text)
    {
        CurrentTutorialMessage = text;
        TutorialMessageTappable = false;
    }
    public void ShowTappableTutorialMessage(string text)
    {
        CurrentTutorialMessage = text;
        TutorialMessageTappable = true;
        ShowTappableTutorialMessageCount++;
    }
    public void HideTutorialMessage()
    {
        CurrentTutorialMessage = null;
        TutorialMessageTappable = false;
        HideTutorialMessageCount++;
    }
    public void RaiseTutorialMessageTapped() => TutorialMessageTapped?.Invoke();

    // Per-button recording surface — tests assert against these.
    public bool BuyRecruitCtaActive { get; private set; }
    public bool EndTurnCtaActive { get; private set; }
    public bool EndTurnCtaPulse { get; private set; }
    public bool BuildTowerCtaActive { get; private set; }
    public bool ClaimVictoryWinNowCtaActive { get; private set; }
    public bool ClaimVictoryContinueCtaActive { get; private set; }
    public bool DefeatContinueCtaActive { get; private set; }
    public bool NextTerritoryCtaActive { get; private set; }

    public void SetCta(CtaButton button, bool isCta, bool pulse = true)
    {
        switch (button)
        {
            case CtaButton.BuyRecruit: BuyRecruitCtaActive = isCta; break;
            case CtaButton.EndTurn: EndTurnCtaActive = isCta; EndTurnCtaPulse = pulse; break;
            case CtaButton.BuildTower: BuildTowerCtaActive = isCta; break;
            case CtaButton.ClaimVictoryWinNow: ClaimVictoryWinNowCtaActive = isCta; break;
            case CtaButton.ClaimVictoryContinue: ClaimVictoryContinueCtaActive = isCta; break;
            case CtaButton.DefeatContinue: DefeatContinueCtaActive = isCta; break;
            case CtaButton.NextTerritory: NextTerritoryCtaActive = isCta; break;
        }
    }

    public bool UndoRedoLocked { get; private set; }
    public void SetUndoRedoLocked(bool locked) => UndoRedoLocked = locked;

    public bool VictoryOverlaySuppressed { get; private set; }
    public void SetVictoryOverlaySuppressed(bool suppressed) => VictoryOverlaySuppressed = suppressed;

    public HexCoord? SummonedCapitalAlertCoord { get; private set; }
    public EconomyOutlook? LastSummonedAlertOutlook { get; private set; }
    public int SummonAlertCallCount { get; private set; }
    public int DismissAlertCallCount { get; private set; }

    public void SummonCapitalAlertNotice(HexCoord capital, EconomyOutlook outlook)
    {
        SummonedCapitalAlertCoord = capital;
        LastSummonedAlertOutlook = outlook;
        SummonAlertCallCount++;
    }

    public void DismissCapitalAlertNotice()
    {
        SummonedCapitalAlertCoord = null;
        DismissAlertCallCount++;
    }

    public bool ReplayAvailable { get; private set; }
    public void SetReplayAvailable(bool available) => ReplayAvailable = available;

    public bool AutomateEnabled { get; private set; }
    public bool AutomateRunning { get; private set; }
    public bool AutomateVisible { get; private set; } = true;
    public void SetAutomateState(bool enabled, bool running, bool visible)
    {
        AutomateEnabled = enabled;
        AutomateRunning = running;
        AutomateVisible = visible;
    }

    public void ClickReplay() => ReplayClicked?.Invoke();

    public void ClickBuyRecruit() => BuyRecruitClicked?.Invoke();
    public void ClickBuyUnit(UnitLevel level) => BuyUnitClicked?.Invoke(level);
    public void ClickBuildTower() => BuildTowerClicked?.Invoke();
    public void PressNextTerritory() => NextTerritoryClicked?.Invoke();
    public void PressPreviousTerritory() => PreviousTerritoryClicked?.Invoke();
    public void PressNextUnit() => NextUnitClicked?.Invoke();
    public void PressNextUnitTier() => NextUnitTierClicked?.Invoke();
    public void PressPreviousUnit() => PreviousUnitClicked?.Invoke();
    public void ClickUndoLast() => UndoLastClicked?.Invoke();
    public void ClickUndoTurn() => UndoTurnClicked?.Invoke();
    public void ClickRedoLast() => RedoLastClicked?.Invoke();
    public void ClickRedoAll() => RedoAllClicked?.Invoke();
    public void ClickEndTurn() => EndTurnClicked?.Invoke();
    public void ClickNewGame() => NewGameClicked?.Invoke();
    public void ClickMainMenu() => MainMenuClicked?.Invoke();
    public void PressCancelAction() => CancelActionPressed?.Invoke();
    public void ClickAutomate() => AutomateClicked?.Invoke();
    public void ClickDefeatContinue() => DefeatContinueClicked?.Invoke();
    public void ClickClaimVictoryWinNow() => ClaimVictoryWinNowClicked?.Invoke();
    public void ClickClaimVictoryContinue() => ClaimVictoryContinueClicked?.Invoke();
}
