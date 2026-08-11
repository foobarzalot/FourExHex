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

/// <summary>Grouping for the achievements panel, in display order.</summary>
public enum AchievementCategory
{
    Victory = 0,
    Campaign = 1,
    Modes = 2,
    Skill = 3,
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
    public const string FirstWin = "victory.first_win";
    public const string Veteran = "victory.veteran";
    public const string WarHero = "victory.war_hero";
    public const string CaptainCommission = "victory.captain";
    public const string CommanderCommission = "victory.commander";
    public const string TotalDomination = "victory.domination";
    public const string DryFeet = "mode.rising_tides";
    public const string ThroughTheMist = "mode.fog_of_war";
    public const string RaidersRepelled = "mode.vikings";
    public const string LastHill = "mode.last_hill";
    public const string VikingSlayer = "mode.viking_slayer";
    public const string Untouchable = "skill.untouchable";
    public const string OpenField = "skill.open_field";
    public const string Blitz = "skill.blitz";
    public const string ChainOfCommand = "skill.chain_of_command";

    /// <summary>The Last Hill's land-tile ceiling at game end.</summary>
    public const int LastHillLandTiles = 20;

    /// <summary>Blitz's winning-turn ceiling (round counter).</summary>
    public const int BlitzTurnLimit = 20;

    public static readonly IReadOnlyList<AchievementDefinition> All = new AchievementDefinition[]
    {
        // --- Victory ---
        new(FirstWin,
            StringKeys.AchieveFirstWinTitle,
            StringKeys.AchieveFirstWinDesc,
            AchievementCategory.Victory,
            Target: 1,
            Hidden: false,
            Advance: e => e is GameEndEvent g && g.HumanWon ? 1 : 0),
        new(Veteran,
            StringKeys.AchieveVeteranTitle,
            StringKeys.AchieveVeteranDesc,
            AchievementCategory.Victory,
            Target: 3,
            Hidden: false,
            Advance: e => e is GameEndEvent g && g.HumanWon ? 1 : 0),
        new(WarHero,
            StringKeys.AchieveWarHeroTitle,
            StringKeys.AchieveWarHeroDesc,
            AchievementCategory.Victory,
            Target: 25,
            Hidden: false,
            Advance: e => e is GameEndEvent g && g.HumanWon ? 1 : 0),
        new(CaptainCommission,
            StringKeys.AchieveCaptainTitle,
            StringKeys.AchieveCaptainDesc,
            AchievementCategory.Victory,
            Target: 1,
            Hidden: false,
            Advance: e => e is GameEndEvent { HumanWon: true, WinnerDifficulty: >= Difficulty.Captain } ? 1 : 0),
        new(CommanderCommission,
            StringKeys.AchieveCommanderTitle,
            StringKeys.AchieveCommanderDesc,
            AchievementCategory.Victory,
            Target: 1,
            Hidden: false,
            Advance: e => e is GameEndEvent { HumanWon: true, WinnerDifficulty: Difficulty.Commander } ? 1 : 0),
        new(TotalDomination,
            StringKeys.AchieveDominationTitle,
            StringKeys.AchieveDominationDesc,
            AchievementCategory.Victory,
            Target: 1,
            Hidden: false,
            Advance: e => e is GameEndEvent { HumanWon: true, WonByClaim: false } ? 1 : 0),

        // --- Modes ---
        new(DryFeet,
            StringKeys.AchieveDryFeetTitle,
            StringKeys.AchieveDryFeetDesc,
            AchievementCategory.Modes,
            Target: 1,
            Hidden: false,
            Advance: e => e is GameEndEvent { HumanWon: true, Mode: GameMode.RisingTides } ? 1 : 0),
        new(ThroughTheMist,
            StringKeys.AchieveThroughMistTitle,
            StringKeys.AchieveThroughMistDesc,
            AchievementCategory.Modes,
            Target: 1,
            Hidden: false,
            Advance: e => e is GameEndEvent { HumanWon: true, Mode: GameMode.FogOfWar } ? 1 : 0),
        new(RaidersRepelled,
            StringKeys.AchieveRaidersRepelledTitle,
            StringKeys.AchieveRaidersRepelledDesc,
            AchievementCategory.Modes,
            Target: 1,
            Hidden: false,
            Advance: e => e is GameEndEvent { HumanWon: true, Mode: GameMode.VikingRaiders } ? 1 : 0),
        new(LastHill,
            StringKeys.AchieveLastHillTitle,
            StringKeys.AchieveLastHillDesc,
            AchievementCategory.Modes,
            Target: 1,
            Hidden: false,
            Advance: e => e is GameEndEvent
            {
                HumanWon: true,
                Mode: GameMode.RisingTides,
                LandTilesRemaining: <= LastHillLandTiles,
            } ? 1 : 0),
        new(VikingSlayer,
            StringKeys.AchieveVikingSlayerTitle,
            StringKeys.AchieveVikingSlayerDesc,
            AchievementCategory.Modes,
            Target: 50,
            Hidden: false,
            // Per-game delta, win or lose — kills earned in a losing defense
            // still count toward the career total.
            Advance: e => e is GameEndEvent g ? g.VikingKills : 0),

        // --- Skill ---
        new(Untouchable,
            StringKeys.AchieveUntouchableTitle,
            StringKeys.AchieveUntouchableDesc,
            AchievementCategory.Skill,
            Target: 1,
            Hidden: false,
            Advance: e => e is GameEndEvent { HumanWon: true, WinnerUnitsLost: 0 } ? 1 : 0),
        new(OpenField,
            StringKeys.AchieveOpenFieldTitle,
            StringKeys.AchieveOpenFieldDesc,
            AchievementCategory.Skill,
            Target: 1,
            Hidden: false,
            Advance: e => e is GameEndEvent { HumanWon: true, WinnerTowersBuilt: 0 } ? 1 : 0),
        new(Blitz,
            StringKeys.AchieveBlitzTitle,
            StringKeys.AchieveBlitzDesc,
            AchievementCategory.Skill,
            Target: 1,
            Hidden: false,
            Advance: e => e is GameEndEvent { HumanWon: true, TurnNumber: <= BlitzTurnLimit } ? 1 : 0),
        new(ChainOfCommand,
            StringKeys.AchieveChainOfCommandTitle,
            StringKeys.AchieveChainOfCommandDesc,
            AchievementCategory.Skill,
            Target: 1,
            Hidden: false,
            // Win or lose — a mechanic milestone, not a victory.
            Advance: e => e is GameEndEvent { MaxHumanUnitLevel: >= (int)UnitLevel.Commander } ? 1 : 0),
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
