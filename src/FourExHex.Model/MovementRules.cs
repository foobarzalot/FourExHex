using System.Collections.Generic;

/// <summary>
/// Pure rules for unit movement, capture, and combining. A unit in
/// territory T can:
///   1. Reposition to any empty non-capital tile within T.
///   2. Combine with any friendly unit in T whose level sum with the
///      source is at most <see cref="UnitLevel.Commander"/>.
///   3. Capture any tile adjacent to T (different color) whose defense
///      is strictly less than the attacker's level.
/// Captures consume the unit's action. Repositions do not. Combining
/// inherits the destination's <see cref="Unit.HasMovedThisTurn"/> flag.
/// </summary>
public static class MovementRules
{
    /// <summary>
    /// True iff <paramref name="territory"/> contains at least one unit
    /// owned by <paramref name="owner"/> whose
    /// <see cref="Unit.HasMovedThisTurn"/> is false. Cheap predicate
    /// counterpart to the controller's sorted-movable enumeration —
    /// useful for HUD "any unmoved units to cycle?" checks where the
    /// caller doesn't need the list itself.
    /// </summary>
    public static bool HasUnmovedUnitsOwnedBy(Territory territory, PlayerId owner, HexGrid grid)
    {
        foreach (HexCoord coord in territory.Coords)
        {
            Unit? unit = grid.Get(coord)?.Unit;
            if (unit != null && unit.Owner == owner && !unit.HasMovedThisTurn) return true;
        }
        return false;
    }

    /// <summary>
    /// Coords of unmoved units owned by <paramref name="owner"/> in
    /// <paramref name="territory"/>, returned in **power-then-coord
    /// order**: <see cref="UnitLevel"/> descending (Commander →
    /// Recruit), <see cref="HexCoord"/> lex ascending within each
    /// tier. The single source of truth for "consider the strongest
    /// unit first" iteration, used by both the human N-cycle (via
    /// <c>GameController.SortedMovableCoords</c>) and the AI
    /// candidate enumerator (<see cref="AiCommon.Enumerate"/>).
    ///
    /// Both call sites must agree on the order — when two units have
    /// equal-scoring moves the AI's first-wins tiebreak picks the
    /// one yielded first, and the human's N-key cycle steps through
    /// in the same order. Without this shared helper, the AI's
    /// `territory.Coords` BFS order would let a Recruit near the
    /// seed tile win ties over a Commander deeper in the territory
    /// — see issue #21.
    /// </summary>
    public static List<HexCoord> MovableUnitsInPowerOrder(
        Territory territory, PlayerId owner, HexGrid grid)
    {
        var movable = new List<(HexCoord Coord, UnitLevel Level)>();
        foreach (HexCoord coord in territory.Coords)
        {
            Unit? unit = grid.Get(coord)?.Unit;
            if (unit != null && unit.Owner == owner && !unit.HasMovedThisTurn)
            {
                movable.Add((coord, unit.Level));
            }
        }
        movable.Sort((a, b) =>
        {
            int byLevel = b.Level.CompareTo(a.Level);
            return byLevel != 0 ? byLevel : a.Coord.CompareTo(b.Coord);
        });
        var result = new List<HexCoord>(movable.Count);
        foreach ((HexCoord c, _) in movable) result.Add(c);
        return result;
    }

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
        // Capital and Tower occupants in own territory are skipped
        // entirely — you can't land a friendly unit on either.
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

                // Can't capture your own tiles (sibling territories are
                // off-limits; repositions would have been caught above).
                if (tile.Owner == attackerTerritory.Owner) continue;

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
    /// recruit can combine into a friendly unit or capture an enemy tile.
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
    /// True iff a unit arriving at <paramref name="destTile"/> from a
    /// territory owned by <paramref name="attackerTerritory"/>.Owner
    /// would consume its single per-turn action. Captures (different
    /// color), tree-clears, and grave-burials all do; empty same-color
    /// repositions and friendly combines do not. Single source of truth
    /// for the action-consumption rule, used by
    /// <see cref="ResolveArrival"/> to set HasMovedThisTurn and by the
    /// controller's preview code (GameController.ActionConsumingTargets)
    /// to ring up the matching destinations. Must be called BEFORE any
    /// mutation of <paramref name="destTile"/>.
    /// </summary>
    public static bool ArrivalConsumesAction(HexTile destTile, Territory attackerTerritory)
    {
        if (destTile.Owner != attackerTerritory.Owner) return true;
        return destTile.Occupant is Tree || destTile.Occupant is Grave;
    }

    /// <summary>
    /// Shared logic for "a unit arrives at a destination tile". Handles
    /// three cases:
    ///   - Combine: destination has a friendly unit → produce a higher
    ///     level unit inheriting the destination's HasMovedThisTurn.
    ///   - Reposition: destination is empty same-owner → drop the unit
    ///     there; action not consumed.
    ///   - Capture: destination has a different owner → transfer ownership,
    ///     drop the unit, mark it as moved.
    /// Clearing a tree OR burying a grave (same-color destination with
    /// a Tree or Grave occupant) also consumes the unit's action —
    /// both are considered a turn's work.
    /// </summary>
    private static MoveResult ResolveArrival(
        Unit arrivingUnit,
        HexTile dstTile,
        Territory attackerTerritory)
    {
        // Case: combine with a friendly unit already at destination.
        if (dstTile.Owner == attackerTerritory.Owner
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
            return new MoveResult(wasCapture: false, destroyed: null);
        }

        // Case: normal reposition, tree/grave clearing, or capture.
        // ArrivalConsumesAction must be evaluated before the mutations
        // below — they overwrite the fields it reads.
        bool consumesAction = ArrivalConsumesAction(dstTile, attackerTerritory);
        bool wasCapture = dstTile.Owner != attackerTerritory.Owner;
        HexOccupant? displaced = dstTile.Occupant;
        if (wasCapture)
        {
            dstTile.Owner = attackerTerritory.Owner;
        }
        dstTile.Occupant = arrivingUnit;
        if (consumesAction)
        {
            arrivingUnit.HasMovedThisTurn = true;
        }

        if (wasCapture && dstTile.IsMountain)
        {
            // Issue #37: capturing a mountain transfers ownership but leaves the
            // terrain intact, so the new owner's occupant earns the +1 bonus.
            Log.Debug(Log.LogCategory.Capture,
                $"[capture] mountain at {dstTile.Coord} → owner " +
                $"{attackerTerritory.Owner.Index} (terrain retained)");
        }

        return new MoveResult(wasCapture, destroyed: displaced);
    }
}

public readonly struct MoveResult
{
    public bool WasCapture { get; }

    /// <summary>
    /// The occupant displaced from the destination tile, if any. Captures
    /// onto an enemy unit or tower, and same-color moves onto a tree or
    /// grave, populate this. The view uses it to play a destruction effect
    /// at the destination before the next refresh paints the arriving unit.
    /// Null for empty captures, plain repositions, and combines.
    /// </summary>
    public HexOccupant? Destroyed { get; }

    public MoveResult(bool wasCapture, HexOccupant? destroyed)
    {
        WasCapture = wasCapture;
        Destroyed = destroyed;
    }
}
