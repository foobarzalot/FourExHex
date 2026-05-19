using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

public class PlayerIdTests
{
    [Fact]
    public void None_IsDefault_AndIsNone()
    {
        Assert.True(PlayerId.None.IsNone);
        Assert.True(default(PlayerId).IsNone);
        Assert.Equal(PlayerId.None, default(PlayerId));
    }

    [Fact]
    public void FromIndex_RoundTrips()
    {
        Assert.Equal(0, PlayerId.FromIndex(0).Index);
        Assert.Equal(5, PlayerId.FromIndex(5).Index);
        Assert.False(PlayerId.FromIndex(0).IsNone);
        Assert.False(PlayerId.FromIndex(5).IsNone);
    }

    [Fact]
    public void Equality_IsByValue()
    {
        PlayerId a = PlayerId.FromIndex(2);
        PlayerId b = PlayerId.FromIndex(2);
        PlayerId c = PlayerId.FromIndex(3);

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.False(a != b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());

        Assert.NotEqual(a, c);
        Assert.True(a != c);
        Assert.False(a == c);
    }

    [Fact]
    public void None_NotEqual_ToFirstPlayer()
    {
        Assert.NotEqual(PlayerId.None, PlayerId.FromIndex(0));
        Assert.True(PlayerId.None != PlayerId.FromIndex(0));
    }

    [Fact]
    public void UsableAsDictionaryKey()
    {
        var dict = new Dictionary<PlayerId, int>
        {
            [PlayerId.FromIndex(0)] = 10,
            [PlayerId.FromIndex(1)] = 20,
            [PlayerId.None] = -1,
        };

        Assert.Equal(10, dict[PlayerId.FromIndex(0)]);
        Assert.Equal(20, dict[PlayerId.FromIndex(1)]);
        Assert.Equal(-1, dict[PlayerId.None]);
        Assert.False(dict.ContainsKey(PlayerId.FromIndex(2)));
    }

    [Fact]
    public void CompareTo_Orders_NoneFirst_ThenByIndex()
    {
        var ids = new List<PlayerId>
        {
            PlayerId.FromIndex(2),
            PlayerId.None,
            PlayerId.FromIndex(0),
            PlayerId.FromIndex(1),
        };

        ids.Sort();

        Assert.Equal(
            new[] { PlayerId.None, PlayerId.FromIndex(0), PlayerId.FromIndex(1), PlayerId.FromIndex(2) },
            ids);
    }

    [Fact]
    public void FromIndex_Negative_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PlayerId.FromIndex(-1));
    }

    [Fact]
    public void FromIndex_Overflow_Throws()
    {
        // _raw is a byte (0 = None, index+1 otherwise) so index 255 overflows.
        Assert.Throws<OverflowException>(() => PlayerId.FromIndex(255));
        // The last representable real player id.
        Assert.Equal(254, PlayerId.FromIndex(254).Index);
    }

    [Fact]
    public void ToString_DistinguishesNoneFromPlayers()
    {
        Assert.Equal("None", PlayerId.None.ToString());
        Assert.Equal("P0", PlayerId.FromIndex(0).ToString());
        Assert.Equal("P4", PlayerId.FromIndex(4).ToString());
    }
}
