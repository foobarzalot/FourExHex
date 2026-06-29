using Godot;

/// <summary>
/// Shared surface for the purpose-built <b>landscape</b> menu layouts.
/// Unlike the portrait menus — tall fixed-size panels that
/// <c>ScaleToFit</c>/<c>FitPanel</c> downscale to fit a short viewport — a
/// landscape menu reflows its content into a <i>wide-but-short</i> panel so
/// controls stay full-size on a landscape phone.
///
/// The surface is centered and <b>size-capped</b>: it grows to fill the safe
/// rect on a small phone (where the cap exceeds the safe area) but never
/// stretches past <see cref="MaxWidth"/> × <see cref="MaxHeight"/> on a large
/// desktop window — same "comfortable centered panel" feel as the portrait
/// caps, just in a landscape aspect. <see cref="Build"/> returns the rounded
/// slate <see cref="PanelContainer"/>; the caller fills it with its
/// <c>HBox</c>/<c>VBox</c>/<c>Grid</c> tree and keeps it laid out by calling
/// <see cref="ApplyLayout"/> on <c>SafeArea.Changed</c> / viewport
/// <c>SizeChanged</c>.
/// </summary>
public static class LandscapeMenuChrome
{
    /// <summary>Gap between the safe rect and the surface when the surface is
    /// smaller-screen-bound (design handoff: ~12px inset from the safe area).</summary>
    public const float EdgeMargin = 12f;

    /// <summary>Comfortable landscape cap. Beyond this the surface stays
    /// centered instead of stretching across a big desktop window — mirrors the
    /// portrait panels' fixed design sizes (520–736 wide). Authored against the
    /// iPhone 13 mini landscape safe rect (~797×447), with headroom.</summary>
    public const float MaxWidth = 920f;
    public const float MaxHeight = 520f;

    /// <summary>Build the centered surface. The caller adds it directly to its
    /// CanvasLayer / scene and fills it; call <see cref="ApplyLayout"/> once
    /// after building and on every safe-area / resize change.</summary>
    public static PanelContainer Build(float contentPadding = 26f)
    {
        var surface = new PanelContainer
        {
            // Center-anchored; ApplyLayout sets the offsets to the capped size.
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.Both,
        };
        surface.AddThemeStyleboxOverride("panel", SurfaceStyle(contentPadding));
        return surface;
    }

    /// <summary>Size and center the surface: it fills the safe rect (minus the
    /// edge margin) up to the <see cref="MaxWidth"/> × <see cref="MaxHeight"/>
    /// cap, so it stays inside the notch / home-indicator on a phone yet remains
    /// a tidy centered panel on a large desktop window. <paramref name="verticalShift"/>
    /// lifts the whole surface up (on-screen-keyboard avoidance for the New Game
    /// seed field).</summary>
    public static void ApplyLayout(PanelContainer surface, Vector2 viewport, LogicalSafeInsets s,
        float edge = EdgeMargin, float maxW = MaxWidth, float maxH = MaxHeight, float verticalShift = 0f)
    {
        (float w, float h) = PanelFitMath.CappedFill(viewport.X, viewport.Y, s, edge, maxW, maxH);
        surface.OffsetLeft = -w * 0.5f;
        surface.OffsetRight = w * 0.5f;
        surface.OffsetTop = -h * 0.5f - verticalShift;
        surface.OffsetBottom = h * 0.5f - verticalShift;

        Log.Debug(Log.LogCategory.Render,
            $"LandscapeMenuChrome: laid out {w:0}x{h:0} shift={verticalShift:0} "
            + $"(viewport {viewport.X:0}x{viewport.Y:0}, safe t{s.Top:0} b{s.Bottom:0} l{s.Left:0} r{s.Right:0})");
    }

    /// <summary>Rounded warm-slate surface (design: #322e28, radius 22, hairline
    /// border) with generous interior padding so the reflowed content breathes.</summary>
    private static StyleBoxFlat SurfaceStyle(float pad)
    {
        var style = new StyleBoxFlat { BgColor = UiPalette.BgPanel, BorderColor = UiPalette.LineSoft };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(22);
        int p = Mathf.RoundToInt(pad);
        style.ContentMarginLeft = p;
        style.ContentMarginRight = p;
        style.ContentMarginTop = p;
        style.ContentMarginBottom = p;
        return style;
    }
}
