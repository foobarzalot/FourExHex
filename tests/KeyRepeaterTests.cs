// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using Xunit;

namespace FourExHex.Tests;

public class KeyRepeaterTests
{
    private const float Delay = 0.25f;
    private const float Interval = 0.05f;

    private static KeyRepeater Held()
    {
        var repeater = new KeyRepeater(Delay, Interval);
        repeater.Press();
        return repeater;
    }

    [Fact]
    public void Idle_AdvanceEmitsNothing()
    {
        var repeater = new KeyRepeater(Delay, Interval);
        Assert.False(repeater.Held);
        Assert.Equal(0, repeater.Advance(1f));
    }

    [Fact]
    public void Press_MarksHeld()
    {
        Assert.True(Held().Held);
    }

    [Fact]
    public void BeforeInitialDelay_EmitsNothing()
    {
        // The press itself already moved one row; the repeat is what waits.
        Assert.Equal(0, Held().Advance(0.1f));
    }

    [Fact]
    public void AtInitialDelay_EmitsFirstRepeat()
    {
        Assert.Equal(1, Held().Advance(Delay));
    }

    [Fact]
    public void AfterFirstRepeat_EmitsOnePerInterval()
    {
        KeyRepeater repeater = Held();
        Assert.Equal(1, repeater.Advance(Delay));
        Assert.Equal(1, repeater.Advance(0.06f));
        Assert.Equal(1, repeater.Advance(0.06f));
    }

    [Fact]
    public void ShortFrames_AccumulateInsteadOfDropping()
    {
        // Frames shorter than the interval must not each round down to zero
        // forever — the elapsed time carries across them.
        KeyRepeater repeater = Held();
        Assert.Equal(0, repeater.Advance(0.24f));
        Assert.Equal(1, repeater.Advance(0.03f));   // 0.27 elapsed
        Assert.Equal(1, repeater.Advance(0.06f));   // 0.33 elapsed
    }

    [Fact]
    public void LongFrame_EmitsEveryStepThatCameDue()
    {
        // A hitched frame owes every step the hold earned: 0.25 delay, then
        // 0.22 / 0.05 = 4 more. Deliberately not a whole number of intervals —
        // a step falling exactly on a boundary is decided by float rounding.
        Assert.Equal(5, Held().Advance(0.47f));
    }

    [Fact]
    public void Release_StopsRepeating()
    {
        KeyRepeater repeater = Held();
        Assert.Equal(1, repeater.Advance(Delay));
        repeater.Release();
        Assert.False(repeater.Held);
        Assert.Equal(0, repeater.Advance(10f));
    }

    [Fact]
    public void RepressRestartsTheInitialDelay()
    {
        // Reversing direction is a fresh press — it must not inherit the old
        // hold's momentum and fire immediately.
        KeyRepeater repeater = Held();
        Assert.Equal(1, repeater.Advance(Delay));
        repeater.Press();
        Assert.Equal(0, repeater.Advance(0.1f));
        Assert.Equal(1, repeater.Advance(0.17f));   // 0.27 held — one step due
    }
}
