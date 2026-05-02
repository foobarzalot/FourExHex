using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace FourExHex.Tests;

/// <summary>
/// In-memory <see cref="IHexMapView"/> for controller tests. Records the
/// last value passed to each method so tests can assert what the
/// controller told the view to do, and exposes a <c>SimulateClick</c>
/// helper to raise the <c>TileClicked</c> event.
/// </summary>
public class MockHexMapView : IHexMapView
{
    public event Action<HexTile?>? TileClicked;

    // Index wired up by the test fixture so TerritoryAt returns the right
    // territory for each coord — mirrors what the real HexMapView caches
    // after flood-fill.
    public Dictionary<HexCoord, Territory> TileIndex { get; } = new();

    public Territory? TerritoryAt(HexCoord coord) =>
        TileIndex.TryGetValue(coord, out Territory? t) ? t : null;

    public List<HexCoord> LastMoveTargets { get; private set; } = new();
    /// <summary>Test hook: invoked-and-cleared at the top of the next
    /// <see cref="ShowMoveTargets"/> call. Used to simulate a mid-handler
    /// failure and verify the controller doesn't push a recovery snapshot.</summary>
    public Action? ThrowOnNextShowMoveTargets { get; set; }
    public void ShowMoveTargets(IEnumerable<HexCoord> coords)
    {
        Action? hook = ThrowOnNextShowMoveTargets;
        ThrowOnNextShowMoveTargets = null;
        hook?.Invoke();
        LastMoveTargets = coords.ToList();
    }

    public List<HexCoord> LastTowerTargets { get; private set; } = new();
    public void ShowTowerTargets(IEnumerable<HexCoord> coords) =>
        LastTowerTargets = coords.ToList();

    public List<HexCoord> LastTowerCoverage { get; private set; } = new();
    public void ShowTowerCoverage(IEnumerable<HexCoord> coords) =>
        LastTowerCoverage = coords.ToList();

    public HexCoord? LastMoveSource { get; private set; }
    public void ShowMoveSource(HexCoord? coord) => LastMoveSource = coord;

    public Territory? LastHighlight { get; private set; }
    public bool HighlightWasCleared { get; private set; }
    public void ShowHighlight(Territory? selected)
    {
        LastHighlight = selected;
        HighlightWasCleared = selected == null;
    }

    public Territory? LastCenteredTerritory { get; private set; }
    public int CenterCount { get; private set; }
    public void CenterOnTerritory(Territory territory)
    {
        LastCenteredTerritory = territory;
        CenterCount++;
    }

    public int RebuildCount { get; private set; }
    public void RebuildAfterTerritoryChange() => RebuildCount++;

    public int RefreshOccupantCount { get; private set; }
    public Color? LastOccupantRefreshPlayer { get; private set; }
    public void RefreshOccupantVisuals(Color? currentPlayerColor, Treasury treasury)
    {
        RefreshOccupantCount++;
        LastOccupantRefreshPlayer = currentPlayerColor;
    }

    public List<(HexCoord Coord, HexOccupant Destroyed)> DestructionEffects { get; } = new();
    public void PlayDestructionEffect(HexCoord coord, HexOccupant destroyed) =>
        DestructionEffects.Add((coord, destroyed));

    /// <summary>Raise the TileClicked event, as if the user clicked.</summary>
    public void SimulateClick(HexTile? tile) => TileClicked?.Invoke(tile);
}
