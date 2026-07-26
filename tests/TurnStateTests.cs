// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

public class TurnStateTests
{
    private static List<Player> MakePlayers(int count)
    {
        var players = new List<Player>();
        for (int i = 0; i < count; i++)
        {
            players.Add(new Player($"P{i}", PlayerId.FromIndex(i)));
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
    public void EndTurn_AfterLastPlayer_LandsOnNeutralSeat_SameTurn()
    {
        var state = new TurnState(MakePlayers(4));

        state.EndTurn(); // P0 -> P1
        state.EndTurn(); // P1 -> P2
        state.EndTurn(); // P2 -> P3
        state.EndTurn(); // P3 -> Neutral seat, still turn 1

        Assert.Equal(4, state.CurrentPlayerIndex);
        Assert.Equal(1, state.TurnNumber);
        Assert.True(state.IsNeutralSeat);
        Assert.Same(Player.Neutral, state.CurrentPlayer);
    }

    [Fact]
    public void EndTurn_AfterNeutralSeat_WrapsAndIncrementsTurnNumber()
    {
        var state = new TurnState(MakePlayers(4));

        for (int i = 0; i < 5; i++)
        {
            state.EndTurn(); // 4 players + the neutral seat = one full round
        }

        Assert.Equal(0, state.CurrentPlayerIndex);
        Assert.Equal(2, state.TurnNumber);
        Assert.False(state.IsNeutralSeat);
    }

    [Fact]
    public void EndTurn_MultipleFullRotations_TurnCountsCorrectly()
    {
        var state = new TurnState(MakePlayers(4));

        // 15 end-turns = 3 full rotations of 5 seats (4 players + neutral)
        // = turn 4, index 0.
        for (int i = 0; i < 15; i++)
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
    [InlineData(4)] // the neutral seat itself is still part of turn 1
    public void EndTurn_WithinRotation_DoesNotAdvanceTurnNumber(int endTurns)
    {
        // k end-turns where k < SeatCount keeps us on turn 1.
        var state = new TurnState(MakePlayers(4));

        for (int i = 0; i < endTurns; i++)
        {
            state.EndTurn();
        }

        Assert.Equal(1, state.TurnNumber);
        Assert.Equal(endTurns, state.CurrentPlayerIndex);
    }

    [Fact]
    public void SeatCount_IsPlayersPlusNeutral()
    {
        Assert.Equal(5, new TurnState(MakePlayers(4)).SeatCount);
        Assert.Equal(7, new TurnState(MakePlayers(6)).SeatCount);
    }

    [Fact]
    public void RestoreCtor_AcceptsNeutralSeatIndex()
    {
        List<Player> players = MakePlayers(4);

        var state = new TurnState(players, currentPlayerIndex: 4, turnNumber: 7);

        Assert.True(state.IsNeutralSeat);
        Assert.Same(Player.Neutral, state.CurrentPlayer);
        Assert.Equal(7, state.TurnNumber);
    }

    [Fact]
    public void NeutralSingleton_IsAiDrivenAndOwnsNoColor()
    {
        Assert.True(Player.Neutral.Id.IsNone);
        Assert.Equal(PlayerKind.Neutral, Player.Neutral.Kind);
        Assert.True(Player.Neutral.IsAi);
        Assert.Equal("Neutral", Player.Neutral.Name);
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
        var color = PlayerId.FromIndex(0);

        var player = new Player("Crimson", color);

        Assert.Equal("Crimson", player.Name);
        Assert.Equal(color, player.Id);
    }
}
