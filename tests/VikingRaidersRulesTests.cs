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

    [Theory]
    [InlineData(0, 40, 5)]  // 40/8 = 5, + wave 0
    [InlineData(5, 40, 10)] // 40/8 = 5, + wave 5
    [InlineData(0, 4, 2)]   // floor(4/8)=0 → MinWaveSize
    [InlineData(3, 0, 5)]   // MinWaveSize + wave 3
    public void WaveComposition_SizeScalesWithCoastAndWave(int wave, int coastal, int expectedSize)
    {
        Assert.Equal(expectedSize, VikingRaidersRules.WaveComposition(wave, coastal).Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void WaveComposition_EarlyWaves_AllRecruits(int wave)
    {
        IReadOnlyList<UnitLevel> comp = VikingRaidersRules.WaveComposition(wave, 40);
        Assert.All(comp, level => Assert.Equal(UnitLevel.Recruit, level));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void WaveComposition_MidWaves_MixInSoldiers_NoCaptains(int wave)
    {
        IReadOnlyList<UnitLevel> comp = VikingRaidersRules.WaveComposition(wave, 40);
        Assert.Contains(UnitLevel.Soldier, comp);
        Assert.DoesNotContain(UnitLevel.Captain, comp);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public void WaveComposition_LateWaves_MixInCaptains(int wave)
    {
        IReadOnlyList<UnitLevel> comp = VikingRaidersRules.WaveComposition(wave, 40);
        Assert.Contains(UnitLevel.Captain, comp);
    }

    [Fact]
    public void WaveComposition_NeverCommander()
    {
        for (int wave = 0; wave < VikingRaidersRules.TotalWaves; wave++)
        {
            IReadOnlyList<UnitLevel> comp = VikingRaidersRules.WaveComposition(wave, 200);
            Assert.DoesNotContain(UnitLevel.Commander, comp);
        }
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
