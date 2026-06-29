using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Pure rules for the "Rising Tides" game mode. Godot-free and
/// integer-only, so it lives in the model assembly and passes the no-floats
/// check. Coast erosion is fully deterministic — shore tiles are selected by
/// strict exposure ordering, no RNG involved.
///
/// The erosion is split into two halves: at the start of an owner's
/// turn the controller calls <see cref="ForecastSubmerge"/> — which <i>selects</i>
/// a budget of that owner's most sea-exposed shore tiles but mutates nothing —
/// and locks the resulting <see cref="TideStep"/> plan on the game state so it can
/// be telegraphed to the player and weighed by the AI. At the <i>end</i> of that
/// same turn the controller calls <see cref="ApplyForecast"/>, which performs the
/// actual demote/submerge for the forecasted tiles. (<see cref="SubmergeStep"/>
/// forecasts and applies in one call — used for the phantom turns of
/// neutral/eliminated colors, which have no during-turn beat to telegraph.)
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
    /// How many of <paramref name="coord"/>'s six sides face the sea — i.e.
    /// <c>6 - (in-grid neighbours)</c>. A missing neighbour
    /// is water, the map edge, or beyond-bounds — all of which render as sea — so
    /// this is the tile's coastal exposure. An interior tile scores 0; a shore
    /// tile scores 1..6 (a lone tile is 6). Drives the strict erosion ordering:
    /// the tide always takes the highest-weight shore tile, so
    /// exposed corners/peninsulas erode before flush edges, every time.
    /// </summary>
    public static int WaterBorderWeight(HexGrid grid, HexCoord coord)
        => 6 - grid.NeighborsOf(coord).Count();

    /// <summary>
    /// Forecast (but do NOT apply) up to <paramref name="budget"/> of
    /// <paramref name="owner"/>'s shore tiles to erode this turn, selected by
    /// strict exposure ordering: the most sea-exposed tiles first
    /// (highest <see cref="WaterBorderWeight"/> = fewest in-grid land neighbours),
    /// ties broken by ascending <see cref="HexCoord"/>. No RNG is consumed — the
    /// selection is fully deterministic from the map. The grid is left untouched —
    /// the returned plan records each selected coord and whether it is currently a
    /// mountain (<see cref="TideStep.DemoteOnly"/>, a reprieve) or a plain shore
    /// that will submerge.
    /// </summary>
    public static IReadOnlyList<TideStep> ForecastSubmerge(
        GameState state, PlayerId owner, int budget)
    {
        IReadOnlyList<HexCoord> shore = ShoreTilesOf(state.Grid, owner);
        if (shore.Count == 0 || budget <= 0) return System.Array.Empty<TideStep>();

        // Strict erosion order: take the most sea-exposed tiles
        // first so coastlines crumble at their points before their flush edges.
        // `shore` is already in ascending HexCoord order, so OrderByDescending's
        // stable sort resolves equal-exposure ties to the smallest coord; the
        // explicit ThenBy makes that tie-break intent unmistakable.
        var plan = shore
            .OrderByDescending(c => WaterBorderWeight(state.Grid, c))
            .ThenBy(c => c)
            .Take(budget)
            .Select(c => new TideStep(c, DemoteOnly: state.Grid.Get(c)!.IsMountain))
            .ToList();

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
    /// it matches <see cref="SubmergeStep"/>'s demote/submerge choice, but the
    /// COORDS are exactly those locked at forecast time (no re-pick, no RNG, no drift).
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
    /// Forecast and apply one erosion step in a single call. Used for phantom
    /// turns of neutral/eliminated colors, which have no during-turn beat to
    /// telegraph, and for tests. Returns true iff anything changed.
    /// </summary>
    public static bool SubmergeStep(GameState state, PlayerId owner, int budget)
        => ApplyForecast(state, owner, ForecastSubmerge(state, owner, budget));
}
