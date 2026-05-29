using Godot;

/// <summary>
/// Shared builders for the orientation-aware HUD bars used by both the play
/// HUD (<see cref="HudView"/>) and the map editor HUD
/// (<see cref="MapEditorHudView"/>). Both are CanvasLayers whose top-level
/// Control children anchor to the viewport, so a bar Panel anchored to the
/// top or bottom edge stretches full-width and tracks window resizes for
/// free. Kept here so the landscape↔portrait bar chrome can't drift between
/// the two scenes.
/// </summary>
public static class HudBars
{
    /// <summary>Detach a persistent cluster from whatever bar currently holds
    /// it, so freeing the old bars on an orientation flip never frees the
    /// cluster along with them.</summary>
    public static void Detach(Node node)
    {
        node.GetParent()?.RemoveChild(node);
    }

    /// <summary>A warm-slate bar Panel pinned to the top or bottom edge of
    /// the viewport.
    /// <para>
    /// Two orthogonal insets:
    /// <list type="bullet">
    /// <item><paramref name="topOffset"/>/<paramref name="bottomOffset"/> is a
    /// STRUCTURAL inset: slide the entire bar away from the viewport edge
    /// (tutorial builder hosts the editor HUD below its own topbar).</item>
    /// <item><paramref name="safeAreaTop"/>/<paramref name="safeAreaBottom"/>
    /// is a DEVICE inset: extend the bar's slate fill into the notch /
    /// home-indicator zone so the bar reads edge-to-edge on iOS. Content sits
    /// inside the safe zone (see <see cref="MakeBarFrame"/>).</item>
    /// </list>
    /// </para>
    /// Default MouseFilter Stop blocks clicks over the bar from reaching the map.</summary>
    public static Panel MakeBarPanel(bool top, float height,
        float topOffset = 0f, float bottomOffset = 0f,
        float safeAreaTop = 0f, float safeAreaBottom = 0f)
    {
        float topExtra = top ? safeAreaTop : safeAreaBottom;
        var panel = new Panel
        {
            AnchorLeft = 0f, AnchorRight = 1f,
            AnchorTop = top ? 0f : 1f,
            AnchorBottom = top ? 0f : 1f,
            OffsetLeft = 0f, OffsetRight = 0f,
            OffsetTop = top ? topOffset : -(height + topExtra + bottomOffset),
            OffsetBottom = top ? topOffset + height + topExtra : -bottomOffset,
        };
        var style = new StyleBoxFlat
        {
            BgColor = UiPalette.HudBar,
            BorderColor = UiPalette.LineSoft,
        };
        if (top) style.BorderWidthBottom = 1; else style.BorderWidthTop = 1;
        panel.AddThemeStyleboxOverride("panel", style);
        return panel;
    }

    /// <summary>Inner margin frame (configurable side inset, 8px top/bottom)
    /// that the anchored region groups live in. Anchors fill its parent bar
    /// Panel. <paramref name="safeAreaTop"/>/<paramref name="safeAreaBottom"/>
    /// pads beyond the 8px margin to push content past the notch (top bar) or
    /// home indicator (bottom bar); pass the same value as the parent
    /// MakeBarPanel call so content sits in the safe zone.</summary>
    public static Control MakeBarFrame(float sideInset = 16f,
        float safeAreaTop = 0f, float safeAreaBottom = 0f)
    {
        return new Control
        {
            AnchorLeft = 0f, AnchorRight = 1f, AnchorTop = 0f, AnchorBottom = 1f,
            OffsetLeft = sideInset, OffsetRight = -sideInset,
            OffsetTop = 8f + safeAreaTop, OffsetBottom = -(8f + safeAreaBottom),
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
    }

    /// <summary>One of the independently-anchored regions inside a bar
    /// (left/center/right), so a cluster growing or vanishing never shoves
    /// the others sideways.</summary>
    public static HBoxContainer MakeAnchoredGroup(float anchorX, Control.GrowDirection grow, int separation = 14)
    {
        var group = new HBoxContainer
        {
            AnchorLeft = anchorX, AnchorRight = anchorX, AnchorTop = 0f, AnchorBottom = 1f,
            GrowHorizontal = grow,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        group.AddThemeConstantOverride("separation", separation);
        return group;
    }
}
