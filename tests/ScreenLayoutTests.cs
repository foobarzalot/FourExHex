// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using Xunit;

namespace FourExHex.Tests;

public class ScreenLayoutTests
{
    [Fact]
    public void Resolve_WiderThanTall_IsLandscape()
    {
        Assert.Equal(ScreenOrientation.Landscape, ScreenLayout.Resolve(1280f, 720f));
    }

    [Fact]
    public void Resolve_TallerThanWide_IsPortrait()
    {
        Assert.Equal(ScreenOrientation.Portrait, ScreenLayout.Resolve(720f, 1280f));
    }

    [Fact]
    public void Resolve_Square_TieGoesToLandscape()
    {
        Assert.Equal(ScreenOrientation.Landscape, ScreenLayout.Resolve(1000f, 1000f));
    }

    // IsCompact — unified phone↔tablet breakpoint for the D1 "Roles Split"
    // HUD. Threshold = ScreenLayout.CompactBreakpointPx (700) logical px on
    // min(w,h); ±32 px dead-band prevents thrash on a window resize through
    // the boundary. Hard requirement: every phone we test must hit compact;
    // every tablet must hit expanded.

    [Fact]
    public void IsCompact_RealPhones_AlwaysCompact()
    {
        // Real on-device logical viewports for the two reference phones,
        // per ARCHITECTURE.md (post-mobile-DPI-floor).
        Assert.True(ScreenLayout.IsCompact(507f, 1097f, prevWasCompact: false), "iPhone 13 mini portrait");
        Assert.True(ScreenLayout.IsCompact(1097f, 507f, prevWasCompact: false), "iPhone 13 mini landscape");
        Assert.True(ScreenLayout.IsCompact(486f, 999f, prevWasCompact: false),  "S9 portrait");
        Assert.True(ScreenLayout.IsCompact(999f, 486f, prevWasCompact: false),  "S9 landscape");
    }

    [Fact]
    public void IsCompact_DevReproPhones_AlwaysCompact()
    {
        // The Option B Mac repros per RELEASE.md. These are NOT the real
        // device factors — they're a developer convenience — but the user
        // tests against them, so they must also resolve to compact.
        Assert.True(ScreenLayout.IsCompact(625f, 1353f, prevWasCompact: false), "iPhone 13 mini Option B portrait");
        Assert.True(ScreenLayout.IsCompact(1353f, 625f, prevWasCompact: false), "iPhone 13 mini Option B landscape");
    }

    [Fact]
    public void IsCompact_Tablets_AlwaysExpanded()
    {
        Assert.False(ScreenLayout.IsCompact(768f, 1024f, prevWasCompact: false),  "iPad mini portrait");
        Assert.False(ScreenLayout.IsCompact(1024f, 768f, prevWasCompact: false),  "iPad mini landscape");
        Assert.False(ScreenLayout.IsCompact(1024f, 1366f, prevWasCompact: false), "iPad Pro portrait");
    }

    [Fact]
    public void IsCompact_UsesMinSide_TallNarrowIsCompact()
    {
        // 480×1200 — width is the short side; tall portrait phone.
        Assert.True(ScreenLayout.IsCompact(480f, 1200f, prevWasCompact: false));
    }

    [Fact]
    public void IsCompact_AtCenter_HoldsPriorState()
    {
        // min = breakpoint sits inside the dead-band → no flip either way.
        float min = ScreenLayout.CompactBreakpointPx;
        Assert.False(ScreenLayout.IsCompact(min, min + 200f, prevWasCompact: false));
        Assert.True(ScreenLayout.IsCompact(min, min + 200f, prevWasCompact: true));
    }

    [Fact]
    public void IsCompact_InsideDeadBand_HoldsPriorState()
    {
        // 5 px below breakpoint — inside the dead-band, state holds.
        float just_below = ScreenLayout.CompactBreakpointPx - 5f;
        float just_above = ScreenLayout.CompactBreakpointPx + 20f;
        Assert.False(ScreenLayout.IsCompact(just_below, 1000f, prevWasCompact: false));
        Assert.True(ScreenLayout.IsCompact(just_above,  1000f, prevWasCompact: true));
    }

    [Fact]
    public void IsCompact_BelowLowerThreshold_FlipsExpandedToCompact()
    {
        // 45 px below — outside the dead-band, expanded → compact.
        float below = ScreenLayout.CompactBreakpointPx - 45f;
        Assert.True(ScreenLayout.IsCompact(below, 1000f, prevWasCompact: false));
    }

    [Fact]
    public void IsCompact_AboveUpperThreshold_FlipsCompactToExpanded()
    {
        // 45 px above — outside the dead-band, compact → expanded.
        float above = ScreenLayout.CompactBreakpointPx + 45f;
        Assert.False(ScreenLayout.IsCompact(above, 1000f, prevWasCompact: true));
    }

    [Fact]
    public void IsCompact_CustomDeadBand_ShiftsThresholds()
    {
        // With band=0 the threshold is exactly the breakpoint, no hysteresis.
        float bp = ScreenLayout.CompactBreakpointPx;
        Assert.True(ScreenLayout.IsCompact(bp - 1f, 1000f, prevWasCompact: false, deadBand: 0f));
        Assert.False(ScreenLayout.IsCompact(bp,      1000f, prevWasCompact: false, deadBand: 0f));
        Assert.False(ScreenLayout.IsCompact(bp + 1f, 1000f, prevWasCompact: true,  deadBand: 0f));
    }
}
