// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// The viking pseudo-turn sequencer (<see cref="VikingAi.ChooseNext"/>) and
/// the <see cref="ComputerAi"/> / <see cref="AiStateScorer"/> adaptations that
/// let the ordinary AI drive capital-less neutral (viking) territories.
/// </summary>
public class VikingAiTests
{
    private static readonly PlayerId Red = PlayerId.FromIndex(0);
    private static readonly PlayerId Blue = PlayerId.FromIndex(1);

    private static GameState MakeState(
        HexGrid grid,
        IReadOnlySet<HexCoord>? water = null,
        int turnNumber = 4,
        GameMode mode = GameMode.VikingRaiders)
    {
        var players = new List<Player>
        {
            new Player("Red", Red),
            new Player("Blue", Blue),
        };
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        return new GameState(
            grid, territories, players,
            new TurnState(players, 0, turnNumber),
            new Treasury(), waterCoords: water, mode: mode);
    }

    private static AiAction? Choose(GameState state, int seed = 7) =>
        VikingAi.ChooseNext(
            state, new HashSet<HexCoord>(), new DeterministicRng(seed),
            new HashSet<HexCoord>());

    // --- sequencer phase 1: disembark ------------------------------------------

    [Fact]
    public void ChooseNext_DisembarksBeforeLandMoves()
    {
        // A sea viking from an earlier round AND a landed viking with a free
        // capture — the disembark must come first.
        HexGrid grid = TestHelpers.BuildRectGrid(3, 3, Red);
        HexCoord sea = HexCoord.FromOffset(3, 1);
        grid.Get(HexCoord.FromOffset(0, 0))!.Owner = PlayerId.None;
        grid.Get(HexCoord.FromOffset(0, 0))!.Occupant = new Unit(PlayerId.None, UnitLevel.Captain);
        GameState state = MakeState(grid, new HashSet<HexCoord> { sea });
        state.Vikings.AddAtSea(new SeaViking(sea, UnitLevel.Captain));
        state.Vikings.LastSpawnRound = 3; // spawned last round; current turn = 4

        AiAction? action = Choose(state);

        VikingDisembarkAction disembark = Assert.IsType<VikingDisembarkAction>(action);
        Assert.Equal(sea, disembark.Sea);
        Assert.Equal(HexCoord.FromOffset(2, 1), disembark.Land);
    }

    [Fact]
    public void ChooseNext_Disembark_PrefersCapturingPlayerLand()
    {
        // Water at offset (3,0) touches two land tiles: (2,0) stays Red,
        // (2,1) is made neutral-empty. Capturing enemy land scores higher
        // than stepping onto already-neutral ground.
        HexGrid grid = TestHelpers.BuildRectGrid(3, 3, Red);
        HexCoord sea = HexCoord.FromOffset(3, 0);
        HexCoord redTile = HexCoord.FromOffset(2, 0);
        HexCoord neutralTile = HexCoord.FromOffset(2, 1);
        grid.Get(neutralTile)!.Owner = PlayerId.None;
        GameState state = MakeState(grid, new HashSet<HexCoord> { sea });
        Assert.Equal(
            new[] { redTile, neutralTile }.OrderBy(c => c).ToList(),
            sea.Neighbors().Where(n => grid.Contains(n)).OrderBy(c => c).ToList());
        state.Vikings.AddAtSea(new SeaViking(sea, UnitLevel.Soldier));
        state.Vikings.LastSpawnRound = 3;

        AiAction? action = Choose(state);

        VikingDisembarkAction disembark = Assert.IsType<VikingDisembarkAction>(action);
        Assert.Equal(redTile, disembark.Land);
    }

    [Fact]
    public void ChooseNext_PerishesWhenEveryLandingBlocked()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(3, 3, Red);
        HexCoord sea = HexCoord.FromOffset(3, 1);
        HexCoord landing = HexCoord.FromOffset(2, 1);
        grid.Get(landing)!.Occupant = new Unit(Red, UnitLevel.Commander); // defense 4
        GameState state = MakeState(grid, new HashSet<HexCoord> { sea });
        state.Vikings.AddAtSea(new SeaViking(sea, UnitLevel.Captain));
        state.Vikings.LastSpawnRound = 3;

        AiAction? action = Choose(state);

        VikingPerishAtSeaAction perish = Assert.IsType<VikingPerishAtSeaAction>(action);
        Assert.Equal(sea, perish.Sea);
    }

    [Fact]
    public void ChooseNext_FreshSpawns_DoNotDisembark()
    {
        // The wave spawned THIS round — it waits (one round of warning).
        HexGrid grid = TestHelpers.BuildRectGrid(3, 3, Red);
        HexCoord sea = HexCoord.FromOffset(3, 1);
        GameState state = MakeState(grid, new HashSet<HexCoord> { sea }, turnNumber: 3);
        state.Vikings.AddAtSea(new SeaViking(sea, UnitLevel.Recruit));
        state.Vikings.NextWaveIndex = 1;
        state.Vikings.LastSpawnRound = 3;

        Assert.Null(Choose(state));
    }

    // --- sequencer phase 2: landed moves ---------------------------------------

    [Fact]
    public void ChooseNext_LandedVikingCaptures_WhenSeaIsEmpty()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(3, 3, Red);
        HexCoord vikingTile = HexCoord.FromOffset(0, 0);
        grid.Get(vikingTile)!.Owner = PlayerId.None;
        // Aggro: a landed WAVE raider (they come ashore hostile). Passive
        // barbarians wander instead — see the #188 tests below.
        grid.Get(vikingTile)!.Occupant =
            new Unit(PlayerId.None, UnitLevel.Soldier) { IsAggro = true };
        GameState state = MakeState(grid);
        state.Vikings.NextWaveIndex = VikingRaidersRules.TotalWaves;

        AiAction? action = Choose(state);

        AiMoveAction move = Assert.IsType<AiMoveAction>(action);
        Assert.Equal(vikingTile, move.Source);
        Assert.Equal(Red, /* captured tile was Red before the move */
            state.Grid.Get(move.Destination)!.Owner);
    }

    // --- raiders outside Viking Raiders mode ------------------------------
    // An authored map can seed raiders on any mode. An AGGRO raider acts
    // exactly as a landed wave raider does (there is just no wave schedule
    // to advance); passive barbarians only wander — see the #188 tests.

    [Fact]
    public void ChooseNext_FreeformLandedRaider_StillCaptures()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(3, 3, Red);
        HexCoord vikingTile = HexCoord.FromOffset(0, 0);
        grid.Get(vikingTile)!.Owner = PlayerId.None;
        grid.Get(vikingTile)!.Occupant =
            new Unit(PlayerId.None, UnitLevel.Soldier) { IsAggro = true };
        GameState state = MakeState(grid, mode: GameMode.Freeform);

        AiAction? action = Choose(state);

        AiMoveAction move = Assert.IsType<AiMoveAction>(action);
        Assert.Equal(vikingTile, move.Source);
    }

    [Fact]
    public void ChooseNext_FreeformNeverSpawnsWaves()
    {
        // Round 4 with NextWaveIndex 0 would be wave-due in Viking Raiders,
        // and the map has coastal water to spawn onto — but Freeform has no
        // schedule, so the turn simply ends.
        HexGrid grid = TestHelpers.BuildRectGrid(3, 3, Red);
        var water = new HashSet<HexCoord>
        {
            HexCoord.FromOffset(3, 0),
            HexCoord.FromOffset(3, 1),
            HexCoord.FromOffset(3, 2),
        };
        GameState state = MakeState(grid, water, mode: GameMode.Freeform);

        Assert.Null(Choose(state));
        Assert.Equal(0, state.Vikings.NextWaveIndex);
    }

    [Fact]
    public void ChooseNext_FreeformIgnoresRaidersAtSea()
    {
        // Sea raiders can only exist in Viking Raiders (nothing spawns them
        // elsewhere); if one is somehow present it must not disembark here.
        HexGrid grid = TestHelpers.BuildRectGrid(3, 3, Red);
        HexCoord sea = HexCoord.FromOffset(3, 1);
        GameState state = MakeState(
            grid, new HashSet<HexCoord> { sea }, mode: GameMode.Freeform);
        state.Vikings.AddAtSea(new SeaViking(sea, UnitLevel.Soldier));
        state.Vikings.LastSpawnRound = 0;

        Assert.Null(Choose(state));
    }

    // --- sequencer phase 3: spawn last ------------------------------------------

    [Fact]
    public void ChooseNext_SpawnsWave_WhenDueAndNothingElseToDo()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(3, 3, Red);
        var water = new HashSet<HexCoord>
        {
            HexCoord.FromOffset(3, 0),
            HexCoord.FromOffset(3, 1),
            HexCoord.FromOffset(3, 2),
            HexCoord.FromOffset(10, 10), // open sea — never a spawn site
        };
        GameState state = MakeState(grid, water, turnNumber: 3);

        AiAction? action = Choose(state);

        VikingSpawnWaveAction spawn = Assert.IsType<VikingSpawnWaveAction>(action);
        Assert.Equal(0, spawn.WaveIndex);
        // Wave 0 is 5 Soldiers + 5 Recruits (strongest first), clamped to
        // this map's 3 coastal coords — so 3 Soldiers spawn.
        Assert.Equal(3, spawn.Spawns.Count);
        IReadOnlyList<HexCoord> coastal = VikingRaidersRules.CoastalWaterCoords(state);
        Assert.All(spawn.Spawns, s =>
        {
            Assert.Contains(s.Coord, coastal);
            Assert.Equal(UnitLevel.Soldier, s.Level);
        });
    }

    [Fact]
    public void ChooseNext_MissedWave_CatchesUpNextRound()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(3, 3, Red);
        var water = new HashSet<HexCoord> { HexCoord.FromOffset(3, 1) };
        GameState state = MakeState(grid, water, turnNumber: 4); // wave 0 was due round 3

        AiAction? action = Choose(state);

        VikingSpawnWaveAction spawn = Assert.IsType<VikingSpawnWaveAction>(action);
        Assert.Equal(0, spawn.WaveIndex);
    }

    [Fact]
    public void ChooseNext_NothingToDo_ReturnsNull()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(3, 3, Red);
        var water = new HashSet<HexCoord> { HexCoord.FromOffset(3, 1) };
        GameState state = MakeState(grid, water, turnNumber: 4);
        state.Vikings.NextWaveIndex = 1; // wave 1 not due until round 6

        Assert.Null(Choose(state));
    }

    // --- ComputerAi adaptation ----------------------------------------------------

    [Fact]
    public void ComputerAi_NeutralOwner_CapturesFromCapitalLessTerritory()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(3, 3, Red);
        HexCoord vikingTile = HexCoord.FromOffset(0, 0);
        grid.Get(vikingTile)!.Owner = PlayerId.None;
        grid.Get(vikingTile)!.Occupant = new Unit(PlayerId.None, UnitLevel.Soldier);
        GameState state = MakeState(grid);

        AiAction? action = ComputerAi.ChooseNextAction(
            state, PlayerId.None, new HashSet<HexCoord>(), new HashSet<HexCoord>(), new DeterministicRng(7));

        AiMoveAction move = Assert.IsType<AiMoveAction>(action);
        Assert.Equal(vikingTile, move.Source);
        Assert.Equal(Red, state.Grid.Get(move.Destination)!.Owner);
    }

    [Fact]
    public void ComputerAi_NeutralOwner_NeverChopsOwnTrees()
    {
        // An all-neutral island: a viking, an own tree, empty tiles — no
        // enemy anywhere. The ordinary AI would chop; vikings must not
        // (no upkeep → own trees are harmless).
        HexGrid grid = TestHelpers.BuildRectGrid(3, 1, PlayerId.None);
        grid.Get(HexCoord.FromOffset(0, 0))!.Occupant = new Unit(PlayerId.None, UnitLevel.Soldier);
        grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Tree();
        GameState state = MakeState(grid);

        AiAction? action = ComputerAi.ChooseNextAction(
            state, PlayerId.None, new HashSet<HexCoord>(), new HashSet<HexCoord>(), new DeterministicRng(7));

        Assert.Null(action);
    }

    /// <summary>
    /// A 7x1 strip: columns 0-4 owned by <paramref name="stripOwner"/> with a
    /// recruit at the far interior end, columns 5-6 Red with a Commander
    /// guarding the shared border so no capture is legal. The strip's border
    /// tile (4,0) is an undefended legal phase-4b reposition target (the
    /// owner's capital, when one exists, reconciles onto the lex-min empty
    /// tile (1,0) — its defense radiation doesn't reach the border).
    /// </summary>
    private static HexGrid BuildRepositionStrip(PlayerId stripOwner)
    {
        HexGrid grid = TestHelpers.BuildRectGrid(7, 1, Red);
        for (int col = 0; col <= 4; col++)
            grid.Get(HexCoord.FromOffset(col, 0))!.Owner = stripOwner;
        grid.Get(HexCoord.FromOffset(0, 0))!.Occupant = new Unit(stripOwner, UnitLevel.Recruit);
        grid.Get(HexCoord.FromOffset(5, 0))!.Occupant = new Unit(Red, UnitLevel.Commander);
        return grid;
    }

    [Fact]
    public void ComputerAi_NeutralOwner_HoldsInsteadOfRepositioning()
    {
        // No capturable target and a legal phase-4b reposition onto the
        // strip's border: the viking must HOLD (null) — the raiding force
        // never makes a defensive-only move. The mirror test below proves a
        // real player in the same position repositions, so it is the IsNone
        // gate (not candidate enumeration) that stops the viking.
        GameState state = MakeState(BuildRepositionStrip(PlayerId.None));

        AiAction? action = ComputerAi.ChooseNextAction(
            state, PlayerId.None, new HashSet<HexCoord>(), new HashSet<HexCoord>(), new DeterministicRng(7));

        Assert.Null(action);
    }

    [Fact]
    public void ComputerAi_RealPlayer_InVikingPosition_RepositionsToDefendBorder()
    {
        // The same board with the strip owned by Blue (capital placed by
        // territory reconciliation): phase 4b runs and mans the border.
        GameState state = MakeState(BuildRepositionStrip(Blue));

        AiAction? action = ComputerAi.ChooseNextAction(
            state, Blue, new HashSet<HexCoord>(), new HashSet<HexCoord>(), new DeterministicRng(7));

        AiMoveAction move = Assert.IsType<AiMoveAction>(action);
        Assert.Equal(HexCoord.FromOffset(0, 0), move.Source);
        Assert.Equal(HexCoord.FromOffset(4, 0), move.Destination);
    }

    // --- non-aggro barbarian wander (#188) -------------------------------------

    /// <summary>
    /// 4x1 strip: (0,0)/(1,0) neutral with a barbarian Soldier at (0,0),
    /// (2,0)/(3,0) Red — (2,0), the only tile bordering the barbarian
    /// territory, is under capital-grade defense only, so the Soldier has
    /// a legal, tempting capture right next door.
    /// </summary>
    private static HexGrid BuildPassiveBarbarianBoard(bool aggro = false)
    {
        HexGrid grid = TestHelpers.BuildRectGrid(4, 1, Red);
        grid.Get(HexCoord.FromOffset(0, 0))!.Owner = PlayerId.None;
        grid.Get(HexCoord.FromOffset(1, 0))!.Owner = PlayerId.None;
        grid.Get(HexCoord.FromOffset(0, 0))!.Occupant =
            new Unit(PlayerId.None, UnitLevel.Soldier) { IsAggro = aggro };
        return grid;
    }

    [Fact]
    public void ChooseNext_PassiveBarbarians_WanderInsteadOfCapturing()
    {
        // A passive barbarian never expands: for every seed the action is
        // either an in-territory reposition to (1,0) or a hold (null) —
        // never the undefended Red capture. Both wander outcomes must
        // actually occur across seeds (the "may hold" draw).
        int moves = 0;
        int holds = 0;
        for (int seed = 0; seed < 20; seed++)
        {
            GameState state = MakeState(
                BuildPassiveBarbarianBoard(), mode: GameMode.Freeform);
            AiAction? action = Choose(state, seed);
            if (action == null)
            {
                holds++;
                continue;
            }
            AiMoveAction move = Assert.IsType<AiMoveAction>(action);
            Assert.Equal(HexCoord.FromOffset(0, 0), move.Source);
            Assert.Equal(HexCoord.FromOffset(1, 0), move.Destination);
            moves++;
        }
        Assert.True(moves > 0, "no seed produced a wander move");
        Assert.True(holds > 0, "no seed produced a hold");
    }

    [Fact]
    public void ChooseNext_AggroBarbarians_StillExpand()
    {
        // The same board with the flag set runs today's expansion path.
        GameState state = MakeState(
            BuildPassiveBarbarianBoard(aggro: true), mode: GameMode.Freeform);

        AiMoveAction move = Assert.IsType<AiMoveAction>(Choose(state));

        Assert.Equal(HexCoord.FromOffset(2, 0), move.Destination);
    }

    [Fact]
    public void ChooseNext_PassiveWander_IsDeterministicPerSeed()
    {
        for (int seed = 0; seed < 10; seed++)
        {
            GameState a = MakeState(
                BuildPassiveBarbarianBoard(), mode: GameMode.Freeform);
            GameState b = MakeState(
                BuildPassiveBarbarianBoard(), mode: GameMode.Freeform);
            Assert.Equal(Choose(a, seed), Choose(b, seed));
        }
    }

    [Fact]
    public void ChooseNext_WanderGuard_OneBeatPerUnitPerTurn()
    {
        // The per-turn guard set caps each passive unit at one wander beat;
        // without it a reposition (which never sets HasMovedThisTurn) would
        // be re-chosen until the driver's 64-step backstop.
        var visited = new HashSet<HexCoord>();
        var wandered = new HashSet<HexCoord>();
        bool sawMove = false;
        for (int seed = 0; seed < 20 && !sawMove; seed++)
        {
            visited.Clear();
            wandered.Clear();
            GameState state = MakeState(
                BuildPassiveBarbarianBoard(), mode: GameMode.Freeform);
            AiAction? action = VikingAi.ChooseNext(
                state, visited, new DeterministicRng(seed), wandered);
            if (action is not AiMoveAction move) continue;
            sawMove = true;

            // Apply the reposition the way ExecuteVikingMove would.
            state.Grid.Get(move.Destination)!.Occupant =
                state.Grid.Get(move.Source)!.Occupant;
            state.Grid.Get(move.Source)!.Occupant = null;

            // Every follow-up call this turn holds: the unit already moved.
            for (int next = 0; next < 3; next++)
            {
                Assert.Null(VikingAi.ChooseNext(
                    state, visited, new DeterministicRng(seed), wandered));
            }
        }
        Assert.True(sawMove, "no seed produced a wander move to guard");
    }

    [Fact]
    public void ChooseNext_TideDoomedPassiveUnit_AlwaysRetreats()
    {
        // Rising Tides: the barbarian's tile is forecast to sink but the
        // territory still has dry ground — for EVERY seed the unit flees
        // (no hold slot for a doomed unit, never onto a doomed tile).
        for (int seed = 0; seed < 20; seed++)
        {
            GameState state = MakeState(
                BuildPassiveBarbarianBoard(), mode: GameMode.RisingTides);
            state.PendingTide = new[] { new TideStep(HexCoord.FromOffset(0, 0), false) };

            AiAction? action = Choose(state, seed);

            AiMoveAction move = Assert.IsType<AiMoveAction>(action);
            Assert.Equal(HexCoord.FromOffset(0, 0), move.Source);
            Assert.Equal(HexCoord.FromOffset(1, 0), move.Destination);
        }
    }

    [Fact]
    public void ChooseNext_PassiveBarbarians_WanderInVikingRaidersToo()
    {
        // Map-authored barbarians stay passive even in Viking Raiders (wave
        // raiders spawn aggro; these were never provoked). Schedule
        // exhausted so no spawn beat can mask the wander/hold outcome.
        for (int seed = 0; seed < 20; seed++)
        {
            GameState state = MakeState(BuildPassiveBarbarianBoard());
            state.Vikings.NextWaveIndex = VikingRaidersRules.TotalWaves;

            AiAction? action = Choose(state, seed);

            if (action == null) continue;
            AiMoveAction move = Assert.IsType<AiMoveAction>(action);
            Assert.Equal(HexCoord.FromOffset(1, 0), move.Destination);
        }
    }

    // --- AiStateScorer adaptation ---------------------------------------------------

    [Fact]
    public void Scorer_NeutralUnits_CountForNeutralPerspective()
    {
        // All-neutral 2-tile island, no borders: adding a viking unit must
        // raise the neutral-perspective score (units are worth keeping alive),
        // which requires the bankruptcy zeroing to skip neutral territories.
        HexGrid grid = TestHelpers.BuildRectGrid(2, 1, PlayerId.None);
        GameState state = MakeState(grid);
        int before = AiStateScorer.Score(state, PlayerId.None);

        grid.Get(HexCoord.FromOffset(0, 0))!.Occupant = new Unit(PlayerId.None, UnitLevel.Captain);
        int after = AiStateScorer.Score(state, PlayerId.None);

        Assert.True(after > before, $"expected unit to add value: before={before} after={after}");
    }

    [Fact]
    public void Scorer_NeutralUnits_ReadAsThreatsToPlayers()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(2, 1, PlayerId.None);
        GameState state = MakeState(grid);
        int before = AiStateScorer.Score(state, Red);

        grid.Get(HexCoord.FromOffset(0, 0))!.Occupant = new Unit(PlayerId.None, UnitLevel.Captain);
        int after = AiStateScorer.Score(state, Red);

        Assert.True(after < before, $"expected viking to read as threat: before={before} after={after}");
    }

    [Fact]
    public void Scorer_NeutralPerspective_NoOwnTreePenalty()
    {
        // A tree on neutral land costs the neutral perspective exactly what
        // it credits an enemy perspective (the lost income component) — no
        // extra own-tree penalty, because upkeep-free vikings don't care.
        HexGrid grid = TestHelpers.BuildRectGrid(4, 1, Red);
        grid.Get(HexCoord.FromOffset(0, 0))!.Owner = PlayerId.None;
        grid.Get(HexCoord.FromOffset(1, 0))!.Owner = PlayerId.None;
        GameState state = MakeState(grid);
        int noneBefore = AiStateScorer.Score(state, PlayerId.None);
        int redBefore = AiStateScorer.Score(state, Red);

        grid.Get(HexCoord.FromOffset(0, 0))!.Occupant = new Tree();
        int noneAfter = AiStateScorer.Score(state, PlayerId.None);
        int redAfter = AiStateScorer.Score(state, Red);

        Assert.Equal(redAfter - redBefore, noneBefore - noneAfter);
    }
}
