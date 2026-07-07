using System.Collections.Generic;

/// <summary>
/// Pure rules for detecting game-end conditions. Two distinct checks:
///   - <see cref="WinnerByDomination"/>: mid-turn check. Returns the
///     sole player iff one player owns every tile on the grid. Used by
///     <see cref="GameController"/> after every capture so a sweep of
///     the board ends the game immediately.
///   - <see cref="WinnerAtEndOfTurn"/>: end-of-turn check. Returns the
///     ending player iff they are the only player with a
///     capital-bearing territory (≥2 adjacent same-owner cells).
///     Orphan singletons of other players don't keep the game alive.
///   - <see cref="IsEliminated"/>: does a given player have no
///     capital-bearing territory (and thus nothing they can act on)?
/// </summary>
public static class WinConditionRules
{
    /// <summary>
    /// Mid-turn winner check: returns the sole player iff it owns
    /// every tile on the grid. Strictest check — any tile owned by a
    /// different player (even an orphan singleton) blocks declaring a winner.
    /// </summary>
    public static PlayerId? WinnerByDomination(HexGrid grid)
    {
        PlayerId? only = null;
        foreach (HexTile tile in grid.Tiles)
        {
            if (only == null)
            {
                only = tile.Owner;
                continue;
            }
            if (tile.Owner != only.Value)
            {
                return null;
            }
        }
        return only;
    }

    /// <summary>
    /// End-of-turn winner check: returns <paramref name="currentPlayer"/>
    /// iff that player is the only one with a capital-bearing territory.
    /// A capital-bearing territory is ≥2 adjacent same-owner cells (the
    /// only kind that gets a capital — see <see cref="CapitalPlacer"/>).
    /// Returns null if any other player still has one, or if the
    /// current player themselves has none (no winner is declared in
    /// degenerate "everyone is singletons" states).
    /// </summary>
    public static PlayerId? WinnerAtEndOfTurn(
        PlayerId currentPlayer, IReadOnlyList<Territory> territories)
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
        return currentHasOne ? currentPlayer : (PlayerId?)null;
    }

    /// <summary>
    /// True if <paramref name="player"/> has no capital-bearing
    /// territory on the grid — they own no tile holding a
    /// <see cref="Capital"/> occupant. Used by turn rotation to skip
    /// players who can't act (no capital → no income, no purchases,
    /// no upkeep, no AI candidates). Capital occupants are kept in
    /// sync with the territory list by <see cref="CapitalReconciler"/>,
    /// so this and <see cref="WinnerAtEndOfTurn"/> agree on what
    /// "still in the game" means.
    /// </summary>
    public static bool IsEliminated(PlayerId player, HexGrid grid)
    {
        foreach (HexTile tile in grid.Tiles)
        {
            if (tile.Owner == player && tile.Occupant is Capital) return false;
        }
        return true;
    }

    /// <summary>
    /// "Last player standing" winner check used by Rising Tides in place
    /// of the end-of-turn sole-capital <see cref="WinnerAtEndOfTurn"/> check.
    /// Only that one path is swapped: Rising Tides still fires the mid-turn
    /// <see cref="WinnerByDomination"/> check and the human claim-victory
    /// prompt, so this is not the mode's only way to win. Returns the sole
    /// owner that
    /// has a capital-bearing territory iff exactly one distinct owner does;
    /// null if two or more still hold a capital, or if none do (a degenerate
    /// all-singletons state — no winner is declared). Mirrors
    /// <see cref="WinnerAtEndOfTurn"/> but without the "must be the current
    /// player" clause, and agrees with <see cref="IsEliminated"/> on the
    /// capital-bearing definition of "still in the game".
    /// </summary>
    public static PlayerId? LastPlayerStanding(IReadOnlyList<Territory> territories)
    {
        PlayerId? sole = null;
        foreach (Territory t in territories)
        {
            if (!t.HasCapital) continue;
            if (sole == null)
            {
                sole = t.Owner;
            }
            else if (sole.Value != t.Owner)
            {
                return null;
            }
        }
        return sole;
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
    /// Among <see cref="ClaimVictoryThresholdsPercent"/>, return the
    /// highest tier <paramref name="player"/> meets that is strictly
    /// greater than <paramref name="highestPromptedPercent"/>, or null
    /// if none. Drives the "show only highest unseen" semantics: if a
    /// player jumps from 40% to 80% in one turn, this returns 75 (the
    /// highest unseen tier they meet), not 50.
    /// </summary>
    public static int? NextClaimVictoryThreshold(
        PlayerId player, HexGrid grid, int highestPromptedPercent)
    {
        int owned = 0;
        int total = 0;
        foreach (HexTile tile in grid.Tiles)
        {
            total++;
            if (tile.Owner == player) owned++;
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
