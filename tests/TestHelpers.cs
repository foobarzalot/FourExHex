using System.Collections.Generic;
using Godot;

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
    /// colored <paramref name="color"/>. Used for hand-crafted topologies
    /// (e.g., testing a specific adjacency or a split/merge).
    /// </summary>
    public static HexGrid BuildSpotGrid(Color color, params HexCoord[] coords)
    {
        var grid = new HexGrid();
        foreach (HexCoord c in coords)
        {
            grid.Add(new HexTile(c, color));
        }
        return grid;
    }

    /// <summary>
    /// Build a rectangular odd-r offset grid of size <paramref name="cols"/>
    /// x <paramref name="rows"/>, every tile colored <paramref name="color"/>.
    /// </summary>
    public static HexGrid BuildRectGrid(int cols, int rows, Color color)
    {
        var grid = new HexGrid();
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                grid.Add(new HexTile(HexCoord.FromOffset(c, r), color));
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
