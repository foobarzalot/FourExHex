using System.Collections.Generic;
using Godot;

/// <summary>
/// Pure rules for detecting game-end conditions.
///   - <see cref="Winner"/>: returns the single color that owns every
///     tile, or null if more than one color (or none) is present.
///   - <see cref="IsEliminated"/>: is a given player's color absent
///     from the grid entirely?
/// Called by <see cref="GameController"/> after every capture to check
/// if the game is over. A player with only singleton territories (no
/// capital, no income) is NOT eliminated — they still own tiles and
/// must be physically captured.
/// </summary>
public static class WinConditionRules
{
    /// <summary>
    /// Returns the sole color that owns every tile on the grid, or
    /// null if two or more colors coexist (or the grid is empty).
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
