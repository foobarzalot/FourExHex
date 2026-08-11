// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// FIFO policy for the achievement unlock toasts (issue #235): one toast
/// in flight at a time, the rest queued and delivered in order. Pure
/// policy — the tween timing lives in the Godot-side HudView shell.
/// </summary>
public class AchievementToastQueueTests
{
    [Fact]
    public void EnqueueWhenIdle_ReturnsTheTextImmediately_AndMarksShowing()
    {
        var queue = new AchievementToastQueue();

        Assert.Equal("first", queue.Enqueue("first"));
        Assert.True(queue.IsShowing);
        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public void EnqueueWhileShowing_Queues_AndReturnsNull()
    {
        var queue = new AchievementToastQueue();
        queue.Enqueue("first");

        Assert.Null(queue.Enqueue("second"));
        Assert.Null(queue.Enqueue("third"));
        Assert.True(queue.IsShowing);
        Assert.Equal(2, queue.PendingCount);
    }

    [Fact]
    public void OnToastFinished_DrainsInFifoOrder()
    {
        var queue = new AchievementToastQueue();
        queue.Enqueue("first");
        queue.Enqueue("second");
        queue.Enqueue("third");

        Assert.Equal("second", queue.OnToastFinished());
        Assert.Equal("third", queue.OnToastFinished());
        Assert.True(queue.IsShowing); // "third" is still on screen
        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public void OnToastFinished_WhenEmpty_ReturnsNull_AndClearsShowing()
    {
        var queue = new AchievementToastQueue();
        queue.Enqueue("only");

        Assert.Null(queue.OnToastFinished());
        Assert.False(queue.IsShowing);
    }

    [Fact]
    public void AfterDraining_TheNextEnqueueShowsImmediatelyAgain()
    {
        var queue = new AchievementToastQueue();
        queue.Enqueue("first");
        queue.OnToastFinished();

        Assert.Equal("later", queue.Enqueue("later"));
    }

    [Fact]
    public void Clear_DropsPendingAndTheInFlightMark()
    {
        var queue = new AchievementToastQueue();
        queue.Enqueue("first");
        queue.Enqueue("second");

        queue.Clear();

        Assert.False(queue.IsShowing);
        Assert.Equal(0, queue.PendingCount);
        Assert.Equal("fresh", queue.Enqueue("fresh"));
    }
}
