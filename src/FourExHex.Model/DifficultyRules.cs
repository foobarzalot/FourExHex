using System.Collections.Generic;

/// <summary>
/// The single place the <see cref="Difficulty"/> level → behavior mapping
/// lives, so retuning a level is one edit. Levels are named after the unit
/// ranks: Recruit (easiest) … Commander (hardest); Soldier is the baseline.
/// Difficulty acts purely through unit upkeep (income is never scaled).
/// Godot-free and integer-only (no-floats rule).
/// </summary>
public static class DifficultyRules
{
    /// <summary>
    /// Upkeep table: gold per turn a unit of <paramref name="level"/> costs
    /// when owned by a player at <paramref name="difficulty"/>. Explicit
    /// hand-picked integers (no percent formula, no truncation arithmetic).
    /// Difficulty is the HUMAN player's self-imposed handicap — AI opponents
    /// always play the Soldier column (2/6/18/54, per Slay). Recruit makes
    /// the human's armies cheaper than the AIs' (easy mode); Captain ≈ ×1.25
    /// (hand-rounded) and Commander = ×1.5 make them more expensive. Upkeep
    /// (not income) is the lever because it engages from turn 1 and scales
    /// with army size rather than land, instead of compounding like the
    /// income lever did.
    /// </summary>
    public static int UnitUpkeep(UnitLevel level, Difficulty difficulty) => (level, difficulty) switch
    {
        (UnitLevel.Recruit, Difficulty.Recruit) => 1,
        (UnitLevel.Recruit, Difficulty.Captain) => 3,
        (UnitLevel.Recruit, Difficulty.Commander) => 3,
        (UnitLevel.Recruit, _) => 2,

        (UnitLevel.Soldier, Difficulty.Recruit) => 4,
        (UnitLevel.Soldier, Difficulty.Captain) => 8,
        (UnitLevel.Soldier, Difficulty.Commander) => 9,
        (UnitLevel.Soldier, _) => 6,

        (UnitLevel.Captain, Difficulty.Recruit) => 13,
        (UnitLevel.Captain, Difficulty.Captain) => 23,
        (UnitLevel.Captain, Difficulty.Commander) => 27,
        (UnitLevel.Captain, _) => 18,

        (UnitLevel.Commander, Difficulty.Recruit) => 40,
        (UnitLevel.Commander, Difficulty.Captain) => 68,
        (UnitLevel.Commander, Difficulty.Commander) => 81,
        (UnitLevel.Commander, _) => 54,

        _ => 0,
    };

    /// <summary>
    /// Map a single global difficulty onto a roster: every human slot gets
    /// <paramref name="global"/> — difficulty is the human player's
    /// self-imposed handicap — while computer slots stay at
    /// <see cref="Difficulty.Soldier"/> (the normal baseline). Used by the
    /// New Game panel, which exposes one global control over per-slot storage.
    /// </summary>
    public static Difficulty[] AssignGlobalToHumans(IReadOnlyList<PlayerKind> kinds, Difficulty global)
    {
        var result = new Difficulty[kinds.Count];
        for (int i = 0; i < kinds.Count; i++)
        {
            result[i] = kinds[i] == PlayerKind.Human ? global : Difficulty.Soldier;
        }
        return result;
    }
}
