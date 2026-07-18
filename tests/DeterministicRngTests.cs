// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Pins the game-state PRNG bit-exactly. <see cref="DeterministicRng"/>
/// is the only source of randomness on the deterministic path (the
/// System.Random ban test enforces that), so these vectors ARE the
/// cross-runtime determinism contract: if any pinned value changes, every
/// seed-derived map, campaign level, and replay stream changes with it.
/// </summary>
public class DeterministicRngTests
{
    [Fact]
    public void SameSeed_ProducesIdenticalSequence()
    {
        var a = new DeterministicRng(12345);
        var b = new DeterministicRng(12345);
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(a.NextUInt(), b.NextUInt());
        }
        Assert.Equal(a.StreamHash, b.StreamHash);
    }

    [Fact]
    public void DifferentSeeds_ProduceDifferentSequences()
    {
        var a = new DeterministicRng(1);
        var b = new DeterministicRng(2);
        bool anyDifferent = false;
        for (int i = 0; i < 16 && !anyDifferent; i++)
        {
            anyDifferent = a.NextUInt() != b.NextUInt();
        }
        Assert.True(anyDifferent, "seeds 1 and 2 produced identical 16-draw prefixes");
    }

    [Fact]
    public void NextBounded_StaysInRange_AndCoversValues()
    {
        var rng = new DeterministicRng(42);
        var seen = new HashSet<int>();
        for (int i = 0; i < 1000; i++)
        {
            int v = rng.NextBounded(100);
            Assert.InRange(v, 0, 99);
            seen.Add(v);
        }
        // A uniform generator covers most of a 100-bucket range in
        // 1000 draws; a broken/constant one cannot.
        Assert.True(seen.Count > 80, $"only {seen.Count} distinct values in 1000 draws");
    }

    [Fact]
    public void NextBounded_MinMax_StaysInRange_AndCoversValues()
    {
        var rng = new DeterministicRng(7);
        var seen = new HashSet<int>();
        for (int i = 0; i < 500; i++)
        {
            int v = rng.NextBounded(4, 10);
            Assert.InRange(v, 4, 9);
            seen.Add(v);
        }
        Assert.Equal(6, seen.Count);
    }

    [Fact]
    public void NextBounded_EdgeCases_MatchRandomSemantics()
    {
        var rng = new DeterministicRng(1);
        Assert.Equal(0, rng.NextBounded(1));
        Assert.Equal(0, rng.NextBounded(0));
        Assert.Equal(5, rng.NextBounded(5, 5));
        Assert.Equal(5, rng.NextBounded(5, 6));
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextBounded(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextBounded(3, 2));
    }

    [Fact]
    public void NextBounded_FullIntRange_StaysNonNegativeAndVaries()
    {
        var rng = new DeterministicRng(99);
        var seen = new HashSet<int>();
        for (int i = 0; i < 64; i++)
        {
            int v = rng.NextBounded(int.MaxValue);
            Assert.InRange(v, 0, int.MaxValue - 1);
            seen.Add(v);
        }
        Assert.True(seen.Count > 32);

        int w = rng.NextBounded(int.MinValue, int.MaxValue);
        Assert.InRange(w, int.MinValue, int.MaxValue - 1);
    }

    [Fact]
    public void NextFullRangeSeed_ReachesNegatives()
    {
        // Matches SeedFormat's full-32-bit-range contract: the high hex
        // digit (negative ints) must be reachable.
        var rng = new DeterministicRng(1234);
        bool sawNegative = false, sawPositive = false;
        for (int i = 0; i < 64; i++)
        {
            int v = rng.NextFullRangeSeed();
            if (v < 0) sawNegative = true;
            if (v > 0) sawPositive = true;
        }
        Assert.True(sawNegative, "no negative seed in 64 draws");
        Assert.True(sawPositive, "no positive seed in 64 draws");
    }

    [Fact]
    public void StreamHash_ChangesWithDraws_AndCoversBoundedRejections()
    {
        var rng = new DeterministicRng(5);
        ulong h0 = rng.StreamHash;
        rng.NextUInt();
        ulong h1 = rng.StreamHash;
        Assert.NotEqual(h0, h1);
        rng.NextBounded(3);
        Assert.NotEqual(h1, rng.StreamHash);
    }

    [Fact]
    public void StreamHash_IsOrderSensitive()
    {
        var a = new DeterministicRng(5);
        var b = new DeterministicRng(5);
        a.NextBounded(10);
        a.NextBounded(100);
        b.NextBounded(100);
        b.NextBounded(10);
        Assert.NotEqual(a.StreamHash, b.StreamHash);
    }

    // --- Pinned golden vectors ------------------------------------------
    // These values ARE the determinism contract. They were computed once
    // from the reference implementation and must never change: a change
    // here means every seed-derived map and campaign level changes.

    [Fact]
    public void PinnedVectors_Seed42_NextUInt()
    {
        var rng = new DeterministicRng(42);
        uint[] expected = GoldenVectors.Seed42NextUInt;
        foreach (uint e in expected)
        {
            Assert.Equal(e, rng.NextUInt());
        }
        Assert.Equal(GoldenVectors.Seed42StreamHashAfter8, rng.StreamHash);
    }

    [Fact]
    public void PinnedVectors_Seed42_NextBounded100()
    {
        var rng = new DeterministicRng(42);
        int[] expected = GoldenVectors.Seed42Bounded100;
        foreach (int e in expected)
        {
            Assert.Equal(e, rng.NextBounded(100));
        }
    }

    [Fact]
    public void PinnedVectors_NegativeSeed_MatchesGolden()
    {
        var rng = new DeterministicRng(-1);
        uint[] expected = GoldenVectors.SeedMinus1NextUInt;
        foreach (uint e in expected)
        {
            Assert.Equal(e, rng.NextUInt());
        }
    }
}

/// <summary>Golden vectors for DeterministicRngTests, computed once from
/// an independent integer replica of the algorithm and pinned forever.</summary>
internal static class GoldenVectors
{
    public static readonly uint[] Seed42NextUInt =
    {
        0xBDD73226u, 0x28EFE333u, 0x47526757u, 0x581CE1FFu,
        0x09BC585Au, 0xDE4431FAu, 0x37E9671Cu, 0xCCF635EEu,
    };

    public const ulong Seed42StreamHashAfter8 = 0x9508E861229F4D86UL;

    public static readonly int[] Seed42Bounded100 = { 74, 15, 27, 34, 3, 86, 21, 80 };

    public static readonly uint[] SeedMinus1NextUInt =
    {
        0x73B13BA2u, 0x61204305u, 0xEE4AC9FFu, 0x12F4EEB7u,
    };
}
