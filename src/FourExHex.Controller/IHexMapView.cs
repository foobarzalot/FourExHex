using System;
using System.Collections.Generic;

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
    /// preview to match the unit's visual (recruit=1 ring, soldier=2,
    /// captain=3, commander=3+dot) so the player sees what the destination
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
    /// Rising Tides: telegraph the given steps as tiles that will
    /// erode at the END of the current player's turn — a cue shown for the whole
    /// turn so the player (and the AI) can react. A submerging step
    /// (<see cref="TideStep.DemoteOnly"/> false) cross-fades the tile between its
    /// land look (before) and the water look (after); a demote-only step (a shore
    /// mountain) shows the milder erosion cue without the water reveal. The
    /// controller passes the locked <see cref="GameState.PendingTide"/> forecast
    /// each refresh; pass an empty sequence to clear (outside Rising Tides, or
    /// after the tiles erode).
    /// </summary>
    void ShowTideForecast(IEnumerable<TideStep> steps);

    /// <summary>
    /// Fog Of War: render the board from the single human player's perspective
    /// per the given projection — tiles in <see cref="FogView.Visible"/> render
    /// live; seen-but-not-visible (stale) tiles render their static terrain
    /// greyed + dimmed, with no owner colour and no occupant; never-seen tiles
    /// render nothing. The controller pushes the current projection each refresh;
    /// pass <c>null</c> (outside Fog Of War) to render everything normally.
    /// </summary>
    void ShowFog(FogView? fog);

    /// <summary>
    /// Mark the unit at <paramref name="coord"/> as "picked up" — the
    /// view may animate it (e.g. a scale pulse) to give the player
    /// visual feedback that their click registered. Pass null to
    /// clear the effect.
    /// </summary>
    void ShowMoveSource(HexCoord? coord);

    /// <summary>
    /// Mark the unit at <paramref name="coord"/> as "tap this unit to
    /// pick it up" — a flashing CTA-style highlight on the unit's own
    /// tile (white box / black border, pulsing alpha), mirroring the
    /// HUD's tutorial-CTA button flash. Deliberately distinct from the
    /// green <see cref="ShowMoveTargets"/> rings, which mean "move TO
    /// here." Used only by Tutorial Preview's "select this unit" cue;
    /// pass null to clear.
    /// </summary>
    void ShowSelectUnitCue(HexCoord? coord);

    /// <summary>
    /// Clear every preview overlay at once: move/tower targets, tower
    /// coverage, the move-source pickup indicator, and the select-unit
    /// cue. Default implementation calls the single-overlay clears in
    /// sequence — that's the right thing for almost every consumer,
    /// since pending-action lifetime spans all of them. Sites that
    /// genuinely want to clear only some overlays should keep doing
    /// it inline.
    /// </summary>
    void ClearAllOverlays()
    {
        ShowMoveTargets(System.Array.Empty<HexCoord>(), UnitLevel.Recruit);
        ShowTowerTargets(System.Array.Empty<HexCoord>());
        ShowTowerCoverage(System.Array.Empty<HexCoord>());
        ShowMoveSource(null);
        ShowSelectUnitCue(null);
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
    /// Pan the map view so <paramref name="coord"/> is centered in the visible
    /// area (clamped to map bounds), using the same eased motion as
    /// <see cref="CenterOnTerritory"/>. Used to focus attention on a specific
    /// tile — e.g. the first-encounter terrain intros drawing the eye to a
    /// gold / mountain hex.
    /// </summary>
    void CenterOnCoord(HexCoord coord);

    /// <summary>
    /// Show (or, with <paramref name="coord"/> null, clear) a pulsing highlight
    /// on a single tile to draw the eye — used by the first-encounter terrain
    /// intros to mark the gold / mountain hex the hint is teaching. Cleared when
    /// the player dismisses the hint.
    /// </summary>
    void ShowTerrainFocusPulse(HexCoord? coord);

    /// <summary>
    /// Rebuild derived view state after a territory-list change
    /// (capture, undo, redo).
    /// </summary>
    void RebuildAfterTerritoryChange();

    /// <summary>
    /// Redraw every occupant visual (units + capitals) with the CTA
    /// coloring rules.
    /// </summary>
    void RefreshOccupantVisuals(PlayerId? currentPlayer, Treasury treasury);

    /// <summary>
    /// Suppress (true) or restore (false) AI/replay fast-forward
    /// feedback — destruction effects, every <see cref="PlaySound"/>
    /// cue (including Bankruptcy/GameWon), and tree/grave growth tweens.
    /// Set by GameController to true while an AI player runs under the
    /// "Instant" AI Speed setting (cleared the moment a human resumes
    /// control) or for the whole of an instant-speed replay. A human
    /// still hears their own bankruptcy / game-won because a human's
    /// own turn is never silent. Game-over *visual* overlays flow
    /// through <see cref="Refresh"/>, not this gate, so they always
    /// render.
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
    /// non-spatial 2D player. Silent-mode policy: ALL cues drop while
    /// the view is in silent mode (AI Instant batch or instant replay)
    /// — no exceptions. A human still hears Bankruptcy/GameWon for
    /// their own turn because a human's own turn is never silent. The
    /// view enforces this — callers don't gate.
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
/// One-shot sound cues the controller can ask the view to play. Every
/// cue (including <see cref="Bankruptcy"/> and <see cref="GameWon"/>) is
/// gated by the view's silent-mode toggle so a silent AI-Instant batch
/// or an instant replay is a fully silent fast-forward. A human still
/// hears Bankruptcy/GameWon on their own turn because a human's own
/// turn is never silent.
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
    TileSubmerged,
}

/// <summary>
/// Which silhouette the rejection-feedback overlay should draw over the
/// target hex. Maps 1:1 to <see cref="UnitLevel"/> for buy/move rejections,
/// with <see cref="Tower"/> added for BuildTower rejections.
/// </summary>
public enum RejectionShape
{
    Recruit,
    Soldier,
    Captain,
    Commander,
    Tower,
}

public static class RejectionShapeExtensions
{
    public static RejectionShape FromUnitLevel(UnitLevel level) => level switch
    {
        UnitLevel.Recruit => RejectionShape.Recruit,
        UnitLevel.Soldier => RejectionShape.Soldier,
        UnitLevel.Captain => RejectionShape.Captain,
        UnitLevel.Commander => RejectionShape.Commander,
        _ => RejectionShape.Recruit,
    };
}
