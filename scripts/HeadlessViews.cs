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
#pragma warning restore CS0067

    private readonly GameState _state;

    public HeadlessHexMapView(GameState state)
    {
        _state = state;
    }

    public Territory? TerritoryAt(HexCoord coord)
    {
        // Controller rarely calls this during autonomous AI play.
        // A linear scan is fine for diagnostic speed.
        foreach (Territory t in _state.Territories)
        {
            if (t.Coords.Contains(coord)) return t;
        }
        return null;
    }

    public void ShowMoveTargets(IEnumerable<HexCoord> coords) { }
    public void ShowMoveSource(HexCoord? coord) { }
    public void ShowHighlight(Territory? selected) { }
    public void RebuildAfterTerritoryChange() { }
    public void RefreshOccupantVisuals(Color? currentPlayerColor, Treasury treasury) { }
}

public sealed class HeadlessHudView : IHudView
{
#pragma warning disable CS0067 // Events never used
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
    public event Action? CancelActionPressed;
#pragma warning restore CS0067

    public void Refresh(GameState state, SessionState session, bool hasActionableRemaining) { }
}
