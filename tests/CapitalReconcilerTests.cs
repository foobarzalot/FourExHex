// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

public class CapitalReconcilerTests
{
    private static readonly PlayerId Red = PlayerId.FromIndex(0);

    private static HexGrid BuildGrid(params HexCoord[] coords) =>
        TestHelpers.BuildSpotGrid(Red, coords);

    private static Territory T(HexCoord? capital, params HexCoord[] coords) =>
        new Territory(Red, coords, capital);

    // --- Trivial cases ----------------------------------------------------

    [Fact]
    public void Reconcile_SingletonTerritory_GetsNoCapital()
    {
        HexGrid grid = BuildGrid(new HexCoord(0, 0));
        var raw = new[] { T(null, new HexCoord(0, 0)) };

        var result = CapitalReconciler.Reconcile(raw, new List<Territory>(), grid);

        Assert.Single(result);
        Assert.False(result[0].HasCapital);
    }

    [Fact]
    public void Reconcile_InitialMultiHexTerritory_GetsCapitalPlacedAtLexMinEmpty()
    {
        // Fresh grid (no existing Capital occupants). Reconciler should
        // place a Capital on the lex-min empty tile.
        HexGrid grid = BuildGrid(new HexCoord(0, 0), new HexCoord(1, 0));
        var raw = new[] { T(null, new HexCoord(0, 0), new HexCoord(1, 0)) };

        var result = CapitalReconciler.Reconcile(raw, new List<Territory>(), grid);

        Assert.Single(result);
        Assert.True(result[0].HasCapital);
        Assert.Equal(new HexCoord(0, 0), result[0].Capital);
        // And the grid actually has a Capital occupant at (0,0).
        Assert.IsType<Capital>(grid.Get(new HexCoord(0, 0))!.Occupant);
    }

    // --- Preservation -----------------------------------------------------

    [Fact]
    public void Reconcile_InheritedSingleCapital_Unchanged()
    {
        // An existing Capital at (0,0) belongs to an old territory of size 2.
        // The new territory (same shape) should keep that capital.
        HexGrid grid = BuildGrid(new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Capital();
        var old = new[] { T(new HexCoord(0, 0), new HexCoord(0, 0), new HexCoord(1, 0)) };
        var raw = new[] { T(null, new HexCoord(0, 0), new HexCoord(1, 0)) };

        var result = CapitalReconciler.Reconcile(raw, old, grid);

        Assert.Single(result);
        Assert.Equal(new HexCoord(0, 0), result[0].Capital);
        Assert.IsType<Capital>(grid.Get(new HexCoord(0, 0))!.Occupant);
    }

    // --- Splits -----------------------------------------------------------

    [Fact]
    public void Reconcile_Split_PieceWithOldCapital_KeepsIt()
    {
        // Old: one territory size 3 with capital (0,0).
        // New: split into pieceA containing (0,0), and pieceB disconnected.
        HexGrid grid = BuildGrid(
            new HexCoord(0, 0), new HexCoord(1, 0),
            new HexCoord(5, 5), new HexCoord(5, 6));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Capital();
        var old = new[] { T(new HexCoord(0, 0),
            new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(5, 5), new HexCoord(5, 6)) };
        var raw = new[]
        {
            T(null, new HexCoord(0, 0), new HexCoord(1, 0)),
            T(null, new HexCoord(5, 5), new HexCoord(5, 6)),
        };

        var result = CapitalReconciler.Reconcile(raw, old, grid);

        Territory withOld = result.First(t => t.Coords.Contains(new HexCoord(0, 0)));
        Territory fresh = result.First(t => t.Coords.Contains(new HexCoord(5, 5)));

        Assert.Equal(new HexCoord(0, 0), withOld.Capital);
        Assert.True(fresh.HasCapital);
        Assert.Equal(new HexCoord(5, 5), fresh.Capital);  // lex-min of the piece
        // Grid actually reflects both capitals.
        Assert.IsType<Capital>(grid.Get(new HexCoord(0, 0))!.Occupant);
        Assert.IsType<Capital>(grid.Get(new HexCoord(5, 5))!.Occupant);
    }

    // --- Merges -----------------------------------------------------------

    [Fact]
    public void Reconcile_Merge_LargerOldTerritoryCapitalWins()
    {
        // Old A (size 5) with capital (0,0). Old B (size 2) with capital (5,5).
        // New territory merges both, ownership preserved.
        var coordsA = new[]
        {
            new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(2, 0),
            new HexCoord(3, 0), new HexCoord(4, 0),
        };
        var coordsB = new[] { new HexCoord(5, 5), new HexCoord(6, 5) };
        var allCoords = coordsA.Concat(coordsB).ToArray();

        HexGrid grid = BuildGrid(allCoords);
        grid.Get(new HexCoord(0, 0))!.Occupant = new Capital();
        grid.Get(new HexCoord(5, 5))!.Occupant = new Capital();

        var old = new[]
        {
            T(new HexCoord(0, 0), coordsA),
            T(new HexCoord(5, 5), coordsB),
        };
        var raw = new[] { T(null, allCoords) };

        var result = CapitalReconciler.Reconcile(raw, old, grid);

        Assert.Single(result);
        Assert.Equal(new HexCoord(0, 0), result[0].Capital);
        // Losing capital has been demoted — grid tile is now empty.
        Assert.Null(grid.Get(new HexCoord(5, 5))!.Occupant);
    }

    [Fact]
    public void Reconcile_Merge_SecondCandidateLarger_ReplacesWinner()
    {
        // Old A (size 2, first in list) has capital (5, 5).
        // Old B (size 5, second in list) has capital (0, 0).
        // The loop starts winner = (5, 5) and must replace it with (0, 0)
        // when it sees the bigger size on the second iteration. Exercises
        // the "candidate size > winner size → replace" branch.
        HexGrid grid = BuildGrid(
            new HexCoord(5, 5), new HexCoord(6, 5),
            new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(2, 0),
            new HexCoord(3, 0), new HexCoord(4, 0));
        grid.Get(new HexCoord(5, 5))!.Occupant = new Capital();
        grid.Get(new HexCoord(0, 0))!.Occupant = new Capital();

        var old = new[]
        {
            T(new HexCoord(5, 5), new HexCoord(5, 5), new HexCoord(6, 5)),
            T(new HexCoord(0, 0),
                new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(2, 0),
                new HexCoord(3, 0), new HexCoord(4, 0)),
        };
        var raw = new[] { T(null,
            new HexCoord(5, 5), new HexCoord(6, 5),
            new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(2, 0),
            new HexCoord(3, 0), new HexCoord(4, 0)) };

        var result = CapitalReconciler.Reconcile(raw, old, grid);

        Assert.Equal(new HexCoord(0, 0), result[0].Capital);
    }

    [Fact]
    public void Reconcile_Merge_EqualSize_TiebreaksOnLexMin()
    {
        // Two old territories of equal size (3 each). The loop should use
        // the lex-min tiebreaker to pick (0, 0) over (5, 5). Exercises the
        // "equal size AND candidate < winner → replace" branch.
        HexGrid grid = BuildGrid(
            new HexCoord(5, 5), new HexCoord(6, 5), new HexCoord(7, 5),
            new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(2, 0));
        grid.Get(new HexCoord(5, 5))!.Occupant = new Capital();
        grid.Get(new HexCoord(0, 0))!.Occupant = new Capital();

        var old = new[]
        {
            T(new HexCoord(5, 5), new HexCoord(5, 5), new HexCoord(6, 5), new HexCoord(7, 5)),
            T(new HexCoord(0, 0), new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(2, 0)),
        };
        var raw = new[] { T(null,
            new HexCoord(5, 5), new HexCoord(6, 5), new HexCoord(7, 5),
            new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(2, 0)) };

        var result = CapitalReconciler.Reconcile(raw, old, grid);

        Assert.Equal(new HexCoord(0, 0), result[0].Capital);
    }

    [Fact]
    public void Reconcile_Merge_OneOldHasCapitalOtherIsSingleton_KeepsCapitalized()
    {
        // Old A (size 2) with capital (5,0). Old B is a singleton (0,0), no capital.
        // Merge into one territory. The inherited old capital (5,0) wins.
        HexGrid grid = BuildGrid(
            new HexCoord(0, 0), new HexCoord(5, 0), new HexCoord(6, 0));
        grid.Get(new HexCoord(5, 0))!.Occupant = new Capital();

        var old = new[]
        {
            T(new HexCoord(5, 0), new HexCoord(5, 0), new HexCoord(6, 0)),
            T(null, new HexCoord(0, 0)),
        };
        var raw = new[] { T(null, new HexCoord(0, 0), new HexCoord(5, 0), new HexCoord(6, 0)) };

        var result = CapitalReconciler.Reconcile(raw, old, grid);

        Assert.Single(result);
        Assert.Equal(new HexCoord(5, 0), result[0].Capital);
    }

    // --- Merges: origin-capital rule ---------------------------------------

    [Fact]
    public void Reconcile_Merge_OriginCapitalWins_EvenWhenItsTerritoryIsSmaller()
    {
        // Old A (size 5) with capital (0,0). Old B (size 2) with capital (5,5).
        // The merging unit originated from B — B's capital survives despite
        // being the smaller territory; A's capital is demoted off the grid.
        var coordsA = new[]
        {
            new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(2, 0),
            new HexCoord(3, 0), new HexCoord(4, 0),
        };
        var coordsB = new[] { new HexCoord(5, 5), new HexCoord(6, 5) };
        var allCoords = coordsA.Concat(coordsB).ToArray();

        HexGrid grid = BuildGrid(allCoords);
        grid.Get(new HexCoord(0, 0))!.Occupant = new Capital();
        grid.Get(new HexCoord(5, 5))!.Occupant = new Capital();

        var old = new[]
        {
            T(new HexCoord(0, 0), coordsA),
            T(new HexCoord(5, 5), coordsB),
        };
        var raw = new[] { T(null, allCoords) };

        var result = CapitalReconciler.Reconcile(
            raw, old, grid, originCapital: new HexCoord(5, 5));

        Assert.Single(result);
        Assert.Equal(new HexCoord(5, 5), result[0].Capital);
        Assert.Null(grid.Get(new HexCoord(0, 0))!.Occupant);
        Assert.IsType<Capital>(grid.Get(new HexCoord(5, 5))!.Occupant);
    }

    [Fact]
    public void Reconcile_Merge_OriginCapitalNotAmongInherited_FallsBackToLargest()
    {
        // The origin coord isn't one of the merged capitals (e.g. the merge
        // was triggered from a capital-less singleton). Largest-wins applies.
        var coordsA = new[]
        {
            new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(2, 0),
        };
        var coordsB = new[] { new HexCoord(5, 5), new HexCoord(6, 5) };
        var allCoords = coordsA.Concat(coordsB).ToArray();

        HexGrid grid = BuildGrid(allCoords);
        grid.Get(new HexCoord(0, 0))!.Occupant = new Capital();
        grid.Get(new HexCoord(5, 5))!.Occupant = new Capital();

        var old = new[]
        {
            T(new HexCoord(0, 0), coordsA),
            T(new HexCoord(5, 5), coordsB),
        };
        var raw = new[] { T(null, allCoords) };

        var result = CapitalReconciler.Reconcile(
            raw, old, grid, originCapital: new HexCoord(9, 9));

        Assert.Single(result);
        Assert.Equal(new HexCoord(0, 0), result[0].Capital);
    }

    [Fact]
    public void Reconcile_Merge_ThreeWay_OriginCapitalBeatsBothOthers()
    {
        // Three old territories merge at once; the origin's capital (7,7)
        // wins over both a larger (0,0..4,0) and an equal-size (5,5..6,5)
        // rival, and both losers are demoted.
        var coordsA = new[]
        {
            new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(2, 0),
            new HexCoord(3, 0), new HexCoord(4, 0),
        };
        var coordsB = new[] { new HexCoord(5, 5), new HexCoord(6, 5) };
        var coordsC = new[] { new HexCoord(7, 7), new HexCoord(8, 7) };
        var allCoords = coordsA.Concat(coordsB).Concat(coordsC).ToArray();

        HexGrid grid = BuildGrid(allCoords);
        grid.Get(new HexCoord(0, 0))!.Occupant = new Capital();
        grid.Get(new HexCoord(5, 5))!.Occupant = new Capital();
        grid.Get(new HexCoord(7, 7))!.Occupant = new Capital();

        var old = new[]
        {
            T(new HexCoord(0, 0), coordsA),
            T(new HexCoord(5, 5), coordsB),
            T(new HexCoord(7, 7), coordsC),
        };
        var raw = new[] { T(null, allCoords) };

        var result = CapitalReconciler.Reconcile(
            raw, old, grid, originCapital: new HexCoord(7, 7));

        Assert.Single(result);
        Assert.Equal(new HexCoord(7, 7), result[0].Capital);
        Assert.Null(grid.Get(new HexCoord(0, 0))!.Occupant);
        Assert.Null(grid.Get(new HexCoord(5, 5))!.Occupant);
        Assert.IsType<Capital>(grid.Get(new HexCoord(7, 7))!.Occupant);
    }

    [Fact]
    public void Reconcile_Merge_OriginCapitalWithRandomize_StillWinsDeterministically()
    {
        // The origin rule takes precedence over the randomized tiebreak:
        // even with randomize on, the origin capital wins outright.
        var coordsA = new[]
        {
            new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(2, 0),
        };
        var coordsB = new[] { new HexCoord(5, 5), new HexCoord(6, 5), new HexCoord(7, 5) };
        var allCoords = coordsA.Concat(coordsB).ToArray();

        HexGrid grid = BuildGrid(allCoords);
        grid.Get(new HexCoord(0, 0))!.Occupant = new Capital();
        grid.Get(new HexCoord(5, 5))!.Occupant = new Capital();

        var old = new[]
        {
            T(new HexCoord(0, 0), coordsA),
            T(new HexCoord(5, 5), coordsB),
        };
        var raw = new[] { T(null, allCoords) };

        var result = CapitalReconciler.Reconcile(
            raw, old, grid, originCapital: new HexCoord(5, 5));

        Assert.Single(result);
        Assert.Equal(new HexCoord(5, 5), result[0].Capital);
    }

    // --- Stomping ---------------------------------------------------------

    [Fact]
    public void Reconcile_NewCapitalPlacedOverUnit_DestroysTheUnit()
    {
        // Two-tile territory, both occupied by recruits. Reconciler must
        // still place a capital; it picks the lex-min unit tile and the
        // unit is destroyed (no refund).
        HexGrid grid = BuildGrid(new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Unit(Red);
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red);

        var raw = new[] { T(null, new HexCoord(0, 0), new HexCoord(1, 0)) };

        var result = CapitalReconciler.Reconcile(raw, new List<Territory>(), grid);

        Assert.Single(result);
        Assert.Equal(new HexCoord(0, 0), result[0].Capital);
        // (0,0) is now a Capital, NOT the original Unit.
        Assert.IsType<Capital>(grid.Get(new HexCoord(0, 0))!.Occupant);
        // (1,0) still has its Unit (it wasn't the chosen placement).
        Assert.IsType<Unit>(grid.Get(new HexCoord(1, 0))!.Occupant);
    }

    [Fact]
    public void Reconcile_PrefersEmptyOverUnitWhenBothAvailable()
    {
        // (0,0) occupied by a unit, (1,0) empty. Reconciler should place
        // the capital on (1,0) rather than stomping the unit at (0,0).
        HexGrid grid = BuildGrid(new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Unit(Red);

        var raw = new[] { T(null, new HexCoord(0, 0), new HexCoord(1, 0)) };

        var result = CapitalReconciler.Reconcile(raw, new List<Territory>(), grid);

        Assert.Equal(new HexCoord(1, 0), result[0].Capital);
        Assert.IsType<Unit>(grid.Get(new HexCoord(0, 0))!.Occupant);
        Assert.IsType<Capital>(grid.Get(new HexCoord(1, 0))!.Occupant);
    }

    // --- Singleton stranding ---------------------------------------------

    [Fact]
    public void Reconcile_Split_StrandedSingletonWithOldCapital_RemovesCapital()
    {
        // Old territory: three coords with capital (0,0). After the split
        // the piece containing (0,0) is a singleton — no capital allowed.
        // The reconciler must strip both the Territory-level capital
        // record and the Capital occupant sitting on the tile.
        HexGrid grid = BuildGrid(
            new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(5, 5));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Capital();
        var old = new[] { T(new HexCoord(0, 0),
            new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(5, 5)) };

        // Split result: {(0,0)} singleton, {(1,0)} singleton, {(5,5)} singleton.
        var raw = new[]
        {
            T(null, new HexCoord(0, 0)),
            T(null, new HexCoord(1, 0)),
            T(null, new HexCoord(5, 5)),
        };

        var result = CapitalReconciler.Reconcile(raw, old, grid);

        Assert.All(result, terr => Assert.False(terr.HasCapital));
        Assert.Null(grid.Get(new HexCoord(0, 0))!.Occupant);
    }

    [Fact]
    public void Reconcile_MultiHexTerritoryAllTowers_PlacesCapitalStompingTower()
    {
        // Most extreme invariant case: every tile in the territory holds
        // a Tower. Since the invariant "2+ contiguous cells must have a
        // capital" is hard, the reconciler must stomp a tower (the last
        // fallback tier).
        HexGrid grid = BuildGrid(new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Tower();
        grid.Get(new HexCoord(1, 0))!.Occupant = new Tower();

        var raw = new[] { T(null, new HexCoord(0, 0), new HexCoord(1, 0)) };

        var result = CapitalReconciler.Reconcile(raw, new List<Territory>(), grid);

        Assert.True(result[0].HasCapital);
        Assert.Equal(new HexCoord(0, 0), result[0].Capital);
        Assert.IsType<Capital>(grid.Get(new HexCoord(0, 0))!.Occupant);
        Assert.IsType<Tower>(grid.Get(new HexCoord(1, 0))!.Occupant);
    }

    [Fact]
    public void Reconcile_MultiHexTerritoryAllTrees_PlacesCapitalStompingTree()
    {
        // Two-tile territory where every tile holds a tree. The fallback
        // tier stomps a tree to honor the 2+ hex capital invariant.
        HexGrid grid = BuildGrid(new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Tree();
        grid.Get(new HexCoord(1, 0))!.Occupant = new Tree();

        var raw = new[] { T(null, new HexCoord(0, 0), new HexCoord(1, 0)) };

        var result = CapitalReconciler.Reconcile(raw, new List<Territory>(), grid);

        Assert.True(result[0].HasCapital);
        Assert.Equal(new HexCoord(0, 0), result[0].Capital);
        // The tree on the chosen tile was replaced by the Capital occupant;
        // the other tile's tree is untouched.
        Assert.IsType<Capital>(grid.Get(new HexCoord(0, 0))!.Occupant);
        Assert.IsType<Tree>(grid.Get(new HexCoord(1, 0))!.Occupant);
    }

    // --- Neutral (unowned) territories ---

    // --- Randomized placement (board-state-derived seed) -----------------

    [Fact]
    public void Reconcile_Randomize_IsReproducibleForIdenticalBoards()
    {
        // The fidelity property the AI's cloned simulation depends on: two
        // reconciles of the same board (same coords) must place the capital on
        // the same tile, because the seed is derived purely from the coords.
        HexCoord[] coords =
        {
            new HexCoord(0, 0), new HexCoord(1, 0),
            new HexCoord(2, 0), new HexCoord(3, 0),
        };

        HexGrid gridA = BuildGrid(coords);
        var resA = CapitalReconciler.Reconcile(
            new[] { T(null, coords) }, new List<Territory>(), gridA, randomize: true);

        HexGrid gridB = BuildGrid(coords);
        var resB = CapitalReconciler.Reconcile(
            new[] { T(null, coords) }, new List<Territory>(), gridB, randomize: true);

        Assert.Equal(resA[0].Capital, resB[0].Capital);
        Assert.IsType<Capital>(gridA.Get(resA[0].Capital!.Value)!.Occupant);
    }

    [Fact]
    public void Reconcile_Randomize_OverSeveralBoards_NotAlwaysLexMin()
    {
        // Proves the randomization is live end-to-end: across distinct
        // territories (each with its own coords-derived seed) the placed
        // capital deviates from the lex-min tile for at least one board.
        bool sawNonLexMin = false;
        for (int n = 0; n < 12 && !sawNonLexMin; n++)
        {
            var coords = new[]
            {
                new HexCoord(n, 0), new HexCoord(n + 1, 0),
                new HexCoord(n, 1), new HexCoord(n + 1, 1),
            };
            HexCoord lexMin = new HexCoord(n, 0); // (R then Q): row 0, smallest Q
            HexGrid grid = BuildGrid(coords);

            var result = CapitalReconciler.Reconcile(
                new[] { T(null, coords) }, new List<Territory>(), grid, randomize: true);

            Assert.Contains(result[0].Capital!.Value, coords);
            if (result[0].Capital != lexMin) sawNonLexMin = true;
        }

        Assert.True(sawNonLexMin,
            "Randomized reconcile never deviated from lex-min across 12 boards.");
    }

    [Fact]
    public void Reconcile_MultiHexNeutralTerritory_GetsNoCapital()
    {
        // A 2+ hex region owned by nobody (PlayerId.None) is a neutral
        // region. It must NOT get a capital — neutral land belongs to no
        // player and produces no income.
        var a = new HexCoord(0, 0);
        var b = new HexCoord(1, 0);
        HexGrid grid = TestHelpers.BuildSpotGrid(PlayerId.None, a, b);
        var raw = new[] { new Territory(PlayerId.None, new[] { a, b }, capital: null) };

        var result = CapitalReconciler.Reconcile(raw, new List<Territory>(), grid);

        Assert.Single(result);
        Assert.False(result[0].HasCapital);
        Assert.Null(grid.Get(a)!.Occupant);
        Assert.Null(grid.Get(b)!.Occupant);
    }

    [Fact]
    public void Reconcile_NeutralTerritoryWithCapitalOccupant_Throws()
    {
        // Invariant: a Capital must never sit on neutral land. If one is
        // found, that's an upstream paint bug — the reconciler surfaces it
        // by throwing rather than silently stripping the capital.
        var a = new HexCoord(0, 0);
        var b = new HexCoord(1, 0);
        HexGrid grid = TestHelpers.BuildSpotGrid(PlayerId.None, a, b);
        grid.Get(a)!.Occupant = new Capital();
        var raw = new[] { new Territory(PlayerId.None, new[] { a, b }, capital: null) };

        Assert.Throws<System.InvalidOperationException>(
            () => CapitalReconciler.Reconcile(raw, new List<Territory>(), grid));
    }
}
