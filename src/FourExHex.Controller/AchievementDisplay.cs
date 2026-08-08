// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot

/// <summary>
/// Which string keys the achievements panel should render for a given
/// definition. A hidden achievement shows placeholder copy until it is
/// earned; everything else always shows its real title and description.
///
/// The rule lives here rather than as an <c>if</c> in the panel so it is
/// unit-tested — no shipped achievement is hidden today, so the view never
/// exercises the masking branch.
/// </summary>
public static class AchievementDisplay
{
    public static string TitleKeyFor(AchievementDefinition def, bool unlocked) =>
        Masked(def, unlocked) ? StringKeys.AchieveHiddenTitle : def.TitleKey;

    public static string DescriptionKeyFor(AchievementDefinition def, bool unlocked) =>
        Masked(def, unlocked) ? StringKeys.AchieveHiddenDesc : def.DescriptionKey;

    private static bool Masked(AchievementDefinition def, bool unlocked) =>
        def.Hidden && !unlocked;
}
