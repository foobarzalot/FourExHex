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

    /// <summary>Look up the territory containing a coord.</summary>
    Territory? TerritoryAt(HexCoord coord);

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
    /// Play the unit-placed/moved sound for the unit now at
    /// <paramref name="coord"/>. Called by the controller after a
    /// successful move or buy-and-place that consumes the unit's
    /// move action — i.e., captures, tree/grave clearing, and any
    /// purchase. NOT called for free repositions onto own-empty
    /// tiles (which leave the unit actionable). Coord is supplied
    /// in case the view chooses to attach a positional cue later.
    /// </summary>
    void PlayUnitPlaced(HexCoord coord);

    /// <summary>
    /// Play the tower-placed sound (stone-on-stone) for a tower
    /// just built at <paramref name="coord"/>. Distinct from
    /// <see cref="PlayUnitPlaced"/> so the player can audibly
    /// distinguish a tower placement from a unit move.
    /// </summary>
    void PlayTowerPlaced(HexCoord coord);

    /// <summary>
    /// Play the unit-combined "level-up" chime when an arriving unit
    /// merges with a same-color unit at <paramref name="coord"/>,
    /// producing a higher-level unit. Replaces (does NOT layer with)
    /// <see cref="PlayUnitPlaced"/> for that action — the controller
    /// chooses one based on whether the destination held a friendly
    /// unit before the move.
    /// </summary>
    void PlayUnitCombined(HexCoord coord);

    /// <summary>
    /// Play the "smoosh" sound when an enemy unit is crushed at
    /// <paramref name="coord"/>. Replaces <see cref="PlayUnitPlaced"/>
    /// for the action — the controller chooses one based on what
    /// (if anything) was destroyed.
    /// </summary>
    void PlayUnitDestroyed(HexCoord coord);

    /// <summary>
    /// Play the "bursting stone" sound when an enemy tower is
    /// captured/destroyed at <paramref name="coord"/>. Replaces
    /// <see cref="PlayUnitPlaced"/> for the action.
    /// </summary>
    void PlayTowerDestroyed(HexCoord coord);

    /// <summary>
    /// Play the "chop" sound when a tree (cleared) or grave (buried)
    /// is removed from <paramref name="coord"/>. Replaces
    /// <see cref="PlayUnitPlaced"/> for the action — both events
    /// share the same audio because the audible character is similar
    /// (a single sharp impact), even though the visual is different.
    /// </summary>
    void PlayTreeCleared(HexCoord coord);

    /// <summary>
    /// Play the dramatic capital-falling sound when an enemy capital
    /// is destroyed at <paramref name="coord"/>. The heaviest of the
    /// destruction sounds — capital loss permanently fragments the
    /// defender's territory, so the audio marks a strategic milestone.
    /// </summary>
    void PlayCapitalDestroyed(HexCoord coord);

    /// <summary>
    /// Play the bankruptcy bell once per player turn-start, when at
    /// least one of the player's territories failed to pay upkeep and
    /// converted its units to graves. No coord arg — the event is
    /// player-scoped, not tile-scoped, and the sound fires exactly
    /// once regardless of how many territories went bankrupt.
    /// </summary>
    void PlayBankruptcy();

    /// <summary>
    /// Play the game-won fanfare (joyful bell peal) when a human
    /// player wins. AI wins stay silent — that maps to a future
    /// "game lost" cue from the human's perspective.
    /// </summary>
    void PlayGameWon();

    /// <summary>
    /// Play the rally whoosh once per long-press rally that actually
    /// moved at least one unit. A single cue per gesture, not per unit
    /// — multiple units rallying read as one swept gesture.
    /// </summary>
    void PlayRally();

    /// <summary>
    /// Play the defeat bong when a capture eliminates a player —
    /// i.e. the captured tile was their last capital and no other
    /// capital-bearing territory of theirs survived the reconcile.
    /// Fires once per eliminated player, after CapitalReconciler runs.
    /// </summary>
    void PlayPlayerDefeated();

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
