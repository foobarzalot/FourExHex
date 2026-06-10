using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

public class AiCommonTests
{
    private static readonly PlayerId Red = PlayerId.FromIndex(0);
    private static readonly PlayerId Blue = PlayerId.FromIndex(1);

    private static GameState BuildState(HexGrid grid, params Player[] players)
    {
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var list = players.ToList();
        return new GameState(grid, territories, list, new TurnState(list), new Treasury());
    }

    [Fact]
    public void Enumerate_BuyGate_SeesDifficultyScaledUpkeep()
    {
        // 20-tile Red territory (income 20 at 100%), gold 100, one adjacent
        // Blue tile so buy targets exist. Buying a Commander unit costs 40
        // and adds upkeep 54 at Soldier difficulty / 27 at Commander
        // difficulty (the table). Solvency gate: gold-cost + 5×net ≥ 0:
        //   Soldier:   60 + 5×(20-54) = -110 → gated out
        //   Commander: 60 + 5×(20-27) =  +25 → enumerated
        // Same board, only the owner's difficulty differs.
        List<AiCandidate> CandidatesFor(Difficulty d)
        {
            HexGrid grid = TestHelpers.BuildRectGrid(5, 4, Red);
            grid.Add(new HexTile(HexCoord.FromOffset(5, 0), Blue));
            GameState state = BuildState(grid,
                new Player("Red", PlayerId.FromIndex(0), PlayerKind.Computer, d),
                new Player("Blue", PlayerId.FromIndex(1), PlayerKind.Computer));
            Territory red = state.Territories.First(t => t.Owner == Red);
            state.Treasury.SetGold(red.Capital!.Value, 100);
            return AiCommon.Enumerate(red, state).ToList();
        }

        bool CommanderBuy(AiCandidate c) =>
            c.Action is AiBuyUnitAction buy && buy.Level == UnitLevel.Commander;

        Assert.DoesNotContain(CandidatesFor(Difficulty.Soldier), CommanderBuy);
        Assert.Contains(CandidatesFor(Difficulty.Commander), CommanderBuy);
    }

    [Fact]
    public void Enumerate_Yields_Move_Reposition_To_Border_Tile()
    {
        // 4-tile Red strip cols 0..3 + Blue tile col 4. Red border
        // tile is col 3 (adjacent to Blue at col 4); cols 0..2 are
        // interior. Recruit on col 0 with no prior moves — its
        // valid in-territory targets include col 3 (border).
        var grid = new HexGrid();
        for (int col = 0; col <= 3; col++)
            grid.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(4, 0), Blue));
        grid.Get(HexCoord.FromOffset(0, 0))!.Occupant = new Unit(Red);
        GameState state = BuildState(grid, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));

        Territory red = state.Territories.First(t => t.Owner == Red);
        List<AiCandidate> candidates = AiCommon.Enumerate(red, state).ToList();

        AiCandidate reposition = candidates.First(c =>
            c.Kind == AiActionKind.Reposition && c.Action is AiMoveAction);
        AiMoveAction move = Assert.IsType<AiMoveAction>(reposition.Action);
        Assert.Equal(HexCoord.FromOffset(0, 0), move.Source);
        Assert.Equal(HexCoord.FromOffset(3, 0), move.Destination);
    }

    [Fact]
    public void Enumerate_Skips_Move_Reposition_To_Interior_Tile()
    {
        // Same 4-tile strip + Blue. Cols 1 and 2 are interior Red
        // tiles (no enemy neighbor) — they must NOT be reposition
        // targets. Only col 3 (border) is allowed.
        var grid = new HexGrid();
        for (int col = 0; col <= 3; col++)
            grid.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(4, 0), Blue));
        grid.Get(HexCoord.FromOffset(0, 0))!.Occupant = new Unit(Red);
        GameState state = BuildState(grid, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));

        Territory red = state.Territories.First(t => t.Owner == Red);
        List<AiCandidate> candidates = AiCommon.Enumerate(red, state).ToList();

        foreach (AiCandidate c in candidates.Where(c => c.Kind == AiActionKind.Reposition))
        {
            switch (c.Action)
            {
                case AiMoveAction mv:
                    Assert.True(AiCommon.IsBorderTile(mv.Destination, state.Grid, Red),
                        $"reposition move target {mv.Destination} should be a border tile");
                    break;
                case AiBuyUnitAction bu:
                    Assert.True(AiCommon.IsBorderTile(bu.Destination, state.Grid, Red),
                        $"reposition buy target {bu.Destination} should be a border tile");
                    break;
            }
        }
    }

    [Fact]
    public void Enumerate_Yields_Buy_Reposition_To_Border_Tile()
    {
        // 4-tile Red strip cols 0..3 + Blue col 4 with a Soldier
        // (defense 2) — a fresh recruit cannot buy-capture col 4.
        // Treasury has enough for a recruit. Border tile is col 3,
        // empty. Net income 4, upkeep 0 → buy-reposition post-net
        // = 4 - 2 = 2 ≥ 0, solvent.
        var grid = new HexGrid();
        for (int col = 0; col <= 3; col++)
            grid.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(4, 0), Blue));
        grid.Get(HexCoord.FromOffset(4, 0))!.Occupant = new Unit(Blue, UnitLevel.Soldier);
        GameState state = BuildState(grid, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));
        HexCoord cap = state.Territories.First(t => t.Owner == Red).Capital!.Value;
        state.Treasury.SetGold(cap, 10);

        Territory red = state.Territories.First(t => t.Owner == Red);
        List<AiCandidate> candidates = AiCommon.Enumerate(red, state).ToList();

        AiCandidate buyReposition = candidates.First(c =>
            c.Kind == AiActionKind.Reposition && c.Action is AiBuyUnitAction);
        AiBuyUnitAction buy = Assert.IsType<AiBuyUnitAction>(buyReposition.Action);
        Assert.Equal(HexCoord.FromOffset(3, 0), buy.Destination);
        Assert.Equal(UnitLevel.Recruit, buy.Level);
    }

    [Fact]
    public void Enumerate_Skips_Tower_Within_MinSpacing_Of_Friendly_Tower()
    {
        // 6-tile Red strip cols 0..5 + Blue col 6 so every Red tile
        // except (0,0) is on a path toward the border. An existing
        // Red tower sits at (1,0). With ample gold and a recruit on
        // (5,0) (so net income is non-negative), the tower-build
        // candidates should NOT include any coord at distance < 3
        // from (1,0) — i.e., (0,0), (2,0), and (3,0) must be absent.
        // (4,0) and (5,0) are at distance >= 3 and may appear (if
        // they're border tiles and pass the other checks).
        var grid = new HexGrid();
        for (int col = 0; col <= 5; col++)
            grid.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(6, 0), Blue));
        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = new Tower();
        grid.Get(HexCoord.FromOffset(5, 0))!.Occupant = new Unit(Red);
        GameState state = BuildState(grid, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));
        HexCoord cap = state.Territories.First(t => t.Owner == Red).Capital!.Value;
        state.Treasury.SetGold(cap, 100);

        Territory red = state.Territories.First(t => t.Owner == Red);
        List<AiBuildTowerAction> towers = AiCommon.Enumerate(red, state)
            .Where(c => c.Action is AiBuildTowerAction)
            .Select(c => (AiBuildTowerAction)c.Action)
            .ToList();

        foreach (AiBuildTowerAction t in towers)
        {
            int dist = HexCoord.Distance(t.Destination, HexCoord.FromOffset(1, 0));
            Assert.True(dist >= AiCommon.MinTowerSpacing,
                $"AI tower candidate at {t.Destination} is distance {dist} from existing tower at (1,0); " +
                $"must be >= {AiCommon.MinTowerSpacing}.");
        }
    }

    [Fact]
    public void Enumerate_Skips_Buy_Reposition_When_Insolvent()
    {
        // 5-tile Red strip cols 0..4 + Blue col 5. Two existing
        // recruits on cols 0, 1 (upkeep 4). Income 5, net = 1.
        // Buy-recruit-capture: post-net = 1 + 1 - 2 = 0 → solvent.
        // Buy-recruit-reposition: post-net = 1 - 2 = -1 → insolvent.
        // Confirm the test scenario actually has a buy-capture
        // available (sanity), then assert no buy-reposition.
        var grid = new HexGrid();
        for (int col = 0; col <= 4; col++)
            grid.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(5, 0), Blue));
        grid.Get(HexCoord.FromOffset(0, 0))!.Occupant = new Unit(Red);
        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = new Unit(Red);
        GameState state = BuildState(grid, new Player("Red", PlayerId.FromIndex(0)), new Player("Blue", PlayerId.FromIndex(1)));
        HexCoord cap = state.Territories.First(t => t.Owner == Red).Capital!.Value;
        state.Treasury.SetGold(cap, 10);

        Territory red = state.Territories.First(t => t.Owner == Red);
        List<AiCandidate> candidates = AiCommon.Enumerate(red, state).ToList();

        Assert.Contains(candidates, c =>
            c.Kind == AiActionKind.Capture && c.Action is AiBuyUnitAction);
        Assert.DoesNotContain(candidates, c =>
            c.Kind == AiActionKind.Reposition && c.Action is AiBuyUnitAction);
    }
}
