using System.Collections.Generic;

/// <summary>
/// Pure rules for unit movement and capture. A unit in territory T can:
///   1. Reposition to any empty tile within T (can't land on a capital).
///   2. Capture any tile adjacent to T (different color) whose defense is
///      strictly less than the attacker's level.
/// Both kinds of action consume the unit's single movement for the turn.
/// </summary>
public static class MovementRules
{
    /// <summary>
    /// Returns every coord a peasant in <paramref name="attackerTerritory"/>
    /// could legally move to. The <paramref name="allTerritories"/> list is
    /// used to determine which territory each neighbor coord belongs to
    /// (needed for defense radiation). For now all attackers are peasants
    /// (level 1), so capturable enemy tiles are those with defense 0.
    /// </summary>
    public static List<HexCoord> ValidTargets(
        Territory attackerTerritory,
        HexGrid grid,
        IReadOnlyList<Territory> allTerritories)
    {
        const int attackerLevel = 1;

        var results = new List<HexCoord>();
        var own = new HashSet<HexCoord>(attackerTerritory.Coords);

        // tile -> territory lookup, for computing radiated defense on
        // potential capture targets.
        Dictionary<HexCoord, Territory> tileToTerritory = allTerritories.BuildTileIndex();

        // 1. Repositions inside the own territory: any empty tile. (An empty
        //    tile means Occupant == null, which excludes capitals and units
        //    alike.)
        foreach (HexCoord coord in own)
        {
            HexTile? tile = grid.Get(coord);
            if (tile == null) continue;
            if (tile.Occupant != null) continue;
            results.Add(coord);
        }

        // 2. Captures: each distinct tile adjacent to our territory.
        var considered = new HashSet<HexCoord>();
        foreach (HexCoord coord in own)
        {
            foreach (HexCoord neighborCoord in coord.Neighbors())
            {
                if (own.Contains(neighborCoord)) continue;
                if (!considered.Add(neighborCoord)) continue;

                HexTile? tile = grid.Get(neighborCoord);
                if (tile == null) continue;

                // Can't capture your own color (sibling territories are
                // off-limits; repositions would have been caught above).
                if (tile.Color == attackerTerritory.Owner) continue;

                if (!tileToTerritory.TryGetValue(neighborCoord, out Territory? targetTerritory))
                {
                    continue;
                }

                int defense = DefenseRules.Defense(neighborCoord, grid, targetTerritory);
                if (defense < attackerLevel)
                {
                    results.Add(neighborCoord);
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Execute a move or capture. Preconditions: source tile has a unit,
    /// destination is in the result of <see cref="ValidTargets"/> for that
    /// unit's territory and level.
    /// </summary>
    public static MoveResult Move(
        HexCoord source,
        HexCoord destination,
        HexGrid grid,
        Territory attackerTerritory)
    {
        HexTile srcTile = grid.Get(source)!;
        HexTile dstTile = grid.Get(destination)!;
        Unit unit = (Unit)srcTile.Occupant!;

        srcTile.Occupant = null;

        bool wasCapture = dstTile.Color != attackerTerritory.Owner;
        if (wasCapture)
        {
            dstTile.Color = attackerTerritory.Owner;
        }
        dstTile.Occupant = unit;
        // Only captures (and, later, tree/grave destruction) consume the
        // unit's single action per turn. Repositioning within own
        // territory leaves the unit free to act again.
        if (wasCapture)
        {
            unit.HasMovedThisTurn = true;
        }

        return new MoveResult(wasCapture);
    }

    /// <summary>
    /// Place a newly created unit directly onto <paramref name="destination"/>
    /// (as if it had moved there this turn). Used by the buy-and-place flow.
    /// </summary>
    public static MoveResult PlaceNew(
        Unit unit,
        HexCoord destination,
        HexGrid grid,
        Territory attackerTerritory)
    {
        HexTile dstTile = grid.Get(destination)!;

        bool wasCapture = dstTile.Color != attackerTerritory.Owner;
        if (wasCapture)
        {
            dstTile.Color = attackerTerritory.Owner;
        }
        dstTile.Occupant = unit;
        if (wasCapture)
        {
            unit.HasMovedThisTurn = true;
        }

        return new MoveResult(wasCapture);
    }
}

public readonly struct MoveResult
{
    public bool WasCapture { get; }

    public MoveResult(bool wasCapture)
    {
        WasCapture = wasCapture;
    }
}
