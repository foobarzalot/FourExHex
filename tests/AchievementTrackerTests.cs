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
        var store = new FakeAchievementStore();
        var tracker = new AchievementTracker(store);
        tracker.OnEvent(AchievementTestEvents.HumanWin());
        tracker.OnEvent(AchievementTestEvents.HumanWin());

        IReadOnlyList<string> unlocked = tracker.OnEvent(AchievementTestEvents.HumanWin());

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
        for (int i = 0; i < 3; i++) tracker.OnEvent(AchievementTestEvents.HumanWin());
        store.ClearCallLog();

        IReadOnlyList<string> unlocked = tracker.OnEvent(AchievementTestEvents.HumanWin());

        Assert.DoesNotContain(Veteran, unlocked);
        Assert.Empty(VeteranReports(store));
    }

    [Fact]
    public void Progress_NeverExceedsTarget()
    {
        var store = new FakeAchievementStore();
        var tracker = new AchievementTracker(store);

        for (int i = 0; i < 5; i++) tracker.OnEvent(AchievementTestEvents.HumanWin());

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
}
