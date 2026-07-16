// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

public partial class GameControllerTests
{
    // --- Place-unit sound -------------------------------------------------
    //
    // The view's PlayUnitPlaced hook fires only on actions that consume
    // the unit's move (captures, tree/grave clears, and any new-unit
    // placement that lands on a non-own-empty tile). Free repositions
    // onto own empty tiles leave the unit actionable and must NOT fire.

    [Fact]
    public void Move_CaptureEnemyTile_FiresUnitPlacedSound()
    {
        var g = new TestGame();
        g.Tile(1, 1).Occupant = new Unit(g.Red.Id);

        g.Map.SimulateClick(g.Tile(1, 1));
        g.Map.SimulateClick(g.Tile(2, 1)); // capture empty Blue tile

        Assert.Single(g.Map.UnitPlacedSounds);
        Assert.Equal(HexCoord.FromOffset(2, 1), g.Map.UnitPlacedSounds[0]);
    }

    [Fact]
    public void Move_RepositionOntoOwnEmptyTile_DoesNotFireUnitPlacedSound()
    {
        // Reposition needs a third Red tile so the unit has somewhere
        // empty to land within its own territory.
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        ControllerHarness h = TestHelpers.BuildControllerGame(
            players: new List<Player> { red, blue },
            ownerOverrides: new[] { (0, 1, red.Id), (1, 1, red.Id), (2, 1, red.Id) });
        GameState state = h.State;
        MockHexMapView map = h.Map;
        HexGrid grid = state.Grid;

        // Place the unit on the middle Red tile (non-capital). The
        // capital placer picks lex-min — (0,1) — so (1,1) and (2,1)
        // are both empty and within range.
        var unit = new Unit(red.Id);
        grid.Get(HexCoord.FromOffset(1, 1))!.Occupant = unit;

        map.SimulateClick(grid.Get(HexCoord.FromOffset(1, 1))); // pick up
        map.SimulateClick(grid.Get(HexCoord.FromOffset(2, 1))); // reposition

        // Sanity: the unit physically moved.
        Assert.Null(grid.Get(HexCoord.FromOffset(1, 1))!.Unit);
        Assert.Same(unit, grid.Get(HexCoord.FromOffset(2, 1))!.Unit);
        // …but reposition leaves it actionable, so no place-sound fires.
        Assert.False(unit.HasMovedThisTurn);
        Assert.Empty(map.UnitPlacedSounds);
    }

    [Fact]
    public void BuyRecruit_CaptureEmptyEnemyTile_FiresUnitPlacedSound()
    {
        var g = new TestGame();
        HexCoord redCapital = g.RedTerritory.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 25);
        g.Map.SimulateClick(g.Tile(0, 1));
        g.Hud.ClickBuyRecruit();
        g.Map.SimulateClick(g.Tile(2, 1));

        Assert.Single(g.Map.UnitPlacedSounds);
        Assert.Equal(HexCoord.FromOffset(2, 1), g.Map.UnitPlacedSounds[0]);
    }

    [Fact]
    public void BuyRecruit_OnOwnEmptyTile_DoesNotFireUnitPlacedSound()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        g.Hud.ClickBuyRecruit();
        // (1,1) is Red and empty — placement leaves the new unit
        // actionable.
        g.Map.SimulateClick(g.Tile(1, 1));

        Assert.False(g.Tile(1, 1).Unit!.HasMovedThisTurn);
        Assert.Empty(g.Map.UnitPlacedSounds);
    }

    [Fact]
    public void BuildTower_FiresTowerPlacedSound()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 35); // enough for one tower

        g.Hud.ClickBuildTower();
        g.Map.SimulateClick(g.Tile(1, 1));

        Assert.IsType<Tower>(g.Tile(1, 1).Occupant);
        Assert.Single(g.Map.TowerPlacedSounds);
        Assert.Equal(HexCoord.FromOffset(1, 1), g.Map.TowerPlacedSounds[0]);
        // The tower path must NOT also fire the unit-placed sound.
        Assert.Empty(g.Map.UnitPlacedSounds);
    }

    [Fact]
    public void Move_CombineWithFriendlyUnit_FiresCombineSoundOnly()
    {
        // 3-Red-tile setup so we can put two friendly units on
        // adjacent non-capital tiles and combine them.
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        ControllerHarness h = TestHelpers.BuildControllerGame(
            players: new List<Player> { red, blue },
            ownerOverrides: new[] { (0, 1, red.Id), (1, 1, red.Id), (2, 1, red.Id) });
        MockHexMapView map = h.Map;
        HexGrid grid = h.State.Grid;

        var moving = new Unit(red.Id, UnitLevel.Recruit);
        var stationary = new Unit(red.Id, UnitLevel.Recruit);
        grid.Get(HexCoord.FromOffset(1, 1))!.Occupant = moving;
        grid.Get(HexCoord.FromOffset(2, 1))!.Occupant = stationary;

        map.SimulateClick(grid.Get(HexCoord.FromOffset(1, 1))); // pick up
        map.SimulateClick(grid.Get(HexCoord.FromOffset(2, 1))); // combine

        // The two recruits merged into a Soldier.
        Unit? combined = grid.Get(HexCoord.FromOffset(2, 1))!.Unit;
        Assert.NotNull(combined);
        Assert.Equal(UnitLevel.Soldier, combined!.Level);
        Assert.Null(grid.Get(HexCoord.FromOffset(1, 1))!.Unit);

        Assert.Single(map.UnitCombinedSounds);
        Assert.Equal(HexCoord.FromOffset(2, 1), map.UnitCombinedSounds[0]);
        // Combine path must NOT also fire the unit-place thud.
        Assert.Empty(map.UnitPlacedSounds);
    }

    [Fact]
    public void BuyRecruit_CombineOntoFriendlyUnit_FiresCombineSoundOnly()
    {
        var g = new TestGame();
        // Stationary recruit on (1,1) for the bought recruit to merge into.
        var stationary = new Unit(g.Red.Id, UnitLevel.Recruit);
        g.Tile(1, 1).Occupant = stationary;

        HexCoord redCapital = g.RedTerritory.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 25);

        g.Map.SimulateClick(g.Tile(0, 1));
        g.Hud.ClickBuyRecruit();
        g.Map.SimulateClick(g.Tile(1, 1)); // combine — bought recruit onto stationary recruit

        Unit? combined = g.Tile(1, 1).Unit;
        Assert.NotNull(combined);
        Assert.Equal(UnitLevel.Soldier, combined!.Level);

        Assert.Single(g.Map.UnitCombinedSounds);
        Assert.Equal(HexCoord.FromOffset(1, 1), g.Map.UnitCombinedSounds[0]);
        Assert.Empty(g.Map.UnitPlacedSounds);
    }

    // --- Destruction sounds: smoosh / burst / chop ----------------------
    //
    // When a Move/PlaceNew destroys an occupant (enemy unit, enemy tower,
    // own-territory tree or grave), the audio gate routes to the matching
    // destruction sound INSTEAD of the generic place thud. Empty-tile
    // captures still play the place sound (no occupant destroyed).

    [Fact]
    public void Move_CaptureEnemyUnit_FiresUnitDestroyedSound_NotPlace()
    {
        var g = new TestGame();
        var attacker = new Unit(g.Red.Id, UnitLevel.Soldier);
        g.Tile(1, 1).Occupant = attacker;
        var defender = new Unit(g.Blue.Id, UnitLevel.Recruit);
        g.Tile(2, 1).Occupant = defender;

        g.Map.SimulateClick(g.Tile(1, 1));
        g.Map.SimulateClick(g.Tile(2, 1));

        Assert.Single(g.Map.UnitDestroyedSounds);
        Assert.Equal(HexCoord.FromOffset(2, 1), g.Map.UnitDestroyedSounds[0]);
        Assert.Empty(g.Map.UnitPlacedSounds);
    }

    [Fact]
    public void Move_CaptureEnemyTower_FiresTowerDestroyedSound_NotPlace()
    {
        var g = new TestGame();
        var captain = new Unit(g.Red.Id, UnitLevel.Captain);
        g.Tile(1, 1).Occupant = captain;
        g.Tile(2, 1).Occupant = new Tower();

        g.Map.SimulateClick(g.Tile(1, 1));
        g.Map.SimulateClick(g.Tile(2, 1));

        Assert.Single(g.Map.TowerDestroyedSounds);
        Assert.Equal(HexCoord.FromOffset(2, 1), g.Map.TowerDestroyedSounds[0]);
        Assert.Empty(g.Map.UnitPlacedSounds);
    }

    // --- Bankruptcy sound -------------------------------------------------

    [Fact]
    public void StartPlayerTurn_BankruptcyOccurs_FiresBankruptcySoundOnce()
    {
        // StartGame doesn't run StartPlayerTurn for the initial human
        // player — upkeep first applies on the *next* turn-start. Set
        // up a Captain on a Blue tile, zero the Blue treasury, then end
        // Red's turn so Blue's StartPlayerTurn runs and bankrupts.
        var g = new TestGame();
        g.Tile(3, 0).Occupant = new Unit(g.Blue.Id, UnitLevel.Captain);
        Territory blueT = g.State.Territories.First(t => t.Owner == g.Blue.Id);
        HexCoord blueCapital = blueT.Capital!.Value;
        g.State.Treasury.SetGold(blueCapital, 0);

        g.Hud.ClickEndTurn();

        Assert.Equal(1, g.Map.BankruptcySoundCount);
        Assert.IsType<Grave>(g.Tile(3, 0).Occupant);
    }

    [Fact]
    public void StartPlayerTurn_NoBankruptcy_DoesNotFireBankruptcySound()
    {
        // Default TestGame has no units anywhere → no upkeep owed →
        // Blue's turn-start runs cleanly with no bankruptcy bell.
        var g = new TestGame();
        g.Hud.ClickEndTurn();
        Assert.Equal(0, g.Map.BankruptcySoundCount);
    }

    private static (GameState State, SessionState Session, MockHexMapView Map,
        MockHudView Hud, Player Red, Player Blue) BuildBlueBankruptcyScenario(bool blueIsAi)
    {
        // 5x2 grid: Red (human) holds {(0,1),(1,1)}, Blue holds the rest.
        // A Captain on a Blue tile with a zeroed Blue capital guarantees
        // Blue's next StartPlayerTurn bankrupts (Captain upkeep 18 > Blue
        // income). Mirrors StartPlayerTurn_BankruptcyOccurs_* but lets the
        // caller make Blue AI and wires aiSilentMode.
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1), isAi: blueIsAi);
        var players = new List<Player> { red, blue };

        var grid = TestHelpers.BuildRectGrid(5, 2, blue.Id);
        grid.Get(HexCoord.FromOffset(0, 1))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(1, 1))!.Owner = red.Id;

        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var session = new SessionState();
        session.ClaimVictoryPromptedHighestThreshold[red.Id] = 90;
        session.ClaimVictoryPromptedHighestThreshold[blue.Id] = 90;
        return (state, session, new MockHexMapView(), new MockHudView(), red, blue);
    }

    [Fact]
    public void StartPlayerTurn_AiBankruptcy_UnderInstantSilentMode_IsSilenced()
    {
        // Blue is AI; aiSilentMode is on (the "Instant" AI Speed wiring).
        // When Blue's turn starts the view is in silent mode, so the
        // per-turn bankruptcy toll must NOT reach the player — the AI
        // batch is a silent fast-forward.
        var (state, session, map, hud, _, blue) = BuildBlueBankruptcyScenario(blueIsAi: true);
        var controller = new GameController(
            state, session, map, hud, seed: 0,
            aiChooser: (s, c, v, ru, r) => null,
            aiPacer: new SynchronousAiPacer(),
            aiSilentMode: () => true);
        controller.StartGame();

        HexCoord blueCapital = state.Territories.First(t => t.Owner == blue.Id).Capital!.Value;
        state.Grid.Get(HexCoord.FromOffset(3, 0))!.Occupant = new Unit(blue.Id, UnitLevel.Captain);
        state.Treasury.SetGold(blueCapital, 0);

        hud.ClickEndTurn(); // Red ends → Blue (AI) StartPlayerTurn bankrupts

        Assert.IsType<Grave>(state.Grid.Get(HexCoord.FromOffset(3, 0))!.Occupant);
        Assert.Equal(0, map.BankruptcySoundCount);
    }

    [Fact]
    public void StartPlayerTurn_HumanBankruptcy_StillAudible_EvenWhenAiSilentModePredicateOn()
    {
        // Blue is human. Even though aiSilentMode() returns true, it's
        // the human's own turn, so silent mode is off and the player
        // hears their own bankruptcy bell. Pins the "always play for a
        // human player" half of the rule.
        var (state, session, map, hud, _, blue) = BuildBlueBankruptcyScenario(blueIsAi: false);
        var controller = new GameController(
            state, session, map, hud, seed: 0,
            aiChooser: (s, c, v, ru, r) => null,
            aiPacer: new SynchronousAiPacer(),
            aiSilentMode: () => true);
        controller.StartGame();

        HexCoord blueCapital = state.Territories.First(t => t.Owner == blue.Id).Capital!.Value;
        state.Grid.Get(HexCoord.FromOffset(3, 0))!.Occupant = new Unit(blue.Id, UnitLevel.Captain);
        state.Treasury.SetGold(blueCapital, 0);

        hud.ClickEndTurn(); // Red ends → Blue (human) StartPlayerTurn bankrupts

        Assert.IsType<Grave>(state.Grid.Get(HexCoord.FromOffset(3, 0))!.Occupant);
        Assert.Equal(1, map.BankruptcySoundCount);
    }

    [Fact]
    public void AiTurn_WhileReplayPaused_DoesNotRunUntilResumed()
    {
        // A narration beat landing during an opponent's turn holds the
        // paced AI run until the player taps it away. Modeled here with an
        // isReplayPaused predicate the test flips by hand.
        var (state, session, map, hud, _, blue) = BuildBlueBankruptcyScenario(blueIsAi: true);
        int chooserCalls = 0;
        bool paused = true;
        var controller = new GameController(
            state, session, map, hud, seed: 0,
            aiChooser: (s, c, v, ru, r) => { chooserCalls++; return null; },
            aiPacer: new SynchronousAiPacer(),
            isReplayPaused: () => paused);
        controller.StartGame();

        hud.ClickEndTurn(); // Red ends → Blue (AI) scheduled, but paused

        // Paused: the AI step machine must NOT consult the chooser or end
        // Blue's turn — control stays parked on Blue.
        Assert.Equal(0, chooserCalls);
        Assert.Equal(blue.Id, state.Turns.CurrentPlayer.Id);

        // Pause clears (player tapped the narration) → resume runs Blue's
        // turn to completion and control returns to Red.
        paused = false;
        controller.ResumeAiTurnsAfterReplayPause();

        Assert.True(chooserCalls >= 1);
        Assert.Equal(PlayerId.FromIndex(0), state.Turns.CurrentPlayer.Id);
    }

    [Fact]
    public void Move_CaptureEnemyCapital_FiresCapitalDestroyedSound_NotPlace()
    {
        // Capital provides 1 defense, so a Soldier (level 2) beats it.
        // We plant a Capital on a regular Blue tile rather than fight
        // the territory layout: the audio dispatcher routes purely on
        // result.Destroyed's runtime type.
        var g = new TestGame();
        g.Tile(1, 1).Occupant = new Unit(g.Red.Id, UnitLevel.Soldier);
        g.Tile(2, 1).Occupant = new Capital();

        g.Map.SimulateClick(g.Tile(1, 1));
        g.Map.SimulateClick(g.Tile(2, 1));

        Assert.Single(g.Map.CapitalDestroyedSounds);
        Assert.Equal(HexCoord.FromOffset(2, 1), g.Map.CapitalDestroyedSounds[0]);
        Assert.Empty(g.Map.UnitPlacedSounds);
    }

    [Fact]
    public void Capture_EliminatingEnemyLastCapital_FiresPlayerDefeatedSound()
    {
        // 4x1: Red {(0,0),(1,0)} with Soldier on (1,0); Blue {(2,0),(3,0)}.
        // Soldier captures (2,0) — Blue's capital tile. Blue's remaining
        // tile (3,0) becomes a singleton, capital-less → eliminated.
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        ControllerHarness h = TestHelpers.BuildControllerGame(
            players: new List<Player> { red, blue }, cols: 4, rows: 1,
            ownerOverrides: new[] { (0, 0, red.Id), (1, 0, red.Id) },
            suppressClaimVictory: false,
            beforeStart: s => s.Grid.Get(HexCoord.FromOffset(1, 0))!.Occupant =
                new Unit(red.Id, UnitLevel.Soldier));
        GameState state = h.State;
        MockHexMapView map = h.Map;

        map.SimulateClick(state.Grid.Get(HexCoord.FromOffset(1, 0)));
        map.SimulateClick(state.Grid.Get(HexCoord.FromOffset(2, 0)));

        Assert.Equal(1, map.PlayerDefeatedSoundCount);
    }

    [Fact]
    public void EliminatedPlayer_PhantomTurnRunsUpkeep_OrphanUnitBecomesGrave()
    {
        // 3-player setup: Red and Green still in the game, Blue is
        // eliminated (no capital on the board) with a single orphan
        // Recruit on a one-tile territory. After Red ends turn → Green
        // ends turn → Blue's skipped turn must still run upkeep so the
        // stranded Recruit bankrupts into a Grave (no capital, no gold,
        // owed > 0). Without the phantom-turn processing,
        // AdvanceToNextActivePlayer skips Blue entirely and the unit
        // would survive indefinitely.
        var red = new Player("Red", PlayerId.FromIndex(0));
        var green = new Player("Green", PlayerId.FromIndex(2));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        HexCoord orphanCoord = HexCoord.FromOffset(5, 1);
        ControllerHarness h = TestHelpers.BuildControllerGame(
            players: new List<Player> { red, green, blue },
            cols: 6, rows: 2, defaultOwner: red.Id,
            ownerOverrides: new[]
            {
                // Green: a 2-tile strip so it has a capital and passes IsEliminated.
                (0, 1, green.Id), (1, 1, green.Id),
                // Blue orphan singleton — no Blue capital on the board.
                (5, 1, blue.Id),
            },
            // Red owns >50% of the map; suppress every claim-victory tier so
            // End Turn doesn't open the modal and stall the test.
            beforeTerritories: g => g.Get(orphanCoord)!.Occupant =
                new Unit(blue.Id, UnitLevel.Recruit));
        GameState state = h.State;
        MockHudView hud = h.Hud;

        hud.ClickEndTurn(); // Red → Green
        hud.ClickEndTurn(); // Green → Blue phantom → Red

        HexTile orphan = state.Grid.Get(orphanCoord)!;
        Assert.Equal(blue.Id, orphan.Owner);
        Assert.IsType<Grave>(orphan.Occupant);
    }

    [Fact]
    public void EliminatedPlayer_PhantomTurnRunsTreeGrowth_SpreadsOntoOrphanSingleton()
    {
        // Blue is eliminated with a single empty singleton at offset
        // (3,1). Two neighbouring tiles — (4,0) NE and (3,2) SW — are
        // Red and each holds a Tree. Tree-growth iterates the
        // eliminated player's empty tiles and counts ANY tree neighbour
        // regardless of color (TreeRules.RunStartOfTurnGrowth), so
        // (3,1) sees two tree neighbours and converts. Bumping
        // TurnNumber > 1 lifts the round-1 tree-growth guard.
        var red = new Player("Red", PlayerId.FromIndex(0));
        var green = new Player("Green", PlayerId.FromIndex(2));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        // Blue empty singleton (neighbour of both tree-holding red tiles,
        // but not adjacent to any other blue tile).
        HexCoord emptyCoord = HexCoord.FromOffset(3, 1);
        ControllerHarness h = TestHelpers.BuildControllerGame(
            players: new List<Player> { red, green, blue },
            cols: 6, rows: 3, defaultOwner: red.Id,
            ownerOverrides: new[]
            {
                // Green's 2-tile territory (so it has a capital and the
                // end-of-turn win check doesn't fire).
                (0, 2, green.Id), (1, 2, green.Id),
                (3, 1, blue.Id),
            },
            // TurnNumber > 1 lifts the round-1 tree-growth guard.
            turnNumber: 2,
            // Two Red tiles flanking the Blue singleton, each with a Tree.
            beforeTerritories: g =>
            {
                g.Get(HexCoord.FromOffset(4, 0))!.Occupant = new Tree();
                g.Get(HexCoord.FromOffset(3, 2))!.Occupant = new Tree();
            });
        GameState state = h.State;
        MockHudView hud = h.Hud;

        hud.ClickEndTurn(); // Red → Green
        hud.ClickEndTurn(); // Green → Blue phantom → Red

        HexTile filled = state.Grid.Get(emptyCoord)!;
        Assert.Equal(blue.Id, filled.Owner);
        Assert.IsType<Tree>(filled.Occupant);
    }

    [Fact]
    public void Capture_EliminatingHumanPlayer_SetsPendingDefeatScreen()
    {
        // Same shape as the elimination test, but assert that
        // SessionState.PendingDefeatScreen is set to the human's color
        // — the HUD reads this to show the defeat overlay.
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        ControllerHarness h = TestHelpers.BuildControllerGame(
            players: new List<Player> { red, blue }, cols: 4, rows: 1,
            ownerOverrides: new[] { (0, 0, red.Id), (1, 0, red.Id) },
            suppressClaimVictory: false,
            beforeStart: s => s.Grid.Get(HexCoord.FromOffset(1, 0))!.Occupant =
                new Unit(red.Id, UnitLevel.Soldier));
        GameState state = h.State;
        MockHexMapView map = h.Map;
        SessionState session = h.Session;

        map.SimulateClick(state.Grid.Get(HexCoord.FromOffset(1, 0)));
        map.SimulateClick(state.Grid.Get(HexCoord.FromOffset(2, 0)));

        Assert.Equal(blue.Id, session.PendingDefeatScreen);
    }

    [Fact]
    public void Capture_EliminatingAiPlayer_DoesNotSetPendingDefeatScreen()
    {
        // AI defeats are silent (sound only, no popup). The Continue
        // overlay would be meaningless for a player no one is at the
        // keyboard for.
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1), isAi: true);
        ControllerHarness h = TestHelpers.BuildControllerGame(
            players: new List<Player> { red, blue }, cols: 4, rows: 1,
            ownerOverrides: new[] { (0, 0, red.Id), (1, 0, red.Id) },
            suppressClaimVictory: false,
            beforeStart: s => s.Grid.Get(HexCoord.FromOffset(1, 0))!.Occupant =
                new Unit(red.Id, UnitLevel.Soldier));
        GameState state = h.State;
        MockHexMapView map = h.Map;
        SessionState session = h.Session;

        map.SimulateClick(state.Grid.Get(HexCoord.FromOffset(1, 0)));
        map.SimulateClick(state.Grid.Get(HexCoord.FromOffset(2, 0)));

        Assert.Null(session.PendingDefeatScreen);
    }

    [Fact]
    public void Capture_RecordingMode_DoesNotSetPendingDefeatScreenForNonZeroPlayer()
    {
        // In Tutorial Builder's Record mode, every slot is forced Human
        // so the dev can play all six. Defeats for non-player-0 colors
        // should be silent because those colors will be AI in the
        // eventual Preview playback (where the overlay wouldn't fire).
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        ControllerHarness h = TestHelpers.BuildControllerGame(
            players: new List<Player> { red, blue }, cols: 4, rows: 1,
            ownerOverrides: new[] { (0, 0, red.Id), (1, 0, red.Id) },
            suppressClaimVictory: false, recordingMode: true,
            beforeStart: s => s.Grid.Get(HexCoord.FromOffset(1, 0))!.Occupant =
                new Unit(red.Id, UnitLevel.Soldier));
        GameState state = h.State;
        MockHexMapView map = h.Map;
        SessionState session = h.Session;

        map.SimulateClick(state.Grid.Get(HexCoord.FromOffset(1, 0)));
        map.SimulateClick(state.Grid.Get(HexCoord.FromOffset(2, 0)));

        Assert.Null(session.PendingDefeatScreen);
    }

    [Fact]
    public void Capture_RecordingMode_StillSetsPendingDefeatScreenForPlayer0()
    {
        // Player 0 (Red here) is the slot that becomes the actual human
        // in playback, so a defeat of player 0 during recording should
        // still raise the overlay — the dev needs it visible to record
        // the matching dismiss beat.
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        ControllerHarness h = TestHelpers.BuildControllerGame(
            players: new List<Player> { red, blue }, cols: 4, rows: 1,
            defaultOwner: red.Id,
            ownerOverrides: new[] { (2, 0, blue.Id), (3, 0, blue.Id) },
            currentPlayerIndex: 1,
            suppressClaimVictory: false, recordingMode: true,
            beforeStart: s => s.Grid.Get(HexCoord.FromOffset(2, 0))!.Occupant =
                new Unit(blue.Id, UnitLevel.Soldier));
        GameState state = h.State;
        MockHexMapView map = h.Map;
        SessionState session = h.Session;

        map.SimulateClick(state.Grid.Get(HexCoord.FromOffset(2, 0)));
        map.SimulateClick(state.Grid.Get(HexCoord.FromOffset(1, 0)));

        Assert.Equal(red.Id, session.PendingDefeatScreen);
    }

    [Fact]
    public void Construct_PreviewMode_TellsHudToSuppressVictoryOverlay()
    {
        // Tutorial Preview must not let the click-blocking "X wins!"
        // modal pop on top of the scripted flow — the tutorial-message
        // panel signals completion instead.
        var players = new List<Player>
        {
            new("Red", PlayerId.FromIndex(0)),
            new("Blue", PlayerId.FromIndex(1), isAi: true),
        };
        ControllerHarness h = TestHelpers.BuildControllerGame(
            players: players, cols: 2, rows: 1, defaultOwner: players[0].Id,
            ownerOverrides: System.Array.Empty<(int, int, PlayerId)>(),
            previewMode: true, startGame: false);

        Assert.True(h.Hud.VictoryOverlaySuppressed);
    }

    [Fact]
    public void Construct_RecordingMode_TellsHudToSuppressVictoryOverlay()
    {
        // Same reasoning for Record: a domination mid-recording would
        // otherwise interrupt the dev with a victory modal they can't
        // record around.
        var players = new List<Player>
        {
            new("Red", PlayerId.FromIndex(0)),
            new("Blue", PlayerId.FromIndex(1)),
        };
        ControllerHarness h = TestHelpers.BuildControllerGame(
            players: players, cols: 2, rows: 1, defaultOwner: players[0].Id,
            ownerOverrides: System.Array.Empty<(int, int, PlayerId)>(),
            recordingMode: true, startGame: false);

        Assert.True(h.Hud.VictoryOverlaySuppressed);
    }

    [Fact]
    public void Construct_DefaultMode_DoesNotSuppressVictoryOverlay()
    {
        // Regular game lets the full-win modal fire normally.
        var players = new List<Player>
        {
            new("Red", PlayerId.FromIndex(0)),
            new("Blue", PlayerId.FromIndex(1), isAi: true),
        };
        ControllerHarness h = TestHelpers.BuildControllerGame(
            players: players, cols: 2, rows: 1, defaultOwner: players[0].Id,
            ownerOverrides: System.Array.Empty<(int, int, PlayerId)>(),
            startGame: false);

        Assert.False(h.Hud.VictoryOverlaySuppressed);
    }

    [Fact]
    public void DismissDefeatScreen_ClearsPendingDefeatScreen()
    {
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        ControllerHarness h = TestHelpers.BuildControllerGame(
            players: new List<Player> { red, blue }, cols: 4, rows: 1,
            ownerOverrides: new[] { (0, 0, red.Id), (1, 0, red.Id) },
            suppressClaimVictory: false,
            beforeStart: s => s.Grid.Get(HexCoord.FromOffset(1, 0))!.Occupant =
                new Unit(red.Id, UnitLevel.Soldier));
        GameState state = h.State;
        MockHexMapView map = h.Map;
        MockHudView hud = h.Hud;
        SessionState session = h.Session;

        map.SimulateClick(state.Grid.Get(HexCoord.FromOffset(1, 0)));
        map.SimulateClick(state.Grid.Get(HexCoord.FromOffset(2, 0)));
        Assert.Equal(blue.Id, session.PendingDefeatScreen);

        hud.ClickDefeatContinue();

        Assert.Null(session.PendingDefeatScreen);
    }

    [Fact]
    public void AiTurn_PausesWhilePendingDefeatScreen()
    {
        // AI is mid-turn capturing a human. Defeat sets PendingDefeatScreen,
        // and the AI loop should NOT schedule its next step until the
        // human dismisses the overlay.
        //
        // 5x1: Red (human) {(0,0)} singleton + Blue (AI) {(1,0),(2,0),(3,0),(4,0)}
        // with a Captain at (1,0) (level 3, beats capital). Wait — Red singleton
        // means Red is "eliminated" at start under our rotation rule. Use a
        // 5x2 layout instead so Red has a 2-hex territory + a one-hex
        // outpost the AI can capture for the kill.
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1), isAi: true);
        var players = new List<Player> { red, blue };

        // 5x1: Red {(3,0),(4,0)} (capital at lex-min (3,0)), Blue
        // {(0,0),(1,0),(2,0)} with a Soldier at (2,0). Soldier at
        // (2,0) is adjacent to Red's capital (3,0); Soldier (atk 2)
        // beats capital (def 1), so the heuristic captures it on
        // first beat, eliminating Red. (Soldier upkeep = 6g vs
        // Blue's 15g seed, so it survives the start-of-turn upkeep
        // pass.)
        // Deterministic chooser: first call returns the killing-blow
        // move, subsequent calls return null (end of AI turn). Removes
        // dependence on the heuristic's scoring behavior.
        AiAction? scriptedKill = new AiMoveAction(
            HexCoord.FromOffset(2, 0), HexCoord.FromOffset(3, 0));
        AiAction? Chooser(GameState s, PlayerId c, HashSet<HexCoord> v, HashSet<HexCoord> ru, Random r)
        {
            AiAction? next = scriptedKill;
            scriptedKill = null;
            return next;
        }

        var pacer = new QueuedAiPacer();
        ControllerHarness h = TestHelpers.BuildControllerGame(
            players: new List<Player> { red, blue }, cols: 5, rows: 1,
            defaultOwner: PlayerId.None,
            ownerOverrides: new[]
            {
                (0, 0, blue.Id), (1, 0, blue.Id), (2, 0, blue.Id),
                (3, 0, red.Id), (4, 0, red.Id),
            },
            seed: 0, aiChooser: Chooser, aiPacer: pacer,
            suppressClaimVictory: false,
            beforeStart: s => s.Grid.Get(HexCoord.FromOffset(2, 0))!.Occupant =
                new Unit(blue.Id, UnitLevel.Soldier));
        MockHexMapView map = h.Map;
        MockHudView hud = h.Hud;
        SessionState session = h.Session;

        // End Red's (human) turn so Blue's (AI) turn begins.
        hud.ClickEndTurn();

        // Drain the AI loop. The AI's first capture should hit Red's
        // (1,0) tile, then capture Red's capital (0,0) — eliminating Red.
        // After elimination fires PendingDefeatScreen, the AI loop must
        // stop scheduling further steps.
        pacer.DrainAll();

        // PendingDefeatScreen is set, AI is paused. There should be no
        // pending callback queued.
        Assert.Equal(red.Id, session.PendingDefeatScreen);
        Assert.False(pacer.HasPending);
    }

    [Fact]
    public void Buy_PlacingSoldierOnEnemyLastCapital_FiresPlayerDefeatedSound()
    {
        // User's repro: enemy has ONE 2-hex territory, total. Player
        // selects own territory, buys a Soldier, places it on the
        // enemy's capital tile. Capture eliminates the enemy.
        //
        // 4x1: Red {(0,0),(1,0)} adjacent to Blue {(2,0),(3,0)}.
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        ControllerHarness h = TestHelpers.BuildControllerGame(
            players: new List<Player> { red, blue }, cols: 4, rows: 1,
            ownerOverrides: new[] { (0, 0, red.Id), (1, 0, red.Id) },
            suppressClaimVictory: false);
        GameState state = h.State;
        MockHexMapView map = h.Map;
        MockHudView hud = h.Hud;

        // Boost Red's treasury so we can afford a Soldier (cost 20).
        HexCoord redCapital = state.Territories.First(t => t.Owner == red.Id).Capital!.Value;
        state.Treasury.SetGold(redCapital, 100);

        // Select Red's territory, cycle buy-mode to Soldier, place on
        // Blue's capital tile (lex-min in Blue's 2-hex territory = (2,0)).
        map.SimulateClick(state.Grid.Get(HexCoord.FromOffset(0, 0)));
        hud.ClickBuyRecruit();             // Recruit
        hud.ClickBuyRecruit();             // Soldier
        map.SimulateClick(state.Grid.Get(HexCoord.FromOffset(2, 0)));

        Assert.Equal(1, map.PlayerDefeatedSoundCount);
    }

    [Fact]
    public void Capture_EnemyStillHasCapital_DoesNotFirePlayerDefeatedSound()
    {
        // 5x1: Red {(0,0),(1,0)} with Soldier on (1,0); Blue {(2,0),(3,0),(4,0)}.
        // Soldier captures (2,0) — Blue's capital. Blue's remaining
        // {(3,0),(4,0)} is still a 2-tile territory → fresh capital
        // placed → Blue is NOT eliminated.
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        ControllerHarness h = TestHelpers.BuildControllerGame(
            players: new List<Player> { red, blue }, cols: 5, rows: 1,
            ownerOverrides: new[] { (0, 0, red.Id), (1, 0, red.Id) },
            suppressClaimVictory: false,
            beforeStart: s => s.Grid.Get(HexCoord.FromOffset(1, 0))!.Occupant =
                new Unit(red.Id, UnitLevel.Soldier));
        GameState state = h.State;
        MockHexMapView map = h.Map;

        map.SimulateClick(state.Grid.Get(HexCoord.FromOffset(1, 0)));
        map.SimulateClick(state.Grid.Get(HexCoord.FromOffset(2, 0)));

        Assert.Equal(0, map.PlayerDefeatedSoundCount);
    }

    [Fact]
    public void Move_ClearTreeInOwnTerritory_FiresTreeClearedSound_NotPlace()
    {
        // Same 3-Red-tile fixture as the existing tree-FX test: capital
        // at (0,1), unit at (2,1), tree at (1,1). Move (2,1) → (1,1)
        // chops the tree.
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        ControllerHarness h = TestHelpers.BuildControllerGame(
            players: new List<Player> { red, blue },
            ownerOverrides: new[] { (0, 1, red.Id), (1, 1, red.Id), (2, 1, red.Id) });
        MockHexMapView map = h.Map;
        HexGrid grid = h.State.Grid;
        grid.Get(HexCoord.FromOffset(2, 1))!.Occupant = new Unit(red.Id);
        grid.Get(HexCoord.FromOffset(1, 1))!.Occupant = new Tree();

        map.SimulateClick(grid.Get(HexCoord.FromOffset(2, 1))); // pick up
        map.SimulateClick(grid.Get(HexCoord.FromOffset(1, 1))); // chop tree

        Assert.Single(map.TreeClearedSounds);
        Assert.Equal(HexCoord.FromOffset(1, 1), map.TreeClearedSounds[0]);
        Assert.Empty(map.UnitPlacedSounds);
    }

    [Fact]
    public void ExecuteAiBuildTower_FiresTowerPlacedSound()
    {
        (GameState state, MockHexMapView map, MockHudView hud) = BuildAiFixture();
        HexCoord cap = RedCapital(state);
        state.Treasury.SetGold(cap, 20);
        // (0,1) is Red, non-capital (capital is (0,0)), and empty — a
        // legal tower site.
        var act = new AiBuildTowerAction(cap, HexCoord.FromOffset(0, 1));
        GameController c = BuildHarnessWithStubAi(state, map, hud, act, null);

        c.StartGame();

        Assert.IsType<Tower>(state.Grid.Get(HexCoord.FromOffset(0, 1))!.Occupant);
        Assert.Single(map.TowerPlacedSounds);
        Assert.Equal(HexCoord.FromOffset(0, 1), map.TowerPlacedSounds[0]);
    }

    [Fact]
    public void Move_CaptureEnemyTower_FiresDestructionEffectWithTower()
    {
        // Captain captures an enemy tower — the displaced Tower is
        // reported in the destruction effect so the view can render
        // tower-shaped FX.
        var g = new TestGame();
        var captain = new Unit(g.Red.Id, UnitLevel.Captain);
        g.Tile(1, 1).Occupant = captain;
        var tower = new Tower();
        g.Tile(2, 1).Occupant = tower;

        g.Map.SimulateClick(g.Tile(1, 1));
        g.Map.SimulateClick(g.Tile(2, 1));

        Assert.Single(g.Map.DestructionEffects);
        Assert.Same(tower, g.Map.DestructionEffects[0].Destroyed);
    }

    [Fact]
    public void Move_OntoOwnTree_FiresDestructionEffectWithTree()
    {
        // Custom 5×2 Red fixture: unit on (2,1), tree on (1,1); the move
        // chops the tree and fires a destruction effect for it.
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        ControllerHarness h = TestHelpers.BuildControllerGame(
            players: new List<Player> { red, blue },
            ownerOverrides: new[] { (0, 1, red.Id), (1, 1, red.Id), (2, 1, red.Id) });
        MockHexMapView map = h.Map;
        HexGrid grid = h.State.Grid;
        var unit = new Unit(red.Id);
        grid.Get(HexCoord.FromOffset(2, 1))!.Occupant = unit;
        var tree = new Tree();
        grid.Get(HexCoord.FromOffset(1, 1))!.Occupant = tree;

        map.SimulateClick(grid.Get(HexCoord.FromOffset(2, 1)));
        map.SimulateClick(grid.Get(HexCoord.FromOffset(1, 1)));

        Assert.Single(map.DestructionEffects);
        Assert.Equal(HexCoord.FromOffset(1, 1), map.DestructionEffects[0].Coord);
        Assert.Same(tree, map.DestructionEffects[0].Destroyed);
    }

    [Fact]
    public void Undo_DoesNotReplayDestructionEffect()
    {
        // Capture fires FX, undo restores the prior state but should
        // NOT replay or fire any new destruction effects — only
        // forward play does.
        var g = new TestGame();
        var attacker = new Unit(g.Red.Id, UnitLevel.Soldier);
        g.Tile(1, 1).Occupant = attacker;
        var defender = new Unit(g.Blue.Id, UnitLevel.Recruit);
        g.Tile(2, 1).Occupant = defender;

        g.Map.SimulateClick(g.Tile(1, 1));
        g.Map.SimulateClick(g.Tile(2, 1));
        Assert.Single(g.Map.DestructionEffects);

        g.Hud.ClickUndoLast();

        // Still exactly the one FX from forward play; no new entries.
        Assert.Single(g.Map.DestructionEffects);
    }

    [Fact]
    public void BuyRecruit_OnOwnTile_StaysInBuyingMode_IfStillAffordable()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        // Give Red enough to buy two recruits in a row.
        g.State.Treasury.SetGold(redCapital, 25);

        g.Hud.ClickBuyRecruit();
        Assert.Equal(SessionState.ActionMode.BuyingRecruit, g.Session.Mode);

        // (1,1) is an empty Red non-capital tile — valid placement.
        g.Map.SimulateClick(g.Tile(1, 1));

        // Bought: 25 - 10 = 15 remaining, still ≥ 10 → stay in mode.
        Assert.Equal(SessionState.ActionMode.BuyingRecruit, g.Session.Mode);
        Assert.Equal(15, g.State.Treasury.GetGold(redCapital));
    }

    [Fact]
    public void BuyRecruit_OnOwnTile_ExitsBuyingMode_IfNoLongerAffordable()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 10); // exactly one recruit

        g.Hud.ClickBuyRecruit();
        g.Map.SimulateClick(g.Tile(1, 1));

        // Bought: 10 - 10 = 0 < 10 → exit mode.
        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
    }

    [Fact]
    public void BuyRecruit_CombineOntoFriendlyUnit_ExitsBuyingMode_EvenIfAffordable()
    {
        // Combining is an explicit punctuation point in a streak of buys —
        // even with gold left for another recruit, the mode exits so the
        // player has to re-press Buy Recruit to continue.
        var g = new TestGame();
        var stationary = new Unit(g.Red.Id, UnitLevel.Recruit);
        g.Tile(1, 1).Occupant = stationary;

        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 25); // enough for two recruits

        g.Hud.ClickBuyRecruit();
        g.Map.SimulateClick(g.Tile(1, 1)); // combine onto stationary recruit

        Assert.Equal(UnitLevel.Soldier, g.Tile(1, 1).Unit!.Level);
        Assert.Equal(15, g.State.Treasury.GetGold(redCapital)); // affordability sanity
        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
    }

    [Fact]
    public void BuyRecruit_Capture_StaysInBuyingMode_IfMergedTerritoryStillAffordable()
    {
        // Capture rebinds the selection to the new territory; the
        // affordability check runs against that new selection. The
        // Red territory in TestGame merges with the captured tile
        // (trivially — (2,1) becomes part of Red). Red's gold is 25-10=15,
        // enough for another recruit.
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 25);

        g.Hud.ClickBuyRecruit();
        g.Map.SimulateClick(g.Tile(2, 1)); // capture Blue adjacent

        // Still in mode; selection rebound; treasury 15.
        Assert.Equal(SessionState.ActionMode.BuyingRecruit, g.Session.Mode);
        Assert.NotNull(g.Session.SelectedTerritory);
        Assert.Contains(HexCoord.FromOffset(2, 1), g.Session.SelectedTerritory!.Coords);
    }

    [Fact]
    public void BuildTower_EnteringMode_ShowsValidTowerTargets()
    {
        // Red territory is (0,1) capital + (1,1). Pressing Build Tower
        // with enough gold should publish (1,1) as a valid tower-target
        // preview — (0,1) is occupied by the capital so it's not legal.
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 20);

        g.Hud.ClickBuildTower();

        Assert.Equal(new[] { HexCoord.FromOffset(1, 1) }, g.Map.LastTowerTargets);
    }

    [Fact]
    public void BuildTower_AfterPlace_RefreshesTowerTargets()
    {
        // 35g lets Red build a tower at (1,1) and stay in BuildingTower
        // mode, but with (0,1) being the capital and (1,1) now a tower
        // there are no legal placements left — the preview should clear
        // so the player isn't staring at stale highlights.
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 35);

        g.Hud.ClickBuildTower();
        g.Map.SimulateClick(g.Tile(1, 1));

        Assert.Empty(g.Map.LastTowerTargets);
    }

    [Fact]
    public void BuildTower_ThenBuyRecruit_ClearsTowerTargets()
    {
        // Switching from BuildingTower mode into a buy mode must wipe
        // the tower-target preview — otherwise the player picks a unit
        // and still sees green tower icons floating around.
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 25); // 15 tower + 10 recruit

        g.Hud.ClickBuildTower();
        Assert.NotEmpty(g.Map.LastTowerTargets); // sanity

        g.Hud.ClickBuyRecruit();

        Assert.Empty(g.Map.LastTowerTargets);
    }

    [Fact]
    public void BuildTower_OnInTerritoryInvalidTarget_KeepsTargetPreview()
    {
        // In-range near-miss (capital is inside the selected territory
        // but already occupied): flash, stay in mode, keep the tower-
        // target preview so the user can pick a different in-territory
        // tile without re-pressing the build button.
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 20);

        g.Hud.ClickBuildTower();
        Assert.NotEmpty(g.Map.LastTowerTargets);
        g.Map.SimulateClick(g.Tile(0, 1));       // capital — in-territory, occupied

        Assert.NotEmpty(g.Map.LastTowerTargets);
        Assert.Equal(SessionState.ActionMode.BuildingTower, g.Session.Mode);
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
    public void BuildTower_WhileInBuyingRecruitMode_SwitchesToBuildingMode()
    {
        // Clicking a different placement button while in a placement
        // mode should switch cleanly to the new mode.
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 30);

        g.Hud.ClickBuyRecruit();
        Assert.Equal(SessionState.ActionMode.BuyingRecruit, g.Session.Mode);

        g.Hud.ClickBuildTower();
        Assert.Equal(SessionState.ActionMode.BuildingTower, g.Session.Mode);
    }

    [Fact]
    public void BuyRecruit_Capture_KeepsSelection_OnAttackerNewTerritory()
    {
        // Same QoL guarantee for the buy-and-capture path.
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        g.Hud.ClickBuyRecruit();
        // (2,1) is Blue, adjacent to Red's (1,1). Capturable by a fresh recruit.
        g.Map.SimulateClick(g.Tile(2, 1));

        Assert.NotNull(g.Session.SelectedTerritory);
        Assert.Equal(g.Red.Id, g.Session.SelectedTerritory!.Owner);
        Assert.Contains(HexCoord.FromOffset(2, 1), g.Session.SelectedTerritory.Coords);
    }

    [Fact]
    public void Move_WithinOwnTerritory_DoesNotConsumeAction()
    {
        var g = new TestGame();
        var unit = new Unit(g.Red.Id);
        // Red has only 2 hexes, one of which is the capital, so the unit on
        // (1,1) has no in-territory reposition target.
        g.Tile(1, 1).Occupant = unit;

        g.Map.SimulateClick(g.Tile(1, 1));
        // Red has nowhere to reposition (other tile is capital). The move
        // targets should still include captures but no repositions.
        Assert.Contains(HexCoord.FromOffset(2, 1), g.Map.LastMoveTargets);
    }
}
