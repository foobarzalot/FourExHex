// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;

/// <summary>
/// Tracks concurrent screen touches by index and detects a 3-finger tap:
/// fires exactly once when the third concurrent touch lands, then stays
/// quiet until every touch has been released (so a fourth finger, or
/// lifting and re-pressing mid-gesture, can't re-trigger). Godot-free so
/// the gesture logic is unit-testable; the view layer feeds it from
/// InputEventScreenTouch.
/// </summary>
public class MultiTouchTapDetector
{
    private const int FingerCount = 3;

    private readonly HashSet<int> _down = new();
    private bool _fired;

    /// <summary>Returns true iff this press is the one that triggers the tap.</summary>
    public bool Press(int index)
    {
        _down.Add(index);
        if (_fired || _down.Count != FingerCount) return false;
        _fired = true;
        return true;
    }

    public void Release(int index)
    {
        _down.Remove(index);
        if (_down.Count == 0) _fired = false;
    }
}
