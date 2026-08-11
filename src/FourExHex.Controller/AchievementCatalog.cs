// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;

/// <summary>Something observable that can advance an achievement. Each
/// concrete event is raised from exactly one guarded site on the live
/// human path; the facts it carries are what catalog predicates match
/// on, so adding an achievement never adds a new raise site unless it
/// needs a genuinely new observation.</summary>
public abstract record AchievementEvent;

/// <summary>
/// The end of an untainted game — raised exactly once per game whether
/// the human won, lost, or the game reached stasis, so achievements for
/// mechanic milestones (not just victories) can advance. Facts about the
/// winner are null/zero when no human seat won.
/// </summary>
public sealed record GameEndEvent : AchievementEvent
{
    /// <summary>True when the winner resolved to a human seat.</summary>
    public bool HumanWon { get; init; }

    public GameMode Mode { get; init; } = GameMode.Freeform;

    /// <summary>The winning human seat's difficulty; null when no human won.</summary>
    public Difficulty? WinnerDifficulty { get; init; }

    /// <summary>True when the win came from the claim-victory prompt rather
    /// than outright elimination.</summary>
    public bool WonByClaim { get; init; }

    /// <summary>Round counter at game end (one increment per full seat rotation).</summary>
    public int TurnNumber { get; init; }

    /// <summary>Grid tiles still on the board — shrinks under Rising Tides.</summary>
    public int LandTilesRemaining { get; init; }

    /// <summary>Units the winning human seat lost this game; 0 when no human won.</summary>
    public int WinnerUnitsLost { get; init; }

    /// <summary>Towers the winning human seat built this game; 0 when no human won.</summary>
    public int WinnerTowersBuilt { get; init; }

    /// <summary>Viking units destroyed by human seats this game.</summary>
    public int VikingKills { get; init; }

    /// <summary>Highest unit level any human seat fielded this game
    /// (<see cref="UnitLevel"/> numeric value; 0 when none).</summary>
    public int MaxHumanUnitLevel { get; init; }
}

/// <summary>
/// A campaign level flipped to Won for the first time (the
/// <c>CampaignProgress.MarkWon</c> "changed" result — replay-idempotent by
/// construction). Carries the ladder counts so tier and completion
/// predicates need no reach-back into campaign storage.
/// </summary>
public sealed record CampaignLevelWonEvent(
    int Level,
    int WonCount,
    int TierIndex,
    int TierWonCount) : AchievementEvent;

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
            Advance: e => e is GameEndEvent g && g.HumanWon ? 1 : 0),
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
