// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot

/// <summary>
/// The do-nothing <see cref="IAchievementStore"/> — nothing is ever
/// unlocked and every write is dropped.
///
/// This is the default a <see cref="GameController"/> gets when no store is
/// injected, which is what keeps the level-design playtest harness, the
/// tutorial builder, headless diagnostic runs, and the whole test suite
/// structurally inert: they never touch achievement state because the type
/// that could write it was never constructed.
/// </summary>
public sealed class NullAchievementStore : IAchievementStore
{
    public static readonly NullAchievementStore Instance = new();

    private NullAchievementStore()
    {
    }

    public bool IsUnlocked(string id) => false;

    public int ProgressFor(string id) => 0;

    public void ReportProgress(string id, int current, int target)
    {
    }

    public void Unlock(string id)
    {
    }
}
