// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;

/// <summary>
/// Category grouping for the achievements panel: the catalog partitioned
/// into titled sections, categories in enum (display) order, rows in
/// catalog order within each. Lives in Controller so the grouping is
/// unit-tested; the Godot panel only renders what this returns.
/// </summary>
public static class AchievementPanelLayout
{
    public sealed record Group(
        AchievementCategory Category,
        string TitleKey,
        IReadOnlyList<AchievementDefinition> Rows);

    /// <summary>Section header key for <paramref name="category"/>.</summary>
    public static string TitleKeyFor(AchievementCategory category) => category switch
    {
        AchievementCategory.Victory => StringKeys.AchieveCategoryVictory,
        AchievementCategory.Campaign => StringKeys.AchieveCategoryCampaign,
        AchievementCategory.Modes => StringKeys.AchieveCategoryModes,
        AchievementCategory.Skill => StringKeys.AchieveCategorySkill,
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
    };

    /// <summary>The panel's sections. Empty categories are omitted.</summary>
    public static IReadOnlyList<Group> Groups()
    {
        var groups = new List<Group>();
        foreach (AchievementCategory category in Enum.GetValues<AchievementCategory>())
        {
            var rows = new List<AchievementDefinition>();
            foreach (AchievementDefinition def in AchievementCatalog.All)
            {
                if (def.Category == category) rows.Add(def);
            }
            if (rows.Count > 0)
            {
                groups.Add(new Group(category, TitleKeyFor(category), rows));
            }
        }
        return groups;
    }
}
