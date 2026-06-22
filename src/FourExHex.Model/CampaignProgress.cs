using System;
using System.Collections.Generic;

/// <summary>
/// Persistent status of one campaign level (issue #2). Member order is
/// load-bearing: statuses persist numerically in the campaign sidecar
/// file (<c>user://campaign.json</c>) — same convention as
/// <c>PlaybackSpeed</c> in <c>user://settings.json</c> — so
/// Untried=0, Lost=1, Won=2 must stay fixed.
/// </summary>
public enum CampaignLevelStatus : byte
{
    Untried = 0,
    Lost = 1,
    Won = 2,
}

/// <summary>
/// The campaign ladder model (issue #2): 256 levels labeled
/// <c>00</c>–<c>FF</c>, in four tiers of 64 mapped onto
/// <see cref="Difficulty"/> (Recruit 00–3F … Commander C0–FF).
/// Pure model — Godot-free, persisted via <see cref="CampaignSerializer"/>.
///
/// Status semantics ("mark at launch"): starting a level marks it
/// <see cref="CampaignLevelStatus.Lost"/> ("attempted, never won"), so an
/// abandon or crash needs no extra bookkeeping; winning flips it to
/// <see cref="CampaignLevelStatus.Won"/>, which is terminal — replaying a
/// won level can never un-win it.
/// </summary>
public sealed class CampaignProgress
{
    public const int LevelCount = 256;
    public const int TierSize = 64;
    public const int TierCount = 4;

    private readonly CampaignLevelStatus[] _statuses = new CampaignLevelStatus[LevelCount];

    /// <summary>Fresh progress: every level <see cref="CampaignLevelStatus.Untried"/>.</summary>
    public CampaignProgress()
    {
    }

    /// <summary>
    /// Build progress from a persisted status list (deserialize path).
    /// Tolerant by design: shorter lists pad with Untried, extras beyond
    /// <see cref="LevelCount"/> are ignored, and values outside the enum
    /// range degrade to Untried — a damaged file costs at worst some
    /// re-recordable progress, never a crash.
    /// </summary>
    public static CampaignProgress FromStatuses(IReadOnlyList<int> statuses)
    {
        var progress = new CampaignProgress();
        int count = Math.Min(statuses.Count, LevelCount);
        for (int i = 0; i < count; i++)
        {
            progress._statuses[i] = statuses[i] switch
            {
                (int)CampaignLevelStatus.Lost => CampaignLevelStatus.Lost,
                (int)CampaignLevelStatus.Won => CampaignLevelStatus.Won,
                _ => CampaignLevelStatus.Untried,
            };
        }
        return progress;
    }

    public CampaignLevelStatus StatusOf(int level)
    {
        ValidateLevel(level);
        return _statuses[level];
    }

    /// <summary>Mark a level attempted (Untried → Lost). Won is terminal
    /// and stays Won. Returns true iff the status changed (caller saves).</summary>
    public bool MarkAttempted(int level)
    {
        ValidateLevel(level);
        if (_statuses[level] != CampaignLevelStatus.Untried) return false;
        _statuses[level] = CampaignLevelStatus.Lost;
        return true;
    }

    /// <summary>Mark a level won (terminal). Returns true iff the status
    /// changed (caller saves).</summary>
    public bool MarkWon(int level)
    {
        ValidateLevel(level);
        if (_statuses[level] == CampaignLevelStatus.Won) return false;
        _statuses[level] = CampaignLevelStatus.Won;
        return true;
    }

    /// <summary>Total levels won, for the «won» / 256 header stat.</summary>
    public int WonCount
    {
        get
        {
            int count = 0;
            foreach (CampaignLevelStatus s in _statuses)
            {
                if (s == CampaignLevelStatus.Won) count++;
            }
            return count;
        }
    }

    /// <summary>Won count within one tier (0..3), for the per-tier
    /// «won» / 64 header stat.</summary>
    public int TierWonCount(int tier)
    {
        if (tier is < 0 or >= TierCount)
        {
            throw new ArgumentOutOfRangeException(nameof(tier), tier,
                $"Campaign tier must be 0..{TierCount - 1}.");
        }
        int count = 0;
        for (int i = tier * TierSize; i < (tier + 1) * TierSize; i++)
        {
            if (_statuses[i] == CampaignLevelStatus.Won) count++;
        }
        return count;
    }

    /// <summary>The "next up" level: lowest level not yet won (the one hex
    /// drawn with the thick outline). Null once all 256 are won.</summary>
    public int? NextUp
    {
        get
        {
            for (int i = 0; i < LevelCount; i++)
            {
                if (_statuses[i] != CampaignLevelStatus.Won) return i;
            }
            return null;
        }
    }

    /// <summary>Snapshot of all statuses as ints (serialize path).</summary>
    public int[] ToStatusArray()
    {
        var result = new int[LevelCount];
        for (int i = 0; i < LevelCount; i++) result[i] = (int)_statuses[i];
        return result;
    }

    /// <summary>Tier difficulty of a level — the high hex digit pair:
    /// 00–3F Recruit, 40–7F Soldier, 80–BF Captain, C0–FF Commander.
    /// Applied as the HUMAN slot's handicap when launching the level.</summary>
    public static Difficulty DifficultyForLevel(int level)
    {
        ValidateLevel(level);
        return (Difficulty)(level / TierSize);
    }

    /// <summary>Display label: two-digit uppercase hex ("00".."FF").</summary>
    public static string LabelFor(int level)
    {
        ValidateLevel(level);
        return level.ToString("X2");
    }

    /// <summary>Level → master-seed mapping. Identity by design (issue #2):
    /// level N plays the procedural map of seed N.</summary>
    public static int SeedForLevel(int level)
    {
        ValidateLevel(level);
        return level;
    }

    // Per-level map-generation densities (issue #48 / #66). Each campaign level
    // gets a fixed, reproducible tree/mountain/gold mix derived from the level
    // number — independent of the freeform New Game steppers and varied across
    // the ladder. Mountains/gold keep the present/absent odds and use a single
    // "on" density; trees vary across a small set so forest cover is part of a
    // level's identity too.
    private const int CampaignMountainChance = 55; // %; mountains slightly common
    private const int CampaignGoldChance = 45;     // %; gold the rarer prize
    private const int CampaignMountainOnDensity = 10; // % of land when mountains present
    private const int CampaignGoldOnDensity = 5;      // % of land when gold present

    /// <summary>The tree/mountain/gold generation densities for a campaign level
    /// (issue #48 / #66). Deterministic — same level always yields the same options
    /// — and decorrelated from both the freeform steppers and the map seed, so a
    /// level's terrain is a fixed part of its identity. Mountains/gold are present
    /// at fixed odds (and a fixed density when present); trees vary across
    /// {0, 5, 10}% of land.</summary>
    public static MapGenOptions MapGenOptionsForLevel(int level)
    {
        ValidateLevel(level);
        // Deterministic per-level draw, offset off the level number so it doesn't
        // track the map seed (= level). Sequential draws give independent
        // mountain/gold/tree densities. Integer-only (no floats — Model rule).
        var rng = new Random(unchecked(level * 2671 + 40503));
        int mountains = rng.Next(100) < CampaignMountainChance ? CampaignMountainOnDensity : 0;
        int gold = rng.Next(100) < CampaignGoldChance ? CampaignGoldOnDensity : 0;
        int trees = 5 * rng.Next(0, 3); // {0, 5, 10}
        return new MapGenOptions(
            TreeDensity: trees, MountainDensity: mountains, GoldDensity: gold);
    }

    private static void ValidateLevel(int level)
    {
        if (level is < 0 or >= LevelCount)
        {
            throw new ArgumentOutOfRangeException(nameof(level), level,
                $"Campaign level must be 0..{LevelCount - 1}.");
        }
    }
}
