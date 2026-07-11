using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Pure calculation of the defense value covering a hex. Defense is the
/// max contribution over the tile's own occupant and the occupants of
/// every adjacent tile in the same territory. Occupant contributions:
///   - <see cref="Unit"/>          -> (int)unit.Level
///   - <see cref="Tower"/>         -> 2  (soldier-equivalent)
///   - <see cref="Capital"/>       -> 1
///   - <see cref="Tree"/> / <see cref="Grave"/> / null -> 0
///   - any unknown subtype        -> throws
/// Any defender — a <see cref="Unit"/>, <see cref="Tower"/>, or
/// <see cref="Capital"/> — standing on a mountain adds
/// <see cref="MountainBonus"/> (+1) on top of its contribution — high
/// ground. Units, towers, and capitals all radiate their (possibly
/// mountain-boosted) contribution to adjacent same-territory tiles.
/// Contributions don't stack — the max single value wins.
/// </summary>
public static class DefenseRules
{
    /// <summary>
    /// Extra defense any defender — a <see cref="Unit"/>,
    /// <see cref="Tower"/>, or <see cref="Capital"/> — gains from standing on a
    /// mountain: the high-ground bonus. A mountain gives no defense on its own
    /// (an empty mountain contributes nothing); only a defending occupant earns
    /// the bonus, and that boosted value radiates to same-territory neighbors
    /// like any other defender. Contributions still don't stack — the max wins.
    /// </summary>
    public const int MountainBonus = 1;

    /// <summary>
    /// Defense value covering the tile at <paramref name="coord"/>. To
    /// capture this tile, the attacker's level must be strictly greater.
    /// </summary>
    public static int Defense(HexCoord coord, HexGrid grid, Territory territory)
        => MaxContribution(coord, grid, territory, committedOnly: false, ignoring: null);

    /// <summary>
    /// Defense covering <paramref name="coord"/> from occupants that are
    /// settled for the turn: towers, capitals, and units that have
    /// already spent their move. A unit with a free move contributes
    /// nothing — it may march away next action. Same max-over-tile-and-
    /// same-territory-neighbors scan as <see cref="Defense"/>.
    /// <paramref name="ignoring"/>, when set, excludes that coord's
    /// occupant from the scan — AI tower scoring passes the placement
    /// tile so a candidate tower never disqualifies its own coverage.
    /// </summary>
    public static int CommittedDefense(
        HexCoord coord, HexGrid grid, Territory territory, HexCoord? ignoring = null)
        => MaxContribution(coord, grid, territory, committedOnly: true, ignoring);

    private static int MaxContribution(
        HexCoord coord, HexGrid grid, Territory territory, bool committedOnly, HexCoord? ignoring)
    {
        int max = 0;

        HexTile? tile = grid.Get(coord);
        if (tile != null && !coord.Equals(ignoring))
            max = System.Math.Max(max, ContributionAt(tile, committedOnly));

        foreach (HexCoord neighbor in coord.Neighbors())
        {
            if (neighbor.Equals(ignoring)) continue;
            if (!territory.Contains(neighbor)) continue;
            HexTile? neighborTile = grid.Get(neighbor);
            if (neighborTile == null) continue;
            max = System.Math.Max(max, ContributionAt(neighborTile, committedOnly));
        }

        return max;
    }

    /// <summary>
    /// A tile's total defense contribution: its occupant's base value plus the
    /// mountain high-ground bonus when any defender (a unit, tower, or capital —
    /// anything with a positive contribution) stands on a mountain. An empty
    /// mountain, or one holding only a tree/grave, contributes nothing. With
    /// <paramref name="committedOnly"/>, a unit that still has its move
    /// contributes nothing (and therefore earns no high-ground bonus either).
    /// </summary>
    private static int ContributionAt(HexTile tile, bool committedOnly = false)
    {
        int contribution =
            committedOnly && tile.Occupant is Unit u && !u.HasMovedThisTurn
                ? 0
                : ContributionOf(tile.Occupant);
        // Only a real defender (contribution > 0) earns the high-ground bonus;
        // an empty mountain — or one with just a tree/grave — adds nothing.
        if (tile.IsMountain && contribution > 0)
            contribution += MountainBonus;
        return contribution;
    }

    /// <summary>
    /// How much defense a single occupant contributes to its own tile (and,
    /// via radiation, to adjacent same-territory tiles).
    /// </summary>
    public static int ContributionOf(HexOccupant? occupant) => occupant switch
    {
        null => 0,
        Unit u => (int)u.Level,
        Tower => 2,
        Capital => 1,
        Tree => 0,
        Grave => 0,
        _ => throw new System.InvalidOperationException(
            $"Unknown HexOccupant subtype: {occupant.GetType().Name}"),
    };

    /// <summary>
    /// Coords of every occupant in <paramref name="targetTerritory"/> whose
    /// contribution is at least <paramref name="attackerLevel"/> — i.e. the
    /// defenders that actually block the would-be attacker. Considers the
    /// target tile's own occupant plus every adjacent same-territory tile.
    /// Returns empty when nothing blocks (open path, target not in a
    /// defending territory, etc.). Used by the view layer to red-flash
    /// only the relevant defenders on a rejected placement/movement.
    /// </summary>
    public static IEnumerable<HexCoord> BlockingDefenders(
        HexCoord target, UnitLevel attackerLevel, HexGrid grid, Territory targetTerritory)
    {
        int threshold = (int)attackerLevel;

        HexTile? targetTile = grid.Get(target);
        if (targetTile != null
            && targetTerritory.Contains(target)
            && ContributionAt(targetTile) >= threshold)
        {
            yield return target;
        }

        foreach (HexCoord neighbor in target.Neighbors())
        {
            if (!targetTerritory.Contains(neighbor)) continue;
            HexTile? neighborTile = grid.Get(neighbor);
            if (neighborTile == null) continue;
            if (ContributionAt(neighborTile) >= threshold)
            {
                yield return neighbor;
            }
        }
    }
}
