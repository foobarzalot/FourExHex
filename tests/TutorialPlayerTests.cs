using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

public class TutorialPlayerTests
{
    private static Tutorial MakeTutorial(int endTurnCount)
    {
        var beats = new List<Beat>();
        for (int i = 0; i < endTurnCount; i++)
        {
            beats.Add(new EndTurnBeat { Index = i, Turn = 1, Actor = 0 });
        }
        return new Tutorial { Beats = beats };
    }

    [Fact]
    public void Constructor_NextExpected_IsFirstBeat()
    {
        Tutorial t = MakeTutorial(1);
        var player = new TutorialPlayer(t);

        Assert.Same(t.Beats[0], player.NextExpectedPlayerBeat);
        Assert.Equal(-1, player.CurrentBeatIndex);
        Assert.Empty(player.Snapshots);
    }

    [Fact]
    public void TryAdvanceForEndTurn_HappyPath_AdvancesAndFiresEvents()
    {
        Tutorial t = MakeTutorial(2);
        var player = new TutorialPlayer(t);
        var beatApplied = new List<int>();
        bool finished = false;
        player.BeatApplied += i => beatApplied.Add(i);
        player.TutorialFinished += () => finished = true;

        bool ok = player.TryAdvanceForEndTurn();

        Assert.True(ok);
        Assert.Equal(0, player.CurrentBeatIndex);
        Assert.Equal(new[] { 0 }, beatApplied);
        Assert.False(finished);
        Assert.Same(t.Beats[1], player.NextExpectedPlayerBeat);
    }

    [Fact]
    public void TryAdvanceForEndTurn_LastBeat_FiresTutorialFinished()
    {
        Tutorial t = MakeTutorial(1);
        var player = new TutorialPlayer(t);
        bool finished = false;
        player.TutorialFinished += () => finished = true;

        bool ok = player.TryAdvanceForEndTurn();

        Assert.True(ok);
        Assert.True(finished);
        Assert.Null(player.NextExpectedPlayerBeat);
    }

    [Fact]
    public void TryAdvanceForEndTurn_AfterFinished_RejectsAndDoesNotAdvance()
    {
        Tutorial t = MakeTutorial(1);
        var player = new TutorialPlayer(t);
        player.TryAdvanceForEndTurn();   // exhaust
        Beat? rejectedBeat = new EndTurnBeat { Index = 99 };  // sentinel non-null
        string? reason = null;
        player.PlayerActionRejected += (b, r) => { rejectedBeat = b; reason = r; };

        bool ok = player.TryAdvanceForEndTurn();

        Assert.False(ok);
        Assert.Null(rejectedBeat);              // NextExpectedPlayerBeat is null
        Assert.NotNull(reason);
        Assert.Contains("complete", reason!);
    }

    [Fact]
    public void NotifyRejected_FiresEventWithExpectedBeatAndAttempt()
    {
        Tutorial t = MakeTutorial(1);
        var player = new TutorialPlayer(t);
        Beat? rejectedBeat = null;
        string? reason = null;
        player.PlayerActionRejected += (b, r) => { rejectedBeat = b; reason = r; };

        player.NotifyRejected("tile click");

        Assert.Same(t.Beats[0], rejectedBeat);
        Assert.NotNull(reason);
        Assert.Contains("tile click", reason!);
        Assert.Contains("EndTurn", reason);
    }
}
