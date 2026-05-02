using Godot;

public static class ZoomMath
{
    /// <summary>
    /// Smallest zoom factor that keeps the entire map inside the play area
    /// (the viewport with the HUD strip subtracted off the top). Floored
    /// at 1.0 so a tiny grid that already fits at 1× can't zoom out
    /// further — that would just inset the map for no gameplay reason.
    /// </summary>
    public static float ComputeZoomMin(Vector2 viewport, float hudHeight, Vector2 mapPixelSize)
    {
        float availY = viewport.Y - hudHeight;
        float fitX = viewport.X / mapPixelSize.X;
        float fitY = availY / mapPixelSize.Y;
        return Mathf.Min(1f, Mathf.Min(fitX, fitY));
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
            levels[i] = Mathf.Lerp(zoomMin, 1f, (float)i / (count - 1));
        }
        return levels;
    }
}
