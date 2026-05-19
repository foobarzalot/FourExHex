using System;
using System.Collections.Generic;

/// <summary>
/// Result of a fresh-map build: the land tiles (owner-colored, with any
/// scattered initial occupants) plus the set of water coords inside the
/// rectangular map bounds. Water coords are NOT in the grid — they are
/// off-map for every gameplay rule and only the renderer reads them.
/// </summary>
public sealed record MapGenResult(HexGrid Grid, IReadOnlySet<HexCoord> WaterCoords);

/// <summary>
/// Builds the initial hex grid for a fresh game. Carves a single contiguous
/// landmass out of a rectangular cols×rows region using a cellular-automata
/// pass; the rest of the rectangle (including every rim cell) is water.
/// Deterministic in <paramref name="seed"/> — the same seed always yields
/// the same map.
///
/// Algorithm at a glance:
///   1. Random init: rim = water, interior cells flip a coin (~45% land).
///   2. Smooth: 5 CA passes with the "B5/S3" hex rule (water becomes land
///      with 5+ land neighbors; land becomes water with 2 or fewer). Cells
///      with 3-4 neighbors persist, which preserves peninsulas without
///      preserving 1-wide bridges.
///   3. Keep only the single largest land component; demote everything else
///      to water.
///   4. Fill enclosed water: any water cell not reachable from the rim
///      becomes land. Combined with step 3 this guarantees both land and
///      water are single connected components.
///   5. Final survival pass: erode any land cell that ended up with fewer
///      than 3 land neighbors after the cleanup (defensive — rarely fires).
///
/// After shape generation, players are assigned per-cell at random (matches
/// Slay's "fragmented territories" feel) and ~5% of land cells get trees.
/// </summary>
public static class MapGenerator
{
    private const double InitialLandProbability = 0.65;
    private const int CaIterations = 5;
    private const int MinLandCount = 30;
    private const int MaxRetries = 8;

    public static MapGenResult BuildInitialGrid(int cols, int rows, IReadOnlyList<Player> players, int seed)
    {
        var rng = new Random(seed);

        HashSet<HexCoord> land = new();
        for (int retry = 0; retry < MaxRetries; retry++)
        {
            land = GenerateLandShape(cols, rows, rng);
            if (land.Count >= MinLandCount) break;
        }

        var grid = new HexGrid();
        foreach (HexCoord coord in land)
        {
            PlayerId owner = players[rng.Next(players.Count)].Id;
            grid.Add(new HexTile(coord, owner));
        }

        // Water = every coord in the rectangle that isn't land.
        var water = new HashSet<HexCoord>();
        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                HexCoord coord = HexCoord.FromOffset(col, row);
                if (!land.Contains(coord)) water.Add(coord);
            }
        }

        // Scatter initial trees on land — roughly 5% of land tiles. Mirrors
        // Slay's behavior of starting boards with visible forest. Tree
        // placement is bounded by available occupant-free land; with sparse
        // maps the loop just stops short of the target rather than retrying.
        int treeTarget = grid.Count / 20;
        var landCoordList = new List<HexCoord>(land);
        for (int i = 0; i < treeTarget && landCoordList.Count > 0; i++)
        {
            int idx = rng.Next(landCoordList.Count);
            HexCoord pick = landCoordList[idx];
            landCoordList.RemoveAt(idx);
            HexTile? t = grid.Get(pick);
            if (t != null && t.Occupant == null)
            {
                t.Occupant = new Tree();
            }
        }

        return new MapGenResult(grid, water);
    }

    /// <summary>
    /// Generate the land/water mask. Returns the set of land coords; all
    /// other coords inside the rectangle (including the rim) are water.
    /// May return a too-small set if the random init was unlucky — the
    /// caller retries with a fresh draw.
    /// </summary>
    private static HashSet<HexCoord> GenerateLandShape(int cols, int rows, Random rng)
    {
        var land = new HashSet<HexCoord>();

        // 1. Random init. Rim cells stay water (we only seed the interior).
        for (int row = 1; row < rows - 1; row++)
        {
            for (int col = 1; col < cols - 1; col++)
            {
                if (rng.NextDouble() < InitialLandProbability)
                {
                    land.Add(HexCoord.FromOffset(col, row));
                }
            }
        }

        // 2. Cellular-automata smoothing. B5/S3 hex rule: a water cell
        // becomes land when 5+ of its 6 neighbors are land; a land cell
        // becomes water when only 0-2 are land. Cells with 3-4 land
        // neighbors stay as they are. Iterate on a copy so the pass is
        // synchronous (no mid-iteration neighbor confusion). Rim cells
        // are skipped so they remain water by construction.
        for (int iter = 0; iter < CaIterations; iter++)
        {
            var next = new HashSet<HexCoord>(land);
            for (int row = 1; row < rows - 1; row++)
            {
                for (int col = 1; col < cols - 1; col++)
                {
                    HexCoord coord = HexCoord.FromOffset(col, row);
                    int n = CountLandNeighbors(coord, land);
                    bool wasLand = land.Contains(coord);
                    if (!wasLand && n >= 5) next.Add(coord);
                    else if (wasLand && n <= 2) next.Remove(coord);
                }
            }
            land = next;
        }

        // 3. Keep only the single largest connected land component.
        land = KeepLargestComponent(land);

        // 4. Fill any enclosed water (water not reachable from any rim cell).
        FillEnclosedWater(land, cols, rows);

        // 5. Final survival pass — defensive cleanup. After step 3 reduces
        // the land set, some cells along the cut may have ended up with <3
        // land neighbors; erode those. Iterate until stable.
        bool changed = true;
        while (changed)
        {
            changed = false;
            var toRemove = new List<HexCoord>();
            foreach (HexCoord c in land)
            {
                if (CountLandNeighbors(c, land) < 3) toRemove.Add(c);
            }
            if (toRemove.Count > 0)
            {
                foreach (HexCoord c in toRemove) land.Remove(c);
                changed = true;
            }
        }

        // Re-run "keep largest" in case erosion split the island.
        land = KeepLargestComponent(land);

        // Re-fill any newly-enclosed water (rare but possible after erosion).
        FillEnclosedWater(land, cols, rows);

        return land;
    }

    private static int CountLandNeighbors(HexCoord coord, HashSet<HexCoord> land)
    {
        int n = 0;
        foreach (HexCoord nb in coord.Neighbors())
        {
            if (land.Contains(nb)) n++;
        }
        return n;
    }

    private static HashSet<HexCoord> KeepLargestComponent(HashSet<HexCoord> land)
    {
        if (land.Count == 0) return land;
        var visited = new HashSet<HexCoord>();
        HashSet<HexCoord>? best = null;
        foreach (HexCoord seed in land)
        {
            if (visited.Contains(seed)) continue;
            var component = new HashSet<HexCoord>();
            var queue = new Queue<HexCoord>();
            queue.Enqueue(seed);
            visited.Add(seed);
            component.Add(seed);
            while (queue.Count > 0)
            {
                HexCoord c = queue.Dequeue();
                foreach (HexCoord n in c.Neighbors())
                {
                    if (!land.Contains(n)) continue;
                    if (!visited.Add(n)) continue;
                    component.Add(n);
                    queue.Enqueue(n);
                }
            }
            if (best == null || component.Count > best.Count) best = component;
        }
        return best ?? new HashSet<HexCoord>();
    }

    /// <summary>
    /// Promote any water cell that is not reachable from the rim to land.
    /// "Water" is implicit: every coord in the cols×rows rectangle that
    /// isn't in <paramref name="land"/>. The rim itself is always water,
    /// so its flood-fill reaches every water coord that participates in
    /// the outer ocean; whatever's left over is enclosed (a "lake") and
    /// gets filled in.
    /// </summary>
    private static void FillEnclosedWater(HashSet<HexCoord> land, int cols, int rows)
    {
        var rimReachable = new HashSet<HexCoord>();
        var queue = new Queue<HexCoord>();
        for (int col = 0; col < cols; col++)
        {
            EnqueueWaterIfUnseen(HexCoord.FromOffset(col, 0), land, rimReachable, queue);
            EnqueueWaterIfUnseen(HexCoord.FromOffset(col, rows - 1), land, rimReachable, queue);
        }
        for (int row = 1; row < rows - 1; row++)
        {
            EnqueueWaterIfUnseen(HexCoord.FromOffset(0, row), land, rimReachable, queue);
            EnqueueWaterIfUnseen(HexCoord.FromOffset(cols - 1, row), land, rimReachable, queue);
        }
        while (queue.Count > 0)
        {
            HexCoord c = queue.Dequeue();
            foreach (HexCoord n in c.Neighbors())
            {
                if (!InBounds(n, cols, rows)) continue;
                if (land.Contains(n)) continue;
                if (!rimReachable.Add(n)) continue;
                queue.Enqueue(n);
            }
        }

        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                HexCoord coord = HexCoord.FromOffset(col, row);
                if (!land.Contains(coord) && !rimReachable.Contains(coord))
                {
                    land.Add(coord);
                }
            }
        }
    }

    private static void EnqueueWaterIfUnseen(
        HexCoord coord,
        HashSet<HexCoord> land,
        HashSet<HexCoord> rimReachable,
        Queue<HexCoord> queue)
    {
        if (land.Contains(coord)) return;
        if (!rimReachable.Add(coord)) return;
        queue.Enqueue(coord);
    }

    private static bool InBounds(HexCoord coord, int cols, int rows)
    {
        (int col, int row) = coord.ToOffset();
        return col >= 0 && col < cols && row >= 0 && row < rows;
    }
}
