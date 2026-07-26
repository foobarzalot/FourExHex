// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Pins the in-app determinism probe to the desktop baseline. The golden
/// values below are the cross-platform contract (issue #59): the same
/// triple must come out of the probe on every machine, runtime, and
/// build target — a mismatch on any platform is a real cross-runtime
/// determinism bug, and a mismatch here means the game rules or RNG
/// changed and every platform's fingerprint moved with them.
/// </summary>
public class DeterminismProbeTests
{
    private static DeterminismProbeResult Run(int seed = DeterminismProbe.DefaultSeed) =>
        DeterminismProbe.Run(new MockHexMapView(), new MockHudView(), seed);

    [Fact]
    public void Probe_IsReproducible()
    {
        DeterminismProbeResult a = Run();
        DeterminismProbeResult b = Run();
        Assert.Equal(a, b);
    }

    [Fact]
    public void Probe_DefaultSeed_MatchesDesktopBaseline()
    {
        // Must equal the FOUREXHEX_6AI_QUICK FOUREXHEX_SEED=42 headless
        // run's digest lines — the probe IS that run, minus env vars.
        DeterminismProbeResult r = Run();
        Assert.Equal(0x3B760EDA60BEC1F8UL, r.MapGenRngStreamHash);
        Assert.Equal(
            "c82f81bd152f11a41a8edf42a78ca466128e5135bb4f4059895bd15231203fdf",
            r.FinalChecksum);
        Assert.Equal(0x5C408A166E9A5143UL, r.RngStreamDigest);
    }

    [Fact]
    public void Probe_DifferentSeed_ProducesDifferentFingerprint()
    {
        DeterminismProbeResult a = Run();
        DeterminismProbeResult b = Run(seed: 43);
        Assert.NotEqual(a.MapGenRngStreamHash, b.MapGenRngStreamHash);
        Assert.NotEqual(a.FinalChecksum, b.FinalChecksum);
        Assert.NotEqual(a.RngStreamDigest, b.RngStreamDigest);
    }
}
