using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// No-op <see cref="IHexMapView"/> / <see cref="IHudView"/>
/// implementations used by the <c>FOUREXHEX_6AI</c> diagnostic
/// launch path. Real view objects derive from <see cref="Node2D"/>
/// / <see cref="CanvasLayer"/> and run a lot of layout / rendering
/// work on every refresh even in headless mode. The diagnostic run
/// doesn't care about any of that — it just wants the game loop to
/// hit <see cref="GameController.GameEnded"/> as fast as possible
/// — so we bypass the view layer entirely with these stubs.
/// </summary>
public sealed class HeadlessHexMapView : IHexMapView
{
    // Declared to satisfy the interface; diagnostic mode never has
    // a human clicking anything.
#pragma warning disable CS0067 // Event never used
    public event Action<HexTile?>? TileClicked;
    public event Action<HexTile?>? TileLongClicked;
    public event Action<HexCoord>? OffGridClicked;
#pragma warning restore CS0067

    public void ShowMoveTargets(IEnumerable<HexCoord> coords, UnitLevel level) { }
    public void ShowTowerTargets(IEnumerable<HexCoord> coords) { }
    public void ShowTowerCoverage(IEnumerable<HexCoord> coords) { }
    public void ShowMoveSource(HexCoord? coord) { }
    public void ShowHighlight(Territory? selected) { }
    public void CenterOnTerritory(Territory territory) { }
    public void RebuildAfterTerritoryChange() { }
    public void RefreshOccupantVisuals(PlayerId? currentPlayer, Treasury treasury) { }
    public void SetSilentMode(bool silent) { }
    public void PlayDestructionEffect(HexCoord coord, HexOccupant destroyed) { }
    public void PlaySound(SoundEffect kind, HexCoord? at = null) { }
    public void FlashRejection(HexCoord target, RejectionShape shape, IEnumerable<HexCoord> blockingDefenders) { }
}

public sealed class HeadlessHudView : IHudView
{
#pragma warning disable CS0067 // Events never used
    public event Action? BuyPeasantClicked;
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
    public event Action? PreviousUnitClicked;
    public event Action? CancelActionPressed;
    public event Action? DefeatContinueClicked;
    public event Action? ClaimVictoryWinNowClicked;
    public event Action? ClaimVictoryContinueClicked;
    public event Action? ReplayClicked;
    public event Action? TutorialMessageTapped;
#pragma warning restore CS0067

    public void Refresh(GameState state, SessionState session, bool hasActionableRemaining) { }
    public void SetMapLabel(string text) { }
    public void ShowTutorialMessage(string text) { }
    public void ShowTappableTutorialMessage(string text) { }
    public void HideTutorialMessage() { }
    public void SetCta(CtaButton button, bool isCta, bool pulse = true) { }
    public void SetReplayAvailable(bool available) { }
    public void SetUndoRedoLocked(bool locked) { }
    public void SetVictoryOverlaySuppressed(bool suppressed) { }
}
