using Godot;

/// <summary>
/// Shared chrome for the purpose-built <b>landscape</b> menu layouts
/// (issue #34). Unlike the portrait menus — fixed-size centered panels that
/// <c>ScaleToFit</c>/<c>FitPanel</c> downscale to fit a short viewport — a
/// landscape menu <i>fills</i> the safe rect and reflows its content with
/// containers, so controls stay full-size on a wide-but-short screen.
///
/// <see cref="Build"/> returns a viewport-filling root holding a
/// <see cref="MarginContainer"/> (margins = device safe insets + a small edge
/// margin) wrapping a rounded slate <see cref="PanelContainer"/> surface. The
/// caller drops its <c>HBox</c>/<c>VBox</c>/<c>Grid</c> tree into the surface and
/// keeps the margins live by calling <see cref="ApplyInsets"/> on
/// <c>SafeArea.Changed</c> / viewport <c>SizeChanged</c> (same upkeep pattern as
/// <see cref="CampaignPanel"/>, the other viewport-filling menu).
/// </summary>
public static class LandscapeMenuChrome
{
    /// <summary>Gap between the device safe rect and the surface, on every
    /// side (design handoff: ~12px inset from the safe area).</summary>
    public const float EdgeMargin = 12f;

    /// <summary>Build the fill root. <paramref name="surface"/> hands back the
    /// rounded panel the caller fills with content; <paramref name="safeMargin"/>
    /// is the <see cref="MarginContainer"/> whose insets the caller must refresh
    /// via <see cref="ApplyInsets"/> on safe-area / resize changes.</summary>
    public static Control Build(out PanelContainer surface, out MarginContainer safeMargin,
        float contentPadding = 26f)
    {
        var root = new Control
        {
            AnchorLeft = 0f, AnchorTop = 0f, AnchorRight = 1f, AnchorBottom = 1f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };

        safeMargin = new MarginContainer
        {
            AnchorLeft = 0f, AnchorTop = 0f, AnchorRight = 1f, AnchorBottom = 1f,
        };
        root.AddChild(safeMargin);

        surface = new PanelContainer();
        surface.AddThemeStyleboxOverride("panel", SurfaceStyle(contentPadding));
        safeMargin.AddChild(surface);

        ApplyInsets(safeMargin, SafeArea.Current);
        Log.Debug(Log.LogCategory.Render,
            "LandscapeMenuChrome: built fill surface "
            + $"(safe insets t{SafeArea.Current.Top:0} b{SafeArea.Current.Bottom:0} "
            + $"l{SafeArea.Current.Left:0} r{SafeArea.Current.Right:0}, edge {EdgeMargin:0})");
        return root;
    }

    /// <summary>Recompute the four <see cref="MarginContainer"/> insets from the
    /// device safe area + the edge margin so the surface stays inside the notch /
    /// home-indicator on every rotation and safe-area change.</summary>
    public static void ApplyInsets(MarginContainer safeMargin, LogicalSafeInsets s,
        float edge = EdgeMargin)
    {
        safeMargin.AddThemeConstantOverride("margin_left", Mathf.RoundToInt(s.Left + edge));
        safeMargin.AddThemeConstantOverride("margin_top", Mathf.RoundToInt(s.Top + edge));
        safeMargin.AddThemeConstantOverride("margin_right", Mathf.RoundToInt(s.Right + edge));
        safeMargin.AddThemeConstantOverride("margin_bottom", Mathf.RoundToInt(s.Bottom + edge));
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
