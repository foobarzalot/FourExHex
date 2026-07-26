// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// The wave-banner copy rules (user-specified): countdown
/// "Wave X/Y arriving in N turns" (singular "turn" at N==1; "Final wave"
/// replaces "Wave X/Y" for the last wave), where N counts to the turn the
/// raiders become visible offshore — one round past the spawn's due round,
/// since the wave spawns at the due round's neutral seat. The bare arrival
/// message "Wave X/Y" / "Final wave" shows while the wave sits offshore
/// (the round after it spawned). Null when nothing is left to announce or
/// outside Viking Raiders.
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
        // FirstWaveRound = 2, offshore the round after: turn 1 → visible in
        // 2 turns; turn 2 (the due round itself) → visible next turn.
        int total = VikingRaidersRules.TotalWaves;
        GameState state = MakeState(turnNumber: VikingRaidersRules.FirstWaveRound - 1);
        Assert.Equal($"Wave 1/{total} arriving in 2 turns", VikingWaveBannerContent.For(state));

        state = MakeState(turnNumber: VikingRaidersRules.FirstWaveRound);
        Assert.Equal($"Wave 1/{total} arriving in 1 turn", VikingWaveBannerContent.For(state));
    }

    [Fact]
    public void For_MidScheduleWaveNumber()
    {
        // Wave 1 landed at the previous round's seat; wave 2 is next, due
        // at round FirstWaveRound + WaveIntervalRounds (visible one later).
        int total = VikingRaidersRules.TotalWaves;
        GameState state = MakeState(turnNumber: VikingRaidersRules.FirstWaveRound + 2);
        state.Vikings.NextWaveIndex = 1;
        Assert.Equal(
            $"Wave 2/{total} arriving in 2 turns", VikingWaveBannerContent.For(state));
    }

    [Fact]
    public void For_FinalWaveCountdown()
    {
        GameState state = MakeState(turnNumber:
            VikingRaidersRules.FirstWaveRound
            + (VikingRaidersRules.TotalWaves - 1) * VikingRaidersRules.WaveIntervalRounds);
        state.Vikings.NextWaveIndex = VikingRaidersRules.TotalWaves - 1;
        Assert.Equal("Final wave arriving in 1 turn", VikingWaveBannerContent.For(state));
    }

    [Fact]
    public void For_ArrivalMessage_WaveOffshore()
    {
        int total = VikingRaidersRules.TotalWaves;
        GameState state = MakeState(turnNumber: VikingRaidersRules.FirstWaveRound + 1);
        state.Vikings.AddAtSea(new SeaViking(HexCoord.FromOffset(3, 1), UnitLevel.Recruit));
        state.Vikings.NextWaveIndex = 1; // wave 1 spawned at last round's seat
        state.Vikings.LastSpawnRound = VikingRaidersRules.FirstWaveRound;

        Assert.Equal($"Wave 1/{total}", VikingWaveBannerContent.For(state));
    }

    [Fact]
    public void For_ArrivalMessage_FinalWave()
    {
        GameState state = MakeState(turnNumber: 18);
        state.Vikings.AddAtSea(new SeaViking(HexCoord.FromOffset(3, 1), UnitLevel.Captain));
        state.Vikings.NextWaveIndex = VikingRaidersRules.TotalWaves;
        state.Vikings.LastSpawnRound = 17;

        Assert.Equal("Final wave", VikingWaveBannerContent.For(state));
    }

    [Fact]
    public void For_NullWhenNothingLeftToAnnounce()
    {
        // Schedule exhausted, sea empty: no countdown, no arrival message —
        // even if landed raiders are still fighting on the island.
        GameState state = MakeState(turnNumber: 20);
        state.Vikings.NextWaveIndex = VikingRaidersRules.TotalWaves;
        state.Vikings.LastSpawnRound = 17;

        Assert.Null(VikingWaveBannerContent.For(state));
    }
}
