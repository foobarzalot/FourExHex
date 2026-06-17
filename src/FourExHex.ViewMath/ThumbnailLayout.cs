/// <summary>
/// Pure sizing math for the main-menu map thumbnail. The thumbnail renders the
/// real board (a <c>HexMapView</c>) into an offscreen <c>SubViewport</c> and
/// snapshots it; that viewport should match the board content's aspect ratio so
/// the snapshot is tightly framed with no distortion. <see cref="FitInside"/> is
/// the classic "contain" fit: the largest box with the content's aspect ratio
/// that fits inside the target rect. Godot-free (plain floats) so it's
/// unit-testable, like its neighbours in this assembly.
/// </summary>
public static class ThumbnailLayout
{
    /// <summary>
    /// Largest <c>(width, height)</c> preserving the <c>contentW : contentH</c>
    /// aspect ratio that fits inside <c>maxW × maxH</c> ("contain"). Returns
    /// <c>(0, 0)</c> when any input is non-positive (degenerate — nothing to
    /// frame). The result touches at least one of the max bounds.
    /// </summary>
    public static (float width, float height) FitInside(
        float contentW, float contentH, float maxW, float maxH)
    {
        if (contentW <= 0f || contentH <= 0f || maxW <= 0f || maxH <= 0f)
            return (0f, 0f);

        // Scale that just touches each axis; the smaller one keeps both inside.
        float scaleX = maxW / contentW;
        float scaleY = maxH / contentH;
        float scale = scaleX < scaleY ? scaleX : scaleY;
        return (contentW * scale, contentH * scale);
    }
}
