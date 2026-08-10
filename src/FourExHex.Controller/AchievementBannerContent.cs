// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot

/// <summary>
/// Copy for the achievement unlock banner. Godot-free so the wording rules
/// are unit-testable; the controller hands the result to
/// <c>IHudView.ShowAchievementBanner</c>. Mirrors
/// <see cref="VikingWaveBannerContent"/>.
///
/// A hidden achievement is named in full here: unlocking is precisely the
/// moment it stops being masked.
/// </summary>
public static class AchievementBannerContent
{
    /// <summary>
    /// Banner text for a freshly unlocked achievement, or null when this
    /// build does not recognize the id (a rename miss, or an id from a
    /// newer record) — a missing definition must never crash mid-game.
    /// </summary>
    public static string? For(string id)
    {
        AchievementDefinition? def = AchievementCatalog.ById(id);
        return def == null ? null : ForDefinition(def);
    }

    /// <summary>Banner text for a known definition.</summary>
    public static string ForDefinition(AchievementDefinition def) =>
        Strings.Get(StringKeys.AchieveUnlockedBanner, ("title", Strings.Get(def.TitleKey)));
}
