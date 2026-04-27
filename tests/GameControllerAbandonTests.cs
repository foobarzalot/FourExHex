using System;
using System.Collections.Generic;
using Godot;
using Xunit;

namespace FourExHex.Tests;

public class GameControllerAbandonTests
{
    /// <summary>
    /// Test pacer that queues callbacks instead of running them. Models
    /// <see cref="GodotAiPacer"/>'s SceneTreeTimer queue: an in-flight
    /// callback that hasn't drained yet. Lets the test observe that
    /// <c>AbandonGame</c> drops the queue before scene teardown can
    /// dispose the polygons the callback would write to.
    /// </summary>
    private sealed class DeferredAiPacer : IAiPacer
    {
        private readonly Queue<Action> _pending = new();
        public int PendingCount => _pending.Count;
        public void Schedule(Action callback, int delayMs) => _pending.Enqueue(callback);
        public void Cancel() => _pending.Clear();
    }

    /// <summary>
    /// Two players, Red is AI. After StartGame, Red's first AI step is
    /// queued in the pacer (not yet executed). Pressing End Game must
    /// drop that pending step so it can't fire after teardown.
    /// </summary>
    [Fact]
    public void AbandonGame_DropsPendingAiStep()
    {
        var red = new Player("Red", new Color(1f, 0f, 0f), isAi: true);
        var blue = new Player("Blue", new Color(0f, 0f, 1f));
        var players = new List<Player> { red, blue };
        HexGrid grid = TestHelpers.BuildRectGrid(5, 2, blue.Color);
        grid.Get(HexCoord.FromOffset(0, 0))!.Color = red.Color;
        grid.Get(HexCoord.FromOffset(0, 1))!.Color = red.Color;
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());

        DeferredAiPacer pacer = new();
        AiAction? Chooser(GameState s, Color c, HashSet<HexCoord> visited, Random rng) => null;
        var controller = new GameController(
            state, new SessionState(), new MockHexMapView(), new MockHudView(),
            seed: 1, aiPacer: pacer, aiChooser: Chooser);

        controller.StartGame();
        Assert.True(pacer.PendingCount > 0,
            "StartGame should have queued at least one AI step on the pacer.");

        controller.AbandonGame();

        Assert.Equal(0, pacer.PendingCount);
    }
}
