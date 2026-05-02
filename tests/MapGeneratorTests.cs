using System.Collections.Generic;
using Godot;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Determinism tests for <see cref="MapGenerator.BuildInitialGrid"/>: the
/// "Map Seed" field on the main menu is only useful if the same seed always
/// produces the same grid. SameSeedProducesIdenticalGrid is the headline
/// guarantee; DifferentSeedsProduceDifferentGrids is regression insurance
/// against a future reintroduction of system-time RNG.
/// </summary>
public class MapGeneratorTests
{
    private static IReadOnlyList<Player> SixPlayers()
    {
        var list = new List<Player>();
        foreach ((string name, string hex) in GameSettings.PlayerConfig)
        {
            list.Add(new Player(name, new Color(hex), AiKind.Heuristic));
        }
        return list;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4242)]
    [InlineData(9999)]
    public void SameSeedProducesIdenticalGrid(int seed)
    {
        IReadOnlyList<Player> players = SixPlayers();

        HexGrid a = MapGenerator.BuildInitialGrid(cols: 16, rows: 12, players, seed);
        HexGrid b = MapGenerator.BuildInitialGrid(cols: 16, rows: 12, players, seed);

        Assert.Equal(a.Count, b.Count);
        // Look tiles up by coord rather than relying on Tiles iteration order
        // (Dictionary.Values isn't contractually ordered).
        foreach (HexTile tA in a.Tiles)
        {
            HexTile? tB = b.Get(tA.Coord);
            Assert.NotNull(tB);
            Assert.Equal(tA.Color, tB!.Color);
            Assert.Equal(tA.Occupant is Tree, tB.Occupant is Tree);
        }
    }

    [Fact]
    public void DifferentSeedsProduceDifferentGrids()
    {
        IReadOnlyList<Player> players = SixPlayers();

        HexGrid a = MapGenerator.BuildInitialGrid(cols: 16, rows: 12, players, seed: 1);
        HexGrid b = MapGenerator.BuildInitialGrid(cols: 16, rows: 12, players, seed: 2);

        // Probability of identical 16x12 grids under independent seeds is
        // astronomically small; one differing tile is enough.
        bool anyDifference = false;
        foreach (HexTile tA in a.Tiles)
        {
            HexTile tB = b.Get(tA.Coord)!;
            if (tA.Color != tB.Color) { anyDifference = true; break; }
            if ((tA.Occupant is Tree) != (tB.Occupant is Tree)) { anyDifference = true; break; }
        }
        Assert.True(anyDifference, "Seeds 1 and 2 should produce visibly different grids");
    }
}
