using System.Collections.Generic;

/// <summary>
/// Picks a coord on which to place a new <see cref="Capital"/>. Prefers
/// tiles that are currently empty; falls back to tiles occupied by a
/// <see cref="Unit"/> (the unit is stomped when the capital is placed).
/// Tiles holding other occupants (towers, trees, graves) are skipped
/// entirely — they shouldn't be destroyed by capital placement. Within
/// each preference tier, picks the lex-min (R, Q) coord for determinism.
/// </summary>
public static class CapitalPlacer
{
    /// <summary>
    /// Returns the coord where a new capital should be placed, or null if
    /// the territory is too small (&lt; 2) to deserve one or if no valid
    /// placement tile exists.
    /// </summary>
    public static HexCoord? Choose(IReadOnlyCollection<HexCoord> coords, HexGrid grid)
    {
        if (coords.Count < 2) return null;

        HexCoord? bestEmpty = null;
        HexCoord? bestUnit = null;

        foreach (HexCoord c in coords)
        {
            HexTile? tile = grid.Get(c);
            if (tile == null) continue;

            if (tile.Occupant == null)
            {
                if (!bestEmpty.HasValue || IsLessThan(c, bestEmpty.Value))
                {
                    bestEmpty = c;
                }
            }
            else if (tile.Occupant is Unit)
            {
                if (!bestUnit.HasValue || IsLessThan(c, bestUnit.Value))
                {
                    bestUnit = c;
                }
            }
            // Other occupant types (Capital, Tower, Tree, Grave) are
            // ignored — the capital won't be placed on them.
        }

        return bestEmpty ?? bestUnit;
    }

    private static bool IsLessThan(HexCoord a, HexCoord b) =>
        a.R < b.R || (a.R == b.R && a.Q < b.Q);
}
