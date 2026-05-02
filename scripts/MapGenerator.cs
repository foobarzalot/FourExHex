using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Builds the initial hex grid for a fresh game: per-tile owner-color
/// assignment plus ~5% scattered trees. Deterministic in the supplied
/// seed — the same seed always yields the same grid — which is what
/// powers the main menu's "Map Seed" field and the reproducibility
/// tests in <c>MapGeneratorTests</c>.
///
/// Lives in its own file (rather than as a private helper on
/// <see cref="Main"/>) so the test assembly can reach it; <c>Main.cs</c>
/// is excluded from the test build because it derives from a Godot node.
/// </summary>
public static class MapGenerator
{
    public static HexGrid BuildInitialGrid(int cols, int rows, IReadOnlyList<Player> players, int seed)
    {
        var rng = new Random(seed);
        var grid = new HexGrid();
        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                HexCoord coord = HexCoord.FromOffset(col, row);
                Color color = players[rng.Next(players.Count)].Color;
                grid.Add(new HexTile(coord, color));
            }
        }

        // Seed a handful of initial trees — roughly 5% of tiles — so
        // the board has visible forest at game start (Slay does this).
        // CapitalPlacer skips tree-occupied tiles, so capital assignment
        // on the downstream pipeline handles this correctly.
        int treeTarget = (cols * rows) / 20;
        var allCoords = new List<HexCoord>();
        foreach (HexTile tile in grid.Tiles) allCoords.Add(tile.Coord);
        for (int i = 0; i < treeTarget; i++)
        {
            int idx = rng.Next(allCoords.Count);
            HexCoord pick = allCoords[idx];
            allCoords.RemoveAt(idx);
            HexTile? t = grid.Get(pick);
            if (t != null && t.Occupant == null)
            {
                t.Occupant = new Tree();
            }
        }
        return grid;
    }
}
