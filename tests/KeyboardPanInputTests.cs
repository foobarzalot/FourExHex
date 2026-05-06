using System.Collections.Generic;
using Godot;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Unit tests for <see cref="KeyboardPanInput.ComputeDirection"/>. The
/// helper is the per-frame pan-direction predicate extracted out of
/// <see cref="HexMapView"/> so its focus-gating rule is testable —
/// HexMapView itself is excluded from the test build.
/// </summary>
public class KeyboardPanInputTests
{
    /// <summary>
    /// Build an isPressed predicate from a set of "pressed" keys.
    /// </summary>
    private static System.Func<Key, bool> Pressed(params Key[] keys)
    {
        var set = new HashSet<Key>(keys);
        return k => set.Contains(k);
    }

    [Fact]
    public void NoKeysPressed_ReturnsZero()
    {
        Vector2 dir = KeyboardPanInput.ComputeDirection(Pressed(), suppressPan: false);
        Assert.Equal(Vector2.Zero, dir);
    }

    [Fact]
    public void APressed_ReturnsLeft()
    {
        Vector2 dir = KeyboardPanInput.ComputeDirection(Pressed(Key.A), suppressPan: false);
        Assert.Equal(new Vector2(-1f, 0f), dir);
    }

    [Fact]
    public void DPressed_ReturnsRight()
    {
        Vector2 dir = KeyboardPanInput.ComputeDirection(Pressed(Key.D), suppressPan: false);
        Assert.Equal(new Vector2(1f, 0f), dir);
    }

    [Fact]
    public void WPressed_ReturnsUp()
    {
        Vector2 dir = KeyboardPanInput.ComputeDirection(Pressed(Key.W), suppressPan: false);
        Assert.Equal(new Vector2(0f, -1f), dir);
    }

    [Fact]
    public void SPressed_ReturnsDown()
    {
        Vector2 dir = KeyboardPanInput.ComputeDirection(Pressed(Key.S), suppressPan: false);
        Assert.Equal(new Vector2(0f, 1f), dir);
    }

    [Fact]
    public void ArrowKeys_BehaveLikeWASD()
    {
        // Arrows are an alias for WASD so either input scheme pans.
        Assert.Equal(new Vector2(-1f, 0f),
            KeyboardPanInput.ComputeDirection(Pressed(Key.Left), suppressPan: false));
        Assert.Equal(new Vector2(1f, 0f),
            KeyboardPanInput.ComputeDirection(Pressed(Key.Right), suppressPan: false));
        Assert.Equal(new Vector2(0f, -1f),
            KeyboardPanInput.ComputeDirection(Pressed(Key.Up), suppressPan: false));
        Assert.Equal(new Vector2(0f, 1f),
            KeyboardPanInput.ComputeDirection(Pressed(Key.Down), suppressPan: false));
    }

    [Fact]
    public void DiagonalKeys_Combine()
    {
        Vector2 dir = KeyboardPanInput.ComputeDirection(Pressed(Key.W, Key.D), suppressPan: false);
        Assert.Equal(new Vector2(1f, -1f), dir);
    }

    [Fact]
    public void OpposingKeys_Cancel()
    {
        Vector2 dir = KeyboardPanInput.ComputeDirection(Pressed(Key.A, Key.D), suppressPan: false);
        Assert.Equal(Vector2.Zero, dir);
    }

    [Fact]
    public void SuppressPan_BlocksAllInput_EvenWhenKeysHeld()
    {
        // Bug repro: typing 'a' in a save-game LineEdit should not pan
        // the map left at the same time. The caller passes suppressPan
        // when a popup dialog is up; the helper must then ignore keys.
        Vector2 dir = KeyboardPanInput.ComputeDirection(
            Pressed(Key.A), suppressPan: true);
        Assert.Equal(Vector2.Zero, dir);
    }

    [Fact]
    public void SuppressPan_BlocksAllDirections()
    {
        Vector2 dir = KeyboardPanInput.ComputeDirection(
            Pressed(Key.W, Key.A, Key.S, Key.D), suppressPan: true);
        Assert.Equal(Vector2.Zero, dir);
    }
}
