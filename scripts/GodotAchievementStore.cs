// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot

/// <summary>
/// Adapter binding the controller's <see cref="IAchievementStore"/> seam to
/// the Godot-side <see cref="AchievementStore"/>. Constructed only outside
/// diagnostic mode, which is what makes "a FOUREXHEX_6AI run writes no
/// achievement state" structural rather than a runtime check — those
/// sessions hold a <c>NullAchievementStore</c> and never touch the file.
///
/// When first-party sync lands, this is where the platform mirror hangs:
/// persist locally first (local stays authoritative), then forward the
/// same call outward.
/// </summary>
public sealed class GodotAchievementStore : IAchievementStore
{
    public bool IsUnlocked(string id) => AchievementStore.Record.IsUnlocked(id);

    public int ProgressFor(string id) => AchievementStore.Record.ProgressFor(id);

    public void ReportProgress(string id, int current, int target) =>
        AchievementStore.SetProgress(id, current);

    public void Unlock(string id) => AchievementStore.Unlock(id);
}
