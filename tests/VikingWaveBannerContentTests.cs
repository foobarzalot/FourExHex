// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// The wave-banner copy rules (user-specified): countdown
/// "Wave X/Y arriving in N turns" (singular "turn" at N==1; "Final wave"
/// replaces "Wave X/Y" for the last wave), and the bare spawn message
/// "Wave X/Y" / "Final wave" on the round a wave just spawned. Null when
/// nothing is left to announce or outside Viking Raiders.
/// </summary>
public class VikingWaveBannerContentTests
{
    private static readonly PlayerId Red = PlayerId.FromIndex(0);

    private static GameState MakeState(GameMode mode = GameMode.VikingRaiders, int turnNumber = 1)
    {
        var players = new List<Player> { new Player("Red", Red) };
        HexGrid grid = TestHelpers.BuildRectGrid(3, 3, Red);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        return new GameState(
            grid, territories, players, new TurnState(players, 0, turnNumber), new Treasury(),
            waterCoords: new HashSet<HexCoord> { HexCoord.FromOffset(3, 1) },
            mode: mode);
    }

    [Fact]
    public void For_NullOutsideVikingRaiders()
    {
        Assert.Null(VikingWaveBannerContent.For(MakeState(GameMode.Freeform)));
    }

    [Fact]
    public void For_CountdownPluralAndSingular()
    {
        // FirstWaveRound = 3: turn 1 → 2 turns out, turn 2 → 1 turn out.
        int total = VikingRaidersRules.TotalWaves;
        GameState state = MakeState(turnNumber: VikingRaidersRules.FirstWaveRound - 2);
        Assert.Equal($"Wave 1/{total} arriving in 2 turns", VikingWaveBannerContent.For(state));

        state = MakeState(turnNumber: VikingRaidersRules.FirstWaveRound - 1);
        Assert.Equal($"Wave 1/{total} arriving in 1 turn", VikingWaveBannerContent.For(state));
    }

    [Fact]
    public void For_MidScheduleWaveNumber()
    {
        int total = VikingRaidersRules.TotalWaves;
        GameState state = MakeState(turnNumber: VikingRaidersRules.FirstWaveRound + 1);
        state.Vikings.NextWaveIndex = 1; // wave 2 is next, due at round 6 → 2 away
        Assert.Equal(
            $"Wave 2/{total} arriving in 2 turns", VikingWaveBannerContent.For(state));
    }

    [Fact]
    public void For_FinalWaveCountdown()
    {
        GameState state = MakeState(turnNumber:
            VikingRaidersRules.FirstWaveRound
            + (VikingRaidersRules.TotalWaves - 1) * VikingRaidersRules.WaveIntervalRounds - 1);
        state.Vikings.NextWaveIndex = VikingRaidersRules.TotalWaves - 1;
        Assert.Equal("Final wave arriving in 1 turn", VikingWaveBannerContent.For(state));
    }

    [Fact]
    public void For_SpawnMessage_WaveOffshore()
    {
        int total = VikingRaidersRules.TotalWaves;
        GameState state = MakeState(turnNumber: VikingRaidersRules.FirstWaveRound);
        state.Vikings.AddAtSea(new SeaViking(HexCoord.FromOffset(3, 1), UnitLevel.Recruit));
        state.Vikings.NextWaveIndex = 1; // wave 1 just spawned this round
        state.Vikings.LastSpawnRound = state.Turns.TurnNumber;

        Assert.Equal($"Wave 1/{total}", VikingWaveBannerContent.For(state));
    }

    [Fact]
    public void For_SpawnMessage_FinalWave()
    {
        GameState state = MakeState(turnNumber: 18);
        state.Vikings.AddAtSea(new SeaViking(HexCoord.FromOffset(3, 1), UnitLevel.Captain));
        state.Vikings.NextWaveIndex = VikingRaidersRules.TotalWaves;
        state.Vikings.LastSpawnRound = state.Turns.TurnNumber;

        Assert.Equal("Final wave", VikingWaveBannerContent.For(state));
    }

    [Fact]
    public void For_NullWhenNothingLeftToAnnounce()
    {
        // Schedule exhausted, sea empty: no countdown, no spawn message —
        // even if landed raiders are still fighting on the island.
        GameState state = MakeState(turnNumber: 20);
        state.Vikings.NextWaveIndex = VikingRaidersRules.TotalWaves;
        state.Vikings.LastSpawnRound = 18;

        Assert.Null(VikingWaveBannerContent.For(state));
    }
}
