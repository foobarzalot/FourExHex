using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Origin-capital merge rule (#117): a human move that merges two
/// same-owner territories keeps the capital of the territory the moving
/// unit originated from — but only in a <see cref="GameState.UseOriginMergeCapital"/>
/// game. Legacy games keep the largest-old-territory rule.
/// </summary>
public partial class GameControllerTests
{
    /// <summary>
    /// A 7×1 strip: Red territory A (0,0)-(2,0) with capital (0,0), a Blue
    /// gap tile (3,0), Red territory B (4,0)-(5,0) with a Recruit on (4,0)
    /// and capital (5,0), Blue (6,0). Moving the Recruit onto the gap
    /// merges A and B; B (the origin) is the smaller territory, so the
    /// origin rule and the largest rule pick different survivors.
    /// </summary>
    private static ControllerHarness BuildMergeStrip(bool useOriginMergeCapital)
    {
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        return TestHelpers.BuildControllerGame(
            players: new List<Player> { red, blue },
            cols: 7, rows: 1,
            ownerOverrides: new[]
            {
                (0, 0, red.Id), (1, 0, red.Id), (2, 0, red.Id),
                (4, 0, red.Id), (5, 0, red.Id),
            },
            beforeTerritories: grid =>
                grid.Get(HexCoord.FromOffset(4, 0))!.Occupant =
                    new Unit(PlayerId.FromIndex(0)),
            useOriginMergeCapital: useOriginMergeCapital);
    }

    [Fact]
    public void HumanMoveMerge_OriginRule_KeepsOriginCapital_EvenThoughSmaller()
    {
        ControllerHarness h = BuildMergeStrip(useOriginMergeCapital: true);
        HexCoord capitalA = HexCoord.FromOffset(0, 0);
        HexCoord capitalB = HexCoord.FromOffset(5, 0);
        Assert.IsType<Capital>(h.State.Grid.Get(capitalA)!.Occupant);
        Assert.IsType<Capital>(h.State.Grid.Get(capitalB)!.Occupant);
        int goldSum = h.State.Treasury.GetGold(capitalA) + h.State.Treasury.GetGold(capitalB);

        // Click the unit (selects B, arms MovingUnit), then the gap tile.
        h.Map.SimulateClick(h.State.Grid.Get(HexCoord.FromOffset(4, 0))!);
        h.Map.SimulateClick(h.State.Grid.Get(HexCoord.FromOffset(3, 0))!);

        Territory merged = h.State.Territories.Single(
            t => t.Owner == h.Players[0].Id && t.Size > 1);
        Assert.Equal(capitalB, merged.Capital);
        Assert.IsType<Capital>(h.State.Grid.Get(capitalB)!.Occupant);
        Assert.Null(h.State.Grid.Get(capitalA)!.Occupant);
        // Merged gold sums onto the surviving (origin) capital.
        Assert.Equal(goldSum, h.State.Treasury.GetGold(capitalB));
    }

    [Fact]
    public void HumanBuyMerge_OriginRule_KeepsPurchasingTerritoryCapital()
    {
        ControllerHarness h = BuildMergeStrip(useOriginMergeCapital: true);
        HexCoord capitalA = HexCoord.FromOffset(0, 0);
        HexCoord capitalB = HexCoord.FromOffset(5, 0);

        // Select B via its capital tile (not the unit tile, which would arm
        // MovingUnit), enter buy mode, and place the Recruit on the gap —
        // a buy-capture that merges A and B. B is the purchasing territory.
        h.Map.SimulateClick(h.State.Grid.Get(capitalB)!);
        h.Hud.ClickBuyRecruit();
        h.Map.SimulateClick(h.State.Grid.Get(HexCoord.FromOffset(3, 0))!);

        Territory merged = h.State.Territories.Single(
            t => t.Owner == h.Players[0].Id && t.Size > 1);
        Assert.Equal(capitalB, merged.Capital);
        Assert.Null(h.State.Grid.Get(capitalA)!.Occupant);
    }

    [Fact]
    public void HumanMoveMerge_LegacyGame_KeepsLargestTerritoryCapital()
    {
        ControllerHarness h = BuildMergeStrip(useOriginMergeCapital: false);
        HexCoord capitalA = HexCoord.FromOffset(0, 0);
        HexCoord capitalB = HexCoord.FromOffset(5, 0);

        h.Map.SimulateClick(h.State.Grid.Get(HexCoord.FromOffset(4, 0))!);
        h.Map.SimulateClick(h.State.Grid.Get(HexCoord.FromOffset(3, 0))!);

        Territory merged = h.State.Territories.Single(
            t => t.Owner == h.Players[0].Id && t.Size > 1);
        Assert.Equal(capitalA, merged.Capital);
        Assert.Null(h.State.Grid.Get(capitalB)!.Occupant);
    }
}
