using System.Collections.Generic;

/// <summary>
/// Picks the capital hex for a territory. Pure function — given the same set
/// of coordinates it must always return the same result, regardless of the
/// enumeration order in which they were discovered.
/// </summary>
public static class CapitalAssigner
{
    /// <summary>
    /// Returns the chosen capital coord, or null if the territory is too
    /// small to deserve a capital (size &lt; 2). In Slay, singletons don't
    /// generate income so they have no capital.
    /// </summary>
    public static HexCoord? Choose(IReadOnlyCollection<HexCoord> coords)
    {
        if (coords.Count < 2) return null;

        // Deterministic order-independent pick: lexicographic minimum by (R, Q).
        HexCoord? best = null;
        foreach (HexCoord c in coords)
        {
            if (!best.HasValue || IsLessThan(c, best.Value))
            {
                best = c;
            }
        }
        return best;
    }

    private static bool IsLessThan(HexCoord a, HexCoord b) =>
        a.R < b.R || (a.R == b.R && a.Q < b.Q);
}
