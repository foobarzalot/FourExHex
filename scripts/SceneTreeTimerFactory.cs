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

    public SceneTreeTimerFactory(SceneTree tree)
    {
        _tree = tree;
    }

    public void After(int delayMs, Action callback)
    {
        double seconds = delayMs / 1000.0;
        SceneTreeTimer timer = _tree.CreateTimer(seconds);
        timer.Timeout += () => callback();
    }
}
