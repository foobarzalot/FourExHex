// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Pins loud-failure behavior for the switches that dispatch on a
/// <c>UnitLevel</c>, an <see cref="AiAction"/> kind, or a
/// <see cref="ReplayBeat"/> kind. Adding a member to any of those
/// without updating every dispatch site must throw
/// <see cref="InvalidOperationException"/>, never silently produce a
/// plausible default — free upkeep, a zero-valued unit, an unbuyable
/// price, a no-op AI step, or a skipped replay beat.
/// <see cref="HexOccupantDispatchTests"/> is the sibling file covering
/// the <see cref="HexOccupant"/> subtype hierarchy.
/// </summary>
public class UnmappedKindDispatchTests
{
    private static readonly PlayerId Red = PlayerId.FromIndex(0);
    private static readonly PlayerId Blue = PlayerId.FromIndex(1);

    /// <summary>A UnitLevel no dispatch site knows about.</summary>
    private const UnitLevel Unmapped = (UnitLevel)99;

    // --- UnitLevel switches ----------------------------------------------

    [Fact]
    public void UpkeepRules_UpkeepFor_ThrowsOnUnmappedLevel()
    {
        Assert.Throws<InvalidOperationException>(
            () => UpkeepRules.UpkeepFor(Unmapped));
    }

    [Fact]
    public void PurchaseRules_CostFor_ThrowsOnUnmappedLevel()
    {
        Assert.Throws<InvalidOperationException>(
            () => PurchaseRules.CostFor(Unmapped, Difficulty.Soldier));
    }

    [Fact]
    public void AiStateScorer_UnitValue_ThrowsOnUnmappedLevel()
    {
        // UnitValue is private; BarbarianProvokePenalty is the public seam
        // that reaches it without first passing through UpkeepFor (neutral
        // territories are upkeep-exempt, so TotalUpkeepFor short-circuits).
        // 5x1 strip: (0,0)-(2,0) neutral holding one passive unit, (3,0)-(4,0) Red.
        HexGrid grid = TestHelpers.BuildRectGrid(5, 1, Red);
        for (int col = 0; col <= 2; col++)
            grid.Get(HexCoord.FromOffset(col, 0))!.Owner = PlayerId.None;
        grid.Get(HexCoord.FromOffset(0, 0))!.Occupant = new Unit(PlayerId.None, Unmapped);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var players = new List<Player>
        {
            new Player("Red", Red, PlayerKind.Computer),
            new Player("Blue", Blue, PlayerKind.Computer),
        };
        var state = new GameState(grid, territories, players,
            new TurnState(players), new Treasury());

        Assert.Throws<InvalidOperationException>(
            () => AiStateScorer.BarbarianProvokePenalty(
                HexCoord.FromOffset(2, 0), state, Red));
    }

    // --- AiAction kinds ---------------------------------------------------

    /// <summary>An AiAction kind no executor knows about.</summary>
    private sealed record UnknownAiAction : AiAction;

    [Fact]
    public void ApplyAiActionCore_ThrowsOnUnmappedActionKind()
    {
        // A silent `default: return null` here means the AI burns its whole
        // per-player step budget on no-ops (instant track) or stalls without
        // rescheduling the next beat (paced track) — both invisible.
        var red = new Player("Red", Red, PlayerKind.Computer);
        var blue = new Player("Blue", Blue, PlayerKind.Human);
        var players = new List<Player> { red, blue };
        HexGrid grid = TestHelpers.BuildRectGrid(5, 2, Blue);
        grid.Get(HexCoord.FromOffset(0, 1))!.Owner = Red;
        grid.Get(HexCoord.FromOffset(1, 1))!.Owner = Red;
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players,
            new TurnState(players), new Treasury());
        var session = new SessionState();
        session.ClaimVictoryPromptedHighestThreshold[Red] = 90;
        session.ClaimVictoryPromptedHighestThreshold[Blue] = 90;

        bool served = false;
        var controller = new GameController(
            state, session, new MockHexMapView(), new MockHudView(),
            seed: 1,
            aiChooser: (s, p, visited, guard, rng) =>
            {
                if (served) return null;
                served = true;
                return new UnknownAiAction();
            },
            aiPacer: new SynchronousAiPacer(),
            maxTurnNumber: 10);

        Assert.Throws<InvalidOperationException>(() => controller.StartGame());
    }

    // --- ReplayBeat kinds -------------------------------------------------

    /// <summary>A ReplayBeat kind no playback path knows about.</summary>
    private sealed record UnknownReplayBeat : ReplayBeat;

    /// <summary>
    /// Two-human 5x2 game plus the snapshot/turn metadata a hand-built
    /// <see cref="Replay"/> needs. Feeding beats through the
    /// <c>loadedReplay:</c> ctor param bypasses SaveSerializer's own
    /// write-side throw, so a synthetic beat reaches the playback switch.
    /// </summary>
    private static (GameState State, Replay Replay) ReplayOf(params ReplayBeat[] beats)
    {
        var players = new List<Player>
        {
            new Player("Red", Red, PlayerKind.Human),
            new Player("Blue", Blue, PlayerKind.Human),
        };
        HexGrid grid = TestHelpers.BuildRectGrid(5, 2, Blue);
        grid.Get(HexCoord.FromOffset(0, 1))!.Owner = Red;
        grid.Get(HexCoord.FromOffset(1, 1))!.Owner = Red;
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players,
            new TurnState(players), new Treasury());
        var recorder = new GameController(state, new SessionState(),
            new MockHexMapView(), new MockHudView(),
            aiPacer: new SynchronousAiPacer());
        recorder.StartGame();

        var replay = new Replay(
            recorder.InitialReplaySnapshot!,
            recorder.InitialReplayTurnNumber,
            recorder.InitialReplayCurrentPlayerIndex,
            beats);
        return (state, replay);
    }

    private static void PlayBack(GameState state, Replay replay)
    {
        var session = new SessionState();
        session.ClaimVictoryPromptedHighestThreshold[Red] = 90;
        session.ClaimVictoryPromptedHighestThreshold[Blue] = 90;
        var controller = new GameController(state, session,
            new MockHexMapView(), new MockHudView(),
            aiPacer: new SynchronousAiPacer(),
            loadedReplay: replay);
        controller.BeginReplay();
    }

    [Fact]
    public void ExecuteReplayBeat_ThrowsOnUnmappedBeatKind()
    {
        (GameState state, Replay replay) = ReplayOf(
            new UnknownReplayBeat { Index = 0, Turn = 1, Actor = 0 });

        Assert.Throws<InvalidOperationException>(() => PlayBack(state, replay));
    }

    [Fact]
    public void ExecuteReplayBeat_IgnoresLegacyVikingTurnEndBeat()
    {
        // ReplayVikingTurnEndBeat is a replay-v1 artifact that playback
        // deliberately no-ops (see ReplayBeat.cs). It must stay an explicit
        // case, not fall into the unmapped-kind throw.
        (GameState state, Replay replay) = ReplayOf(
            new ReplayVikingTurnEndBeat { Index = 0, Turn = 1, Actor = 0 });

        PlayBack(state, replay);
    }
}
