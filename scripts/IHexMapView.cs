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
}
