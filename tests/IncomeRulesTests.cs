using Xunit;

namespace FourExHex.Tests;

public class IncomeRulesTests
{
    private static readonly PlayerId Red = PlayerId.FromIndex(0);

    private static Territory MakeTerritory(HexCoord capital, params HexCoord[] coords) =>
        new Territory(Red, coords, capital);

    // 4-tile territory, one tree → 3 income tiles.
    private static (Territory, HexGrid) ThreeIncomeTiles()
    {
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Add(new HexTile(new HexCoord(1, 0), Red));
        grid.Add(new HexTile(new HexCoord(2, 0), Red));
        grid.Add(new HexTile(new HexCoord(3, 0), Red));
        grid.Get(new HexCoord(1, 0))!.Occupant = new Tree();
        Territory t = MakeTerritory(new HexCoord(0, 0),
            new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(2, 0), new HexCoord(3, 0));
        return (t, grid);
    }

    // 12-tile single-row territory, two trees → 10 income tiles. Big enough
    // that the percent-based Captain/Commander bonuses survive truncation.
    private static (Territory, HexGrid) TenIncomeTiles()
    {
        var grid = new HexGrid();
        var coords = new HexCoord[12];
        for (int c = 0; c < 12; c++)
        {
            coords[c] = new HexCoord(c, 0);
            grid.Add(new HexTile(coords[c], Red));
        }
        grid.Get(new HexCoord(1, 0))!.Occupant = new Tree();
        grid.Get(new HexCoord(2, 0))!.Occupant = new Tree();
        Territory t = new Territory(Red, coords, coords[0]);
        return (t, grid);
    }

    // Earn rate is flat 1× at every difficulty — the lever stays plumbed
    // ("for now") but income never varies; upkeep is the handicap.
    [Theory]
    [InlineData(Difficulty.Recruit, 3)]
    [InlineData(Difficulty.Soldier, 3)]
    [InlineData(Difficulty.Captain, 3)]
    [InlineData(Difficulty.Commander, 3)]
    public void IncomeFor_SmallTerritory_ScalesByDifficulty(Difficulty d, int expected)
    {
        (Territory t, HexGrid grid) = ThreeIncomeTiles();
        Assert.Equal(expected, IncomeRules.IncomeFor(t, grid, d));
    }

    [Theory]
    [InlineData(Difficulty.Recruit, 10)]
    [InlineData(Difficulty.Soldier, 10)]
    [InlineData(Difficulty.Captain, 10)]
    [InlineData(Difficulty.Commander, 10)]
    public void IncomeFor_LargeTerritory_ScalesByDifficulty(Difficulty d, int expected)
    {
        (Territory t, HexGrid grid) = TenIncomeTiles();
        Assert.Equal(expected, IncomeRules.IncomeFor(t, grid, d));
    }
}
