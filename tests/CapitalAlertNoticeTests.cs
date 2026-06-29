using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Tests for the tap-summoned capital-alert notice. The notice is summoned by
/// tapping an alerted human-owned capital and dismissed by any subsequent
/// human action (or by re-tapping the same capital). It is view-layer
/// state only — never pushed into the undo stack.
/// </summary>
public class CapitalAlertNoticeTests
{
    /// <summary>
    /// Test fixture: 3x2 grid. Red owns (0,1) and (1,1) — a 2-tile
    /// territory whose capital gets placed automatically. Blue owns the
    /// other 4 tiles. Red has a unit placed manually on its non-capital
    /// tile so we can drive the territory into different
    /// <see cref="EconomyOutlook"/> classifications by varying the unit
    /// level + treasury gold. It's Red's turn (Red is Human).
    /// </summary>
    private class AlertFixture
    {
        public GameState State { get; }
        public SessionState Session { get; }
        public MockHexMapView Map { get; }
        public MockHudView Hud { get; }
        public GameController Controller { get; }
        public Player Red { get; }
        public Player Blue { get; }
        public Territory RedTerritory => State.Territories.First(t => t.Owner == Red.Id);
        public Territory BlueTerritory => State.Territories.First(t => t.Owner == Blue.Id);
        public HexCoord RedCapital => RedTerritory.Capital!.Value;
        public HexCoord BlueCapital => BlueTerritory.Capital!.Value;

        public AlertFixture(bool redIsAi = false, bool blueIsAi = true)
        {
            Red = new Player("Red", PlayerId.FromIndex(0), redIsAi);
            Blue = new Player("Blue", PlayerId.FromIndex(1), blueIsAi);
            var players = new List<Player> { Red, Blue };

            var grid = TestHelpers.BuildRectGrid(3, 2, Blue.Id);
            grid.Get(HexCoord.FromOffset(0, 1))!.Owner = Red.Id;
            grid.Get(HexCoord.FromOffset(1, 1))!.Owner = Red.Id;

            IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);

            State = new GameState(grid, territories, players, new TurnState(players), new Treasury());
            Session = new SessionState();
            Session.ClaimVictoryPromptedHighestThreshold[Red.Id] = 90;
            Session.ClaimVictoryPromptedHighestThreshold[Blue.Id] = 90;
            Map = new MockHexMapView();
            Hud = new MockHudView();
            Controller = new GameController(State, Session, Map, Hud);
            Controller.StartGame();
        }

        public HexTile Tile(int col, int row) => State.Grid.Get(HexCoord.FromOffset(col, row))!;

        /// <summary>Drive Red's territory into BankruptNextTurn by placing a
        /// Captain (upkeep 18) on a non-capital tile with treasury = 0
        /// (income 2 + reserves 0 = 2 &lt; 18).</summary>
        public void ForceRedBankruptNextTurn()
        {
            HexCoord nonCapital = RedTerritory.Coords.First(c => c != RedCapital);
            State.Grid.Get(nonCapital)!.Occupant = new Unit(Red.Id, UnitLevel.Captain);
            State.Treasury.SetGold(RedCapital, 0);
            Assert.Equal(EconomyOutlook.BankruptNextTurn,
                UpkeepRules.Classify(RedTerritory, State.Grid, State.Treasury, Difficulty.Soldier));
        }

        /// <summary>Drive Red's territory into NegativeDelta by placing a
        /// Soldier (upkeep 6) on a non-capital tile with enough reserves
        /// to cover next turn (income 2 + reserves 100 = 102 &gt;= 6, but
        /// income 2 &lt; upkeep 6 → bleeding).</summary>
        public void ForceRedNegativeDelta()
        {
            HexCoord nonCapital = RedTerritory.Coords.First(c => c != RedCapital);
            State.Grid.Get(nonCapital)!.Occupant = new Unit(Red.Id, UnitLevel.Soldier);
            State.Treasury.SetGold(RedCapital, 100);
            Assert.Equal(EconomyOutlook.NegativeDelta,
                UpkeepRules.Classify(RedTerritory, State.Grid, State.Treasury, Difficulty.Soldier));
        }
    }

    // --- Summon on tap ---------------------------------------------------

    [Fact]
    public void TapOnRedBadgedHumanCapital_Summons_BankruptNextTurnNotice()
    {
        var f = new AlertFixture();
        f.ForceRedBankruptNextTurn();

        f.Map.SimulateClick(f.State.Grid.Get(f.RedCapital)!);

        Assert.Equal(f.RedCapital, f.Hud.SummonedCapitalAlertCoord);
        Assert.Equal(EconomyOutlook.BankruptNextTurn, f.Hud.LastSummonedAlertOutlook);
        Assert.Equal(1, f.Hud.SummonAlertCallCount);
    }

    [Fact]
    public void TapOnYellowBadgedHumanCapital_Summons_NegativeDeltaNotice()
    {
        var f = new AlertFixture();
        f.ForceRedNegativeDelta();

        f.Map.SimulateClick(f.State.Grid.Get(f.RedCapital)!);

        Assert.Equal(f.RedCapital, f.Hud.SummonedCapitalAlertCoord);
        Assert.Equal(EconomyOutlook.NegativeDelta, f.Hud.LastSummonedAlertOutlook);
        Assert.Equal(1, f.Hud.SummonAlertCallCount);
    }

    [Fact]
    public void TapOnHealthyHumanCapital_DoesNotSummon()
    {
        var f = new AlertFixture();
        // No units placed → owed = 0 → Healthy.
        Assert.Equal(EconomyOutlook.Healthy,
            UpkeepRules.Classify(f.RedTerritory, f.State.Grid, f.State.Treasury, Difficulty.Soldier));

        f.Map.SimulateClick(f.State.Grid.Get(f.RedCapital)!);

        Assert.Null(f.Hud.SummonedCapitalAlertCoord);
        Assert.Equal(0, f.Hud.SummonAlertCallCount);
    }

    [Fact]
    public void TapOnNonCapitalTileInAlertedTerritory_DoesNotSummon()
    {
        var f = new AlertFixture();
        f.ForceRedBankruptNextTurn();
        HexCoord nonCapital = f.RedTerritory.Coords.First(c => c != f.RedCapital);

        f.Map.SimulateClick(f.State.Grid.Get(nonCapital)!);

        Assert.Null(f.Hud.SummonedCapitalAlertCoord);
        Assert.Equal(0, f.Hud.SummonAlertCallCount);
    }

    [Fact]
    public void TapOnAiOwnedCapital_NeverSummons_EvenIfAlerted()
    {
        // Make Red the AI and Blue the human (current player) so a tap
        // on Red's capital lands on an enemy alerted capital from the
        // perspective of the current player. We don't want enemy
        // notices.
        var f = new AlertFixture(redIsAi: true, blueIsAi: false);
        // End Red's (AI) turn so Blue (human) is current. Run controller
        // by simulating AI's turn first if needed — for this simple
        // fixture, Red AI has nothing to do but End Turn fires from the
        // AI loop. To stay deterministic, just construct so Blue is
        // current at construction time by reordering players.
        // Simpler: rebuild with Blue first.
        var blue = new Player("Blue", PlayerId.FromIndex(0));
        var red = new Player("Red", PlayerId.FromIndex(1), isAi: true);
        var players = new List<Player> { blue, red };
        var grid = TestHelpers.BuildRectGrid(3, 2, blue.Id);
        grid.Get(HexCoord.FromOffset(0, 1))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(1, 1))!.Owner = red.Id;
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var session = new SessionState();
        session.ClaimVictoryPromptedHighestThreshold[blue.Id] = 90;
        session.ClaimVictoryPromptedHighestThreshold[red.Id] = 90;
        var map = new MockHexMapView();
        var hud = new MockHudView();
        var ctrl = new GameController(state, session, map, hud);
        ctrl.StartGame();
        // Place a Captain on Red AI territory + zero treasury → Red would
        // be BankruptNextTurn. The badge isn't drawn for AI in HexMapView
        // either, so taps on it must not summon.
        Territory redT = state.Territories.First(t => t.Owner == red.Id);
        HexCoord redCap = redT.Capital!.Value;
        HexCoord nonCap = redT.Coords.First(c => c != redCap);
        state.Grid.Get(nonCap)!.Occupant = new Unit(red.Id, UnitLevel.Captain);
        state.Treasury.SetGold(redCap, 0);

        map.SimulateClick(state.Grid.Get(redCap)!);

        Assert.Null(hud.SummonedCapitalAlertCoord);
        Assert.Equal(0, hud.SummonAlertCallCount);
    }

    // --- Toggle off / swap ----------------------------------------------

    [Fact]
    public void TapOnSameAlertedCapitalTwice_TogglesOff()
    {
        var f = new AlertFixture();
        f.ForceRedBankruptNextTurn();
        HexTile capTile = f.State.Grid.Get(f.RedCapital)!;

        f.Map.SimulateClick(capTile);
        Assert.Equal(f.RedCapital, f.Hud.SummonedCapitalAlertCoord);

        f.Map.SimulateClick(capTile);

        Assert.Null(f.Hud.SummonedCapitalAlertCoord);
        Assert.Equal(1, f.Hud.SummonAlertCallCount);
        // Both taps dismiss (1st via the default-dismiss in TrackHandler
        // + then summon; 2nd via default-dismiss with no re-summon).
        Assert.True(f.Hud.DismissAlertCallCount >= 2);
    }

    [Fact]
    public void TapOnDifferentAlertedCapital_SwapsContent()
    {
        // Build a fixture with TWO Red territories, both alerted with
        // different outlooks. Tapping A → red notice. Tapping B → yellow
        // notice (A dismissed first by the default-dismiss).
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1), isAi: true);
        var players = new List<Player> { red, blue };
        // 6x2 grid: Red owns columns 0..1 (territory A) and 4..5
        // (territory B); Blue owns the middle gap (cols 2..3) so the
        // two Red regions are not contiguous.
        var grid = TestHelpers.BuildRectGrid(6, 2, blue.Id);
        for (int c = 0; c <= 1; c++)
        {
            for (int r = 0; r < 2; r++) grid.Get(HexCoord.FromOffset(c, r))!.Owner = red.Id;
        }
        for (int c = 4; c <= 5; c++)
        {
            for (int r = 0; r < 2; r++) grid.Get(HexCoord.FromOffset(c, r))!.Owner = red.Id;
        }
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var session = new SessionState();
        session.ClaimVictoryPromptedHighestThreshold[red.Id] = 90;
        session.ClaimVictoryPromptedHighestThreshold[blue.Id] = 90;
        var map = new MockHexMapView();
        var hud = new MockHudView();
        var ctrl = new GameController(state, session, map, hud);
        ctrl.StartGame();

        var redTerritories = state.Territories.Where(t => t.Owner == red.Id).OrderBy(t => t.Capital!.Value.Q).ToList();
        Territory tA = redTerritories[0];
        Territory tB = redTerritories[1];
        HexCoord capA = tA.Capital!.Value;
        HexCoord capB = tB.Capital!.Value;

        // Force A → BankruptNextTurn (red): Captain + 0 reserves.
        HexCoord nonCapA = tA.Coords.First(c => c != capA);
        state.Grid.Get(nonCapA)!.Occupant = new Unit(red.Id, UnitLevel.Captain);
        state.Treasury.SetGold(capA, 0);
        // Force B → NegativeDelta (yellow): Soldier + plenty reserves.
        HexCoord nonCapB = tB.Coords.First(c => c != capB);
        state.Grid.Get(nonCapB)!.Occupant = new Unit(red.Id, UnitLevel.Soldier);
        state.Treasury.SetGold(capB, 100);

        Assert.Equal(EconomyOutlook.BankruptNextTurn, UpkeepRules.Classify(tA, state.Grid, state.Treasury, Difficulty.Soldier));
        Assert.Equal(EconomyOutlook.NegativeDelta, UpkeepRules.Classify(tB, state.Grid, state.Treasury, Difficulty.Soldier));

        map.SimulateClick(state.Grid.Get(capA)!);
        Assert.Equal(capA, hud.SummonedCapitalAlertCoord);
        Assert.Equal(EconomyOutlook.BankruptNextTurn, hud.LastSummonedAlertOutlook);

        map.SimulateClick(state.Grid.Get(capB)!);
        Assert.Equal(capB, hud.SummonedCapitalAlertCoord);
        Assert.Equal(EconomyOutlook.NegativeDelta, hud.LastSummonedAlertOutlook);
    }

    // --- Dismiss on other actions ---------------------------------------

    [Fact]
    public void BuyRecruitPress_DismissesNotice()
    {
        var f = new AlertFixture();
        f.ForceRedBankruptNextTurn();
        f.Map.SimulateClick(f.State.Grid.Get(f.RedCapital)!);
        Assert.NotNull(f.Hud.SummonedCapitalAlertCoord);

        f.Hud.ClickBuyRecruit();

        Assert.Null(f.Hud.SummonedCapitalAlertCoord);
    }

    [Fact]
    public void EndTurnPress_DismissesNotice()
    {
        var f = new AlertFixture();
        f.ForceRedBankruptNextTurn();
        f.Map.SimulateClick(f.State.Grid.Get(f.RedCapital)!);
        Assert.NotNull(f.Hud.SummonedCapitalAlertCoord);

        f.Hud.ClickEndTurn();

        Assert.Null(f.Hud.SummonedCapitalAlertCoord);
    }

    [Fact]
    public void UndoPress_DismissesNotice()
    {
        var f = new AlertFixture();
        f.ForceRedBankruptNextTurn();
        // Make an undoable action first so Undo has something to do.
        f.Map.SimulateClick(f.State.Grid.Get(f.RedCapital)!);
        Assert.NotNull(f.Hud.SummonedCapitalAlertCoord);

        f.Hud.ClickUndoLast();

        Assert.Null(f.Hud.SummonedCapitalAlertCoord);
    }

    [Fact]
    public void CancelActionPress_DismissesNotice()
    {
        var f = new AlertFixture();
        f.ForceRedBankruptNextTurn();
        f.Map.SimulateClick(f.State.Grid.Get(f.RedCapital)!);
        Assert.NotNull(f.Hud.SummonedCapitalAlertCoord);

        f.Hud.PressCancelAction();

        Assert.Null(f.Hud.SummonedCapitalAlertCoord);
    }

    [Fact]
    public void TapOnEnemyTile_DismissesNotice()
    {
        var f = new AlertFixture();
        f.ForceRedBankruptNextTurn();
        f.Map.SimulateClick(f.State.Grid.Get(f.RedCapital)!);
        Assert.NotNull(f.Hud.SummonedCapitalAlertCoord);

        // Blue-owned tile.
        f.Map.SimulateClick(f.Tile(2, 0));

        Assert.Null(f.Hud.SummonedCapitalAlertCoord);
    }

    [Fact]
    public void TapOnOwnNonCapitalTile_DismissesNotice()
    {
        var f = new AlertFixture();
        f.ForceRedBankruptNextTurn();
        f.Map.SimulateClick(f.State.Grid.Get(f.RedCapital)!);
        Assert.NotNull(f.Hud.SummonedCapitalAlertCoord);

        HexCoord nonCapital = f.RedTerritory.Coords.First(c => c != f.RedCapital);
        f.Map.SimulateClick(f.State.Grid.Get(nonCapital)!);

        Assert.Null(f.Hud.SummonedCapitalAlertCoord);
    }

    // --- No undo entries ------------------------------------------------

    [Fact]
    public void SummonAndDismiss_DoNotPushUndoEntries()
    {
        var f = new AlertFixture();
        f.ForceRedBankruptNextTurn();
        HexTile capTile = f.State.Grid.Get(f.RedCapital)!;
        // Pre-select the territory so the tap is a same-territory re-tap
        // (no SetSelection state change → no undo push from the click body).
        f.Map.SimulateClick(f.State.Grid.Get(f.RedTerritory.Coords.First(c => c != f.RedCapital))!);
        int undoBefore = f.Session.Undo.UndoCount;

        f.Map.SimulateClick(capTile); // summon
        f.Map.SimulateClick(capTile); // dismiss (toggle off)
        f.Map.SimulateClick(capTile); // summon again

        // Territory already selected pre-measurement, so the capital
        // taps don't trigger a SetSelection state change. The
        // summon/dismiss-only changes are view-layer only and must not
        // push an undo entry.
        Assert.Equal(undoBefore, f.Session.Undo.UndoCount);
    }
}
