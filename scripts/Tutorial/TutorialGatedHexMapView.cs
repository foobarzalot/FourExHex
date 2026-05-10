using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// IHexMapView wrapper for tutorial Preview. Subscribes to a "real"
/// IHexMapView; for input events (TileClicked, TileLongClicked) decides
/// whether to forward to GameController based on the next expected
/// scripted beat (via <see cref="TutorialPlayer"/>); for output methods,
/// delegates to the real view unchanged.
///
/// Phase 3c: no tile-action beats exist (Move/BuyPeasant/BuildTower
/// land in Phase 4-6), so any tile click is a rejection. Phase 4+
/// extends the OnRealTileClicked handler to actually try matches.
///
/// Call <see cref="Unbind"/> on Preview teardown to release the
/// subscription to the real view (otherwise the real view holds a
/// reference to this wrapper's handler, preventing garbage collection
/// of the wrapper / TutorialPlayer / GameController graph).
/// </summary>
public sealed class TutorialGatedHexMapView : IHexMapView
{
    private readonly IHexMapView _real;
    private readonly TutorialPlayer _player;

    public TutorialGatedHexMapView(IHexMapView real, TutorialPlayer player)
    {
        _real = real;
        _player = player;
        _real.TileClicked += OnRealTileClicked;
        _real.TileLongClicked += OnRealTileLongClicked;
    }

    public void Unbind()
    {
        _real.TileClicked -= OnRealTileClicked;
        _real.TileLongClicked -= OnRealTileLongClicked;
    }

    public event Action<HexTile?>? TileClicked;
    public event Action<HexTile?>? TileLongClicked;

    private void OnRealTileClicked(HexTile? tile)
    {
        // Phase 3c: no tile-action beats exist. Reject every tile click.
        // Phase 4+ replaces this with a TutorialValidator call against
        // MoveBeat / BuyPeasantBeat / BuildTowerBeat.
        _player.NotifyRejected("tile click");
    }

    private void OnRealTileLongClicked(HexTile? tile)
    {
        // Same gating policy: rejected in 3c.
        _player.NotifyRejected("tile long-press");
    }

    // --- Output methods: pure delegation ---

    public Territory? TerritoryAt(HexCoord coord) => _real.TerritoryAt(coord);
    public void ShowMoveTargets(IEnumerable<HexCoord> coords, UnitLevel level) =>
        _real.ShowMoveTargets(coords, level);
    public void ShowTowerTargets(IEnumerable<HexCoord> coords) =>
        _real.ShowTowerTargets(coords);
    public void ShowTowerCoverage(IEnumerable<HexCoord> coords) =>
        _real.ShowTowerCoverage(coords);
    public void ShowMoveSource(HexCoord? coord) => _real.ShowMoveSource(coord);
    public void ShowHighlight(Territory? selected) => _real.ShowHighlight(selected);
    public void CenterOnTerritory(Territory territory) => _real.CenterOnTerritory(territory);
    public void RebuildAfterTerritoryChange() => _real.RebuildAfterTerritoryChange();
    public void RefreshOccupantVisuals(Color? currentPlayerColor, Treasury treasury) =>
        _real.RefreshOccupantVisuals(currentPlayerColor, treasury);
    public void PlayDestructionEffect(HexCoord coord, HexOccupant destroyed) =>
        _real.PlayDestructionEffect(coord, destroyed);
    public void PlayUnitPlaced(HexCoord coord) => _real.PlayUnitPlaced(coord);
    public void PlayTowerPlaced(HexCoord coord) => _real.PlayTowerPlaced(coord);
    public void PlayUnitCombined(HexCoord coord) => _real.PlayUnitCombined(coord);
    public void PlayUnitDestroyed(HexCoord coord) => _real.PlayUnitDestroyed(coord);
    public void PlayTowerDestroyed(HexCoord coord) => _real.PlayTowerDestroyed(coord);
    public void PlayTreeCleared(HexCoord coord) => _real.PlayTreeCleared(coord);
    public void PlayCapitalDestroyed(HexCoord coord) => _real.PlayCapitalDestroyed(coord);
    public void PlayBankruptcy() => _real.PlayBankruptcy();
    public void PlayGameWon() => _real.PlayGameWon();
    public void PlayRally() => _real.PlayRally();
    public void PlayPlayerDefeated() => _real.PlayPlayerDefeated();
}
