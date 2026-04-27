using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Pure lookups over a territory list. Shared by GameController
/// (real play) and AiSimulator (simulated play) so both paths
/// resolve territories identically.
/// </summary>
public static class TerritoryLookup
{
    public static Territory? FindOwnedContaining(
        IReadOnlyList<Territory> territories, Color owner, HexCoord coord)
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
}
