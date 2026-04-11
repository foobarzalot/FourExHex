using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Pure rules for tree behavior on the board:
///   - <see cref="ConvertGravesToTrees"/>: turn every grave into a tree.
///   - <see cref="SpreadTrees"/>: each pair of adjacent trees spawns a
///     third tree in an empty cell they both touch. Only ONE new tree is
///     created per pair, chosen deterministically as the lex-min candidate
///     when several common empty neighbors exist. Spawns from a single
///     call are applied simultaneously, so trees created this turn do
///     NOT seed further spreads within the same call.
///   - <see cref="CountNonTreeTiles"/>: how many tiles in a territory
///     actually produce income (trees block income on their tile).
/// Trees do not block unit placement: moving a unit onto a tree clears
/// it and consumes the unit's action — that rule lives in
/// <see cref="MovementRules"/>.
/// </summary>
public static class TreeRules
{
    /// <summary>
    /// Replace every <see cref="Grave"/> on the grid with a <see cref="Tree"/>.
    /// Called at the end of a turn before <see cref="SpreadTrees"/>.
    /// </summary>
    public static void ConvertGravesToTrees(HexGrid grid)
    {
        foreach (HexTile tile in grid.Tiles)
        {
            if (tile.Occupant is Grave)
            {
                tile.Occupant = new Tree();
            }
        }
    }

    /// <summary>
    /// Spread trees by one step. For every unordered pair of adjacent trees
    /// we find their common empty neighbors; if any, we schedule a new
    /// tree on the lex-min candidate. All scheduled spawns are then
    /// applied together — trees created during this call do not themselves
    /// participate in further spreading during the same call.
    /// </summary>
    public static void SpreadTrees(HexGrid grid)
    {
        // Deterministic iteration: sort all existing tree coords lex-min.
        List<HexCoord> treeCoords = grid.Tiles
            .Where(t => t.Occupant is Tree)
            .Select(t => t.Coord)
            .OrderBy(c => c)
            .ToList();
        var treeSet = new HashSet<HexCoord>(treeCoords);

        var spawnTargets = new HashSet<HexCoord>();

        foreach (HexCoord a in treeCoords)
        {
            var aNeighbors = new HashSet<HexCoord>(a.Neighbors());
            foreach (HexCoord b in a.Neighbors())
            {
                if (!treeSet.Contains(b)) continue;
                // Each pair processed once: only consider pairs with a < b.
                if (b.CompareTo(a) <= 0) continue;

                // Common empty neighbors of a and b.
                List<HexCoord> candidates = new();
                foreach (HexCoord bn in b.Neighbors())
                {
                    if (!aNeighbors.Contains(bn)) continue;
                    HexTile? tile = grid.Get(bn);
                    if (tile == null) continue;
                    if (tile.Occupant != null) continue;
                    candidates.Add(bn);
                }

                if (candidates.Count == 0) continue;
                candidates.Sort();
                spawnTargets.Add(candidates[0]);
            }
        }

        foreach (HexCoord coord in spawnTargets)
        {
            HexTile? tile = grid.Get(coord);
            if (tile != null && tile.Occupant == null)
            {
                tile.Occupant = new Tree();
            }
        }
    }

    /// <summary>
    /// Number of tiles in <paramref name="territory"/> that are NOT
    /// occupied by a tree. Used by income collection so tree tiles
    /// don't pay out.
    /// </summary>
    public static int CountNonTreeTiles(Territory territory, HexGrid grid)
    {
        int count = 0;
        foreach (HexCoord coord in territory.Coords)
        {
            HexTile? tile = grid.Get(coord);
            if (tile == null) continue;
            if (tile.Occupant is Tree) continue;
            count++;
        }
        return count;
    }
}
