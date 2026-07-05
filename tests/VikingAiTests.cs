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
        VikingAi.ChooseNext(state, new HashSet<HexCoord>(), new Random(seed));

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
        grid.Get(vikingTile)!.Occupant = new Unit(PlayerId.None, UnitLevel.Soldier);
        GameState state = MakeState(grid);
        state.Vikings.NextWaveIndex = VikingRaidersRules.TotalWaves;

        AiAction? action = Choose(state);

        AiMoveAction move = Assert.IsType<AiMoveAction>(action);
        Assert.Equal(vikingTile, move.Source);
        Assert.Equal(Red, /* captured tile was Red before the move */
            state.Grid.Get(move.Destination)!.Owner);
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
            state, PlayerId.None, new HashSet<HexCoord>(), new Random(7));

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
            state, PlayerId.None, new HashSet<HexCoord>(), new Random(7));

        Assert.Null(action);
    }

    [Fact]
    public void ComputerAi_NeutralOwner_RepositionsToDefendBorder()
    {
        // No capturable target (Red's border tile carries a Commander), so
        // the viking should fall through to phase 4b and man its own border.
        // The unit starts two tiles from the border so its defense radiation
        // doesn't already cover the border tile from the interior.
        HexGrid grid = TestHelpers.BuildRectGrid(5, 1, Red);
        HexCoord interior = HexCoord.FromOffset(0, 0);
        HexCoord border = HexCoord.FromOffset(2, 0);
        grid.Get(interior)!.Owner = PlayerId.None;
        grid.Get(HexCoord.FromOffset(1, 0))!.Owner = PlayerId.None;
        grid.Get(border)!.Owner = PlayerId.None;
        grid.Get(interior)!.Occupant = new Unit(PlayerId.None, UnitLevel.Recruit);
        grid.Get(HexCoord.FromOffset(3, 0))!.Occupant = new Unit(Red, UnitLevel.Commander);
        GameState state = MakeState(grid);

        AiAction? action = ComputerAi.ChooseNextAction(
            state, PlayerId.None, new HashSet<HexCoord>(), new Random(7));

        AiMoveAction move = Assert.IsType<AiMoveAction>(action);
        Assert.Equal(interior, move.Source);
        Assert.Equal(border, move.Destination);
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
