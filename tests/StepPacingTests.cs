// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Pins the distance-scaled move-travel curve and the settle delay that
/// keeps the paced beat cadence wide enough for the tween to finish
/// before the next beat's refresh rebuilds the unit layer.
/// </summary>
public class StepPacingTests
{
    [Theory]
    [InlineData(1, 200)]   // adjacent hop
    [InlineData(2, 280)]   // +80 per hex
    [InlineData(3, 360)]
    [InlineData(4, 440)]
    [InlineData(6, 600)]
    [InlineData(7, 680)]   // cap
    [InlineData(12, 680)]  // stays capped
    public void MoveTravelBaseMs_ScalesWithDistanceAndCaps(int distance, int expectedMs)
    {
        Assert.Equal(expectedMs, StepPacing.MoveTravelBaseMs(distance));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void MoveTravelBaseMs_ClampsDegenerateDistancesToOneHex(int distance)
    {
        Assert.Equal(StepPacing.MoveTravelBaseMs(1), StepPacing.MoveTravelBaseMs(distance));
    }

    [Fact]
    public void MoveSettleDelayMs_AdjacentHopKeepsTheBaselineCadence()
    {
        // travel(1)=200 + 60 margin = 260 ≤ AiActionDelayMs → baseline.
        Assert.Equal(StepPacing.AiActionDelayMs, StepPacing.MoveSettleDelayMs(1));
    }

    [Theory]
    [InlineData(2, 340)]   // travel 280 + 60 margin
    [InlineData(3, 420)]   // travel 360 + 60 margin
    [InlineData(6, 660)]
    [InlineData(7, 740)]   // capped travel 680 + 60
    [InlineData(12, 740)]
    public void MoveSettleDelayMs_LongMovesCoverTravelPlusMargin(int distance, int expectedMs)
    {
        Assert.Equal(expectedMs, StepPacing.MoveSettleDelayMs(distance));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(9)]
    public void MoveSettleDelayMs_AlwaysCoversTheTravelDuration(int distance)
    {
        Assert.True(StepPacing.MoveSettleDelayMs(distance)
            > StepPacing.MoveTravelBaseMs(distance));
    }
}
