// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using Xunit;

namespace FourExHex.Tests;

// When is a layout "done reflowing"? Not after a fixed frame count: one window
// resize fans out into DisplayScale writing ContentScaleFactor (which re-fires
// SizeChanged) → SafeArea → OrientationHud.ApplyLayout → deferred fit rebuilds
// that can chain three deep. The harness instead watches the layout epoch and
// waits for it to hold still. Pure state machine, so it is pinned here.
public class LayoutSettlePolicyTests
{
    private const int StableFrames = 3;
    private const int MaxFrames = 30;

    private static LayoutSettlePolicy NewPolicy() => new(StableFrames, MaxFrames);

    [Fact]
    public void EpochHoldingStillForTheRequiredFrames_Settles()
    {
        LayoutSettlePolicy policy = NewPolicy();

        Assert.Equal(SettleState.Waiting, policy.Observe(epoch: 7));
        Assert.Equal(SettleState.Waiting, policy.Observe(epoch: 7));
        Assert.Equal(SettleState.Settled, policy.Observe(epoch: 7));
    }

    [Fact]
    public void AChangingEpoch_RestartsTheStableCount()
    {
        LayoutSettlePolicy policy = NewPolicy();

        policy.Observe(epoch: 1);
        policy.Observe(epoch: 1);
        Assert.Equal(SettleState.Waiting, policy.Observe(epoch: 2));  // churned — start over
        Assert.Equal(SettleState.Waiting, policy.Observe(epoch: 2));
        Assert.Equal(SettleState.Settled, policy.Observe(epoch: 2));
    }

    // A layout that never converges is a finding in its own right — the
    // play-config fit already warns after 3 rebuild chains — so the cap reports
    // Stalled rather than quietly proceeding as if settled.
    [Fact]
    public void AnEpochThatNeverStops_StallsAtTheCap()
    {
        LayoutSettlePolicy policy = NewPolicy();
        SettleState state = SettleState.Waiting;

        for (int i = 0; i < MaxFrames; i++)
        {
            state = policy.Observe(epoch: i);
        }

        Assert.Equal(SettleState.Stalled, state);
    }

    [Fact]
    public void StalledIsReportedOnce_ThenTheMachineIsDone()
    {
        LayoutSettlePolicy policy = NewPolicy();
        for (int i = 0; i < MaxFrames; i++) policy.Observe(epoch: i);

        Assert.Equal(SettleState.Stalled, policy.Observe(epoch: 9999));
        Assert.True(policy.IsFinished);
    }

    [Fact]
    public void Reset_MakesThePolicyReusableForTheNextCell()
    {
        LayoutSettlePolicy policy = NewPolicy();
        for (int i = 0; i < MaxFrames; i++) policy.Observe(epoch: i);
        Assert.True(policy.IsFinished);

        policy.Reset();

        Assert.False(policy.IsFinished);
        Assert.Equal(SettleState.Waiting, policy.Observe(epoch: 5));
    }

    [Fact]
    public void FramesWaited_TracksProgressForTheStallDiagnostic()
    {
        LayoutSettlePolicy policy = NewPolicy();

        policy.Observe(epoch: 3);
        policy.Observe(epoch: 3);

        Assert.Equal(2, policy.FramesWaited);
    }

    // A single stable frame is enough when the caller asks for it; the harness
    // uses 3, but screens that never relayout shouldn't cost 3 frames each.
    [Fact]
    public void StableFramesOfOne_SettlesImmediately()
    {
        var policy = new LayoutSettlePolicy(stableFrames: 1, maxFrames: MaxFrames);

        Assert.Equal(SettleState.Settled, policy.Observe(epoch: 42));
    }
}
