using System;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// The master seed is a full 32-bit value shown and edited as 8 hex
/// digits. <see cref="SeedFormat"/> is the single place that formats,
/// parses, and randomizes it; these tests pin the sign-agnostic
/// round-trip and the full-range randomization that the play-setup
/// field, the in-game label, and the random fallbacks all rely on.
/// </summary>
public class SeedFormatTests
{
    [Theory]
    [InlineData(0, "00000000")]
    [InlineData(-1, "FFFFFFFF")]
    [InlineData(0x2A, "0000002A")]
    [InlineData(0x12345678, "12345678")]
    public void ToHex_FormatsAsEightUppercaseDigits(int seed, string expected)
    {
        Assert.Equal(expected, SeedFormat.ToHex(seed));
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(0x12345678)]
    public void ToHex_TryParseHex_RoundTrips(int seed)
    {
        Assert.True(SeedFormat.TryParseHex(SeedFormat.ToHex(seed), out int parsed));
        Assert.Equal(seed, parsed);
    }

    [Theory]
    [InlineData("ff", 255)]
    [InlineData("FFFFFFFF", -1)]
    [InlineData("0", 0)]
    [InlineData("2a", 0x2A)]
    public void TryParseHex_AcceptsValidHex(string text, int expected)
    {
        Assert.True(SeedFormat.TryParseHex(text, out int parsed));
        Assert.Equal(expected, parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("GG")]
    [InlineData("123456789")] // >8 hex digits overflows uint
    [InlineData("  ")]
    public void TryParseHex_RejectsInvalid(string? text)
    {
        Assert.False(SeedFormat.TryParseHex(text, out int parsed));
        Assert.Equal(0, parsed);
    }

    [Fact]
    public void NextSeed_IsDeterministicForAGivenRng()
    {
        Assert.Equal(
            SeedFormat.NextSeed(new Random(123)),
            SeedFormat.NextSeed(new Random(123)));
    }

    [Fact]
    public void NextSeed_ReachesNegativeValues()
    {
        // Random.Next() only spans [0, int.MaxValue]; the new helper must
        // span the full 32-bit range, so the high bit (negative ints)
        // must be reachable. Draw many and assert at least one is negative.
        var rng = new Random(7);
        bool sawNegative = false;
        for (int i = 0; i < 200 && !sawNegative; i++)
        {
            if (SeedFormat.NextSeed(rng) < 0) sawNegative = true;
        }
        Assert.True(sawNegative);
    }
}
