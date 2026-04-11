using System.Collections.Generic;
using System.Linq;
using Godot;
using Xunit;

namespace FourExHex.Tests;

public class TerritoryReconcilerTests
{
    private static readonly Color Red = new Color(1f, 0f, 0f);

    private static Territory T(Color owner, HexCoord capital, params HexCoord[] coords) =>
        new Territory(owner, coords, capital);

    private static Territory TNoCapital(Color owner, params HexCoord[] coords) =>
        new Territory(owner, coords, capital: null);

    // --- Trivial pass-throughs -------------------------------------------

    [Fact]
    public void NoMerge_SingleInheritedCapital_Unchanged()
    {
        // Old: one territory with capital (0,0).
        // New: same territory shape, same capital.
        var old1 = T(Red, new HexCoord(0, 0), new HexCoord(0, 0), new HexCoord(1, 0));
        var newRaw = T(Red, new HexCoord(0, 0), new HexCoord(0, 0), new HexCoord(1, 0));

        var corrected = TerritoryReconciler.OverrideMergeWinners(
            new[] { newRaw }, new[] { old1 });

        Assert.Single(corrected);
        Assert.Equal(new HexCoord(0, 0), corrected[0].Capital);
    }

    [Fact]
    public void SingleInheritedOldCapital_RawLexMinPicksDifferent_OverridesToInherited()
    {
        // Old: a 2-cell territory with capital (5, 0). (No cells at (0, 0).)
        // New: a 3-cell territory containing (5, 0) PLUS a smaller coord
        // (0, 0). CapitalAssigner's natural lex-min pick on the new
        // territory would be (0, 0), but we must override to (5, 0)
        // because that's the inherited old capital.
        var old1 = T(Red, new HexCoord(5, 0),
            new HexCoord(5, 0), new HexCoord(6, 0));
        var newRaw = T(Red, new HexCoord(0, 0),
            new HexCoord(0, 0), new HexCoord(5, 0), new HexCoord(6, 0));

        var corrected = TerritoryReconciler.OverrideMergeWinners(
            new[] { newRaw }, new[] { old1 });

        Assert.Single(corrected);
        Assert.Equal(new HexCoord(5, 0), corrected[0].Capital);
    }

    [Fact]
    public void Merge_OneOldHasCapital_OtherIsSingleton_KeepsCapitalizedOldCapital()
    {
        // Old: 2-cell Red territory with capital (5, 0), plus a singleton
        // Red tile at (0, 0) (no capital).
        // Player captures a bridging hex, merging them into a 3-cell
        // territory. The merged territory's capital should remain (5, 0)
        // because the singleton contributed no capital to fight for the
        // position.
        var withCapital = T(Red, new HexCoord(5, 0),
            new HexCoord(5, 0), new HexCoord(6, 0));
        var singleton = TNoCapital(Red, new HexCoord(0, 0));

        // CapitalAssigner on the merged coords would pick lex-min (0, 0).
        var newRaw = T(Red, new HexCoord(0, 0),
            new HexCoord(0, 0), new HexCoord(5, 0), new HexCoord(6, 0));

        var corrected = TerritoryReconciler.OverrideMergeWinners(
            new[] { newRaw }, new[] { withCapital, singleton });

        Assert.Single(corrected);
        Assert.Equal(new HexCoord(5, 0), corrected[0].Capital);
    }

    [Fact]
    public void NoMerge_ZeroInheritedCapitals_Unchanged()
    {
        // Old: capital (0,0) in some territory.
        // New: a territory that doesn't contain (0,0) at all — e.g., it
        // was spawned by a split and gets its own lex-min capital.
        var old1 = T(Red, new HexCoord(0, 0), new HexCoord(0, 0), new HexCoord(1, 0));
        var freshNew = T(Red, new HexCoord(5, 5), new HexCoord(5, 5), new HexCoord(6, 5));

        var corrected = TerritoryReconciler.OverrideMergeWinners(
            new[] { freshNew }, new[] { old1 });

        Assert.Single(corrected);
        Assert.Equal(new HexCoord(5, 5), corrected[0].Capital);
    }

    [Fact]
    public void NoMerge_SingletonTerritory_Unchanged()
    {
        var old1 = TNoCapital(Red, new HexCoord(0, 0));
        var newRaw = TNoCapital(Red, new HexCoord(0, 0));

        var corrected = TerritoryReconciler.OverrideMergeWinners(
            new[] { newRaw }, new[] { old1 });

        Assert.Single(corrected);
        Assert.False(corrected[0].HasCapital);
    }

    // --- Merge cases -----------------------------------------------------

    [Fact]
    public void Merge_LargerOldTerritoryCapitalWins()
    {
        // Old: A has capital (0,0), size 5.
        //      B has capital (10,10), size 2.
        // New: merged territory containing both old capitals. Raw
        // TerritoryFinder would choose the lex-min capital (something
        // smaller than (10,10)). After reconciliation, the winner must
        // be (0,0) because A was larger.
        var a = T(Red, new HexCoord(0, 0),
            new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(2, 0),
            new HexCoord(3, 0), new HexCoord(4, 0));
        var b = T(Red, new HexCoord(10, 10),
            new HexCoord(10, 10), new HexCoord(11, 10));

        var mergedCoords = new[]
        {
            new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(2, 0),
            new HexCoord(3, 0), new HexCoord(4, 0),
            new HexCoord(5, 0), // the bridging capture
            new HexCoord(10, 10), new HexCoord(11, 10),
        };
        // Raw capital would be (0,0) here (lex-min) — which happens to
        // match A's capital. Use a raw capital that ISN'T A's so the test
        // actually observes the override.
        var raw = T(Red, new HexCoord(5, 0), mergedCoords);

        var corrected = TerritoryReconciler.OverrideMergeWinners(
            new[] { raw }, new[] { a, b });

        Assert.Single(corrected);
        Assert.Equal(new HexCoord(0, 0), corrected[0].Capital);
    }

    [Fact]
    public void Merge_BiggerIsSecondHalf_StillWins()
    {
        // Verify the rule isn't accidentally picking the first capital it
        // sees. A=size 2, B=size 5. B wins.
        var a = T(Red, new HexCoord(0, 0),
            new HexCoord(0, 0), new HexCoord(1, 0));
        var b = T(Red, new HexCoord(10, 10),
            new HexCoord(10, 10), new HexCoord(11, 10), new HexCoord(12, 10),
            new HexCoord(13, 10), new HexCoord(14, 10));

        var mergedCoords = new[]
        {
            new HexCoord(0, 0), new HexCoord(1, 0),
            new HexCoord(5, 0),
            new HexCoord(10, 10), new HexCoord(11, 10), new HexCoord(12, 10),
            new HexCoord(13, 10), new HexCoord(14, 10),
        };
        var raw = T(Red, new HexCoord(0, 0), mergedCoords);

        var corrected = TerritoryReconciler.OverrideMergeWinners(
            new[] { raw }, new[] { a, b });

        Assert.Single(corrected);
        Assert.Equal(new HexCoord(10, 10), corrected[0].Capital);
    }

    [Fact]
    public void Merge_TiebreakUsesLexMin()
    {
        // Both old territories are size 3. Tiebreaker picks the lex-min
        // of the two *old capitals*, which is (0,0) over (5,5).
        var a = T(Red, new HexCoord(0, 0),
            new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(2, 0));
        var b = T(Red, new HexCoord(5, 5),
            new HexCoord(5, 5), new HexCoord(6, 5), new HexCoord(7, 5));

        var mergedCoords = new[]
        {
            new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(2, 0),
            new HexCoord(3, 0),
            new HexCoord(5, 5), new HexCoord(6, 5), new HexCoord(7, 5),
        };
        var raw = T(Red, new HexCoord(3, 0), mergedCoords);

        var corrected = TerritoryReconciler.OverrideMergeWinners(
            new[] { raw }, new[] { a, b });

        Assert.Single(corrected);
        Assert.Equal(new HexCoord(0, 0), corrected[0].Capital);
    }

    [Fact]
    public void Merge_ThreeWay_LargestWins()
    {
        var smallA = T(Red, new HexCoord(0, 0),
            new HexCoord(0, 0), new HexCoord(1, 0));
        var bigB = T(Red, new HexCoord(5, 5),
            new HexCoord(5, 5), new HexCoord(6, 5), new HexCoord(7, 5),
            new HexCoord(8, 5), new HexCoord(9, 5));
        var smallC = T(Red, new HexCoord(10, 10),
            new HexCoord(10, 10), new HexCoord(11, 10), new HexCoord(12, 10));

        var mergedCoords = new[]
        {
            new HexCoord(0, 0), new HexCoord(1, 0),
            new HexCoord(5, 5), new HexCoord(6, 5), new HexCoord(7, 5),
            new HexCoord(8, 5), new HexCoord(9, 5),
            new HexCoord(10, 10), new HexCoord(11, 10), new HexCoord(12, 10),
        };
        var raw = T(Red, new HexCoord(0, 0), mergedCoords);

        var corrected = TerritoryReconciler.OverrideMergeWinners(
            new[] { raw }, new[] { smallA, bigB, smallC });

        Assert.Single(corrected);
        Assert.Equal(new HexCoord(5, 5), corrected[0].Capital);
    }

    // --- Split ------------------------------------------------------------

    [Fact]
    public void Split_PieceWithOldCapital_InheritsIt()
    {
        // Old: one territory of size 5 with capital (0,0).
        // New: two pieces — piece A contains (0,0), piece B is disconnected.
        var old1 = T(Red, new HexCoord(0, 0),
            new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(2, 0),
            new HexCoord(10, 10), new HexCoord(11, 10));

        var pieceA = T(Red, new HexCoord(0, 0),
            new HexCoord(0, 0), new HexCoord(1, 0));
        var pieceB = T(Red, new HexCoord(10, 10),
            new HexCoord(10, 10), new HexCoord(11, 10));

        var corrected = TerritoryReconciler.OverrideMergeWinners(
            new[] { pieceA, pieceB }, new[] { old1 });

        Assert.Equal(2, corrected.Count);
        Assert.Contains(corrected, t => t.Capital == new HexCoord(0, 0));
        Assert.Contains(corrected, t => t.Capital == new HexCoord(10, 10));
    }
}
