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
    /// integer percent so levels can be tuned finely near 1× (income bonuses
    /// compound — gold buys units, units take tiles, tiles raise income — so
    /// even 1.5× proved far too punishing in playtesting). Recruit = 50%,
    /// Soldier = 100%, Captain = 120%, Commander = 140%. All integer math
    /// (no-floats rule), truncating: small territories see little or no
    /// bonus (at 120%, a bonus only appears from 5 income tiles up), which
    /// softens the early game. Tuning a level is a one-integer edit here.
    /// </summary>
    public static int ScaleIncome(int baseIncome, Difficulty difficulty)
    {
        int percent = difficulty switch
        {
            Difficulty.Recruit => 50,
            Difficulty.Captain => 120,
            Difficulty.Commander => 140,
            _ => 100, // Soldier (default)
        };
        return baseIncome * percent / 100; // integer division truncates
    }

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
