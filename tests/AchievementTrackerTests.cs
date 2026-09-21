// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Award arithmetic for <see cref="AchievementTracker"/>: what a single
/// observable event does to the stored record, pinned through the Veteran
/// counter (the assertions filter to it — the same event legitimately
/// advances other catalog rows). The controller-side guards that decide
/// <em>whether</em> an event is raised at all are covered by
/// <see cref="AchievementAwardTests"/>.
/// </summary>
public class AchievementTrackerTests
{
    private const string Veteran = AchievementCatalog.Veteran;

    private static IEnumerable<(string Id, int Current, int Target)> VeteranReports(
        FakeAchievementStore store)
        => store.ProgressReports.Where(r => r.Id == Veteran);

    [Fact]
    public void FirstWin_ReportsOneOfThree_AndDoesNotUnlockTheCounter()
    {
        var store = new FakeAchievementStore();
        var tracker = new AchievementTracker(store);

        IReadOnlyList<string> unlocked = tracker.OnEvent(AchievementTestEvents.HumanWin());

        Assert.Equal((Veteran, 1, 3), Assert.Single(VeteranReports(store)));
        Assert.DoesNotContain(Veteran, store.Unlocks);
        Assert.DoesNotContain(Veteran, unlocked);
    }

    [Fact]
    public void ThirdWin_UnlocksAndReturnsTheId()
    {
        // Three wins on three different levels — one level counts once.
        var store = new FakeAchievementStore();
        var tracker = new AchievementTracker(store);
        tracker.OnEvent(AchievementTestEvents.HumanWin(level: 0));
        tracker.OnEvent(AchievementTestEvents.HumanWin(level: 1));

        IReadOnlyList<string> unlocked = tracker.OnEvent(AchievementTestEvents.HumanWin(level: 2));

        Assert.Contains(Veteran, unlocked);
        Assert.Contains(Veteran, store.Unlocks);
        Assert.Equal(new[] { (Veteran, 1, 3), (Veteran, 2, 3), (Veteran, 3, 3) },
            VeteranReports(store));
    }

    [Fact]
    public void FourthWin_DoesNotUnlockAgainOrReportProgress()
    {
        var store = new FakeAchievementStore();
        var tracker = new AchievementTracker(store);
        for (int i = 0; i < 3; i++) tracker.OnEvent(AchievementTestEvents.HumanWin(level: i));
        store.ClearCallLog();

        IReadOnlyList<string> unlocked = tracker.OnEvent(AchievementTestEvents.HumanWin(level: 3));

        Assert.DoesNotContain(Veteran, unlocked);
        Assert.Empty(VeteranReports(store));
    }

    [Fact]
    public void Progress_NeverExceedsTarget()
    {
        var store = new FakeAchievementStore();
        var tracker = new AchievementTracker(store);

        for (int i = 0; i < 5; i++) tracker.OnEvent(AchievementTestEvents.HumanWin(level: i));

        foreach ((string _, int current, int target) in store.ProgressReports)
        {
            Assert.True(current <= target);
        }
        Assert.Equal(3, store.ProgressFor(Veteran));
    }

    [Fact]
    public void StoreWithEverythingUnlocked_SeesNoWritesAtAll()
    {
        // A record loaded from disk with every achievement already earned.
        var store = new FakeAchievementStore();
        foreach (AchievementDefinition def in AchievementCatalog.All)
        {
            store.Unlock(def.Id);
        }
        store.ClearCallLog();
        var tracker = new AchievementTracker(store);

        Assert.Empty(tracker.OnEvent(AchievementTestEvents.HumanWin()));
        Assert.Equal(0, store.TotalCalls);
    }

    // --- Per-level credit: a campaign level contributes its best run once ---

    [Fact]
    public void SameLevelTwice_SecondWinAddsNothing()
    {
        var store = new FakeAchievementStore();
        var tracker = new AchievementTracker(store);
        tracker.OnEvent(AchievementTestEvents.HumanWin(level: 5));
        store.ClearCallLog();

        tracker.OnEvent(AchievementTestEvents.HumanWin(level: 5));

        Assert.Equal(0, store.TotalCalls);
        Assert.Equal(1, store.ProgressFor(Veteran));
    }

    [Fact]
    public void DifferentLevel_AdvancesAndCreditsThatLevel()
    {
        var store = new FakeAchievementStore();
        var tracker = new AchievementTracker(store);
        tracker.OnEvent(AchievementTestEvents.HumanWin(level: 5));

        tracker.OnEvent(AchievementTestEvents.HumanWin(level: 6));

        Assert.Equal(new[] { (Veteran, 1, 3), (Veteran, 2, 3) }, VeteranReports(store));
        Assert.Contains((Veteran, 5, 1), store.Credits);
        Assert.Contains((Veteran, 6, 1), store.Credits);
    }

    [Fact]
    public void VikingSlayer_ALevelContributesItsBestRun_OnlyTheImprovementIsAdded()
    {
        const string slayer = AchievementCatalog.VikingSlayer;
        var store = new FakeAchievementStore();
        var tracker = new AchievementTracker(store);

        tracker.OnEvent(AchievementTestEvents.HumanLoss(level: 9) with { VikingKills = 5 });
        Assert.Equal(5, store.ProgressFor(slayer));

        tracker.OnEvent(AchievementTestEvents.HumanWin(level: 9) with { VikingKills = 3 });
        Assert.Equal(5, store.ProgressFor(slayer));
        Assert.Equal(5, store.CreditFor(slayer, 9));

        tracker.OnEvent(AchievementTestEvents.HumanWin(level: 9) with { VikingKills = 8 });
        Assert.Equal(8, store.ProgressFor(slayer));
        Assert.Equal(8, store.CreditFor(slayer, 9));
        Assert.Equal((slayer, 8, 50), store.ProgressReports.Last(r => r.Id == slayer));
    }

    [Fact]
    public void Credit_SurvivesARestart_SoTheReloadedRecordStillSkips()
    {
        var before = new FakeAchievementStore();
        new AchievementTracker(before).OnEvent(AchievementTestEvents.HumanWin(level: 5));
        AchievementRecord reloaded = AchievementSerializer.Deserialize(
            AchievementSerializer.Serialize(before.Record));

        var after = new FakeAchievementStore(reloaded);
        new AchievementTracker(after).OnEvent(AchievementTestEvents.HumanWin(level: 5));

        Assert.Equal(0, after.TotalCalls);
        Assert.Equal(1, after.ProgressFor(Veteran));
    }

    [Fact]
    public void AlreadyUnlockedRow_RecordsNoCredit()
    {
        var store = new FakeAchievementStore();
        store.Unlock(Veteran);
        store.ClearCallLog();
        var tracker = new AchievementTracker(store);

        tracker.OnEvent(AchievementTestEvents.HumanWin(level: 5));

        Assert.DoesNotContain(store.Credits, c => c.Id == Veteran);
    }
}
