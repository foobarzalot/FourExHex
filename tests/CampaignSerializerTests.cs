using System;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Round-trip and tolerance tests for <see cref="CampaignSerializer"/>.
/// The campaign sidecar file must survive corruption,
/// truncation, and unknown future versions without ever crashing the
/// menu — the store falls back to fresh progress when Deserialize throws.
/// </summary>
public class CampaignSerializerTests
{
    [Fact]
    public void RoundTrip_PreservesEveryStatus()
    {
        var p = new CampaignProgress();
        p.MarkWon(0);
        p.MarkAttempted(1);
        p.MarkWon(64);
        p.MarkWon(255);

        string json = CampaignSerializer.Serialize(p);
        CampaignProgress loaded = CampaignSerializer.Deserialize(json);

        Assert.Equal(CampaignLevelStatus.Won, loaded.StatusOf(0));
        Assert.Equal(CampaignLevelStatus.Lost, loaded.StatusOf(1));
        Assert.Equal(CampaignLevelStatus.Untried, loaded.StatusOf(2));
        Assert.Equal(CampaignLevelStatus.Won, loaded.StatusOf(64));
        Assert.Equal(CampaignLevelStatus.Won, loaded.StatusOf(255));
        Assert.Equal(3, loaded.WonCount);
    }

    [Fact]
    public void Serialize_WritesVersionAndNumericStatuses()
    {
        var p = new CampaignProgress();
        p.MarkWon(0);

        string json = CampaignSerializer.Serialize(p);

        Assert.Contains("\"FormatVersion\": 1", json);
        // Statuses persist numerically (enum member order is load-bearing,
        // same convention as PlaybackSpeed in user://settings.json).
        Assert.DoesNotContain("Won", json);
        Assert.DoesNotContain("Untried", json);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all {")]
    [InlineData("null")]
    public void Deserialize_CorruptOrEmpty_Throws(string json)
    {
        Assert.ThrowsAny<Exception>(() => CampaignSerializer.Deserialize(json));
    }

    [Fact]
    public void Deserialize_UnsupportedFutureVersion_Throws()
    {
        string json = "{ \"FormatVersion\": 2, \"Statuses\": [2] }";

        Assert.ThrowsAny<Exception>(() => CampaignSerializer.Deserialize(json));
    }

    [Fact]
    public void Deserialize_ShortStatusesArray_PadsWithUntried()
    {
        string json = "{ \"FormatVersion\": 1, \"Statuses\": [2, 1] }";

        CampaignProgress loaded = CampaignSerializer.Deserialize(json);

        Assert.Equal(CampaignLevelStatus.Won, loaded.StatusOf(0));
        Assert.Equal(CampaignLevelStatus.Lost, loaded.StatusOf(1));
        Assert.Equal(CampaignLevelStatus.Untried, loaded.StatusOf(2));
        Assert.Equal(CampaignLevelStatus.Untried, loaded.StatusOf(255));
    }

    [Fact]
    public void Deserialize_LongStatusesArray_IgnoresExtras()
    {
        // 300 entries, all Won — extras beyond 256 must be ignored.
        string statuses = string.Join(",", Enumerable.Repeat(2, 300));
        string json = "{ \"FormatVersion\": 1, \"Statuses\": [" + statuses + "] }";

        CampaignProgress loaded = CampaignSerializer.Deserialize(json);

        Assert.Equal(CampaignProgress.LevelCount, loaded.WonCount);
    }

    [Fact]
    public void Deserialize_MissingStatuses_AllUntried()
    {
        string json = "{ \"FormatVersion\": 1 }";

        CampaignProgress loaded = CampaignSerializer.Deserialize(json);

        Assert.Equal(0, loaded.WonCount);
        Assert.Equal(CampaignLevelStatus.Untried, loaded.StatusOf(0));
    }

    [Fact]
    public void Deserialize_OutOfRangeStatusValue_BecomesUntried()
    {
        string json = "{ \"FormatVersion\": 1, \"Statuses\": [9, -3, 2] }";

        CampaignProgress loaded = CampaignSerializer.Deserialize(json);

        Assert.Equal(CampaignLevelStatus.Untried, loaded.StatusOf(0));
        Assert.Equal(CampaignLevelStatus.Untried, loaded.StatusOf(1));
        Assert.Equal(CampaignLevelStatus.Won, loaded.StatusOf(2));
    }
}
