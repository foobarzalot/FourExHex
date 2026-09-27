// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Pins the campaign confirm sheet's action set per stored-attempt state.
/// The first action is the primary (Enter / the default button); Cancel is
/// always present and is the sheet's own, so it never appears here.
/// </summary>
public class CampaignSheetActionsTests
{
    [Fact]
    public void NoAttempt_OffersPlayOnly() =>
        Assert.Equal(new[] { CampaignSheetAction.Play },
            CampaignSheetActions.For(CampaignAttemptKind.None, canReplay: false));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Unfinished_OffersContinueThenRestart(bool canReplay) =>
        Assert.Equal(new[] { CampaignSheetAction.Continue, CampaignSheetAction.Restart },
            CampaignSheetActions.For(CampaignAttemptKind.Unfinished, canReplay));

    [Fact]
    public void Finished_WithReplay_OffersWatchReplayThenRestart() =>
        Assert.Equal(new[] { CampaignSheetAction.WatchReplay, CampaignSheetAction.Restart },
            CampaignSheetActions.For(CampaignAttemptKind.Finished, canReplay: true));

    [Fact]
    public void Finished_WithoutReplay_OffersRestartOnly() =>
        Assert.Equal(new[] { CampaignSheetAction.Restart },
            CampaignSheetActions.For(CampaignAttemptKind.Finished, canReplay: false));

    [Fact]
    public void Restart_ConfirmsOnlyWhenDiscardingAnUnfinishedAttempt()
    {
        Assert.True(CampaignSheetActions.RestartNeedsConfirm(CampaignAttemptKind.Unfinished));
        Assert.False(CampaignSheetActions.RestartNeedsConfirm(CampaignAttemptKind.Finished));
        Assert.False(CampaignSheetActions.RestartNeedsConfirm(CampaignAttemptKind.None));
    }

    [Theory]
    [InlineData(0, true, 1)]
    [InlineData(0x40, true, 0x41)]
    [InlineData(0x40, false, 0x3F)]
    [InlineData(CampaignProgress.LevelCount - 1, false, CampaignProgress.LevelCount - 2)]
    public void Neighbor_StepsOneLevelInsideTheLadder(int level, bool forward, int expected) =>
        Assert.Equal(expected, CampaignSheetPaging.Neighbor(level, forward));

    [Fact]
    public void Neighbor_ClampsAtTheFirstLevel() =>
        Assert.Null(CampaignSheetPaging.Neighbor(0, forward: false));

    [Fact]
    public void Neighbor_ClampsAtTheLastLevel() =>
        Assert.Null(CampaignSheetPaging.Neighbor(CampaignProgress.LevelCount - 1, forward: true));
}
