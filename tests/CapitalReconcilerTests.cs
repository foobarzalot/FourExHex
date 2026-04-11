using System.Collections.Generic;
using System.Linq;
using Godot;
using Xunit;

namespace FourExHex.Tests;

public class CapitalReconcilerTests
{
    private static readonly Color Red = new Color(1f, 0f, 0f);

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

    // --- Stomping ---------------------------------------------------------

    [Fact]
    public void Reconcile_NewCapitalPlacedOverUnit_DestroysTheUnit()
    {
        // Two-tile territory, both occupied by peasants. Reconciler must
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
}
