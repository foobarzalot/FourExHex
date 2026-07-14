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

    /// <summary>Pointer travel from the press point at which the gesture
    /// locks to its dominant axis: horizontal drags start reporting live
    /// offsets, vertical drags are locked out for the rest of the gesture
    /// (a scroll-ish drag never wiggles the page).</summary>
    public const float AxisLockPx = 10f;

    private enum AxisLock { Undecided, Horizontal, Vertical }

    private bool _armed;
    private float _startX;
    private float _startY;
    private AxisLock _lock;

    /// <summary>True once the gesture has locked horizontal — the view is
    /// expected to be applying <see cref="Drag"/> offsets to its page.</summary>
    public bool IsTrackingHorizontal => _armed && _lock == AxisLock.Horizontal;

    /// <summary>Arm the detector at the pointer-down position.</summary>
    public void Press(float x, float y)
    {
        _armed = true;
        _startX = x;
        _startY = y;
        _lock = AxisLock.Undecided;
    }

    /// <summary>
    /// Live pointer-motion tracking: the horizontal display offset the
    /// page should show right now. Zero until the gesture locks
    /// horizontal (and always zero for vertical-locked gestures or when
    /// unarmed); raw <c>x − startX</c> afterward.
    /// </summary>
    public float Drag(float x, float y)
    {
        if (!_armed) return 0f;

        if (_lock == AxisLock.Undecided)
        {
            float mx = x - _startX;
            float my = y - _startY;
            float absMx = mx < 0f ? -mx : mx;
            float absMy = my < 0f ? -my : my;
            if (absMx < AxisLockPx && absMy < AxisLockPx) return 0f;
            _lock = absMx >= absMy ? AxisLock.Horizontal : AxisLock.Vertical;
        }

        return _lock == AxisLock.Horizontal ? x - _startX : 0f;
    }

    /// <summary>
    /// Judge the gesture at pointer-up and disarm. Returns
    /// <see cref="SwipeDirection.None"/> when unarmed (no matching press)
    /// or when the gesture locked vertical.
    /// </summary>
    public SwipeDirection Release(float x, float y)
    {
        if (!_armed) return SwipeDirection.None;
        _armed = false;

        if (_lock == AxisLock.Vertical) return SwipeDirection.None;

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
        _lock = AxisLock.Undecided;
    }
}
