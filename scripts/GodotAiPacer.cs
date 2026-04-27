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
    private bool _cancelled;

    public GodotAiPacer(SceneTree tree)
    {
        _tree = tree;
    }

    public void Schedule(Action callback, int delayMs)
    {
        double seconds = delayMs / 1000.0;
        SceneTreeTimer timer = _tree.CreateTimer(seconds);
        // Capture _cancelled by reference via the closure on `this`.
        // A Timeout already in flight when Cancel() runs will see the
        // flag set and no-op, so a callback can't reach the
        // GameController after the scene starts tearing down.
        timer.Timeout += () => { if (!_cancelled) callback(); };
    }

    public void Cancel() => _cancelled = true;
}
