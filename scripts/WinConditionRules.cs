using System.Collections.Generic;
using Godot;

/// <summary>
/// Pure rules for detecting game-end conditions. Two distinct checks:
///   - <see cref="WinnerByDomination"/>: mid-turn check. Returns the
///     sole color iff one color owns every tile on the grid. Used by
///     <see cref="GameController"/> after every capture so a sweep of
///     the board ends the game immediately.
///   - <see cref="WinnerAtEndOfTurn"/>: end-of-turn check. Returns the
///     ending player's color iff they are the only player with a
///     capital-bearing territory (≥2 adjacent same-color cells).
///     Orphan singletons of other colors don't keep the game alive.
///   - <see cref="IsEliminated"/>: does a given player have no
///     capital-bearing territory (and thus nothing they can act on)?
/// </summary>
public static class WinConditionRules
{
    /// <summary>
    /// Mid-turn winner check: returns the sole color iff it owns
    /// every tile on the grid. Strictest check — any non-current-color
    /// tile (even an orphan singleton) blocks declaring a winner.
    /// </summary>
    public static Color? WinnerByDomination(HexGrid grid)
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
    /// End-of-turn winner check: returns <paramref name="currentPlayer"/>
    /// iff that color is the only one with a capital-bearing territory.
    /// A capital-bearing territory is ≥2 adjacent same-color cells (the
    /// only kind that gets a capital — see <see cref="CapitalPlacer"/>).
    /// Returns null if any other player still has one, or if the
    /// current player themselves has none (no winner is declared in
    /// degenerate "everyone is singletons" states).
    /// </summary>
    public static Color? WinnerAtEndOfTurn(
        Color currentPlayer, IReadOnlyList<Territory> territories)
    {
        bool currentHasOne = false;
        foreach (Territory t in territories)
        {
            if (!t.HasCapital) continue;
            if (t.Owner == currentPlayer)
            {
                currentHasOne = true;
            }
            else
            {
                return null;
            }
        }
        return currentHasOne ? currentPlayer : (Color?)null;
    }

    /// <summary>
    /// True if <paramref name="color"/> has no capital-bearing
    /// territory on the grid — they own no tile holding a
    /// <see cref="Capital"/> occupant. Used by turn rotation to skip
    /// players who can't act (no capital → no income, no purchases,
    /// no upkeep, no AI candidates). Capital occupants are kept in
    /// sync with the territory list by <see cref="CapitalReconciler"/>,
    /// so this and <see cref="WinnerAtEndOfTurn"/> agree on what
    /// "still in the game" means.
    /// </summary>
    public static bool IsEliminated(Color color, HexGrid grid)
    {
        foreach (HexTile tile in grid.Tiles)
        {
            if (tile.Color == color && tile.Occupant is Capital) return false;
        }
        return true;
    }

    /// <summary>
    /// Tiers at which the End-Turn claim-victory prompt fires for a
    /// human owning strictly more than that fraction of land tiles.
    /// Each tier prompts at most once per human per game; "show only
    /// highest unseen" means a single End Turn that crosses multiple
    /// tiers shows just the topmost not-yet-dismissed one.
    /// </summary>
    public static readonly int[] ClaimVictoryThresholdsPercent = { 50, 75, 90 };

    /// <summary>
    /// True iff <paramref name="color"/> owns strictly more than
    /// <paramref name="thresholdPercent"/> percent of the tiles in
    /// <paramref name="grid"/>. Water (off-map blockers) is not counted
    /// because it isn't part of the grid. Strict ">" (not ">=") so
    /// exactly the threshold does NOT trigger.
    /// </summary>
    public static bool MeetsClaimVictoryThreshold(
        Color color, HexGrid grid, int thresholdPercent)
    {
        int owned = 0;
        int total = 0;
        foreach (HexTile tile in grid.Tiles)
        {
            total++;
            if (tile.Color == color) owned++;
        }
        return owned * 100 > total * thresholdPercent;
    }

    /// <summary>
    /// Among <see cref="ClaimVictoryThresholdsPercent"/>, return the
    /// highest tier <paramref name="color"/> meets that is strictly
    /// greater than <paramref name="highestPromptedPercent"/>, or null
    /// if none. Drives the "show only highest unseen" semantics: if a
    /// player jumps from 40% to 80% in one turn, this returns 75 (the
    /// highest unseen tier they meet), not 50.
    /// </summary>
    public static int? NextClaimVictoryThreshold(
        Color color, HexGrid grid, int highestPromptedPercent)
    {
        int owned = 0;
        int total = 0;
        foreach (HexTile tile in grid.Tiles)
        {
            total++;
            if (tile.Color == color) owned++;
        }
        int? best = null;
        foreach (int t in ClaimVictoryThresholdsPercent)
        {
            if (t <= highestPromptedPercent) continue;
            if (owned * 100 > total * t)
            {
                if (best == null || t > best.Value) best = t;
            }
        }
        return best;
    }
}
