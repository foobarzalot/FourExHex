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
    /// the viewport. The bar is exactly <paramref name="height"/> logical px
    /// tall — on iOS it sits BENEATH the notch (top bar) or home indicator
    /// (bottom bar) rather than extending past them, so the map reclaims the
    /// vertical space that the safe insets would otherwise cost. Buttons in
    /// the central portion of the bar can be partly obscured by the notch
    /// cutout; on iPhone 13 mini the bottom bar's bottom ~46 logical px overlap
    /// the home-indicator gesture strip, which iOS still allows taps through.
    /// <paramref name="topOffset"/>/<paramref name="bottomOffset"/> is a
    /// STRUCTURAL inset: slide the entire bar away from the viewport edge
    /// (tutorial builder hosts the editor HUD below its own topbar).
    /// Default MouseFilter Stop blocks clicks over the bar from reaching the
    /// map.</summary>
    public static Panel MakeBarPanel(bool top, float height,
        float topOffset = 0f, float bottomOffset = 0f)
    {
        var panel = new Panel
        {
            AnchorLeft = 0f, AnchorRight = 1f,
            AnchorTop = top ? 0f : 1f,
            AnchorBottom = top ? 0f : 1f,
            OffsetLeft = 0f, OffsetRight = 0f,
            OffsetTop = top ? topOffset : -(height + bottomOffset),
            OffsetBottom = top ? topOffset + height : -bottomOffset,
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
    /// Panel — including any safe-inset extension MakeBarPanel added — so the
    /// button row spans the full bar height instead of being marooned in the
    /// non-inset portion. On iOS this means tappable controls reach into the
    /// notch / home-indicator zone; the slate fill remains edge-to-edge and
    /// the visible bar reads at the S9-baseline 96 px usable area on both
    /// notched and non-notched devices.</summary>
    public static Control MakeBarFrame(float sideInset = 16f)
    {
        return new Control
        {
            AnchorLeft = 0f, AnchorRight = 1f, AnchorTop = 0f, AnchorBottom = 1f,
            OffsetLeft = sideInset, OffsetRight = -sideInset,
            OffsetTop = 8f, OffsetBottom = -8f,
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
