using System.Collections.Generic;

/// <summary>
/// The gameplay HUD elements the UI tour (<c>HudTour</c>) walks through, in
/// display order. Each id maps view-side (in <c>HudView</c>) to a concrete
/// Control node plus its title/description copy; this enum stays Godot-free so
/// the navigation logic is unit-testable.
/// </summary>
public enum HudTourStep
{
    TurnCounter,
    ProfitLoss,
    BuyUnits,
    BuildTower,
    UndoRedo,
    NextUnit,
    NextTerritory,
    EndTurn,
    Automate,
    Options,
    Help,
}

/// <summary>
/// Pure cursor over an ordered, visibility-filtered list of <see cref="HudTourStep"/>s:
/// forward/back navigation (both wrap at the ends) and click-to-jump. Godot-free
/// (holds ids, not nodes) so it's unit-testable; <c>HudTour</c> owns the node
/// highlighting and reads <see cref="Current"/> to know what to point at.
/// </summary>
public sealed class HudTourSteps
{
    private readonly List<HudTourStep> _steps;
    private int _index;

    /// <param name="steps">
    /// The ordered steps to tour — already filtered to the currently-visible
    /// HUD elements. Must be non-empty.
    /// </param>
    public HudTourSteps(IReadOnlyList<HudTourStep> steps)
    {
        _steps = new List<HudTourStep>(steps);
        _index = 0;
    }

    /// <summary>Number of steps in the tour.</summary>
    public int Count => _steps.Count;

    /// <summary>Zero-based position of the current step.</summary>
    public int Index => _index;

    /// <summary>The step currently being pointed at.</summary>
    public HudTourStep Current => _steps[_index];

    /// <summary>Advance one step, wrapping from the last back to the first.</summary>
    public HudTourStep Next()
    {
        _index = (_index + 1) % _steps.Count;
        return Current;
    }

    /// <summary>Go back one step, wrapping from the first around to the last.</summary>
    public HudTourStep Prev()
    {
        _index = (_index - 1 + _steps.Count) % _steps.Count;
        return Current;
    }

    /// <summary>
    /// Jump the cursor to <paramref name="step"/> if it is in the list (click-to-jump).
    /// Returns false and leaves the cursor untouched when the step isn't present
    /// (e.g. a hidden element not in the visibility-filtered tour).
    /// </summary>
    public bool JumpTo(HudTourStep step)
    {
        int i = _steps.IndexOf(step);
        if (i < 0) return false;
        _index = i;
        return true;
    }
}
