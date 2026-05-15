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
    public event Action<HexTile?>? TileLongClicked;
    public event Action<HexCoord>? OffGridClicked;

    // Index wired up by the test fixture so TerritoryAt returns the right
    // territory for each coord — mirrors what the real HexMapView caches
    // after flood-fill.
    public Dictionary<HexCoord, Territory> TileIndex { get; } = new();

    public Territory? TerritoryAt(HexCoord coord) =>
        TileIndex.TryGetValue(coord, out Territory? t) ? t : null;

    public List<HexCoord> LastMoveTargets { get; private set; } = new();
    /// <summary>The <see cref="UnitLevel"/> the controller most recently
    /// passed to <see cref="ShowMoveTargets"/>, or null if it has never
    /// been called. Used to verify the destination preview is sized for
    /// the source unit's level (e.g., a Spearman's preview should render
    /// two rings, not one).</summary>
    public UnitLevel? LastMoveTargetsLevel { get; private set; }
    /// <summary>Test hook: invoked-and-cleared at the top of the next
    /// <see cref="ShowMoveTargets"/> call. Used to simulate a mid-handler
    /// failure and verify the controller doesn't push a recovery snapshot.</summary>
    public Action? ThrowOnNextShowMoveTargets { get; set; }
    public void ShowMoveTargets(IEnumerable<HexCoord> coords, UnitLevel level)
    {
        Action? hook = ThrowOnNextShowMoveTargets;
        ThrowOnNextShowMoveTargets = null;
        hook?.Invoke();
        LastMoveTargets = coords.ToList();
        LastMoveTargetsLevel = level;
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

    public List<HexCoord> UnitPlacedSounds { get; } = new();
    public void PlayUnitPlaced(HexCoord coord) => UnitPlacedSounds.Add(coord);

    public List<HexCoord> TowerPlacedSounds { get; } = new();
    public void PlayTowerPlaced(HexCoord coord) => TowerPlacedSounds.Add(coord);

    public List<HexCoord> UnitCombinedSounds { get; } = new();
    public void PlayUnitCombined(HexCoord coord) => UnitCombinedSounds.Add(coord);

    public List<HexCoord> UnitDestroyedSounds { get; } = new();
    public void PlayUnitDestroyed(HexCoord coord) => UnitDestroyedSounds.Add(coord);

    public List<HexCoord> TowerDestroyedSounds { get; } = new();
    public void PlayTowerDestroyed(HexCoord coord) => TowerDestroyedSounds.Add(coord);

    public List<HexCoord> TreeClearedSounds { get; } = new();
    public void PlayTreeCleared(HexCoord coord) => TreeClearedSounds.Add(coord);

    public List<HexCoord> CapitalDestroyedSounds { get; } = new();
    public void PlayCapitalDestroyed(HexCoord coord) => CapitalDestroyedSounds.Add(coord);

    public int BankruptcySoundCount { get; private set; }
    public void PlayBankruptcy() => BankruptcySoundCount++;

    public int GameWonSoundCount { get; private set; }
    public void PlayGameWon() => GameWonSoundCount++;

    public int RallySoundCount { get; private set; }
    public void PlayRally() => RallySoundCount++;

    public int PlayerDefeatedSoundCount { get; private set; }
    public void PlayPlayerDefeated() => PlayerDefeatedSoundCount++;

    /// <summary>
    /// Records every rejection feedback event the controller raised. Each
    /// entry holds the target hex, the shape the player was trying to
    /// place, and the coords of defenders (empty for non-defense rejections).
    /// Tests assert against the last entry to verify the controller routed
    /// the right shape + defender set for each rejection site.
    /// </summary>
    public List<(HexCoord Target, RejectionShape Shape, HexCoord[] Defenders)> Rejections { get; } = new();
    public (HexCoord Target, RejectionShape Shape, HexCoord[] Defenders)? LastRejection =>
        Rejections.Count == 0 ? null : Rejections[Rejections.Count - 1];
    public void FlashRejection(HexCoord target, RejectionShape shape, IEnumerable<HexCoord> blockingDefenders) =>
        Rejections.Add((target, shape, blockingDefenders.ToArray()));

    /// <summary>Raise the TileClicked event, as if the user clicked.</summary>
    public void SimulateClick(HexTile? tile) => TileClicked?.Invoke(tile);

    /// <summary>Raise the TileLongClicked event, as if the user long-pressed.</summary>
    public void SimulateLongClick(HexTile? tile) => TileLongClicked?.Invoke(tile);

    /// <summary>Raise the OffGridClicked event, as if the user clicked a water or
    /// off-grid coord.</summary>
    public void SimulateOffGridClick(HexCoord coord) => OffGridClicked?.Invoke(coord);
}
