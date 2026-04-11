using Godot;
using Xunit;

namespace FourExHex.Tests;

public class HexPixelTests
{
    private const float Size = 48f;
    private const float Tolerance = 0.0001f;

    [Fact]
    public void FromPixel_Origin_ReturnsZeroZero()
    {
        HexCoord hex = HexCoord.FromPixel(Vector2.Zero, Size);
        Assert.Equal(new HexCoord(0, 0), hex);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(3, -2)]
    [InlineData(5, 7)]
    [InlineData(-4, 3)]
    public void FromPixel_AtHexCenter_ReturnsThatHex(int q, int r)
    {
        var original = new HexCoord(q, r);
        Vector2 center = original.ToPixel(Size);

        HexCoord recovered = HexCoord.FromPixel(center, Size);

        Assert.Equal(original, recovered);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    [InlineData(3, -2)]
    public void FromPixel_NearHexCenter_ReturnsThatHex(int q, int r)
    {
        // A small jitter inside the hex should still round to the same hex.
        // The inradius (center to edge) for size s is s * sqrt(3)/2, so any
        // offset less than that magnitude along an axis stays inside.
        var original = new HexCoord(q, r);
        Vector2 center = original.ToPixel(Size);
        float jitter = Size * 0.3f;

        Assert.Equal(original, HexCoord.FromPixel(center + new Vector2(jitter, 0), Size));
        Assert.Equal(original, HexCoord.FromPixel(center + new Vector2(-jitter, 0), Size));
        Assert.Equal(original, HexCoord.FromPixel(center + new Vector2(0, jitter), Size));
        Assert.Equal(original, HexCoord.FromPixel(center + new Vector2(0, -jitter), Size));
    }

    [Fact]
    public void FromPixel_RoundTripOverGrid_MatchesOriginal()
    {
        // Sweep a reasonable axial range and confirm center round-trips.
        for (int q = -8; q <= 8; q++)
        {
            for (int r = -8; r <= 8; r++)
            {
                var original = new HexCoord(q, r);
                Vector2 center = original.ToPixel(Size);
                HexCoord recovered = HexCoord.FromPixel(center, Size);
                Assert.Equal(original, recovered);
            }
        }
    }

    [Fact]
    public void ToPixel_Origin_IsZero()
    {
        Vector2 center = new HexCoord(0, 0).ToPixel(Size);
        Assert.True(Mathf.Abs(center.X) < Tolerance);
        Assert.True(Mathf.Abs(center.Y) < Tolerance);
    }

    [Fact]
    public void ToPixel_KnownEastNeighbor_IsOneHexWidthEast()
    {
        // The east neighbor of (0,0) is at axial (1,0); its x-distance
        // from origin is exactly one hex width (sqrt(3) * size).
        Vector2 center = new HexCoord(1, 0).ToPixel(Size);
        float expectedX = Size * Mathf.Sqrt(3f);

        Assert.True(Mathf.Abs(center.X - expectedX) < Tolerance);
        Assert.True(Mathf.Abs(center.Y) < Tolerance);
    }
}
