// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Award arithmetic for <see cref="AchievementTracker"/>: what a single
/// observable event does to the stored record. The controller-side guards
/// that decide <em>whether</em> an event is raised at all are covered by
/// <see cref="AchievementAwardTests"/>.
/// </summary>
public class AchievementTrackerTests
{
    private const string Veteran = AchievementCatalog.Veteran;

    [Fact]
    public void FirstWin_ReportsOneOfThree_AndDoesNotUnlock()
    {
        var store = new FakeAchievementStore();
        var tracker = new AchievementTracker(store);

        IReadOnlyList<string> unlocked = tracker.OnEvent(AchievementTestEvents.HumanWin());

        Assert.Equal((Veteran, 1, 3), Assert.Single(store.ProgressReports));
        Assert.Empty(store.Unlocks);
        Assert.Empty(unlocked);
    }

    [Fact]
    public void ThirdWin_UnlocksAndReturnsTheId()
    {
        var store = new FakeAchievementStore();
        var tracker = new AchievementTracker(store);
        tracker.OnEvent(AchievementTestEvents.HumanWin());
        tracker.OnEvent(AchievementTestEvents.HumanWin());

        IReadOnlyList<string> unlocked = tracker.OnEvent(AchievementTestEvents.HumanWin());

        Assert.Equal(new[] { Veteran }, unlocked);
        Assert.Equal(new[] { Veteran }, store.Unlocks);
        Assert.Equal(new[] { (Veteran, 1, 3), (Veteran, 2, 3), (Veteran, 3, 3) },
            store.ProgressReports);
    }

    [Fact]
    public void FourthWin_DoesNotUnlockAgainOrReportProgress()
    {
        var store = new FakeAchievementStore();
        var tracker = new AchievementTracker(store);
        for (int i = 0; i < 3; i++) tracker.OnEvent(AchievementTestEvents.HumanWin());
        store.ClearCallLog();

        IReadOnlyList<string> unlocked = tracker.OnEvent(AchievementTestEvents.HumanWin());

        Assert.Empty(unlocked);
        Assert.Equal(0, store.TotalCalls);
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
    public void AlreadyUnlockedBeforeTracker_IsLeftAlone()
    {
        // A record loaded from disk with the achievement already earned.
        var store = new FakeAchievementStore();
        store.Unlock(Veteran);
        store.ClearCallLog();
        var tracker = new AchievementTracker(store);

        Assert.Empty(tracker.OnEvent(AchievementTestEvents.HumanWin()));
        Assert.Equal(0, store.TotalCalls);
    }
}
