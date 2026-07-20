// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;

/// <summary>
/// Pure-C# setup helper for Tutorial Preview. Resets the live
/// <see cref="GameState"/> back to the tutorial's recorded initial
/// snapshot AND walks the view back to a clean state — territory
/// borders, capital nodes, highlight rings, move-target overlays.
///
/// <para>
/// Extracted from <see cref="PreviewPane"/> so the visual-reset
/// invariant is testable from xUnit. PreviewPane itself is
/// test-excluded (Godot Control); this helper takes the
/// <see cref="IHexMapView"/> interface and is reachable from
/// <see cref="MockHexMapView"/>-driven tests.
/// </para>
///
/// <para>
/// Why every step matters:
///   • ApplyTo restores tile colors + occupants via per-tile setters
///     (tile colors auto-push to polygons; occupants don't, but
///     RebuildAfterTerritoryChange takes care of them).
///   • Territories assignment + turn-state reset gets the model
///     back to game-start.
///   • RebuildAfterTerritoryChange rebuilds the borders / capital
///     / tree / grave layers — these inherit the post-recording
///     partition if we skip the call.
///   • ShowHighlight(null) + the ShowMoveTargets / TowerTargets /
///     TowerCoverage / MoveSource clears drop any leftover overlays
///     from the prior session.
/// </para>
/// </summary>
public static class PreviewSetup
{
    public static void Apply(IHexMapView map, GameState state, Tutorial tutorial) =>
        Apply(map, state, tutorial.Replay);

    public static void Apply(IHexMapView map, GameState state, Replay replay)
    {
        state.Territories = replay.InitialSnapshot.ApplyTo(state.Grid, state.Treasury);
        state.Turns.Reset(replay.InitialCurrentPlayerIndex, replay.InitialTurnNumber);
        map.RebuildAfterTerritoryChange();
        map.ShowHighlight(null);
        map.ShowMoveTargets(Array.Empty<HexCoord>(), UnitLevel.Recruit);
        map.ShowTowerTargets(Array.Empty<HexCoord>());
        map.ShowTowerCoverage(Array.Empty<HexCoord>());
        map.ShowMoveSource(null);
    }
}
