using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

public class TutorialGatedHudViewTests
{
    private static (TutorialGatedHudView gated, MockHudView real, TutorialPlayer player)
        Setup(int beatCount = 1)
    {
        var beats = new List<Beat>();
        for (int i = 0; i < beatCount; i++)
            beats.Add(new EndTurnBeat { Index = i, Turn = 1, Actor = 0 });
        var tutorial = new Tutorial { Beats = beats };
        var player = new TutorialPlayer(tutorial);
        var real = new MockHudView();
        var gated = new TutorialGatedHudView(real, player);
        return (gated, real, player);
    }

    [Fact]
    public void EndTurnClick_Forwards_WhenNextBeatIsEndTurn()
    {
        (TutorialGatedHudView gated, MockHudView real, TutorialPlayer player) = Setup();
        bool forwarded = false;
        gated.EndTurnClicked += () => forwarded = true;

        real.ClickEndTurn();

        Assert.True(forwarded);
        Assert.Equal(0, player.CurrentBeatIndex);
    }

    [Fact]
    public void EndTurnClick_DoesNotForward_AfterTutorialFinished()
    {
        (TutorialGatedHudView gated, MockHudView real, TutorialPlayer player) = Setup();
        real.ClickEndTurn();   // exhaust the only beat
        bool forwardedSecond = false;
        gated.EndTurnClicked += () => forwardedSecond = true;
        bool rejected = false;
        player.PlayerActionRejected += (_, _) => rejected = true;

        real.ClickEndTurn();

        Assert.False(forwardedSecond);
        Assert.True(rejected);
    }

    [Fact]
    public void BuyPeasantClick_AlwaysRejects_InPhase3c()
    {
        (TutorialGatedHudView gated, MockHudView real, TutorialPlayer player) = Setup();
        bool forwarded = false;
        bool rejected = false;
        gated.BuyPeasantClicked += () => forwarded = true;
        player.PlayerActionRejected += (_, _) => rejected = true;

        real.ClickBuyPeasant();

        Assert.False(forwarded);
        Assert.True(rejected);
    }

    [Fact]
    public void UndoLastClick_PassesThrough_Unchanged()
    {
        (TutorialGatedHudView gated, MockHudView real, _) = Setup();
        bool forwarded = false;
        gated.UndoLastClicked += () => forwarded = true;

        real.ClickUndoLast();

        Assert.True(forwarded);
    }

    [Fact]
    public void OutputMethods_DelegateToReal()
    {
        (TutorialGatedHudView gated, MockHudView real, _) = Setup();

        gated.SetMapLabel("hello");
        gated.ShowTutorialMessage("toast");

        Assert.Equal("hello", real.LastSetMapLabel);
        Assert.Equal("toast", real.CurrentTutorialMessage);
    }
}
