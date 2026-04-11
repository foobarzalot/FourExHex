using System.Collections.Generic;

/// <summary>
/// Pure rules for unit movement and capture. A unit in territory T can:
///   1. Reposition to any empty non-capital tile within T.
///   2. Capture any tile adjacent to T (different color) whose defense is
///      strictly less than the attacker's level.
/// Both kinds of action consume the unit's single movement for the turn.
/// </summary>
public static class MovementRules
{
    /// <summary>
    /// Returns every coord a level-<paramref name="attackerLevel"/> unit in
    /// <paramref name="attackerTerritory"/> could legally move to. The
    /// <paramref name="allTerritories"/> list is used to identify capitals
    /// (for the defense check).
    /// </summary>
    public static List<HexCoord> ValidTargets(
        int attackerLevel,
        Territory attackerTerritory,
        HexGrid grid,
        IReadOnlyList<Territory> allTerritories)
    {
        var results = new List<HexCoord>();
        var own = new HashSet<HexCoord>(attackerTerritory.Coords);

        // Collect every coord that's a capital of ANY territory, for the
        // defense check when evaluating capture targets.
        var capitals = new HashSet<HexCoord>();
        foreach (Territory t in allTerritories)
        {
            if (t.HasCapital) capitals.Add(t.Capital!.Value);
        }

        // 1. Repositions inside the own territory: any empty non-capital tile.
        foreach (HexCoord coord in own)
        {
            if (capitals.Contains(coord)) continue;
            HexTile? tile = grid.Get(coord);
            if (tile == null || tile.Unit != null) continue;
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

                bool isCapital = capitals.Contains(neighborCoord);
                int defense = DefenseRules.Defense(tile, isCapital);
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
    /// unit's territory and level. Returns a <see cref="MoveResult"/>
    /// describing whether the move captured a tile (so the caller knows to
    /// re-run <see cref="TerritoryFinder"/> and reconcile the treasury).
    /// </summary>
    public static MoveResult Move(
        HexCoord source,
        HexCoord destination,
        HexGrid grid,
        Territory attackerTerritory)
    {
        HexTile srcTile = grid.Get(source)!;
        HexTile dstTile = grid.Get(destination)!;
        Unit unit = srcTile.Unit!;

        srcTile.Unit = null;

        bool wasCapture = dstTile.Color != attackerTerritory.Owner;
        if (wasCapture)
        {
            dstTile.Color = attackerTerritory.Owner;
        }
        dstTile.Unit = unit;
        unit.HasMovedThisTurn = true;

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
        dstTile.Unit = unit;
        unit.HasMovedThisTurn = true;

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
