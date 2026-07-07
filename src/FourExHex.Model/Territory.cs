using System.Collections.Generic;

/// <summary>
/// A maximally-connected group of same-owner hexes. Immutable snapshot of
/// what the map looked like when the territory was discovered — consumers
/// should re-run territory detection after any tile changes owner.
/// </summary>
public class Territory
{
    public PlayerId Owner { get; }
    public IReadOnlyCollection<HexCoord> Coords { get; }
    public HexCoord? Capital { get; }
    public int Size => Coords.Count;

    public bool HasCapital => Capital.HasValue;

    // Membership only — iteration must go through Coords, whose order is a
    // determinism-load-bearing tie-breaker and the save wire order.
    private readonly HashSet<HexCoord> _coordSet;

    public Territory(PlayerId owner, IReadOnlyCollection<HexCoord> coords, HexCoord? capital = null)
    {
        Owner = owner;
        Coords = coords;
        Capital = capital;
        _coordSet = new HashSet<HexCoord>(coords);
    }

    /// <summary>O(1) membership test; equivalent to <c>Coords.Contains</c>.</summary>
    public bool Contains(HexCoord coord) => _coordSet.Contains(coord);
}

public static class TerritoryExtensions
{
    /// <summary>
    /// Build a <c>coord -&gt; containing territory</c> lookup table from a
    /// list of territories. Each coord maps to exactly one territory (the
    /// partition is disjoint by construction).
    /// </summary>
    public static Dictionary<HexCoord, Territory> BuildTileIndex(
        this IEnumerable<Territory> territories)
    {
        var index = new Dictionary<HexCoord, Territory>();
        foreach (Territory territory in territories)
        {
            foreach (HexCoord coord in territory.Coords)
            {
                index[coord] = territory;
            }
        }
        return index;
    }
}
