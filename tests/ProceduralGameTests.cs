using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Characterization + determinism tests for <see cref="ProceduralGame.Build"/>,
/// the shared procedural-game builder used by both the play scene and the
/// main-menu map thumbnail. The headline guarantees: same seed → identical
/// state, and the builder reproduces the same pipeline the play scene
/// uses (so the thumbnail can't drift from Start Game).
/// </summary>
public class ProceduralGameTests
{
    private const int Cols = 20;
    private const int Rows = 15;

    private static IReadOnlyList<Player> SixPlayers()
    {
        var list = new List<Player>();
        for (int i = 0; i < GameSettings.PlayerConfig.Length; i++)
        {
            (string name, _) = GameSettings.PlayerConfig[i];
            list.Add(new Player(name, PlayerId.FromIndex(i), PlayerKind.Computer));
        }
        return list;
    }

    /// <summary>Reference: the inline pipeline Main.cs runs.</summary>
    private static GameState InlineReference(int seed)
    {
        IReadOnlyList<Player> players = SixPlayers();
        var turnState = new TurnState(players);
        var treasury = new Treasury();
        MapGenResult mapGen = MapGenerator.BuildInitialGrid(Cols, Rows, players, seed);
        HexGrid grid = mapGen.Grid;
        IReadOnlyList<Territory> territories = TerritoryFinder.Recompute(
            grid, new List<Territory>());
        return new GameState(grid, territories, players, turnState, treasury, mapGen.WaterCoords);
    }

    private static void AssertTilesEqual(HexGrid a, HexGrid b)
    {
        Assert.Equal(a.Count, b.Count);
        foreach (HexTile tA in a.Tiles)
        {
            HexTile? tB = b.Get(tA.Coord);
            Assert.NotNull(tB);
            Assert.Equal(tA.Owner, tB!.Owner);
            Assert.Equal(tA.IsGold, tB.IsGold);
            Assert.Equal(tA.IsMountain, tB.IsMountain);
            Assert.Equal(tA.Occupant?.GetType(), tB.Occupant?.GetType());
        }
    }

    private static void AssertTerritoriesEqual(
        IReadOnlyList<Territory> a, IReadOnlyList<Territory> b)
    {
        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i].Owner, b[i].Owner);
            Assert.Equal(a[i].Capital, b[i].Capital);
            Assert.Equal(new HashSet<HexCoord>(a[i].Coords), new HashSet<HexCoord>(b[i].Coords));
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4242)]
    [InlineData(9999)]
    public void SameSeedProducesIdenticalState(int seed)
    {
        GameState a = ProceduralGame.Build(Cols, Rows, SixPlayers(), seed);
        GameState b = ProceduralGame.Build(Cols, Rows, SixPlayers(), seed);

        AssertTilesEqual(a.Grid, b.Grid);
        AssertTerritoriesEqual(a.Territories, b.Territories);
        Assert.Equal(new HashSet<HexCoord>(a.WaterCoords), new HashSet<HexCoord>(b.WaterCoords));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4242)]
    [InlineData(9999)]
    public void MatchesInlinePipeline(int seed)
    {
        GameState actual = ProceduralGame.Build(Cols, Rows, SixPlayers(), seed);
        GameState reference = InlineReference(seed);

        AssertTilesEqual(reference.Grid, actual.Grid);
        AssertTerritoriesEqual(reference.Territories, actual.Territories);
        Assert.Equal(
            new HashSet<HexCoord>(reference.WaterCoords),
            new HashSet<HexCoord>(actual.WaterCoords));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4242)]
    [InlineData(9999)]
    public void WithMountains_BuildsValidStateWithNeutralRanges(int seed)
    {
        // Mountains are placed neutral, so they flow through TerritoryFinder /
        // CapitalReconciler as capital-less neutral regions. Guard that the full
        // pipeline (Build → Recompute) stays valid: it doesn't throw, mountains
        // are neutral, and every capital-bearing territory is owned (no capital
        // ever lands on neutral mountain land).
        GameState state = ProceduralGame.Build(
            Cols, Rows, SixPlayers(), seed, new MapGenOptions(MountainDensity: 10));

        int mountains = 0;
        foreach (HexTile t in state.Grid.Tiles)
        {
            if (!t.IsMountain) continue;
            mountains++;
            Assert.True(t.Owner.IsNone, $"Mountain tile {t.Coord} should be neutral");
        }
        Assert.True(mountains > 0, $"Expected mountains for seed {seed}");

        foreach (Territory territory in state.Territories)
        {
            if (territory.HasCapital)
            {
                Assert.False(territory.Owner.IsNone,
                    "A capital-bearing territory must be owned, never neutral");
                HexTile? capTile = state.Grid.Get(territory.Capital!.Value);
                Assert.NotNull(capTile);
                Assert.False(capTile!.IsMountain, "A capital must never sit on a mountain");
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4242)]
    [InlineData(9999)]
    public void WithGoldAndMountains_BuildsValidStateWithNeutralFeatures(int seed)
    {
        // Gold (and mountains) are neutral, flowing through TerritoryFinder /
        // CapitalReconciler as capital-less neutral regions. Guard the full
        // pipeline: it doesn't throw, gold tiles are neutral, and no capital
        // lands on a neutral gold tile.
        GameState state = ProceduralGame.Build(
            Cols, Rows, SixPlayers(), seed,
            new MapGenOptions(MountainDensity: 10, GoldDensity: 5));

        int gold = 0;
        foreach (HexTile t in state.Grid.Tiles)
        {
            if (!t.IsGold) continue;
            gold++;
            Assert.True(t.Owner.IsNone, $"Gold tile {t.Coord} should be neutral");
        }
        Assert.True(gold > 0, $"Expected gold for seed {seed}");

        foreach (Territory territory in state.Territories)
        {
            if (!territory.HasCapital) continue;
            Assert.False(territory.Owner.IsNone,
                "A capital-bearing territory must be owned, never neutral");
            HexTile? capTile = state.Grid.Get(territory.Capital!.Value);
            Assert.NotNull(capTile);
            Assert.False(capTile!.IsGold && capTile.Owner.IsNone,
                "A capital must never sit on a neutral gold tile");
        }
    }

    [Fact]
    public void StartsAtTurnOneWithEmptyTreasuryAndGivenPlayers()
    {
        IReadOnlyList<Player> players = SixPlayers();
        GameState state = ProceduralGame.Build(Cols, Rows, players, 1234);

        Assert.Equal(1, state.Turns.TurnNumber);
        Assert.Same(players, state.Players);
        // Empty treasury at game start (starting gold is seeded later by the
        // controller, not by the world build).
        foreach (Territory t in state.Territories)
        {
            if (t.HasCapital)
                Assert.Equal(0, state.Treasury.GetGold(t.Capital!.Value));
        }
    }
}
