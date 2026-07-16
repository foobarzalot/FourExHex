// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;

/// <summary>
/// Persistent status of one campaign level. Member order is
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
/// The campaign ladder model: 256 levels labeled <c>00</c>–<c>FF</c>, in
/// four tiers of 64 mapped onto <see cref="Difficulty"/> (Recruit 00–3F …
/// Commander C0–FF). Pure model — Godot-free, persisted via
/// <see cref="CampaignSerializer"/>. Starting a level marks it Lost
/// ("attempted, never won"); winning flips it to Won, which is terminal.
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

    /// <summary>Level → master-seed mapping, read from the baked winnable-seed
    /// table (<see cref="CampaignSeeds.ByLevel"/>): every level's seed carries a
    /// proof that the level's human slot can win at Soldier difficulty. Levels
    /// whose original map (seed = level number) already proved winnable keep it;
    /// the rest carry a searched replacement. See <c>CAMPAIGN_SEEDS.md</c> for
    /// the regeneration runbook.</summary>
    public static int SeedForLevel(int level)
    {
        ValidateLevel(level);
        return CampaignSeeds.ByLevel[level];
    }

    /// <summary>Exact number of levels each complication mode gets in every
    /// Soldier+ tier. The rotation size times this must stay ≤
    /// <see cref="TierSize"/>.</summary>
    public const int ComplicationLevelsPerModePerTier = 6;

    // The campaign complication rotation. Modes are assigned as consecutive
    // ComplicationLevelsPerModePerTier-sized slices of a tier-seeded shuffle,
    // so appending a future mode here consumes the next slice and never moves
    // existing assignments (their baked winnable seeds stay valid).
    private static readonly GameMode[] ComplicationModes =
    {
        GameMode.RisingTides,
        GameMode.FogOfWar,
        GameMode.VikingRaiders,
    };

    /// <summary>Game mode for a campaign level. Complication modes are
    /// later-game challenges: never in the Recruit tier, and every Soldier+
    /// tier holds exactly <see cref="ComplicationLevelsPerModePerTier"/> levels
    /// of each mode in the rotation. Deterministic — same level always yields
    /// the same mode — via a tier-seeded shuffle of the tier's level offsets
    /// sliced per mode, with a magic offset distinct from the map-gen
    /// (2671/40503), roster (6151/24593) and slot-hash draws so it doesn't
    /// track them. Integer-only (no floats — Model rule).</summary>
    public static GameMode ModeForLevel(int level) => ModeForLevel(level, ComplicationModes);

    /// <summary>Rotation-explicit overload backing <see cref="ModeForLevel(int)"/>.
    /// Public so tests can pin extension-stability: because modes take
    /// consecutive slices of one tier shuffle, any prefix of the rotation
    /// yields the same assignments for its modes as the full rotation.</summary>
    public static GameMode ModeForLevel(int level, IReadOnlyList<GameMode> rotation)
    {
        ValidateLevel(level);
        if (DifficultyForLevel(level) < Difficulty.Soldier) return GameMode.Freeform;
        int tier = level / TierSize;
        int slot = ShuffledPositionInTier(tier, level - tier * TierSize)
            / ComplicationLevelsPerModePerTier;
        return slot < rotation.Count ? rotation[slot] : GameMode.Freeform;
    }

    /// <summary>Position of a tier-relative level offset in the tier's
    /// Fisher–Yates shuffle (same seeded-<see cref="Random"/> idiom as
    /// <see cref="ComputeRoster"/>, distinct offset domain: tier, not level).</summary>
    private static int ShuffledPositionInTier(int tier, int offset)
    {
        var rng = new Random(unchecked(tier * 8209 + 49157));
        int[] order = new int[TierSize];
        for (int i = 0; i < TierSize; i++) order[i] = i;
        for (int i = TierSize - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }
        return Array.IndexOf(order, offset);
    }

    // Per-level map-generation densities. Each campaign level
    // gets a fixed, reproducible tree/mountain/gold mix derived from the level
    // number — independent of the freeform New Game steppers and varied across
    // the ladder. Mountains/gold keep the present/absent odds and use a single
    // "on" density; trees vary across a small set so forest cover is part of a
    // level's identity too.
    private const int CampaignMountainChance = 55; // %; mountains slightly common
    private const int CampaignGoldChance = 45;     // %; gold the rarer prize
    private const int CampaignMountainOnDensity = 10; // % of land when mountains present
    private const int CampaignGoldOnDensity = 5;      // % of land when gold present

    /// <summary>Minimum territory clumping for a Viking Raiders campaign level.
    /// Fragmented starts are near-unwinnable in that mode (10-game all-AI
    /// probes: AI survival ~20% at clumping ≤75, ~80% at ≥90 — every fragment
    /// is coastline, so raiders eat the board).</summary>
    private const int VikingMinClumpingFactor = 90;

    /// <summary>The tree/mountain/gold densities and territory clumping for a campaign
    /// level. Deterministic — same level always yields the same
    /// options — and decorrelated from both the freeform steppers and the map seed, so a
    /// level's terrain is a fixed part of its identity. Mountains/gold are present at
    /// fixed odds (and a fixed density when present); trees vary across {0, 5, 10}% of
    /// land; clumping is drawn from the shared nonlinear stop set so each level's
    /// sparse↔clumped feel is part of its identity too — except Viking Raiders levels,
    /// whose draw clamps to ≥<see cref="VikingMinClumpingFactor"/>.</summary>
    public static MapGenOptions MapGenOptionsForLevel(int level)
    {
        ValidateLevel(level);
        // Deterministic per-level draw, offset off the level number so it doesn't
        // track the map seed (= level). Sequential draws give independent
        // mountain/gold/tree/clumping values. Integer-only (no floats — Model rule).
        // The clumping draw goes last so adding it leaves the existing
        // mountain/gold/tree values for every level byte-unchanged.
        var rng = new Random(unchecked(level * 2671 + 40503));
        int mountains = rng.Next(100) < CampaignMountainChance ? CampaignMountainOnDensity : 0;
        int gold = rng.Next(100) < CampaignGoldChance ? CampaignGoldOnDensity : 0;
        int trees = 5 * rng.Next(0, 3); // {0, 5, 10}
        int clumping = MapGenOptions.ClumpingFactorStops[
            rng.Next(MapGenOptions.ClumpingFactorStops.Length)];
        // The clamp stays after the full draw so non-viking levels are
        // byte-unchanged and 90/95/100 remain legal stop values.
        if (ModeForLevel(level) == GameMode.VikingRaiders)
        {
            clumping = Math.Max(clumping, VikingMinClumpingFactor);
        }
        return new MapGenOptions(
            TreeDensity: trees, MountainDensity: mountains, GoldDensity: gold,
            ClumpingFactor: clumping);
    }

    /// <summary>Which roster slot the human occupies for a campaign level.
    /// Deterministic and stable forever, always in
    /// <c>[0, playerCount)</c>, spread across slots (no trivial 0,1,2… cycle),
    /// so a given level always plays byte-identically while the human's start
    /// varies across the ladder. <paramref name="playerCount"/> is the active
    /// roster size; the result is always a real color slot, never neutral
    /// (<c>PlayerId.None</c> is not a roster slot). Integer-only (no floats —
    /// Model rule).</summary>
    public static int HumanSlotForLevel(int level, int playerCount)
    {
        ValidateLevel(level);
        if (playerCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(playerCount), playerCount,
                "Player count must be >= 1.");
        }
        // Knuth-style multiplicative hash, decorrelated from the map seed
        // (= level) and from the per-level map-gen draw, mod playerCount for a
        // stable spread across slots.
        uint h = unchecked((uint)level * 2654435761u);
        return (int)(h % (uint)playerCount);
    }

    /// <summary>Number of players in a campaign level — a uniform-random 2..6
    /// derived from the level, so not every level fields all six colors. Stable
    /// forever per level.</summary>
    public static int PlayerCountForLevel(int level)
    {
        ValidateLevel(level);
        return ComputeRoster(level).ActiveSlots.Length;
    }

    /// <summary>The active color slots for a campaign level — a deterministic
    /// sorted subset of [0,6) of size <see cref="PlayerCountForLevel"/>. Returns
    /// a fresh array (callers may mutate).</summary>
    public static int[] ActiveColorSlotsForLevel(int level)
    {
        ValidateLevel(level);
        return ComputeRoster(level).ActiveSlots;
    }

    /// <summary>The human's actual color slot (0..5) for a campaign level —
    /// always one of <see cref="ActiveColorSlotsForLevel"/>, picked via the
    /// multiplicative hash within the active set so it stays spread across the ladder.</summary>
    public static int HumanColorSlotForLevel(int level)
    {
        ValidateLevel(level);
        return ComputeRoster(level).HumanSlot;
    }

    /// <summary>Deterministic per-level roster: a uniform-random count of distinct
    /// color slots, with the human at one of them. Integer-only (no floats — Model
    /// rule) and reproducible forever, using the same seeded-<see cref="Random"/>
    /// idiom as <see cref="MapGenOptionsForLevel"/> but with a distinct offset, so
    /// the player set is decorrelated from both the map seed (= level) and the
    /// terrain-density draw — a level's terrain is unchanged; only who plays it
    /// varies. All draws come from one rng in a fixed order so it never shifts.</summary>
    private static (int[] ActiveSlots, int HumanSlot) ComputeRoster(int level)
    {
        int slotCount = GameSettings.PlayerConfig.Length; // 6
        var rng = new Random(unchecked(level * 6151 + 24593));

        // Player count, biased toward more players: weight a count of c by (c-1),
        // so 6 is the plurality, then 5, 4, 3, with 2 the least likely. Total
        // weight = sum(1..slotCount-1) = slotCount*(slotCount-1)/2 (= 15 for 6).
        // Integer-only (no floats — Model rule).
        int totalWeight = slotCount * (slotCount - 1) / 2;
        int draw = rng.Next(totalWeight);
        int count = 2;
        int acc = 0;
        for (int c = slotCount; c >= 2; c--)
        {
            acc += c - 1;
            if (draw < acc) { count = c; break; }
        }

        // Fisher–Yates shuffle of the slot indices, take the first `count`, sort
        // ascending so the roster order (and thus map-gen owner assignment) is
        // canonical and stable.
        int[] order = new int[slotCount];
        for (int i = 0; i < slotCount; i++) order[i] = i;
        for (int i = slotCount - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }
        int[] active = new int[count];
        Array.Copy(order, active, count);
        Array.Sort(active);

        int humanSlot = active[HumanSlotForLevel(level, count)];
        return (active, humanSlot);
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
