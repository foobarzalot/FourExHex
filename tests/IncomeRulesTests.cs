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

    [Theory]
    [InlineData(Difficulty.Easy, 1)]    // 3/2 = 1
    [InlineData(Difficulty.Normal, 3)]
    [InlineData(Difficulty.Hard, 4)]    // 1.5×: 3*3/2 = 4
    [InlineData(Difficulty.Brutal, 6)]  // 2×
    public void IncomeFor_ScalesIncomeTileCountByDifficulty(Difficulty d, int expected)
    {
        (Territory t, HexGrid grid) = ThreeIncomeTiles();
        Assert.Equal(expected, IncomeRules.IncomeFor(t, grid, d));
    }
}
