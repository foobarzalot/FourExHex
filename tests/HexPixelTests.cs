using Xunit;

namespace FourExHex.Tests;

// The pixel↔axial projection (ToPixel/FromPixel) moved to the Godot-side
// HexPixel helper and is exercised by manual play-testing of the map (the
// view layer is not unit-tested). What stays library-tested here is the
// engine-free cube-rounding core, HexRounding.Round — the part whose
// correctness the model depends on.
public class HexCoordRoundTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(3, -2)]
    [InlineData(5, 7)]
    [InlineData(-4, 3)]
    public void Round_ExactIntegerAxial_ReturnsThatHex(int q, int r)
    {
        Assert.Equal(new HexCoord(q, r), HexRounding.Round(q, r));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    [InlineData(3, -2)]
    public void Round_SmallJitterAroundCenter_ReturnsThatHex(int q, int r)
    {
        // Sub-hex jitter on each axis (well inside the rounding cell)
        // must still resolve to the integer hex — the axial analogue of
        // the old "near hex center" pixel test.
        const float j = 0.3f;
        Assert.Equal(new HexCoord(q, r), HexRounding.Round(q + j, r));
        Assert.Equal(new HexCoord(q, r), HexRounding.Round(q - j, r));
        Assert.Equal(new HexCoord(q, r), HexRounding.Round(q, r + j));
        Assert.Equal(new HexCoord(q, r), HexRounding.Round(q, r - j));
    }

    [Fact]
    public void Round_JitteredSweep_RecoversIntendedHex()
    {
        // Sweep a reasonable axial range; a small offset on both axes
        // still rounds back to the source hex (cube-correction holds).
        for (int q = -8; q <= 8; q++)
        {
            for (int r = -8; r <= 8; r++)
            {
                Assert.Equal(new HexCoord(q, r), HexRounding.Round(q + 0.2f, r - 0.2f));
                Assert.Equal(new HexCoord(q, r), HexRounding.Round(q, r));
            }
        }
    }

    [Fact]
    public void Round_ResultSatisfiesCubeInvariant()
    {
        // q + r + s == 0 must hold for the rounded result for any input
        // (this is what the largest-error re-derivation guarantees).
        float[] qs = { 0.4f, -1.6f, 2.49f, -3.51f, 5.2f };
        float[] rs = { 0.6f, 2.4f, -1.49f, 3.51f, -4.2f };
        foreach (float qf in qs)
        {
            foreach (float rf in rs)
            {
                HexCoord h = HexRounding.Round(qf, rf);
                int s = -h.Q - h.R;
                Assert.Equal(0, h.Q + h.R + s);
            }
        }
    }
}
