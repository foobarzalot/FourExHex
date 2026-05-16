using System;
using System.Collections.Generic;
using Godot;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Determinism tests for the per-turn RNG reseed. The save/load feature
/// requires that a saved <see cref="GameController.MasterSeed"/> plus the
/// (turn, player) tuple uniquely determines the RNG sequence used during
/// that player's turn — so a reloaded game replays the same AI choices
/// the original timeline made.
/// </summary>
public class GameControllerRngTests
{
    /// <summary>
    /// Drive a controller forward with a heuristic-AI Blue player so each
    /// AI turn actually consumes RNG, and capture every action the AI
    /// chooses via the chooser hook. Returns the action log so two runs
    /// can be compared for equality.
    /// </summary>
    private static List<string> RunAndCaptureAiActions(int seed, int turns)
    {
        var red = new Player("Red", new Color(1f, 0f, 0f));
        var blue = new Player("Blue", new Color(0f, 0f, 1f), isAi: true);
        var players = new List<Player> { red, blue };

        // 8x2 grid. Red owns (0,0)/(0,1); Blue owns the rest. Both
        // territories have plenty of room for AI-driven moves and buys.
        HexGrid grid = TestHelpers.BuildRectGrid(8, 2, blue.Color);
        grid.Get(HexCoord.FromOffset(0, 0))!.Color = red.Color;
        grid.Get(HexCoord.FromOffset(0, 1))!.Color = red.Color;

        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var session = new SessionState();
        var map = new MockHexMapView();
        var hud = new MockHudView();
        var log = new List<string>();
        AiAction? Chooser(GameState s, Color c, HashSet<HexCoord> visited, Random rng)
        {
            AiAction? action = RandomAi.ChooseNextAction(s, c, visited, rng);
            log.Add(action?.ToString() ?? "<end>");
            return action;
        }

        var controller = new GameController(
            state, session, map, hud,
            seed: seed,
            aiChooser: Chooser);
        controller.StartGame();
        for (int i = 0; i < turns; i++)
        {
            if (session.IsGameOver) break;
            // Auto-dismiss the human-defeat overlay so the AI loop
            // (paused on first human elimination) keeps running and
            // the per-seed RNG stream stays observable for the full
            // span of `turns`. This test isn't exercising defeat UX.
            if (session.PendingDefeatScreen.HasValue)
            {
                hud.ClickDefeatContinue();
            }
            hud.ClickEndTurn();
        }
        return log;
    }

    [Fact]
    public void SameSeed_ProducesIdenticalAiActionSequence()
    {
        // The save/load contract: a captured master seed replays the
        // entire game deterministically. Two controllers built with
        // identical seeds and inputs must choose the same AI actions.
        List<string> first = RunAndCaptureAiActions(seed: 12345, turns: 8);
        List<string> second = RunAndCaptureAiActions(seed: 12345, turns: 8);

        Assert.Equal(first, second);
    }

    [Fact]
    public void DifferentSeeds_ProduceDifferentAiActionSequences()
    {
        // A weak mixer would correlate adjacent seeds enough to produce
        // identical-looking action streams in short games. Use very
        // different seeds and assert the streams differ somewhere.
        List<string> a = RunAndCaptureAiActions(seed: 1, turns: 8);
        List<string> b = RunAndCaptureAiActions(seed: 999_999, turns: 8);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void MasterSeed_IsExposedAndStableAcrossTurns()
    {
        // Save format stores MasterSeed; the property must reflect the
        // value passed in (or the auto-generated one) and never change
        // mid-game even though the per-turn RNG is reseeded.
        var red = new Player("Red", new Color(1f, 0f, 0f));
        var blue = new Player("Blue", new Color(0f, 0f, 1f), isAi: true);
        var players = new List<Player> { red, blue };
        HexGrid grid = TestHelpers.BuildRectGrid(5, 2, blue.Color);
        grid.Get(HexCoord.FromOffset(0, 1))!.Color = red.Color;
        grid.Get(HexCoord.FromOffset(1, 1))!.Color = red.Color;
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var session = new SessionState();
        var map = new MockHexMapView();
        var hud = new MockHudView();
        var controller = new GameController(state, session, map, hud, seed: 42);
        Assert.Equal(42, controller.MasterSeed);
        controller.StartGame();
        Assert.Equal(42, controller.MasterSeed);
        hud.ClickEndTurn();
        Assert.Equal(42, controller.MasterSeed);
    }
}
