using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Tests for <see cref="CampaignProgress"/> (issue #2): the 256-level
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

    [Fact]
    public void SeedForLevel_IsIdentity()
    {
        Assert.Equal(0, CampaignProgress.SeedForLevel(0));
        Assert.Equal(171, CampaignProgress.SeedForLevel(171));
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
    }

    // ── Per-level map-generation densities (issue #48 / #66) ────────────────

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

    // ── Per-level human slot assignment (issue #74) ─────────────────────────

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
}
