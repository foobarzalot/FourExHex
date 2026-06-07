using System;

public static class ZoomMath
{
    /// <summary>
    /// Smallest zoom factor that keeps the entire map inside the play area
    /// (the viewport with the HUD strip subtracted off the top). Floored
    /// at 1.0 so a tiny grid that already fits at 1× can't zoom out
    /// further — that would just inset the map for no gameplay reason.
    /// Godot-free (plain floats) so it stays in the engine-free model.
    /// </summary>
    public static float ComputeZoomMin(
        float viewportX, float viewportY, float hudHeight,
        float mapPixelX, float mapPixelY)
    {
        float availY = viewportY - hudHeight;
        float fitX = viewportX / mapPixelX;
        float fitY = availY / mapPixelY;
        return Math.Min(1f, Math.Min(fitX, fitY));
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
