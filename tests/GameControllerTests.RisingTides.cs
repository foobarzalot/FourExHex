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
    public void RisingTides_EndTurnWithTerritorialLead_OffersClaimVictory()
    {
        // Claim-victory tiers (50/75/90%) apply in Rising Tides too, computed
        // over the current non-sunk tiles. Red owns 4/6 (66%) of a single
        // connected row, so ending its turn trips the 50% tier — the same
        // prompt freeform shows.
        var freeform = new TidesGame(LopsidedRow(), GameMode.Freeform);
        freeform.Hud.ClickEndTurn();
        Assert.NotNull(freeform.Session.PendingClaimVictory);
        Assert.Equal(50, freeform.Session.PendingClaimVictory!.Value.ThresholdPercent);

        var tides = new TidesGame(LopsidedRow(), GameMode.RisingTides);
        tides.Hud.ClickEndTurn();
        Assert.NotNull(tides.Session.PendingClaimVictory);
        Assert.Equal(50, tides.Session.PendingClaimVictory!.Value.ThresholdPercent);
        // The offer holds the turn — still Red's until Win Now / Continue.
        Assert.Equal(tides.Red.Id, tides.State.Turns.CurrentPlayer.Id);
    }

    [Fact]
    public void RisingTides_ClaimVictoryWinNow_DeclaresWinner()
    {
        // The restored claim-victory Win Now path ends the game in Rising
        // Tides just as it does in freeform.
        var tides = new TidesGame(LopsidedRow(), GameMode.RisingTides);
        tides.Hud.ClickEndTurn();
        Assert.NotNull(tides.Session.PendingClaimVictory);

        tides.Hud.ClickClaimVictoryWinNow();

        Assert.True(tides.Session.IsGameOver);
        Assert.Equal(tides.Red.Id, tides.Session.Winner);
    }

    [Fact]
    public void RisingTides_ForecastsAtTurnStart_SubmergesAtTurnEnd()
    {
        // Issue #85: the erosion is telegraphed at the START of a player's turn
        // (a PendingTide forecast, tile still present) and only actualized at the
        // END of that same turn. The tide runs from turn 1, so the very first
        // player already has a forecast right after the game starts.
        var g = new TidesGame(TwoBlocks(), GameMode.RisingTides);
        int before = g.State.Grid.Count; // 12

        // During Red's turn 1: the doomed tile is telegraphed but still on the map.
        Assert.Equal(before, g.State.Grid.Count);
        Assert.Empty(g.State.WaterCoords);
        Assert.Single(g.State.PendingTide);
        HexCoord doomed = g.State.PendingTide.Single().Coord;
        Assert.True(g.State.Grid.Contains(doomed));
        Assert.Equal(g.Red.Id, g.State.Grid.Get(doomed)!.Owner); // Red's own shore

        g.Hud.ClickEndTurn(); // end Red t1: Red's forecast actualizes now

        Assert.Equal(before - 1, g.State.Grid.Count);
        Assert.Contains(doomed, g.State.WaterCoords);
        Assert.False(g.State.Grid.Contains(doomed));
        Assert.False(g.Session.IsGameOver); // Red still has a capital
    }

    [Fact]
    public void RisingTides_SubmergeDrownsLastCapital_OpponentWins()
    {
        // Red is a 2-tile territory; Blue a solid 3-tile territory. Red's turn-1
        // forecast is set at game start but only applied at the END of turn 1,
        // dropping Red to a capital-less singleton — Blue is last standing.
        var grid = TestHelpers.BuildRectGrid(5, 1, PlayerId.FromIndex(1));
        grid.Get(HexCoord.FromOffset(0, 0))!.Owner = PlayerId.FromIndex(0);
        grid.Get(HexCoord.FromOffset(1, 0))!.Owner = PlayerId.FromIndex(0);
        var g = new TidesGame(grid, GameMode.RisingTides);

        Assert.False(g.Session.IsGameOver); // turn-1 forecast set but not yet applied

        g.Hud.ClickEndTurn(); // end Red t1: forecast applies, drowns Red's capital

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
    public void RisingTides_SubmergeEliminatesHumanAtTurnEnd_RaisesDefeatScreen()
    {
        // 8x1 row, three human players: Red owns 2 tiles, Blue and Green 3 each.
        // Red's turn-1 forecast is set at game start and applied at the END of
        // turn 1 — the sea takes one of Red's two tiles, dropping Red to a
        // capital-less singleton. Red is defeated by its own end-of-turn flood,
        // but Blue and Green remain, so the game continues and Red (a human) must
        // see the defeat screen even though it was Red who ended the turn.
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

        Assert.False(WinConditionRules.IsEliminated(red.Id, state.Grid)); // alive, telegraphed

        hud.ClickEndTurn(); // end Red t1: forecast applies, Red drowns its own capital

        Assert.True(WinConditionRules.IsEliminated(red.Id, state.Grid));
        Assert.False(session.IsGameOver); // Blue + Green remain
        Assert.Equal(red.Id, session.PendingDefeatScreen);
        // End-of-turn elimination advances off Red automatically (the AI loop and
        // OnDefeatContinue both gate on PendingDefeatScreen), so the turn has
        // already moved on while Red's defeat overlay is up.
        Assert.NotEqual(red.Id, state.Turns.CurrentPlayer.Id);

        // Dismissing the defeat screen is informational here; the turn stays off Red.
        hud.ClickDefeatContinue();
        Assert.NotEqual(red.Id, state.Turns.CurrentPlayer.Id);
    }

    [Fact]
    public void RisingTides_UndoAfterSubmergeTurn_DoesNotResurrectDrownedTile()
    {
        // Undo is turn-local and the stack is cleared each turn, so no snapshot
        // ever spans the end-of-turn submerge — undoing an in-turn action in the
        // NEXT player's turn must not bring the just-drowned tile back.
        var g = new TidesGame(TwoBlocks(), GameMode.RisingTides);
        g.Hud.ClickEndTurn(); // end Red t1: one Red tile drowns -> Blue t1
        int afterSubmerge = g.State.Grid.Count;
        HexCoord drowned = g.State.WaterCoords.Single();

        // Current player is now Blue; act + undo within Blue's turn.
        HexTile capTile = g.State.Grid.Tiles.First(
            t => t.Owner == g.Blue.Id && t.Occupant is Capital);
        g.Map.SimulateClick(capTile);
        HexCoord cap = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(cap, 15);
        g.Hud.ClickBuyRecruit();
        HexTile dest = g.State.Grid.Tiles.First(
            t => t.Owner == g.Blue.Id && t.Occupant == null);
        g.Map.SimulateClick(dest);
        Assert.NotNull(dest.Unit);

        g.Hud.ClickUndoLast();

        Assert.Null(g.State.Grid.Get(dest.Coord)?.Unit); // placement undone
        Assert.Equal(afterSubmerge, g.State.Grid.Count);  // tile stayed drowned
        Assert.Contains(drowned, g.State.WaterCoords);
    }
}
