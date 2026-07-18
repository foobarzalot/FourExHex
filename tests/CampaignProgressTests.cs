// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Tests for <see cref="CampaignProgress"/>: the 256-level
/// campaign ladder model. Status transitions (Won is terminal),
/// derived stats (won counts, next-up), the tier → <see cref="Difficulty"/>
/// mapping, hex labels, and the identity level → seed mapping.
/// </summary>
public class CampaignProgressTests
{
    [Fact]
    public void FreshProgress_AllUntried_NextUpIsZero()
    {
        var p = new CampaignProgress();

        Assert.Equal(0, p.WonCount);
        Assert.Equal(0, p.NextUp);
        Assert.All(Enumerable.Range(0, CampaignProgress.LevelCount),
            i => Assert.Equal(CampaignLevelStatus.Untried, p.StatusOf(i)));
    }

    [Fact]
    public void MarkAttempted_UntriedBecomesLost()
    {
        var p = new CampaignProgress();

        bool changed = p.MarkAttempted(5);

        Assert.True(changed);
        Assert.Equal(CampaignLevelStatus.Lost, p.StatusOf(5));
    }

    [Fact]
    public void MarkAttempted_LostStaysLost_ReportsNoChange()
    {
        var p = new CampaignProgress();
        p.MarkAttempted(5);

        bool changed = p.MarkAttempted(5);

        Assert.False(changed);
        Assert.Equal(CampaignLevelStatus.Lost, p.StatusOf(5));
    }

    [Fact]
    public void MarkAttempted_WonIsTerminal_ReplayCannotUnwin()
    {
        var p = new CampaignProgress();
        p.MarkWon(5);

        bool changed = p.MarkAttempted(5);

        Assert.False(changed);
        Assert.Equal(CampaignLevelStatus.Won, p.StatusOf(5));
    }

    [Fact]
    public void MarkWon_FromUntriedAndFromLost()
    {
        var p = new CampaignProgress();
        p.MarkAttempted(3);

        Assert.True(p.MarkWon(3));
        Assert.True(p.MarkWon(4));

        Assert.Equal(CampaignLevelStatus.Won, p.StatusOf(3));
        Assert.Equal(CampaignLevelStatus.Won, p.StatusOf(4));
        Assert.Equal(2, p.WonCount);
    }

    [Fact]
    public void MarkWon_AlreadyWon_ReportsNoChange()
    {
        var p = new CampaignProgress();
        p.MarkWon(7);

        Assert.False(p.MarkWon(7));
        Assert.Equal(1, p.WonCount);
    }

    [Fact]
    public void NextUp_IsLowestNonWonLevel_LostDoesNotAdvanceIt()
    {
        var p = new CampaignProgress();
        p.MarkWon(0);
        p.MarkWon(1);
        p.MarkAttempted(2); // lost — still the next target
        p.MarkWon(3);

        Assert.Equal(2, p.NextUp);
    }

    [Fact]
    public void NextUp_NullWhenAllWon()
    {
        var p = new CampaignProgress();
        for (int i = 0; i < CampaignProgress.LevelCount; i++) p.MarkWon(i);

        Assert.Null(p.NextUp);
        Assert.Equal(CampaignProgress.LevelCount, p.WonCount);
    }

    [Fact]
    public void TierWonCount_SplitsAtTierBoundaries()
    {
        var p = new CampaignProgress();
        p.MarkWon(0);    // Recruit tier
        p.MarkWon(63);   // Recruit tier (last)
        p.MarkWon(64);   // Soldier tier (first)
        p.MarkWon(255);  // Commander tier (last)

        Assert.Equal(2, p.TierWonCount(0));
        Assert.Equal(1, p.TierWonCount(1));
        Assert.Equal(0, p.TierWonCount(2));
        Assert.Equal(1, p.TierWonCount(3));
    }

    [Theory]
    [InlineData(0, Difficulty.Recruit)]
    [InlineData(63, Difficulty.Recruit)]
    [InlineData(64, Difficulty.Soldier)]
    [InlineData(127, Difficulty.Soldier)]
    [InlineData(128, Difficulty.Captain)]
    [InlineData(191, Difficulty.Captain)]
    [InlineData(192, Difficulty.Commander)]
    [InlineData(255, Difficulty.Commander)]
    public void DifficultyForLevel_TierIsHighHexDigitPair(int level, Difficulty expected)
    {
        Assert.Equal(expected, CampaignProgress.DifficultyForLevel(level));
    }

    [Theory]
    [InlineData(0, "00")]
    [InlineData(10, "0A")]
    [InlineData(79, "4F")]
    [InlineData(255, "FF")]
    public void LabelFor_TwoDigitUppercaseHex(int level, string expected)
    {
        Assert.Equal(expected, CampaignProgress.LabelFor(level));
    }

    // SeedForLevel reads the baked winnable-seed table (#73): each level's
    // seed is the first candidate whose all-AI game at Soldier is won by the
    // level's hash-assigned human slot. Candidate 0 is the level number
    // itself, so a level whose original map was already winnable keeps it
    // byte-identically; the rest are re-seeded. The expectations below are
    // facts established by the CampaignWinnerSweepTests sweep/search harness.
    [Theory]
    [InlineData(9)]
    [InlineData(23)]
    public void SeedForLevel_KeepsIdentityWhereOriginalMapIsWinnable(int level)
    {
        Assert.Equal(level, CampaignProgress.SeedForLevel(level));
    }

    [Theory]
    [InlineData(0)]   // human slot lost on the identity seed
    [InlineData(171)] // human slot lost on the identity seed
    [InlineData(41)]  // stasis: no winner by the turn cap on the identity seed
    public void SeedForLevel_ReseedsLevelsWhoseOriginalMapIsNotWinnable(int level)
    {
        Assert.NotEqual(level, CampaignProgress.SeedForLevel(level));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(256)]
    public void OutOfRangeLevel_Throws(int level)
    {
        var p = new CampaignProgress();

        Assert.Throws<ArgumentOutOfRangeException>(() => p.StatusOf(level));
        Assert.Throws<ArgumentOutOfRangeException>(() => p.MarkAttempted(level));
        Assert.Throws<ArgumentOutOfRangeException>(() => p.MarkWon(level));
        Assert.Throws<ArgumentOutOfRangeException>(() => CampaignProgress.DifficultyForLevel(level));
        Assert.Throws<ArgumentOutOfRangeException>(() => CampaignProgress.LabelFor(level));
        Assert.Throws<ArgumentOutOfRangeException>(() => CampaignProgress.ModeForLevel(level));
        Assert.Throws<ArgumentOutOfRangeException>(() => CampaignProgress.SeedForLevel(level));
    }

    // ── Per-level game mode ─────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(64)]
    [InlineData(150)]
    [InlineData(255)]
    public void ModeForLevel_IsDeterministic(int level)
    {
        Assert.Equal(CampaignProgress.ModeForLevel(level), CampaignProgress.ModeForLevel(level));
    }

    [Fact]
    public void ModeForLevel_RecruitTierIsAlwaysFreeform()
    {
        // The sea is a later-game complication — Rising Tides never appears in
        // the Recruit tier (levels 0..63).
        for (int level = 0; level < CampaignProgress.TierSize; level++)
        {
            Assert.Equal(GameMode.Freeform, CampaignProgress.ModeForLevel(level));
        }
    }

    [Fact]
    public void ModeForLevel_IntroducedInEverySoldierAndAboveTier()
    {
        // Rising Tides is introduced at Soldier and present in each higher tier.
        for (int tier = 1; tier < CampaignProgress.TierCount; tier++)
        {
            int start = tier * CampaignProgress.TierSize;
            int risingTides = 0;
            for (int level = start; level < start + CampaignProgress.TierSize; level++)
            {
                if (CampaignProgress.ModeForLevel(level) == GameMode.RisingTides) risingTides++;
            }
            Assert.True(risingTides > 0, $"tier {tier} has no Rising Tides level");
        }
    }

    [Fact]
    public void ModeForLevel_RarerThanFreeformOverall()
    {
        int risingTides = 0, freeform = 0;
        for (int level = 0; level < CampaignProgress.LevelCount; level++)
        {
            if (CampaignProgress.ModeForLevel(level) == GameMode.RisingTides) risingTides++;
            else freeform++;
        }
        Assert.True(risingTides > 0);
        Assert.True(risingTides < freeform, $"Rising Tides ({risingTides}) not rarer than Freeform ({freeform})");
    }

    [Fact]
    public void ModeForLevel_FogOfWar_NeverInRecruitTier()
    {
        for (int level = 0; level < CampaignProgress.TierSize; level++)
        {
            Assert.NotEqual(GameMode.FogOfWar, CampaignProgress.ModeForLevel(level));
        }
    }

    [Fact]
    public void ModeForLevel_FogOfWar_PresentInEverySoldierAndAboveTier()
    {
        for (int tier = 1; tier < CampaignProgress.TierCount; tier++)
        {
            int start = tier * CampaignProgress.TierSize;
            int fog = 0;
            for (int level = start; level < start + CampaignProgress.TierSize; level++)
            {
                if (CampaignProgress.ModeForLevel(level) == GameMode.FogOfWar) fog++;
            }
            Assert.True(fog > 0, $"tier {tier} has no Fog Of War level");
        }
    }

    [Fact]
    public void ModeForLevel_FogOfWar_RarerThanFreeformOverall()
    {
        int fog = 0, freeform = 0;
        for (int level = 0; level < CampaignProgress.LevelCount; level++)
        {
            GameMode m = CampaignProgress.ModeForLevel(level);
            if (m == GameMode.FogOfWar) fog++;
            else if (m == GameMode.Freeform) freeform++;
        }
        Assert.True(fog > 0);
        Assert.True(fog < freeform, $"Fog Of War ({fog}) not rarer than Freeform ({freeform})");
    }

    [Fact]
    public void ModeForLevel_VikingRaiders_NeverInRecruitTier()
    {
        for (int level = 0; level < CampaignProgress.TierSize; level++)
        {
            Assert.NotEqual(GameMode.VikingRaiders, CampaignProgress.ModeForLevel(level));
        }
    }

    [Fact]
    public void ModeForLevel_ExactQuotaPerModePerTier()
    {
        // The complication rotation is quota-based: every Soldier+ tier holds
        // exactly ComplicationLevelsPerModePerTier levels of each complication
        // mode — guaranteed counts, not a probabilistic draw.
        GameMode[] modes = { GameMode.RisingTides, GameMode.FogOfWar, GameMode.VikingRaiders };
        for (int tier = 1; tier < CampaignProgress.TierCount; tier++)
        {
            int start = tier * CampaignProgress.TierSize;
            foreach (GameMode mode in modes)
            {
                int count = 0;
                for (int level = start; level < start + CampaignProgress.TierSize; level++)
                {
                    if (CampaignProgress.ModeForLevel(level) == mode) count++;
                }
                Assert.True(CampaignProgress.ComplicationLevelsPerModePerTier == count,
                    $"tier {tier}: {mode} count {count} != quota {CampaignProgress.ComplicationLevelsPerModePerTier}");
            }
        }
    }

    [Theory]
    [InlineData(0, GameMode.Freeform)]    // Recruit tier — never a complication
    [InlineData(64, GameMode.Freeform)]   // Soldier tier, outside every mode slice
    [InlineData(71, GameMode.RisingTides)]
    [InlineData(78, GameMode.FogOfWar)]
    [InlineData(77, GameMode.VikingRaiders)]
    public void ModeForLevel_PinsKnownAssignments(int level, GameMode expected)
    {
        // Campaign level identity is forever: these spot pins make an accidental
        // reshuffle of the tier-seeded slice assignment a visible test failure.
        Assert.Equal(expected, CampaignProgress.ModeForLevel(level));
    }

    [Fact]
    public void ModeForLevel_AppendingAModeNeverMovesExistingAssignments()
    {
        // Modes take consecutive slices of one tier shuffle, so any prefix of
        // the rotation must assign its modes to exactly the same levels as the
        // full rotation — the guarantee that lets a future mode join without
        // invalidating existing levels' baked winnable seeds.
        GameMode[][] prefixes =
        {
            new[] { GameMode.RisingTides },
            new[] { GameMode.RisingTides, GameMode.FogOfWar },
        };
        foreach (GameMode[] prefix in prefixes)
        {
            for (int level = 0; level < CampaignProgress.LevelCount; level++)
            {
                GameMode withPrefix = CampaignProgress.ModeForLevel(level, prefix);
                if (withPrefix != GameMode.Freeform)
                    Assert.Equal(withPrefix, CampaignProgress.ModeForLevel(level));
            }
        }
    }

    // ── Per-level map-generation densities ────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(255)]
    public void MapGenOptionsForLevel_IsDeterministic(int level)
    {
        MapGenOptions a = CampaignProgress.MapGenOptionsForLevel(level);
        MapGenOptions b = CampaignProgress.MapGenOptionsForLevel(level);
        Assert.Equal(a.TreeDensity, b.TreeDensity);
        Assert.Equal(a.MountainDensity, b.MountainDensity);
        Assert.Equal(a.GoldDensity, b.GoldDensity);
        Assert.Equal(a.ClumpingFactor, b.ClumpingFactor);
    }

    [Fact]
    public void MapGenOptionsForLevel_VikingLevelsClumpingFlooredAt90()
    {
        // Empirically (10-game all-AI probes per clumping stop), fragmented
        // starts are near-unwinnable in Viking Raiders — AI survival jumps
        // from ~20% at clumping ≤75 to ~80% at ≥90. Viking levels clamp the
        // drawn clumping to ≥90; every other level keeps its raw draw.
        for (int level = 0; level < CampaignProgress.LevelCount; level++)
        {
            // The raw draw, replicated verbatim: mountain, gold, tree, then
            // clumping off the same per-level rng.
            var rng = new DeterministicRng(unchecked(level * 2671 + 40503));
            rng.NextBounded(100); rng.NextBounded(100); rng.NextBounded(0, 3);
            int rawClumping = MapGenOptions.ClumpingFactorStops[
                rng.NextBounded(MapGenOptions.ClumpingFactorStops.Length)];

            int actual = CampaignProgress.MapGenOptionsForLevel(level).ClumpingFactor;
            if (CampaignProgress.ModeForLevel(level) == GameMode.VikingRaiders)
                Assert.Equal(Math.Max(rawClumping, 90), actual);
            else
                Assert.Equal(rawClumping, actual);
        }
    }

    [Fact]
    public void MapGenOptionsForLevel_ClumpingStaysWithinStops()
    {
        // Each level's clumping is drawn from the shared nonlinear stop set;
        // pin membership so a retune is a deliberate, visible change.
        for (int level = 0; level < CampaignProgress.LevelCount; level++)
        {
            MapGenOptions o = CampaignProgress.MapGenOptionsForLevel(level);
            Assert.Contains(o.ClumpingFactor, MapGenOptions.ClumpingFactorStops);
        }
    }

    [Fact]
    public void MapGenOptionsForLevel_ClumpingVariesAcrossTheLadder()
    {
        // Clumping should genuinely vary level-to-level — and over 256 levels every
        // stop should turn up, so no clumping degree is unreachable in the campaign.
        var seen = new HashSet<int>();
        for (int level = 0; level < CampaignProgress.LevelCount; level++)
        {
            seen.Add(CampaignProgress.MapGenOptionsForLevel(level).ClumpingFactor);
        }
        foreach (int stop in MapGenOptions.ClumpingFactorStops)
        {
            Assert.Contains(stop, seen);
        }
    }

    [Fact]
    public void MapGenOptionsForLevel_VariesAcrossTheLadder()
    {
        int mtnOn = 0, mtnOff = 0, goldOn = 0, goldOff = 0;
        var treeValues = new HashSet<int>();
        for (int level = 0; level < CampaignProgress.LevelCount; level++)
        {
            MapGenOptions o = CampaignProgress.MapGenOptionsForLevel(level);
            if (o.MountainDensity > 0) mtnOn++; else mtnOff++;
            if (o.GoldDensity > 0) goldOn++; else goldOff++;
            treeValues.Add(o.TreeDensity);
        }
        // Mountains and gold should appear present and absent across the 256 levels —
        // neither pinned, neither absent — and tree density should genuinely vary.
        Assert.True(mtnOn > 0 && mtnOff > 0, $"mountains on={mtnOn} off={mtnOff}");
        Assert.True(goldOn > 0 && goldOff > 0, $"gold on={goldOn} off={goldOff}");
        Assert.True(treeValues.Count > 1, $"tree density did not vary: {string.Join(",", treeValues)}");
    }

    [Fact]
    public void MapGenOptionsForLevel_MountainsAndGoldAreNotPerfectlyCorrelated()
    {
        // The presence draws are independent, so the ladder should contain at
        // least one level of each of the four combinations.
        bool both = false, neither = false, mtnOnly = false, goldOnly = false;
        for (int level = 0; level < CampaignProgress.LevelCount; level++)
        {
            MapGenOptions o = CampaignProgress.MapGenOptionsForLevel(level);
            bool mtn = o.MountainDensity > 0;
            bool gold = o.GoldDensity > 0;
            if (mtn && gold) both = true;
            else if (!mtn && !gold) neither = true;
            else if (mtn) mtnOnly = true;
            else goldOnly = true;
        }
        Assert.True(both && neither && mtnOnly && goldOnly,
            $"combos both={both} neither={neither} mtnOnly={mtnOnly} goldOnly={goldOnly}");
    }

    [Fact]
    public void MapGenOptionsForLevel_DensitiesStayWithinTheirAllowedSets()
    {
        // Mountains/gold use a single "on" density (or 0); trees vary across
        // {0, 5, 10}. Pin those sets so a retune is a deliberate, visible change.
        for (int level = 0; level < CampaignProgress.LevelCount; level++)
        {
            MapGenOptions o = CampaignProgress.MapGenOptionsForLevel(level);
            Assert.Contains(o.MountainDensity, new[] { 0, 10 });
            Assert.Contains(o.GoldDensity, new[] { 0, 5 });
            Assert.Contains(o.TreeDensity, new[] { 0, 5, 10 });
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(256)]
    public void MapGenOptionsForLevel_RejectsOutOfRange(int level)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CampaignProgress.MapGenOptionsForLevel(level));
    }

    // ── Per-level human slot assignment ─────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(171)]
    [InlineData(255)]
    public void HumanSlotForLevel_IsDeterministic(int level)
    {
        int a = CampaignProgress.HumanSlotForLevel(level, 6);
        int b = CampaignProgress.HumanSlotForLevel(level, 6);
        Assert.Equal(a, b);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(6)]
    public void HumanSlotForLevel_StaysInRangeForEveryLevel(int playerCount)
    {
        for (int level = 0; level < CampaignProgress.LevelCount; level++)
        {
            int slot = CampaignProgress.HumanSlotForLevel(level, playerCount);
            Assert.InRange(slot, 0, playerCount - 1);
        }
    }

    [Fact]
    public void HumanSlotForLevel_SpreadsAcrossAllSlots()
    {
        const int playerCount = 6;
        var hit = new HashSet<int>();
        for (int level = 0; level < CampaignProgress.LevelCount; level++)
        {
            hit.Add(CampaignProgress.HumanSlotForLevel(level, playerCount));
        }
        // Every real color slot should be the human's at least once across the
        // ladder — it must not collapse to one slot (e.g. always 0).
        Assert.Equal(playerCount, hit.Count);
    }

    [Fact]
    public void HumanSlotForLevel_DoesNotTriviallyCycle()
    {
        // Guard against accidentally shipping plain `level % playerCount`,
        // which would map levels 0..5 to exactly 0,1,2,3,4,5.
        int[] firstSix = Enumerable.Range(0, 6)
            .Select(level => CampaignProgress.HumanSlotForLevel(level, 6))
            .ToArray();
        Assert.NotEqual(new[] { 0, 1, 2, 3, 4, 5 }, firstSix);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(256)]
    public void HumanSlotForLevel_RejectsOutOfRangeLevel(int level)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CampaignProgress.HumanSlotForLevel(level, 6));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void HumanSlotForLevel_RejectsNonPositivePlayerCount(int playerCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CampaignProgress.HumanSlotForLevel(0, playerCount));
    }

    // --- Per-level roster (deterministic player count + color set) -----------

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    [InlineData(255)]
    public void PlayerCountForLevel_IsDeterministic(int level)
    {
        Assert.Equal(
            CampaignProgress.PlayerCountForLevel(level),
            CampaignProgress.PlayerCountForLevel(level));
    }

    [Fact]
    public void PlayerCountForLevel_InRange_AndVariesAcrossLadder()
    {
        var counts = new HashSet<int>();
        for (int level = 0; level < CampaignProgress.LevelCount; level++)
        {
            int n = CampaignProgress.PlayerCountForLevel(level);
            Assert.InRange(n, 2, 6);
            counts.Add(n);
        }
        // Every count 2..6 should occur somewhere on the ladder.
        Assert.Equal(new HashSet<int> { 2, 3, 4, 5, 6 }, counts);
    }

    [Fact]
    public void PlayerCountForLevel_BiasesTowardMorePlayers()
    {
        // Counts should descend in frequency across the ladder: 6 the plurality,
        // then 5, 4, 3, with 2 the least common.
        var freq = new int[7];
        for (int level = 0; level < CampaignProgress.LevelCount; level++)
        {
            freq[CampaignProgress.PlayerCountForLevel(level)]++;
        }
        Assert.True(freq[6] > freq[5], $"6 ({freq[6]}) should beat 5 ({freq[5]})");
        Assert.True(freq[5] > freq[4], $"5 ({freq[5]}) should beat 4 ({freq[4]})");
        Assert.True(freq[4] > freq[3], $"4 ({freq[4]}) should beat 3 ({freq[3]})");
        Assert.True(freq[3] > freq[2], $"3 ({freq[3]}) should beat 2 ({freq[2]})");
    }

    [Fact]
    public void ActiveColorSlots_AreDistinctSortedInRange_SizedToCount()
    {
        for (int level = 0; level < CampaignProgress.LevelCount; level++)
        {
            int[] slots = CampaignProgress.ActiveColorSlotsForLevel(level);
            Assert.Equal(CampaignProgress.PlayerCountForLevel(level), slots.Length);
            Assert.Equal(slots.Length, slots.Distinct().Count());
            Assert.True(slots.SequenceEqual(slots.OrderBy(s => s)), "slots must be sorted");
            Assert.All(slots, s => Assert.InRange(s, 0, 5));
        }
    }

    [Fact]
    public void ActiveColorSlots_AreDeterministic_AndNotAlwaysTheSameSubset()
    {
        Assert.True(CampaignProgress.ActiveColorSlotsForLevel(42)
            .SequenceEqual(CampaignProgress.ActiveColorSlotsForLevel(42)));

        // Every color slot is active on some level — not a fixed "first N" subset.
        var everActive = new HashSet<int>();
        for (int level = 0; level < CampaignProgress.LevelCount; level++)
        {
            everActive.UnionWith(CampaignProgress.ActiveColorSlotsForLevel(level));
        }
        Assert.Equal(new HashSet<int> { 0, 1, 2, 3, 4, 5 }, everActive);
    }

    [Fact]
    public void HumanColorSlot_IsDeterministic_AndAlwaysActive()
    {
        for (int level = 0; level < CampaignProgress.LevelCount; level++)
        {
            int human = CampaignProgress.HumanColorSlotForLevel(level);
            Assert.Equal(human, CampaignProgress.HumanColorSlotForLevel(level));
            Assert.Contains(human, CampaignProgress.ActiveColorSlotsForLevel(level));
        }
    }
}
