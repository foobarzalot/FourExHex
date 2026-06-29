using System;
using System.Collections.Generic;

/// <summary>
/// Picks a coord on which to place a new <see cref="Capital"/>. The
/// invariant the rest of the game relies on: any territory with two
/// or more contiguous same-color hexes MUST end up with a capital.
/// To guarantee that even when every candidate tile is occupied, the
/// placer falls back through a tier list, stomping the cheapest
/// occupant available:
///
///   1. Empty tile (no cost)
///   2. Unit-occupied tile (stomps the unit — lost without refund)
///   3. Grave-occupied tile (stomps the grave — about to die anyway)
///   4. Tree-occupied tile (stomps the tree)
///   5. Tower-occupied tile (stomps the tower — last resort; towers
///      are a 15g investment so we destroy them only when nothing
///      else is available)
///
/// Existing <see cref="Capital"/> occupants are never considered. The
/// tier priority above always holds. WITHIN the chosen tier, the pick
/// is the lex-min (R, Q) coord when <c>rng</c> is null (the historical
/// deterministic choice), or a seed-deterministic random candidate when
/// an <c>rng</c> is supplied (see <see cref="GameState.UseRandomizedSelection"/>).
/// </summary>
public static class CapitalPlacer
{
    /// <summary>
    /// Returns the coord where a new capital should be placed, or null if
    /// the territory is too small (&lt; 2) to deserve one. A non-null return
    /// is guaranteed whenever <paramref name="coords"/> has &gt;= 2 entries
    /// that are Empty/Unit/Grave/Tree/Tower (mountains included — capitals may
    /// sit on them) — only an all-Capital territory (impossible in
    /// normal play) returns null. When <paramref name="rng"/> is non-null the
    /// in-tier choice is randomized (reproducibly, from that rng); when null it
    /// is the lex-min coord.
    /// </summary>
    public static HexCoord? Choose(
        IReadOnlyCollection<HexCoord> coords, HexGrid grid, Random? rng = null)
    {
        if (coords.Count < 2) return null;

        var empty = new List<HexCoord>();
        var units = new List<HexCoord>();
        var graves = new List<HexCoord>();
        var trees = new List<HexCoord>();
        var towers = new List<HexCoord>();

        foreach (HexCoord c in coords)
        {
            HexTile? tile = grid.Get(c);
            if (tile == null) continue;
            // Capitals may sit on mountains: a mountain tile is an
            // ordinary candidate, tiered by its occupant like any other.

            if (tile.Occupant == null) empty.Add(c);
            else if (tile.Occupant is Unit) units.Add(c);
            else if (tile.Occupant is Grave) graves.Add(c);
            else if (tile.Occupant is Tree) trees.Add(c);
            else if (tile.Occupant is Tower) towers.Add(c);
            // Existing Capital occupants are ignored — placing a capital
            // on top of an existing one is never useful.
        }

        List<HexCoord> tier =
            empty.Count > 0 ? empty :
            units.Count > 0 ? units :
            graves.Count > 0 ? graves :
            trees.Count > 0 ? trees :
            towers;
        return PickFromTier(tier, rng);
    }

    /// <summary>
    /// Pick one coord from a non-prioritized tier list: the lex-min coord
    /// when <paramref name="rng"/> is null, else a uniformly random element.
    /// The list is sorted first so the random index maps to a stable order —
    /// the draw is reproducible regardless of how <paramref name="tier"/> was
    /// enumerated. Returns null only for an empty list (the all-Capital case).
    /// </summary>
    private static HexCoord? PickFromTier(List<HexCoord> tier, Random? rng)
    {
        if (tier.Count == 0) return null;
        tier.Sort();
        return rng == null ? tier[0] : tier[rng.Next(tier.Count)];
    }
}
