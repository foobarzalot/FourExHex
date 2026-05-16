using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// The contract the game controller uses to talk to the hex map view.
/// Extracted from <see cref="HexMapView"/> so the controller can be
/// unit-tested with a mock view (tests can't construct the real one
/// because it derives from Godot's <c>Node2D</c>).
/// </summary>
public interface IHexMapView
{
    /// <summary>
    /// Raised when the player left-clicks on the map. The argument is
    /// the tile they clicked, or null if outside the grid.
    /// </summary>
    event Action<HexTile?>? TileClicked;

    /// <summary>
    /// Raised when the player long-presses (≥ long-press threshold) on
    /// the map without dragging. Suppresses the normal
    /// <see cref="TileClicked"/> for that gesture so the controller can
    /// treat it as a distinct rally action. Argument is the tile under
    /// the cursor at release, or null if outside the grid.
    /// </summary>
    event Action<HexTile?>? TileLongClicked;

    /// <summary>
    /// Raised on a left-click whose coord falls outside the land grid —
    /// water, the render-only water rim, or far past the map. Carries
    /// the raw <see cref="HexCoord"/> the player clicked so the
    /// controller can anchor rejection feedback there. Fires INSTEAD OF
    /// <see cref="TileClicked"/> for off-grid clicks (TileClicked only
    /// fires for in-grid clicks); the editor's separate CoordClicked
    /// event continues to fire for every click regardless of grid
    /// membership.
    /// </summary>
    event Action<HexCoord>? OffGridClicked;

    /// <summary>
    /// Highlight the given coords as valid move/placement targets for a
    /// would-be unit of <paramref name="level"/>. The view sizes the
    /// preview to match the unit's visual (peasant=1 ring, spearman=2,
    /// knight=3, baron=3+dot) so the player sees what the destination
    /// will hold. Pass an empty sequence to clear; <paramref name="level"/>
    /// is ignored when there's nothing to draw.
    /// </summary>
    void ShowMoveTargets(IEnumerable<HexCoord> coords, UnitLevel level);

    /// <summary>
    /// Highlight the given coords as valid tower-placement targets,
    /// rendered with a tower-shaped preview in the move-target green.
    /// Driven by the controller while in BuildingTower mode. Pass an
    /// empty sequence to clear.
    /// </summary>
    void ShowTowerTargets(IEnumerable<HexCoord> coords);

    /// <summary>
    /// Tint the given coords as already-tower-defended (subtle overlay)
    /// while the player is planning a tower placement. The controller
    /// scopes these to the selected territory only — coverage from
    /// other players' towers is not displayed. Pass an empty sequence
    /// to clear.
    /// </summary>
    void ShowTowerCoverage(IEnumerable<HexCoord> coords);

    /// <summary>
    /// Mark the unit at <paramref name="coord"/> as "picked up" — the
    /// view may animate it (e.g. a scale pulse) to give the player
    /// visual feedback that their click registered. Pass null to
    /// clear the effect.
    /// </summary>
    void ShowMoveSource(HexCoord? coord);

    /// <summary>
    /// Clear every preview overlay at once: move/tower targets, tower
    /// coverage, and the move-source pickup indicator. Default
    /// implementation calls the four single-overlay clears in
    /// sequence — that's the right thing for almost every consumer,
    /// since pending-action lifetime spans all four. Sites that
    /// genuinely want to clear only some overlays should keep doing
    /// it inline.
    /// </summary>
    void ClearAllOverlays()
    {
        ShowMoveTargets(System.Array.Empty<HexCoord>(), UnitLevel.Peasant);
        ShowTowerTargets(System.Array.Empty<HexCoord>());
        ShowTowerCoverage(System.Array.Empty<HexCoord>());
        ShowMoveSource(null);
    }

    /// <summary>
    /// Draw a bright perimeter around the selected territory, or clear
    /// it if null.
    /// </summary>
    void ShowHighlight(Territory? selected);

    /// <summary>
    /// Pan the map view so the territory's capital is centered in the
    /// visible area (clamped to map bounds). Driven by the controller
    /// when the player cycles selection via Tab / Shift+Tab.
    /// </summary>
    void CenterOnTerritory(Territory territory);

    /// <summary>
    /// Rebuild derived view state after a territory-list change
    /// (capture, undo, redo).
    /// </summary>
    void RebuildAfterTerritoryChange();

    /// <summary>
    /// Redraw every occupant visual (units + capitals) with the CTA
    /// coloring rules.
    /// </summary>
    void RefreshOccupantVisuals(Color? currentPlayerColor, Treasury treasury);

    /// <summary>
    /// Suppress (true) or restore (false) per-action AI feedback —
    /// destruction effects, placement/move/combine sounds, and
    /// tree/grave growth tweens. Set by GameController to true while
    /// an AI player runs under the "Instant" AI Speed setting, then
    /// false the moment a human resumes control. Game-state overlays
    /// (victory, defeat, bankruptcy) flow through <see cref="Refresh"/>
    /// and are unaffected — the user still sees those events even
    /// when the AI batch is otherwise silent.
    /// </summary>
    void SetSilentMode(bool silent);

    /// <summary>
    /// Play a one-shot destruction effect at <paramref name="coord"/> for
    /// the displaced occupant. Called by the controller after a
    /// movement-rule-driven capture, tree chop, or grave burial — once
    /// the model has been mutated, before <see cref="RefreshOccupantVisuals"/>
    /// repaints. The view chooses what (if anything) to render based on
    /// the occupant type. Pure visual side effect; not invoked during
    /// undo/redo.
    /// </summary>
    void PlayDestructionEffect(HexCoord coord, HexOccupant destroyed);

    /// <summary>
    /// Play a one-shot sound cue for a game event. The optional
    /// <paramref name="at"/> coord is reserved for a future positional
    /// implementation; the current AudioBus plays through a single
    /// non-spatial 2D player. Silent-mode policy: per-action cues
    /// (placement, combine, destruction, rally, defeat) drop while the
    /// view is in silent mode (AI Instant batch); turn-/game-boundary
    /// cues (<see cref="SoundEffect.Bankruptcy"/>,
    /// <see cref="SoundEffect.GameWon"/>) always play. The view enforces
    /// this — callers don't gate.
    /// </summary>
    void PlaySound(SoundEffect kind, HexCoord? at = null);

    /// <summary>
    /// Visual + audio feedback when a player's place/move/build-tower
    /// click is rejected. The view renders a short red-pulsing ghost
    /// shaped like <paramref name="shape"/> over <paramref name="target"/>,
    /// pulses a matching red ghost over each defender coord, and plays
    /// the defended-rejection sound iff <paramref name="blockingDefenders"/>
    /// is non-empty (else the generic-rejection sound). Pass an empty
    /// defender sequence for non-defense rejections (water, distance,
    /// own-territory occupied, BuildTower invalidity).
    /// </summary>
    void FlashRejection(HexCoord target, RejectionShape shape, IEnumerable<HexCoord> blockingDefenders);
}

/// <summary>
/// One-shot sound cues the controller can ask the view to play. The
/// per-action cues (Unit*, Tower*, Tree*, Capital*, Rally, PlayerDefeated)
/// are gated by the view's silent-mode toggle so the AI Instant batch
/// stays inaudible from the human's perspective. <see cref="Bankruptcy"/>
/// and <see cref="GameWon"/> are turn-/game-boundary events the user
/// asked to still hear under Instant — the view exempts them from the
/// gate.
/// </summary>
public enum SoundEffect
{
    UnitPlaced,
    TowerPlaced,
    UnitCombined,
    UnitDestroyed,
    TowerDestroyed,
    TreeCleared,
    CapitalDestroyed,
    Bankruptcy,
    GameWon,
    Rally,
    PlayerDefeated,
}

/// <summary>
/// Which silhouette the rejection-feedback overlay should draw over the
/// target hex. Maps 1:1 to <see cref="UnitLevel"/> for buy/move rejections,
/// with <see cref="Tower"/> added for BuildTower rejections.
/// </summary>
public enum RejectionShape
{
    Peasant,
    Spearman,
    Knight,
    Baron,
    Tower,
}

public static class RejectionShapeExtensions
{
    public static RejectionShape FromUnitLevel(UnitLevel level) => level switch
    {
        UnitLevel.Peasant => RejectionShape.Peasant,
        UnitLevel.Spearman => RejectionShape.Spearman,
        UnitLevel.Knight => RejectionShape.Knight,
        UnitLevel.Baron => RejectionShape.Baron,
        _ => RejectionShape.Peasant,
    };
}
