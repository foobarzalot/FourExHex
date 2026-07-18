// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;

/// <summary>
/// The game-state PRNG: an integer-only splitmix64 generator with
/// unbiased bounded draws (Lemire multiply-high with rejection). Every
/// random draw on the deterministic game-state path (map generation,
/// per-turn gameplay RNG, campaign level derivation, capital and tide
/// tie-breaks) goes through this class; <see cref="System.Random"/> is
/// banned in Model and Controller because its bounded overloads hide a
/// BCL <c>double</c> multiply on the deterministic path. All math here
/// is <c>uint</c>/<c>ulong</c>, so reproducibility depends only on this
/// code — bit-exact across runtimes and architectures, pinned by
/// DeterministicRngTests.
/// </summary>
public sealed class DeterministicRng
{
    private const ulong SplitMix64Gamma = 0x9E3779B97F4A7C15UL;
    private const ulong Fnv1a64Offset = 0xCBF29CE484222325UL;
    private const ulong Fnv1a64Prime = 0x00000100000001B3UL;

    private ulong _state;
    private ulong _streamHash = Fnv1a64Offset;

    /// <summary>Seed with a 32-bit master/sub seed (sign-agnostic: the
    /// bit pattern is the identity, matching <see cref="SeedFormat"/>).</summary>
    public DeterministicRng(int seed)
    {
        _state = (uint)seed;
    }

    /// <summary>FNV-1a-64 fold of the consumption trace so far: every raw
    /// draw (including rejection redraws) plus each bounded call's bound,
    /// so two call sequences that consume the same raws through different
    /// bounds still digest differently.</summary>
    public ulong StreamHash => _streamHash;

    /// <summary>Next raw 32-bit draw (high half of the splitmix64 output).</summary>
    public uint NextUInt()
    {
        unchecked
        {
            _state += SplitMix64Gamma;
            ulong z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            uint result = (uint)(z >> 32);
            _streamHash = (_streamHash ^ result) * Fnv1a64Prime;
            return result;
        }
    }

    /// <summary>Uniform draw in [0, maxExclusive); 0 when maxExclusive is 0
    /// (matching <see cref="Random.Next(int)"/> semantics).</summary>
    public int NextBounded(int maxExclusive)
    {
        if (maxExclusive < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxExclusive),
                maxExclusive, "maxExclusive must be non-negative.");
        }
        return (int)NextBoundedUInt((uint)maxExclusive);
    }

    /// <summary>Uniform draw in [minInclusive, maxExclusive); minInclusive
    /// when the range is empty (matching <see cref="Random.Next(int, int)"/>).</summary>
    public int NextBounded(int minInclusive, int maxExclusive)
    {
        if (minInclusive > maxExclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(minInclusive),
                minInclusive, "minInclusive must not exceed maxExclusive.");
        }
        uint range = unchecked((uint)((long)maxExclusive - minInclusive));
        return unchecked(minInclusive + (int)NextBoundedUInt(range));
    }

    /// <summary>Fresh master seed across the full 32-bit range (negatives
    /// reachable, unlike <see cref="Random.Next()"/>) — the integer twin
    /// of <see cref="SeedFormat.NextSeed"/>.</summary>
    public int NextFullRangeSeed() => unchecked((int)NextUInt());

    /// <summary>Unbiased uniform draw in [0, n) via Lemire multiply-high
    /// with rejection; 0 when n is 0. Integer math only.</summary>
    private uint NextBoundedUInt(uint n)
    {
        if (n == 0) return 0;
        unchecked
        {
            _streamHash = (_streamHash ^ n) * Fnv1a64Prime;
            ulong m = (ulong)NextUInt() * n;
            uint low = (uint)m;
            if (low < n)
            {
                // Reject the partial top interval so every residue is
                // covered by exactly floor(2^32 / n) raw values.
                uint threshold = (uint)(-(int)n) % n;
                while (low < threshold)
                {
                    m = (ulong)NextUInt() * n;
                    low = (uint)m;
                }
            }
            return (uint)(m >> 32);
        }
    }
}
