using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

public partial class GameControllerTests
{
    // --- Win condition ---------------------------------------------------

    [Fact]
    public void HumanWin_FiresGameWonSound()
    {
        // Mirror the Capture_LastEnemyHex_DeclaresWinner setup: Red is
        // a human with a recruit adjacent to the last Blue tile;
        // capturing it ends the game with a human winner.
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        var players = new List<Player> { red, blue };

        var grid = TestHelpers.BuildRectGrid(4, 1, red.Id);
        grid.Get(HexCoord.FromOffset(3, 0))!.Owner = blue.Id;
        grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Unit(red.Id);

        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var session = new SessionState();
        var map = new MockHexMapView();
        var hud = new MockHudView();
        var controller = new GameController(state, session, map, hud);
        controller.StartGame();

        map.SimulateClick(grid.Get(HexCoord.FromOffset(2, 0)));
        map.SimulateClick(grid.Get(HexCoord.FromOffset(3, 0)));

        Assert.True(session.IsGameOver);
        Assert.Equal(red.Id, session.Winner);
        Assert.Equal(1, map.GameWonSoundCount);
    }

    [Fact]
    public void AiWin_DoesNotFireGameWonSound()
    {
        // Mirror AiTurn_CanCaptureLastEnemyHex_DeclaresWinner. From the
        // human's perspective an AI win is a loss, so the won-sound
        // must stay silent — the future game-lost cue handles this case.
        var red = new Player("Red", PlayerId.FromIndex(0), isAi: true);
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        var players = new List<Player> { red, blue };

        var grid = TestHelpers.BuildRectGrid(5, 1, red.Id);
        grid.Get(HexCoord.FromOffset(4, 0))!.Owner = blue.Id;
        grid.Get(HexCoord.FromOffset(3, 0))!.Occupant = new Unit(red.Id);

        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var session = new SessionState();
        var map = new MockHexMapView();
        var hud = new MockHudView();
        var controller = new GameController(state, session, map, hud, seed: 1);
        controller.StartGame();

        Assert.True(session.IsGameOver);
        Assert.Equal(red.Id, session.Winner);
        Assert.Equal(0, map.GameWonSoundCount);
    }

    [Fact]
    public void Capture_LastEnemyHex_DeclaresWinner()
    {
        // Build a minimal fixture: all tiles Red except one Blue tile
        // adjacent to Red. Capturing that tile wipes Blue out and Red
        // should be declared the winner.
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        var players = new List<Player> { red, blue };

        var grid = TestHelpers.BuildRectGrid(4, 1, red.Id);
        grid.Get(HexCoord.FromOffset(3, 0))!.Owner = blue.Id;
        // Park a Red recruit adjacent to the Blue hex.
        grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Unit(red.Id);

        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var session = new SessionState();
        var map = new MockHexMapView();
        var hud = new MockHudView();
        var controller = new GameController(state, session, map, hud);
        controller.StartGame();

        // Select Red, pick up the unit, capture the last Blue hex.
        map.SimulateClick(grid.Get(HexCoord.FromOffset(2, 0)));
        map.SimulateClick(grid.Get(HexCoord.FromOffset(3, 0)));

        Assert.True(session.IsGameOver);
        Assert.Equal(red.Id, session.Winner);
    }

    [Fact]
    public void Capture_NonFinalHex_DoesNotDeclareWinner()
    {
        var g = new TestGame();
        var unit = new Unit(g.Red.Id);
        g.Tile(1, 1).Occupant = unit;

        g.Map.SimulateClick(g.Tile(1, 1));
        g.Map.SimulateClick(g.Tile(2, 1)); // capture (2,1) — Blue still has tiles

        Assert.False(g.Session.IsGameOver);
        Assert.Null(g.Session.Winner);
    }

    [Fact]
    public void Capture_LeavesOpponentWithOrphanSingleton_DoesNotEndMidTurn()
    {
        // 5x1 grid: Red Red Red Blue Blue, soldier on Red(2,0).
        // (Blue's 2-tile territory has a capital, so a recruit
        // couldn't beat its defense — we need a soldier.)
        // Red captures Blue(3,0). Blue is left with (4,0) — a
        // singleton with no capital. Mid-turn check requires the
        // current player to own EVERY cell, so the game does NOT
        // end yet (Blue still has 1 tile).
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        var players = new List<Player> { red, blue };

        var grid = TestHelpers.BuildRectGrid(5, 1, red.Id);
        grid.Get(HexCoord.FromOffset(3, 0))!.Owner = blue.Id;
        grid.Get(HexCoord.FromOffset(4, 0))!.Owner = blue.Id;
        grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Unit(red.Id, UnitLevel.Soldier);

        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var session = new SessionState();
        var map = new MockHexMapView();
        var hud = new MockHudView();
        var controller = new GameController(state, session, map, hud);
        controller.StartGame();

        map.SimulateClick(grid.Get(HexCoord.FromOffset(2, 0)));
        map.SimulateClick(grid.Get(HexCoord.FromOffset(3, 0))); // capture

        Assert.False(session.IsGameOver);
        Assert.Null(session.Winner);
        // Blue's last tile is still there, just orphaned.
        Assert.Equal(blue.Id, grid.Get(HexCoord.FromOffset(4, 0))!.Owner);
    }

    [Fact]
    public void EndTurn_AfterReducingOpponentToSingleton_DeclaresWinner()
    {
        // Same fixture as above. After the capture the game continues
        // mid-turn. Ending Red's turn should now declare Red the winner
        // because Blue holds only an orphan singleton — no
        // capital-bearing territory.
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        var players = new List<Player> { red, blue };

        var grid = TestHelpers.BuildRectGrid(5, 1, red.Id);
        grid.Get(HexCoord.FromOffset(3, 0))!.Owner = blue.Id;
        grid.Get(HexCoord.FromOffset(4, 0))!.Owner = blue.Id;
        grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Unit(red.Id, UnitLevel.Soldier);

        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var session = new SessionState();
        // Suppress the End-Turn claim-victory prompt: this test exercises
        // the end-of-turn sole-capital-bearer winner path, not the new
        // human-at->50% prompt that would otherwise interject.
        session.ClaimVictoryPromptedHighestThreshold[red.Id] = 90;
        var map = new MockHexMapView();
        var hud = new MockHudView();
        var controller = new GameController(state, session, map, hud);
        controller.StartGame();

        map.SimulateClick(grid.Get(HexCoord.FromOffset(2, 0)));
        map.SimulateClick(grid.Get(HexCoord.FromOffset(3, 0))); // capture
        Assert.False(session.IsGameOver);

        hud.ClickEndTurn();

        Assert.True(session.IsGameOver);
        Assert.Equal(red.Id, session.Winner);
    }

    [Fact]
    public void EndTurn_OpponentStillHasCapitalBearingTerritory_GameContinues()
    {
        // Sanity check: ending the turn while another player still
        // owns a capital-bearing territory must NOT declare a winner.
        var g = new TestGame();

        g.Hud.ClickEndTurn();

        Assert.False(g.Session.IsGameOver);
        Assert.Null(g.Session.Winner);
    }

    [Fact]
    public void BuyRecruit_AfterWin_IsNoOp()
    {
        var g = new TestGame();
        g.Session.Winner = g.Red.Id; // simulate already-won state

        g.Map.SimulateClick(g.Tile(0, 1));
        g.Hud.ClickBuyRecruit();

        // Mode should still be None because the controller short-
        // circuits. Selection also does not happen.
        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
    }

    [Fact]
    public void EndTurn_AfterWin_IsNoOp()
    {
        var g = new TestGame();
        PlayerId initialPlayer = g.State.Turns.CurrentPlayer.Id;
        g.Session.Winner = g.Red.Id;

        g.Hud.ClickEndTurn();

        Assert.Equal(initialPlayer, g.State.Turns.CurrentPlayer.Id);
    }

    [Fact]
    public void UndoLast_AfterWin_IsNoOp()
    {
        var g = new TestGame();
        // Do an action so undo is available, then simulate a win.
        g.Map.SimulateClick(g.Tile(0, 1));
        g.Hud.ClickBuyRecruit();
        g.Map.SimulateClick(g.Tile(1, 1));
        Assert.NotNull(g.Tile(1, 1).Unit);

        g.Session.Winner = g.Red.Id;
        g.Hud.ClickUndoLast();

        // Unit should still be present; undo was frozen.
        Assert.NotNull(g.Tile(1, 1).Unit);
    }

    [Fact]
    public void Capture_WinningCapture_ClearsUndoStack()
    {
        // Once the game is won, we don't want players rewinding past
        // the killing blow. HandleCapture should clear the undo stack.
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        var players = new List<Player> { red, blue };

        var grid = TestHelpers.BuildRectGrid(4, 1, red.Id);
        grid.Get(HexCoord.FromOffset(3, 0))!.Owner = blue.Id;
        grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Unit(red.Id);

        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var session = new SessionState();
        var map = new MockHexMapView();
        var hud = new MockHudView();
        var controller = new GameController(state, session, map, hud);
        controller.StartGame();

        map.SimulateClick(grid.Get(HexCoord.FromOffset(2, 0)));
        map.SimulateClick(grid.Get(HexCoord.FromOffset(3, 0)));

        Assert.False(session.Undo.CanUndo);
    }

    [Fact]
    public void EndTurn_SkipsEliminatedPlayer()
    {
        // Three-player fixture where the middle player (Blue) has zero
        // tiles. Ending Red's turn should jump straight to Green,
        // skipping Blue entirely.
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        var green = new Player("Green", PlayerId.FromIndex(2));
        var players = new List<Player> { red, blue, green };

        // Grid has only Red and Green tiles — no Blue.
        var grid = new HexGrid();
        grid.Add(new HexTile(HexCoord.FromOffset(0, 0), red.Id));
        grid.Add(new HexTile(HexCoord.FromOffset(1, 0), red.Id));
        grid.Add(new HexTile(HexCoord.FromOffset(3, 0), green.Id));
        grid.Add(new HexTile(HexCoord.FromOffset(4, 0), green.Id));

        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var session = new SessionState();
        var map = new MockHexMapView();
        var hud = new MockHudView();
        var controller = new GameController(state, session, map, hud);
        controller.StartGame();

        Assert.Equal(red.Id, state.Turns.CurrentPlayer.Id);
        hud.ClickEndTurn();

        // Should skip Blue (eliminated) and land on Green.
        Assert.Equal(green.Id, state.Turns.CurrentPlayer.Id);
    }

    [Fact]
    public void EndTurn_ClearsUndoStack()
    {
        var g = new TestGame();
        // Queue an undoable action (buy recruit).
        g.Map.SimulateClick(g.Tile(0, 1));
        g.Hud.ClickBuyRecruit();
        g.Map.SimulateClick(g.Tile(1, 1));
        Assert.True(g.Session.Undo.CanUndo);

        g.Hud.ClickEndTurn();

        Assert.False(g.Session.Undo.CanUndo);
    }
}
