using System.Collections.Generic;
using Godot;

/// <summary>
/// Pure-logic helpers for the map editor's paint actions. Mutates a
/// <see cref="HexGrid"/> and water set in place, then re-runs the
/// territory finder + capital reconciler so the model stays consistent
/// across successive edits.
///
/// The previously-returned territory list MUST be threaded back in on
/// each call. Without it, <see cref="CapitalReconciler.Reconcile"/>
/// can't recognize already-placed <see cref="Capital"/> occupants as
/// inherited, so it places a fresh capital somewhere else without
/// clearing the old one — leaving orphan capitals behind.
/// </summary>
public static class MapEditPaint
{
    /// <summary>
    /// Set the tile at <paramref name="coord"/> to <paramref name="color"/>.
    /// Creates a tile (and removes the coord from <paramref name="water"/>)
    /// if it was previously water; recolors an existing tile in place if it
    /// was already land. Out-of-bounds coords and same-color repaints are
    /// no-ops. Returns the up-to-date territory list to be threaded into
    /// the next call.
    /// </summary>
    public static IReadOnlyList<Territory> PaintLand(
        HexGrid grid,
        HashSet<HexCoord> water,
        IReadOnlyList<Territory> previousTerritories,
        int cols,
        int rows,
        HexCoord coord,
        Color color)
    {
        if (!InBounds(coord, cols, rows)) return previousTerritories;

        HexTile? existing = grid.Get(coord);
        if (existing != null)
        {
            if (existing.Color == color) return previousTerritories;
            existing.Color = color;
        }
        else
        {
            grid.Add(new HexTile(coord, color));
            water.Remove(coord);
        }

        return Reconcile(grid, previousTerritories);
    }

    /// <summary>
    /// Toggle a tree on the tile at <paramref name="coord"/>. Empty land
    /// gets a fresh <see cref="Tree"/> occupant; an existing tree is
    /// cleared. No-op on water and on tiles that already hold something
    /// other than a tree (capital, unit, tower, grave) — the tree palette
    /// must not stomp gameplay occupants placed by other paints.
    /// </summary>
    public static IReadOnlyList<Territory> PaintTreeToggle(
        HexGrid grid,
        HashSet<HexCoord> water,
        IReadOnlyList<Territory> previousTerritories,
        int cols,
        int rows,
        HexCoord coord)
    {
        if (!InBounds(coord, cols, rows)) return previousTerritories;
        HexTile? tile = grid.Get(coord);
        if (tile == null) return previousTerritories;

        if (tile.Occupant is Tree)
        {
            tile.Occupant = null;
        }
        else if (tile.Occupant == null)
        {
            tile.Occupant = new Tree();
        }
        else
        {
            return previousTerritories;
        }

        return Reconcile(grid, previousTerritories);
    }

    /// <summary>
    /// Convert the tile at <paramref name="coord"/> back to water. No-op if
    /// the coord is out of bounds or already water. Returns the up-to-date
    /// territory list.
    /// </summary>
    public static IReadOnlyList<Territory> PaintWater(
        HexGrid grid,
        HashSet<HexCoord> water,
        IReadOnlyList<Territory> previousTerritories,
        int cols,
        int rows,
        HexCoord coord)
    {
        if (!InBounds(coord, cols, rows)) return previousTerritories;
        if (!grid.Contains(coord)) return previousTerritories;

        grid.Remove(coord);
        water.Add(coord);

        return Reconcile(grid, previousTerritories);
    }

    private static bool InBounds(HexCoord coord, int cols, int rows)
    {
        (int col, int row) = coord.ToOffset();
        return col >= 0 && col < cols && row >= 0 && row < rows;
    }

    private static IReadOnlyList<Territory> Reconcile(
        HexGrid grid,
        IReadOnlyList<Territory> previousTerritories)
    {
        IReadOnlyList<Territory> raw = TerritoryFinder.FindAll(grid);
        return CapitalReconciler.Reconcile(raw, previousTerritories, grid);
    }
}
