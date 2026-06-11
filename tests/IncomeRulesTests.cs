using Xunit;

namespace FourExHex.Tests;

public class IncomeRulesTests
{
    private static readonly PlayerId Red = PlayerId.FromIndex(0);

    private static Territory MakeTerritory(HexCoord capital, params HexCoord[] coords) =>
        new Territory(Red, coords, capital);

    [Fact]
    public void IncomeFor_CountsIncomeProducingTiles()
    {
        // 4-tile territory, one tree → 3 income tiles → 3 gold. Income is
        // not difficulty-scaled; IncomeRules is the single income entry
        // point shared by Treasury collection and the AI lookahead.
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Add(new HexTile(new HexCoord(1, 0), Red));
        grid.Add(new HexTile(new HexCoord(2, 0), Red));
        grid.Add(new HexTile(new HexCoord(3, 0), Red));
        grid.Get(new HexCoord(1, 0))!.Occupant = new Tree();
        Territory t = MakeTerritory(new HexCoord(0, 0),
            new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(2, 0), new HexCoord(3, 0));

        Assert.Equal(3, IncomeRules.IncomeFor(t, grid));
    }
}
