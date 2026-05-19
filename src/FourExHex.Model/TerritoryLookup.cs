using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Pure lookups over a territory list. Shared by GameController
/// (real play) and AiSimulator (simulated play) so both paths
/// resolve territories identically.
/// </summary>
public static class TerritoryLookup
{
    /// <summary>
    /// First territory (in iteration order) whose coords contain
    /// <paramref name="coord"/>, regardless of owner. Territory partitions
    /// are disjoint per CapitalReconciler, so iteration order doesn't
    /// affect correctness.
    /// </summary>
    public static Territory? FindContaining(
        IReadOnlyList<Territory> territories, HexCoord coord)
    {
        foreach (Territory t in territories)
        {
            if (t.Coords.Contains(coord)) return t;
        }
        return null;
    }

    public static Territory? FindOwnedContaining(
        IReadOnlyList<Territory> territories, PlayerId owner, HexCoord coord)
    {
        foreach (Territory t in territories)
        {
            if (t.Owner == owner && t.Coords.Contains(coord))
            {
                return t;
            }
        }
        return null;
    }

    public static Territory? FindByCapital(
        IReadOnlyList<Territory> territories, HexCoord capital)
    {
        foreach (Territory t in territories)
        {
            if (t.HasCapital && t.Capital!.Value == capital)
            {
                return t;
            }
        }
        return null;
    }

    /// <summary>
    /// Every territory owned by <paramref name="owner"/> that still has a
    /// capital — i.e. the territories that own a treasury and can still
    /// take actions. The "alive" set from this owner's perspective:
    /// territories that lost their capital to capture are excluded.
    /// </summary>
    public static IEnumerable<Territory> OwnedCapitalBearing(
        IReadOnlyList<Territory> territories, PlayerId owner)
    {
        foreach (Territory t in territories)
        {
            if (t.Owner == owner && t.HasCapital) yield return t;
        }
    }
}
