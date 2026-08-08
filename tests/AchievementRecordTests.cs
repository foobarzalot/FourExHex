// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Behavior tests for <see cref="AchievementRecord"/> — the authoritative
/// local achievement record. Three properties carry the design:
/// unlocks are append-only and never revoked, progress only ever rises,
/// and ids the running build does not recognize survive a round-trip
/// (so an older build can never destroy a newer record).
/// </summary>
public class AchievementRecordTests
{
    private static readonly IReadOnlyDictionary<string, string> NoRenames =
        new Dictionary<string, string>();

    private static AchievementEntryData Entry(string id, int order = 0, int progress = 0) =>
        new AchievementEntryData { Id = id, Order = order, Progress = progress };

    // --- Unlock ---

    [Fact]
    public void Unlock_FirstTime_ReturnsTrueAndAssignsOrderOne()
    {
        var record = new AchievementRecord();

        Assert.True(record.Unlock("a.one"));
        Assert.True(record.IsUnlocked("a.one"));
        Assert.Equal(new[] { "a.one" }, record.UnlockedInOrder);
    }

    [Fact]
    public void Unlock_Twice_ReturnsFalseAndDoesNotDuplicate()
    {
        var record = new AchievementRecord();
        record.Unlock("a.one");

        Assert.False(record.Unlock("a.one"));
        Assert.Equal(new[] { "a.one" }, record.UnlockedInOrder);
    }

    [Fact]
    public void UnlockedInOrder_ReflectsUnlockSequence()
    {
        var record = new AchievementRecord();
        record.Unlock("a.two");
        record.Unlock("a.one");
        record.Unlock("a.three");

        Assert.Equal(new[] { "a.two", "a.one", "a.three" }, record.UnlockedInOrder);
    }

    [Fact]
    public void UnlockedInOrder_ExcludesProgressOnlyEntries()
    {
        var record = new AchievementRecord();
        record.SetProgress("a.counter", 2);
        record.Unlock("a.one");

        Assert.Equal(new[] { "a.one" }, record.UnlockedInOrder);
    }

    // --- Progress ---

    [Fact]
    public void SetProgress_RaisesValueAndReturnsTrue()
    {
        var record = new AchievementRecord();

        Assert.True(record.SetProgress("a.counter", 2));
        Assert.Equal(2, record.ProgressFor("a.counter"));
    }

    [Fact]
    public void SetProgress_LowerValue_ReturnsFalseAndKeepsBest()
    {
        var record = new AchievementRecord();
        record.SetProgress("a.counter", 3);

        Assert.False(record.SetProgress("a.counter", 1));
        Assert.Equal(3, record.ProgressFor("a.counter"));
    }

    [Fact]
    public void ProgressFor_UnknownId_IsZero()
    {
        Assert.Equal(0, new AchievementRecord().ProgressFor("a.nope"));
    }

    [Fact]
    public void IsUnlocked_UnknownId_IsFalse()
    {
        Assert.False(new AchievementRecord().IsUnlocked("a.nope"));
    }

    // --- Round-trip and unknown-id preservation ---

    [Fact]
    public void ToEntries_RoundTripsUnlockOrderAndProgress()
    {
        var record = new AchievementRecord();
        record.SetProgress("a.counter", 2);
        record.Unlock("a.one");
        record.Unlock("a.two");

        AchievementRecord loaded = AchievementRecord.FromEntries(record.ToEntries(), NoRenames);

        Assert.Equal(new[] { "a.one", "a.two" }, loaded.UnlockedInOrder);
        Assert.Equal(2, loaded.ProgressFor("a.counter"));
    }

    [Fact]
    public void FromEntries_UnknownId_IsPreservedAndReEmitted()
    {
        // An id this build has never heard of — written by a future build.
        // The record must carry it through untouched.
        AchievementEntryData[] fromFuture =
        {
            Entry("future.unknown", order: 1, progress: 7),
            Entry("a.counter", progress: 2),
        };

        AchievementRecord loaded = AchievementRecord.FromEntries(fromFuture, NoRenames);
        AchievementEntryData[] reEmitted = loaded.ToEntries();

        AchievementEntryData survivor = Assert.Single(
            reEmitted.Where(e => e.Id == "future.unknown"));
        Assert.Equal(1, survivor.Order);
        Assert.Equal(7, survivor.Progress);
        Assert.True(loaded.IsUnlocked("future.unknown"));
    }

    // --- Tolerance: a damaged file costs progress, never a crash ---

    [Fact]
    public void FromEntries_Null_IsEmpty()
    {
        AchievementRecord loaded = AchievementRecord.FromEntries(null, NoRenames);

        Assert.Empty(loaded.UnlockedInOrder);
        Assert.Empty(loaded.ToEntries());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromEntries_BlankId_IsSkipped(string? id)
    {
        AchievementRecord loaded = AchievementRecord.FromEntries(
            new[] { Entry(id!, order: 1), Entry("a.one", order: 2) }, NoRenames);

        Assert.Equal(new[] { "a.one" }, loaded.UnlockedInOrder);
    }

    [Fact]
    public void FromEntries_NegativeValues_ClampToZero()
    {
        AchievementRecord loaded = AchievementRecord.FromEntries(
            new[] { Entry("a.one", order: -5, progress: -3) }, NoRenames);

        Assert.False(loaded.IsUnlocked("a.one"));
        Assert.Equal(0, loaded.ProgressFor("a.one"));
    }

    [Fact]
    public void FromEntries_DuplicateIds_ProgressIsMaxAndOrderIsLowerNonZero()
    {
        // a.one appears twice with orders 4 and 2. Taking the lower puts it
        // ahead of a.two (order 3); taking the higher would not.
        AchievementRecord loaded = AchievementRecord.FromEntries(
            new[]
            {
                Entry("a.one", order: 4, progress: 1),
                Entry("a.two", order: 3),
                Entry("a.one", order: 2, progress: 9),
            },
            NoRenames);

        Assert.Equal(2, loaded.ToEntries().Length);
        Assert.Equal(9, loaded.ProgressFor("a.one"));
        Assert.Equal(new[] { "a.one", "a.two" }, loaded.UnlockedInOrder);
    }

    [Fact]
    public void FromEntries_UnlockedEntries_AreRenumberedFromOne()
    {
        // A hand-edited or half-written file can carry gappy order values.
        AchievementRecord loaded = AchievementRecord.FromEntries(
            new[] { Entry("a.late", order: 90), Entry("a.early", order: 7) }, NoRenames);

        Assert.Equal(new[] { "a.early", "a.late" }, loaded.UnlockedInOrder);
        Assert.Equal(new[] { 1, 2 }, loaded.ToEntries()
            .Where(e => e.Order > 0)
            .OrderBy(e => e.Order)
            .Select(e => e.Order));
    }

    // --- Renames: ids stay changeable until first-party registration ---

    [Fact]
    public void FromEntries_RenameMap_MapsOldIdToNewPreservingOrderAndProgress()
    {
        var renames = new Dictionary<string, string> { ["a.old"] = "a.new" };

        AchievementRecord loaded = AchievementRecord.FromEntries(
            new[] { Entry("a.old", order: 1, progress: 5) }, renames);

        Assert.True(loaded.IsUnlocked("a.new"));
        Assert.False(loaded.IsUnlocked("a.old"));
        Assert.Equal(5, loaded.ProgressFor("a.new"));
    }

    [Fact]
    public void FromEntries_RenameMap_OldAndNewBothPresent_CollapseToOne()
    {
        var renames = new Dictionary<string, string> { ["a.old"] = "a.new" };

        AchievementRecord loaded = AchievementRecord.FromEntries(
            new[] { Entry("a.old", order: 3, progress: 2), Entry("a.new", progress: 8) },
            renames);

        Assert.Single(loaded.ToEntries());
        Assert.Equal(8, loaded.ProgressFor("a.new"));
        Assert.True(loaded.IsUnlocked("a.new"));
    }

    [Fact]
    public void RenameMap_NoValueIsAlsoAKey()
    {
        // Rename chains are not followed — the map is applied once, at
        // depth 1. An author adding a second hop must collapse it into a
        // single old -> current entry instead.
        foreach (KeyValuePair<string, string> pair in AchievementRenames.Map)
        {
            Assert.False(
                AchievementRenames.Map.ContainsKey(pair.Value),
                $"Rename target '{pair.Value}' is itself a rename key — collapse the chain.");
        }
    }
}
