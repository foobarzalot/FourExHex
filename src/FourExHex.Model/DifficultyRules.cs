// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
/// <summary>
/// The single place the <see cref="Difficulty"/> level → behavior mapping
/// lives, so retuning a level is one edit. Levels are named after the unit
/// ranks: Recruit (easiest) … Commander (hardest); Soldier is the baseline.
/// Difficulty is the HUMAN player's self-imposed handicap and acts purely
/// through purchase costs (<see cref="UnitBaseCost"/> /
/// <see cref="TowerCost"/>); upkeep and income are never scaled. Godot-free
/// and integer-only (no-floats rule).
/// </summary>
public static class DifficultyRules
{
    /// <summary>
    /// Base unit purchase cost at each difficulty. A unit of tier N
    /// (Recruit 1 … Commander 4) costs base × N — see
    /// <see cref="PurchaseRules.CostFor"/> — so the Soldier base of 10
    /// reproduces the classic 10/20/30/40 ladder the AIs always pay.
    /// The bases step by exactly one gold per difficulty (9/10/11/12) so
    /// stepping up a level is a nudge, not a swing; the tier ladder still
    /// amplifies the step (a Commander-difficulty Commander pays 48 vs the
    /// baseline 40).
    /// </summary>
    public static int UnitBaseCost(Difficulty difficulty) => difficulty switch
    {
        Difficulty.Recruit => 9,
        Difficulty.Captain => 11,
        Difficulty.Commander => 12,
        _ => 10, // Soldier baseline
    };

    /// <summary>
    /// Tower cost at each difficulty (single value, no tier ladder) —
    /// one-gold steps (14/15/16/17). Soldier = 15, the baseline the AIs
    /// always pay.
    /// </summary>
    public static int TowerCost(Difficulty difficulty) => difficulty switch
    {
        Difficulty.Recruit => 14,
        Difficulty.Captain => 16,
        Difficulty.Commander => 17,
        _ => 15, // Soldier baseline
    };
}
