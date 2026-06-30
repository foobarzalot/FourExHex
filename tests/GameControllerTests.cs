using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// One partial class spread across GameControllerTests.*.cs domain
/// files (Selection, Actions, SoundsAndDefeat, Turns, Ai,
/// TerritoryCycle, UnitCycle, WinCondition, UndoRedo, Hud, BuyMode,
/// Rally) so each stays a tractable size. This file holds only the
/// shared <see cref="TestGame"/> fixture. Partial — rather than
/// separate classes — because several private helpers (e.g.
/// BuildAiFixture in the Ai file) are used across domain files, and
/// the assembly is serialized anyway (see AssemblyInfo.cs) so
/// per-class parallelism wouldn't apply.
/// </summary>
public partial class GameControllerTests
{
    /// <summary>
    /// Test fixture: a 5x2 grid with a 2-tile Red territory at (0,1)/(1,1)
    /// and Blue everywhere else. After StartGame, Red has 10 gold at its
    /// capital (5 × 2 tree-free cells) and it's Red's turn.
    /// </summary>
    private class TestGame
    {
        public GameState State { get; }
        public SessionState Session { get; }
        public MockHexMapView Map { get; }
        public MockHudView Hud { get; }
        public GameController Controller { get; }
        public Player Red { get; }
        public Player Blue { get; }

        // autoSelect defaults off: most fixture tests predate the
        // turn-start auto-selection (#94) and assume a fresh turn starts
        // with nothing selected. The #94 tests opt in with autoSelect: true.
        public TestGame(IReadOnlySet<HexCoord>? waterCoords = null, bool autoSelect = false)
        {
            Red = new Player("Red", PlayerId.FromIndex(0));
            Blue = new Player("Blue", PlayerId.FromIndex(1));
            var players = new List<Player> { Red, Blue };

            var grid = TestHelpers.BuildRectGrid(5, 2, Blue.Id);
            grid.Get(HexCoord.FromOffset(0, 1))!.Owner = Red.Id;
            grid.Get(HexCoord.FromOffset(1, 1))!.Owner = Red.Id;

            IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);

            State = new GameState(grid, territories, players, new TurnState(players), new Treasury(), waterCoords);
            Session = new SessionState();
            // Suppress the End-Turn claim-victory prompt for both colors:
            // Blue starts the fixture owning 80% of the board, which would
            // otherwise interrupt every test that cycles turns via End
            // Turn. Tests specifically about the prompt build their own
            // fixture (see ClaimVictoryTests).
            Session.ClaimVictoryPromptedHighestThreshold[Red.Id] = 90;
            Session.ClaimVictoryPromptedHighestThreshold[Blue.Id] = 90;
            Map = new MockHexMapView();
            Hud = new MockHudView();
            Controller = new GameController(State, Session, Map, Hud,
                autoSelectFirstTerritory: autoSelect);
            Controller.StartGame();
        }

        public HexTile Tile(int col, int row) => State.Grid.Get(HexCoord.FromOffset(col, row))!;

        public Territory RedTerritory =>
            State.Territories.First(t => t.Owner == Red.Id);
    }
}
