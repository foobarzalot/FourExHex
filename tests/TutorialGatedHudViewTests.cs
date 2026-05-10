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
    public void BuyPeasantClick_RejectsWhenNextBeatIsEndTurn()
    {
        // Setup() builds a tutorial of EndTurnBeat(s); BuyPeasant is the
        // wrong action for that next beat → reject + don't forward.
        (TutorialGatedHudView gated, MockHudView real, TutorialPlayer player) = Setup();
        bool forwarded = false;
        bool rejected = false;
        gated.BuyPeasantClicked += () => forwarded = true;
        player.PlayerActionRejected += (_, _) => rejected = true;

        real.ClickBuyPeasant();

        Assert.False(forwarded);
        Assert.True(rejected);
        Assert.False(player.IsArmedForBuyPeasant);
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

    private static (TutorialGatedHudView gated, MockHudView real, TutorialPlayer player)
        SetupBuyPeasant(HexCoord at)
    {
        var tutorial = new Tutorial
        {
            Beats = new List<Beat>
            {
                new BuyPeasantBeat { Index = 0, Turn = 1, Actor = 0, At = at },
            },
        };
        var player = new TutorialPlayer(tutorial);
        var real = new MockHudView();
        var gated = new TutorialGatedHudView(real, player);
        return (gated, real, player);
    }

    [Fact]
    public void BuyPeasantClick_Forwards_AndArms_WhenNextBeatIsBuyPeasant()
    {
        (TutorialGatedHudView gated, MockHudView real, TutorialPlayer player) =
            SetupBuyPeasant(new HexCoord(2, 3));
        bool forwarded = false;
        gated.BuyPeasantClicked += () => forwarded = true;

        real.ClickBuyPeasant();

        Assert.True(forwarded);
        Assert.True(player.IsArmedForBuyPeasant);
        Assert.Equal(-1, player.CurrentBeatIndex);
    }

    [Fact]
    public void BuyPeasantClick_SecondClick_RejectsAndDoesNotForward()
    {
        (TutorialGatedHudView gated, MockHudView real, TutorialPlayer player) =
            SetupBuyPeasant(new HexCoord(2, 3));
        real.ClickBuyPeasant();
        int forwardedCount = 0;
        gated.BuyPeasantClicked += () => forwardedCount++;
        bool rejected = false;
        player.PlayerActionRejected += (_, _) => rejected = true;

        real.ClickBuyPeasant();

        Assert.Equal(0, forwardedCount);
        Assert.True(rejected);
        Assert.True(player.IsArmedForBuyPeasant);
    }

    [Fact]
    public void CancelAction_Disarms_AndForwardsCancel()
    {
        (TutorialGatedHudView gated, MockHudView real, TutorialPlayer player) =
            SetupBuyPeasant(new HexCoord(2, 3));
        real.ClickBuyPeasant();
        Assert.True(player.IsArmedForBuyPeasant);
        bool forwardedCancel = false;
        gated.CancelActionPressed += () => forwardedCancel = true;

        real.PressCancelAction();

        Assert.True(forwardedCancel);
        Assert.False(player.IsArmedForBuyPeasant);
    }
}
