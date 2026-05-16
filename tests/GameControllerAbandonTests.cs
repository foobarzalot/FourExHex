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

    /// <summary>
    /// Regression: after AbandonGame, view events (TileClicked,
    /// HUD buttons) must not still route into the controller. The
    /// TutorialBuilder shares the map view between Record and
    /// Preview sessions; if the abandoned record controller stayed
    /// subscribed to TileClicked, a Preview-mode click fired its
    /// handler too — which then called RefreshViews on the now-
    /// disposed record HudView and threw ObjectDisposedException.
    /// </summary>
    [Fact]
    public void AbandonGame_UnsubscribesFromViewEvents_NoLingerHandler()
    {
        var red = new Player("Red", new Color(1f, 0f, 0f));
        var blue = new Player("Blue", new Color(0f, 0f, 1f));
        var players = new List<Player> { red, blue };
        HexGrid grid = TestHelpers.BuildRectGrid(2, 2, red.Color);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());

        var map = new MockHexMapView();
        var hud = new MockHudView();
        var controller = new GameController(
            state, new SessionState(), map, hud,
            seed: 1, aiPacer: new SynchronousAiPacer());
        controller.StartGame();

        int hudRefreshCountBeforeAbandon = hud.RefreshCount;
        controller.AbandonGame();
        int hudRefreshCountAfterAbandon = hud.RefreshCount;

        // Simulate a post-abandon click — represents the user
        // clicking during Preview while the old record controller
        // is still alive but should have stopped listening.
        map.SimulateClick(grid.Get(new HexCoord(0, 0)));

        // The abandoned controller must not have run RefreshViews
        // (which would have bumped the HUD's RefreshCount). If it
        // did, that's the bug: the controller is still routing
        // events into its now-stale view references.
        Assert.Equal(hudRefreshCountAfterAbandon, hud.RefreshCount);
    }
}
