using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

public partial class GameControllerTests
{
    // --- Auto-select first territory at human turn start (#94) -----------
    //
    // A human turn opens with their "first" territory already selected,
    // reusing the Next-Territory (Tab) ordering: largest by tile count,
    // capital-coord tie-break, first actionable. AI turns are unaffected.

    [Fact]
    public void StartGame_AutoSelectsLargestTerritoryForHuman()
    {
        // Red owns a small (2-tile, cap (0,0)) and a big (3-tile, cap (5,0))
        // territory. The human's turn opens with the LARGEST already selected.
        var g = new UnequalRedTerritoriesGame(autoSelect: true);

        Assert.NotNull(g.Session.SelectedTerritory);
        Assert.Contains(HexCoord.FromOffset(5, 0), g.Session.SelectedTerritory!.Coords);
    }

    [Fact]
    public void StartGame_AutoSelect_TieBrokenByLowerCapitalCoord()
    {
        // Two equal-size (2-tile) Red territories, caps (0,0) and (5,0).
        // The size tie resolves to the lower capital coord.
        var g = new TwoRedTerritoriesGame(autoSelect: true);

        Assert.NotNull(g.Session.SelectedTerritory);
        Assert.Contains(HexCoord.FromOffset(0, 0), g.Session.SelectedTerritory!.Coords);
    }

    [Fact]
    public void StartGame_AutoSelect_RoutesThroughMapHighlight()
    {
        // Auto-selection participates in the normal refresh path: the map
        // highlight reflects it like a manual click.
        var g = new UnequalRedTerritoriesGame(autoSelect: true);

        Assert.NotNull(g.Map.LastHighlight);
        Assert.Contains(HexCoord.FromOffset(5, 0), g.Map.LastHighlight!.Coords);
    }

    [Fact]
    public void NextTerritory_AfterAutoSelect_AdvancesToNextTerritory()
    {
        // Auto-select consumes the "first" pick at turn start; pressing
        // Next Territory advances to the next one in cycle order.
        var g = new UnequalRedTerritoriesGame(autoSelect: true);
        Assert.Contains(HexCoord.FromOffset(5, 0), g.Session.SelectedTerritory!.Coords);

        g.Hud.PressNextTerritory();

        Assert.Contains(HexCoord.FromOffset(0, 0), g.Session.SelectedTerritory!.Coords);
    }

    [Fact]
    public void EndTurn_AutoSelectsForNextHumanPlayer()
    {
        // Two human players. After Red ends its turn, Blue's turn opens
        // with Blue's territory already selected.
        var g = new TestGame(autoSelect: true); // Red (2 tiles) + Blue (8 tiles), both human
        Assert.Equal(g.Red.Id, g.Session.SelectedTerritory!.Owner);

        g.Hud.ClickEndTurn();

        Assert.NotNull(g.Session.SelectedTerritory);
        Assert.Equal(g.Blue.Id, g.Session.SelectedTerritory!.Owner);
    }

    [Fact]
    public void TurnStart_HumanWithNothingActionable_LeavesSelectionNull()
    {
        // Blue (human) opens its turn broke with no units → nothing to act
        // on. Auto-select no-ops and must leave the board cleanly
        // unselected, never showing the prior player's (Red's) territory.
        var g = new TestGame(autoSelect: true);
        Assert.Equal(g.Red.Id, g.Session.SelectedTerritory!.Owner); // Red auto-selected

        // Drain Blue so nothing is actionable when its turn opens. Round 1
        // credits no income (TurnNumber stays 1 across the first round).
        Territory blue = g.State.Territories.First(t => t.Owner == g.Blue.Id);
        g.State.Treasury.SetGold(blue.Capital!.Value, 0);

        g.Hud.ClickEndTurn(); // → Blue's turn; auto-select finds nothing

        Assert.Null(g.Session.SelectedTerritory);
    }

    [Fact]
    public void TurnStart_AiPlayer_DoesNotReceiveAutoSelection()
    {
        // Red is a Computer, Blue a Human. At StartGame the AI's turn runs
        // (and ends) with no human-style selection; control settles on the
        // human, whose territory is the one auto-selected — never Red's.
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
        AiAction? Chooser(GameState s, PlayerId c, HashSet<HexCoord> v, HashSet<HexCoord> ru, Random r) => null;
        var controller = new GameController(
            state, session, map, hud, seed: 0,
            aiChooser: Chooser, aiPacer: new SynchronousAiPacer());
        controller.StartGame();

        Assert.NotNull(session.SelectedTerritory);
        Assert.Equal(blue.Id, session.SelectedTerritory!.Owner);
    }
}
