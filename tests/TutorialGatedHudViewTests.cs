using System.Collections.Generic;
using Godot;
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

    private static (GameState state, SessionState session) BuildState()
    {
        // Minimal state so Refresh has a non-null GameState to forward.
        // The wrapper itself doesn't read state — it just consults
        // _player.NextExpectedPlayerBeat to decide whether to override
        // hasActionableRemaining.
        var red = new Player("Red", new Color("e53935"), AiKind.Human);
        var players = new List<Player> { red };
        HexGrid grid = TestHelpers.BuildRectGrid(2, 2, red.Color);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var turnState = new TurnState(players, currentPlayerIndex: 0, turnNumber: 1);
        var state = new GameState(grid, territories, players, turnState, new Treasury());
        var session = new SessionState();
        return (state, session);
    }

    [Fact]
    public void Refresh_NextBeatIsEndTurn_ForcesEndTurnCta()
    {
        // Setup() builds an EndTurnBeat-only tutorial. Even when the
        // controller says "you have other things to do" (hasActionable
        // = true), the wrapper overrides to false so the End Turn
        // button gets the same CTA styling ordinary play uses when the
        // turn is "done."
        (TutorialGatedHudView gated, MockHudView real, _) = Setup();
        (GameState state, SessionState session) = BuildState();

        gated.Refresh(state, session, hasActionableRemaining: true);

        Assert.False(real.LastHasActionableRemaining);
    }

    [Fact]
    public void Refresh_NextBeatIsBuyPeasant_PassesThroughUnchanged()
    {
        (TutorialGatedHudView gated, MockHudView real, _) =
            SetupBuyPeasant(new HexCoord(2, 3));
        (GameState state, SessionState session) = BuildState();

        gated.Refresh(state, session, hasActionableRemaining: true);

        Assert.True(real.LastHasActionableRemaining);
    }

    [Fact]
    public void Refresh_TutorialFinished_PassesThroughUnchanged()
    {
        (TutorialGatedHudView gated, MockHudView real, TutorialPlayer player) = Setup();
        real.ClickEndTurn();   // exhaust the only EndTurnBeat
        (GameState state, SessionState session) = BuildState();

        gated.Refresh(state, session, hasActionableRemaining: true);

        Assert.True(real.LastHasActionableRemaining);
        Assert.Null(player.NextExpectedPlayerBeat);
    }
}
