// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
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
            // The canonical fixture is exactly BuildControllerGame's default
            // (5×2, Red at (0,1)/(1,1), claim-victory suppressed); only the
            // water/auto-select knobs vary here.
            ControllerHarness h = TestHelpers.BuildControllerGame(
                waterCoords: waterCoords, autoSelect: autoSelect);
            State = h.State;
            Session = h.Session;
            Map = h.Map;
            Hud = h.Hud;
            Controller = h.Controller;
            Red = h.Players[0];
            Blue = h.Players[1];
        }

        public HexTile Tile(int col, int row) => State.Grid.Get(HexCoord.FromOffset(col, row))!;

        public Territory RedTerritory =>
            State.Territories.First(t => t.Owner == Red.Id);
    }
}
