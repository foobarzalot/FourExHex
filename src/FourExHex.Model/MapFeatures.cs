// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Linq;

/// <summary>
/// Pure grid-scan helpers for terrain-feature intros (issue #53): does a map
/// contain a given <see cref="TerrainFeature"/>, and which tile should the
/// camera pan to when introducing it. Godot-free and integer-only so it stays
/// unit-testable and honours the no-floats rule.
/// </summary>
public static class MapFeatures
{
    /// <summary>True when any tile carries <paramref name="feature"/>.</summary>
    public static bool Contains(HexGrid grid, TerrainFeature feature) =>
        feature != TerrainFeature.None && grid.Tiles.Any(t => t.Feature == feature);

    /// <summary>
    /// The tile carrying <paramref name="feature"/> with the smallest
    /// <see cref="HexCoord"/>, or null if none. Deterministic (order-independent
    /// of the underlying dictionary) so the camera-pan focus target is stable.
    /// </summary>
    public static HexCoord? FirstTile(HexGrid grid, TerrainFeature feature)
    {
        if (feature == TerrainFeature.None) return null;
        HexCoord? best = null;
        foreach (HexTile tile in grid.Tiles)
        {
            if (tile.Feature != feature) continue;
            if (best == null || tile.Coord.CompareTo(best.Value) < 0)
            {
                best = tile.Coord;
            }
        }
        return best;
    }
}
