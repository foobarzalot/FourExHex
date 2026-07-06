using System;
using System.Collections.Generic;

public static class ZoomMath
{
    /// <summary>
    /// Index of the discrete zoom level nearest <paramref name="zoom"/> (a
    /// min-abs-delta scan over <paramref name="levels"/>). Ties keep the lower
    /// index. Used to re-sync the discrete level index after a continuous
    /// gesture / framing change.
    /// </summary>
    public static int ClosestLevelIndex(IReadOnlyList<float> levels, float zoom)
    {
        int best = 0;
        float bestDelta = Math.Abs(levels[0] - zoom);
        for (int i = 1; i < levels.Count; i++)
        {
            float d = Math.Abs(levels[i] - zoom);
            if (d < bestDelta)
            {
                bestDelta = d;
                best = i;
            }
        }
        return best;
    }

    /// <summary>
    /// Target discrete level index after stepping <paramref name="delta"/> stops
    /// (−1 out, +1 in) from the level nearest <paramref name="currentZoom"/>,
    /// clamped to <c>[0, count−1]</c>. The caller decides whether the result
    /// differs enough to apply (the view skips a no-op when already exactly on
    /// the target stop).
    /// </summary>
    public static int StepLevel(IReadOnlyList<float> levels, float currentZoom, int delta)
    {
        int from = ClosestLevelIndex(levels, currentZoom);
        return Math.Clamp(from + delta, 0, levels.Count - 1);
    }

    /// <summary>
    /// Smallest zoom factor the camera may reach: the fit that keeps the
    /// entire map inside the play area (the viewport with the HUD strip
    /// subtracted off the top), divided by <paramref name="zoomOutGrace"/>
    /// so max zoom-out leaves margin around the board instead of pressing
    /// edge hexes against the screen edges. Capped at 1.0 so a grid that
    /// already fits at 1× with more than the grace's worth of slack can't
    /// zoom out further — that would just inset the map for no gameplay
    /// reason. Godot-free (plain floats) so it stays in the engine-free model.
    /// </summary>
    public static float ComputeZoomMin(
        float viewportX, float viewportY, float hudHeight,
        float mapPixelX, float mapPixelY, float zoomOutGrace = 1f)
    {
        float availY = viewportY - hudHeight;
        float fitX = viewportX / mapPixelX;
        float fitY = availY / mapPixelY;
        return Math.Min(1f, Math.Min(fitX, fitY) / zoomOutGrace);
    }

    /// <summary>
    /// <paramref name="count"/> evenly-spaced zoom levels from
    /// <paramref name="zoomMin"/> (index 0, fit-all) to 1.0 (last index,
    /// max zoom in). Indexed stepping avoids float drift across many
    /// wheel ticks.
    /// </summary>
    public static float[] BuildLevels(float zoomMin, int count)
    {
        var levels = new float[count];
        for (int i = 0; i < count; i++)
        {
            float t = (float)i / (count - 1);
            levels[i] = zoomMin + (1f - zoomMin) * t;
        }
        return levels;
    }

    /// <summary>
    /// New zoom factor for a two-finger pinch: scale the current zoom by the
    /// ratio of the fingers' new separation to their previous separation. A
    /// non-positive <paramref name="prevDist"/> (degenerate seed) returns the
    /// current zoom unchanged so the gesture can't divide-by-zero or blow up.
    /// Caller clamps to the legal range (ApplyZoom already does), matching the
    /// trackpad gesture paths.
    /// </summary>
    public static float PinchZoom(float currentZoom, float prevDist, float curDist)
    {
        if (prevDist <= 0f) return currentZoom;
        return currentZoom * (curDist / prevDist);
    }
}
