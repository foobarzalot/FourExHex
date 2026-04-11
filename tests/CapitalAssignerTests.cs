using System.Collections.Generic;
using System.Linq;
using Godot;
using Xunit;

namespace FourExHex.Tests;

public class CapitalAssignerTests
{
    [Fact]
    public void Choose_EmptyCoords_ReturnsNull()
    {
        HexCoord? capital = CapitalAssigner.Choose(new List<HexCoord>());

        Assert.Null(capital);
    }

    [Fact]
    public void Choose_SingleCoord_ReturnsNull()
    {
        // Singletons are not "real" territories in Slay; no capital.
        var coords = new List<HexCoord> { new HexCoord(3, 5) };

        HexCoord? capital = CapitalAssigner.Choose(coords);

        Assert.Null(capital);
    }

    [Fact]
    public void Choose_TwoCoords_ReturnsOneOfTheInputs()
    {
        var coords = new List<HexCoord>
        {
            new HexCoord(0, 0),
            new HexCoord(1, 0),
        };

        HexCoord? capital = CapitalAssigner.Choose(coords);

        Assert.NotNull(capital);
        Assert.Contains(capital!.Value, coords);
    }

    [Fact]
    public void Choose_LargeTerritory_ReturnsAnInputCoord()
    {
        var coords = new List<HexCoord>
        {
            new HexCoord(5, 5),
            new HexCoord(6, 5),
            new HexCoord(5, 6),
            new HexCoord(6, 6),
            new HexCoord(4, 6),
            new HexCoord(7, 5),
        };

        HexCoord? capital = CapitalAssigner.Choose(coords);

        Assert.NotNull(capital);
        Assert.Contains(capital!.Value, coords);
    }

    [Fact]
    public void Choose_SameInputTwice_ReturnsSameCapital()
    {
        var coords = new List<HexCoord>
        {
            new HexCoord(2, 3),
            new HexCoord(3, 3),
            new HexCoord(2, 4),
            new HexCoord(3, 4),
        };

        HexCoord? first = CapitalAssigner.Choose(coords);
        HexCoord? second = CapitalAssigner.Choose(coords);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Choose_ReorderedInput_ReturnsSameCapital()
    {
        // Capital must not depend on enumeration order — flood-fill discovers
        // tiles in BFS order, which varies based on the seed tile.
        var ordered = new List<HexCoord>
        {
            new HexCoord(2, 3),
            new HexCoord(3, 3),
            new HexCoord(2, 4),
            new HexCoord(3, 4),
            new HexCoord(4, 3),
        };
        var reversed = ordered.AsEnumerable().Reverse().ToList();
        var scrambled = new List<HexCoord>
        {
            ordered[2], ordered[0], ordered[4], ordered[1], ordered[3],
        };

        HexCoord? fromOrdered = CapitalAssigner.Choose(ordered);
        HexCoord? fromReversed = CapitalAssigner.Choose(reversed);
        HexCoord? fromScrambled = CapitalAssigner.Choose(scrambled);

        Assert.Equal(fromOrdered, fromReversed);
        Assert.Equal(fromOrdered, fromScrambled);
    }
}
