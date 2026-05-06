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

    /// <summary>Look up the territory containing a coord.</summary>
    Territory? TerritoryAt(HexCoord coord);

    /// <summary>
    /// Highlight the given coords as valid move/placement targets.
    /// Pass an empty sequence to clear.
    /// </summary>
    void ShowMoveTargets(IEnumerable<HexCoord> coords);

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
}
