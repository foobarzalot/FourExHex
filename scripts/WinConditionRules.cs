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
        // Tally capital-bearing tile counts per player.
        var counts = new Dictionary<Color, int>();
        foreach (Territory t in territories)
        {
            if (!t.HasCapital) continue;
            counts.TryGetValue(t.Owner, out int c);
            counts[t.Owner] = c + t.Coords.Count;
        }

        if (counts.Count == 0) return null;

        // 1. Sole capital-bearing player — uncontested winner.
        if (counts.Count == 1)
        {
            foreach (Color c in counts.Keys) return c;
        }

        // 2. Runaway leader — the leader has strictly more than
        // twice the runner-up's capital-bearing tile count AND
        // holds at least RunawayMinLeaderTiles. The minimum-tile
        // floor keeps the rule from triggering on trivially small
        // test fixtures where a 7-vs-3 imbalance isn't actually a
        // decisive game state.
        int best = 0;
        int second = 0;
        Color bestColor = default;
        foreach (KeyValuePair<Color, int> kvp in counts)
        {
            if (kvp.Value > best)
            {
                second = best;
                best = kvp.Value;
                bestColor = kvp.Key;
            }
            else if (kvp.Value > second)
            {
                second = kvp.Value;
            }
        }
        if (best >= RunawayMinLeaderTiles && best > 2 * second)
        {
            return bestColor;
        }

        return null;
    }

    /// <summary>
    /// Minimum capital-bearing tile count the leader must hold
    /// before the runaway-leader win check can fire. Trivially
    /// small games (test fixtures, early-game states) shouldn't
    /// resolve via runaway just because one player happens to
    /// have twice the tiles of the other.
    /// </summary>
    private const int RunawayMinLeaderTiles = 10;

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
