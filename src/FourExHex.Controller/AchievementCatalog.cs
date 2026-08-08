// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;

/// <summary>Something observable that can advance an achievement. Each
/// arm is raised from exactly one guarded site on the live human path.</summary>
public enum AchievementEvent
{
    /// <summary>A game ended with a human seat as the winner.</summary>
    GameWonByHuman = 0,
}

/// <summary>Grouping for the achievements panel.</summary>
public enum AchievementCategory
{
    Victory = 0,
}

/// <summary>
/// One achievement. <paramref name="Target"/> derives the kind — 1 is a
/// boolean achievement, above 1 a counter — which is also what a platform
/// console eventually wants: Game Center takes
/// <c>current * 100 / Target</c> as a percentage, Play Games takes
/// <c>Target</c> as its incremental step count.
///
/// <paramref name="Advance"/> is the predicate: how much this event adds
/// to the achievement's progress, 0 for events it does not care about.
/// Keeping it in the row is what makes adding an achievement one table
/// entry rather than a table entry plus a branch somewhere else.
/// </summary>
public sealed record AchievementDefinition(
    string Id,
    string TitleKey,
    string DescriptionKey,
    AchievementCategory Category,
    int Target,
    bool Hidden,
    Func<AchievementEvent, int> Advance)
{
    /// <summary>True when this tracks partial progress worth showing.</summary>
    public bool IsCounter => Target > 1;
}

/// <summary>
/// Every achievement the build knows about. This table is the single list
/// a future platform-console registration is derived from, so it stays
/// mechanically enumerable rather than scattered across call sites.
///
/// Lives in Controller rather than Model because the display keys are
/// <see cref="StringKeys"/> constants and Model must never reference
/// upward. The persisted record (<c>AchievementRecord</c>) is Model and
/// deliberately knows nothing about this table — an id missing here is
/// preserved on disk, not dropped.
/// </summary>
public static class AchievementCatalog
{
    public const string Veteran = "victory.veteran";

    public static readonly IReadOnlyList<AchievementDefinition> All = new AchievementDefinition[]
    {
        new(Veteran,
            StringKeys.AchieveVeteranTitle,
            StringKeys.AchieveVeteranDesc,
            AchievementCategory.Victory,
            Target: 3,
            Hidden: false,
            Advance: e => e == AchievementEvent.GameWonByHuman ? 1 : 0),
    };

    /// <summary>The definition for <paramref name="id"/>, or null when this
    /// build does not recognize it (an id from a newer record).</summary>
    public static AchievementDefinition? ById(string id)
    {
        foreach (AchievementDefinition def in All)
        {
            if (def.Id == id) return def;
        }
        return null;
    }
}
