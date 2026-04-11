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
///   4. Tree-occupied tile (stomps the tree — last resort)
///
/// Existing <see cref="Capital"/> occupants are never considered.
/// Within each tier the placer picks the lex-min (R, Q) coord so the
/// choice is deterministic across runs.
/// </summary>
public static class CapitalPlacer
{
    /// <summary>
    /// Returns the coord where a new capital should be placed, or null if
    /// the territory is too small (&lt; 2) to deserve one. A
    /// non-null return is guaranteed whenever <paramref name="coords"/>
    /// has &gt;= 2 entries that are Empty/Unit/Grave/Tree — only an
    /// all-Capital territory (impossible in normal play) returns null.
    /// </summary>
    public static HexCoord? Choose(IReadOnlyCollection<HexCoord> coords, HexGrid grid)
    {
        if (coords.Count < 2) return null;

        HexCoord? bestEmpty = null;
        HexCoord? bestUnit = null;
        HexCoord? bestGrave = null;
        HexCoord? bestTree = null;

        foreach (HexCoord c in coords)
        {
            HexTile? tile = grid.Get(c);
            if (tile == null) continue;

            if (tile.Occupant == null)
            {
                if (!bestEmpty.HasValue || c.CompareTo(bestEmpty.Value) < 0)
                {
                    bestEmpty = c;
                }
            }
            else if (tile.Occupant is Unit)
            {
                if (!bestUnit.HasValue || c.CompareTo(bestUnit.Value) < 0)
                {
                    bestUnit = c;
                }
            }
            else if (tile.Occupant is Grave)
            {
                if (!bestGrave.HasValue || c.CompareTo(bestGrave.Value) < 0)
                {
                    bestGrave = c;
                }
            }
            else if (tile.Occupant is Tree)
            {
                if (!bestTree.HasValue || c.CompareTo(bestTree.Value) < 0)
                {
                    bestTree = c;
                }
            }
            // Existing Capital occupants are ignored — placing a capital
            // on top of an existing one is never useful.
        }

        return bestEmpty ?? bestUnit ?? bestGrave ?? bestTree;
    }
}
