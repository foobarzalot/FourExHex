using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

public class VikingRaidersRulesTests
{
    private static readonly PlayerId Red = PlayerId.FromIndex(0);
    private static readonly PlayerId Blue = PlayerId.FromIndex(1);

    private static GameState MakeState(
        HexGrid grid,
        IReadOnlyList<Territory> territories,
        IReadOnlySet<HexCoord>? water = null,
        GameMode mode = GameMode.VikingRaiders)
    {
        var players = new List<Player>
        {
            new Player("Red", Red),
            new Player("Blue", Blue),
        };
        return new GameState(
            grid, territories, players, new TurnState(players), new Treasury(),
            waterCoords: water, mode: mode);
    }

    /// <summary>3×3 Red board with the given water coords.</summary>
    private static GameState MakeIslandState(params HexCoord[] water)
    {
        HexGrid grid = TestHelpers.BuildRectGrid(3, 3, Red);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        return MakeState(grid, territories, water.ToHashSet());
    }

    // --- CoastalWaterCoords ------------------------------------------------

    [Fact]
    public void CoastalWaterCoords_KeepsOnlyWaterTouchingLand_SortedAscending()
    {
        HexCoord adjacentA = HexCoord.FromOffset(3, 0);
        HexCoord adjacentB = HexCoord.FromOffset(3, 1);
        HexCoord farOut = HexCoord.FromOffset(10, 10);
        GameState state = MakeIslandState(adjacentA, adjacentB, farOut);

        // Preconditions: the two near coords really touch the grid; the far
        // one really doesn't.
        Assert.Contains(adjacentA.Neighbors(), n => state.Grid.Contains(n));
        Assert.Contains(adjacentB.Neighbors(), n => state.Grid.Contains(n));
        Assert.DoesNotContain(farOut.Neighbors(), n => state.Grid.Contains(n));

        IReadOnlyList<HexCoord> coastal = VikingRaidersRules.CoastalWaterCoords(state);

        Assert.Equal(new[] { adjacentA, adjacentB }.OrderBy(c => c).ToList(), coastal);
    }

    [Fact]
    public void CoastalWaterCoords_NoWater_IsEmpty()
    {
        GameState state = MakeIslandState();
        Assert.Empty(VikingRaidersRules.CoastalWaterCoords(state));
    }

    // --- WaveIndexForRound ---------------------------------------------------

    [Theory]
    [InlineData(0, null)]
    [InlineData(1, null)]
    [InlineData(2, null)]
    [InlineData(3, 0)]
    [InlineData(4, null)]
    [InlineData(5, null)]
    [InlineData(6, 1)]
    [InlineData(9, 2)]
    [InlineData(12, 3)]
    [InlineData(15, 4)]
    [InlineData(18, 5)]
    [InlineData(21, null)] // schedule exhausted
    [InlineData(24, null)]
    public void WaveIndexForRound_FollowsFixedSchedule(int round, int? expected)
    {
        Assert.Equal(expected, VikingRaidersRules.WaveIndexForRound(round));
    }

    // --- WaveComposition -----------------------------------------------------

    // The fixed per-wave table (user-tuned): Recruits pinned at 5,
    // Soldiers +1 per wave until Captains take over the increment at
    // wave 5 (index 4). Totals run 10 → 15.
    [Theory]
    [InlineData(0, 5, 5, 0)]
    [InlineData(1, 5, 6, 0)]
    [InlineData(2, 5, 7, 0)]
    [InlineData(3, 5, 8, 0)]
    [InlineData(4, 5, 8, 1)]
    [InlineData(5, 5, 8, 2)]
    public void WaveComposition_FollowsFixedTable(
        int wave, int recruits, int soldiers, int captains)
    {
        IReadOnlyList<UnitLevel> comp = VikingRaidersRules.WaveComposition(wave);
        Assert.Equal(recruits, comp.Count(l => l == UnitLevel.Recruit));
        Assert.Equal(soldiers, comp.Count(l => l == UnitLevel.Soldier));
        Assert.Equal(captains, comp.Count(l => l == UnitLevel.Captain));
        Assert.Equal(recruits + soldiers + captains, comp.Count);
        Assert.DoesNotContain(UnitLevel.Commander, comp);
    }

    [Fact]
    public void WaveComposition_StrongestFirst()
    {
        // Placement is sequential and the first raiders claim the best
        // landing spots, so the composition lists strongest levels first.
        IReadOnlyList<UnitLevel> comp = VikingRaidersRules.WaveComposition(5);
        Assert.Equal(comp.OrderByDescending(l => l).ToList(), comp);
    }

    // --- ChooseSpawns ---------------------------------------------------------

    /// <summary>An island whose whole east flank (col 3) is coastal water.</summary>
    private static GameState MakeSpawnState()
    {
        return MakeIslandState(
            HexCoord.FromOffset(3, 0),
            HexCoord.FromOffset(3, 1),
            HexCoord.FromOffset(3, 2),
            HexCoord.FromOffset(10, 10)); // far water — never a spawn
    }

    [Fact]
    public void ChooseSpawns_DeterministicInSeed()
    {
        GameState state = MakeSpawnState();
        var comp = new List<UnitLevel> { UnitLevel.Recruit, UnitLevel.Soldier };

        IReadOnlyList<SeaViking> a = VikingRaidersRules.ChooseSpawns(state, comp, new Random(7));
        IReadOnlyList<SeaViking> b = VikingRaidersRules.ChooseSpawns(state, comp, new Random(7));

        Assert.Equal(a, b);
    }

    [Fact]
    public void ChooseSpawns_OnePerCoord_OnCoastalWater_SortedByCoord()
    {
        GameState state = MakeSpawnState();
        var comp = new List<UnitLevel> { UnitLevel.Recruit, UnitLevel.Recruit, UnitLevel.Recruit };
        IReadOnlyList<HexCoord> coastal = VikingRaidersRules.CoastalWaterCoords(state);

        IReadOnlyList<SeaViking> spawns = VikingRaidersRules.ChooseSpawns(state, comp, new Random(7));

        Assert.Equal(comp.Count, spawns.Count);
        Assert.Equal(spawns.Count, spawns.Select(s => s.Coord).Distinct().Count());
        Assert.All(spawns, s => Assert.Contains(s.Coord, coastal));
        Assert.Equal(spawns.OrderBy(s => s.Coord).ToList(), spawns);
    }

    [Fact]
    public void ChooseSpawns_SkipsCoordsAlreadyHoldingASeaViking()
    {
        GameState state = MakeSpawnState();
        HexCoord occupied = HexCoord.FromOffset(3, 1);
        state.Vikings.AddAtSea(new SeaViking(occupied, UnitLevel.Recruit));
        var comp = new List<UnitLevel> { UnitLevel.Recruit, UnitLevel.Recruit, UnitLevel.Recruit };

        IReadOnlyList<SeaViking> spawns = VikingRaidersRules.ChooseSpawns(state, comp, new Random(7));

        Assert.Equal(2, spawns.Count); // 3 coastal coords, 1 occupied
        Assert.DoesNotContain(spawns, s => s.Coord == occupied);
    }

    [Fact]
    public void ChooseSpawns_ClampsToAvailableCoords()
    {
        GameState state = MakeSpawnState();
        var comp = Enumerable.Repeat(UnitLevel.Recruit, 10).ToList();

        IReadOnlyList<SeaViking> spawns = VikingRaidersRules.ChooseSpawns(state, comp, new Random(7));

        Assert.Equal(3, spawns.Count);
    }

    [Fact]
    public void ChooseSpawns_CarriesCompositionLevels()
    {
        GameState state = MakeSpawnState();
        var comp = new List<UnitLevel> { UnitLevel.Captain, UnitLevel.Soldier, UnitLevel.Recruit };

        IReadOnlyList<SeaViking> spawns = VikingRaidersRules.ChooseSpawns(state, comp, new Random(7));

        Assert.Equal(
            comp.OrderBy(l => l).ToList(),
            spawns.Select(s => s.Level).OrderBy(l => l).ToList());
    }

    // --- ChooseSpawns: landing-party placement -----------------------------------

    /// <summary>
    /// A straight beach: land row 1 (cols 0..9), plus the given water
    /// coords on row 0 (or beyond the ends). A row-0 water coord at col c
    /// has land neighbours (c,1) and (c−1,1), so interior waters see 2
    /// landings and (10,0) sees 1. Beware the WEST end: the strip's
    /// capital sits at lex-min (0,1), whose defense blocks Recruit
    /// landings there — (0,0) is 0-viable for a Recruit.
    /// </summary>
    private static GameState MakeBeachState(params HexCoord[] water)
    {
        var grid = new HexGrid();
        for (int col = 0; col < 10; col++)
        {
            grid.Add(new HexTile(HexCoord.FromOffset(col, 1), Red));
        }
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        return MakeState(grid, territories, water.ToHashSet());
    }

    [Fact]
    public void ChooseSpawns_PrefersCoordWithMostViableLandings()
    {
        // Candidates: one interior water (2 viable landings) among three
        // weaker ones (1, 1, and 0) — the wave must anchor on the interior.
        HexCoord best = HexCoord.FromOffset(4, 0);
        GameState state = MakeBeachState(
            best,
            HexCoord.FromOffset(0, 0),
            HexCoord.FromOffset(10, 0),
            HexCoord.FromOffset(-1, 0));
        Assert.Equal(2, VikingRaidersRules.DisembarkTargets(state, best, UnitLevel.Recruit).Count);

        // Every seed must anchor on the interior water — viability is a
        // rule, not a lucky draw.
        for (int seed = 1; seed <= 5; seed++)
        {
            IReadOnlyList<SeaViking> spawns = VikingRaidersRules.ChooseSpawns(
                state, new List<UnitLevel> { UnitLevel.Recruit }, new Random(seed));
            Assert.Equal(best, Assert.Single(spawns).Coord);
        }
    }

    [Fact]
    public void ChooseSpawns_ViabilityIsJudgedAtTheRaidersOwnLevel()
    {
        // The interior water's two landing tiles carry Soldiers (defense 2):
        // worthless to a Recruit (0 viable — it must take an end water with
        // 1 undefended landing), but the best spot for a Captain (2 viable).
        HexCoord defended = HexCoord.FromOffset(4, 0);
        GameState state = MakeBeachState(
            defended,
            HexCoord.FromOffset(0, 0),
            HexCoord.FromOffset(10, 0));
        state.Grid.Get(HexCoord.FromOffset(4, 1))!.Occupant = new Unit(Red, UnitLevel.Soldier);
        state.Grid.Get(HexCoord.FromOffset(3, 1))!.Occupant = new Unit(Red, UnitLevel.Soldier);
        Assert.Equal(0, VikingRaidersRules.DisembarkTargets(state, defended, UnitLevel.Recruit).Count);
        Assert.Equal(2, VikingRaidersRules.DisembarkTargets(state, defended, UnitLevel.Captain).Count);

        IReadOnlyList<SeaViking> recruitSpawn = VikingRaidersRules.ChooseSpawns(
            state, new List<UnitLevel> { UnitLevel.Recruit }, new Random(7));
        IReadOnlyList<SeaViking> captainSpawn = VikingRaidersRules.ChooseSpawns(
            state, new List<UnitLevel> { UnitLevel.Captain }, new Random(7));

        Assert.NotEqual(defended, Assert.Single(recruitSpawn).Coord);
        Assert.Equal(defended, Assert.Single(captainSpawn).Coord);
    }

    [Fact]
    public void ChooseSpawns_ClustersWave_WithoutOverlappingLandingZones()
    {
        // A long straight beach: land row 1 (cols 0..9), water row 0 above
        // it. Interior water coords tie on viability, so cohesion decides:
        // the second raider spawns as close as possible to the first while
        // keeping their landing sets disjoint — exactly 2 columns away on a
        // straight coast (1 away would share a landing tile).
        var grid = new HexGrid();
        for (int col = 0; col < 10; col++)
        {
            grid.Add(new HexTile(HexCoord.FromOffset(col, 1), Red));
        }
        var water = new HashSet<HexCoord>();
        for (int col = 0; col < 10; col++)
        {
            water.Add(HexCoord.FromOffset(col, 0));
        }
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        GameState state = MakeState(grid, territories, water);

        IReadOnlyList<SeaViking> spawns = VikingRaidersRules.ChooseSpawns(
            state, new List<UnitLevel> { UnitLevel.Recruit, UnitLevel.Recruit }, new Random(7));

        Assert.Equal(2, spawns.Count);
        var landingsA = VikingRaidersRules.DisembarkTargets(
            state, spawns[0].Coord, spawns[0].Level).ToHashSet();
        var landingsB = VikingRaidersRules.DisembarkTargets(
            state, spawns[1].Coord, spawns[1].Level).ToHashSet();
        Assert.Empty(landingsA.Intersect(landingsB));
        Assert.Equal(2, HexCoord.Distance(spawns[0].Coord, spawns[1].Coord));
    }

    [Fact]
    public void ChooseSpawns_OverlapIsLastResort_EvenAtLowerViability()
    {
        // Waters A=(4,0) and B=(5,0) are the richest spots (2 landings
        // each) but share the landing tile (4,1); C=(10,0) has only 1
        // landing but overlaps nobody. A wave of two must take one of the
        // rich spots and then C — never the A+B pair.
        HexCoord a = HexCoord.FromOffset(4, 0);
        HexCoord b = HexCoord.FromOffset(5, 0);
        HexCoord c = HexCoord.FromOffset(10, 0);
        GameState state = MakeBeachState(a, b, c);
        Assert.Equal(
            1, VikingRaidersRules.DisembarkTargets(state, c, UnitLevel.Recruit).Count);
        Assert.Contains(
            HexCoord.FromOffset(4, 1),
            VikingRaidersRules.DisembarkTargets(state, a, UnitLevel.Recruit));
        Assert.Contains(
            HexCoord.FromOffset(4, 1),
            VikingRaidersRules.DisembarkTargets(state, b, UnitLevel.Recruit));

        for (int seed = 1; seed <= 5; seed++)
        {
            IReadOnlyList<SeaViking> spawns = VikingRaidersRules.ChooseSpawns(
                state,
                new List<UnitLevel> { UnitLevel.Recruit, UnitLevel.Recruit },
                new Random(seed));
            Assert.Contains(spawns, s => s.Coord == c);
        }
    }

    [Fact]
    public void ChooseSpawns_OverlappingLandableSpot_StillBeatsCertainDeath()
    {
        // The only non-overlapping alternative has NO viable landing at all
        // ((-1,0) touches no land) — a guaranteed perish. An overlapping but
        // landable spot is preferable to that.
        HexCoord a = HexCoord.FromOffset(4, 0);
        HexCoord b = HexCoord.FromOffset(5, 0);
        HexCoord dead = HexCoord.FromOffset(-1, 0);
        GameState state = MakeBeachState(a, b, dead);
        _ = dead; // not coastal (no land neighbour) — never even a candidate

        IReadOnlyList<SeaViking> spawns = VikingRaidersRules.ChooseSpawns(
            state,
            new List<UnitLevel> { UnitLevel.Recruit, UnitLevel.Recruit },
            new Random(7));

        Assert.Equal(2, spawns.Count);
        Assert.Contains(spawns, s => s.Coord == a);
        Assert.Contains(spawns, s => s.Coord == b);
    }

    [Fact]
    public void ChooseSpawns_AllowsOverlap_WhenUnavoidable()
    {
        // A single land tile with two adjacent water coords: both raiders'
        // landing sets are the same one tile. Overlap is unavoidable — the
        // wave must still spawn in full rather than hold raiders back.
        var grid = new HexGrid();
        grid.Add(new HexTile(HexCoord.FromOffset(1, 1), Red));
        var water = new HashSet<HexCoord>
        {
            HexCoord.FromOffset(0, 1),
            HexCoord.FromOffset(2, 1),
        };
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        GameState state = MakeState(grid, territories, water);

        IReadOnlyList<SeaViking> spawns = VikingRaidersRules.ChooseSpawns(
            state, new List<UnitLevel> { UnitLevel.Recruit, UnitLevel.Recruit }, new Random(7));

        Assert.Equal(2, spawns.Count);
    }

    // --- DisembarkTargets -------------------------------------------------------

    /// <summary>
    /// 3×3 board with water at offset (3,1). Precondition asserted per test:
    /// that water coord has exactly one grid neighbour, offset (2,1), so the
    /// tests fully control the single candidate tile.
    /// </summary>
    private static (GameState state, HexCoord sea, HexCoord landing) MakeDisembarkState()
    {
        HexCoord sea = HexCoord.FromOffset(3, 1);
        HexCoord landing = HexCoord.FromOffset(2, 1);
        HexGrid grid = TestHelpers.BuildRectGrid(3, 3, Red);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        GameState state = MakeState(grid, territories, new HashSet<HexCoord> { sea });
        HexCoord[] inGrid = sea.Neighbors().Where(n => state.Grid.Contains(n)).ToArray();
        Assert.Equal(new[] { landing }, inGrid);
        return (state, sea, landing);
    }

    private static void Recompute(GameState state) =>
        state.Territories = TerritoryFinder.Recompute(state.Grid, state.Territories, state.Treasury);

    [Fact]
    public void DisembarkTargets_UndefendedPlayerTile_Included()
    {
        (GameState state, HexCoord sea, HexCoord landing) = MakeDisembarkState();

        IReadOnlyList<HexCoord> targets =
            VikingRaidersRules.DisembarkTargets(state, sea, UnitLevel.Recruit);

        Assert.Equal(new[] { landing }, targets);
    }

    [Fact]
    public void DisembarkTargets_DefendedPlayerTile_RequiresHigherLevel()
    {
        (GameState state, HexCoord sea, HexCoord landing) = MakeDisembarkState();
        state.Grid.Get(landing)!.Occupant = new Unit(Red, UnitLevel.Soldier); // defense 2

        Assert.Empty(VikingRaidersRules.DisembarkTargets(state, sea, UnitLevel.Recruit));
        Assert.Empty(VikingRaidersRules.DisembarkTargets(state, sea, UnitLevel.Soldier));
        Assert.Equal(
            new[] { landing },
            VikingRaidersRules.DisembarkTargets(state, sea, UnitLevel.Captain));
    }

    [Fact]
    public void DisembarkTargets_NeutralEmptyTile_Included_NoDefenseCheck()
    {
        (GameState state, HexCoord sea, HexCoord landing) = MakeDisembarkState();
        state.Grid.Get(landing)!.Owner = PlayerId.None;
        // A landed viking on a neighbouring neutral tile must NOT block the
        // reinforcement landing (no defense check against the vikings' own side).
        HexCoord neighbour = HexCoord.FromOffset(2, 0);
        state.Grid.Get(neighbour)!.Owner = PlayerId.None;
        state.Grid.Get(neighbour)!.Occupant = new Unit(PlayerId.None, UnitLevel.Captain);
        Recompute(state);

        IReadOnlyList<HexCoord> targets =
            VikingRaidersRules.DisembarkTargets(state, sea, UnitLevel.Recruit);

        Assert.Equal(new[] { landing }, targets);
    }

    [Fact]
    public void DisembarkTargets_NeutralTileWithUnitOrTower_Excluded()
    {
        (GameState state, HexCoord sea, HexCoord landing) = MakeDisembarkState();
        state.Grid.Get(landing)!.Owner = PlayerId.None;
        Recompute(state);

        state.Grid.Get(landing)!.Occupant = new Unit(PlayerId.None, UnitLevel.Recruit);
        Assert.Empty(VikingRaidersRules.DisembarkTargets(state, sea, UnitLevel.Captain));

        state.Grid.Get(landing)!.Occupant = new Tower();
        Assert.Empty(VikingRaidersRules.DisembarkTargets(state, sea, UnitLevel.Captain));
    }

    [Fact]
    public void DisembarkTargets_NeutralTileWithTree_Included()
    {
        (GameState state, HexCoord sea, HexCoord landing) = MakeDisembarkState();
        state.Grid.Get(landing)!.Owner = PlayerId.None;
        state.Grid.Get(landing)!.Occupant = new Tree();
        Recompute(state);

        Assert.Equal(
            new[] { landing },
            VikingRaidersRules.DisembarkTargets(state, sea, UnitLevel.Recruit));
    }

    [Fact]
    public void DisembarkTargets_OpenSea_Empty()
    {
        GameState state = MakeIslandState(HexCoord.FromOffset(10, 10));

        Assert.Empty(VikingRaidersRules.DisembarkTargets(
            state, HexCoord.FromOffset(10, 10), UnitLevel.Captain));
    }

    // --- ThreatRemains --------------------------------------------------------

    [Fact]
    public void ThreatRemains_FalseOutsideVikingRaidersMode()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(3, 3, Red);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        GameState state = MakeState(grid, territories, mode: GameMode.Freeform);

        // A fresh VikingState has NextWaveIndex 0 < TotalWaves, but outside
        // the mode that must not read as a live threat.
        Assert.False(VikingRaidersRules.ThreatRemains(state));
    }

    [Fact]
    public void ThreatRemains_TrueWhileWavesPending()
    {
        GameState state = MakeIslandState();
        Assert.True(VikingRaidersRules.ThreatRemains(state));
    }

    [Fact]
    public void ThreatRemains_TrueWhileRaidersAtSea()
    {
        GameState state = MakeIslandState();
        state.Vikings.NextWaveIndex = VikingRaidersRules.TotalWaves;
        state.Vikings.AddAtSea(new SeaViking(HexCoord.FromOffset(3, 1), UnitLevel.Recruit));

        Assert.True(VikingRaidersRules.ThreatRemains(state));
    }

    [Fact]
    public void ThreatRemains_TrueWhileLandedVikingsAlive()
    {
        GameState state = MakeIslandState();
        state.Vikings.NextWaveIndex = VikingRaidersRules.TotalWaves;
        HexCoord landed = HexCoord.FromOffset(1, 1);
        state.Grid.Get(landed)!.Owner = PlayerId.None;
        state.Grid.Get(landed)!.Occupant = new Unit(PlayerId.None, UnitLevel.Soldier);

        Assert.True(VikingRaidersRules.ThreatRemains(state));
    }

    [Fact]
    public void ThreatRemains_FalseWhenScheduleDoneAndAllVikingsDead()
    {
        GameState state = MakeIslandState();
        state.Vikings.NextWaveIndex = VikingRaidersRules.TotalWaves;

        Assert.False(VikingRaidersRules.ThreatRemains(state));
    }

    // --- VikingState ------------------------------------------------------------

    [Fact]
    public void VikingState_AtSea_KeptSortedByCoord()
    {
        var vikings = new VikingState();
        var c1 = HexCoord.FromOffset(3, 2);
        var c2 = HexCoord.FromOffset(3, 0);
        var c3 = HexCoord.FromOffset(3, 1);
        vikings.AddAtSea(new SeaViking(c1, UnitLevel.Recruit));
        vikings.AddAtSea(new SeaViking(c2, UnitLevel.Soldier));
        vikings.AddAtSea(new SeaViking(c3, UnitLevel.Captain));

        Assert.Equal(
            vikings.AtSea.OrderBy(v => v.Coord).ToList(),
            vikings.AtSea.ToList());
        Assert.True(vikings.HasVikingAt(c2));
        Assert.True(vikings.RemoveAtSea(c2));
        Assert.False(vikings.HasVikingAt(c2));
        Assert.False(vikings.RemoveAtSea(c2));
        Assert.Equal(2, vikings.AtSea.Count);
    }

    [Fact]
    public void VikingState_Reset_OverwritesEverything()
    {
        var vikings = new VikingState();
        vikings.AddAtSea(new SeaViking(HexCoord.FromOffset(0, 0), UnitLevel.Recruit));
        vikings.NextWaveIndex = 4;
        vikings.LastCompletedRound = 9;
        vikings.LastSpawnRound = 9;

        vikings.Reset(
            new[] { new SeaViking(HexCoord.FromOffset(5, 5), UnitLevel.Captain) },
            nextWaveIndex: 2, lastCompletedRound: 6, lastSpawnRound: 3);

        Assert.Single(vikings.AtSea);
        Assert.Equal(HexCoord.FromOffset(5, 5), vikings.AtSea[0].Coord);
        Assert.Equal(2, vikings.NextWaveIndex);
        Assert.Equal(6, vikings.LastCompletedRound);
        Assert.Equal(3, vikings.LastSpawnRound);
    }

    // --- RoundsUntilWaveDue -----------------------------------------------------

    [Fact]
    public void RoundsUntilWaveDue_CountsDownToTheScheduledRound()
    {
        GameState state = MakeIslandState(HexCoord.FromOffset(3, 1));
        // With FirstWaveRound = 3: turn 1 → 2 away, turn 2 → 1, turn 3 → due.
        state.Turns.Reset(0, VikingRaidersRules.FirstWaveRound - 2);
        Assert.Equal(2, VikingRaidersRules.RoundsUntilWaveDue(state));
        state.Turns.Reset(0, VikingRaidersRules.FirstWaveRound - 1);
        Assert.Equal(1, VikingRaidersRules.RoundsUntilWaveDue(state));
        state.Turns.Reset(0, VikingRaidersRules.FirstWaveRound);
        Assert.Equal(0, VikingRaidersRules.RoundsUntilWaveDue(state));
    }

    [Fact]
    public void RoundsUntilWaveDue_TracksTheNextUnspawnedWave()
    {
        GameState state = MakeIslandState(HexCoord.FromOffset(3, 1));
        state.Vikings.NextWaveIndex = 1; // wave 0 spawned; wave 1 due at round 6
        state.Turns.Reset(0, VikingRaidersRules.FirstWaveRound);
        Assert.Equal(
            VikingRaidersRules.WaveIntervalRounds,
            VikingRaidersRules.RoundsUntilWaveDue(state));
    }

    [Fact]
    public void RoundsUntilWaveDue_NullWhenScheduleExhausted_OrOutsideMode()
    {
        GameState state = MakeIslandState(HexCoord.FromOffset(3, 1));
        state.Vikings.NextWaveIndex = VikingRaidersRules.TotalWaves;
        state.Turns.Reset(0, 20);
        Assert.Null(VikingRaidersRules.RoundsUntilWaveDue(state));

        HexGrid grid = TestHelpers.BuildRectGrid(3, 3, Red);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        GameState freeform = MakeState(grid, territories, mode: GameMode.Freeform);
        Assert.Null(VikingRaidersRules.RoundsUntilWaveDue(freeform));
    }

    // --- TurnDue --------------------------------------------------------------

    [Fact]
    public void TurnDue_FalseOutsideVikingRaidersMode()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(3, 3, Red);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        GameState state = MakeState(grid, territories, mode: GameMode.Freeform);
        state.Turns.Reset(0, 5);

        Assert.False(VikingRaidersRules.TurnDue(state));
    }

    [Fact]
    public void TurnDue_FalseBeforeTheFirstWaveRound()
    {
        GameState state = MakeIslandState(HexCoord.FromOffset(3, 1));
        state.Turns.Reset(0, VikingRaidersRules.FirstWaveRound - 1);

        Assert.False(VikingRaidersRules.TurnDue(state));
    }

    [Fact]
    public void TurnDue_TrueAtRoundStart_FalseOnceCompleted()
    {
        GameState state = MakeIslandState(HexCoord.FromOffset(3, 1));
        state.Turns.Reset(0, VikingRaidersRules.FirstWaveRound);

        Assert.True(VikingRaidersRules.TurnDue(state));

        state.Vikings.LastCompletedRound = VikingRaidersRules.FirstWaveRound;
        Assert.False(VikingRaidersRules.TurnDue(state));
    }

    [Fact]
    public void TurnDue_FalseOnceThreatIsCleared()
    {
        GameState state = MakeIslandState(HexCoord.FromOffset(3, 1));
        state.Turns.Reset(0, 10);
        state.Vikings.NextWaveIndex = VikingRaidersRules.TotalWaves;
        state.Vikings.LastCompletedRound = 9; // this round's phase never ran

        Assert.False(VikingRaidersRules.TurnDue(state));
    }

    // --- WaveDue --------------------------------------------------------------

    [Theory]
    [InlineData(2, 0, false)] // before the first wave
    [InlineData(3, 0, true)]  // wave 0 on schedule
    [InlineData(4, 0, true)]  // wave 0 missed → catch up
    [InlineData(5, 1, false)] // wave 1 not yet due
    [InlineData(6, 1, true)]  // wave 1 on schedule
    [InlineData(18, 5, true)] // last wave
    [InlineData(50, 6, false)] // schedule exhausted (NextWaveIndex == TotalWaves)
    public void WaveDue_FollowsScheduleWithCatchUp(int round, int nextWave, bool expected)
    {
        Assert.Equal(expected, VikingRaidersRules.WaveDue(round, nextWave));
    }
}
