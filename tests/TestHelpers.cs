using System.Collections.Generic;

namespace FourExHex.Tests;

/// <summary>
/// Shared helpers for building grids, territories, and fully-reconciled
/// game state in tests. Keeps test fixtures DRY so grid-shape changes in
/// one place instead of five.
/// </summary>
public static class TestHelpers
{
    /// <summary>
    /// Build a grid consisting of exactly the given coords, each tile
    /// owned by <paramref name="owner"/>. Used for hand-crafted topologies
    /// (e.g., testing a specific adjacency or a split/merge).
    /// </summary>
    public static HexGrid BuildSpotGrid(PlayerId owner, params HexCoord[] coords)
    {
        var grid = new HexGrid();
        foreach (HexCoord c in coords)
        {
            grid.Add(new HexTile(c, owner));
        }
        return grid;
    }

    /// <summary>
    /// Build a rectangular odd-r offset grid of size <paramref name="cols"/>
    /// x <paramref name="rows"/>, every tile owned by <paramref name="owner"/>.
    /// </summary>
    public static HexGrid BuildRectGrid(int cols, int rows, PlayerId owner)
    {
        var grid = new HexGrid();
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                grid.Add(new HexTile(HexCoord.FromOffset(c, r), owner));
            }
        }
        return grid;
    }

    /// <summary>
    /// Run <see cref="TerritoryFinder.FindAll"/> followed by
    /// <see cref="CapitalReconciler.Reconcile"/> against no prior
    /// territories, producing a territory list with capitals placed.
    /// Mutates <paramref name="grid"/> by adding Capital occupants.
    /// </summary>
    public static IReadOnlyList<Territory> BuildTerritoriesFromGrid(HexGrid grid)
    {
        var raw = TerritoryFinder.FindAll(grid);
        return CapitalReconciler.Reconcile(raw, new List<Territory>(), grid);
    }
}
