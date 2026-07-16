// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using Xunit;

namespace FourExHex.Tests;

public class KeyboardAvoidanceTests
{
    private const float Tolerance = 0.0001f;

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(-300f)]
    public void LiftFor_NoKeyboard_IsZero(float keyboardHeight)
    {
        Assert.Equal(0f,
            KeyboardAvoidance.LiftFor(
                fieldBottomY: 700f, viewportHeight: 800f,
                keyboardLogicalHeight: keyboardHeight, margin: 16f),
            Tolerance);
    }

    [Fact]
    public void LiftFor_FieldAlreadyAboveKeyboard_IsZero()
    {
        // Keyboard top at y=500; field bottom at 400 + 16 margin = 416 clears it.
        Assert.Equal(0f,
            KeyboardAvoidance.LiftFor(
                fieldBottomY: 400f, viewportHeight: 800f,
                keyboardLogicalHeight: 300f, margin: 16f),
            Tolerance);
    }

    [Fact]
    public void LiftFor_FieldExactlyAtKeyboardTopWithMargin_IsZero()
    {
        // Keyboard top at y=500; field bottom 484 + 16 margin sits exactly on it.
        Assert.Equal(0f,
            KeyboardAvoidance.LiftFor(
                fieldBottomY: 484f, viewportHeight: 800f,
                keyboardLogicalHeight: 300f, margin: 16f),
            Tolerance);
    }

    [Fact]
    public void LiftFor_OccludedField_LiftsByExactOverlap()
    {
        // Keyboard top at y=500; field bottom at 700 + 16 margin = 716 → lift 216.
        Assert.Equal(216f,
            KeyboardAvoidance.LiftFor(
                fieldBottomY: 700f, viewportHeight: 800f,
                keyboardLogicalHeight: 300f, margin: 16f),
            Tolerance);
    }

    [Fact]
    public void LiftFor_MarginIsHonored()
    {
        // Same geometry, zero margin → lift shrinks by exactly the margin.
        Assert.Equal(200f,
            KeyboardAvoidance.LiftFor(
                fieldBottomY: 700f, viewportHeight: 800f,
                keyboardLogicalHeight: 300f, margin: 0f),
            Tolerance);
    }

    [Fact]
    public void LiftFor_KeyboardTallerThanFieldOffset_StillFiniteLift()
    {
        // Pathological short viewport: lift is whatever it takes, never NaN/negative.
        float lift = KeyboardAvoidance.LiftFor(
            fieldBottomY: 380f, viewportHeight: 400f,
            keyboardLogicalHeight: 350f, margin: 16f);
        Assert.Equal(346f, lift, Tolerance);
    }
}
