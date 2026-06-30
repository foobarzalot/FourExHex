using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

public partial class GameControllerTests
{
    // --- Next territory hotkey -------------------------------------------

    /// <summary>
    /// 4-hex Red strip at (0,0)..(3,0) with Blue elsewhere. Wide enough
    /// to put a tower in the middle and exercise tower-coverage logic
    /// (covered set = tower coord + same-territory neighbors).
    /// </summary>
    private class FourStripGame
    {
        public GameState State { get; }
        public SessionState Session { get; }
        public MockHexMapView Map { get; }
        public MockHudView Hud { get; }
        public GameController Controller { get; }
        public Player Red { get; }
        public Player Blue { get; }

        public FourStripGame(HexCoord? preExistingTowerAt = null)
        {
            Red = new Player("Red", PlayerId.FromIndex(0));
            Blue = new Player("Blue", PlayerId.FromIndex(1));
            var players = new List<Player> { Red, Blue };
            var grid = TestHelpers.BuildRectGrid(8, 1, Blue.Id);
            for (int col = 0; col < 4; col++)
                grid.Get(HexCoord.FromOffset(col, 0))!.Owner = Red.Id;
            if (preExistingTowerAt.HasValue)
                grid.Get(preExistingTowerAt.Value)!.Occupant = new Tower();

            IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
            State = new GameState(grid, territories, players, new TurnState(players), new Treasury());
            Session = new SessionState();
            Map = new MockHexMapView();
            Hud = new MockHudView();
            Controller = new GameController(State, Session, Map, Hud);
            Controller.StartGame();
        }

        public HexTile Tile(int col, int row) =>
            State.Grid.Get(HexCoord.FromOffset(col, row))!;
        public Territory RedTerritory =>
            State.Territories.First(t => t.Owner == Red.Id);
    }

    [Fact]
    public void BuildTower_EnteringMode_ShowsCoverageOfExistingTowers()
    {
        // Red strip (0,0)..(3,0) with a pre-existing tower at (1,0).
        // Coverage = tower coord + same-territory neighbors of (1,0)
        // = { (0,0), (1,0), (2,0) }. (3,0) is at distance 2, uncovered.
        var g = new FourStripGame(preExistingTowerAt: HexCoord.FromOffset(1, 0));
        g.Map.SimulateClick(g.Tile(0, 0));
        g.State.Treasury.SetGold(g.RedTerritory.Capital!.Value, 20);

        g.Hud.ClickBuildTower();

        Assert.Equal(
            new HashSet<HexCoord>
            {
                HexCoord.FromOffset(0, 0),
                HexCoord.FromOffset(1, 0),
                HexCoord.FromOffset(2, 0),
            },
            new HashSet<HexCoord>(g.Map.LastTowerCoverage));
    }

    [Fact]
    public void BuildTower_OverlappingCoverage_PublishedAsSingleEntryPerCoord()
    {
        // Two towers in the same 4-strip overlap on (2,0) — covered
        // by both (1,0) (neighbor) and (3,0) (neighbor). The published
        // coverage list must dedupe so the view doesn't render two
        // overlays on top of each other and the tile doesn't darken
        // twice as much as a single-tower-covered tile.
        var g = new FourStripGame(preExistingTowerAt: HexCoord.FromOffset(1, 0));
        g.Map.SimulateClick(g.Tile(0, 0));
        g.State.Treasury.SetGold(g.RedTerritory.Capital!.Value, 30);
        g.Hud.ClickBuildTower();
        g.Map.SimulateClick(g.Tile(3, 0));

        Assert.Equal(
            g.Map.LastTowerCoverage.Count,
            g.Map.LastTowerCoverage.Distinct().Count());
        Assert.Equal(
            new HashSet<HexCoord>
            {
                HexCoord.FromOffset(0, 0),
                HexCoord.FromOffset(1, 0),
                HexCoord.FromOffset(2, 0),
                HexCoord.FromOffset(3, 0),
            },
            new HashSet<HexCoord>(g.Map.LastTowerCoverage));
    }

    [Fact]
    public void BuildTower_AfterPlace_RefreshesCoverage()
    {
        // No pre-existing tower; build one at (3,0) mid-mode and stay
        // in mode (15g remains, exactly the tower cost). Coverage should
        // now reflect the just-placed tower: { (2,0), (3,0) }.
        var g = new FourStripGame();
        g.Map.SimulateClick(g.Tile(0, 0));
        g.State.Treasury.SetGold(g.RedTerritory.Capital!.Value, 30);

        g.Hud.ClickBuildTower();
        g.Map.SimulateClick(g.Tile(3, 0));

        Assert.Equal(
            new HashSet<HexCoord>
            {
                HexCoord.FromOffset(2, 0),
                HexCoord.FromOffset(3, 0),
            },
            new HashSet<HexCoord>(g.Map.LastTowerCoverage));
    }

    [Fact]
    public void BuildTower_OnInTerritoryInvalidTarget_KeepsCoverageTint()
    {
        // In-range near-miss (tower-occupied tile inside the selected
        // territory): flash, stay in BuildingTower mode, keep the
        // coverage tint visible so the user can pick another in-territory
        // tile without re-pressing the build button.
        var g = new FourStripGame(preExistingTowerAt: HexCoord.FromOffset(1, 0));
        g.Map.SimulateClick(g.Tile(0, 0));
        g.State.Treasury.SetGold(g.RedTerritory.Capital!.Value, 20);
        g.Hud.ClickBuildTower();
        Assert.NotEmpty(g.Map.LastTowerCoverage);

        g.Map.SimulateClick(g.Tile(1, 0)); // tower-occupied, in-territory

        Assert.NotEmpty(g.Map.LastTowerCoverage);
        Assert.Equal(SessionState.ActionMode.BuildingTower, g.Session.Mode);
    }

    [Fact]
    public void Undo_FromTowerBuild_RefreshesOverlaysForRestoredBuildingTowerMode()
    {
        // Pre-existing tower at (1,0). Build a second tower at (3,0)
        // (stays in BuildingTower mode — 15g remains, valid spots
        // remain). Undo should restore the pre-build snapshot AND
        // re-emit the targets/coverage that match it. Without a
        // BuildingTower branch in RestoreOverlaysForCurrentMode the
        // overlays remain stuck on the post-build values.
        var g = new FourStripGame(preExistingTowerAt: HexCoord.FromOffset(1, 0));
        g.Map.SimulateClick(g.Tile(0, 0));
        g.State.Treasury.SetGold(g.RedTerritory.Capital!.Value, 30);
        g.Hud.ClickBuildTower();
        g.Map.SimulateClick(g.Tile(3, 0));

        g.Hud.ClickUndoLast();

        // Capital lands on (0,0), so it's never a legal tower target;
        // (1,0) is the pre-existing tower; pre-build legal placements
        // are the remaining two empty cells.
        Assert.Equal(SessionState.ActionMode.BuildingTower, g.Session.Mode);
        Assert.Equal(
            new HashSet<HexCoord>
            {
                HexCoord.FromOffset(2, 0),
                HexCoord.FromOffset(3, 0),
            },
            new HashSet<HexCoord>(g.Map.LastTowerTargets));
        Assert.Equal(
            new HashSet<HexCoord>
            {
                HexCoord.FromOffset(0, 0),
                HexCoord.FromOffset(1, 0),
                HexCoord.FromOffset(2, 0),
            },
            new HashSet<HexCoord>(g.Map.LastTowerCoverage));
    }

    [Fact]
    public void Undo_OutOfBuildingTowerMode_ClearsTowerOverlays()
    {
        // Two undos walk back: first to BuildingTower-with-1-tower,
        // then to Mode=None. With Mode=None the tower preview and
        // coverage tint must both be empty — otherwise leftover
        // overlays bleed into normal click handling.
        var g = new FourStripGame(preExistingTowerAt: HexCoord.FromOffset(1, 0));
        g.Map.SimulateClick(g.Tile(0, 0));
        g.State.Treasury.SetGold(g.RedTerritory.Capital!.Value, 30);
        g.Hud.ClickBuildTower();
        g.Map.SimulateClick(g.Tile(3, 0));

        g.Hud.ClickUndoLast(); // back to BuildingTower with 1 tower
        g.Hud.ClickUndoLast(); // back to Mode=None

        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
        Assert.Empty(g.Map.LastTowerTargets);
        Assert.Empty(g.Map.LastTowerCoverage);
    }

    [Fact]
    public void Redo_BackIntoBuildingTowerMode_RefreshesTowerOverlays()
    {
        // build → undo → undo (to None) → redo (back to BuildingTower
        // with 1 tower). Mock state right before the redo is empty
        // (Mode=None cleared everything), so the redo must actively
        // re-emit BuildingTower's targets+coverage. Bug repro: redo
        // currently leaves the overlays empty.
        var g = new FourStripGame(preExistingTowerAt: HexCoord.FromOffset(1, 0));
        g.Map.SimulateClick(g.Tile(0, 0));
        g.State.Treasury.SetGold(g.RedTerritory.Capital!.Value, 30);
        g.Hud.ClickBuildTower();
        g.Map.SimulateClick(g.Tile(3, 0));
        g.Hud.ClickUndoLast();
        g.Hud.ClickUndoLast();

        g.Hud.ClickRedoLast();

        Assert.Equal(SessionState.ActionMode.BuildingTower, g.Session.Mode);
        Assert.Equal(
            new HashSet<HexCoord>
            {
                HexCoord.FromOffset(2, 0),
                HexCoord.FromOffset(3, 0),
            },
            new HashSet<HexCoord>(g.Map.LastTowerTargets));
        Assert.Equal(
            new HashSet<HexCoord>
            {
                HexCoord.FromOffset(0, 0),
                HexCoord.FromOffset(1, 0),
                HexCoord.FromOffset(2, 0),
            },
            new HashSet<HexCoord>(g.Map.LastTowerCoverage));
    }

    [Fact]
    public void BuildTower_ThenBuyRecruit_ClearsCoverage()
    {
        var g = new FourStripGame(preExistingTowerAt: HexCoord.FromOffset(1, 0));
        g.Map.SimulateClick(g.Tile(0, 0));
        g.State.Treasury.SetGold(g.RedTerritory.Capital!.Value, 25);
        g.Hud.ClickBuildTower();
        Assert.NotEmpty(g.Map.LastTowerCoverage);

        g.Hud.ClickBuyRecruit();

        Assert.Empty(g.Map.LastTowerCoverage);
    }

    /// <summary>
    /// Build a two-player, two-Red-territory fixture so we can exercise
    /// territory cycling. Red has a capital in each of two disjoint
    /// multi-hex blobs; Blue has one blob. Red is the starting player.
    /// </summary>
    private class TwoRedTerritoriesGame
    {
        public GameState State { get; }
        public SessionState Session { get; }
        public MockHexMapView Map { get; }
        public MockHudView Hud { get; }
        public GameController Controller { get; }
        public Player Red { get; }
        public Player Blue { get; }

        public TwoRedTerritoriesGame(bool autoSelect = false)
        {
            Red = new Player("Red", PlayerId.FromIndex(0));
            Blue = new Player("Blue", PlayerId.FromIndex(1));
            var players = new List<Player> { Red, Blue };

            // 10x1 Blue grid. Overlay two disjoint 2-tile Red blobs:
            // A at columns 0-1, B at columns 5-6.
            var grid = TestHelpers.BuildRectGrid(10, 1, Blue.Id);
            grid.Get(HexCoord.FromOffset(0, 0))!.Owner = Red.Id;
            grid.Get(HexCoord.FromOffset(1, 0))!.Owner = Red.Id;
            grid.Get(HexCoord.FromOffset(5, 0))!.Owner = Red.Id;
            grid.Get(HexCoord.FromOffset(6, 0))!.Owner = Red.Id;

            IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);

            State = new GameState(grid, territories, players, new TurnState(players), new Treasury());
            Session = new SessionState();
            Map = new MockHexMapView();
            Hud = new MockHudView();
            Controller = new GameController(State, Session, Map, Hud,
                autoSelectFirstTerritory: autoSelect);
            Controller.StartGame();
        }

        public IEnumerable<Territory> RedTerritories =>
            State.Territories.Where(t => t.Owner == Red.Id);
    }

    /// <summary>
    /// Three-blob variant of <see cref="TwoRedTerritoriesGame"/> for tests
    /// that need to verify Tab skips a middle territory. Red owns three
    /// disjoint 2-tile blobs at columns 0-1, 5-6, and 10-11; Blue owns
    /// the rest of a 12x1 strip. After StartGame each Red capital holds
    /// 10 gold (5 × 2 earning cells), so by default every blob is
    /// actionable; tests flip individual ones actionless by zeroing gold.
    /// </summary>
    private class ThreeRedTerritoriesGame
    {
        public GameState State { get; }
        public SessionState Session { get; }
        public MockHexMapView Map { get; }
        public MockHudView Hud { get; }
        public GameController Controller { get; }
        public Player Red { get; }
        public Player Blue { get; }

        public ThreeRedTerritoriesGame(bool autoSelect = false)
        {
            Red = new Player("Red", PlayerId.FromIndex(0));
            Blue = new Player("Blue", PlayerId.FromIndex(1));
            var players = new List<Player> { Red, Blue };

            var grid = TestHelpers.BuildRectGrid(12, 1, Blue.Id);
            grid.Get(HexCoord.FromOffset(0, 0))!.Owner = Red.Id;
            grid.Get(HexCoord.FromOffset(1, 0))!.Owner = Red.Id;
            grid.Get(HexCoord.FromOffset(5, 0))!.Owner = Red.Id;
            grid.Get(HexCoord.FromOffset(6, 0))!.Owner = Red.Id;
            grid.Get(HexCoord.FromOffset(10, 0))!.Owner = Red.Id;
            grid.Get(HexCoord.FromOffset(11, 0))!.Owner = Red.Id;

            IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);

            State = new GameState(grid, territories, players, new TurnState(players), new Treasury());
            Session = new SessionState();
            Map = new MockHexMapView();
            Hud = new MockHudView();
            Controller = new GameController(State, Session, Map, Hud,
                autoSelectFirstTerritory: autoSelect);
            Controller.StartGame();
        }

        public Territory RedTerritoryAt(int col, int row) =>
            State.Territories.First(t =>
                t.Owner == Red.Id && t.Coords.Contains(HexCoord.FromOffset(col, row)));
    }

    /// <summary>
    /// Two Red territories of DIFFERENT sizes: small (2 tiles at cols 0-1, capital
    /// at (0,0)) and big (3 tiles at cols 5-7, capital at (5,0)). Used to verify
    /// that next-territory cycling visits the larger territory first.
    /// </summary>
    private class UnequalRedTerritoriesGame
    {
        public GameState State { get; }
        public SessionState Session { get; }
        public MockHexMapView Map { get; }
        public MockHudView Hud { get; }
        public GameController Controller { get; }
        public Player Red { get; }
        public Player Blue { get; }

        public UnequalRedTerritoriesGame(bool autoSelect = false)
        {
            Red = new Player("Red", PlayerId.FromIndex(0));
            Blue = new Player("Blue", PlayerId.FromIndex(1));
            var players = new List<Player> { Red, Blue };

            var grid = TestHelpers.BuildRectGrid(12, 1, Blue.Id);
            grid.Get(HexCoord.FromOffset(0, 0))!.Owner = Red.Id;
            grid.Get(HexCoord.FromOffset(1, 0))!.Owner = Red.Id;
            grid.Get(HexCoord.FromOffset(5, 0))!.Owner = Red.Id;
            grid.Get(HexCoord.FromOffset(6, 0))!.Owner = Red.Id;
            grid.Get(HexCoord.FromOffset(7, 0))!.Owner = Red.Id;

            IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);

            State = new GameState(grid, territories, players, new TurnState(players), new Treasury());
            Session = new SessionState();
            Session.ClaimVictoryPromptedHighestThreshold[Red.Id] = 90;
            Session.ClaimVictoryPromptedHighestThreshold[Blue.Id] = 90;
            Map = new MockHexMapView();
            Hud = new MockHudView();
            Controller = new GameController(State, Session, Map, Hud,
                autoSelectFirstTerritory: autoSelect);
            Controller.StartGame();
        }
    }

    [Fact]
    public void NextTerritory_NoneSelected_SelectsLexMinCapital()
    {
        var g = new TwoRedTerritoriesGame();
        Assert.Null(g.Session.SelectedTerritory);

        g.Hud.PressNextTerritory();

        // Two Red territories: {(0,0),(1,0)} and {(5,0),(6,0)}. Sorted
        // by capital coord → the blob with capital (0,0) wins.
        Assert.NotNull(g.Session.SelectedTerritory);
        Assert.Contains(HexCoord.FromOffset(0, 0), g.Session.SelectedTerritory!.Coords);
    }

    [Fact]
    public void NextTerritory_CyclesToNextInSortedOrder()
    {
        var g = new TwoRedTerritoriesGame();
        g.Hud.PressNextTerritory(); // selects first
        Territory first = g.Session.SelectedTerritory!;

        g.Hud.PressNextTerritory(); // advances to second

        Assert.NotNull(g.Session.SelectedTerritory);
        Assert.NotSame(first, g.Session.SelectedTerritory);
        Assert.Contains(HexCoord.FromOffset(5, 0), g.Session.SelectedTerritory!.Coords);
    }

    [Fact]
    public void NextTerritory_OnLastTerritory_WrapsToFirst()
    {
        var g = new TwoRedTerritoriesGame();
        g.Hud.PressNextTerritory(); // first
        g.Hud.PressNextTerritory(); // second
        g.Hud.PressNextTerritory(); // wraps back to first

        Assert.NotNull(g.Session.SelectedTerritory);
        Assert.Contains(HexCoord.FromOffset(0, 0), g.Session.SelectedTerritory!.Coords);
    }

    [Fact]
    public void NextTerritory_WithSingleTerritory_StaysOnIt()
    {
        var g = new TestGame(); // single Red 2-hex territory
        g.Hud.PressNextTerritory(); // selects it
        Territory first = g.Session.SelectedTerritory!;

        g.Hud.PressNextTerritory(); // wraps — same one

        Assert.Same(first, g.Session.SelectedTerritory);
    }

    [Fact]
    public void NextTerritory_LargerTerritoryVisitedFirst()
    {
        // Small territory (2 tiles, capital at (0,0)) and big territory
        // (3 tiles, capital at (5,0)). Sort is size-desc, so big is
        // selected first.
        var g = new UnequalRedTerritoriesGame();
        g.Hud.PressNextTerritory();
        Assert.NotNull(g.Session.SelectedTerritory);
        Assert.Contains(HexCoord.FromOffset(5, 0), g.Session.SelectedTerritory!.Coords);
    }

    [Fact]
    public void NextTerritory_CancelsPendingBuyMode()
    {
        // If the player is mid-buy, pressing Tab should cancel the
        // pending action so they're not stuck in BuyingRecruit mode
        // on a different territory.
        var g = new TwoRedTerritoriesGame();
        g.Hud.PressNextTerritory(); // select first Red territory
        HexCoord cap = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(cap, 20);
        g.Hud.ClickBuyRecruit();
        Assert.Equal(SessionState.ActionMode.BuyingRecruit, g.Session.Mode);

        g.Hud.PressNextTerritory(); // Tab

        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
    }

    [Fact]
    public void PreviousTerritory_NoneSelected_SelectsLexMaxCapital()
    {
        // Mirrors NextTerritory_NoneSelected: from no selection,
        // Shift+Tab should land on the LAST territory in lex-min-capital
        // order (the lex-max), so a single press lets the player jump
        // to the bottom of the cycle.
        var g = new TwoRedTerritoriesGame();
        Assert.Null(g.Session.SelectedTerritory);

        g.Hud.PressPreviousTerritory();

        Assert.NotNull(g.Session.SelectedTerritory);
        Assert.Contains(HexCoord.FromOffset(5, 0), g.Session.SelectedTerritory!.Coords);
    }

    [Fact]
    public void PreviousTerritory_CyclesToPrevInSortedOrder()
    {
        var g = new TwoRedTerritoriesGame();
        g.Hud.PressNextTerritory(); // first
        g.Hud.PressNextTerritory(); // second
        Territory second = g.Session.SelectedTerritory!;

        g.Hud.PressPreviousTerritory(); // back to first

        Assert.NotNull(g.Session.SelectedTerritory);
        Assert.NotSame(second, g.Session.SelectedTerritory);
        Assert.Contains(HexCoord.FromOffset(0, 0), g.Session.SelectedTerritory!.Coords);
    }

    [Fact]
    public void PreviousTerritory_OnFirstTerritory_WrapsToLast()
    {
        var g = new TwoRedTerritoriesGame();
        g.Hud.PressNextTerritory(); // first

        g.Hud.PressPreviousTerritory(); // wraps to last

        Assert.NotNull(g.Session.SelectedTerritory);
        Assert.Contains(HexCoord.FromOffset(5, 0), g.Session.SelectedTerritory!.Coords);
    }

    [Fact]
    public void PreviousTerritory_WithSingleTerritory_StaysOnIt()
    {
        var g = new TestGame();
        g.Hud.PressNextTerritory();
        Territory first = g.Session.SelectedTerritory!;

        g.Hud.PressPreviousTerritory();

        Assert.Same(first, g.Session.SelectedTerritory);
    }

    [Fact]
    public void PreviousTerritory_CancelsPendingBuyMode()
    {
        var g = new TwoRedTerritoriesGame();
        g.Hud.PressNextTerritory();
        HexCoord cap = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(cap, 20);
        g.Hud.ClickBuyRecruit();
        Assert.Equal(SessionState.ActionMode.BuyingRecruit, g.Session.Mode);

        g.Hud.PressPreviousTerritory();

        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
    }

    [Fact]
    public void NextTerritory_AfterWin_IsNoOp()
    {
        var g = new TwoRedTerritoriesGame();
        g.Session.Winner = g.Red.Id;

        g.Hud.PressNextTerritory();

        Assert.Null(g.Session.SelectedTerritory);
    }

    [Fact]
    public void NextTerritory_SkipsSingletons()
    {
        // Build a fixture where Red has a 2-hex territory and also a
        // lone singleton tile. Tab should only cycle the multi-hex one.
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        var players = new List<Player> { red, blue };

        var grid = TestHelpers.BuildRectGrid(6, 2, blue.Id);
        grid.Get(HexCoord.FromOffset(0, 0))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(1, 0))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(5, 1))!.Owner = red.Id; // singleton

        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var session = new SessionState();
        var map = new MockHexMapView();
        var hud = new MockHudView();
        var controller = new GameController(state, session, map, hud);
        controller.StartGame();

        hud.PressNextTerritory(); // first (and only) multi-hex
        Territory first = session.SelectedTerritory!;
        Assert.True(first.HasCapital);

        hud.PressNextTerritory(); // wraps — same one, not the singleton
        Assert.Same(first, session.SelectedTerritory);
    }

    [Fact]
    public void NextTerritory_CentersViewOnNewSelection()
    {
        // Pan position is view-state, not snapshotted, but the controller
        // is responsible for telling the view to recenter when the player
        // cycles selection — otherwise Tab on a large map sends focus to
        // a territory that may be off-screen.
        var g = new TwoRedTerritoriesGame();

        g.Hud.PressNextTerritory();

        Assert.Equal(1, g.Map.CenterCount);
        Assert.Same(g.Session.SelectedTerritory, g.Map.LastCenteredTerritory);
    }

    [Fact]
    public void PreviousTerritory_CentersViewOnNewSelection()
    {
        var g = new TwoRedTerritoriesGame();

        g.Hud.PressPreviousTerritory();

        Assert.Equal(1, g.Map.CenterCount);
        Assert.Same(g.Session.SelectedTerritory, g.Map.LastCenteredTerritory);
    }

    [Fact]
    public void NextTerritory_SkipsTerritoryWithNoUnmovedUnitsAndNoGold()
    {
        // A (cap (0,0)) and C (cap (10,0)) are actionable via their default
        // 10g of starting gold; B (cap (5,0)) has its gold zeroed and no
        // units, so it has nothing the player can do. Tab from A should
        // skip B and land directly on C.
        var g = new ThreeRedTerritoriesGame();
        Territory b = g.RedTerritoryAt(5, 0);
        Territory c = g.RedTerritoryAt(10, 0);
        g.State.Treasury.SetGold(b.Capital!.Value, 0);

        g.Hud.PressNextTerritory(); // → A
        g.Hud.PressNextTerritory(); // → should skip B → C

        Assert.Same(c, g.Session.SelectedTerritory);
    }

    [Fact]
    public void NextTerritory_StopsOnTerritoryWithUnmovedUnitEvenWithoutGold()
    {
        // B is broke but contains a fresh recruit — still actionable.
        var g = new ThreeRedTerritoriesGame();
        Territory b = g.RedTerritoryAt(5, 0);
        g.State.Treasury.SetGold(b.Capital!.Value, 0);
        g.State.Grid.Get(HexCoord.FromOffset(6, 0))!.Occupant =
            new Unit(g.Red.Id);

        g.Hud.PressNextTerritory(); // → A
        g.Hud.PressNextTerritory(); // → B (recruit makes it actionable)

        Assert.Same(b, g.Session.SelectedTerritory);
    }

    [Fact]
    public void NextTerritory_SkipsTerritoryWhereUnitsHaveAllMoved()
    {
        // B has no gold and a unit that already moved this turn — same
        // as having no movable units. Tab should skip B → C.
        var g = new ThreeRedTerritoriesGame();
        Territory b = g.RedTerritoryAt(5, 0);
        Territory c = g.RedTerritoryAt(10, 0);
        g.State.Treasury.SetGold(b.Capital!.Value, 0);
        g.State.Grid.Get(HexCoord.FromOffset(6, 0))!.Occupant =
            new Unit(g.Red.Id) { HasMovedThisTurn = true };

        g.Hud.PressNextTerritory(); // → A
        g.Hud.PressNextTerritory(); // → should skip B → C

        Assert.Same(c, g.Session.SelectedTerritory);
    }

    [Fact]
    public void NextTerritory_NoOpWhenNoTerritoryHasAction()
    {
        // Drain every Red capital and place no units → nothing actionable.
        // Tab should leave the selection alone (and not call CenterOnTerritory).
        var g = new ThreeRedTerritoriesGame();
        foreach (Territory t in g.State.Territories)
        {
            if (t.Owner == g.Red.Id)
            {
                g.State.Treasury.SetGold(t.Capital!.Value, 0);
            }
        }
        Assert.Null(g.Session.SelectedTerritory);
        int centerBaseline = g.Map.CenterCount;

        g.Hud.PressNextTerritory();

        Assert.Null(g.Session.SelectedTerritory);
        Assert.Equal(centerBaseline, g.Map.CenterCount);
    }

    [Fact]
    public void PreviousTerritory_SkipsTerritoryWithNoActionAvailable()
    {
        // Mirror of the forward skip test: Shift+Tab from C should skip B
        // (gold drained, no units) and land on A.
        var g = new ThreeRedTerritoriesGame();
        Territory a = g.RedTerritoryAt(0, 0);
        Territory b = g.RedTerritoryAt(5, 0);
        g.State.Treasury.SetGold(b.Capital!.Value, 0);

        g.Hud.PressPreviousTerritory(); // → C (lex max)
        g.Hud.PressPreviousTerritory(); // → should skip B → A

        Assert.Same(a, g.Session.SelectedTerritory);
    }

    [Fact]
    public void NextTerritory_OnlyActionableIsCurrentSelection_IsNoOp()
    {
        // A is the sole actionable territory. With it selected, Tab has
        // nowhere else to go — selection stays put and no extra center
        // call fires.
        var g = new ThreeRedTerritoriesGame();
        Territory b = g.RedTerritoryAt(5, 0);
        Territory c = g.RedTerritoryAt(10, 0);
        g.State.Treasury.SetGold(b.Capital!.Value, 0);
        g.State.Treasury.SetGold(c.Capital!.Value, 0);

        g.Hud.PressNextTerritory(); // → A (only actionable)
        Territory a = g.Session.SelectedTerritory!;
        int centerAfterFirst = g.Map.CenterCount;

        g.Hud.PressNextTerritory(); // no-op

        Assert.Same(a, g.Session.SelectedTerritory);
        Assert.Equal(centerAfterFirst, g.Map.CenterCount);
    }

    // --- Visited-territory tracking ---------------------------------------
    //
    // The size-desc sort re-runs on every press, so acting on a territory
    // (changing its size) reorders the walk and could revisit an already-
    // toured territory before untouched ones. SessionState tracks visited
    // capitals per turn; Tab prefers unvisited, and starts a fresh round
    // once every actionable territory has been toured.

    [Fact]
    public void NextTerritory_SizeChangeMidTurn_DoesNotRevisitBeforeUntouched()
    {
        // Equal-size territories A(0,0), B(5,0), C(10,0).
        // Tab→A, Tab→B, then grow B by capturing (4,0): B re-sorts to the
        // front, putting visited A next in walk order. Tab must still
        // reach untouched C, not revisit A.
        var g = new ThreeRedTerritoriesGame();
        g.State.Grid.Get(HexCoord.FromOffset(6, 0))!.Occupant = new Unit(g.Red.Id);

        g.Hud.PressNextTerritory(); // → A
        g.Hud.PressNextTerritory(); // → B
        Assert.Contains(HexCoord.FromOffset(5, 0), g.Session.SelectedTerritory!.Coords);

        g.Map.SimulateClick(g.State.Grid.Get(HexCoord.FromOffset(6, 0))!); // pick up unit
        Assert.Equal(SessionState.ActionMode.MovingUnit, g.Session.Mode);
        g.Map.SimulateClick(g.State.Grid.Get(HexCoord.FromOffset(4, 0))!); // capture → B has 3 tiles
        Assert.Contains(HexCoord.FromOffset(4, 0), g.Session.SelectedTerritory!.Coords);

        g.Hud.PressNextTerritory();

        Assert.Contains(HexCoord.FromOffset(10, 0), g.Session.SelectedTerritory!.Coords);
    }

    [Fact]
    public void NextTerritory_ClickedTerritoryCountsAsVisited()
    {
        // Clicking a territory marks it visited just like Tab does. Click
        // the big territory (which sorts first), deselect, then Tab: the
        // cycle must prefer the untouched small one over the visited big.
        var g = new UnequalRedTerritoriesGame();
        g.Map.SimulateClick(g.State.Grid.Get(HexCoord.FromOffset(5, 0))!); // big
        Assert.Contains(HexCoord.FromOffset(5, 0), g.Session.SelectedTerritory!.Coords);
        g.Map.SimulateClick(g.State.Grid.Get(HexCoord.FromOffset(3, 0))!); // Blue → deselect
        Assert.Null(g.Session.SelectedTerritory);

        g.Hud.PressNextTerritory();

        Assert.Contains(HexCoord.FromOffset(0, 0), g.Session.SelectedTerritory!.Coords);
    }

    [Fact]
    public void NextTerritory_AllVisited_StartsNewRound()
    {
        // Once every actionable territory has been toured, the next press
        // starts a fresh round: selection wraps and the visited set is
        // reset to contain only the new pick — so round 2 carries the
        // same no-revisit-before-untouched guarantee as round 1.
        var g = new ThreeRedTerritoriesGame();
        g.Hud.PressNextTerritory(); // → A
        g.Hud.PressNextTerritory(); // → B
        g.Hud.PressNextTerritory(); // → C: all three visited

        g.Hud.PressNextTerritory(); // exhausted → new round → A

        Assert.Contains(HexCoord.FromOffset(0, 0), g.Session.SelectedTerritory!.Coords);
        Assert.Equal(
            new HashSet<HexCoord> { g.Session.SelectedTerritory!.Capital!.Value },
            g.Session.VisitedTerritoryCapitals);
    }

    [Fact]
    public void PreviousTerritory_PrefersUnvisited()
    {
        // Backward mirror of the size-change repro: Shift+Tab→C, →B, grow
        // B to the front of the sort, Shift+Tab again — must reach
        // untouched A, not revisit C.
        var g = new ThreeRedTerritoriesGame();
        g.State.Grid.Get(HexCoord.FromOffset(6, 0))!.Occupant = new Unit(g.Red.Id);

        g.Hud.PressPreviousTerritory(); // → C
        g.Hud.PressPreviousTerritory(); // → B
        Assert.Contains(HexCoord.FromOffset(5, 0), g.Session.SelectedTerritory!.Coords);

        g.Map.SimulateClick(g.State.Grid.Get(HexCoord.FromOffset(6, 0))!);
        g.Map.SimulateClick(g.State.Grid.Get(HexCoord.FromOffset(4, 0))!); // capture → B has 3 tiles

        g.Hud.PressPreviousTerritory();

        Assert.Contains(HexCoord.FromOffset(0, 0), g.Session.SelectedTerritory!.Coords);
    }

    [Fact]
    public void NextTerritory_NoOpPress_PushesNoUndoEntry()
    {
        // The sole-actionable no-op must stay a true no-op: no selection
        // change, no visited-set churn, no undo entry.
        var g = new ThreeRedTerritoriesGame();
        g.State.Treasury.SetGold(g.RedTerritoryAt(5, 0).Capital!.Value, 0);
        g.State.Treasury.SetGold(g.RedTerritoryAt(10, 0).Capital!.Value, 0);
        g.Hud.PressNextTerritory(); // → A (only actionable)
        int depth = g.Session.Undo.UndoCount;

        g.Hud.PressNextTerritory(); // no-op

        Assert.Equal(depth, g.Session.Undo.UndoCount);
    }

    [Fact]
    public void UndoLast_RestoresDifferentSelection_CentersView()
    {
        // Tab twice (center 1 + 2), then undo-last reverts selection from
        // second→first. The view must follow the selection so the player
        // sees what they just rolled back to.
        var g = new TwoRedTerritoriesGame();
        g.Hud.PressNextTerritory(); // → first
        g.Hud.PressNextTerritory(); // → second
        Assert.Equal(2, g.Map.CenterCount);

        g.Hud.ClickUndoLast();

        Assert.Equal(3, g.Map.CenterCount);
        Assert.Same(g.Session.SelectedTerritory, g.Map.LastCenteredTerritory);
    }

    [Fact]
    public void RedoLast_RestoresDifferentSelection_CentersView()
    {
        var g = new TwoRedTerritoriesGame();
        g.Hud.PressNextTerritory(); // → first
        g.Hud.PressNextTerritory(); // → second
        g.Hud.ClickUndoLast();      // → first  (CenterCount=3)
        Assert.Equal(3, g.Map.CenterCount);

        g.Hud.ClickRedoLast();      // → second

        Assert.Equal(4, g.Map.CenterCount);
        Assert.Same(g.Session.SelectedTerritory, g.Map.LastCenteredTerritory);
    }

    [Fact]
    public void UndoLast_NoSelectionChange_DoesNotCenter()
    {
        // Undoing a non-selection change (e.g. exiting BuyingRecruit mode)
        // must not pan the view — pan is reserved for selection moves.
        var g = new TwoRedTerritoriesGame();
        g.Hud.PressNextTerritory();                                  // → first; center=1
        HexCoord cap = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(cap, 20);
        g.Hud.ClickBuyRecruit();                                     // mode=BuyingRecruit
        Assert.Equal(SessionState.ActionMode.BuyingRecruit, g.Session.Mode);
        int before = g.Map.CenterCount;

        g.Hud.ClickUndoLast();                                       // mode→None; selection unchanged

        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
        Assert.Equal(before, g.Map.CenterCount);
    }

    [Fact]
    public void UndoTurn_DoesNotCenter()
    {
        // Undo-all (whole-turn rewind) is explicitly excluded from the
        // recenter rule — the player asked for a global rewind, not a
        // guided tour of the selection chain.
        var g = new TwoRedTerritoriesGame();
        g.Hud.PressNextTerritory();
        g.Hud.PressNextTerritory();
        int before = g.Map.CenterCount;

        g.Hud.ClickUndoTurn();

        Assert.Equal(before, g.Map.CenterCount);
    }

    [Fact]
    public void RedoAll_DoesNotCenter()
    {
        // Mirror exception for redo-all: even though selection changes
        // from null to second, the view must not auto-pan.
        var g = new TwoRedTerritoriesGame();
        g.Hud.PressNextTerritory();
        g.Hud.PressNextTerritory();
        g.Hud.ClickUndoTurn();      // selection=null
        int before = g.Map.CenterCount;

        g.Hud.ClickRedoAll();       // selection=second again

        Assert.Equal(before, g.Map.CenterCount);
    }
}
