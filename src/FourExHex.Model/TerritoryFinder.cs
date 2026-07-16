// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;

/// <summary>
/// Partitions a HexGrid into Territories by flood-fill: two tiles belong
/// to the same territory iff they are connected in the grid through an
/// unbroken chain of same-owner neighbors. Does NOT assign capitals — the
/// returned territories all have <see cref="Territory.Capital"/> equal to
/// null. Callers that need capitals should run
/// <see cref="CapitalReconciler.Reconcile"/> on the output.
/// </summary>
public static class TerritoryFinder
{
    /// <summary>
    /// Standard post-mutation recompute: re-flood-fill the grid,
    /// reconcile capitals against the previous territory list (so
    /// inherited capitals keep their tile while orphaned ones get
    /// new ones placed), and — when <paramref name="treasury"/> is
    /// non-null — reconcile gold across the resulting split/merge.
    /// Editor callers pass <paramref name="treasury"/> = null since
    /// the map editor has no per-capital gold to track.
    /// Returns the new reconciled territory list; the caller is
    /// responsible for assigning it back into <c>GameState.Territories</c>
    /// (the helper can't, since <c>Treasury.ReconcileAfterCapture</c>
    /// needs both old and new lists to walk).
    /// </summary>
    public static IReadOnlyList<Territory> Recompute(
        HexGrid grid,
        IReadOnlyList<Territory> previous,
        Treasury? treasury = null,
        bool randomizeCapital = false,
        HexCoord? originCapital = null)
    {
        IReadOnlyList<Territory> raw = FindAll(grid);
        IReadOnlyList<Territory> reconciled =
            CapitalReconciler.Reconcile(raw, previous, grid, randomizeCapital, originCapital);
        treasury?.ReconcileAfterCapture(previous, reconciled);
        return reconciled;
    }

    public static IReadOnlyList<Territory> FindAll(HexGrid grid)
    {
        var territories = new List<Territory>();
        var visited = new HashSet<HexCoord>();

        foreach (HexTile seed in grid.Tiles)
        {
            if (!visited.Add(seed.Coord))
            {
                continue;
            }

            PlayerId owner = seed.Owner;
            var coords = new List<HexCoord> { seed.Coord };
            var frontier = new Queue<HexCoord>();
            frontier.Enqueue(seed.Coord);

            while (frontier.Count > 0)
            {
                HexCoord current = frontier.Dequeue();

                foreach (HexTile neighbor in grid.NeighborsOf(current))
                {
                    if (neighbor.Owner != owner)
                    {
                        continue;
                    }
                    if (!visited.Add(neighbor.Coord))
                    {
                        continue;
                    }

                    coords.Add(neighbor.Coord);
                    frontier.Enqueue(neighbor.Coord);
                }
            }

            territories.Add(new Territory(owner, coords, capital: null));
        }

        return territories;
    }
}
