using System;
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
}
