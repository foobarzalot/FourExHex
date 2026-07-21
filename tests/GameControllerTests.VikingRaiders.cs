// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

public partial class GameControllerTests
{
    // --- Viking Raiders mode -----------------------------------

    /// <summary>
    /// Controller harness for Viking Raiders games (mirrors TidesGame).
    /// <paramref name="beforeStart"/> runs after state construction but
    /// before StartGame — seed viking state / occupants there.
    /// </summary>
    private sealed class VikingGame
    {
        public GameState State { get; }
        public SessionState Session { get; }
        public MockHexMapView Map { get; }
        public MockHudView Hud { get; }
        public GameController Controller { get; }
        public Player Red { get; }
        public Player Blue { get; }
        public bool GameEndedRaised { get; private set; }

        public VikingGame(
            HexGrid grid,
            IReadOnlySet<HexCoord>? water = null,
            int currentPlayerIndex = 0,
            int turnNumber = 1,
            PlayerKind blueKind = PlayerKind.Human,
            bool suppressClaimVictory = true,
            System.Action<GameState>? beforeStart = null)
        {
            Red = new Player("Red", PlayerId.FromIndex(0));
            Blue = new Player("Blue", PlayerId.FromIndex(1), blueKind);
            var players = new List<Player> { Red, Blue };
            IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
            State = new GameState(
                grid, territories, players,
                new TurnState(players, currentPlayerIndex, turnNumber),
                new Treasury(),
                waterCoords: water, mode: GameMode.VikingRaiders);
            Session = new SessionState();
            if (suppressClaimVictory)
            {
                foreach (Player p in players)
                {
                    Session.ClaimVictoryPromptedHighestThreshold[p.Id] = 90;
                }
            }
            Map = new MockHexMapView();
            Hud = new MockHudView();
            beforeStart?.Invoke(State);
            Controller = new GameController(State, Session, Map, Hud);
            Controller.GameEnded += () => GameEndedRaised = true;
            Controller.StartGame();
        }

        public int LandedVikingCount => State.Grid.Tiles.Count(
            t => t.Occupant is Unit u && u.Owner.IsNone);
    }

    /// <summary>
    /// 3×3 island: Red owns rows 0–1, Blue row 2, the whole east flank
    /// (offset col 3) is coastal water plus one far-out sea tile.
    /// </summary>
    private static (HexGrid grid, HashSet<HexCoord> water) VikingIsland()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(3, 3, PlayerId.FromIndex(0));
        for (int col = 0; col < 3; col++)
        {
            grid.Get(HexCoord.FromOffset(col, 2))!.Owner = PlayerId.FromIndex(1);
        }
        var water = new HashSet<HexCoord>
        {
            HexCoord.FromOffset(3, 0),
            HexCoord.FromOffset(3, 1),
            HexCoord.FromOffset(3, 2),
            HexCoord.FromOffset(10, 10),
        };
        return (grid, water);
    }

    private static void EndRound(VikingGame g)
    {
        g.Hud.ClickEndTurn(); // Red
        g.Hud.ClickEndTurn(); // Blue
    }

    [Fact]
    public void VikingRaiders_FirstWaveSpawnsAtRoundThreeStart()
    {
        (HexGrid grid, HashSet<HexCoord> water) = VikingIsland();
        var g = new VikingGame(grid, water);

        EndRound(g); // round 1 → 2
        Assert.Empty(g.State.Vikings.AtSea);
        Assert.Equal(0, g.State.Vikings.NextWaveIndex);

        EndRound(g); // round 2 → 3: the viking turn spawns wave 0
        Assert.Equal(3, g.State.Turns.TurnNumber);
        // Wave 0 is 5 Soldiers + 5 Recruits (strongest first), clamped to
        // this map's 3 coastal coords — 3 Soldiers spawn.
        Assert.Equal(3, g.State.Vikings.AtSea.Count);
        // The wave's arrival is sounded with the longship cue (one per wave).
        Assert.Single(g.Map.VikingArrivalSounds);
        Assert.All(g.State.Vikings.AtSea, v => Assert.Equal(UnitLevel.Soldier, v.Level));
        Assert.Equal(1, g.State.Vikings.NextWaveIndex);
        Assert.Equal(3, g.State.Vikings.LastSpawnRound);
        Assert.Equal(3, g.State.Vikings.LastCompletedRound);
        // Control has come back to Red, whose turn started AFTER the phase.
        Assert.Equal(g.Red.Id, g.State.Turns.CurrentPlayer.Id);
        Assert.Equal(0, g.LandedVikingCount); // spawn round: nobody lands yet
    }

    [Fact]
    public void VikingRaiders_WaveDisembarksTheFollowingRound()
    {
        (HexGrid grid, HashSet<HexCoord> water) = VikingIsland();
        var g = new VikingGame(grid, water);

        EndRound(g);
        EndRound(g); // wave 0 spawned at round-3 start
        EndRound(g); // round 3 → 4: the wave lands

        Assert.Equal(4, g.State.Turns.TurnNumber);
        Assert.Empty(g.State.Vikings.AtSea); // disembarked (or perished)
        Assert.Equal(3, g.LandedVikingCount); // undefended coast: all land
        // Every landed viking sits on a neutral tile.
        foreach (HexTile t in g.State.Grid.Tiles)
        {
            if (t.Occupant is Unit u && u.Owner.IsNone)
            {
                Assert.True(t.Owner.IsNone, $"viking at {t.Coord} on non-neutral tile");
            }
        }
    }

    [Fact]
    public void VikingRaiders_VikingTurnRunsOncePerRound()
    {
        (HexGrid grid, HashSet<HexCoord> water) = VikingIsland();
        var g = new VikingGame(grid, water);

        EndRound(g);
        EndRound(g); // round 3: wave spawned
        Assert.Equal(3, g.State.Vikings.AtSea.Count);

        g.Hud.ClickEndTurn(); // Red ends: mid-round boundary (Blue's turn)
        // No second viking activity this round.
        Assert.Equal(3, g.State.Vikings.AtSea.Count);
        Assert.Equal(3, g.State.Vikings.LastCompletedRound);
        Assert.Equal(0, g.LandedVikingCount);
    }

    [Fact]
    public void VikingRaiders_NoEndOfTurnWinWhileThreatRemains()
    {
        // Red is the sole capital-bearer (Blue is an orphan singleton), so
        // End Turn would normally declare Red the winner — but pending
        // waves gate every win until the onslaught is over.
        HexGrid grid = TestHelpers.BuildRectGrid(3, 3, PlayerId.FromIndex(0));
        grid.Get(HexCoord.FromOffset(2, 2))!.Owner = PlayerId.FromIndex(1);
        var g = new VikingGame(grid); // no water: waves spawn empty but stay scheduled

        g.Hud.ClickEndTurn();

        Assert.False(g.Session.IsGameOver);
        Assert.Null(g.Session.Winner);

        // Clear the threat: schedule exhausted, nothing at sea, nothing landed.
        g.State.Vikings.Reset(
            System.Array.Empty<SeaViking>(),
            nextWaveIndex: VikingRaidersRules.TotalWaves,
            lastCompletedRound: g.State.Turns.TurnNumber,
            lastSpawnRound: 0);

        g.Hud.ClickEndTurn();

        Assert.True(g.Session.IsGameOver);
        Assert.Equal(g.Red.Id, g.Session.Winner);
    }

    [Fact]
    public void VikingRaiders_DominationGatedWhileWavesPending()
    {
        // Red captures Blue's last tile and owns the whole board — in any
        // other mode that's an instant domination win; here pending waves
        // block it.
        HexGrid grid = TestHelpers.BuildRectGrid(3, 1, PlayerId.FromIndex(0));
        HexCoord blueTile = HexCoord.FromOffset(2, 0);
        grid.Get(blueTile)!.Owner = PlayerId.FromIndex(1);
        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant =
            new Unit(PlayerId.FromIndex(0), UnitLevel.Soldier);
        var g = new VikingGame(grid);

        g.Map.SimulateClick(g.State.Grid.Get(HexCoord.FromOffset(1, 0)));  // pick the unit
        Assert.Equal(SessionState.ActionMode.MovingUnit, g.Session.Mode);
        g.Map.SimulateClick(g.State.Grid.Get(blueTile));                   // capture

        Assert.Equal(PlayerId.FromIndex(0), g.State.Grid.Get(blueTile)!.Owner);
        Assert.False(g.Session.IsGameOver);
        Assert.Null(g.Session.Winner);
    }

    [Fact]
    public void VikingRaiders_ClaimVictoryPromptSuppressedWhileThreatRemains()
    {
        // Red owns 4/6 tiles (66%) — freeform would offer the 50% claim tier
        // on End Turn; Viking Raiders never offers a claim while the
        // onslaught is live.
        HexGrid grid = TestHelpers.BuildRectGrid(6, 1, PlayerId.FromIndex(0));
        grid.Get(HexCoord.FromOffset(4, 0))!.Owner = PlayerId.FromIndex(1);
        grid.Get(HexCoord.FromOffset(5, 0))!.Owner = PlayerId.FromIndex(1);
        var g = new VikingGame(grid, suppressClaimVictory: false);

        g.Hud.ClickEndTurn();

        Assert.Null(g.Session.PendingClaimVictory);
        Assert.Equal(g.Blue.Id, g.State.Turns.CurrentPlayer.Id); // turn simply ended
    }

    [Fact]
    public void VikingRaiders_VikingTakingHumansLastCapital_RaisesDefeatAndResumes()
    {
        // Red: capital at (2,1) — kept off (1,1) by a unit there — with the
        // capital tile the sea viking's only landing site. A Captain
        // (defense there is capital 1 + adjacent Soldier radiation 2 = 2 < 3)
        // storms it, eliminating Red mid-phase: the defeat overlay pauses the
        // phase; Continue resumes and finishes it, handing control to Blue.
        HexGrid grid = TestHelpers.BuildRectGrid(3, 3, PlayerId.FromIndex(1));
        HexCoord redUnitTile = HexCoord.FromOffset(1, 1);
        HexCoord redCapital = HexCoord.FromOffset(2, 1);
        grid.Get(redUnitTile)!.Owner = PlayerId.FromIndex(0);
        grid.Get(redCapital)!.Owner = PlayerId.FromIndex(0);
        grid.Get(redUnitTile)!.Occupant = new Unit(PlayerId.FromIndex(0), UnitLevel.Soldier);
        HexCoord sea = HexCoord.FromOffset(3, 1);
        var g = new VikingGame(
            grid,
            water: new HashSet<HexCoord> { sea },
            currentPlayerIndex: 1, // Blue is mid-turn on round 2
            turnNumber: 2,
            beforeStart: s => s.Vikings.Reset(
                new[] { new SeaViking(sea, UnitLevel.Captain) },
                nextWaveIndex: VikingRaidersRules.TotalWaves, // isolate: no more spawns
                lastCompletedRound: 2,
                lastSpawnRound: 2));

        Assert.Equal(redCapital,
            g.State.Territories.First(t => t.Owner == g.Red.Id).Capital);

        g.Hud.ClickEndTurn(); // Blue ends round 2 → round-3 viking turn

        // The disembark captured Red's capital → Red (human) defeated;
        // the phase pauses on the overlay before Red's turn can start.
        Assert.Equal(g.Red.Id, g.Session.PendingDefeatScreen);
        Assert.True(g.State.Grid.Get(redCapital)!.Owner.IsNone);
        Assert.Equal(1, g.LandedVikingCount);

        g.Hud.ClickDefeatContinue();

        // Phase finished; eliminated Red was skipped; Blue's turn started.
        Assert.Null(g.Session.PendingDefeatScreen);
        Assert.Equal(3, g.State.Vikings.LastCompletedRound);
        Assert.Equal(g.Blue.Id, g.State.Turns.CurrentPlayer.Id);
        Assert.False(g.Session.IsGameOver); // Blue survives; threat remains
    }

    [Fact]
    public void VikingRaiders_TotalWipeout_VikingsWin()
    {
        // Two 2-tile players, each capital one hex from the sea, and a
        // Captain waiting off each coast. The round-3 viking turn takes both
        // capitals: nobody holds a capital → the Vikings win outright.
        HexGrid grid = TestHelpers.BuildSpotGrid(
            PlayerId.FromIndex(0),
            HexCoord.FromOffset(2, 0), HexCoord.FromOffset(2, 1));
        grid.Add(new HexTile(HexCoord.FromOffset(0, 0), PlayerId.FromIndex(1)));
        grid.Add(new HexTile(HexCoord.FromOffset(0, 1), PlayerId.FromIndex(1)));
        HexCoord redSea = HexCoord.FromOffset(3, 0);
        HexCoord blueSea = HexCoord.FromOffset(-1, 0);
        var g = new VikingGame(
            grid,
            water: new HashSet<HexCoord> { redSea, blueSea },
            turnNumber: 3, // viking turn pending immediately at game start
            blueKind: PlayerKind.Computer, // only one human defeat overlay
            beforeStart: s => s.Vikings.Reset(
                new[]
                {
                    new SeaViking(redSea, UnitLevel.Captain),
                    new SeaViking(blueSea, UnitLevel.Captain),
                },
                nextWaveIndex: VikingRaidersRules.TotalWaves,
                lastCompletedRound: 0,
                lastSpawnRound: 2));

        // The Red (human) defeat pauses the phase; dismiss to finish it.
        Assert.Equal(g.Red.Id, g.Session.PendingDefeatScreen);
        g.Hud.ClickDefeatContinue();

        Assert.True(g.Session.IsGameOver);
        Assert.Equal(PlayerId.None, g.Session.Winner);
        Assert.True(g.GameEndedRaised);
    }

    [Fact]
    public void VikingRaiders_TotalWipeout_DeclaredAtPhaseEnd_EvenWithWavesPending()
    {
        // The Vikings-conquered declaration happens the moment the viking
        // phase completes with no capital left standing — the usual
        // "no win while threat remains" gate does NOT hold it back (the
        // raiders being the only side left IS the outcome).
        HexGrid grid = TestHelpers.BuildSpotGrid(
            PlayerId.FromIndex(0),
            HexCoord.FromOffset(2, 0), HexCoord.FromOffset(2, 1));
        grid.Add(new HexTile(HexCoord.FromOffset(0, 0), PlayerId.FromIndex(1)));
        grid.Add(new HexTile(HexCoord.FromOffset(0, 1), PlayerId.FromIndex(1)));
        HexCoord redSea = HexCoord.FromOffset(3, 0);
        HexCoord blueSea = HexCoord.FromOffset(-1, 0);
        var g = new VikingGame(
            grid,
            water: new HashSet<HexCoord> { redSea, blueSea },
            turnNumber: 3,
            blueKind: PlayerKind.Computer,
            beforeStart: s => s.Vikings.Reset(
                new[]
                {
                    new SeaViking(redSea, UnitLevel.Captain),
                    new SeaViking(blueSea, UnitLevel.Captain),
                },
                nextWaveIndex: 0, // waves still pending — threat clearly remains
                lastCompletedRound: 0,
                lastSpawnRound: 2));

        Assert.Equal(g.Red.Id, g.Session.PendingDefeatScreen);
        g.Hud.ClickDefeatContinue(); // resume: the phase finishes inline

        Assert.True(g.Session.IsGameOver);
        Assert.Equal(PlayerId.None, g.Session.Winner);
        Assert.True(g.GameEndedRaised);
        Assert.Equal(3, g.State.Vikings.LastCompletedRound); // declared AT phase end
    }

    /// <summary>
    /// A round-2 board where the round-3 viking turn is forced: the Captain
    /// at (3,1) has its only landing (2,1) blocked by a Red Commander →
    /// perish. With <paramref name="keepThreatAlive"/>, a landed Recruit
    /// sits boxed in on the neutral tile (2,0) — every neighbour is
    /// defense-covered (capital radiation west, the Commander east), so it
    /// keeps the threat alive without ever acting or capturing a capital.
    /// </summary>
    private static VikingGame BuildPerishGame(bool keepThreatAlive)
    {
        HexGrid grid = TestHelpers.BuildRectGrid(3, 3, PlayerId.FromIndex(0));
        for (int col = 0; col < 3; col++)
        {
            grid.Get(HexCoord.FromOffset(col, 2))!.Owner = PlayerId.FromIndex(1);
        }
        grid.Get(HexCoord.FromOffset(2, 1))!.Occupant =
            new Unit(PlayerId.FromIndex(0), UnitLevel.Commander); // defense 4: blocks Captain
        if (keepThreatAlive)
        {
            grid.Get(HexCoord.FromOffset(2, 0))!.Owner = PlayerId.None;
            grid.Get(HexCoord.FromOffset(2, 0))!.Occupant =
                new Unit(PlayerId.None, UnitLevel.Recruit);
        }
        HexCoord blockedSea = HexCoord.FromOffset(3, 1);
        return new VikingGame(
            grid,
            water: new HashSet<HexCoord> { blockedSea },
            currentPlayerIndex: 1,
            turnNumber: 2,
            beforeStart: s => s.Vikings.Reset(
                new[] { new SeaViking(blockedSea, UnitLevel.Captain) },
                nextWaveIndex: VikingRaidersRules.TotalWaves, // no spawn noise
                lastCompletedRound: 2,
                lastSpawnRound: 2));
    }

    [Fact]
    public void VikingRaiders_PerishLeavesSeaGrave_AndPlaysSubmergeBloop()
    {
        VikingGame g = BuildPerishGame(keepThreatAlive: true);
        HexCoord blockedSea = HexCoord.FromOffset(3, 1);

        g.Hud.ClickEndTurn(); // Blue ends round 2 → round-3 viking turn runs

        Assert.Equal(3, g.State.Vikings.LastCompletedRound);
        Assert.Empty(g.State.Vikings.AtSea);
        Assert.Contains(blockedSea, g.State.Vikings.SeaGraves);
        Assert.Contains(blockedSea, g.Map.TileSubmergedSounds);
        Assert.DoesNotContain(blockedSea, g.Map.UnitDestroyedSounds);

        // The grave washes away when the NEXT viking turn begins.
        g.Hud.ClickEndTurn(); // Red
        g.Hud.ClickEndTurn(); // Blue → round-4 viking turn
        Assert.Equal(4, g.State.Vikings.LastCompletedRound);
        Assert.Empty(g.State.Vikings.SeaGraves);
    }

    [Fact]
    public void VikingRaiders_SeaGraveClearsImmediately_WhenPerishEndsTheThreat()
    {
        // The perishing Captain was the last viking anywhere — no future
        // viking turn will run, so the grave must not linger forever.
        VikingGame g = BuildPerishGame(keepThreatAlive: false);

        g.Hud.ClickEndTurn();

        Assert.Contains(HexCoord.FromOffset(3, 1), g.Map.TileSubmergedSounds);
        Assert.Empty(g.State.Vikings.SeaGraves);
        Assert.False(g.Session.IsGameOver); // ordinary play continues
    }

    [Fact]
    public void VikingRaiders_WaveBannerShownAtEveryHumanTurnStart()
    {
        (HexGrid grid, HashSet<HexCoord> water) = VikingIsland();
        var g = new VikingGame(grid, water);
        int total = VikingRaidersRules.TotalWaves;

        // Turn 1, Red (game start counts as a human turn start).
        Assert.Equal($"Wave 1/{total} arriving in 2 turns", g.Hud.TransientBanners.Last());
        int seen = g.Hud.TransientBanners.Count;

        g.Hud.ClickEndTurn(); // Blue's turn 1: their own banner
        Assert.Equal(seen + 1, g.Hud.TransientBanners.Count);
        Assert.Equal($"Wave 1/{total} arriving in 2 turns", g.Hud.TransientBanners.Last());

        g.Hud.ClickEndTurn(); // Red, turn 2
        Assert.Equal($"Wave 1/{total} arriving in 1 turn", g.Hud.TransientBanners.Last());

        g.Hud.ClickEndTurn(); // Blue, turn 2
        g.Hud.ClickEndTurn(); // round 3: wave 1 spawns, then Red's turn
        Assert.Equal($"Wave 1/{total}", g.Hud.TransientBanners.Last());

        g.Hud.ClickEndTurn(); // Blue, round 3: same spawn message
        Assert.Equal($"Wave 1/{total}", g.Hud.TransientBanners.Last());
    }

    [Fact]
    public void Freeform_NeverShowsWaveBanner()
    {
        var g = new TestGame();
        g.Hud.ClickEndTurn();
        g.Hud.ClickEndTurn();

        Assert.Empty(g.Hud.TransientBanners);
    }

    [Fact]
    public void VikingRaiders_SpawnBeatHoldsThePhase_ForTheArrivalPresentation()
    {
        // After the wave-spawn beat executes, the driver must schedule the
        // phase-ending continuation with the arrival-presentation hold —
        // NOT the ordinary between-beats delay — so the human's turn start
        // (auto-select, camera pan, banner) waits for the animation+sound.
        (HexGrid grid, HashSet<HexCoord> water) = VikingIsland();
        var players = new List<Player>
        {
            new Player("Red", PlayerId.FromIndex(0)),
            new Player("Blue", PlayerId.FromIndex(1)),
        };
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(
            grid, territories, players,
            new TurnState(players, currentPlayerIndex: 1, turnNumber: 2), new Treasury(),
            waterCoords: water, mode: GameMode.VikingRaiders);
        var session = new SessionState();
        foreach (Player p in players)
        {
            session.ClaimVictoryPromptedHighestThreshold[p.Id] = 90;
        }
        var pacer = new QueuedAiPacer();
        var hud = new MockHudView();
        var controller = new GameController(
            state, session, new MockHexMapView(), hud, aiPacer: pacer);
        controller.StartGame();
        pacer.DrainAll();
        _ = controller;

        hud.ClickEndTurn();

        // Pump one beat at a time until the spawn has executed.
        int guard = 0;
        while (state.Vikings.AtSea.Count == 0 && pacer.HasPending && guard++ < 20)
        {
            pacer.StepOne();
        }
        Assert.True(state.Vikings.AtSea.Count > 0, "wave never spawned");
        // The phase is still open (its ending continuation is queued) and
        // the just-requested delay is the presentation hold.
        Assert.True(state.Vikings.LastCompletedRound < 3);
        Assert.Equal(StepPacing.VikingSpawnPresentationMs, pacer.ScheduledDelaysMs.Last());

        pacer.DrainAll(); // presentation over: phase completes, Red's turn starts
        Assert.Equal(3, state.Vikings.LastCompletedRound);
        Assert.Equal(players[0].Id, state.Turns.CurrentPlayer.Id);
    }

    [Fact]
    public void VikingRaiders_HumanInputLockedDuringVikingPhase()
    {
        // A queued pacer holds the viking phase open mid-flight; the human
        // (whose StartPlayerTurn hasn't run yet) must not be able to act.
        (HexGrid grid, HashSet<HexCoord> water) = VikingIsland();
        var players = new List<Player>
        {
            new Player("Red", PlayerId.FromIndex(0)),
            new Player("Blue", PlayerId.FromIndex(1)),
        };
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(
            grid, territories, players,
            new TurnState(players, currentPlayerIndex: 1, turnNumber: 2), new Treasury(),
            waterCoords: water, mode: GameMode.VikingRaiders);
        var session = new SessionState();
        foreach (Player p in players)
        {
            session.ClaimVictoryPromptedHighestThreshold[p.Id] = 90;
        }
        var map = new MockHexMapView();
        var hud = new MockHudView();
        var pacer = new QueuedAiPacer();
        var controller = new GameController(state, session, map, hud, aiPacer: pacer);
        controller.StartGame();
        pacer.DrainAll(); // settle any startup scheduling
        _ = controller;

        hud.ClickEndTurn(); // Blue ends round 2 → round-3 viking phase (paced)
        pacer.StepOne();    // first viking beat only — phase mid-flight
        Assert.True(pacer.HasPending); // phase genuinely open

        int undoDepthBefore = session.Undo.UndoCount;
        // Try to act as the human mid-phase: click a tile, press End Turn.
        map.SimulateClick(state.Grid.Get(HexCoord.FromOffset(0, 0)));
        hud.ClickEndTurn();

        Assert.Equal(undoDepthBefore, session.Undo.UndoCount); // nothing tracked
        Assert.Null(session.SelectedTerritory);                // click ignored

        pacer.DrainAll(); // let the phase finish
        Assert.Equal(3, state.Vikings.LastCompletedRound);
        Assert.Equal(players[0].Id, state.Turns.CurrentPlayer.Id); // Red's turn began
    }

    [Fact]
    public void VikingRaiders_IsVikingPhaseActiveProbe_TracksThePhase()
    {
        // Main's playback-speed closure uses this probe to keep viking
        // beats (which run while the waiting player may be human) on the
        // Computer Player Speed instead of the human's.
        (HexGrid grid, HashSet<HexCoord> water) = VikingIsland();
        var players = new List<Player>
        {
            new Player("Red", PlayerId.FromIndex(0)),
            new Player("Blue", PlayerId.FromIndex(1)),
        };
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(
            grid, territories, players,
            new TurnState(players, currentPlayerIndex: 1, turnNumber: 2), new Treasury(),
            waterCoords: water, mode: GameMode.VikingRaiders);
        var session = new SessionState();
        foreach (Player p in players)
        {
            session.ClaimVictoryPromptedHighestThreshold[p.Id] = 90;
        }
        var map = new MockHexMapView();
        var hud = new MockHudView();
        var pacer = new QueuedAiPacer();
        var controller = new GameController(state, session, map, hud, aiPacer: pacer);
        controller.StartGame();
        pacer.DrainAll();

        Assert.False(controller.IsVikingPhaseActive); // ordinary human turn

        hud.ClickEndTurn(); // Blue ends round 2 → round-3 viking phase (paced)
        pacer.StepOne();    // first viking beat only — phase mid-flight
        Assert.True(controller.IsVikingPhaseActive);

        pacer.DrainAll();   // phase completes, Red's turn begins
        Assert.False(controller.IsVikingPhaseActive);
    }
}
