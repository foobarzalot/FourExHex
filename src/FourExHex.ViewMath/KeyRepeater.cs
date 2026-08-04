// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;

/// <summary>
/// Auto-repeat schedule for a held navigation key. Godot-free so it is
/// unit-testable; the view feeds it frame deltas and performs one action per
/// step it returns.
///
/// Owning the cadence rather than riding the platform's key-echo events keeps
/// held-key navigation identical everywhere — OS repeat settings vary widely
/// and typically start around half a second, which reads as an unresponsive
/// list. The press itself is the caller's first action; this only schedules
/// the repeats that follow: one at <c>initialDelaySec</c>, then one every
/// <c>intervalSec</c>.
/// </summary>
public sealed class KeyRepeater
{
    private readonly float _initialDelaySec;
    private readonly float _intervalSec;
    private bool _held;
    private float _heldSec;
    private int _emitted;

    public KeyRepeater(float initialDelaySec, float intervalSec)
    {
        _initialDelaySec = initialDelaySec;
        _intervalSec = intervalSec;
    }

    public bool Held => _held;

    /// <summary>Key went down. Restarts the schedule, so reversing direction
    /// waits out the initial delay again instead of inheriting the previous
    /// hold's momentum.</summary>
    public void Press()
    {
        _held = true;
        _heldSec = 0f;
        _emitted = 0;
    }

    /// <summary>Key came up.</summary>
    public void Release()
    {
        _held = false;
        _heldSec = 0f;
        _emitted = 0;
    }

    /// <summary>
    /// Repeat steps that came due over the last frame — 0 on most frames, and
    /// more than one after a frame longer than <c>intervalSec</c> (the hold
    /// earned those steps; dropping them would make a hitch eat input).
    /// Elapsed time accumulates, so frames shorter than the interval can't
    /// each round down to nothing.
    /// </summary>
    public int Advance(float dtSec)
    {
        if (!_held) return 0;
        _heldSec += dtSec;
        if (_heldSec < _initialDelaySec) return 0;

        int due = 1 + (int)MathF.Floor((_heldSec - _initialDelaySec) / _intervalSec);
        int steps = due - _emitted;
        _emitted = due;
        return steps > 0 ? steps : 0;
    }
}
