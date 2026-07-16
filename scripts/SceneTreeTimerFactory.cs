// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using Godot;

/// <summary>
/// Production <see cref="ITimerFactory"/> that schedules each
/// delivery via <see cref="SceneTree.CreateTimer(double, bool, bool, bool)"/>.
/// Test-excluded — depends on Godot's scene tree. The pure C# logic
/// that drives cancellation lives in <see cref="GodotAiPacer"/>,
/// which IS tested with a <c>ManualTimerFactory</c> stand-in.
/// </summary>
public sealed class SceneTreeTimerFactory : ITimerFactory
{
    private readonly SceneTree _tree;
    private readonly bool _processAlways;

    /// <param name="processAlways">
    /// False (default): timers freeze while <c>GetTree().Paused</c> — what
    /// every live-game pacer wants (the pause and Help modals expect AI
    /// pacing to halt). True: timers keep firing while paused — for pacing
    /// that must survive a paused tree (the Instructions demo board, which
    /// keeps animating behind the pausing Help family).
    /// </param>
    public SceneTreeTimerFactory(SceneTree tree, bool processAlways = false)
    {
        _tree = tree;
        _processAlways = processAlways;
    }

    public void After(int delayMs, Action callback)
    {
        double seconds = delayMs / 1000.0;
        // SceneTreeTimer's own default is processAlways: true (keeps firing
        // while GetTree().Paused) — the wrong default for gameplay pacing,
        // so this factory defaults it off.
        SceneTreeTimer timer = _tree.CreateTimer(seconds, processAlways: _processAlways);
        timer.Timeout += () => callback();
    }
}
