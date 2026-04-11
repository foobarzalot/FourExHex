using System.Linq;

/// <summary>
/// Pure calculation of the defense value covering a hex. Defense is the
/// max contribution over the tile's own occupant and the occupants of
/// every adjacent tile in the same territory. Occupant contributions:
///   - <see cref="Unit"/>     -> 1 (only peasants exist for now)
///   - <see cref="Capital"/>  -> 1
///   - null / other occupants -> 0
/// Both units and capitals radiate their contribution to adjacent
/// same-territory tiles.
/// </summary>
public static class DefenseRules
{
    /// <summary>
    /// Defense value covering the tile at <paramref name="coord"/>. To
    /// capture this tile, the attacker's level must be strictly greater.
    /// </summary>
    public static int Defense(HexCoord coord, HexGrid grid, Territory territory)
    {
        int max = 0;

        HexTile? tile = grid.Get(coord);
        if (tile != null)
        {
            max = System.Math.Max(max, ContributionOf(tile.Occupant));
        }

        foreach (HexCoord neighbor in coord.Neighbors())
        {
            if (!territory.Coords.Contains(neighbor)) continue;
            HexTile? neighborTile = grid.Get(neighbor);
            if (neighborTile == null) continue;
            max = System.Math.Max(max, ContributionOf(neighborTile.Occupant));
        }

        return max;
    }

    /// <summary>
    /// How much defense a single occupant contributes to its own tile (and,
    /// via radiation, to adjacent same-territory tiles).
    /// </summary>
    public static int ContributionOf(HexOccupant? occupant) => occupant switch
    {
        Unit => 1,
        Capital => 1,
        _ => 0,
    };
}
