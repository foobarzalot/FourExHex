// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot

/// <summary>Where a layout is in its reflow.</summary>
public enum SettleState
{
    /// <summary>Still churning — the epoch moved recently.</summary>
    Waiting,
    /// <summary>Quiet for the required number of frames; safe to measure.</summary>
    Settled,
    /// <summary>Never went quiet before the cap. A layout that cannot converge
    /// is a finding, not a reason to measure it anyway.</summary>
    Stalled,
}

/// <summary>
/// Decides when a layout has stopped reflowing, by watching a monotonically
/// increasing layout epoch (bumped by <c>LayoutAudit.BumpEpoch</c> on every
/// completed layout pass) rather than counting frames.
///
/// A fixed frame count is not safe here: one window resize fans out into
/// DisplayScale writing <c>ContentScaleFactor</c> — which re-fires
/// <c>SizeChanged</c> — then SafeArea, then <c>OrientationHud.ApplyLayout</c>,
/// then deferred fit rebuilds that chain up to three deep.
///
/// Godot-free so the state machine is unit-testable; the harness feeds it
/// <c>LayoutAudit.Epoch</c> once per process frame.
/// </summary>
public sealed class LayoutSettlePolicy
{
    private readonly int _stableFrames;
    private readonly int _maxFrames;

    public LayoutSettlePolicy(int stableFrames, int maxFrames)
    {
        _stableFrames = stableFrames;
        _maxFrames = maxFrames;
    }

    /// <summary>Frames observed since the last <see cref="Reset"/> — the number
    /// the stall diagnostic reports.</summary>
    public int FramesWaited { get; private set; }

    /// <summary>True once this policy has reached a terminal state.</summary>
    public bool IsFinished { get; private set; }

    private long _lastEpoch = long.MinValue;
    private int _stableRun;

    /// <summary>Feed the current epoch; call once per process frame.</summary>
    public SettleState Observe(long epoch)
    {
        if (IsFinished) return _terminal;

        FramesWaited++;

        if (epoch == _lastEpoch) _stableRun++;
        else _stableRun = 1;
        _lastEpoch = epoch;

        if (_stableRun >= _stableFrames)
        {
            IsFinished = true;
            _terminal = SettleState.Settled;
            return _terminal;
        }

        if (FramesWaited >= _maxFrames)
        {
            IsFinished = true;
            _terminal = SettleState.Stalled;
            return _terminal;
        }

        return SettleState.Waiting;
    }

    private SettleState _terminal = SettleState.Waiting;

    /// <summary>Ready the policy for the next cell or screen.</summary>
    public void Reset()
    {
        FramesWaited = 0;
        IsFinished = false;
        _stableRun = 0;
        _lastEpoch = long.MinValue;
        _terminal = SettleState.Waiting;
    }
}
