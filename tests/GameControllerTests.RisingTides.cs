// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

public partial class GameControllerTests
{
    // --- Rising Tides mode -----------------------------------

    private sealed class TidesGame
    {
        public GameState State { get; }
        public SessionState Session { get; }
        public MockHexMapView Map { get; }
        public MockHudView Hud { get; }
        public GameController Controller { get; }
        public Player Red { get; }
        public Player Blue { get; }

        public TidesGame(HexGrid grid, GameMode mode, int turnNumber = 1)
        {
            Red = new Player("Red", PlayerId.FromIndex(0));
            Blue = new Player("Blue", PlayerId.FromIndex(1));
            var players = new List<Player> { Red, Blue };
            IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
            State = new GameState(
                grid, territories, players, new TurnState(players, 0, turnNumber), new Treasury(),
                waterCoords: null, mode: mode);
            Session = new SessionState();
            Map = new MockHexMapView();
            Hud = new MockHudView();
            Controller = new GameController(State, Session, Map, Hud);
            Controller.StartGame();
        }
    }

    // A 10x1 row: Red owns cols 0-7 (80%), Blue cols 8-9. Both have capitals.
    private static HexGrid LopsidedRow()
    {
        var grid = TestHelpers.BuildRectGrid(10, 1, PlayerId.FromIndex(0));
        grid.Get(HexCoord.FromOffset(8, 0))!.Owner = PlayerId.FromIndex(1);
        grid.Get(HexCoord.FromOffset(9, 0))!.Owner = PlayerId.FromIndex(1);
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
        // Claim-victory tiers (75/90%) apply in Rising Tides too, computed
        // over the current non-sunk tiles. Red owns 8/10 (80%) of a single
        // connected row, so ending its turn trips the 75% tier — the same
        // prompt freeform shows.
        var freeform = new TidesGame(LopsidedRow(), GameMode.Freeform);
        freeform.Hud.ClickEndTurn();
        Assert.NotNull(freeform.Session.PendingClaimVictory);
        Assert.Equal(75, freeform.Session.PendingClaimVictory!.Value.ThresholdPercent);

        var tides = new TidesGame(LopsidedRow(), GameMode.RisingTides);
        tides.Hud.ClickEndTurn();
        Assert.NotNull(tides.Session.PendingClaimVictory);
        Assert.Equal(75, tides.Session.PendingClaimVictory!.Value.ThresholdPercent);
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
        // The erosion is telegraphed at the START of a player's turn
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
        // Mid-turn win uses WinnerByDomination in every mode: Red captures Blue's
        // only tile, so Red owns the whole (non-water) board — full domination
        // ends the game immediately.
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
    public void RisingTides_CaptureLeavesEnemyOrphanSingleton_NoMidTurnWin()
    {
        // 5x1 row: Red owns cols 0-2 (with a Soldier on col 2), Blue owns cols
        // 3-4 (its only capital-bearing territory; the capital lands on the
        // lex-min tile, col 3). Red's Soldier (level 2) captures Blue's capital
        // on col 3 (defense 1), reducing Blue to a lone capital-less singleton on
        // col 4 that Blue STILL owns. Red is now the sole capital-bearer, but does
        // NOT own every tile — so the mid-turn domination check must NOT end the
        // game. The "last player standing" force-win only resolves at end of turn.
        var grid = TestHelpers.BuildRectGrid(5, 1, PlayerId.FromIndex(0));
        grid.Get(HexCoord.FromOffset(3, 0))!.Owner = PlayerId.FromIndex(1);
        grid.Get(HexCoord.FromOffset(4, 0))!.Owner = PlayerId.FromIndex(1);
        grid.Get(HexCoord.FromOffset(2, 0))!.Occupant =
            new Unit(PlayerId.FromIndex(0), UnitLevel.Soldier);
        var g = new TidesGame(grid, GameMode.RisingTides);

        g.Map.SimulateClick(g.State.Grid.Get(HexCoord.FromOffset(2, 0)));
        g.Map.SimulateClick(g.State.Grid.Get(HexCoord.FromOffset(3, 0)));

        // Capture happened (col 3 is now Red) but Blue's orphan singleton survives.
        Assert.Equal(g.Red.Id, g.State.Grid.Get(HexCoord.FromOffset(3, 0))!.Owner);
        Assert.Equal(g.Blue.Id, g.State.Grid.Get(HexCoord.FromOffset(4, 0))!.Owner);
        Assert.False(g.Session.IsGameOver);
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
    public void RisingTides_HumanTurnStart_PansToDoomedTileAndSelectsItsTerritory()
    {
        // Issue #113: at every human turn start in Rising Tides the camera
        // centers the telegraphed doomed tile and the auto-select picks the
        // territory containing it (instead of the largest actionable one).
        var g = new TidesGame(TwoBlocks(), GameMode.RisingTides);

        Assert.Single(g.State.PendingTide);
        HexCoord doomed = g.State.PendingTide[0].Coord;
        Assert.Equal(doomed, g.Map.LastCenteredCoord);
        Assert.NotNull(g.Session.SelectedTerritory);
        Assert.Contains(doomed, g.Session.SelectedTerritory!.Coords);
        Assert.Equal(g.Red.Id, g.Session.SelectedTerritory.Owner);

        // Turn rotation reaches the same seam: Blue's turn start focuses
        // Blue's own forecast tile.
        g.Hud.ClickEndTurn();
        Assert.Equal(g.Blue.Id, g.State.Turns.CurrentPlayer.Id);
        Assert.Single(g.State.PendingTide);
        HexCoord blueDoomed = g.State.PendingTide[0].Coord;
        Assert.Equal(blueDoomed, g.Map.LastCenteredCoord);
        Assert.NotNull(g.Session.SelectedTerritory);
        Assert.Contains(blueDoomed, g.Session.SelectedTerritory!.Coords);
        Assert.Equal(g.Blue.Id, g.Session.SelectedTerritory.Owner);
    }

    [Fact]
    public void RisingTides_DoomedSingleton_PansButLeavesNothingSelected()
    {
        // 6x2 grid with (4,0), (4,1), (5,1) removed: Red block cols 0-1,
        // Blue block cols 2-3, and a fully isolated Red singleton at (5,0)
        // whose six missing neighbours give it the maximum water-border
        // weight — the forecast deterministically dooms it. A capital-less
        // singleton isn't selectable, so the camera pans but nothing is
        // auto-selected.
        var grid = TestHelpers.BuildRectGrid(6, 2, PlayerId.FromIndex(1));
        for (int row = 0; row < 2; row++)
            for (int col = 0; col < 2; col++)
                grid.Get(HexCoord.FromOffset(col, row))!.Owner = PlayerId.FromIndex(0);
        HexCoord singleton = HexCoord.FromOffset(5, 0);
        grid.Get(singleton)!.Owner = PlayerId.FromIndex(0);
        grid.Remove(HexCoord.FromOffset(4, 0));
        grid.Remove(HexCoord.FromOffset(4, 1));
        grid.Remove(HexCoord.FromOffset(5, 1));
        var g = new TidesGame(grid, GameMode.RisingTides);

        Assert.Single(g.State.PendingTide);
        Assert.Equal(singleton, g.State.PendingTide[0].Coord);
        Assert.Equal(singleton, g.Map.LastCenteredCoord);
        Assert.Null(g.Session.SelectedTerritory);
    }

    [Fact]
    public void Freeform_HumanTurnStart_FirstTurnSelectsAndPans()
    {
        // Outside Rising Tides the doomed-tile focus doesn't apply: no
        // tide forecast, no coord-centering — the first-turn fallback
        // selects the lex-min actionable territory and pans to it.
        var g = new TidesGame(TwoBlocks(), GameMode.Freeform);

        Assert.Empty(g.State.PendingTide);
        Assert.Null(g.Map.LastCenteredCoord);
        Assert.NotNull(g.Session.SelectedTerritory);
        Assert.Same(g.Session.SelectedTerritory, g.Map.LastCenteredTerritory);
        Assert.Equal(1, g.Map.CenterCount);
    }

    [Fact]
    public void RisingTides_LaterTurnStart_DoomedFocusBeatsSelectionMemory()
    {
        // The doomed-hex focus is the one turn-start pan that survives
        // #209 — and it wins over the restore path even once the player
        // HAS a remembered selection: Red's second turn still pans to
        // (and selects the territory of) the fresh forecast tile.
        var g = new TidesGame(TwoBlocks(), GameMode.RisingTides);
        g.Hud.ClickEndTurn(); // end Red t1 (memory recorded, tide applied)
        g.Hud.ClickEndTurn(); // end Blue t1 → Red's second turn

        Assert.Equal(g.Red.Id, g.State.Turns.CurrentPlayer.Id);
        Assert.Single(g.State.PendingTide);
        HexCoord doomed = g.State.PendingTide[0].Coord;
        Assert.Equal(doomed, g.Map.LastCenteredCoord);
        Assert.NotNull(g.Session.SelectedTerritory);
        Assert.Contains(doomed, g.Session.SelectedTerritory!.Coords);
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

    [Fact]
    public void RisingTides_TideBannerShownAtEveryHumanTurnStart()
    {
        // The tide level + countdown banner shows at every human turn start,
        // via the same transient-banner slot as the Viking wave banner.
        var g = new TidesGame(TwoBlocks(), GameMode.RisingTides);

        // Turn 1, Red (game start counts as a human turn start).
        Assert.Equal("Tide level 1 — rising in 6 turns", g.Hud.TransientBanners.Last());
        int seen = g.Hud.TransientBanners.Count;

        g.Hud.ClickEndTurn(); // Blue's turn 1: their own banner
        Assert.Equal(seen + 1, g.Hud.TransientBanners.Count);
        Assert.Equal("Tide level 1 — rising in 6 turns", g.Hud.TransientBanners.Last());
    }

    [Fact]
    public void RisingTides_LastRoundOfLevel_BannerCountsDownSingularTurn()
    {
        var g = new TidesGame(TwoBlocks(), GameMode.RisingTides, turnNumber: 6);
        Assert.Equal("Tide level 1 — rising in 1 turn", g.Hud.TransientBanners.Last());
    }

    [Fact]
    public void RisingTides_LateRound_ForecastsLevelTilesAndAppliesAll()
    {
        // Round 7 is tide level 2: the turn-start forecast holds two steps
        // and the end-of-turn apply submerges both.
        var g = new TidesGame(TwoBlocks(), GameMode.RisingTides, turnNumber: 7);

        Assert.Equal("Tide level 2 — rising in 6 turns", g.Hud.TransientBanners.Last());
        Assert.Equal(2, g.State.PendingTide.Count);
        int before = g.State.Grid.Count;

        g.Hud.ClickEndTurn();

        Assert.Equal(before - 2, g.State.Grid.Count);
        Assert.Equal(2, g.State.WaterCoords.Count);
    }

    [Fact]
    public void RisingTides_MultiTileForecast_PansToLexMinDoomedTile()
    {
        // With more than one doomed tile the camera pans to the first in
        // lex order — NOT PendingTide[0]. The isolated Red singleton at
        // (5,0) has the maximum water-border weight, so it is always the
        // forecast's FIRST pick; at level 2 the second pick comes from the
        // main Red block and is lex-smaller, so the pan target differs
        // from PendingTide[0] by construction.
        var grid = TestHelpers.BuildRectGrid(6, 2, PlayerId.FromIndex(1));
        for (int row = 0; row < 2; row++)
            for (int col = 0; col < 2; col++)
                grid.Get(HexCoord.FromOffset(col, row))!.Owner = PlayerId.FromIndex(0);
        HexCoord singleton = HexCoord.FromOffset(5, 0);
        grid.Get(singleton)!.Owner = PlayerId.FromIndex(0);
        grid.Remove(HexCoord.FromOffset(4, 0));
        grid.Remove(HexCoord.FromOffset(4, 1));
        grid.Remove(HexCoord.FromOffset(5, 1));
        var g = new TidesGame(grid, GameMode.RisingTides, turnNumber: 7);

        Assert.Equal(2, g.State.PendingTide.Count);
        Assert.Equal(singleton, g.State.PendingTide[0].Coord);
        HexCoord lexMin = g.State.PendingTide.Select(s => s.Coord).Min();
        Assert.NotEqual(singleton, lexMin);
        Assert.Equal(lexMin, g.Map.LastCenteredCoord);
        Assert.NotNull(g.Session.SelectedTerritory);
        Assert.Contains(lexMin, g.Session.SelectedTerritory!.Coords);
    }
}
