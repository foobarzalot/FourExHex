using System;

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

    public int RefreshCount { get; private set; }
    public GameState? LastState { get; private set; }
    public SessionState? LastSession { get; private set; }
    public bool LastHasActionableRemaining { get; private set; }

    public void Refresh(GameState state, SessionState session, bool hasActionableRemaining)
    {
        RefreshCount++;
        LastState = state;
        LastSession = session;
        LastHasActionableRemaining = hasActionableRemaining;
    }

    public void ClickBuyPeasant() => BuyPeasantClicked?.Invoke();
    public void ClickBuildTower() => BuildTowerClicked?.Invoke();
    public void PressNextTerritory() => NextTerritoryClicked?.Invoke();
    public void ClickUndoLast() => UndoLastClicked?.Invoke();
    public void ClickUndoTurn() => UndoTurnClicked?.Invoke();
    public void ClickRedoLast() => RedoLastClicked?.Invoke();
    public void ClickRedoAll() => RedoAllClicked?.Invoke();
    public void ClickEndTurn() => EndTurnClicked?.Invoke();
    public void ClickNewGame() => NewGameClicked?.Invoke();
    public void ClickMainMenu() => MainMenuClicked?.Invoke();
}
