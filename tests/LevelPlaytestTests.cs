// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

public class LevelPlaytestTests
{
    private static PlaytestSnapshot Snap(int turn, params int[] land) =>
        new PlaytestSnapshot(turn, land);

    // --- pure metric functions on synthetic series ---

    [Fact]
    public void LeaderOf_PicksStrictMax_TieIsNoLeader()
    {
        Assert.Equal(1, PlaytestMetrics.LeaderOf(new[] { 2, 5, 3 }));
        Assert.Equal(-1, PlaytestMetrics.LeaderOf(new[] { 4, 4, 1 }));
        Assert.Equal(-1, PlaytestMetrics.LeaderOf(new[] { 0, 0, 0 }));
    }

    [Fact]
    public void LeadChanges_CountsStrictLeaderHandoffs()
    {
        var series = new List<PlaytestSnapshot>
        {
            Snap(1, 3, 1), Snap(2, 1, 3), Snap(3, 4, 2), Snap(4, 5, 2),
        };
        // 0 -> 1 -> 0 -> 0: two handoffs.
        Assert.Equal(2, PlaytestMetrics.LeadChanges(series));
    }

    [Fact]
    public void LeadChanges_IgnoresTieGaps()
    {
        var series = new List<PlaytestSnapshot>
        {
            Snap(1, 3, 1), Snap(2, 2, 2), Snap(3, 4, 1),
        };
        // 0 -> (tie) -> 0: no handoff.
        Assert.Equal(0, PlaytestMetrics.LeadChanges(series));
    }

    [Fact]
    public void DecidedTurn_IsFirstTurnOfTheWinnersUnbrokenFinalLead()
    {
        var series = new List<PlaytestSnapshot>
        {
            Snap(1, 3, 1), Snap(2, 1, 3), Snap(3, 4, 2), Snap(4, 5, 2),
        };
        Assert.Equal(3, PlaytestMetrics.DecidedTurn(series, winnerSlot: 0));
    }

    [Fact]
    public void DecidedTurn_WinnerLedThroughout_IsFirstSnapshotTurn()
    {
        var series = new List<PlaytestSnapshot> { Snap(1, 3, 1), Snap(2, 4, 1) };
        Assert.Equal(1, PlaytestMetrics.DecidedTurn(series, winnerSlot: 0));
    }

    [Fact]
    public void DecidedTurn_UnresolvedGame_IsMinusOne()
    {
        var series = new List<PlaytestSnapshot> { Snap(1, 3, 1) };
        Assert.Equal(-1, PlaytestMetrics.DecidedTurn(series, winnerSlot: -1));
    }

    [Fact]
    public void ClosenessPercent_IsRunnerUpShareOfWinnerLand()
    {
        Assert.Equal(50, PlaytestMetrics.ClosenessPercent(new[] { 10, 5, 0 }, 0));
        Assert.Equal(100, PlaytestMetrics.ClosenessPercent(new[] { 7, 7, 2 }, 0));
        Assert.Equal(0, PlaytestMetrics.ClosenessPercent(new[] { 10, 0, 0 }, 0));
        Assert.Equal(-1, PlaytestMetrics.ClosenessPercent(new[] { 10, 5 }, -1));
    }

    // --- end-to-end runs on a tiny authored map ---

    private static string LopsidedMapJson()
    {
        // A contiguous strip: slot 0 owns a 5x2 block, slot 1 a touching
        // 2x2 block. Slot 0's economy should overwhelm slot 1.
        var ws = new LevelWorkspace(12, 5);
        foreach (HexCoord c in LevelWorkspace.RectCoords(1, 1, 5, 2))
            ws.PaintLand(0, c);
        foreach (HexCoord c in LevelWorkspace.RectCoords(6, 1, 7, 2))
            ws.PaintLand(1, c);
        ws.SetSlot(0, PlayerKind.Human); // playtest must force this to Computer
        Assert.Empty(ws.Validate());
        return ws.ToJson("lopsided");
    }

    [Fact]
    public void Run_PlaysAllGamesToCompletion()
    {
        PlaytestReport report = LevelPlaytest.Run(
            LopsidedMapJson(), games: 2, baseSeed: 7, maxTurnNumber: 300);

        Assert.Equal(2, report.Games.Count);
        Assert.All(report.Games, g => Assert.True(g.FinalTurn > 0));
        Assert.Equal(new[] { 7, 8 }, report.Games.Select(g => g.Seed).ToArray());
    }

    [Fact]
    public void Run_StrongSlotWinsAndWeakSlotEliminationIsRecorded()
    {
        PlaytestReport report = LevelPlaytest.Run(
            LopsidedMapJson(), games: 2, baseSeed: 7, maxTurnNumber: 300);

        Assert.All(report.Games, g => Assert.Equal(0, g.WinnerSlot));
        Assert.All(report.Games, g =>
        {
            Assert.True(g.EliminationTurns.ContainsKey(1));
            Assert.True(g.EliminationTurns[1] > 0);
        });
    }

    [Fact]
    public void Run_SameSeeds_ProduceIdenticalReports()
    {
        string json = LopsidedMapJson();
        PlaytestReport a = LevelPlaytest.Run(json, games: 2, baseSeed: 42, maxTurnNumber: 300);
        PlaytestReport b = LevelPlaytest.Run(json, games: 2, baseSeed: 42, maxTurnNumber: 300);

        Assert.Equal(a.Format(), b.Format());
    }

    [Fact]
    public void Format_SummarizesWinnersAndLengths()
    {
        PlaytestReport report = LevelPlaytest.Run(
            LopsidedMapJson(), games: 2, baseSeed: 7, maxTurnNumber: 300);
        string text = report.Format();

        Assert.Contains("games: 2", text);
        Assert.Contains("slot 0", text);
        Assert.Contains("winners:", text);
        Assert.Contains("length:", text);
    }
}
