using System.Collections.Generic;

/// <summary>
/// Pure-logic resolver for long-press rally gestures. Walks the
/// unmoved units in a territory, sorted closest-to-target first
/// (lex-min tiebreak so order is deterministic), and slides each
/// to the closest empty in-territory tile strictly closer to the
/// target than where it started.
///
/// Greedy by distance: the unit currently closest to the target
/// gets first pick of the closest empty cell, so a far unit can't
/// leapfrog a near one. Each call to <see cref="MovementRules.Move"/>
/// for an own-empty destination is a free reposition that leaves
/// <see cref="Unit.HasMovedThisTurn"/> false (so a human can chain
/// more actions on the same unit).
///
/// Shared between the live <c>OnTileLongClickedBody</c> handler
/// and the replay <c>ApplyLongPressRally</c> dispatch so the two
/// paths can't drift; if rally semantics change, they change in
/// exactly one place.
/// </summary>
public static class RallyRules
{
    /// <summary>
    /// Apply a rally targeting <paramref name="target"/> in
    /// <paramref name="territory"/>. Returns true iff at least one
    /// unit actually moved; callers use this to gate audio cues
    /// and undo bookkeeping.
    /// </summary>
    public static bool ResolveRally(
        HexGrid grid,
        Territory territory,
        HexCoord target,
        PlayerId color)
    {
        var unmoved = new List<HexCoord>();
        foreach (HexCoord coord in territory.Coords)
        {
            Unit? u = grid.Get(coord)?.Unit;
            if (u != null && u.Owner == color && !u.HasMovedThisTurn)
            {
                unmoved.Add(coord);
            }
        }
        if (unmoved.Count == 0) return false;

        unmoved.Sort((a, b) =>
        {
            int da = HexCoord.Distance(a, target);
            int db = HexCoord.Distance(b, target);
            int cmp = da.CompareTo(db);
            return cmp != 0 ? cmp : a.CompareTo(b);
        });

        bool anyMoved = false;
        foreach (HexCoord src in unmoved)
        {
            HexCoord? dst = FindClosestEmptyCellInTerritory(grid, territory, src, target);
            if (!dst.HasValue) continue;
            MovementRules.Move(src, dst.Value, grid, territory);
            anyMoved = true;
        }
        return anyMoved;
    }

    /// <summary>
    /// Find the empty non-occupant tile in <paramref name="territory"/>
    /// strictly closer to <paramref name="target"/> than
    /// <paramref name="src"/>'s current distance, minimizing distance
    /// to the target. Lex-min tiebreak. Returns null if no strictly
    /// closer empty cell exists — the unit stays put.
    /// </summary>
    private static HexCoord? FindClosestEmptyCellInTerritory(
        HexGrid grid, Territory territory, HexCoord src, HexCoord target)
    {
        int currentDist = HexCoord.Distance(src, target);
        HexCoord? best = null;
        int bestDist = int.MaxValue;
        foreach (HexCoord coord in territory.Coords)
        {
            if (coord.Equals(src)) continue;
            HexTile? t = grid.Get(coord);
            if (t == null || t.Occupant != null) continue;
            int d = HexCoord.Distance(coord, target);
            if (d >= currentDist) continue;
            if (best == null || d < bestDist || (d == bestDist && coord.CompareTo(best.Value) < 0))
            {
                best = coord;
                bestDist = d;
            }
        }
        return best;
    }
}
