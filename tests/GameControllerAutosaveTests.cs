using System;
using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Timing tests for the <see cref="GameController.HumanTurnStarted"/> event.
/// Save/load wires this event to the autosave path: it must fire exactly
/// once at the start of every human turn, never on AI turns, and only
/// after view refresh so the saved state matches what the player sees.
/// </summary>
public class GameControllerAutosaveTests
{
    /// <summary>
    /// 5x2 fixture: Red owns (0,1)/(1,1) (a 2-tile territory), Blue owns
    /// the rest. Red and Blue's <see cref="PlayerKind"/> are configurable so
    /// tests can drive the human/AI mix.
    /// </summary>
    private class Fixture
    {
        public GameState State { get; }
        public SessionState Session { get; }
        public MockHexMapView Map { get; }
        public MockHudView Hud { get; }
        public GameController Controller { get; }
        public Player Red { get; }
        public Player Blue { get; }
        public int HumanTurnFireCount;
        public List<int> FireTurnNumbers { get; } = new();
        public List<PlayerId> FirePlayerColors { get; } = new();

        public Fixture(PlayerKind redKind, PlayerKind blueKind)
        {
            Red = new Player("Red", PlayerId.FromIndex(0), redKind);
            Blue = new Player("Blue", PlayerId.FromIndex(1), blueKind);
            var players = new List<Player> { Red, Blue };

            HexGrid grid = TestHelpers.BuildRectGrid(5, 2, Blue.Id);
            grid.Get(HexCoord.FromOffset(0, 1))!.Owner = Red.Id;
            grid.Get(HexCoord.FromOffset(1, 1))!.Owner = Red.Id;

            IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
            State = new GameState(grid, territories, players, new TurnState(players), new Treasury());
            Session = new SessionState();
            Map = new MockHexMapView();
            Hud = new MockHudView();
            // Stub chooser: AI players take no actions. Keeps the
            // fixture stable across grid changes and isolates the
            // test to the turn-transition event we care about. Cap
            // turn count so the all-AI variant terminates.
            AiAction? NoopChooser(GameState s, PlayerId c, HashSet<HexCoord> visited, HashSet<HexCoord> ru, Random rng) => null;
            Controller = new GameController(
                State, Session, Map, Hud,
                seed: 1, aiChooser: NoopChooser, maxTurnNumber: 5);
            Controller.HumanTurnStarted += () =>
            {
                HumanTurnFireCount++;
                FireTurnNumbers.Add(State.Turns.TurnNumber);
                FirePlayerColors.Add(State.Turns.CurrentPlayer.Id);
            };
        }
    }

    [Fact]
    public void HumanTurnStarted_FiresOnGameStart_WhenFirstPlayerIsHuman()
    {
        // Red is human and goes first. The event should fire once during
        // StartGame so autosave captures the very first state.
        var f = new Fixture(redKind: PlayerKind.Human, blueKind: PlayerKind.Computer);
        f.Controller.StartGame();
        Assert.Equal(1, f.HumanTurnFireCount);
        Assert.Equal(1, f.FireTurnNumbers[0]);
        Assert.Equal(f.Red.Id, f.FirePlayerColors[0]);
    }

    [Fact]
    public void HumanTurnStarted_DoesNotFire_WhenAiPlayerStartsTurn()
    {
        // Blue is AI; Red is human. After Red ends turn 1, Blue's AI
        // turn runs and then Red's turn 2 begins. The event should fire
        // for Red's turn 1 and Red's turn 2 only — never for Blue.
        var f = new Fixture(redKind: PlayerKind.Human, blueKind: PlayerKind.Computer);
        f.Controller.StartGame();
        f.Hud.ClickEndTurn();

        Assert.Equal(2, f.HumanTurnFireCount);
        Assert.All(f.FirePlayerColors, c => Assert.Equal(f.Red.Id, c));
    }

    [Fact]
    public void HumanTurnStarted_DoesNotFire_WhenStartingPlayerIsAi()
    {
        // Red is AI, Blue is human. StartGame auto-runs Red's AI turn,
        // then transitions to Blue's human turn. The event should fire
        // exactly once — for Blue, on Blue's first turn.
        var f = new Fixture(redKind: PlayerKind.Computer, blueKind: PlayerKind.Human);
        f.Controller.StartGame();

        Assert.Equal(1, f.HumanTurnFireCount);
        Assert.Equal(f.Blue.Id, f.FirePlayerColors[0]);
    }

    [Fact]
    public void HumanTurnStarted_DoesNotFire_WhenAllPlayersAreAi()
    {
        // All-AI game: no event firings. Tested with synchronous pacer.
        var f = new Fixture(redKind: PlayerKind.Computer, blueKind: PlayerKind.Computer);
        f.Controller.StartGame();

        Assert.Equal(0, f.HumanTurnFireCount);
    }

    [Fact]
    public void HumanTurnStarted_FiresAfterRefreshViews()
    {
        // The autosave subscriber reads GameState; the saved state must
        // match what the player sees, which means the event fires
        // *after* RefreshViews has pushed the latest state into the HUD.
        // Use the Hud's RefreshCount as a proxy: the count when the
        // event fires must equal or exceed the count immediately before.
        var f = new Fixture(redKind: PlayerKind.Human, blueKind: PlayerKind.Computer);
        int refreshCountAtFire = -1;
        f.Controller.HumanTurnStarted += () => refreshCountAtFire = f.Hud.RefreshCount;
        f.Controller.StartGame();

        Assert.True(refreshCountAtFire > 0,
            $"HumanTurnStarted must fire after at least one RefreshViews call, " +
            $"but fired with refresh count {refreshCountAtFire}");
    }
}
