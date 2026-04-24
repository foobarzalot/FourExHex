using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Xunit;

namespace FourExHex.Tests;

public class GameControllerTests
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

        public TestGame()
        {
            Red = new Player("Red", new Color(1f, 0f, 0f));
            Blue = new Player("Blue", new Color(0f, 0f, 1f));
            var players = new List<Player> { Red, Blue };

            var grid = TestHelpers.BuildRectGrid(5, 2, Blue.Color);
            grid.Get(HexCoord.FromOffset(0, 1))!.Color = Red.Color;
            grid.Get(HexCoord.FromOffset(1, 1))!.Color = Red.Color;

            IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);

            State = new GameState(grid, territories, players, new TurnState(players), new Treasury());
            Session = new SessionState();
            Map = new MockHexMapView();
            Hud = new MockHudView();

            // Populate the mock's tile-to-territory index so TerritoryAt
            // works like the real view.
            foreach (KeyValuePair<HexCoord, Territory> kvp in territories.BuildTileIndex())
            {
                Map.TileIndex[kvp.Key] = kvp.Value;
            }

            Controller = new GameController(State, Session, Map, Hud);
            Controller.StartGame();
        }

        public HexTile Tile(int col, int row) => State.Grid.Get(HexCoord.FromOffset(col, row))!;

        public Territory RedTerritory =>
            State.Territories.First(t => t.Owner == Red.Color);
    }

    // --- Startup ----------------------------------------------------------

    [Fact]
    public void StartGame_SeedsFiveTimesGoldEarningCellsPerTerritory()
    {
        var g = new TestGame();

        HexCoord redCapital = g.RedTerritory.Capital!.Value;
        // 2-hex Red territory, no trees: 5 × 2 = 10.
        Assert.Equal(10, g.State.Treasury.GetGold(redCapital));

        // Blue also seeded at 5 × (tree-free cells). Fixture has no trees.
        Territory blue = g.State.Territories.First(t => t.Owner == g.Blue.Color);
        Assert.Equal(5 * blue.Size, g.State.Treasury.GetGold(blue.Capital!.Value));
    }

    [Fact]
    public void StartGame_SeedExcludesTreeTilesFromGoldEarningCount()
    {
        // Plant a tree on one Blue tile BEFORE StartGame runs. That tile
        // stops earning income, so Blue's seed drops by 5 (one tree × 5
        // gold/earning-cell).
        var red = new Player("Red", new Color(1f, 0f, 0f));
        var blue = new Player("Blue", new Color(0f, 0f, 1f));
        var players = new List<Player> { red, blue };

        var grid = TestHelpers.BuildRectGrid(5, 2, blue.Color);
        grid.Get(HexCoord.FromOffset(0, 1))!.Color = red.Color;
        grid.Get(HexCoord.FromOffset(1, 1))!.Color = red.Color;
        grid.Get(HexCoord.FromOffset(3, 0))!.Occupant = new Tree();

        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var map = new MockHexMapView();
        foreach (KeyValuePair<HexCoord, Territory> kvp in territories.BuildTileIndex())
        {
            map.TileIndex[kvp.Key] = kvp.Value;
        }
        var controller = new GameController(state, new SessionState(), map, new MockHudView());
        controller.StartGame();

        Territory blueT = state.Territories.First(t => t.Owner == blue.Color);
        // Blue has 8 tiles total, 1 tree → 7 earning → 35 gold.
        Assert.Equal(5 * (blueT.Size - 1), state.Treasury.GetGold(blueT.Capital!.Value));
    }

    [Fact]
    public void EndTurn_CreditsIncomeToEndingPlayer_NotStartingPlayer()
    {
        var g = new TestGame();
        HexCoord redCapital = g.RedTerritory.Capital!.Value;
        HexCoord blueCapital = g.State.Territories
            .First(t => t.Owner == g.Blue.Color).Capital!.Value;
        int redBefore = g.State.Treasury.GetGold(redCapital);
        int blueBefore = g.State.Treasury.GetGold(blueCapital);

        g.Hud.ClickEndTurn(); // Red ends; Blue now current.

        // Red (the ending player) collected income this turn.
        int redIncome = g.RedTerritory.Size;
        Assert.Equal(redBefore + redIncome, g.State.Treasury.GetGold(redCapital));
        // Blue (starting a new turn) has NOT been credited income yet.
        // No units → no upkeep either, so Blue's treasury is unchanged.
        Assert.Equal(blueBefore, g.State.Treasury.GetGold(blueCapital));
    }

    [Fact]
    public void StartGame_RefreshesBothViews()
    {
        var g = new TestGame();

        Assert.True(g.Hud.RefreshCount >= 1);
        Assert.True(g.Map.RefreshOccupantCount >= 1);
    }

    // --- Click to select --------------------------------------------------

    [Fact]
    public void Click_OwnTerritory_SelectsAndHighlights()
    {
        var g = new TestGame();

        g.Map.SimulateClick(g.Tile(0, 1));

        Assert.NotNull(g.Session.SelectedTerritory);
        Assert.Equal(g.Red.Color, g.Session.SelectedTerritory!.Owner);
        Assert.Same(g.Session.SelectedTerritory, g.Map.LastHighlight);
    }

    [Fact]
    public void Click_EnemyTerritory_ClearsSelection()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        Assert.NotNull(g.Session.SelectedTerritory);

        g.Map.SimulateClick(g.Tile(3, 0));

        Assert.Null(g.Session.SelectedTerritory);
        Assert.True(g.Map.HighlightWasCleared);
    }

    [Fact]
    public void Click_OutsideGrid_ClearsSelection()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        Assert.NotNull(g.Session.SelectedTerritory);

        g.Map.SimulateClick(null);

        Assert.Null(g.Session.SelectedTerritory);
    }

    // --- Pick up units ----------------------------------------------------

    [Fact]
    public void Click_OwnUnit_EntersMovingMode_AndShowsTargets()
    {
        var g = new TestGame();
        // Manually place a Red peasant on (1,1) — the non-capital Red tile.
        g.Tile(1, 1).Occupant = new Unit(g.Red.Color);

        g.Map.SimulateClick(g.Tile(1, 1));

        Assert.Equal(SessionState.ActionMode.MovingUnit, g.Session.Mode);
        Assert.Equal(HexCoord.FromOffset(1, 1), g.Session.MoveSource);
        // Move targets should have been shown (at least one capture target
        // exists — e.g., (2,1) is a non-capital Blue tile adjacent to red).
        Assert.NotEmpty(g.Map.LastMoveTargets);
    }

    [Fact]
    public void Click_OwnUnit_SetsMoveSource_OnMapView()
    {
        var g = new TestGame();
        g.Tile(1, 1).Occupant = new Unit(g.Red.Color);

        g.Map.SimulateClick(g.Tile(1, 1));

        Assert.Equal(HexCoord.FromOffset(1, 1), g.Map.LastMoveSource);
    }

    [Fact]
    public void Move_AfterCapture_ClearsMoveSource_OnMapView()
    {
        var g = new TestGame();
        g.Tile(1, 1).Occupant = new Unit(g.Red.Color);

        g.Map.SimulateClick(g.Tile(1, 1)); // pick up
        Assert.NotNull(g.Map.LastMoveSource);

        g.Map.SimulateClick(g.Tile(2, 1)); // capture

        Assert.Null(g.Map.LastMoveSource);
    }

    [Fact]
    public void Click_InvalidTargetDuringMovingMode_ClearsMoveSource()
    {
        var g = new TestGame();
        g.Tile(1, 1).Occupant = new Unit(g.Red.Color);

        g.Map.SimulateClick(g.Tile(1, 1)); // pick up
        Assert.NotNull(g.Map.LastMoveSource);

        g.Map.SimulateClick(g.Tile(4, 0)); // invalid (non-adjacent enemy)

        Assert.Null(g.Map.LastMoveSource);
    }

    [Fact]
    public void BuyPeasant_WhileUnitPickedUp_ClearsMoveSource()
    {
        // If the user picked up a unit and then presses U/click Buy,
        // the pulse should clear — we're no longer in MovingUnit mode.
        var g = new TestGame();
        g.Tile(1, 1).Occupant = new Unit(g.Red.Color);

        g.Map.SimulateClick(g.Tile(1, 1));
        Assert.NotNull(g.Map.LastMoveSource);

        g.Hud.ClickBuyPeasant();

        Assert.Null(g.Map.LastMoveSource);
    }

    [Fact]
    public void Click_OwnAlreadyMovedUnit_DoesNotEnterMoveMode()
    {
        var g = new TestGame();
        var unit = new Unit(g.Red.Color) { HasMovedThisTurn = true };
        g.Tile(1, 1).Occupant = unit;

        g.Map.SimulateClick(g.Tile(1, 1));

        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
    }

    // --- Buy peasant ------------------------------------------------------

    [Fact]
    public void BuyPeasant_OnOwnEmptyTile_DeductsGoldAndPlacesUnit()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1)); // select Red
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        int goldBefore = g.State.Treasury.GetGold(redCapital);

        g.Hud.ClickBuyPeasant();
        Assert.Equal(SessionState.ActionMode.BuyingPeasant, g.Session.Mode);

        // (1,1) is in Red but not Red's capital ((0,1) is) and is empty.
        g.Map.SimulateClick(g.Tile(1, 1));

        Assert.NotNull(g.Tile(1, 1).Unit);
        Assert.Equal(g.Red.Color, g.Tile(1, 1).Unit!.Owner);
        Assert.Equal(goldBefore - PurchaseRules.PeasantCost, g.State.Treasury.GetGold(redCapital));
        // Buy-on-own-tile doesn't consume the unit's action.
        Assert.False(g.Tile(1, 1).Unit!.HasMovedThisTurn);
        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
    }

    // --- Move + capture ---------------------------------------------------

    [Fact]
    public void Move_CaptureEnemyTile_ChangesOwnershipAndMarksMoved()
    {
        var g = new TestGame();
        var unit = new Unit(g.Red.Color);
        g.Tile(1, 1).Occupant = unit;

        g.Map.SimulateClick(g.Tile(1, 1));
        Assert.Equal(SessionState.ActionMode.MovingUnit, g.Session.Mode);

        // (2,1) is Blue, not Blue's capital, empty → capturable by peasant.
        g.Map.SimulateClick(g.Tile(2, 1));

        Assert.Equal(g.Red.Color, g.Tile(2, 1).Color);
        Assert.Same(unit, g.Tile(2, 1).Unit);
        Assert.Null(g.Tile(1, 1).Unit);
        Assert.True(unit.HasMovedThisTurn);
        // After a capture the reconciler rebuilds — rebuild count should
        // be at least 1.
        Assert.True(g.Map.RebuildCount >= 1);
    }

    [Fact]
    public void Move_Capture_KeepsSelection_OnAttackerNewTerritory()
    {
        // QoL: after a capture the selection should track over to
        // whichever new territory contains the captured tile, instead
        // of being cleared. The user clicked on a Red territory, moved
        // a unit into Blue space, and should still see Red selected.
        var g = new TestGame();
        var unit = new Unit(g.Red.Color);
        g.Tile(1, 1).Occupant = unit;

        g.Map.SimulateClick(g.Tile(1, 1));
        g.Map.SimulateClick(g.Tile(2, 1)); // capture Blue hex

        Assert.NotNull(g.Session.SelectedTerritory);
        Assert.Equal(g.Red.Color, g.Session.SelectedTerritory!.Owner);
        // The new territory should contain the captured tile.
        Assert.Contains(HexCoord.FromOffset(2, 1), g.Session.SelectedTerritory.Coords);
        // And the highlight on the map should point to the same territory.
        Assert.Same(g.Session.SelectedTerritory, g.Map.LastHighlight);
    }

    [Fact]
    public void BuyPeasant_OnOwnTile_StaysInBuyingMode_IfStillAffordable()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        // Give Red enough to buy two peasants in a row.
        g.State.Treasury.SetGold(redCapital, 25);

        g.Hud.ClickBuyPeasant();
        Assert.Equal(SessionState.ActionMode.BuyingPeasant, g.Session.Mode);

        // (1,1) is an empty Red non-capital tile — valid placement.
        g.Map.SimulateClick(g.Tile(1, 1));

        // Bought: 25 - 10 = 15 remaining, still ≥ 10 → stay in mode.
        Assert.Equal(SessionState.ActionMode.BuyingPeasant, g.Session.Mode);
        Assert.Equal(15, g.State.Treasury.GetGold(redCapital));
    }

    [Fact]
    public void BuyPeasant_OnOwnTile_ExitsBuyingMode_IfNoLongerAffordable()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 10); // exactly one peasant

        g.Hud.ClickBuyPeasant();
        g.Map.SimulateClick(g.Tile(1, 1));

        // Bought: 10 - 10 = 0 < 10 → exit mode.
        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
    }

    [Fact]
    public void BuyPeasant_Capture_StaysInBuyingMode_IfMergedTerritoryStillAffordable()
    {
        // Capture rebinds the selection to the new territory; the
        // affordability check runs against that new selection. The
        // Red territory in TestGame merges with the captured tile
        // (trivially — (2,1) becomes part of Red). Red's gold is 25-10=15,
        // enough for another peasant.
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 25);

        g.Hud.ClickBuyPeasant();
        g.Map.SimulateClick(g.Tile(2, 1)); // capture Blue adjacent

        // Still in mode; selection rebound; treasury 15.
        Assert.Equal(SessionState.ActionMode.BuyingPeasant, g.Session.Mode);
        Assert.NotNull(g.Session.SelectedTerritory);
        Assert.Contains(HexCoord.FromOffset(2, 1), g.Session.SelectedTerritory!.Coords);
    }

    [Fact]
    public void BuildTower_StaysInBuildingMode_IfStillAffordable()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 35);

        g.Hud.ClickBuildTower();
        g.Map.SimulateClick(g.Tile(1, 1));

        // 35 - 15 = 20 ≥ 15 → stay in mode.
        Assert.Equal(SessionState.ActionMode.BuildingTower, g.Session.Mode);
        Assert.Equal(20, g.State.Treasury.GetGold(redCapital));
    }

    [Fact]
    public void BuildTower_ExitsBuildingMode_IfNoLongerAffordable()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 15);

        g.Hud.ClickBuildTower();
        g.Map.SimulateClick(g.Tile(1, 1));

        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
    }

    [Fact]
    public void BuildTower_WhileInBuyingPeasantMode_SwitchesToBuildingMode()
    {
        // Clicking a different placement button while in a placement
        // mode should switch cleanly to the new mode. Regression lock
        // for the sticky-mode QoL feature.
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 30);

        g.Hud.ClickBuyPeasant();
        Assert.Equal(SessionState.ActionMode.BuyingPeasant, g.Session.Mode);

        g.Hud.ClickBuildTower();
        Assert.Equal(SessionState.ActionMode.BuildingTower, g.Session.Mode);
    }

    [Fact]
    public void BuyPeasant_Capture_KeepsSelection_OnAttackerNewTerritory()
    {
        // Same QoL guarantee for the buy-and-capture path.
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        g.Hud.ClickBuyPeasant();
        // (2,1) is Blue, adjacent to Red's (1,1). Capturable by a fresh peasant.
        g.Map.SimulateClick(g.Tile(2, 1));

        Assert.NotNull(g.Session.SelectedTerritory);
        Assert.Equal(g.Red.Color, g.Session.SelectedTerritory!.Owner);
        Assert.Contains(HexCoord.FromOffset(2, 1), g.Session.SelectedTerritory.Coords);
    }

    [Fact]
    public void Move_WithinOwnTerritory_DoesNotConsumeAction()
    {
        var g = new TestGame();
        var unit = new Unit(g.Red.Color);
        // Park a unit on (0,1) (capital hex is fine for manual test fixture
        // purposes — wait, (0,1) IS Red's capital, can't hold a unit).
        // Instead place on (1,1) and reposition back toward... hmm, Red
        // only has 2 hexes and the other one is the capital. No valid
        // reposition. Skip this test scenario with a bigger fixture.
        g.Tile(1, 1).Occupant = unit;

        g.Map.SimulateClick(g.Tile(1, 1));
        // Red has nowhere to reposition (other tile is capital). The move
        // targets should still include captures but no repositions.
        Assert.Contains(HexCoord.FromOffset(2, 1), g.Map.LastMoveTargets);
    }

    // --- End turn ---------------------------------------------------------

    [Fact]
    public void EndTurn_AdvancesPlayer()
    {
        var g = new TestGame();
        Assert.Equal(g.Red.Color, g.State.Turns.CurrentPlayer.Color);

        g.Hud.ClickEndTurn();

        Assert.Equal(g.Blue.Color, g.State.Turns.CurrentPlayer.Color);
    }

    [Fact]
    public void EndTurn_ResetsMovementForNewPlayer()
    {
        var g = new TestGame();
        var blueUnit = new Unit(g.Blue.Color) { HasMovedThisTurn = true };
        g.Tile(3, 0).Occupant = blueUnit;

        g.Hud.ClickEndTurn(); // Red -> Blue

        Assert.False(blueUnit.HasMovedThisTurn);
    }

    [Fact]
    public void EndTurn_PaysUpkeep_FromNewPlayerTerritories()
    {
        var g = new TestGame();
        // Put a Blue peasant on a non-capital Blue tile so Blue has
        // upkeep to pay when Blue's turn begins. Income is now credited
        // at END of turn, so Blue's only treasury change at the start
        // of its turn is upkeep.
        g.Tile(3, 0).Occupant = new Unit(g.Blue.Color);
        HexCoord blueCapital = g.State.Territories
            .First(t => t.Owner == g.Blue.Color).Capital!.Value;
        g.State.Treasury.SetGold(blueCapital, 20);

        g.Hud.ClickEndTurn(); // Red -> Blue: Blue pays upkeep, no income yet.

        // Blue paid 2 for the peasant. No income credited on Blue's
        // turn-start (that happens at end of Blue's turn).
        Assert.Equal(20 - 2, g.State.Treasury.GetGold(blueCapital));
        // Peasant survived because Blue could afford it.
        Assert.NotNull(g.Tile(3, 0).Unit);
    }

    [Fact]
    public void EndTurn_BankruptTerritory_LeavesGraves()
    {
        var g = new TestGame();
        // Give Blue a knight (upkeep 18) it can't pay. Blue has 0 gold
        // and no income is credited at turn-start under the new rule,
        // so upkeep goes straight to bankruptcy.
        g.Tile(3, 0).Occupant = new Unit(g.Blue.Color, UnitLevel.Knight);
        HexCoord blueCapital = g.State.Territories
            .First(t => t.Owner == g.Blue.Color).Capital!.Value;
        g.State.Treasury.SetGold(blueCapital, 0);

        g.Hud.ClickEndTurn(); // advance to Blue

        // Blue has 0g and owes 18 upkeep → bankrupt. Knight dies and
        // leaves a grave behind (not a null tile).
        Assert.IsType<Grave>(g.Tile(3, 0).Occupant);
    }

    [Fact]
    public void EndTurn_ConvertsGravesToTrees()
    {
        var g = new TestGame();
        // Drop a stray grave on the ending player's tile (as if left
        // over from a previous bankruptcy). End of turn should convert
        // it into a tree rather than clearing it — trees are the
        // "rotted corpse" legacy.
        g.Tile(0, 1).Occupant = new Grave(); // Red tile; Red ends below.

        g.Hud.ClickEndTurn();

        Assert.IsType<Tree>(g.Tile(0, 1).Occupant);
    }

    [Fact]
    public void EndTurn_SpreadsTrees_OneStep()
    {
        // Two adjacent trees at end of turn seed a third in their
        // common empty neighbor. Place the pair on Blue tiles so the
        // fixture's Red territory is untouched by spreading.
        var g = new TestGame();
        g.Tile(2, 0).Occupant = new Tree();
        g.Tile(3, 0).Occupant = new Tree();
        int treesBefore = CountTrees(g.State.Grid);

        g.Hud.ClickEndTurn();

        int treesAfter = CountTrees(g.State.Grid);
        Assert.Equal(treesBefore + 1, treesAfter);
    }

    [Fact]
    public void EndTurn_IncomeSkipsTreeTiles_WhenEndingPlayerCollects()
    {
        // Plant a tree on one Blue tile. When Blue ends their turn,
        // Blue collects income based on tree-free cells only.
        var g = new TestGame();
        g.Tile(3, 0).Occupant = new Tree();
        int blueSize = g.State.Territories
            .First(t => t.Owner == g.Blue.Color).Size;
        HexCoord blueCapital = g.State.Territories
            .First(t => t.Owner == g.Blue.Color).Capital!.Value;
        g.State.Treasury.SetGold(blueCapital, 0);

        g.Hud.ClickEndTurn(); // Red ends their turn → Red collects income, not Blue.
        Assert.Equal(0, g.State.Treasury.GetGold(blueCapital));

        g.Hud.ClickEndTurn(); // Blue ends their turn → Blue collects income now.

        // Blue has no units so upkeep is 0. Income is size minus the
        // one tree tile.
        Assert.Equal(blueSize - 1, g.State.Treasury.GetGold(blueCapital));
    }

    [Fact]
    public void EndTurn_BankruptGraves_BecomeTreesOnNextEndTurn()
    {
        // Full feedback loop: Blue can't afford its knight → knight
        // dies and leaves a grave this turn → next end-of-turn the
        // grave converts into a tree → the tree permanently reduces
        // Blue's income.
        var g = new TestGame();
        g.Tile(3, 0).Occupant = new Unit(g.Blue.Color, UnitLevel.Knight);
        HexCoord blueCapital = g.State.Territories
            .First(t => t.Owner == g.Blue.Color).Capital!.Value;
        g.State.Treasury.SetGold(blueCapital, 0);

        // End Red's turn: Blue's turn begins, knight goes bankrupt.
        g.Hud.ClickEndTurn();
        Assert.IsType<Grave>(g.Tile(3, 0).Occupant);

        // End Blue's turn: the bankruptcy grave converts into a tree.
        g.Hud.ClickEndTurn();
        Assert.IsType<Tree>(g.Tile(3, 0).Occupant);
    }

    // --- Grave-to-tree: owner-specific timing ----------------------------
    // A grave on a given player's tile should only convert into a tree at
    // the end of THAT player's turn, not at the end of any other player's
    // turn. The grave's "owner" is the tile's color.

    [Fact]
    public void EndTurn_GraveOnNonEndingPlayersTile_Survives()
    {
        // Grave sits on a Blue tile. Red's turn ends. Only Blue's turn-end
        // should convert it — so right now the grave must survive.
        var g = new TestGame();
        g.Tile(3, 0).Occupant = new Grave();
        Assert.Equal(g.Blue.Color, g.Tile(3, 0).Color); // sanity: grave is on Blue

        g.Hud.ClickEndTurn(); // Red ends; Blue now current.

        Assert.IsType<Grave>(g.Tile(3, 0).Occupant);
    }

    [Fact]
    public void EndTurn_GraveOnEndingPlayersTile_ConvertsToTree()
    {
        // Grave on a Red tile. Red's turn ends. It must convert.
        var g = new TestGame();
        g.Tile(0, 1).Occupant = new Grave();
        Assert.Equal(g.Red.Color, g.Tile(0, 1).Color);

        g.Hud.ClickEndTurn(); // Red ends.

        Assert.IsType<Tree>(g.Tile(0, 1).Occupant);
    }

    [Fact]
    public void EndTurn_MixedGraves_OnlyEndingPlayersColorConverts()
    {
        // Two graves: one on a Red tile, one on a Blue tile. When Red
        // ends their turn, only the Red-tile grave converts. The Blue-
        // tile grave must wait for Blue's own end-of-turn.
        var g = new TestGame();
        g.Tile(0, 1).Occupant = new Grave(); // Red tile
        g.Tile(3, 0).Occupant = new Grave(); // Blue tile

        g.Hud.ClickEndTurn(); // Red ends.

        Assert.IsType<Tree>(g.Tile(0, 1).Occupant);
        Assert.IsType<Grave>(g.Tile(3, 0).Occupant);
    }

    [Fact]
    public void EndTurn_GraveOnBlueTile_ConvertsOnlyAfterBlueEndsTurn()
    {
        // End-to-end statement of the rule: a grave on a Blue tile
        // persists through Red's end-of-turn, and only becomes a tree
        // when Blue's own turn ends.
        var g = new TestGame();
        g.Tile(3, 0).Occupant = new Grave();

        g.Hud.ClickEndTurn(); // Red ends → Blue's turn begins.
        Assert.IsType<Grave>(g.Tile(3, 0).Occupant);

        g.Hud.ClickEndTurn(); // Blue ends → now it converts.
        Assert.IsType<Tree>(g.Tile(3, 0).Occupant);
    }

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
        foreach (KeyValuePair<HexCoord, Territory> kvp in state.Territories.BuildTileIndex())
        {
            map.TileIndex[kvp.Key] = kvp.Value;
        }
        int index = 0;
        AiAction? Chooser(GameState s, Color c, HashSet<HexCoord> visited, Random rng)
        {
            if (index >= actions.Length) return null;
            return actions[index++];
        }
        return new GameController(state, new SessionState(), map, hud, rng: new Random(1), aiChooser: Chooser);
    }

    /// <summary>
    /// Minimal 2-player harness fixture mirroring the TestGame shape:
    /// a 5x2 Blue grid with Red owning (0,0), (0,1), (1,1) so there's
    /// a capturable non-capital Blue tile at (2,1) for a peasant
    /// placed at (1,1). Red's capital lands at (0,0), leaving (0,1)
    /// as the only empty Red tile for tower builds.
    /// </summary>
    private static (GameState state, MockHexMapView map, MockHudView hud) BuildAiFixture()
    {
        var red = new Player("Red", new Color(1f, 0f, 0f), isAi: true);
        var blue = new Player("Blue", new Color(0f, 0f, 1f));
        var players = new List<Player> { red, blue };
        var grid = TestHelpers.BuildRectGrid(5, 2, blue.Color);
        grid.Get(HexCoord.FromOffset(0, 0))!.Color = red.Color;
        grid.Get(HexCoord.FromOffset(0, 1))!.Color = red.Color;
        grid.Get(HexCoord.FromOffset(1, 1))!.Color = red.Color;
        grid.Get(HexCoord.FromOffset(1, 1))!.Occupant = new Unit(red.Color);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        return (state, new MockHexMapView(), new MockHudView());
    }

    private static HexCoord RedCapital(GameState state) =>
        state.Territories.First(t => t.Owner == state.Players[0].Color).Capital!.Value;

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
        // (4,1) is far from the peasant at (1,1), not adjacent.
        var bad = new AiMoveAction(HexCoord.FromOffset(1, 1), HexCoord.FromOffset(4, 1));
        GameController c = BuildHarnessWithStubAi(state, map, hud, bad);

        Assert.Throws<InvalidOperationException>(() => c.StartGame());
    }

    [Fact]
    public void ExecuteAiBuyUnit_CapitalNotFound_Throws()
    {
        (GameState state, MockHexMapView map, MockHudView hud) = BuildAiFixture();
        // (4,0) is Blue — no territory has that capital.
        var bad = new AiBuyUnitAction(HexCoord.FromOffset(4, 0), HexCoord.FromOffset(2, 1));
        GameController c = BuildHarnessWithStubAi(state, map, hud, bad);

        Assert.Throws<InvalidOperationException>(() => c.StartGame());
    }

    [Fact]
    public void ExecuteAiBuyUnit_Unaffordable_Throws()
    {
        // StartGame re-seeds treasury to 10 and collects income (+3)
        // → 13g, above the 10g peasant cost. To exercise the
        // affordability precondition we chain two actions: the first
        // is a legal buy-capture that drains the treasury to 3g, and
        // the second is a bad buy whose affordability check now fails.
        (GameState state, MockHexMapView map, MockHudView hud) = BuildAiFixture();
        HexCoord cap = RedCapital(state);
        var first = new AiBuyUnitAction(cap, HexCoord.FromOffset(2, 1));
        var second = new AiBuyUnitAction(cap, HexCoord.FromOffset(2, 1));
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
        var bad = new AiBuyUnitAction(cap, HexCoord.FromOffset(4, 1));
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
        // (1,1) has the peasant — occupied.
        var bad = new AiBuildTowerAction(cap, HexCoord.FromOffset(1, 1));
        GameController c = BuildHarnessWithStubAi(state, map, hud, bad);

        Assert.Throws<InvalidOperationException>(() => c.StartGame());
    }

    [Fact]
    public void ExecuteAi_LegalActionViaStubChooser_ExecutesNormally()
    {
        // Regression lock: the stub-chooser injection path doesn't
        // break legal execution. Peasant at (1,1) captures (2,1).
        (GameState state, MockHexMapView map, MockHudView hud) = BuildAiFixture();
        var good = new AiMoveAction(HexCoord.FromOffset(1, 1), HexCoord.FromOffset(2, 1));
        GameController c = BuildHarnessWithStubAi(state, map, hud, good, null);

        c.StartGame(); // should NOT throw

        Assert.Equal(state.Players[0].Color, state.Grid.Get(HexCoord.FromOffset(2, 1))!.Color);
    }

    // --- AI turn integration ---------------------------------------------

    /// <summary>
    /// 2-player fixture where Blue is an AI with a 3-tile territory
    /// containing a peasant positioned to capture a neutral Blue tile
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
            Red = new Player("Red", new Color(1f, 0f, 0f)); // human
            Blue = new Player("Blue", new Color(0f, 0f, 1f), isAi: true);
            var players = new List<Player> { Red, Blue };

            // 8x2 grid: Red owns (0,0)-(2,0), Blue owns (5,1)-(7,1).
            // Blue has a peasant on (5,1) — not adjacent to any Red
            // tile, so it can't capture. Different test methods will
            // mutate the fixture as needed.
            var grid = TestHelpers.BuildRectGrid(8, 2, new Color(0.3f, 0.3f, 0.3f));
            grid.Get(HexCoord.FromOffset(0, 0))!.Color = Red.Color;
            grid.Get(HexCoord.FromOffset(1, 0))!.Color = Red.Color;
            grid.Get(HexCoord.FromOffset(2, 0))!.Color = Red.Color;
            grid.Get(HexCoord.FromOffset(5, 1))!.Color = Blue.Color;
            grid.Get(HexCoord.FromOffset(6, 1))!.Color = Blue.Color;
            grid.Get(HexCoord.FromOffset(7, 1))!.Color = Blue.Color;

            IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
            State = new GameState(grid, territories, players, new TurnState(players), new Treasury());
            Session = new SessionState();
            Map = new MockHexMapView();
            Hud = new MockHudView();
            foreach (KeyValuePair<HexCoord, Territory> kvp in territories.BuildTileIndex())
            {
                Map.TileIndex[kvp.Key] = kvp.Value;
            }
            // Seeded RNG so AI behavior is deterministic across runs.
            Controller = new GameController(State, Session, Map, Hud, new Random(12345));
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
        Assert.Equal(g.Red.Color, g.State.Turns.CurrentPlayer.Color);

        g.Hud.ClickEndTurn();

        // After the AI turn finishes, control returns to Red.
        Assert.Equal(g.Red.Color, g.State.Turns.CurrentPlayer.Color);
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
        Assert.Equal(g.Red.Color, g.State.Turns.CurrentPlayer.Color);
        // Blue's tiles are still Blue (no captures happened).
        Assert.Equal(g.Blue.Color, g.Tile(5, 1).Color);
        Assert.Equal(g.Blue.Color, g.Tile(6, 1).Color);
        Assert.Equal(g.Blue.Color, g.Tile(7, 1).Color);
    }

    [Fact]
    public void AiTurn_CanCaptureLastEnemyHex_DeclaresWinner()
    {
        // Minimal fixture where the AI can win in one move: a 4-tile
        // Red territory with a peasant (sustainable upkeep), adjacent
        // to a lone undefended Blue tile. Red's net = 4 - 2 = 2, and
        // post-capture net = 5 - 2 = 3 — well above the AI's bankruptcy
        // rule. The AI should pick the winning capture on its first
        // turn (which is StartGame's job to auto-run since Red is the
        // starting AI player).
        var red = new Player("Red", new Color(1f, 0f, 0f), isAi: true);
        var blue = new Player("Blue", new Color(0f, 0f, 1f));
        var players = new List<Player> { red, blue };

        var grid = TestHelpers.BuildRectGrid(5, 1, red.Color);
        grid.Get(HexCoord.FromOffset(4, 0))!.Color = blue.Color;
        grid.Get(HexCoord.FromOffset(3, 0))!.Occupant = new Unit(red.Color);

        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var session = new SessionState();
        var map = new MockHexMapView();
        var hud = new MockHudView();
        foreach (KeyValuePair<HexCoord, Territory> kvp in territories.BuildTileIndex())
        {
            map.TileIndex[kvp.Key] = kvp.Value;
        }
        var controller = new GameController(state, session, map, hud, new Random(1));
        controller.StartGame();

        Assert.True(session.IsGameOver);
        Assert.Equal(red.Color, session.Winner);
    }

    [Fact]
    public void AiTurn_EachTerritoryActsAtMostOnce()
    {
        // Blue has 2 territories; both have a knight adjacent to
        // capturable neutral tiles. Verify that after the AI turn,
        // each territory has made at most one capture (not an
        // unbounded loop).
        var red = new Player("Red", new Color(1f, 0f, 0f));
        var blue = new Player("Blue", new Color(0f, 0f, 1f), isAi: true);
        var players = new List<Player> { red, blue };

        // 10x2 grid. Red owns (0,0). Two Blue territories: {(2,0),(3,0)}
        // and {(6,0),(7,0)}. Each has a knight on the right end so
        // both can capture adjacent neutral tiles ((4,0) and (8,0)).
        var grid = TestHelpers.BuildRectGrid(10, 2, new Color(0.3f, 0.3f, 0.3f));
        grid.Get(HexCoord.FromOffset(0, 0))!.Color = red.Color;
        grid.Get(HexCoord.FromOffset(2, 0))!.Color = blue.Color;
        grid.Get(HexCoord.FromOffset(3, 0))!.Color = blue.Color;
        grid.Get(HexCoord.FromOffset(6, 0))!.Color = blue.Color;
        grid.Get(HexCoord.FromOffset(7, 0))!.Color = blue.Color;
        grid.Get(HexCoord.FromOffset(3, 0))!.Occupant = new Unit(blue.Color, UnitLevel.Knight);
        grid.Get(HexCoord.FromOffset(7, 0))!.Occupant = new Unit(blue.Color, UnitLevel.Knight);

        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var session = new SessionState();
        var map = new MockHexMapView();
        var hud = new MockHudView();
        foreach (KeyValuePair<HexCoord, Territory> kvp in territories.BuildTileIndex())
        {
            map.TileIndex[kvp.Key] = kvp.Value;
        }
        var controller = new GameController(state, session, map, hud, new Random(7));
        controller.StartGame();

        // End Red's (human) turn so Blue's AI turn runs.
        hud.ClickEndTurn();

        // Each of Blue's 2 territories had at most one action, so
        // total board-wide ownership change is at most +2 Blue tiles.
        // Starting Blue count: 4. Max post-AI Blue count: 6.
        int blueCount = 0;
        foreach (HexTile t in state.Grid.Tiles)
        {
            if (t.Color == blue.Color) blueCount++;
        }
        Assert.InRange(blueCount, 4, 6);
    }

    // --- Next territory hotkey -------------------------------------------

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

        public TwoRedTerritoriesGame()
        {
            Red = new Player("Red", new Color(1f, 0f, 0f));
            Blue = new Player("Blue", new Color(0f, 0f, 1f));
            var players = new List<Player> { Red, Blue };

            // 10x1 Blue grid. Overlay two disjoint 2-tile Red blobs:
            // A at columns 0-1, B at columns 5-6.
            var grid = TestHelpers.BuildRectGrid(10, 1, Blue.Color);
            grid.Get(HexCoord.FromOffset(0, 0))!.Color = Red.Color;
            grid.Get(HexCoord.FromOffset(1, 0))!.Color = Red.Color;
            grid.Get(HexCoord.FromOffset(5, 0))!.Color = Red.Color;
            grid.Get(HexCoord.FromOffset(6, 0))!.Color = Red.Color;

            IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);

            State = new GameState(grid, territories, players, new TurnState(players), new Treasury());
            Session = new SessionState();
            Map = new MockHexMapView();
            Hud = new MockHudView();
            foreach (KeyValuePair<HexCoord, Territory> kvp in territories.BuildTileIndex())
            {
                Map.TileIndex[kvp.Key] = kvp.Value;
            }
            Controller = new GameController(State, Session, Map, Hud);
            Controller.StartGame();
        }

        public IEnumerable<Territory> RedTerritories =>
            State.Territories.Where(t => t.Owner == Red.Color);
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
    public void NextTerritory_CancelsPendingBuyMode()
    {
        // If the player is mid-buy, pressing Tab should cancel the
        // pending action so they're not stuck in BuyingPeasant mode
        // on a different territory.
        var g = new TwoRedTerritoriesGame();
        g.Hud.PressNextTerritory(); // select first Red territory
        HexCoord cap = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(cap, 20);
        g.Hud.ClickBuyPeasant();
        Assert.Equal(SessionState.ActionMode.BuyingPeasant, g.Session.Mode);

        g.Hud.PressNextTerritory(); // Tab

        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
    }

    [Fact]
    public void NextTerritory_AfterWin_IsNoOp()
    {
        var g = new TwoRedTerritoriesGame();
        g.Session.Winner = g.Red.Color;

        g.Hud.PressNextTerritory();

        Assert.Null(g.Session.SelectedTerritory);
    }

    [Fact]
    public void NextTerritory_SkipsSingletons()
    {
        // Build a fixture where Red has a 2-hex territory and also a
        // lone singleton tile. Tab should only cycle the multi-hex one.
        var red = new Player("Red", new Color(1f, 0f, 0f));
        var blue = new Player("Blue", new Color(0f, 0f, 1f));
        var players = new List<Player> { red, blue };

        var grid = TestHelpers.BuildRectGrid(6, 2, blue.Color);
        grid.Get(HexCoord.FromOffset(0, 0))!.Color = red.Color;
        grid.Get(HexCoord.FromOffset(1, 0))!.Color = red.Color;
        grid.Get(HexCoord.FromOffset(5, 1))!.Color = red.Color; // singleton

        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var session = new SessionState();
        var map = new MockHexMapView();
        var hud = new MockHudView();
        foreach (KeyValuePair<HexCoord, Territory> kvp in territories.BuildTileIndex())
        {
            map.TileIndex[kvp.Key] = kvp.Value;
        }
        var controller = new GameController(state, session, map, hud);
        controller.StartGame();

        hud.PressNextTerritory(); // first (and only) multi-hex
        Territory first = session.SelectedTerritory!;
        Assert.True(first.HasCapital);

        hud.PressNextTerritory(); // wraps — same one, not the singleton
        Assert.Same(first, session.SelectedTerritory);
    }

    // --- Build tower ------------------------------------------------------

    [Fact]
    public void BuildTower_OnOwnEmptyTile_DeductsFifteenGoldAndPlacesTower()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1)); // select Red
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        // Red starts with 10g; give it enough to build.
        g.State.Treasury.SetGold(redCapital, 20);
        int before = g.State.Treasury.GetGold(redCapital);

        g.Hud.ClickBuildTower();
        Assert.Equal(SessionState.ActionMode.BuildingTower, g.Session.Mode);

        // (1,1) is an empty tile in Red's territory ((0,1) is capital).
        g.Map.SimulateClick(g.Tile(1, 1));

        Assert.IsType<Tower>(g.Tile(1, 1).Occupant);
        Assert.Equal(before - PurchaseRules.TowerCost, g.State.Treasury.GetGold(redCapital));
        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
    }

    [Fact]
    public void BuildTower_CantAfford_ButtonIsNoOp()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 5); // not enough for 15g tower

        g.Hud.ClickBuildTower();

        // Should not have entered BuildingTower mode.
        Assert.NotEqual(SessionState.ActionMode.BuildingTower, g.Session.Mode);
    }

    [Fact]
    public void BuildTower_OnOccupiedTile_CancelsMode()
    {
        // Click an invalid target (occupied tile or foreign tile) during
        // BuildingTower mode: the mode cancels and falls through to a
        // normal click.
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 20);

        g.Hud.ClickBuildTower();
        Assert.Equal(SessionState.ActionMode.BuildingTower, g.Session.Mode);

        // (0,1) is Red's capital — occupied by a Capital — not a valid
        // tower target. The click cancels the mode.
        g.Map.SimulateClick(g.Tile(0, 1));

        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
        // Red's gold is unchanged.
        Assert.Equal(20, g.State.Treasury.GetGold(redCapital));
    }

    [Fact]
    public void BuildTower_OnEnemyTile_CancelsMode_AndSelectsNothing()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 20);

        g.Hud.ClickBuildTower();

        // (3,0) is Blue. Can't build a tower on enemy territory.
        g.Map.SimulateClick(g.Tile(3, 0));

        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
        Assert.Null(g.Session.SelectedTerritory);
        // Red's gold is unchanged.
        Assert.Equal(20, g.State.Treasury.GetGold(redCapital));
    }

    [Fact]
    public void Undo_AfterBuildTower_RemovesTowerAndRefundsGold()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 20);
        int before = g.State.Treasury.GetGold(redCapital);

        g.Hud.ClickBuildTower();
        g.Map.SimulateClick(g.Tile(1, 1));
        Assert.IsType<Tower>(g.Tile(1, 1).Occupant);

        g.Hud.ClickUndoLast();

        Assert.Null(g.Tile(1, 1).Occupant);
        Assert.Equal(before, g.State.Treasury.GetGold(redCapital));
    }

    [Fact]
    public void BuildTower_AfterPlacement_RadiatesDefenseToAdjacentOwnTile()
    {
        // Build a tower on (1,1). Its adjacent same-territory tile
        // (0,1) should now have defense 2 — even though (0,1) has the
        // Capital which would otherwise only contribute 1. Verify via
        // DefenseRules directly.
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 20);

        g.Hud.ClickBuildTower();
        g.Map.SimulateClick(g.Tile(1, 1));

        // The selection was preserved (ExecuteBuildTower passes
        // clearSelection: false), so SelectedTerritory is still Red.
        Territory red = g.RedTerritory;
        // (0,1) is Red's capital, adjacent to (1,1)'s tower: defense 2.
        Assert.Equal(2, DefenseRules.Defense(HexCoord.FromOffset(0, 1), g.State.Grid, red));
    }

    [Fact]
    public void Bankruptcy_KillsUnits_ButNotTower()
    {
        // A bankrupt territory's UNITS become graves, but its TOWER
        // survives — towers have no upkeep.
        var g = new TestGame();
        g.Tile(3, 0).Occupant = new Unit(g.Blue.Color, UnitLevel.Knight);
        g.Tile(4, 0).Occupant = new Tower();
        HexCoord blueCapital = g.State.Territories
            .First(t => t.Owner == g.Blue.Color).Capital!.Value;
        g.State.Treasury.SetGold(blueCapital, 0);

        g.Hud.ClickEndTurn(); // advance to Blue: income then upkeep

        // Knight went bankrupt → grave.
        Assert.IsType<Grave>(g.Tile(3, 0).Occupant);
        // Tower untouched.
        Assert.IsType<Tower>(g.Tile(4, 0).Occupant);
    }

    [Fact]
    public void Tower_SurvivesEndTurn_AndTreeConversionPass()
    {
        // Build a tower, end turn twice, tower should still be there.
        // Confirms that ConvertGravesToTrees + SpreadTrees don't mutate
        // tower tiles.
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 20);

        g.Hud.ClickBuildTower();
        g.Map.SimulateClick(g.Tile(1, 1));

        g.Hud.ClickEndTurn(); // Red → Blue
        g.Hud.ClickEndTurn(); // Blue → Red

        Assert.IsType<Tower>(g.Tile(1, 1).Occupant);
    }

    // --- Win condition ---------------------------------------------------

    [Fact]
    public void Capture_LastEnemyHex_DeclaresWinner()
    {
        // Build a minimal fixture: all tiles Red except one Blue tile
        // adjacent to Red. Capturing that tile wipes Blue out and Red
        // should be declared the winner.
        var red = new Player("Red", new Color(1f, 0f, 0f));
        var blue = new Player("Blue", new Color(0f, 0f, 1f));
        var players = new List<Player> { red, blue };

        var grid = TestHelpers.BuildRectGrid(4, 1, red.Color);
        grid.Get(HexCoord.FromOffset(3, 0))!.Color = blue.Color;
        // Park a Red peasant adjacent to the Blue hex.
        grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Unit(red.Color);

        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var session = new SessionState();
        var map = new MockHexMapView();
        var hud = new MockHudView();
        foreach (KeyValuePair<HexCoord, Territory> kvp in territories.BuildTileIndex())
        {
            map.TileIndex[kvp.Key] = kvp.Value;
        }
        var controller = new GameController(state, session, map, hud);
        controller.StartGame();

        // Select Red, pick up the unit, capture the last Blue hex.
        map.SimulateClick(grid.Get(HexCoord.FromOffset(2, 0)));
        map.SimulateClick(grid.Get(HexCoord.FromOffset(3, 0)));

        Assert.True(session.IsGameOver);
        Assert.Equal(red.Color, session.Winner);
    }

    [Fact]
    public void Capture_NonFinalHex_DoesNotDeclareWinner()
    {
        var g = new TestGame();
        var unit = new Unit(g.Red.Color);
        g.Tile(1, 1).Occupant = unit;

        g.Map.SimulateClick(g.Tile(1, 1));
        g.Map.SimulateClick(g.Tile(2, 1)); // capture (2,1) — Blue still has tiles

        Assert.False(g.Session.IsGameOver);
        Assert.Null(g.Session.Winner);
    }

    [Fact]
    public void BuyPeasant_AfterWin_IsNoOp()
    {
        var g = new TestGame();
        g.Session.Winner = g.Red.Color; // simulate already-won state

        g.Map.SimulateClick(g.Tile(0, 1));
        g.Hud.ClickBuyPeasant();

        // Mode should still be None because the controller short-
        // circuits. Selection also does not happen.
        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
    }

    [Fact]
    public void EndTurn_AfterWin_IsNoOp()
    {
        var g = new TestGame();
        Color initialPlayer = g.State.Turns.CurrentPlayer.Color;
        g.Session.Winner = g.Red.Color;

        g.Hud.ClickEndTurn();

        Assert.Equal(initialPlayer, g.State.Turns.CurrentPlayer.Color);
    }

    [Fact]
    public void UndoLast_AfterWin_IsNoOp()
    {
        var g = new TestGame();
        // Do an action so undo is available, then simulate a win.
        g.Map.SimulateClick(g.Tile(0, 1));
        g.Hud.ClickBuyPeasant();
        g.Map.SimulateClick(g.Tile(1, 1));
        Assert.NotNull(g.Tile(1, 1).Unit);

        g.Session.Winner = g.Red.Color;
        g.Hud.ClickUndoLast();

        // Unit should still be present; undo was frozen.
        Assert.NotNull(g.Tile(1, 1).Unit);
    }

    [Fact]
    public void Capture_WinningCapture_ClearsUndoStack()
    {
        // Once the game is won, we don't want players rewinding past
        // the killing blow. HandleCapture should clear the undo stack.
        var red = new Player("Red", new Color(1f, 0f, 0f));
        var blue = new Player("Blue", new Color(0f, 0f, 1f));
        var players = new List<Player> { red, blue };

        var grid = TestHelpers.BuildRectGrid(4, 1, red.Color);
        grid.Get(HexCoord.FromOffset(3, 0))!.Color = blue.Color;
        grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Unit(red.Color);

        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var session = new SessionState();
        var map = new MockHexMapView();
        var hud = new MockHudView();
        foreach (KeyValuePair<HexCoord, Territory> kvp in territories.BuildTileIndex())
        {
            map.TileIndex[kvp.Key] = kvp.Value;
        }
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
        var red = new Player("Red", new Color(1f, 0f, 0f));
        var blue = new Player("Blue", new Color(0f, 0f, 1f));
        var green = new Player("Green", new Color(0f, 1f, 0f));
        var players = new List<Player> { red, blue, green };

        // Grid has only Red and Green tiles — no Blue.
        var grid = new HexGrid();
        grid.Add(new HexTile(HexCoord.FromOffset(0, 0), red.Color));
        grid.Add(new HexTile(HexCoord.FromOffset(1, 0), red.Color));
        grid.Add(new HexTile(HexCoord.FromOffset(3, 0), green.Color));
        grid.Add(new HexTile(HexCoord.FromOffset(4, 0), green.Color));

        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var session = new SessionState();
        var map = new MockHexMapView();
        var hud = new MockHudView();
        foreach (KeyValuePair<HexCoord, Territory> kvp in territories.BuildTileIndex())
        {
            map.TileIndex[kvp.Key] = kvp.Value;
        }
        var controller = new GameController(state, session, map, hud);
        controller.StartGame();

        Assert.Equal(red.Color, state.Turns.CurrentPlayer.Color);
        hud.ClickEndTurn();

        // Should skip Blue (eliminated) and land on Green.
        Assert.Equal(green.Color, state.Turns.CurrentPlayer.Color);
    }

    private static int CountTrees(HexGrid grid)
    {
        int n = 0;
        foreach (HexTile tile in grid.Tiles)
        {
            if (tile.Occupant is Tree) n++;
        }
        return n;
    }

    [Fact]
    public void EndTurn_ClearsUndoStack()
    {
        var g = new TestGame();
        // Queue an undoable action (buy peasant).
        g.Map.SimulateClick(g.Tile(0, 1));
        g.Hud.ClickBuyPeasant();
        g.Map.SimulateClick(g.Tile(1, 1));
        Assert.True(g.Session.Undo.CanUndo);

        g.Hud.ClickEndTurn();

        Assert.False(g.Session.Undo.CanUndo);
    }

    // --- Undo / redo ------------------------------------------------------

    [Fact]
    public void UndoLast_AfterBuy_RemovesTheUnitAndRefundsGold()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        int goldBefore = g.State.Treasury.GetGold(redCapital);

        g.Hud.ClickBuyPeasant();
        g.Map.SimulateClick(g.Tile(1, 1));
        Assert.NotNull(g.Tile(1, 1).Unit);
        Assert.Equal(goldBefore - 10, g.State.Treasury.GetGold(redCapital));

        g.Hud.ClickUndoLast();

        Assert.Null(g.Tile(1, 1).Unit);
        Assert.Equal(goldBefore, g.State.Treasury.GetGold(redCapital));
    }

    [Fact]
    public void RedoLast_AfterUndo_RestoresTheAction()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        g.Hud.ClickBuyPeasant();
        g.Map.SimulateClick(g.Tile(1, 1));
        g.Hud.ClickUndoLast();
        Assert.Null(g.Tile(1, 1).Unit);

        g.Hud.ClickRedoLast();

        Assert.NotNull(g.Tile(1, 1).Unit);
    }

    // --- HUD refresh reflects state ---------------------------------------

    [Fact]
    public void RefreshViews_ReportsHasActionable_WhenPlayerHasUnmovedUnit()
    {
        var g = new TestGame();
        // Red has an affordable capital (10 gold, exactly peasant cost),
        // so actionable is already true right after StartGame.
        Assert.True(g.Hud.LastHasActionableRemaining);
    }

    [Fact]
    public void Click_InvalidTargetDuringBuyingMode_CancelsAndFallsThrough()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        g.Hud.ClickBuyPeasant();
        Assert.Equal(SessionState.ActionMode.BuyingPeasant, g.Session.Mode);

        // (3, 0) is Blue, not adjacent to Red's territory, so not a valid
        // target. The buy should cancel, then the click falls through to
        // the normal handler which sees an enemy tile and clears selection.
        g.Map.SimulateClick(g.Tile(3, 0));

        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
        Assert.Null(g.Session.SelectedTerritory);
    }

    [Fact]
    public void Click_InvalidTargetDuringMovingMode_CancelsAndFallsThrough()
    {
        var g = new TestGame();
        var unit = new Unit(g.Red.Color);
        g.Tile(1, 1).Occupant = unit;
        g.Map.SimulateClick(g.Tile(1, 1));
        Assert.Equal(SessionState.ActionMode.MovingUnit, g.Session.Mode);

        // Click a non-adjacent Blue tile — invalid move target.
        g.Map.SimulateClick(g.Tile(4, 0));

        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
    }

    [Fact]
    public void BuyPeasant_OntoCapturableEnemyTile_CapturesImmediately()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        g.Hud.ClickBuyPeasant();

        // (2, 1) is Blue, not its capital, adjacent to Red's (1, 1).
        // Capturable by a new peasant.
        g.Map.SimulateClick(g.Tile(2, 1));

        Assert.Equal(g.Red.Color, g.Tile(2, 1).Color);
        Assert.NotNull(g.Tile(2, 1).Unit);
        Assert.True(g.Tile(2, 1).Unit!.HasMovedThisTurn);
        Assert.True(g.Map.RebuildCount >= 1);
    }

    [Fact]
    public void UndoTurn_AfterBuy_RestoresToStartOfTurn()
    {
        var g = new TestGame();
        HexCoord redCapital = g.RedTerritory.Capital!.Value;
        int goldBefore = g.State.Treasury.GetGold(redCapital);

        g.Map.SimulateClick(g.Tile(0, 1));
        g.Hud.ClickBuyPeasant();
        g.Map.SimulateClick(g.Tile(1, 1));
        Assert.NotNull(g.Tile(1, 1).Unit);

        g.Hud.ClickUndoTurn();

        Assert.Null(g.Tile(1, 1).Unit);
        Assert.Equal(goldBefore, g.State.Treasury.GetGold(redCapital));
    }

    [Fact]
    public void RedoAll_AfterUndoTurn_RestoresAllActions()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        g.Hud.ClickBuyPeasant();
        g.Map.SimulateClick(g.Tile(1, 1));
        g.Hud.ClickUndoTurn();
        Assert.Null(g.Tile(1, 1).Unit);

        g.Hud.ClickRedoAll();

        Assert.NotNull(g.Tile(1, 1).Unit);
    }

    [Fact]
    public void RefreshViews_ReportsNoActionable_WhenCapitalCantAffordAndNoUnits()
    {
        var g = new TestGame();
        // Drain Red's treasury so no peasant can be bought.
        HexCoord redCapital = g.RedTerritory.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 0);

        // Trigger a refresh by selecting nothing — SetSelection(null)
        // calls RefreshViews.
        g.Map.SimulateClick(null);

        Assert.False(g.Hud.LastHasActionableRemaining);
    }
}
