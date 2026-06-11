using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

public partial class GameControllerTests
{
    // --- Cycle movable units in selection (N / Shift+N) -------------------

    /// <summary>
    /// 6x2 grid, Blue everywhere, Red overlay on row 1 cols 0-4 (5 tiles
    /// → capital lands on (0,1), the lex-min empty tile). Three unmoved
    /// Red recruits on (1,1), (2,1), (3,1) so cycling has somewhere to go;
    /// (4,1) is left empty so the BuildTower-mode test has a valid tower
    /// target. Lex order on row 1 is by Q ascending: UnitA &lt; UnitB &lt; UnitC.
    /// </summary>
    private class ThreeUnitsRedGame
    {
        public GameState State { get; }
        public SessionState Session { get; }
        public MockHexMapView Map { get; }
        public MockHudView Hud { get; }
        public GameController Controller { get; }
        public Player Red { get; }
        public Player Blue { get; }

        public HexCoord UnitA { get; } = HexCoord.FromOffset(1, 1);
        public HexCoord UnitB { get; } = HexCoord.FromOffset(2, 1);
        public HexCoord UnitC { get; } = HexCoord.FromOffset(3, 1);

        public Territory RedTerritory =>
            State.Territories.First(t => t.Owner == Red.Id);

        public ThreeUnitsRedGame()
        {
            Red = new Player("Red", PlayerId.FromIndex(0));
            Blue = new Player("Blue", PlayerId.FromIndex(1));
            var players = new List<Player> { Red, Blue };

            var grid = TestHelpers.BuildRectGrid(6, 2, Blue.Id);
            for (int col = 0; col <= 4; col++)
            {
                grid.Get(HexCoord.FromOffset(col, 1))!.Owner = Red.Id;
            }

            IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
            State = new GameState(grid, territories, players, new TurnState(players), new Treasury());
            Session = new SessionState();
            Map = new MockHexMapView();
            Hud = new MockHudView();
            Controller = new GameController(State, Session, Map, Hud);
            Controller.StartGame();

            // Place the units AFTER StartGame so their HasMovedThisTurn
            // isn't reset by the start-of-turn pass (and so the capital
            // — placed by CapitalReconciler on the lex-min empty tile —
            // ends up on (0,1) before any unit can occupy it).
            grid.Get(UnitA)!.Occupant = new Unit(Red.Id);
            grid.Get(UnitB)!.Occupant = new Unit(Red.Id);
            grid.Get(UnitC)!.Occupant = new Unit(Red.Id);
        }

        public void SelectRed() => Map.SimulateClick(State.Grid.Get(RedTerritory.Capital!.Value));
    }

    [Fact]
    public void NextUnit_NoSelection_IsNoOp()
    {
        var g = new ThreeUnitsRedGame();
        Assert.Null(g.Session.SelectedTerritory);
        int baseline = g.Session.Undo.UndoCount;

        g.Hud.PressNextUnit();

        Assert.Null(g.Session.MoveSource);
        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
        Assert.Equal(baseline, g.Session.Undo.UndoCount);
    }

    [Fact]
    public void NextUnit_SelectedTerritoryHasNoMovableUnits_IsNoOp()
    {
        // Select Red on the empty 2-hex fixture (no units anywhere).
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        Assert.NotNull(g.Session.SelectedTerritory);
        int baseline = g.Session.Undo.UndoCount;

        g.Hud.PressNextUnit();

        Assert.Null(g.Session.MoveSource);
        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
        Assert.Equal(baseline, g.Session.Undo.UndoCount);
    }

    [Fact]
    public void NextUnit_NoSourcePickedYet_PicksHighestPowerMovableUnit()
    {
        var g = new ThreeUnitsRedGame();
        g.SelectRed();
        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);

        g.Hud.PressNextUnit();

        Assert.Equal(SessionState.ActionMode.MovingUnit, g.Session.Mode);
        Assert.Equal(g.UnitA, g.Session.MoveSource);
        Assert.Equal(g.UnitA, g.Map.LastMoveSource);
        Assert.NotEmpty(g.Map.LastMoveTargets);
    }

    [Fact]
    public void PreviousUnit_NoSourcePickedYet_PicksLowestPowerMovableUnit()
    {
        var g = new ThreeUnitsRedGame();
        g.SelectRed();

        g.Hud.PressPreviousUnit();

        Assert.Equal(SessionState.ActionMode.MovingUnit, g.Session.Mode);
        Assert.Equal(g.UnitC, g.Session.MoveSource);
    }

    [Fact]
    public void NextUnit_CyclesForwardThroughUnits_AndWraps()
    {
        var g = new ThreeUnitsRedGame();
        g.SelectRed();

        g.Hud.PressNextUnit();
        Assert.Equal(g.UnitA, g.Session.MoveSource);
        g.Hud.PressNextUnit();
        Assert.Equal(g.UnitB, g.Session.MoveSource);
        g.Hud.PressNextUnit();
        Assert.Equal(g.UnitC, g.Session.MoveSource);
        g.Hud.PressNextUnit();
        Assert.Equal(g.UnitA, g.Session.MoveSource);
    }

    [Fact]
    public void PreviousUnit_CyclesBackwardThroughUnits_AndWraps()
    {
        var g = new ThreeUnitsRedGame();
        g.SelectRed();

        g.Hud.PressPreviousUnit();
        Assert.Equal(g.UnitC, g.Session.MoveSource);
        g.Hud.PressPreviousUnit();
        Assert.Equal(g.UnitB, g.Session.MoveSource);
        g.Hud.PressPreviousUnit();
        Assert.Equal(g.UnitA, g.Session.MoveSource);
        g.Hud.PressPreviousUnit();
        Assert.Equal(g.UnitC, g.Session.MoveSource);
    }

    [Fact]
    public void NextUnit_SkipsAlreadyMovedUnits()
    {
        var g = new ThreeUnitsRedGame();
        // Mark the middle unit as already moved this turn — N must skip it.
        g.State.Grid.Get(g.UnitB)!.Unit!.HasMovedThisTurn = true;
        g.SelectRed();

        g.Hud.PressNextUnit();
        Assert.Equal(g.UnitA, g.Session.MoveSource);
        g.Hud.PressNextUnit();
        Assert.Equal(g.UnitC, g.Session.MoveSource);
        g.Hud.PressNextUnit();
        Assert.Equal(g.UnitA, g.Session.MoveSource);
    }

    [Fact]
    public void NextUnit_FromBuildingTower_EntersMovingUnit_AndClearsTowerOverlays()
    {
        var g = new ThreeUnitsRedGame();
        g.SelectRed();
        HexCoord cap = g.RedTerritory.Capital!.Value;
        g.State.Treasury.SetGold(cap, 20);
        g.Hud.ClickBuildTower();
        Assert.Equal(SessionState.ActionMode.BuildingTower, g.Session.Mode);
        Assert.NotEmpty(g.Map.LastTowerTargets);

        g.Hud.PressNextUnit();

        Assert.Equal(SessionState.ActionMode.MovingUnit, g.Session.Mode);
        Assert.Equal(g.UnitA, g.Session.MoveSource);
        Assert.Empty(g.Map.LastTowerTargets);
        Assert.Empty(g.Map.LastTowerCoverage);
    }

    [Fact]
    public void NextUnit_OneMovableUnitAlreadyTheSource_DoesNotPushUndo()
    {
        var g = new TestGame();
        g.Tile(1, 1).Occupant = new Unit(g.Red.Id);
        g.Map.SimulateClick(g.Tile(1, 1)); // enters MovingUnit on (1,1)
        Assert.Equal(SessionState.ActionMode.MovingUnit, g.Session.Mode);
        Assert.Equal(HexCoord.FromOffset(1, 1), g.Session.MoveSource);
        int baseline = g.Session.Undo.UndoCount;

        g.Hud.PressNextUnit();

        // Single movable unit, already the source: nothing changes.
        Assert.Equal(HexCoord.FromOffset(1, 1), g.Session.MoveSource);
        Assert.Equal(baseline, g.Session.Undo.UndoCount);
    }

    [Fact]
    public void NextUnit_AfterWin_IsNoOp()
    {
        var g = new ThreeUnitsRedGame();
        g.SelectRed();
        g.Session.Winner = g.Red.Id;

        g.Hud.PressNextUnit();

        Assert.Null(g.Session.MoveSource);
        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
    }

    [Fact]
    public void NextUnit_PushesUndo_AndIsReversible()
    {
        var g = new ThreeUnitsRedGame();
        g.SelectRed();
        int baseline = g.Session.Undo.UndoCount;

        g.Hud.PressNextUnit();
        Assert.Equal(baseline + 1, g.Session.Undo.UndoCount);
        Assert.Equal(SessionState.ActionMode.MovingUnit, g.Session.Mode);
        Assert.Equal(g.UnitA, g.Session.MoveSource);

        g.Hud.ClickUndoLast();

        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
        Assert.Null(g.Session.MoveSource);
    }

    [Fact]
    public void NextUnit_ChangingFromOneSourceToAnother_PushesUndo_AndIsReversible()
    {
        var g = new ThreeUnitsRedGame();
        g.SelectRed();

        g.Hud.PressNextUnit(); // → UnitA
        Assert.Equal(g.UnitA, g.Session.MoveSource);
        g.Hud.PressNextUnit(); // → UnitB
        Assert.Equal(g.UnitB, g.Session.MoveSource);

        g.Hud.ClickUndoLast(); // restores MoveSource = UnitA

        Assert.Equal(g.UnitA, g.Session.MoveSource);
        Assert.Equal(SessionState.ActionMode.MovingUnit, g.Session.Mode);
    }

    // --- N / Shift+N: power-ordered cycle ---------------------------------

    [Fact]
    public void NextUnit_PowerOrder_DescendsByLevel()
    {
        // Replace the three Recruits with a Recruit, a Soldier, and a
        // Captain. N walks them strongest-first.
        var g = new ThreeUnitsRedGame();
        g.State.Grid.Get(g.UnitA)!.Occupant = new Unit(g.Red.Id, UnitLevel.Recruit);
        g.State.Grid.Get(g.UnitB)!.Occupant = new Unit(g.Red.Id, UnitLevel.Soldier);
        g.State.Grid.Get(g.UnitC)!.Occupant = new Unit(g.Red.Id, UnitLevel.Captain);
        g.SelectRed();

        g.Hud.PressNextUnit();
        Assert.Equal(g.UnitC, g.Session.MoveSource); // Captain (highest)
        g.Hud.PressNextUnit();
        Assert.Equal(g.UnitB, g.Session.MoveSource); // Soldier
        g.Hud.PressNextUnit();
        Assert.Equal(g.UnitA, g.Session.MoveSource); // Recruit
        g.Hud.PressNextUnit();
        Assert.Equal(g.UnitC, g.Session.MoveSource); // wraps back to Captain
    }

    [Fact]
    public void NextUnit_PowerOrder_LexTiebreakerWithinTier()
    {
        // Level-desc-then-coord-lex-asc: with two Recruits and one Soldier,
        // cycle goes Soldier-B → Recruit-A → Recruit-C → wrap.
        var g = new ThreeUnitsRedGame();
        g.State.Grid.Get(g.UnitA)!.Occupant = new Unit(g.Red.Id, UnitLevel.Recruit);
        g.State.Grid.Get(g.UnitB)!.Occupant = new Unit(g.Red.Id, UnitLevel.Soldier);
        g.State.Grid.Get(g.UnitC)!.Occupant = new Unit(g.Red.Id, UnitLevel.Recruit);
        g.SelectRed();

        g.Hud.PressNextUnit();
        Assert.Equal(g.UnitB, g.Session.MoveSource); // Soldier (highest tier)
        g.Hud.PressNextUnit();
        Assert.Equal(g.UnitA, g.Session.MoveSource); // Recruit at (1,1) — next tier, lex first
        g.Hud.PressNextUnit();
        Assert.Equal(g.UnitC, g.Session.MoveSource); // Recruit at (3,1) — same tier, lex next
    }

    [Fact]
    public void PreviousUnit_PowerOrder_WalksLevelAscending()
    {
        var g = new ThreeUnitsRedGame();
        g.State.Grid.Get(g.UnitA)!.Occupant = new Unit(g.Red.Id, UnitLevel.Recruit);
        g.State.Grid.Get(g.UnitB)!.Occupant = new Unit(g.Red.Id, UnitLevel.Soldier);
        g.State.Grid.Get(g.UnitC)!.Occupant = new Unit(g.Red.Id, UnitLevel.Captain);
        g.SelectRed();

        g.Hud.PressPreviousUnit();
        Assert.Equal(g.UnitA, g.Session.MoveSource); // Recruit (lowest)
        g.Hud.PressPreviousUnit();
        Assert.Equal(g.UnitB, g.Session.MoveSource); // Soldier
        g.Hud.PressPreviousUnit();
        Assert.Equal(g.UnitC, g.Session.MoveSource); // Captain
        g.Hud.PressPreviousUnit();
        Assert.Equal(g.UnitA, g.Session.MoveSource); // wraps to Recruit
    }

    // --- Repeated-movement mode: flag, auto-advance, exits ---------------

    [Fact]
    public void NextUnit_TurnsOnRepeatedMovementFlag()
    {
        var g = new ThreeUnitsRedGame();
        g.SelectRed();
        Assert.False(g.Session.RepeatedMovement);

        g.Hud.PressNextUnit();

        Assert.True(g.Session.RepeatedMovement);
    }

    [Fact]
    public void RepeatedMovement_AfterMove_AutoAdvancesToNextUnitInPowerOrder()
    {
        var g = new ThreeUnitsRedGame();
        g.SelectRed();
        g.Hud.PressNextUnit(); // picks UnitA (Recruit), flag → true
        Assert.Equal(g.UnitA, g.Session.MoveSource);

        // (4,1) is empty Red — a valid in-territory move target.
        g.Map.SimulateClick(g.State.Grid.Get(HexCoord.FromOffset(4, 1)));

        // Unit moved off (1,1).
        Assert.Null(g.State.Grid.Get(g.UnitA)!.Unit);
        Assert.NotNull(g.State.Grid.Get(HexCoord.FromOffset(4, 1))!.Unit);
        // Flag still on; next unit auto-picked.
        Assert.True(g.Session.RepeatedMovement);
        Assert.Equal(SessionState.ActionMode.MovingUnit, g.Session.Mode);
        Assert.Equal(g.UnitB, g.Session.MoveSource);
    }

    [Fact]
    public void RepeatedMovement_CombineWithFriendlyUnit_ClearsFlag_AndExitsMovingMode()
    {
        // Combining onto a friendly unit is an explicit punctuation point
        // in a streak of moves — the sticky flag clears and Mode exits so
        // the player has to re-press N to keep auto-advancing.
        var g = new ThreeUnitsRedGame();
        // Drop a stationary friendly recruit on the click target so the
        // move from UnitA → (4,1) becomes a combine instead of an empty
        // placement. Mark it already-moved so NextUnit's power-then-coord
        // walk still picks UnitA first (otherwise it might land on (4,1)).
        var stationary = new Unit(g.Red.Id, UnitLevel.Recruit);
        stationary.HasMovedThisTurn = true;
        g.State.Grid.Get(HexCoord.FromOffset(4, 1))!.Occupant = stationary;

        g.SelectRed();
        g.Hud.PressNextUnit(); // picks UnitA, flag → true
        Assert.Equal(g.UnitA, g.Session.MoveSource);
        Assert.True(g.Session.RepeatedMovement);

        g.Map.SimulateClick(g.State.Grid.Get(HexCoord.FromOffset(4, 1))); // combine

        // Combine happened: stationary recruit + moved recruit → Soldier.
        Assert.Equal(UnitLevel.Soldier, g.State.Grid.Get(HexCoord.FromOffset(4, 1))!.Unit!.Level);
        Assert.Null(g.State.Grid.Get(g.UnitA)!.Unit);
        // Mode and sticky flag both exited.
        Assert.False(g.Session.RepeatedMovement);
        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
        Assert.Null(g.Session.MoveSource);
    }

    [Fact]
    public void RepeatedMovement_AutoAdvanceFromLastMovableUnit_ClearsFlag()
    {
        // Only UnitC is unmoved — A and B already spent.
        var g = new ThreeUnitsRedGame();
        g.State.Grid.Get(g.UnitA)!.Unit!.HasMovedThisTurn = true;
        g.State.Grid.Get(g.UnitB)!.Unit!.HasMovedThisTurn = true;
        g.SelectRed();
        g.Hud.PressNextUnit();
        Assert.Equal(g.UnitC, g.Session.MoveSource);
        Assert.True(g.Session.RepeatedMovement);

        g.Map.SimulateClick(g.State.Grid.Get(HexCoord.FromOffset(4, 1)));

        Assert.False(g.Session.RepeatedMovement);
        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
        Assert.Null(g.Session.MoveSource);
    }

    [Fact]
    public void RepeatedMovement_Cancel_ClearsFlag()
    {
        var g = new ThreeUnitsRedGame();
        g.SelectRed();
        g.Hud.PressNextUnit();
        Assert.True(g.Session.RepeatedMovement);

        g.Hud.PressCancelAction();

        Assert.False(g.Session.RepeatedMovement);
        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
    }

    [Fact]
    public void RepeatedMovement_BuyRecruitHotkey_ClearsFlag()
    {
        var g = new ThreeUnitsRedGame();
        g.SelectRed();
        // Ensure recruit is affordable so U enters BuyingRecruit (otherwise
        // the cycle exits back to None and the flag-clear is still tested
        // — but make the assertion deterministic about the buy-mode entry).
        HexCoord cap = g.RedTerritory.Capital!.Value;
        g.State.Treasury.SetGold(cap, 20);
        g.Hud.PressNextUnit();
        Assert.True(g.Session.RepeatedMovement);

        g.Hud.ClickBuyRecruit();

        Assert.False(g.Session.RepeatedMovement);
        Assert.Equal(SessionState.ActionMode.BuyingRecruit, g.Session.Mode);
    }

    [Fact]
    public void RepeatedMovement_BuyUnitDirectButton_ClearsFlag()
    {
        var g = new ThreeUnitsRedGame();
        g.SelectRed();
        HexCoord cap = g.RedTerritory.Capital!.Value;
        g.State.Treasury.SetGold(cap, 20);
        g.Hud.PressNextUnit();
        Assert.True(g.Session.RepeatedMovement);

        g.Hud.ClickBuyUnit(UnitLevel.Recruit);

        Assert.False(g.Session.RepeatedMovement);
        Assert.Equal(SessionState.ActionMode.BuyingRecruit, g.Session.Mode);
    }

    [Fact]
    public void RepeatedMovement_BuildTower_ClearsFlag()
    {
        var g = new ThreeUnitsRedGame();
        g.SelectRed();
        HexCoord cap = g.RedTerritory.Capital!.Value;
        g.State.Treasury.SetGold(cap, 20);
        g.Hud.PressNextUnit();
        Assert.True(g.Session.RepeatedMovement);

        g.Hud.ClickBuildTower();

        Assert.False(g.Session.RepeatedMovement);
        Assert.Equal(SessionState.ActionMode.BuildingTower, g.Session.Mode);
    }

    [Fact]
    public void RepeatedMovement_EndTurn_ClearsFlag()
    {
        var g = new ThreeUnitsRedGame();
        g.SelectRed();
        g.Hud.PressNextUnit();
        Assert.True(g.Session.RepeatedMovement);

        g.Hud.ClickEndTurn();

        Assert.False(g.Session.RepeatedMovement);
    }

    [Fact]
    public void RepeatedMovement_AdjacentInvalidClick_KeepsFlagOn()
    {
        // Clicking the (adjacent) Blue capital is an in-range near-miss
        // (defended, Recruit can't capture). Under the in-range-stays
        // policy, the move mode flashes but stays, so repeated-movement
        // also persists — the user can adjust without losing context.
        var g = new ThreeUnitsRedGame();
        g.SelectRed();
        g.Hud.PressNextUnit();
        Assert.True(g.Session.RepeatedMovement);

        Territory blueT = g.State.Territories.First(t => t.Owner == g.Blue.Id);
        g.Map.SimulateClick(g.State.Grid.Get(blueT.Capital!.Value));

        Assert.True(g.Session.RepeatedMovement);
        Assert.Equal(SessionState.ActionMode.MovingUnit, g.Session.Mode);
    }

    [Fact]
    public void RepeatedMovement_FarAwayClick_ClearsFlag()
    {
        // A click that's truly out of range (non-adjacent enemy tile)
        // cancels the mode → flag clears. Uses a custom 10x2 grid so
        // there's a Blue tile far enough from Red to be unreachable
        // (ThreeUnitsRedGame's 6x2 layout has every Blue tile adjacent
        // to Red).
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        var players = new List<Player> { red, blue };

        var grid = TestHelpers.BuildRectGrid(10, 2, blue.Id);
        for (int col = 0; col <= 3; col++)
        {
            grid.Get(HexCoord.FromOffset(col, 1))!.Owner = red.Id;
        }

        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var session = new SessionState();
        session.ClaimVictoryPromptedHighestThreshold[red.Id] = 90;
        session.ClaimVictoryPromptedHighestThreshold[blue.Id] = 90;
        var map = new MockHexMapView();
        var hud = new MockHudView();
        var controller = new GameController(state, session, map, hud);
        controller.StartGame();

        // Drop a Recruit at (1,1) (post StartGame so the start-of-turn
        // reset doesn't undo HasMoved state).
        grid.Get(HexCoord.FromOffset(1, 1))!.Occupant = new Unit(red.Id);

        map.SimulateClick(grid.Get(HexCoord.FromOffset(0, 1))); // select Red
        hud.PressNextUnit();
        Assert.True(session.RepeatedMovement);

        // (9,0) is at the far end of the grid — well beyond reach.
        map.SimulateClick(grid.Get(HexCoord.FromOffset(9, 0)));

        Assert.False(session.RepeatedMovement);
        Assert.Equal(SessionState.ActionMode.None, session.Mode);
    }

    [Fact]
    public void RepeatedMovement_CaptureRebind_KeepsFlagAndAutoPicksInReboundTerritory()
    {
        // 7x1 grid: Red at cols 0..3, Blue at cols 4..6 with Trees on
        // (4,0)+(5,0) so Blue's capital placement falls on (6,0) (the
        // lex-min empty Blue tile). Trees defend 0 and the capital at
        // (6,0) doesn't radiate to (4,0) (non-adjacent: (4,0)→(5,0)→(6,0)),
        // so a Red Recruit can clear the tree at (4,0) — that's a capture
        // (Blue→Red ownership flip + tree destroyed) but Blue still holds
        // (5,0)+(6,0) with a capital, so the game continues and the
        // capture-rebind path is exercised in isolation from game-over.
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        var players = new List<Player> { red, blue };

        var grid = TestHelpers.BuildRectGrid(7, 1, red.Id);
        grid.Get(HexCoord.FromOffset(4, 0))!.Owner = blue.Id;
        grid.Get(HexCoord.FromOffset(5, 0))!.Owner = blue.Id;
        grid.Get(HexCoord.FromOffset(6, 0))!.Owner = blue.Id;
        grid.Get(HexCoord.FromOffset(4, 0))!.Occupant = new Tree();
        grid.Get(HexCoord.FromOffset(5, 0))!.Occupant = new Tree();

        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var session = new SessionState();
        session.ClaimVictoryPromptedHighestThreshold[red.Id] = 90;
        session.ClaimVictoryPromptedHighestThreshold[blue.Id] = 90;
        var map = new MockHexMapView();
        var hud = new MockHudView();
        var controller = new GameController(state, session, map, hud);
        controller.StartGame();

        // Place units after StartGame so they don't get their HasMoved reset.
        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = new Unit(red.Id);
        grid.Get(HexCoord.FromOffset(3, 0))!.Occupant = new Unit(red.Id);

        map.SimulateClick(grid.Get(HexCoord.FromOffset(0, 0))); // select Red
        // Two Recruits in the territory; first N picks lex-min: (1,0).
        // Advance once to pick (3,0) — the attacker we want.
        hud.PressNextUnit();
        Assert.Equal(HexCoord.FromOffset(1, 0), session.MoveSource);
        hud.PressNextUnit();
        Assert.Equal(HexCoord.FromOffset(3, 0), session.MoveSource);
        Assert.True(session.RepeatedMovement);

        // Capture (4,0) — Blue-owned tree-occupied tile, undefended.
        map.SimulateClick(grid.Get(HexCoord.FromOffset(4, 0)));

        Assert.True(session.RepeatedMovement);
        Assert.Equal(SessionState.ActionMode.MovingUnit, session.Mode);
        // (3,0) is now empty (the attacker moved to (4,0) with HasMoved=true);
        // (1,0)'s Recruit is the only unmoved unit remaining in the rebound
        // Red territory.
        Assert.Equal(HexCoord.FromOffset(1, 0), session.MoveSource);
    }

    [Fact]
    public void Undo_AfterEnteringRepeatedMovement_RevertsFlagAndModeAndMoveSource()
    {
        var g = new ThreeUnitsRedGame();
        g.SelectRed();
        Assert.False(g.Session.RepeatedMovement);
        g.Hud.PressNextUnit();
        Assert.True(g.Session.RepeatedMovement);

        g.Hud.ClickUndoLast();

        Assert.False(g.Session.RepeatedMovement);
        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
        Assert.Null(g.Session.MoveSource);
    }

    [Fact]
    public void Undo_AfterAutoAdvancedMove_RestoresOriginalSource_FlagStillOn()
    {
        var g = new ThreeUnitsRedGame();
        g.SelectRed();
        g.Hud.PressNextUnit(); // picks UnitA, flag → true (undo entry #1)
        g.Map.SimulateClick(g.State.Grid.Get(HexCoord.FromOffset(4, 1))); // move + auto-advance (entry #2)
        Assert.Equal(g.UnitB, g.Session.MoveSource);

        g.Hud.ClickUndoLast(); // pops entry #2

        // Move reverted: unit back on (1,1), target empty again.
        Assert.NotNull(g.State.Grid.Get(g.UnitA)!.Unit);
        Assert.Null(g.State.Grid.Get(HexCoord.FromOffset(4, 1))!.Unit);
        // MoveSource and flag restored to the pre-place state.
        Assert.Equal(g.UnitA, g.Session.MoveSource);
        Assert.True(g.Session.RepeatedMovement);
        Assert.Equal(SessionState.ActionMode.MovingUnit, g.Session.Mode);
    }

    [Fact]
    public void RepeatedMovement_LongPressRally_ClearsFlag_AndRallyFires()
    {
        // Long-press rally normally short-circuits when Mode != None,
        // but repeated-movement is a passive sticky intent — a deliberate
        // long-press should override it: clear the flag, cancel the
        // pending pick, and rally as usual.
        var g = new ThreeUnitsRedGame();
        g.SelectRed();
        g.Hud.PressNextUnit(); // Mode=MovingUnit on UnitA, flag on
        Assert.True(g.Session.RepeatedMovement);
        Assert.Equal(SessionState.ActionMode.MovingUnit, g.Session.Mode);
        int rallyCountBefore = g.Map.RallySoundCount;

        // Long-press (4,1), an empty Red tile — every unmoved unit
        // gravitates toward it. Rally must fire AND the flag must clear.
        g.Map.SimulateLongClick(g.State.Grid.Get(HexCoord.FromOffset(4, 1)));

        Assert.False(g.Session.RepeatedMovement);
        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
        Assert.Equal(rallyCountBefore + 1, g.Map.RallySoundCount);
    }

    [Fact]
    public void Redo_AfterAutoAdvancedMove_ReappliesMoveAndAutoAdvance()
    {
        var g = new ThreeUnitsRedGame();
        g.SelectRed();
        g.Hud.PressNextUnit();
        g.Map.SimulateClick(g.State.Grid.Get(HexCoord.FromOffset(4, 1)));
        g.Hud.ClickUndoLast(); // back to pre-place

        g.Hud.ClickRedoLast(); // re-apply move + auto-advance

        Assert.Null(g.State.Grid.Get(g.UnitA)!.Unit);
        Assert.NotNull(g.State.Grid.Get(HexCoord.FromOffset(4, 1))!.Unit);
        Assert.True(g.Session.RepeatedMovement);
        Assert.Equal(g.UnitB, g.Session.MoveSource);
    }
}
