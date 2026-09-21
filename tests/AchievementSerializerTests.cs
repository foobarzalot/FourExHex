// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Round-trip and tolerance tests for <see cref="AchievementSerializer"/>.
/// Deserialize throws on anything unreadable and the store falls back to a
/// fresh record; readable damage degrades gracefully via
/// <see cref="AchievementRecord.FromEntries"/> instead of throwing.
/// </summary>
public class AchievementSerializerTests
{
    [Fact]
    public void RoundTrip_PreservesUnlockOrderAndProgress()
    {
        var record = new AchievementRecord();
        record.SetProgress("a.counter", 2);
        record.Unlock("a.first");
        record.Unlock("a.second");

        AchievementRecord loaded =
            AchievementSerializer.Deserialize(AchievementSerializer.Serialize(record));

        Assert.Equal(new[] { "a.first", "a.second" }, loaded.UnlockedInOrder);
        Assert.Equal(2, loaded.ProgressFor("a.counter"));
    }

    [Fact]
    public void Serialize_WritesVersionStamp()
    {
        string json = AchievementSerializer.Serialize(new AchievementRecord());

        Assert.Contains("\"FormatVersion\": 2", json);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all {")]
    [InlineData("null")]
    public void Deserialize_CorruptOrEmpty_Throws(string json)
    {
        Assert.ThrowsAny<Exception>(() => AchievementSerializer.Deserialize(json));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void Deserialize_UnsupportedVersion_Throws(int version)
    {
        string json = $"{{ \"FormatVersion\": {version}, \"Entries\": [] }}";

        Assert.ThrowsAny<Exception>(() => AchievementSerializer.Deserialize(json));
    }

    [Fact]
    public void Deserialize_MissingEntries_IsEmptyRecord()
    {
        AchievementRecord loaded =
            AchievementSerializer.Deserialize("{ \"FormatVersion\": 1 }");

        Assert.Empty(loaded.UnlockedInOrder);
    }

    [Fact]
    public void Deserialize_FileFromFutureBuild_RoundTripsUnknownIdsWithoutLoss()
    {
        // The acceptance case: a build that knows more achievements wrote
        // this file. Loading and re-saving here must not drop what we
        // don't recognize.
        string json = """
            {
              "FormatVersion": 1,
              "Entries": [
                { "Id": "a.known", "Order": 1, "Progress": 3 },
                { "Id": "future.mystery", "Order": 2, "Progress": 42 }
              ]
            }
            """;

        AchievementRecord loaded = AchievementSerializer.Deserialize(json);
        AchievementRecord reloaded =
            AchievementSerializer.Deserialize(AchievementSerializer.Serialize(loaded));

        Assert.Equal(new[] { "a.known", "future.mystery" }, reloaded.UnlockedInOrder);
        Assert.Equal(42, reloaded.ProgressFor("future.mystery"));
    }

    [Fact]
    public void Deserialize_EntryWithBlankId_IsSkippedNotThrown()
    {
        string json = """
            {
              "FormatVersion": 1,
              "Entries": [ { "Id": "", "Order": 1 }, { "Id": "a.ok", "Order": 2 } ]
            }
            """;

        AchievementRecord loaded = AchievementSerializer.Deserialize(json);

        Assert.Equal(new[] { "a.ok" }, loaded.UnlockedInOrder);
    }

    [Fact]
    public void RoundTrip_PreservesPerLevelCredit()
    {
        var record = new AchievementRecord();
        record.SetCredit("a.counter", 3, 5);
        record.SetCredit("a.counter", 7, 1);
        record.SetProgress("a.counter", 6);

        AchievementRecord loaded =
            AchievementSerializer.Deserialize(AchievementSerializer.Serialize(record));

        Assert.Equal(5, loaded.CreditFor("a.counter", 3));
        Assert.Equal(1, loaded.CreditFor("a.counter", 7));
        Assert.Equal(6, loaded.ProgressFor("a.counter"));
    }

    [Fact]
    public void Deserialize_VersionOneFile_LoadsWithEmptyLevelAttribution()
    {
        // A record written before per-level credit existed: unlocks and
        // progress stay, but no level has contributed anything yet.
        string json = """
            {
              "FormatVersion": 1,
              "Entries": [
                { "Id": "a.first", "Order": 1, "Progress": 1 },
                { "Id": "a.counter", "Order": 0, "Progress": 2 }
              ]
            }
            """;

        AchievementRecord loaded = AchievementSerializer.Deserialize(json);

        Assert.True(loaded.IsUnlocked("a.first"));
        Assert.Equal(2, loaded.ProgressFor("a.counter"));
        Assert.Equal(0, loaded.CreditFor("a.counter", 0));
        Assert.Null(Assert.Single(loaded.ToEntries().Where(e => e.Id == "a.counter")).Credits);
    }

    [Fact]
    public void Deserialize_DamagedCredits_DegradeGracefully()
    {
        string json = """
            {
              "FormatVersion": 2,
              "Entries": [
                { "Id": "a.counter", "Progress": 2,
                  "Credits": [ { "Level": -3, "Amount": 1 }, null, { "Level": 4, "Amount": 2 } ] }
              ]
            }
            """;

        AchievementRecord loaded = AchievementSerializer.Deserialize(json);

        Assert.Equal(2, loaded.CreditFor("a.counter", 4));
        Assert.Single(loaded.ToEntries()[0].Credits!);
    }
}
