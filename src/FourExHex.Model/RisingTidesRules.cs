using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Pure rules for the "Rising Tides" game mode (issue #56). Godot-free and
/// integer-only (the one randomness source is an injected <see cref="Random"/>),
/// so it lives in the model assembly and passes the no-floats check.
///
/// At the start of each owner's turn the controller calls
/// <see cref="SubmergeStep"/>, which erodes a budget of that owner's own
/// shore tiles. A shore tile is any land tile with at least one missing
/// neighbour (water, the rectangular map edge, or beyond-bounds — none of
/// which are in the grid). A mountain on a shore demotes first (losing its
/// mountain status) and only submerges on a later turn, so mountains are the
/// last land to go. Submerging reuses the same remove-tile-then-add-water +
/// territory-reconcile sequence the map editor uses
/// (<see cref="MapEditPaint.PaintWater"/>).
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
    /// Erode up to <paramref name="budget"/> of <paramref name="owner"/>'s shore
    /// tiles, picked with <paramref name="rng"/>. A mountain shore demotes
    /// (clears <see cref="HexTile.IsMountain"/>) and spends a budget unit without
    /// submerging; a non-mountain shore is removed from the grid and added to the
    /// water set. If any tile actually submerged, the territory partition is
    /// recomputed (capitals relocate or the territory is lost, occupants vanish,
    /// the treasury reconciles). Returns true iff anything changed.
    /// </summary>
    public static bool SubmergeStep(GameState state, PlayerId owner, Random rng, int budget)
    {
        var shore = new List<HexCoord>(ShoreTilesOf(state.Grid, owner));
        if (shore.Count == 0 || budget <= 0) return false;

        bool anySubmerged = false;
        bool anyChange = false;
        for (int i = 0; i < budget && shore.Count > 0; i++)
        {
            int pick = rng.Next(shore.Count);
            HexCoord coord = shore[pick];
            shore.RemoveAt(pick);

            HexTile? tile = state.Grid.Get(coord);
            if (tile == null) continue; // defensive: already gone

            if (tile.IsMountain)
            {
                // Reprieve: a mountain on a shore demotes first and spends the
                // budget unit; it can submerge on a future turn.
                tile.IsMountain = false;
                anyChange = true;
                Log.Debug(Log.LogCategory.Tide, $"[tide] {owner} demoted mountain {coord}");
            }
            else
            {
                state.Grid.Remove(coord);
                state.AddWater(coord);
                anySubmerged = true;
                anyChange = true;
                Log.Debug(Log.LogCategory.Tide, $"[tide] {owner} submerged {coord}");
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
}
