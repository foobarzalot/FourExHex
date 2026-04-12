using System;
using Godot;

/// <summary>
/// Godot-backed <see cref="IAiPacer"/> that schedules the callback via
/// <see cref="SceneTree.CreateTimer(double, bool, bool, bool)"/>. Each
/// scheduled step is a one-shot <see cref="SceneTreeTimer"/> owned by
/// the scene tree, so pending timers are cleaned up automatically when
/// the scene is reloaded (e.g. via New Game).
/// </summary>
public sealed class GodotAiPacer : IAiPacer
{
    private readonly SceneTree _tree;

    public GodotAiPacer(SceneTree tree)
    {
        _tree = tree;
    }

    public void Schedule(Action callback, int delayMs)
    {
        double seconds = delayMs / 1000.0;
        SceneTreeTimer timer = _tree.CreateTimer(seconds);
        timer.Timeout += () => callback();
    }
}
