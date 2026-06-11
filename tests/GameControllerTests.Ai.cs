using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

public partial class GameControllerTests
{
    // --- AI action validation (harness defense) --------------------------

    /// <summary>
    /// Build a controller wired to a stub AI that returns the given
    /// sequence of actions, one per call to ChooseNextAction. Used
    /// to feed deliberately-invalid actions and verify the harness
    /// rejects them with an exception.
    /// </summary>
    private static GameController BuildHarnessWithStubAi(
        GameState state,
        MockHexMapView map,
        MockHudView hud,
        params AiAction?[] actions)
    {
        int index = 0;
        AiAction? Chooser(GameState s, PlayerId c, HashSet<HexCoord> visited, Random rng)
        {
            if (index >= actions.Length) return null;
            return actions[index++];
        }
        return new GameController(state, new SessionState(), map, hud, seed: 1, aiChooser: Chooser);
    }

    /// <summary>
    /// Minimal 2-player harness fixture mirroring the TestGame shape:
    /// a 5x2 Blue grid with Red owning (0,0), (0,1), (1,1) so there's
    /// a capturable non-capital Blue tile at (2,1) for a recruit
    /// placed at (1,1). Red's capital lands at (0,0), leaving (0,1)
    /// as the only empty Red tile for tower builds.
    /// </summary>
    private static (GameState state, MockHexMapView map, MockHudView hud) BuildAiFixture()
    {
        var red = new Player("Red", PlayerId.FromIndex(0), isAi: true);
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        var players = new List<Player> { red, blue };
        var grid = TestHelpers.BuildRectGrid(5, 2, blue.Id);
        grid.Get(HexCoord.FromOffset(0, 0))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(0, 1))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(1, 1))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(1, 1))!.Occupant = new Unit(red.Id);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        return (state, new MockHexMapView(), new MockHudView());
    }

    private static HexCoord RedCapital(GameState state) =>
        state.Territories.First(t => t.Owner == state.Players[0].Id).Capital!.Value;

    [Fact]
    public void ExecuteAiMove_SourceNotInOwnedTerritory_Throws()
    {
        (GameState state, MockHexMapView map, MockHudView hud) = BuildAiFixture();
        // Source (4,0) is Blue — not owned by Red.
        var bad = new AiMoveAction(HexCoord.FromOffset(4, 0), HexCoord.FromOffset(3, 0));
        GameController c = BuildHarnessWithStubAi(state, map, hud, bad);

        Assert.Throws<InvalidOperationException>(() => c.StartGame());
    }

    [Fact]
    public void ExecuteAiMove_SourceHasNoUnit_Throws()
    {
        (GameState state, MockHexMapView map, MockHudView hud) = BuildAiFixture();
        // Source = Red capital tile, which holds a Capital occupant
        // but no Unit. The "no unit" precondition check fires.
        HexCoord cap = RedCapital(state);
        var bad = new AiMoveAction(cap, HexCoord.FromOffset(2, 1));
        GameController c = BuildHarnessWithStubAi(state, map, hud, bad);

        Assert.Throws<InvalidOperationException>(() => c.StartGame());
    }

    [Fact]
    public void ExecuteAiMove_UnitAlreadyMoved_Throws()
    {
        (GameState state, MockHexMapView map, MockHudView hud) = BuildAiFixture();
        state.Grid.Get(HexCoord.FromOffset(1, 1))!.Unit!.HasMovedThisTurn = true;
        var bad = new AiMoveAction(HexCoord.FromOffset(1, 1), HexCoord.FromOffset(2, 1));
        GameController c = BuildHarnessWithStubAi(state, map, hud, bad);

        Assert.Throws<InvalidOperationException>(() => c.StartGame());
    }

    [Fact]
    public void ExecuteAiMove_DestinationNotAValidTarget_Throws()
    {
        (GameState state, MockHexMapView map, MockHudView hud) = BuildAiFixture();
        // (4,1) is far from the recruit at (1,1), not adjacent.
        var bad = new AiMoveAction(HexCoord.FromOffset(1, 1), HexCoord.FromOffset(4, 1));
        GameController c = BuildHarnessWithStubAi(state, map, hud, bad);

        Assert.Throws<InvalidOperationException>(() => c.StartGame());
    }

    [Fact]
    public void ExecuteAiBuyUnit_CapitalNotFound_Throws()
    {
        (GameState state, MockHexMapView map, MockHudView hud) = BuildAiFixture();
        // (4,0) is Blue — no territory has that capital.
        var bad = new AiBuyUnitAction(HexCoord.FromOffset(4, 0), HexCoord.FromOffset(2, 1), UnitLevel.Recruit);
        GameController c = BuildHarnessWithStubAi(state, map, hud, bad);

        Assert.Throws<InvalidOperationException>(() => c.StartGame());
    }

    [Fact]
    public void ExecuteAiBuyUnit_Unaffordable_Throws()
    {
        // StartGame re-seeds treasury to 10 and collects income (+3)
        // → 13g, above the 10g recruit cost. To exercise the
        // affordability precondition we chain two actions: the first
        // is a legal buy-capture that drains the treasury to 3g, and
        // the second is a bad buy whose affordability check now fails.
        (GameState state, MockHexMapView map, MockHudView hud) = BuildAiFixture();
        HexCoord cap = RedCapital(state);
        var first = new AiBuyUnitAction(cap, HexCoord.FromOffset(2, 1), UnitLevel.Recruit);
        var second = new AiBuyUnitAction(cap, HexCoord.FromOffset(2, 1), UnitLevel.Recruit);
        GameController c = BuildHarnessWithStubAi(state, map, hud, first, second);

        Assert.Throws<InvalidOperationException>(() => c.StartGame());
    }

    [Fact]
    public void ExecuteAiBuyUnit_InvalidDestination_Throws()
    {
        (GameState state, MockHexMapView map, MockHudView hud) = BuildAiFixture();
        HexCoord cap = RedCapital(state);
        state.Treasury.SetGold(cap, 20);
        // (4,1) is Blue and not adjacent to any Red tile.
        var bad = new AiBuyUnitAction(cap, HexCoord.FromOffset(4, 1), UnitLevel.Recruit);
        GameController c = BuildHarnessWithStubAi(state, map, hud, bad);

        Assert.Throws<InvalidOperationException>(() => c.StartGame());
    }

    [Fact]
    public void ExecuteAiBuildTower_CapitalNotFound_Throws()
    {
        (GameState state, MockHexMapView map, MockHudView hud) = BuildAiFixture();
        var bad = new AiBuildTowerAction(HexCoord.FromOffset(4, 0), HexCoord.FromOffset(0, 1));
        GameController c = BuildHarnessWithStubAi(state, map, hud, bad);

        Assert.Throws<InvalidOperationException>(() => c.StartGame());
    }

    [Fact]
    public void ExecuteAiBuildTower_Unaffordable_Throws()
    {
        (GameState state, MockHexMapView map, MockHudView hud) = BuildAiFixture();
        HexCoord cap = RedCapital(state);
        // Red has 3 tiles; a tree on one of them drops earning cells to
        // 2 → seed = 5 × 2 = 10 gold, less than the tower cost of 15.
        state.Grid.Get(HexCoord.FromOffset(0, 1))!.Occupant = new Tree();
        var bad = new AiBuildTowerAction(cap, HexCoord.FromOffset(0, 1));
        GameController c = BuildHarnessWithStubAi(state, map, hud, bad);

        Assert.Throws<InvalidOperationException>(() => c.StartGame());
    }

    [Fact]
    public void ExecuteAiBuildTower_DestinationNotInTerritory_Throws()
    {
        (GameState state, MockHexMapView map, MockHudView hud) = BuildAiFixture();
        HexCoord cap = RedCapital(state);
        state.Treasury.SetGold(cap, 20);
        // (4,0) is Blue — not in Red's territory.
        var bad = new AiBuildTowerAction(cap, HexCoord.FromOffset(4, 0));
        GameController c = BuildHarnessWithStubAi(state, map, hud, bad);

        Assert.Throws<InvalidOperationException>(() => c.StartGame());
    }

    [Fact]
    public void ExecuteAiBuildTower_DestinationOccupied_Throws()
    {
        (GameState state, MockHexMapView map, MockHudView hud) = BuildAiFixture();
        HexCoord cap = RedCapital(state);
        state.Treasury.SetGold(cap, 20);
        // (1,1) has the recruit — occupied.
        var bad = new AiBuildTowerAction(cap, HexCoord.FromOffset(1, 1));
        GameController c = BuildHarnessWithStubAi(state, map, hud, bad);

        Assert.Throws<InvalidOperationException>(() => c.StartGame());
    }

    [Fact]
    public void ExecuteAiBuildTower_NearExistingTower_DoesNotThrowOnSpacing()
    {
        // Tower spacing is an AI *selection* heuristic (filtered in
        // AiCommon.Enumerate), NOT an execution legality rule — humans
        // may bunch towers, so replaying a recorded human tower-build
        // adjacent to another tower must not throw. Regression for the
        // "about_to_win" replay desync (beat #714: human BuildTower
        // rejected because ExecuteAiBuildTower applied AI-only spacing).
        var red = new Player("Red", PlayerId.FromIndex(0), isAi: true);
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        var players = new List<Player> { red, blue };
        // 5x1 strip: Red owns (0,0)-(3,0); (4,0) Blue. Capital lands at
        // lex-min (0,0); (1,0)/(2,0)/(3,0) are empty Red tiles.
        var grid = TestHelpers.BuildRectGrid(5, 1, blue.Id);
        for (int x = 0; x < 4; x++)
            grid.Get(HexCoord.FromOffset(x, 0))!.Owner = red.Id;
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var map = new MockHexMapView();
        var hud = new MockHudView();

        Territory redTerr = state.Territories.First(t => t.Owner == red.Id);
        HexCoord cap = redTerr.Capital!.Value;
        state.Treasury.SetGold(cap, 20);
        // Pre-place a Red tower and target a second tower one hex away —
        // distance 1 < MinTowerSpacing (3), but otherwise fully legal.
        HexCoord existing = HexCoord.FromOffset(1, 0);
        HexCoord target = HexCoord.FromOffset(2, 0);
        Assert.NotEqual(cap, existing);
        Assert.NotEqual(cap, target);
        Assert.True(HexCoord.Distance(existing, target) < AiCommon.MinTowerSpacing);
        grid.Get(existing)!.Occupant = new Tower();

        var build = new AiBuildTowerAction(cap, target);
        GameController c = BuildHarnessWithStubAi(state, map, hud, build);

        c.StartGame(); // must NOT throw on spacing

        Assert.IsType<Tower>(grid.Get(target)!.Occupant);
        Assert.Equal(20 - PurchaseRules.TowerCost, state.Treasury.GetGold(cap));
    }

    [Fact]
    public void ExecuteAi_LegalActionViaStubChooser_ExecutesNormally()
    {
        // Regression lock: the stub-chooser injection path doesn't
        // break legal execution. Recruit at (1,1) captures (2,1).
        (GameState state, MockHexMapView map, MockHudView hud) = BuildAiFixture();
        var good = new AiMoveAction(HexCoord.FromOffset(1, 1), HexCoord.FromOffset(2, 1));
        GameController c = BuildHarnessWithStubAi(state, map, hud, good, null);

        c.StartGame(); // should NOT throw

        Assert.Equal(state.Players[0].Id, state.Grid.Get(HexCoord.FromOffset(2, 1))!.Owner);
    }

    // --- AI turn integration ---------------------------------------------

    /// <summary>
    /// 2-player fixture where Blue is an AI with a 3-tile territory
    /// containing a recruit positioned to capture a neutral Blue tile
    /// once it's their turn. Wait — Blue captures Blue? Let me rebuild.
    /// This fixture has Red (human) and Blue (AI) with their own
    /// territories; Blue's unit is adjacent to a capturable Red tile.
    /// </summary>
    private class HumanVsAiGame
    {
        public GameState State { get; }
        public SessionState Session { get; }
        public MockHexMapView Map { get; }
        public MockHudView Hud { get; }
        public GameController Controller { get; }
        public Player Red { get; }
        public Player Blue { get; }

        public HumanVsAiGame()
        {
            Red = new Player("Red", PlayerId.FromIndex(0)); // human
            Blue = new Player("Blue", PlayerId.FromIndex(1), isAi: true);
            var players = new List<Player> { Red, Blue };

            // 8x2 grid: Red owns (0,0)-(2,0), Blue owns (5,1)-(7,1).
            // Blue has a recruit on (5,1) — not adjacent to any Red
            // tile, so it can't capture. Different test methods will
            // mutate the fixture as needed.
            var grid = TestHelpers.BuildRectGrid(8, 2, PlayerId.None);
            grid.Get(HexCoord.FromOffset(0, 0))!.Owner = Red.Id;
            grid.Get(HexCoord.FromOffset(1, 0))!.Owner = Red.Id;
            grid.Get(HexCoord.FromOffset(2, 0))!.Owner = Red.Id;
            grid.Get(HexCoord.FromOffset(5, 1))!.Owner = Blue.Id;
            grid.Get(HexCoord.FromOffset(6, 1))!.Owner = Blue.Id;
            grid.Get(HexCoord.FromOffset(7, 1))!.Owner = Blue.Id;

            IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
            State = new GameState(grid, territories, players, new TurnState(players), new Treasury());
            Session = new SessionState();
            Map = new MockHexMapView();
            Hud = new MockHudView();
            // Seeded RNG so AI behavior is deterministic across runs.
            Controller = new GameController(State, Session, Map, Hud, seed: 12345);
            Controller.StartGame();
        }

        public HexTile Tile(int col, int row) => State.Grid.Get(HexCoord.FromOffset(col, row))!;
    }

    [Fact]
    public void EndTurn_AdvancesToAiPlayer_RunsAiTurnAutomatically()
    {
        // Human ends their turn, and since Blue is AI, the controller
        // should auto-run Blue's turn and end up back on Red without
        // a second human click.
        var g = new HumanVsAiGame();
        Assert.Equal(g.Red.Id, g.State.Turns.CurrentPlayer.Id);

        g.Hud.ClickEndTurn();

        // After the AI turn finishes, control returns to Red.
        Assert.Equal(g.Red.Id, g.State.Turns.CurrentPlayer.Id);
    }

    [Fact]
    public void AiTurn_WithNoValidActions_EndsImmediately()
    {
        // Blue has no gold, no units with capture targets, no trees,
        // and no empty tiles big enough for a tower. AI should take
        // no actions and simply end its turn (advancing back to Red).
        var g = new HumanVsAiGame();
        // Blue's territory has no capital income initially; leave
        // treasury empty. No units on Blue tiles. Nothing to do.
        g.Hud.ClickEndTurn();

        // No changes to Blue's tiles beyond income collection and
        // upkeep. Control is back to Red.
        Assert.Equal(g.Red.Id, g.State.Turns.CurrentPlayer.Id);
        // Blue's tiles are still Blue (no captures happened).
        Assert.Equal(g.Blue.Id, g.Tile(5, 1).Owner);
        Assert.Equal(g.Blue.Id, g.Tile(6, 1).Owner);
        Assert.Equal(g.Blue.Id, g.Tile(7, 1).Owner);
    }

    [Fact]
    public void AiTurn_CanCaptureLastEnemyHex_DeclaresWinner()
    {
        // Minimal fixture where the AI can win in one move: a 4-tile
        // Red territory with a recruit (sustainable upkeep), adjacent
        // to a lone undefended Blue tile. Red's net = 4 - 2 = 2, and
        // post-capture net = 5 - 2 = 3 — well above the AI's bankruptcy
        // rule. The AI should pick the winning capture on its first
        // turn (which is StartGame's job to auto-run since Red is the
        // starting AI player).
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
    }

    [Fact]
    public void AiWin_HidesOpponentsTakingTurnsOverlay()
    {
        // #23: when an AI wins on its own (paced) turn, the
        // "Opponents are taking their turns…" overlay must be hidden so
        // it doesn't draw on top of the victory screen. Same one-move
        // domination fixture as AiTurn_CanCaptureLastEnemyHex_DeclaresWinner;
        // the overlay is shown when the AI batch starts, and must be
        // reconciled away once the win fires.
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
        Assert.Null(hud.CurrentTutorialMessage);
    }

    [Fact]
    public void AiTurn_CaptureEnemyUnit_FiresDestructionEffectOnView()
    {
        // AI captures a defended enemy tile — destruction effect fires
        // for the displaced defender, same as the human path. Uses the
        // stub-chooser harness so the test pins the action regardless
        // of how the heuristic would score the move.
        (GameState state, MockHexMapView map, MockHudView hud) = BuildAiFixture();
        // Replace the default recruit attacker with a soldier so it
        // can capture a defender at (2,1).
        state.Grid.Get(HexCoord.FromOffset(1, 1))!.Occupant =
            new Unit(state.Players[0].Id, UnitLevel.Soldier);
        var defender = new Unit(state.Players[1].Id, UnitLevel.Recruit);
        state.Grid.Get(HexCoord.FromOffset(2, 1))!.Occupant = defender;

        var move = new AiMoveAction(HexCoord.FromOffset(1, 1), HexCoord.FromOffset(2, 1));
        GameController c = BuildHarnessWithStubAi(state, map, hud, move);

        c.StartGame();

        // Sanity: capture happened.
        Assert.Equal(state.Players[0].Id, state.Grid.Get(HexCoord.FromOffset(2, 1))!.Owner);

        Assert.Single(map.DestructionEffects);
        Assert.Equal(HexCoord.FromOffset(2, 1), map.DestructionEffects[0].Coord);
        Assert.Same(defender, map.DestructionEffects[0].Destroyed);
    }

    [Fact]
    public void AiTurn_DominationWin_RefreshesHudAfterWinnerSet()
    {
        // Regression: when the AI's capture triggered a domination
        // win mid-action, StepAiExecute early-returned on
        // _gameEndedFired without calling RefreshViews — so the HUD
        // never re-rendered after Winner became non-null and the
        // real victory overlay (gated on session.Winner inside
        // HudView.Refresh) stayed hidden, leaving the game looking
        // frozen. MockHudView.LastSeenWinner snapshots the value at
        // refresh time. Uses QueuedAiPacer (not the default
        // synchronous one) so the AI step machine doesn't collapse
        // into Resume's call stack — the synchronous case has
        // Resume's trailing RefreshViews after RunAi returns, which
        // masks the bug.
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
        var pacer = new QueuedAiPacer();
        var controller = new GameController(state, session, map, hud,
            seed: 1, aiPacer: pacer);
        controller.StartGame();
        pacer.DrainAll();

        Assert.Equal(red.Id, hud.LastSeenWinner);
    }

    [Fact]
    public void HumanCapture_DominationWin_FiresGameEnded()
    {
        // Regression: when a HUMAN player's mid-turn capture triggers
        // domination, HandleCapture sets Winner via DeclareWinner +
        // ClearUndoAndReplayBookkeeping but never calls
        // CheckGameEndConditions. TrackHandler then sees IsGameOver
        // and early-returns without firing GameEnded. Result: Main's
        // GameEnded → SetReplayAvailable(true) never runs, so the
        // Replay button on the victory overlay stays disabled even
        // though replay history is complete from game start. The
        // End-Turn win path runs CheckGameEndConditions explicitly
        // (line ~1729 in OnEndTurnPressed) and works correctly —
        // hence the discrepancy the user observed.
        var red = new Player("Red", PlayerId.FromIndex(0));   // human
        var blue = new Player("Blue", PlayerId.FromIndex(1)); // human (irrelevant)
        var players = new List<Player> { red, blue };

        // 5x1 grid. Red owns (0..3); Blue owns the single (4,0)
        // capital tile. A red Captain at (3,0) captures (4,0) → all-red
        // → domination win.
        var grid = TestHelpers.BuildRectGrid(5, 1, red.Id);
        grid.Get(HexCoord.FromOffset(4, 0))!.Owner = blue.Id;
        grid.Get(HexCoord.FromOffset(3, 0))!.Occupant = new Unit(red.Id, UnitLevel.Captain);

        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var session = new SessionState();
        var map = new MockHexMapView();
        var hud = new MockHudView();
        var controller = new GameController(state, session, map, hud, seed: 1);
        controller.StartGame();

        bool gameEnded = false;
        controller.GameEnded += () => gameEnded = true;

        // Two clicks: pick up the captain, then drop onto Blue's tile
        // to capture it. The capture triggers WinConditionRules
        // domination → DeclareWinner.
        map.SimulateClick(grid.Get(HexCoord.FromOffset(3, 0)));
        map.SimulateClick(grid.Get(HexCoord.FromOffset(4, 0)));

        Assert.True(session.IsGameOver);
        Assert.Equal(red.Id, session.Winner);
        // Without the fix, GameEnded never fires on the human mid-turn
        // capture path — Main can't enable the Replay button.
        Assert.True(gameEnded,
            "GameEnded did not fire after mid-turn human domination win.");
        Assert.True(controller.ReplayDataIsCompleteFromStart);
    }

    [Fact]
    public void AiTurn_EachTerritoryActsAtMostOnce()
    {
        // Blue has 2 territories; both have a captain adjacent to
        // capturable neutral tiles. Verify that after the AI turn,
        // each territory has made at most one capture (not an
        // unbounded loop).
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1), isAi: true);
        var players = new List<Player> { red, blue };

        // 10x2 grid. Red owns (0,0)+(0,1) — a 2-tile territory so Red
        // has a real capital and stays in rotation. Two Blue
        // territories: {(2,0),(3,0)} and {(6,0),(7,0)}. Each has a
        // captain on the right end so both can capture adjacent
        // neutral tiles ((4,0) and (8,0)).
        var grid = TestHelpers.BuildRectGrid(10, 2, PlayerId.None);
        grid.Get(HexCoord.FromOffset(0, 0))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(0, 1))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(2, 0))!.Owner = blue.Id;
        grid.Get(HexCoord.FromOffset(3, 0))!.Owner = blue.Id;
        grid.Get(HexCoord.FromOffset(6, 0))!.Owner = blue.Id;
        grid.Get(HexCoord.FromOffset(7, 0))!.Owner = blue.Id;
        grid.Get(HexCoord.FromOffset(3, 0))!.Occupant = new Unit(blue.Id, UnitLevel.Captain);
        grid.Get(HexCoord.FromOffset(7, 0))!.Occupant = new Unit(blue.Id, UnitLevel.Captain);

        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var session = new SessionState();
        var map = new MockHexMapView();
        var hud = new MockHudView();
        var controller = new GameController(state, session, map, hud, seed: 7);
        controller.StartGame();

        // End Red's (human) turn so Blue's AI turn runs.
        hud.ClickEndTurn();

        // Each of Blue's 2 territories had at most one action, so
        // total board-wide ownership change is at most +2 Blue tiles.
        // Starting Blue count: 4. Max post-AI Blue count: 6.
        int blueCount = 0;
        foreach (HexTile t in state.Grid.Tiles)
        {
            if (t.Owner == blue.Id) blueCount++;
        }
        Assert.InRange(blueCount, 4, 6);
    }

    // --- AI silent mode (Instant speed setting) -------------------------
    // GameController takes an injected Func<bool> aiSilentMode. When true
    // AND the current player is AI, the controller asks the view to enter
    // silent mode for the duration of that AI's turn — per-action Play*
    // effects and tween-animations are suppressed so the human sees the
    // post-AI map state with no visible/audible AI playback. The wiring
    // is symmetric: silent mode flips off as soon as control returns to a
    // human player (or the game ends).

    private static (GameState State, SessionState Session, MockHexMapView Map,
        MockHudView Hud, Player Human, Player Ai) BuildHumanVsAiKillScenario()
    {
        // 5x1 line: Red (human) holds capital at (3,0) with one outpost
        // at (4,0); Blue (AI) holds {(0,0),(1,0),(2,0)} with a Soldier
        // at (2,0). The scripted chooser below directs Blue's Soldier
        // to (3,0), killing Red's capital and capturing the territory.
        // Used by both Silent and NonSilent tests below to keep the
        // single AI action that fires a destruction effect identical
        // across them.
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1), isAi: true);
        var players = new List<Player> { red, blue };

        var grid = TestHelpers.BuildRectGrid(5, 1, PlayerId.None);
        grid.Get(HexCoord.FromOffset(0, 0))!.Owner = blue.Id;
        grid.Get(HexCoord.FromOffset(1, 0))!.Owner = blue.Id;
        grid.Get(HexCoord.FromOffset(2, 0))!.Owner = blue.Id;
        grid.Get(HexCoord.FromOffset(3, 0))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(4, 0))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Unit(blue.Id, UnitLevel.Soldier);

        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var session = new SessionState();
        var map = new MockHexMapView();
        var hud = new MockHudView();
        return (state, session, map, hud, red, blue);
    }

    [Fact]
    public void AiSilentMode_True_SuppressesPerActionEffects()
    {
        // With silent mode on, the AI's soldier-into-capital capture
        // still mutates state (capital destroyed, territory captured)
        // but no Play* effects reach the view. This is the visible
        // behavior the user expects from "Instant" speed.
        var (state, session, map, hud, _, blue) = BuildHumanVsAiKillScenario();

        AiAction? scripted = new AiMoveAction(
            HexCoord.FromOffset(2, 0), HexCoord.FromOffset(3, 0));
        AiAction? Chooser(GameState s, PlayerId c, HashSet<HexCoord> v, Random r)
        {
            AiAction? next = scripted;
            scripted = null;
            return next;
        }

        var controller = new GameController(
            state, session, map, hud, seed: 0,
            aiChooser: Chooser,
            aiPacer: new SynchronousAiPacer(),
            aiSilentMode: () => true);
        controller.StartGame();
        hud.ClickEndTurn(); // hand turn to Blue (AI); SynchronousAiPacer drains the AI turn inline

        // State mutated.
        Assert.Null(state.Grid.Get(HexCoord.FromOffset(2, 0))!.Unit);
        Assert.Equal(blue.Id, state.Grid.Get(HexCoord.FromOffset(3, 0))!.Owner);
        // But none of the per-action effects fired.
        Assert.Empty(map.DestructionEffects);
        Assert.Empty(map.CapitalDestroyedSounds);
        Assert.Empty(map.UnitPlacedSounds);
    }

    [Fact]
    public void AiSilentMode_False_PlaysAllPerActionEffects()
    {
        // Baseline: same scenario, silent mode off — the destruction
        // effect and capital-destroyed sound DO fire. Establishes that
        // the silent-mode gate (not some unrelated short-circuit) is
        // what suppressed them in the silent test.
        var (state, session, map, hud, _, _) = BuildHumanVsAiKillScenario();

        AiAction? scripted = new AiMoveAction(
            HexCoord.FromOffset(2, 0), HexCoord.FromOffset(3, 0));
        AiAction? Chooser(GameState s, PlayerId c, HashSet<HexCoord> v, Random r)
        {
            AiAction? next = scripted;
            scripted = null;
            return next;
        }

        var controller = new GameController(
            state, session, map, hud, seed: 0,
            aiChooser: Chooser,
            aiPacer: new SynchronousAiPacer(),
            aiSilentMode: () => false);
        controller.StartGame();
        hud.ClickEndTurn();

        Assert.NotEmpty(map.DestructionEffects);
        Assert.NotEmpty(map.CapitalDestroyedSounds);
    }

    [Fact]
    public void AiSilentMode_DefaultOff_DoesNotBreakExistingFlow()
    {
        // Regression guard: if a caller omits aiSilentMode (existing
        // tests, production paths that haven't been wired yet), the
        // controller behaves exactly as before — effects fire normally.
        var (state, session, map, hud, _, _) = BuildHumanVsAiKillScenario();

        AiAction? scripted = new AiMoveAction(
            HexCoord.FromOffset(2, 0), HexCoord.FromOffset(3, 0));
        AiAction? Chooser(GameState s, PlayerId c, HashSet<HexCoord> v, Random r)
        {
            AiAction? next = scripted;
            scripted = null;
            return next;
        }

        var controller = new GameController(
            state, session, map, hud, seed: 0,
            aiChooser: Chooser,
            aiPacer: new SynchronousAiPacer());
        controller.StartGame();
        hud.ClickEndTurn();

        Assert.NotEmpty(map.DestructionEffects);
        Assert.False(map.SilentMode);
    }

    // Live-AI Instant (chunked-driver) coverage — silent lifecycle,
    // "Opponents are taking their turns…" overlay, input gating,
    // per-turn sampling, no-drift vs paced, and mid-batch defeat —
    // lives in InstantAiTests. The old background-runner dispatch
    // tests were removed with that mechanism (replaced 1:1 there).
}
