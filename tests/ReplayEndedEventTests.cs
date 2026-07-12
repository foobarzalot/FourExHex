using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Tests for <see cref="GameController.ReplayEnded"/> — the completion
/// signal the Instructions demo player uses to loop hands-free playback
/// (re-calling <see cref="GameController.BeginReplay"/> on each end).
/// </summary>
public class ReplayEndedEventTests
{
    [Fact]
    public void ReplayEnded_FiresOncePerPlayback_AndReplayIsRestartable()
    {
        var pacer = new QueuedAiPacer();
        ControllerHarness h = TestHelpers.BuildControllerGame(aiPacer: pacer);
        pacer.DrainAll();

        // A minimal but real script: a select beat and an end-turn.
        h.Controller.RecordTutorialOnlyBeat(new ReplaySelectTerritoryBeat
        {
            Anchor = HexCoord.FromOffset(1, 1),
        });
        h.Hud.ClickEndTurn();
        pacer.DrainAll();

        int ended = 0;
        h.Controller.ReplayEnded += () => ended++;

        h.Controller.BeginReplay();
        Assert.Equal(0, ended);          // not raised until playback finishes
        pacer.DrainAll();
        Assert.Equal(1, ended);
        Assert.False(h.Controller.IsReplayMode);

        // Loop: BeginReplay is re-callable and signals completion again.
        h.Controller.BeginReplay();
        Assert.True(h.Controller.IsReplayMode);
        pacer.DrainAll();
        Assert.Equal(2, ended);
        Assert.False(h.Controller.IsReplayMode);
    }
}
