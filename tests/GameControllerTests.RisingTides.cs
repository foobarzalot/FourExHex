using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

public partial class GameControllerTests
{
    // --- Rising Tides mode (issue #56) -----------------------------------

    private sealed class TidesGame
    {
        public GameState State { get; }
        public SessionState Session { get; }
        public MockHexMapView Map { get; }
        public MockHudView Hud { get; }
        public GameController Controller { get; }
        public Player Red { get; }
        public Player Blue { get; }

        public TidesGame(HexGrid grid, GameMode mode)
        {
            Red = new Player("Red", PlayerId.FromIndex(0));
            Blue = new Player("Blue", PlayerId.FromIndex(1));
            var players = new List<Player> { Red, Blue };
            IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
            State = new GameState(
                grid, territories, players, new TurnState(players), new Treasury(),
                waterCoords: null, mode: mode);
            Session = new SessionState();
            Map = new MockHexMapView();
            Hud = new MockHudView();
            Controller = new GameController(State, Session, Map, Hud);
            Controller.StartGame();
        }
    }

    // A 6x1 row: Red owns cols 0-3 (66%), Blue cols 4-5. Both have capitals.
    private static HexGrid LopsidedRow()
    {
        var grid = TestHelpers.BuildRectGrid(6, 1, PlayerId.FromIndex(0));
        grid.Get(HexCoord.FromOffset(4, 0))!.Owner = PlayerId.FromIndex(1);
        grid.Get(HexCoord.FromOffset(5, 0))!.Owner = PlayerId.FromIndex(1);
        return grid;
    }

    // 6x2: Red owns cols 0-2 (both rows), Blue owns cols 3-5 — two solid
    // 6-tile blocks, every tile a shore (the grid is only two rows tall).
    private static HexGrid TwoBlocks()
    {
        var grid = TestHelpers.BuildRectGrid(6, 2, PlayerId.FromIndex(1));
        for (int row = 0; row < 2; row++)
            for (int col = 0; col < 3; col++)
                grid.Get(HexCoord.FromOffset(col, row))!.Owner = PlayerId.FromIndex(0);
        return grid;
    }

    [Fact]
    public void RisingTides_EndTurnWithTerritorialLead_SuppressesClaimVictoryPrompt()
    {
        // Control: in freeform, ending Red's turn while owning 66% trips the
        // 50% claim-victory tier.
        var freeform = new TidesGame(LopsidedRow(), GameMode.Freeform);
        freeform.Hud.ClickEndTurn();
        Assert.NotNull(freeform.Session.PendingClaimVictory);

        // Rising Tides: same lead, no prompt — the turn just ends.
        var tides = new TidesGame(LopsidedRow(), GameMode.RisingTides);
        tides.Hud.ClickEndTurn();
        Assert.Null(tides.Session.PendingClaimVictory);
        Assert.Equal(tides.Blue.Id, tides.State.Turns.CurrentPlayer.Id);
    }

    [Fact]
    public void RisingTides_StartOfSecondTurn_SubmergesOneOwnerShoreTile()
    {
        var g = new TidesGame(TwoBlocks(), GameMode.RisingTides);
        int before = g.State.Grid.Count; // 12

        g.Hud.ClickEndTurn(); // Red t1 -> Blue t1
        g.Hud.ClickEndTurn(); // Blue t1 -> Red t2 (Red's shore erodes)

        Assert.Equal(before - 1, g.State.Grid.Count);
        Assert.Single(g.State.WaterCoords);
        HexCoord drowned = g.State.WaterCoords.Single();
        Assert.False(g.State.Grid.Contains(drowned));
        Assert.False(g.Session.IsGameOver); // Red still has a capital
    }

    [Fact]
    public void RisingTides_SubmergeDrownsLastCapital_OpponentWins()
    {
        // Red is a 2-tile territory; Blue a solid 3-tile territory. On Red's
        // second turn the sea takes one Red tile, dropping Red to a
        // capital-less singleton — Blue is the last player standing.
        var grid = TestHelpers.BuildRectGrid(5, 1, PlayerId.FromIndex(1));
        grid.Get(HexCoord.FromOffset(0, 0))!.Owner = PlayerId.FromIndex(0);
        grid.Get(HexCoord.FromOffset(1, 0))!.Owner = PlayerId.FromIndex(0);
        var g = new TidesGame(grid, GameMode.RisingTides);

        g.Hud.ClickEndTurn(); // Red t1 -> Blue t1
        g.Hud.ClickEndTurn(); // Blue t1 -> Red t2 submerge drowns Red's capital

        Assert.True(g.Session.IsGameOver);
        Assert.Equal(g.Blue.Id, g.Session.Winner);
    }

    [Fact]
    public void RisingTides_CaptureEliminatesLastOpponent_DeclaresWinner()
    {
        // The mid-turn domination check is rerouted to LastPlayerStanding,
        // so a sweep that leaves one capital-bearer still ends the game.
        var grid = TestHelpers.BuildRectGrid(4, 1, PlayerId.FromIndex(0));
        grid.Get(HexCoord.FromOffset(3, 0))!.Owner = PlayerId.FromIndex(1);
        grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Unit(PlayerId.FromIndex(0));
        var g = new TidesGame(grid, GameMode.RisingTides);

        g.Map.SimulateClick(g.State.Grid.Get(HexCoord.FromOffset(2, 0)));
        g.Map.SimulateClick(g.State.Grid.Get(HexCoord.FromOffset(3, 0)));

        Assert.True(g.Session.IsGameOver);
        Assert.Equal(g.Red.Id, g.Session.Winner);
    }

    [Fact]
    public void RisingTides_SubmergeEliminatesHumanAtTurnStart_RaisesDefeatScreen()
    {
        // 8x1 row, three human players: Red owns 2 tiles, Blue and Green 3 each.
        // At the start of Red's second turn the sea takes one of Red's two tiles,
        // dropping Red to a capital-less singleton — Red is defeated, but Blue and
        // Green remain, so the game continues and Red must see the defeat screen.
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        var green = new Player("Green", PlayerId.FromIndex(2));
        var players = new List<Player> { red, blue, green };
        var grid = TestHelpers.BuildRectGrid(8, 1, blue.Id);
        grid.Get(HexCoord.FromOffset(0, 0))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(1, 0))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(5, 0))!.Owner = green.Id;
        grid.Get(HexCoord.FromOffset(6, 0))!.Owner = green.Id;
        grid.Get(HexCoord.FromOffset(7, 0))!.Owner = green.Id;
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(
            grid, territories, players, new TurnState(players), new Treasury(),
            waterCoords: null, mode: GameMode.RisingTides);
        var session = new SessionState();
        var map = new MockHexMapView();
        var hud = new MockHudView();
        var controller = new GameController(state, session, map, hud);
        controller.StartGame();

        hud.ClickEndTurn(); // Red t1 -> Blue
        hud.ClickEndTurn(); // Blue t1 -> Green
        hud.ClickEndTurn(); // Green t1 -> Red t2: Red's tile sinks, Red eliminated

        Assert.True(WinConditionRules.IsEliminated(red.Id, state.Grid));
        Assert.False(session.IsGameOver); // Blue + Green remain
        Assert.Equal(red.Id, session.PendingDefeatScreen);

        // Dismissing the defeat screen advances past the eliminated human
        // (without the fix, Red would stay the current player, stuck with an
        // empty turn). Any later player's own submerge can legitimately raise a
        // fresh defeat, so only assert the turn moved off Red.
        hud.ClickDefeatContinue();
        Assert.NotEqual(red.Id, state.Turns.CurrentPlayer.Id);
    }

    [Fact]
    public void RisingTides_UndoAfterSubmergeTurn_DoesNotResurrectDrownedTile()
    {
        // Undo is turn-local and the stack is cleared each turn, so no
        // snapshot ever spans the start-of-turn submerge — undoing an in-turn
        // action must not bring a drowned tile back.
        var g = new TidesGame(TwoBlocks(), GameMode.RisingTides);
        g.Hud.ClickEndTurn();
        g.Hud.ClickEndTurn(); // Red t2: one Red tile drowned
        int afterSubmerge = g.State.Grid.Count;
        HexCoord drowned = g.State.WaterCoords.Single();

        HexTile capTile = g.State.Grid.Tiles.First(
            t => t.Owner == g.Red.Id && t.Occupant is Capital);
        g.Map.SimulateClick(capTile);
        HexCoord cap = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(cap, 15);
        g.Hud.ClickBuyRecruit();
        HexTile dest = g.State.Grid.Tiles.First(
            t => t.Owner == g.Red.Id && t.Occupant == null);
        g.Map.SimulateClick(dest);
        Assert.NotNull(dest.Unit);

        g.Hud.ClickUndoLast();

        Assert.Null(g.State.Grid.Get(dest.Coord)?.Unit); // placement undone
        Assert.Equal(afterSubmerge, g.State.Grid.Count);  // tile stayed drowned
        Assert.Contains(drowned, g.State.WaterCoords);
    }
}
