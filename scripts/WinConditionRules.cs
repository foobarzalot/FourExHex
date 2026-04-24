using System.Collections.Generic;
using Godot;

/// <summary>
/// Pure rules for detecting game-end conditions.
///   - <see cref="Winner(HexGrid, IReadOnlyList{Territory})"/>:
///     returns the sole color whose player still has a
///     capital-bearing territory, or null otherwise. This is the
///     canonical win check — orphaned tiles (singletons / no-capital
///     fragments left behind by a collapsed enemy) can't fight back,
///     so the game is decided the moment one player is the only one
///     with any live territory.
///   - <see cref="Winner(HexGrid)"/>: legacy stricter check — "one
///     color owns EVERY tile." Kept for the existing unit tests and
///     as a hard backstop; the live game uses the territory-aware
///     overload.
///   - <see cref="IsEliminated"/>: is a given player's color absent
///     from the grid entirely?
/// Called by <see cref="GameController"/> after every capture to
/// check if the game is over.
/// </summary>
public static class WinConditionRules
{
    /// <summary>
    /// Territory-aware winner check. Returns the sole color whose
    /// player owns at least one capital-bearing territory when no
    /// other player does. If two or more players have live
    /// territories, or none do, returns null. This is the correct
    /// game-end criterion: orphaned tiles (colored but part of a
    /// no-capital fragment) retain their color forever after the
    /// owning player's economy collapses, so waiting for a
    /// sole-color board never terminates a decided game.
    /// </summary>
    public static Color? Winner(HexGrid grid, IReadOnlyList<Territory> territories)
    {
        Color? only = null;
        foreach (Territory t in territories)
        {
            if (!t.HasCapital) continue;
            if (only == null) { only = t.Owner; continue; }
            if (t.Owner != only.Value) return null;
        }
        return only;
    }

    /// <summary>
    /// Legacy strict winner check: returns the sole color only if
    /// it owns every tile on the grid. The live game prefers the
    /// territory-aware overload; this one survives for its existing
    /// unit tests and as a defensive backstop.
    /// </summary>
    public static Color? Winner(HexGrid grid)
    {
        Color? only = null;
        foreach (HexTile tile in grid.Tiles)
        {
            if (only == null)
            {
                only = tile.Color;
                continue;
            }
            if (tile.Color != only.Value)
            {
                return null;
            }
        }
        return only;
    }

    /// <summary>
    /// True if <paramref name="color"/> owns zero tiles on the grid.
    /// Used by turn rotation to skip players who've been wiped out.
    /// </summary>
    public static bool IsEliminated(Color color, HexGrid grid)
    {
        foreach (HexTile tile in grid.Tiles)
        {
            if (tile.Color == color) return false;
        }
        return true;
    }
}
