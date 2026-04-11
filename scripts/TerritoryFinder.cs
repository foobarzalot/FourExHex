using System.Collections.Generic;
using Godot;

/// <summary>
/// Partitions a HexGrid into Territories by flood-fill: two tiles belong
/// to the same territory iff they are connected in the grid through an
/// unbroken chain of same-color neighbors. Does NOT assign capitals — the
/// returned territories all have <see cref="Territory.Capital"/> equal to
/// null. Callers that need capitals should run
/// <see cref="CapitalReconciler.Reconcile"/> on the output.
/// </summary>
public static class TerritoryFinder
{
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

            Color color = seed.Color;
            var coords = new List<HexCoord> { seed.Coord };
            var frontier = new Queue<HexCoord>();
            frontier.Enqueue(seed.Coord);

            while (frontier.Count > 0)
            {
                HexCoord current = frontier.Dequeue();

                foreach (HexTile neighbor in grid.NeighborsOf(current))
                {
                    if (neighbor.Color != color)
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

            territories.Add(new Territory(color, coords, capital: null));
        }

        return territories;
    }
}
