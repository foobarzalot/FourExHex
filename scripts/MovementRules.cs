using System.Collections.Generic;

/// <summary>
/// Pure rules for unit movement, capture, and combining. A unit in
/// territory T can:
///   1. Reposition to any empty non-capital tile within T.
///   2. Combine with any friendly unit in T whose level sum with the
///      source is at most <see cref="UnitLevel.Baron"/>.
///   3. Capture any tile adjacent to T (different color) whose defense
///      is strictly less than the attacker's level.
/// Captures consume the unit's action. Repositions do not. Combining
/// inherits the destination's <see cref="Unit.HasMovedThisTurn"/> flag.
/// </summary>
public static class MovementRules
{
    /// <summary>
    /// Returns every coord a level-<paramref name="attackerLevel"/> unit
    /// in <paramref name="attackerTerritory"/> could legally move to.
    /// The <paramref name="allTerritories"/> list is used to determine
    /// which territory each neighbor coord belongs to (needed for defense
    /// radiation).
    /// </summary>
    public static List<HexCoord> ValidTargets(
        UnitLevel attackerLevel,
        Territory attackerTerritory,
        HexGrid grid,
        IReadOnlyList<Territory> allTerritories)
    {
        var results = new List<HexCoord>();
        var own = new HashSet<HexCoord>(attackerTerritory.Coords);

        // tile -> territory lookup, for computing radiated defense on
        // potential capture targets.
        Dictionary<HexCoord, Territory> tileToTerritory = allTerritories.BuildTileIndex();

        // 1. Repositions inside own territory: empty tiles, graves, or
        //    trees. Graves don't block placement (a unit buries them);
        //    trees don't block placement either but clearing a tree
        //    consumes the unit's action (handled in ResolveArrival).
        // 2. Combine targets: own-territory tiles whose occupant is a
        //    Unit the attacker can merge with.
        foreach (HexCoord coord in own)
        {
            HexTile? tile = grid.Get(coord);
            if (tile == null) continue;

            if (tile.Occupant == null || tile.Occupant is Grave || tile.Occupant is Tree)
            {
                results.Add(coord);
                continue;
            }

            if (tile.Occupant is Unit ownUnit
                && attackerLevel.CanCombineWith(ownUnit.Level))
            {
                results.Add(coord);
            }
            // Capital occupants are skipped — can't land on your own capital.
        }

        // 3. Captures: each distinct tile adjacent to our territory.
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
                if (defense < (int)attackerLevel)
                {
                    results.Add(neighborCoord);
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Execute a move, combine, or capture. Preconditions: source tile
    /// has a unit, destination is in the result of
    /// <see cref="ValidTargets"/>.
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
        return ResolveArrival(unit, dstTile, attackerTerritory);
    }

    /// <summary>
    /// Place a newly created unit onto <paramref name="destination"/>,
    /// used by the buy-and-place flow. Goes through the same
    /// combine/capture branches as <see cref="Move"/> so a freshly bought
    /// peasant can combine into a friendly unit or capture an enemy tile.
    /// </summary>
    public static MoveResult PlaceNew(
        Unit unit,
        HexCoord destination,
        HexGrid grid,
        Territory attackerTerritory)
    {
        HexTile dstTile = grid.Get(destination)!;
        return ResolveArrival(unit, dstTile, attackerTerritory);
    }

    /// <summary>
    /// Shared logic for "a unit arrives at a destination tile". Handles
    /// three cases:
    ///   - Combine: destination has a friendly unit → produce a higher
    ///     level unit inheriting the destination's HasMovedThisTurn.
    ///   - Reposition: destination is empty same-color → drop the unit
    ///     there; action not consumed.
    ///   - Capture: destination is different color → transfer ownership,
    ///     drop the unit, mark it as moved.
    /// Clearing a tree (same-color destination occupied by a Tree) also
    /// consumes the unit's action — chopping wood is a turn's work.
    /// Burying a grave does not.
    /// </summary>
    private static MoveResult ResolveArrival(
        Unit arrivingUnit,
        HexTile dstTile,
        Territory attackerTerritory)
    {
        // Case: combine with a friendly unit already at destination.
        if (dstTile.Color == attackerTerritory.Owner
            && dstTile.Occupant is Unit destUnit)
        {
            var combined = new Unit(
                attackerTerritory.Owner,
                arrivingUnit.Level.CombinedWith(destUnit.Level))
            {
                // The combined unit inherits the destination's move
                // state: combining into a still-actionable unit leaves
                // the combined unit still actionable; combining into an
                // already-moved unit leaves the combined unit moved.
                HasMovedThisTurn = destUnit.HasMovedThisTurn,
            };
            dstTile.Occupant = combined;
            return new MoveResult(wasCapture: false);
        }

        // Case: normal reposition, tree clearing, or capture.
        bool wasCapture = dstTile.Color != attackerTerritory.Owner;
        bool clearedTree = !wasCapture && dstTile.Occupant is Tree;
        if (wasCapture)
        {
            dstTile.Color = attackerTerritory.Owner;
        }
        dstTile.Occupant = arrivingUnit;
        // Captures and tree destruction consume the unit's single action
        // per turn. Empty repositions and grave burial don't.
        if (wasCapture || clearedTree)
        {
            arrivingUnit.HasMovedThisTurn = true;
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
