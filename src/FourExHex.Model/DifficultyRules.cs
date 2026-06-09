using System.Collections.Generic;

/// <summary>
/// The single place the <see cref="Difficulty"/> level → behavior mapping
/// lives, so the "for now Hard is 2×, Brutal is 3×" tuning is one edit.
/// Godot-free and integer-only (no-floats rule).
/// </summary>
public static class DifficultyRules
{
    /// <summary>
    /// Scale a base per-turn income by a player's difficulty, all integer
    /// (no-floats rule) and truncating. Easy halves (⌊income/2⌋); Normal is
    /// unchanged; Hard is 1.5× (×3 then ÷2, so it truncates); Brutal doubles.
    /// </summary>
    public static int ScaleIncome(int baseIncome, Difficulty difficulty) => difficulty switch
    {
        Difficulty.Easy => baseIncome / 2,        // integer division truncates
        Difficulty.Hard => baseIncome * 3 / 2,    // 1.5× — multiply first, then divide
        Difficulty.Brutal => baseIncome * 2,
        _ => baseIncome,                          // Normal
    };

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
