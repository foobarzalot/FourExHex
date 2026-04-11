using System.Collections.Generic;
using Godot;
using Xunit;

namespace FourExHex.Tests;

public class TurnStateTests
{
    private static List<Player> MakePlayers(int count)
    {
        var players = new List<Player>();
        for (int i = 0; i < count; i++)
        {
            players.Add(new Player($"P{i}", new Color(i / 10f, 0f, 0f)));
        }
        return players;
    }

    [Fact]
    public void InitialState_FirstPlayerAndTurnOne()
    {
        List<Player> players = MakePlayers(4);

        var state = new TurnState(players);

        Assert.Equal(0, state.CurrentPlayerIndex);
        Assert.Equal(1, state.TurnNumber);
        Assert.Same(players[0], state.CurrentPlayer);
    }

    [Fact]
    public void EndTurn_AdvancesToNextPlayer()
    {
        var state = new TurnState(MakePlayers(4));

        state.EndTurn();

        Assert.Equal(1, state.CurrentPlayerIndex);
        Assert.Equal(1, state.TurnNumber);
    }

    [Fact]
    public void EndTurn_AfterLastPlayer_WrapsAndIncrementsTurnNumber()
    {
        var state = new TurnState(MakePlayers(4));

        state.EndTurn(); // P0 -> P1
        state.EndTurn(); // P1 -> P2
        state.EndTurn(); // P2 -> P3
        state.EndTurn(); // P3 -> P0, turn 2

        Assert.Equal(0, state.CurrentPlayerIndex);
        Assert.Equal(2, state.TurnNumber);
    }

    [Fact]
    public void EndTurn_MultipleFullRotations_TurnCountsCorrectly()
    {
        var state = new TurnState(MakePlayers(4));

        // 12 end-turns = 3 full rotations of 4 players = turn 4, index 0.
        for (int i = 0; i < 12; i++)
        {
            state.EndTurn();
        }

        Assert.Equal(0, state.CurrentPlayerIndex);
        Assert.Equal(4, state.TurnNumber);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void EndTurn_WithinRotation_DoesNotAdvanceTurnNumber(int endTurns)
    {
        // k end-turns where k < Players.Count keeps us on turn 1.
        var state = new TurnState(MakePlayers(4));

        for (int i = 0; i < endTurns; i++)
        {
            state.EndTurn();
        }

        Assert.Equal(1, state.TurnNumber);
        Assert.Equal(endTurns, state.CurrentPlayerIndex);
    }

    [Fact]
    public void CurrentPlayer_MatchesPlayersAtCurrentIndex()
    {
        List<Player> players = MakePlayers(6);
        var state = new TurnState(players);

        state.EndTurn();
        state.EndTurn();
        state.EndTurn();

        Assert.Same(players[3], state.CurrentPlayer);
        Assert.Same(players[state.CurrentPlayerIndex], state.CurrentPlayer);
    }

    [Fact]
    public void Player_ConstructorStoresNameAndColor()
    {
        var color = new Color(0.2f, 0.4f, 0.6f);

        var player = new Player("Crimson", color);

        Assert.Equal("Crimson", player.Name);
        Assert.Equal(color, player.Color);
    }
}
