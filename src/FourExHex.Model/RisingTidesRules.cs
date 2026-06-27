using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Pure rules for the "Rising Tides" game mode (issue #56). Godot-free and
/// integer-only (the one randomness source is an injected <see cref="Random"/>),
/// so it lives in the model assembly and passes the no-floats check.
///
/// The erosion is split into two halves (issue #85): at the start of an owner's
/// turn the controller calls <see cref="ForecastSubmerge"/> — which <i>selects</i>
/// a budget of that owner's shore tiles (consuming the RNG) but mutates nothing —
/// and locks the resulting <see cref="TideStep"/> plan on the game state so it can
/// be telegraphed to the player and weighed by the AI. At the <i>end</i> of that
/// same turn the controller calls <see cref="ApplyForecast"/>, which performs the
/// actual demote/submerge for the forecasted tiles. (<see cref="SubmergeStep"/>
/// keeps the old forecast-then-immediately-apply behaviour in one call — still
/// used for the phantom turns of neutral/eliminated colors, which have no
/// during-turn beat to telegraph.)
///
/// A shore tile is any land tile with at least one missing neighbour (water, the
/// rectangular map edge, or beyond-bounds — none of which are in the grid). A
/// mountain on a shore demotes first (losing its mountain status) and only
/// submerges on a later turn, so mountains are the last land to go. Submerging
/// reuses the same remove-tile-then-add-water + territory-reconcile sequence the
/// map editor uses (<see cref="MapEditPaint.PaintWater"/>).
/// </summary>
public static class RisingTidesRules
{
    /// <summary>
    /// The land tiles owned by <paramref name="owner"/> that are shores — i.e.
    /// have fewer than six in-grid neighbours. Returned in deterministic
    /// ascending <see cref="HexCoord"/> order so a seeded draw over the list is
    /// reproducible.
    /// </summary>
    public static IReadOnlyList<HexCoord> ShoreTilesOf(HexGrid grid, PlayerId owner)
    {
        var shore = new List<HexCoord>();
        foreach (HexTile tile in grid.Tiles)
        {
            if (tile.Owner != owner) continue;
            // A missing neighbour is water, the rectangular map edge, or
            // beyond-bounds — none are in the grid. Fewer than six in-grid
            // neighbours therefore means the tile touches the sea or an edge.
            if (grid.NeighborsOf(tile.Coord).Count() < 6)
            {
                shore.Add(tile.Coord);
            }
        }
        shore.Sort();
        return shore;
    }

    /// <summary>
    /// Forecast (but do NOT apply) up to <paramref name="budget"/> of
    /// <paramref name="owner"/>'s shore tiles to erode this turn, picked with
    /// <paramref name="rng"/>. The grid is left untouched — the returned plan
    /// records each selected coord and whether it is currently a mountain
    /// (<see cref="TideStep.DemoteOnly"/>, a reprieve) or a plain shore that will
    /// submerge. RNG consumption is byte-for-byte identical to <see cref="SubmergeStep"/>
    /// (same <see cref="ShoreTilesOf"/> order, same draws), so deferring the
    /// mutation to <see cref="ApplyForecast"/> does not shift the seeded stream.
    /// </summary>
    public static IReadOnlyList<TideStep> ForecastSubmerge(
        GameState state, PlayerId owner, Random rng, int budget)
    {
        var shore = new List<HexCoord>(ShoreTilesOf(state.Grid, owner));
        if (shore.Count == 0 || budget <= 0) return System.Array.Empty<TideStep>();

        var plan = new List<TideStep>();
        for (int i = 0; i < budget && shore.Count > 0; i++)
        {
            int pick = rng.Next(shore.Count);
            HexCoord coord = shore[pick];
            shore.RemoveAt(pick);

            HexTile? tile = state.Grid.Get(coord);
            if (tile == null) continue; // defensive: already gone
            plan.Add(new TideStep(coord, DemoteOnly: tile.IsMountain));
        }

        if (plan.Count > 0)
        {
            Log.Debug(Log.LogCategory.Tide,
                $"[tide] forecast {owner} {string.Join(",", plan.Select(s => s.Coord))}");
        }
        return plan;
    }

    /// <summary>
    /// Apply a previously forecasted <paramref name="plan"/> for
    /// <paramref name="owner"/>. A tile that is (still) a mountain demotes
    /// (clears <see cref="HexTile.IsMountain"/>) and spends the step without
    /// submerging; a plain shore is removed from the grid and added to the water
    /// set. The demote-vs-submerge decision is re-derived from live tile state so
    /// it matches what <see cref="SubmergeStep"/> would have done, but the COORDS
    /// are exactly those locked at forecast time (no re-pick, no RNG, no drift).
    /// If any tile actually submerged, the territory partition is recomputed
    /// (capitals relocate or the territory is lost, occupants vanish, the treasury
    /// reconciles). Returns true iff anything changed.
    /// </summary>
    public static bool ApplyForecast(
        GameState state, PlayerId owner, IReadOnlyList<TideStep> plan)
    {
        if (plan.Count == 0) return false;

        bool anySubmerged = false;
        bool anyChange = false;
        foreach (TideStep step in plan)
        {
            HexTile? tile = state.Grid.Get(step.Coord);
            if (tile == null) continue; // defensive: already gone

            if (tile.IsMountain)
            {
                // Reprieve: a mountain on a shore demotes first and spends the
                // step; it can submerge on a future turn.
                tile.IsMountain = false;
                anyChange = true;
                Log.Debug(Log.LogCategory.Tide, $"[tide] {owner} demoted mountain {step.Coord}");
            }
            else
            {
                state.Grid.Remove(step.Coord);
                state.AddWater(step.Coord);
                anySubmerged = true;
                anyChange = true;
                Log.Debug(Log.LogCategory.Tide, $"[tide] {owner} submerged {step.Coord}");
            }
        }

        if (anySubmerged)
        {
            // Same reconcile the editor's PaintWater runs: re-partition,
            // relocate/strip orphaned capitals, drop occupants on vanished
            // tiles, and sync the treasury.
            state.Territories = TerritoryFinder.Recompute(
                state.Grid, state.Territories, state.Treasury);
        }

        return anyChange;
    }

    /// <summary>
    /// Forecast and immediately apply one erosion step — the original
    /// single-call behaviour (issue #56). Retained for phantom turns of
    /// neutral/eliminated colors, which have no during-turn beat to telegraph,
    /// and for tests. Returns true iff anything changed.
    /// </summary>
    public static bool SubmergeStep(GameState state, PlayerId owner, Random rng, int budget)
        => ApplyForecast(state, owner, ForecastSubmerge(state, owner, rng, budget));
}
