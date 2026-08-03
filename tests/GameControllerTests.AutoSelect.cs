// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

public partial class GameControllerTests
{
    // --- Turn-start territory selection (#209) ---------------------------
    //
    // A human's FIRST turn (no selection memory yet — fresh game or a
    // loaded save) opens with the lex-min actionable territory selected
    // and the camera panned to it. Every LATER turn reselects whatever
    // territory that player had selected when they last ended a turn —
    // with no camera movement — or nothing if they ended with nothing
    // selected (or the remembered territory no longer exists / lost its
    // capital). AI turns are unaffected.

    [Fact]
    public void FirstTurn_SelectsLexMinTerritory()
    {
        // Red owns a small (2-tile, cap (0,0)) and a big (3-tile, cap (5,0))
        // territory. The first turn opens on the lex-min capital, size be damned.
        var g = new UnequalRedTerritoriesGame(autoSelect: true);

        Assert.NotNull(g.Session.SelectedTerritory);
        Assert.Contains(HexCoord.FromOffset(0, 0), g.Session.SelectedTerritory!.Coords);
    }

    [Fact]
    public void FirstTurn_RoutesThroughMapHighlight()
    {
        // The turn-start selection participates in the normal refresh path:
        // the map highlight reflects it like a manual click.
        var g = new UnequalRedTerritoriesGame(autoSelect: true);

        Assert.NotNull(g.Map.LastHighlight);
        Assert.Contains(HexCoord.FromOffset(0, 0), g.Map.LastHighlight!.Coords);
    }

    [Fact]
    public void FirstTurn_PansToSelection()
    {
        // With no memory to restore, the fallback pick pans the camera —
        // the ordinary unconditional center, same as a Tab press.
        var g = new UnequalRedTerritoriesGame(autoSelect: true);

        Assert.Equal(1, g.Map.CenterCount);
        Assert.Same(g.Session.SelectedTerritory, g.Map.LastCenteredTerritory);
    }

    [Fact]
    public void NextTerritory_AfterFirstTurnSelect_AdvancesToNextTerritory()
    {
        // The turn-start pick consumes the "first" slot in cycle order;
        // pressing Next Territory advances to the next one.
        var g = new UnequalRedTerritoriesGame(autoSelect: true);
        Assert.Contains(HexCoord.FromOffset(0, 0), g.Session.SelectedTerritory!.Coords);

        g.Hud.PressNextTerritory();

        Assert.Contains(HexCoord.FromOffset(5, 0), g.Session.SelectedTerritory!.Coords);
    }

    [Fact]
    public void TurnStart_RestoresPriorTurnSelection_WithoutPanning()
    {
        // Red selects its second territory (cap (5,0)) during turn 1. When
        // Red's turn comes around again the same territory is already
        // selected — and the camera has not moved.
        var g = new TwoRedTerritoriesGame(autoSelect: true);
        Assert.Contains(HexCoord.FromOffset(0, 0), g.Session.SelectedTerritory!.Coords);
        g.Map.SimulateClick(g.State.Grid.Get(HexCoord.FromOffset(5, 0))!);
        Assert.Contains(HexCoord.FromOffset(5, 0), g.Session.SelectedTerritory!.Coords);

        g.Hud.ClickEndTurn(); // → Blue's first turn (fallback select + pan)
        Assert.Equal(g.Blue.Id, g.State.Turns.CurrentPlayer.Id);
        int centerBaseline = g.Map.CenterCount;

        g.Hud.ClickEndTurn(); // → Red's second turn

        Assert.Equal(g.Red.Id, g.State.Turns.CurrentPlayer.Id);
        Assert.NotNull(g.Session.SelectedTerritory);
        Assert.Contains(HexCoord.FromOffset(5, 0), g.Session.SelectedTerritory!.Coords);
        Assert.Equal(centerBaseline, g.Map.CenterCount); // restore never pans
    }

    [Fact]
    public void TurnStart_EndedWithNothingSelected_StartsUnselected()
    {
        // Red deselects (clicks empty space) before ending turn 1. Red's
        // second turn opens with nothing selected — no fallback, no pan.
        var g = new TwoRedTerritoriesGame(autoSelect: true);
        g.Map.SimulateClick(null);
        Assert.Null(g.Session.SelectedTerritory);

        g.Hud.ClickEndTurn(); // → Blue's first turn
        int centerBaseline = g.Map.CenterCount;

        g.Hud.ClickEndTurn(); // → Red's second turn

        Assert.Equal(g.Red.Id, g.State.Turns.CurrentPlayer.Id);
        Assert.Null(g.Session.SelectedTerritory);
        Assert.Equal(centerBaseline, g.Map.CenterCount);
    }

    [Fact]
    public void TurnStart_RememberedTerritoryGone_LeavesNothingSelected()
    {
        // The remembered capital no longer matches any of the player's
        // capital-bearing territories (captured / merged away between
        // turns) → the turn opens with nothing selected.
        var g = new TwoRedTerritoriesGame(autoSelect: true);
        g.Hud.ClickEndTurn(); // → Blue; Red's memory = cap (0,0)

        // Simulate the remembered territory vanishing while Blue acted.
        g.Session.LastSelectedCapitalByPlayer[g.Red.Id] = HexCoord.FromOffset(9, 0);

        g.Hud.ClickEndTurn(); // → Red's second turn

        Assert.Equal(g.Red.Id, g.State.Turns.CurrentPlayer.Id);
        Assert.Null(g.Session.SelectedTerritory);
    }

    [Fact]
    public void EndTurn_NextHumanFirstTurn_GetsLexMinFallback()
    {
        // Two human players. After Red ends its turn, Blue's FIRST turn
        // opens with Blue's lex-min territory selected (Blue has no
        // memory yet), panned to.
        var g = new TestGame(autoSelect: true); // Red (2 tiles) + Blue (8 tiles), both human
        Assert.Equal(g.Red.Id, g.Session.SelectedTerritory!.Owner);
        int centerBaseline = g.Map.CenterCount;

        g.Hud.ClickEndTurn();

        Assert.NotNull(g.Session.SelectedTerritory);
        Assert.Equal(g.Blue.Id, g.Session.SelectedTerritory!.Owner);
        Assert.Equal(centerBaseline + 1, g.Map.CenterCount);
    }

    [Fact]
    public void Hotseat_EachHumanRestoresOwnSelection()
    {
        // Red parks on its (5,0) blob, Blue on its (7,0) blob. Each
        // player's later turns restore their OWN remembered territory.
        var g = new TwoRedTerritoriesGame(autoSelect: true);
        g.Map.SimulateClick(g.State.Grid.Get(HexCoord.FromOffset(5, 0))!);
        g.Hud.ClickEndTurn(); // → Blue's first turn

        g.Map.SimulateClick(g.State.Grid.Get(HexCoord.FromOffset(7, 0))!);
        Assert.Contains(HexCoord.FromOffset(7, 0), g.Session.SelectedTerritory!.Coords);
        g.Hud.ClickEndTurn(); // → Red's second turn

        Assert.Contains(HexCoord.FromOffset(5, 0), g.Session.SelectedTerritory!.Coords);
        g.Hud.ClickEndTurn(); // → Blue's second turn

        Assert.Contains(HexCoord.FromOffset(7, 0), g.Session.SelectedTerritory!.Coords);
        Assert.Equal(g.Blue.Id, g.Session.SelectedTerritory!.Owner);
    }

    [Fact]
    public void TurnStart_HumanWithNothingActionable_LeavesSelectionNull()
    {
        // Blue (human) opens its FIRST turn broke with no units → nothing
        // to act on. The fallback pick no-ops and must leave the board
        // cleanly unselected, never showing the prior player's (Red's)
        // territory.
        var g = new TestGame(autoSelect: true);
        Assert.Equal(g.Red.Id, g.Session.SelectedTerritory!.Owner); // Red's first-turn pick

        // Drain Blue so nothing is actionable when its turn opens. Round 1
        // credits no income (TurnNumber stays 1 across the first round).
        Territory blue = g.State.Territories.First(t => t.Owner == g.Blue.Id);
        g.State.Treasury.SetGold(blue.Capital!.Value, 0);

        g.Hud.ClickEndTurn(); // → Blue's turn; fallback finds nothing

        Assert.Null(g.Session.SelectedTerritory);
    }

    [Fact]
    public void TurnStart_AiPlayer_DoesNotReceiveAutoSelection()
    {
        // Red is a Computer, Blue a Human. At StartGame the AI's turn runs
        // (and ends) with no human-style selection; control settles on the
        // human, whose lex-min territory is the one selected — never Red's.
        var red = new Player("Red", PlayerId.FromIndex(0), PlayerKind.Computer);
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        var players = new List<Player> { red, blue };
        var grid = TestHelpers.BuildRectGrid(5, 2, blue.Id);
        grid.Get(HexCoord.FromOffset(0, 1))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(1, 1))!.Owner = red.Id;
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var session = new SessionState();
        session.ClaimVictoryPromptedHighestThreshold[red.Id] = 90;
        session.ClaimVictoryPromptedHighestThreshold[blue.Id] = 90;
        var map = new MockHexMapView();
        var hud = new MockHudView();
        AiAction? Chooser(GameState s, PlayerId c, HashSet<HexCoord> v, HashSet<HexCoord> ru, DeterministicRng r) => null;
        var controller = new GameController(
            state, session, map, hud, seed: 0,
            aiChooser: Chooser, aiPacer: new SynchronousAiPacer());
        controller.StartGame();

        Assert.NotNull(session.SelectedTerritory);
        Assert.Equal(blue.Id, session.SelectedTerritory!.Owner);
    }
}
