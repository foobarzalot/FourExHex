using System.Collections.Generic;

/// <summary>
/// The single place the <see cref="Difficulty"/> level → behavior mapping
/// lives, so the "for now Hard is 2×, Brutal is 3×" tuning is one edit.
/// Godot-free and integer-only (no-floats rule).
/// </summary>
public static class DifficultyRules
{
    /// <summary>
    /// Scale a base per-turn income by a player's difficulty, expressed as an
    /// integer percent so levels can be tuned finely near 1× (income bonuses
    /// compound — gold buys units, units take tiles, tiles raise income — so
    /// even 1.5× proved far too punishing in playtesting). Easy = 50%,
    /// Normal = 100%, Hard = 120%, Brutal = 140%. All integer math
    /// (no-floats rule), truncating: small territories see little or no
    /// bonus (at 120%, a bonus only appears from 5 income tiles up), which
    /// softens the early game. Tuning a level is a one-integer edit here.
    /// </summary>
    public static int ScaleIncome(int baseIncome, Difficulty difficulty)
    {
        int percent = difficulty switch
        {
            Difficulty.Easy => 50,
            Difficulty.Hard => 120,
            Difficulty.Brutal => 140,
            _ => 100, // Normal
        };
        return baseIncome * percent / 100; // integer division truncates
    }

    /// <summary>
    /// Map a single global difficulty onto a roster: every computer slot
    /// gets <paramref name="global"/>; human slots stay <see cref="Difficulty.Normal"/>
    /// (difficulty only affects AI income). Used by the New Game panel, which
    /// exposes one global control over per-slot storage.
    /// </summary>
    public static Difficulty[] AssignGlobalToAi(IReadOnlyList<PlayerKind> kinds, Difficulty global)
    {
        var result = new Difficulty[kinds.Count];
        for (int i = 0; i < kinds.Count; i++)
        {
            result[i] = kinds[i] == PlayerKind.Computer ? global : Difficulty.Normal;
        }
        return result;
    }
}
