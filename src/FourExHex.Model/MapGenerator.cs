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
    // Integer percent (was a double 0.65 before issue #20's no-floats
    // rule) — compared against rng.Next(100) below for the same
    // 65% land seed probability with no floating-point on the path.
    private const int InitialLandPercent = 65;
    private const int CaIterations = 5;
    private const int MinLandCount = 30;
    private const int MaxRetries = 8;

    // Clumped owner assignment (#72). Lloyd relaxation passes that re-center seeds
    // on their region centroids to even out Voronoi areas, and the gate that runs
    // them only in the few-seeds regime — when the average region is at least this
    // many cells, so centroids are meaningful and the fairness actually bites. At
    // high seed counts (low clumping) regions are tiny and Lloyd is a no-op, so we
    // skip the work.
    private const int LloydPasses = 2;
    private const int LloydMinRegionSize = 6;

    public static MapGenResult BuildInitialGrid(
        int cols, int rows, IReadOnlyList<Player> players, int seed, MapGenOptions? options = null)
    {
        options ??= MapGenOptions.None;
        var rng = new Random(seed);

        HashSet<HexCoord> land = new();
        for (int retry = 0; retry < MaxRetries; retry++)
        {
            land = GenerateLandShape(cols, rows, rng);
            if (land.Count >= MinLandCount) break;
        }

        var grid = new HexGrid();
        if (options.ClumpingFactor <= 0)
        {
            // Factor 0: today's per-cell random assignment, verbatim — one
            // rng.Next(players.Count) draw per land cell in HashSet order. Gated
            // like the mountain/gold passes so a disabled clumping pass makes the
            // map byte-identical to the pre-#72 baseline (the #20 determinism ref).
            foreach (HexCoord coord in land)
            {
                PlayerId owner = players[rng.Next(players.Count)].Id;
                grid.Add(new HexTile(coord, owner));
            }
        }
        else
        {
            AssignClumpedOwners(grid, land, players, options.ClumpingFactor, rng);
        }

        // Mountain ranges (issue #48), gated on density > 0 so a disabled pass
        // makes zero RNG draws and the map stays byte-identical to the no-options
        // baseline. Placed before the tree scatter so trees simply avoid mountains.
        if (options.MountainDensity > 0)
        {
            ScatterMountainRanges(grid, land, options.MountainDensity, rng);
        }

        // Gold clusters (issue #48), gated the same way. Placed after mountains
        // so cluster seeds can be biased toward mountain tiles, and before the
        // tree scatter so trees avoid the fresh gold tiles.
        if (options.GoldDensity > 0)
        {
            ScatterGoldClusters(grid, land, options.GoldDensity, rng);
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

        // Scatter initial trees on land — TreeDensity percent of land tiles
        // (default 5%). Mirrors Slay's behavior of starting boards with visible
        // forest. `grid` holds exactly the land tiles (one per land coord, none
        // added/removed since), so land.Count is the single density base shared
        // with the mountain/gold passes; at density 5 this equals the historical
        // grid.Count / 20 byte-for-byte. Tree placement is bounded by available
        // occupant-free land; sparse maps just stop short of the target.
        int treeTarget = land.Count * options.TreeDensity / 100;
        Log.Debug(Log.LogCategory.MapGen,
            $"[mapgen] densities trees={options.TreeDensity} mtn={options.MountainDensity} " +
            $"gold={options.GoldDensity} (land {land.Count}, treeTarget {treeTarget})");
        var landCoordList = new List<HexCoord>(land);
        for (int i = 0; i < treeTarget && landCoordList.Count > 0; i++)
        {
            int idx = rng.Next(landCoordList.Count);
            HexCoord pick = landCoordList[idx];
            landCoordList.RemoveAt(idx);
            HexTile? t = grid.Get(pick);
            // Trees coexist with mountains (issue #81) but not with gold (gold
            // tiles stay income-clear), so only the gold flag blocks placement.
            if (t != null && t.Occupant == null && !t.IsGold)
            {
                t.Occupant = new Tree();
            }
        }

        return new MapGenResult(grid, water);
    }

    // Clumped owner assignment (issue #72) — seed-flood Voronoi. Instead of an
    // independent random owner per cell (the fragmented baseline), scatter a few
    // owned "seed" cells and flood-fill ownership outward so each player ends up
    // with one or more coherent contiguous regions. The clumping factor (1..100)
    // controls how many seeds: fewer seeds → larger blobs. At 100 there is exactly
    // one seed per player (one blob each); as the factor drops, the seed count
    // rises toward land.Count, shrinking blobs back toward salt-and-pepper noise.
    //
    // Fairness: seeds start farthest-point (spread as far apart as the landmass
    // allows), then — in the few-seeds regime — two Lloyd relaxation passes re-center
    // each seed on its region centroid and re-flood, so the Voronoi regions come out
    // near-equal in AREA, not just equal in count. Clustered random seeds were the
    // source of wildly unfair splits (one player a sliver, another a basin) at the
    // high end. Owners are assigned round-robin, so every player gets a balanced
    // share and clumped starts stay capital-placeable for any player count (#70).
    // Determinism: the candidate order is sorted (HexCoord.CompareTo) so every tie
    // (farthest cell, contested flood cell, centroid-nearest cell) breaks lex-min,
    // and the only rng draw is the first seed — same seed + factor reproduces the
    // map. Integer-only (no floats — Model rule).
    private static void AssignClumpedOwners(
        HexGrid grid, HashSet<HexCoord> land, IReadOnlyList<Player> players, int clump, Random rng)
    {
        clump = Math.Clamp(clump, 1, 100);
        int playerCount = players.Count;

        // Deterministic universe of candidate cells; sorted = stable lex order used
        // for every tie-break below.
        var ordered = new List<HexCoord>(land);
        ordered.Sort();

        // Interpolate seed count: clump 100 → playerCount seeds (max blob), clump→1
        // → ≈land.Count seeds (noise). Clamp to [playerCount, land.Count].
        int seedCount = playerCount + (ordered.Count - playerCount) * (100 - clump) / 100;
        seedCount = Math.Clamp(seedCount, Math.Min(playerCount, ordered.Count), ordered.Count);

        // Farthest-point seed placement. `dist[c]` is the hop distance from c to the
        // nearest seed chosen so far; each new seed is the cell currently farthest
        // from every seed (ties → lex-min). The first seed is one seeded-rng pick so
        // different game seeds still produce different (but equally fair) layouts.
        var dist = new Dictionary<HexCoord, int>(ordered.Count);
        foreach (HexCoord c in ordered) dist[c] = int.MaxValue;
        var seeds = new List<HexCoord>(seedCount);

        void AddSeed(HexCoord s)
        {
            seeds.Add(s);
            dist[s] = 0;
            // Bounded BFS: relax distances outward, stopping where this seed no
            // longer beats an already-nearer seed.
            var q = new Queue<HexCoord>();
            q.Enqueue(s);
            while (q.Count > 0)
            {
                HexCoord cur = q.Dequeue();
                int nd = dist[cur] + 1;
                foreach (HexCoord nb in cur.Neighbors())
                {
                    if (!land.Contains(nb) || dist[nb] <= nd) continue;
                    dist[nb] = nd;
                    q.Enqueue(nb);
                }
            }
        }

        AddSeed(ordered[rng.Next(ordered.Count)]);
        while (seeds.Count < seedCount)
        {
            HexCoord best = ordered[0];
            int bestDist = -1;
            foreach (HexCoord c in ordered) // sorted → the first cell at the max wins (lex-min)
            {
                if (dist[c] > bestDist) { bestDist = dist[c]; best = c; }
            }
            AddSeed(best);
        }

        // Multi-source BFS Voronoi flood, labelling each cell with the index of the
        // seed that claims it. Seeds enter the frontier in sorted order so a cell
        // reached by two wavefronts at the same depth resolves to the lex-min seed —
        // stable and rng-independent. Returns the cell→seedIndex region map.
        Dictionary<HexCoord, int> Flood()
        {
            var order = new List<int>(seeds.Count);
            for (int i = 0; i < seeds.Count; i++) order.Add(i);
            order.Sort((a, b) => seeds[a].CompareTo(seeds[b]));

            var region = new Dictionary<HexCoord, int>(ordered.Count);
            var frontier = new Queue<HexCoord>();
            foreach (int i in order) { region[seeds[i]] = i; frontier.Enqueue(seeds[i]); }
            while (frontier.Count > 0)
            {
                HexCoord cur = frontier.Dequeue();
                int r = region[cur];
                foreach (HexCoord nb in cur.Neighbors())
                {
                    if (!land.Contains(nb) || region.ContainsKey(nb)) continue;
                    region[nb] = r;
                    frontier.Enqueue(nb);
                }
            }
            return region;
        }

        Dictionary<HexCoord, int> regionOf = Flood();

        // Lloyd relaxation (gated to the few-seeds regime): re-center each seed on its
        // region's centroid and re-flood, so the Voronoi areas even out instead of
        // skewing with the landmass shape. Two passes capture most of the benefit;
        // bail early if a pass moves nothing. Skipped when regions are already tiny
        // (high seed count) — there centroids add nothing and area is balanced.
        bool fewSeeds = seedCount > 0 && land.Count >= seedCount * LloydMinRegionSize;
        if (fewSeeds)
        {
            for (int pass = 0; pass < LloydPasses; pass++)
            {
                // Integer centroid of each region: mean axial (Q, R) over its cells.
                long[] sumQ = new long[seeds.Count];
                long[] sumR = new long[seeds.Count];
                int[] cnt = new int[seeds.Count];
                foreach (KeyValuePair<HexCoord, int> kv in regionOf)
                {
                    sumQ[kv.Value] += kv.Key.Q;
                    sumR[kv.Value] += kv.Key.R;
                    cnt[kv.Value]++;
                }

                // Move each seed to the region cell nearest its centroid (ties →
                // lex-min, via the sorted scan). cnt is ≥1: a seed is in its own region.
                var nearest = new HexCoord[seeds.Count];
                int[] bestHexDist = new int[seeds.Count];
                for (int i = 0; i < seeds.Count; i++) bestHexDist[i] = int.MaxValue;
                foreach (HexCoord c in ordered)
                {
                    int r = regionOf[c];
                    var centroid = new HexCoord((int)(sumQ[r] / cnt[r]), (int)(sumR[r] / cnt[r]));
                    int d = HexCoord.Distance(c, centroid);
                    if (d < bestHexDist[r]) { bestHexDist[r] = d; nearest[r] = c; }
                }

                bool moved = false;
                for (int i = 0; i < seeds.Count; i++)
                {
                    if (!nearest[i].Equals(seeds[i])) { seeds[i] = nearest[i]; moved = true; }
                }
                if (!moved) break;
                regionOf = Flood();
            }
        }

        // Materialize: owner = round-robin over the seed index that claimed the cell.
        // Every land cell is reachable from a seed (the landmass is one connected
        // component — the LandIsContiguous guarantee); the fallback is purely
        // defensive so grid.Add never sees a gap.
        int fallback = 0;
        foreach (HexCoord coord in land)
        {
            PlayerId owner = regionOf.TryGetValue(coord, out int r2)
                ? players[r2 % playerCount].Id
                : players[fallback++ % playerCount].Id;
            grid.Add(new HexTile(coord, owner));
        }

        Log.Debug(Log.LogCategory.MapGen,
            $"[mapgen] clumped owners: factor={clump} land={land.Count} seeds={seedCount} " +
            $"lloyd={(fewSeeds ? LloydPasses : 0)}" + (fallback > 0 ? $" fallback={fallback}" : ""));
    }

    // Mountain-range scatter (issue #48, Phase 1). Each "range" is a biased
    // random walk (a "mountain agent"): pick a land start tile and a hex
    // direction, then walk a chain that mostly continues straight but
    // occasionally veers ±1, marking each tile a mountain. Each spine tile also
    // has a chance to drop one perpendicular "foothill" neighbor, giving 1–2-wide
    // ranges rather than 1-wide lines or random speckle. Repeat ranges until the
    // density target (percent of land) is met. All draws are integer rng.Next (no
    // floats — Model assembly rule) and the start-tile list is sorted, so it stays
    // deterministic in the seed.
    private const int MinRangeLen = 4;
    private const int MaxRangeLen = 9;
    private const int TurnPercent = 22;    // per-step chance to veer one hex CCW (and again CW)
    private const int ThickenPercent = 45; // per-spine-tile chance to add a side foothill

    private static void ScatterMountainRanges(
        HexGrid grid, HashSet<HexCoord> land, int density, Random rng)
    {
        if (land.Count == 0) return;

        int target = land.Count * density / 100;
        if (target <= 0) return;

        // Sorted start-tile list keeps sampling deterministic regardless of the
        // HashSet's internal ordering.
        var landList = new List<HexCoord>(land);
        landList.Sort();

        int placed = 0;
        int attempts = 0;
        int maxAttempts = target * 4;
        while (placed < target && attempts < maxAttempts)
        {
            attempts++;
            HexCoord cur = landList[rng.Next(landList.Count)];
            int dir = rng.Next(6);
            int len = rng.Next(MinRangeLen, MaxRangeLen + 1);
            for (int step = 0; step <= len && placed < target; step++)
            {
                if (!land.Contains(cur)) break; // walked off the landmass — end the range
                placed += MarkMountain(grid, cur);

                // Thicken: occasional foothill on one (roughly perpendicular) side.
                if (rng.Next(100) < ThickenPercent)
                {
                    int sideDir = (dir + (rng.Next(2) == 0 ? 2 : 4)) % 6;
                    HexCoord side = cur.Neighbor(sideDir);
                    if (land.Contains(side)) placed += MarkMountain(grid, side);
                }

                // Advance with an occasional ±1 veer so the spine bends.
                int v = rng.Next(100);
                if (v < TurnPercent) dir = (dir + 5) % 6;
                else if (v < 2 * TurnPercent) dir = (dir + 1) % 6;
                cur = cur.Neighbor(dir);
            }
        }

        Log.Debug(Log.LogCategory.MapGen,
            $"[mapgen] mountains: placed {placed} across {attempts} ranges, target {target} (land {land.Count})");
    }

    // Set the mountain flag on a land tile, forfeiting ownership — a generated
    // range is neutral terrain players must capture, not pre-owned land. Trees
    // coexist with mountains (issue #81) so any occupant is left in place; gold
    // is never present here (mountains are placed before gold, and ScatterGold
    // skips mountain tiles). Returns 1 if the tile was newly marked, else 0 so
    // the caller can count real coverage without double-counting overlaps.
    private static int MarkMountain(HexGrid grid, HexCoord coord)
    {
        HexTile? t = grid.Get(coord);
        if (t == null || t.IsMountain) return 0;
        t.IsMountain = true;
        t.Owner = PlayerId.None;
        return 1;
    }

    // Gold-cluster scatter (issue #48, Phase 2). Gold is a sparse, contested
    // resource: small neutral clusters (a seed tile plus a few grown neighbors)
    // rather than ranges. Gold and mountain are mutually exclusive (issue #81),
    // so gold never lands on a mountain tile (MarkGold skips them) and the old
    // gold-on-mountain seed bias is gone. All draws are integer rng.Next and
    // every sampled list is sorted, so it stays deterministic in the seed.
    private const int GoldMinCluster = 2;        // min tiles per cluster (>=2 → no speckle)
    private const int GoldMaxCluster = 4;

    private static void ScatterGoldClusters(
        HexGrid grid, HashSet<HexCoord> land, int density, Random rng)
    {
        if (land.Count == 0) return;

        int target = land.Count * density / 100;
        if (target <= 0) return;

        var landList = new List<HexCoord>(land);
        landList.Sort();

        int placed = 0;
        int attempts = 0;
        int maxAttempts = target * 5;
        while (placed < target && attempts < maxAttempts)
        {
            attempts++;

            HexCoord seed = landList[rng.Next(landList.Count)];

            int size = rng.Next(GoldMinCluster, GoldMaxCluster + 1);
            var cluster = new HashSet<HexCoord> { seed };
            placed += MarkGold(grid, seed);

            while (cluster.Count < size && placed < target)
            {
                // Land neighbors of the cluster not already in it.
                var cands = new List<HexCoord>();
                var seen = new HashSet<HexCoord>();
                foreach (HexCoord c in cluster)
                {
                    foreach (HexCoord nb in c.Neighbors())
                    {
                        if (!land.Contains(nb)) continue;
                        if (cluster.Contains(nb)) continue;
                        if (seen.Add(nb)) cands.Add(nb);
                    }
                }
                if (cands.Count == 0) break;
                cands.Sort();
                HexCoord pick = cands[rng.Next(cands.Count)];
                cluster.Add(pick);
                placed += MarkGold(grid, pick);
            }
        }

        Log.Debug(Log.LogCategory.MapGen,
            $"[mapgen] gold: placed {placed} across {attempts} clusters, target {target} (land {land.Count})");
    }

    // Set the gold flag on a land tile and forfeit ownership — generated gold is a
    // neutral, contested resource players must capture. Gold and mountain are
    // mutually exclusive (issue #81), so a mountain tile is skipped (mountain
    // wins). Returns 1 if newly marked, else 0.
    private static int MarkGold(HexGrid grid, HexCoord coord)
    {
        HexTile? t = grid.Get(coord);
        if (t == null || t.IsGold || t.IsMountain) return 0;
        t.IsGold = true;
        t.Owner = PlayerId.None;
        return 1;
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
                if (rng.Next(100) < InitialLandPercent)
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
