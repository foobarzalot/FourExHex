// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Tests for pausing paced replay playback via the controller's
/// <c>isReplayPaused</c> hook: the step machine parks instead of
/// scheduling the next beat while the hook reports paused, and
/// <see cref="GameController.ResumeReplayAfterPause"/> re-kicks it —
/// how the Instructions demo freezes mid-loop during a swipe drag and
/// resumes seamlessly on spring-back.
/// </summary>
public class ReplayPauseTests
{
    [Fact]
    public void Replay_ParksWhilePaused_AndResumesWhereItStopped()
    {
        var pacer = new QueuedAiPacer();
        bool paused = false;
        ControllerHarness h = TestHelpers.BuildControllerGame(
            aiPacer: pacer, isReplayPaused: () => paused);
        pacer.DrainAll();

        h.Controller.RecordTutorialOnlyBeat(new ReplaySelectTerritoryBeat
        {
            Anchor = HexCoord.FromOffset(1, 1),
        });
        h.Hud.ClickEndTurn();
        pacer.DrainAll();

        int ended = 0;
        h.Controller.ReplayEnded += () => ended++;

        paused = true;
        h.Controller.BeginReplay();
        pacer.DrainAll();
        // Parked at the first beat: playback is alive but went nowhere.
        Assert.True(h.Controller.IsReplayMode);
        Assert.Equal(0, ended);
        Assert.False(pacer.HasPending);

        // Resume: playback picks up from the parked beat and completes.
        paused = false;
        h.Controller.ResumeReplayAfterPause();
        pacer.DrainAll();
        Assert.Equal(1, ended);
        Assert.False(h.Controller.IsReplayMode);
    }

    [Fact]
    public void ResumeWithoutPark_DoesNotDoubleStep()
    {
        var pacer = new QueuedAiPacer();
        ControllerHarness h = TestHelpers.BuildControllerGame(
            aiPacer: pacer, isReplayPaused: () => false);
        pacer.DrainAll();

        h.Hud.ClickEndTurn();
        pacer.DrainAll();

        int ended = 0;
        h.Controller.ReplayEnded += () => ended++;

        h.Controller.BeginReplay();
        // A stray resume while playback is running normally must not
        // inject a second concurrent step chain.
        h.Controller.ResumeReplayAfterPause();
        pacer.DrainAll();
        Assert.Equal(1, ended);
    }
}
