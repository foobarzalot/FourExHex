/// <summary>Verdict of a completed pointer gesture: a mostly-horizontal
/// drag long enough to page, or nothing.</summary>
public enum SwipeDirection
{
    None,
    Left,
    Right,
}

/// <summary>
/// Pure horizontal-swipe recognition for the paged overlays (the guided
/// UI tour and the Instructions panel). The view feeds pointer
/// press/release positions; <see cref="Release"/> returns
/// <see cref="SwipeDirection.Left"/> / <see cref="SwipeDirection.Right"/>
/// when the drag traveled at least <see cref="MinDistancePx"/>
/// horizontally and is horizontally dominant (|dx| ≥
/// <see cref="HorizontalDominance"/>·|dy|), else
/// <see cref="SwipeDirection.None"/> — a tap. Page-turning mapping is the
/// caller's: finger left = Next, finger right = Back. Godot-free (plain
/// floats), like its neighbours in this assembly.
/// </summary>
public sealed class SwipeDetector
{
    /// <summary>Minimum horizontal travel to count as a swipe — well above
    /// the map's 5 px tap/drag divider so ordinary taps can never page.</summary>
    public const float MinDistancePx = 60f;

    /// <summary>How horizontally dominant the drag must be: |dx| must be at
    /// least this multiple of |dy|.</summary>
    public const float HorizontalDominance = 2f;

    private bool _armed;
    private float _startX;
    private float _startY;

    /// <summary>Arm the detector at the pointer-down position.</summary>
    public void Press(float x, float y)
    {
        _armed = true;
        _startX = x;
        _startY = y;
    }

    /// <summary>
    /// Judge the gesture at pointer-up and disarm. Returns
    /// <see cref="SwipeDirection.None"/> when unarmed (no matching press).
    /// </summary>
    public SwipeDirection Release(float x, float y)
    {
        if (!_armed) return SwipeDirection.None;
        _armed = false;

        float dx = x - _startX;
        float dy = y - _startY;
        float absDx = dx < 0f ? -dx : dx;
        float absDy = dy < 0f ? -dy : dy;
        if (absDx < MinDistancePx) return SwipeDirection.None;
        if (absDx < HorizontalDominance * absDy) return SwipeDirection.None;
        return dx < 0f ? SwipeDirection.Left : SwipeDirection.Right;
    }

    /// <summary>Disarm without judging (multi-touch takeover, focus loss).</summary>
    public void Cancel()
    {
        _armed = false;
    }
}
