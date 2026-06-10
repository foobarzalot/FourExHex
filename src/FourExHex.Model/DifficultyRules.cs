using System.Collections.Generic;

/// <summary>
/// The single place the <see cref="Difficulty"/> level → behavior mapping
/// lives, so retuning a level's percent is one edit. Levels are named after
/// the unit ranks: Recruit (easiest) … Commander (hardest); Soldier is the
/// 100% default.
/// Godot-free and integer-only (no-floats rule).
/// </summary>
public static class DifficultyRules
{
    /// <summary>
    /// Scale a base per-turn income by a player's difficulty, expressed as an
    /// integer percent. Recruit = 50% (the easy-level handicap); everything
    /// else is flat 100% — hard levels are driven by the cheaper-upkeep table
    /// (<see cref="UnitUpkeep"/>) instead, because income bonuses compound
    /// (gold buys units, units take tiles, tiles raise income) and proved
    /// knife-edged to tune. All integer math (no-floats rule), truncating.
    /// </summary>
    public static int ScaleIncome(int baseIncome, Difficulty difficulty)
    {
        int percent = difficulty switch
        {
            Difficulty.Recruit => 50,
            _ => 100, // Soldier baseline; Captain/Commander use the upkeep lever
        };
        return baseIncome * percent / 100; // integer division truncates
    }

    /// <summary>
    /// Upkeep table: gold per turn a unit of <paramref name="level"/> costs
    /// when owned by a player at <paramref name="difficulty"/>. Explicit
    /// hand-picked integers (no percent formula, no truncation arithmetic).
    /// The Soldier column is the baseline (2/6/18/54, per Slay) — humans are
    /// always Soldier, so human economics never change. Captain/Commander
    /// difficulties pay roughly 3/4 and 1/2: cheaper armies are the sole
    /// hard-level lever (income is flat 100% above Recruit), chosen because
    /// upkeep relief engages from turn 1, scales with army size rather than
    /// land, and counters the bankruptcy doom-spiral (#22) instead of
    /// compounding like the income lever did.
    /// </summary>
    public static int UnitUpkeep(UnitLevel level, Difficulty difficulty) => (level, difficulty) switch
    {
        (UnitLevel.Recruit, Difficulty.Captain) => 1,
        (UnitLevel.Recruit, Difficulty.Commander) => 1,
        (UnitLevel.Recruit, _) => 2,

        (UnitLevel.Soldier, Difficulty.Captain) => 4,
        (UnitLevel.Soldier, Difficulty.Commander) => 3,
        (UnitLevel.Soldier, _) => 6,

        (UnitLevel.Captain, Difficulty.Captain) => 13,
        (UnitLevel.Captain, Difficulty.Commander) => 9,
        (UnitLevel.Captain, _) => 18,

        (UnitLevel.Commander, Difficulty.Captain) => 40,
        (UnitLevel.Commander, Difficulty.Commander) => 27,
        (UnitLevel.Commander, _) => 54,

        _ => 0,
    };

    /// <summary>
    /// Map a single global difficulty onto a roster: every computer slot
    /// gets <paramref name="global"/>; human slots stay <see cref="Difficulty.Soldier"/>
    /// (difficulty only affects AI income). Used by the New Game panel, which
    /// exposes one global control over per-slot storage.
    /// </summary>
    public static Difficulty[] AssignGlobalToAi(IReadOnlyList<PlayerKind> kinds, Difficulty global)
    {
        var result = new Difficulty[kinds.Count];
        for (int i = 0; i < kinds.Count; i++)
        {
            result[i] = kinds[i] == PlayerKind.Computer ? global : Difficulty.Soldier;
        }
        return result;
    }
}
