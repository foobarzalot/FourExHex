using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// IHexMapView wrapper for tutorial Preview. Subscribes to a "real"
/// IHexMapView; for input events (TileClicked, TileLongClicked)
/// decides whether the click matches the next expected scripted beat
/// (via <see cref="TutorialPlayer"/>) and forwards to the controller
/// accordingly. Output methods delegate to the real view unchanged.
///
/// Phase 4: when <see cref="TutorialPlayer.IsArmedForBuyPeasant"/> is
/// true, tile clicks route through
/// <see cref="TutorialPlayer.TryAdvanceForBuyPeasantTile"/> — match
/// advances the beat + forwards; mismatch rejects + doesn't forward
/// (controller stays in BuyingPeasant mode for retry). All other
/// tile clicks pass through as passive selection. Phase 5 adds the
/// MoveBeat Src/Dst two-click sequence on top of this.
///
/// Long-press (rally) stays a passive forward in Phase 4 (no rally
/// beat exists; rally is dev affordance during Preview).
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
        // Phase 4: if the player is armed for BuyPeasant (HUD wrapper
        // forwarded the Buy Peasant click), this tile click is the
        // follow-up that completes the BuyPeasantBeat. Validate the
        // coord; on match, advance + forward (controller fires
        // ExecuteBuyAndPlace); on miss, reject + don't forward
        // (controller stays in BuyingPeasant mode for the dev to retry).
        if (_player.IsArmedForBuyPeasant && tile != null)
        {
            if (_player.TryAdvanceForBuyPeasantTile(tile.Coord))
            {
                TileClicked?.Invoke(tile);
            }
            // else: rejected; PlayerActionRejected already fired with reason.
            return;
        }

        // Phase 4: not armed for any tile-action beat (and BuyPeasant
        // is the only one that exists). Forward as passive selection.
        // Phase 5 (Move) extends this with a Src/Dst two-click sequence.
        TileClicked?.Invoke(tile);
    }

    private void OnRealTileLongClicked(HexTile? tile)
    {
        // Same passive-forward policy as TileClicked. Long-press is
        // the rally gesture; in 3c it's a no-op selection-style
        // gesture (no rally beat yet).
        TileLongClicked?.Invoke(tile);
    }

    // --- Output methods: pure delegation ---

    public Territory? TerritoryAt(HexCoord coord) => _real.TerritoryAt(coord);
    public void ShowMoveTargets(IEnumerable<HexCoord> coords, UnitLevel level)
    {
        // When armed for BuyPeasant, the controller's coord set is
        // <see cref="GameController.ActionConsumingTargets"/> — only
        // captures + tree-clears, not friendly empty placements. The
        // scripted <c>At</c> may be a perfectly legal friendly empty
        // tile that just isn't action-consuming, in which case the
        // controller would draw nothing and the dev wouldn't see where
        // to click. Force-show the scripted At as the single ring so
        // the cue is unambiguous and visible regardless of capture
        // status. The actual click is gated by
        // <see cref="GameController.IsValidTarget"/>; if the dev
        // authored an illegal At they'll discover it on the buy
        // attempt (Phase 12 surfaces it as a validation warning).
        if (_player.ArmedBeat is BuyPeasantBeat bpb)
        {
            _real.ShowMoveTargets(new[] { bpb.At }, level);
            return;
        }
        _real.ShowMoveTargets(coords, level);
    }
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
