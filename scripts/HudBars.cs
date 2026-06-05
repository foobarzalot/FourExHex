using Godot;

/// <summary>
/// Shared builders for the floating zone containers used by both the play HUD
/// (<see cref="HudView"/>) and the map editor HUD
/// (<see cref="MapEditorHudView"/>). The D1 "Roles Split" design keeps the
/// HUD over the map (no opaque chrome bar) — the corner zones are content-
/// sized and click-block only their own footprint; the portrait bottom bar
/// and the landscape side rails are full-strip Panels that intercept clicks
/// across the strip so the obscured map can't be tapped through the gap
/// between hero buttons.
///
/// Kept here so the floating chrome can't drift between the two HUDs.
/// </summary>
public static class HudBars
{
    /// <summary>Side-rail width in logical px (spec §6). Holds one column of
    /// 54-px buttons with 8-px gutter on each side.</summary>
    public const float RailWidth = 78f;

    /// <summary>Bottom-bar height in portrait. Sized for two rows of 68-px
    /// HudIconButtons (the buy palette panel chrome adds a little extra
    /// vertical bulk) + 8-px row separation + 10-px top/bottom padding.
    /// The bar overlaps the iOS home-indicator zone (see MakeBottomBar);
    /// the inner VBox subtracts safe.Bottom from its bottom offset so the
    /// content sits above the indicator.</summary>
    public const float PortraitBottomBarHeight = 200f;

    /// <summary>Detach a persistent cluster from whatever zone currently
    /// holds it, so freeing the old zones on a layout flip never frees the
    /// cluster along with them.</summary>
    public static void Detach(Node node)
    {
        node.GetParent()?.RemoveChild(node);
    }

    /// <summary>A content-sized HBox anchored to the top-left or top-right
    /// corner of the viewport, inset by the safe-area top/side. Floats over
    /// the map — only the chips parented inside block clicks (the empty
    /// gap between zones stays map-clickable). MouseFilter.Pass on the
    /// HBox itself; chips inside are MouseFilter.Stop.</summary>
    public static HBoxContainer MakeCornerZone(bool left)
    {
        FourExHex.Model.LogicalSafeInsets safe = SafeArea.Current;
        // Top respects the safe inset (notch / Dynamic Island vertically);
        // the horizontal sides do NOT — corner readouts and the
        // undo/redo/options buttons may sit IN the safe-area corners
        // (acceptable on iPhone landscape: the notch occupies one
        // top-corner; the others are safe). Rails take the inset instead.
        float topOffset = safe.Top + 10f;
        float sideOffset = 10f;

        var zone = new HBoxContainer
        {
            AnchorLeft  = left ? 0f : 1f,
            AnchorRight = left ? 0f : 1f,
            AnchorTop = 0f,
            AnchorBottom = 0f,
            GrowHorizontal = left ? Control.GrowDirection.End : Control.GrowDirection.Begin,
            GrowVertical = Control.GrowDirection.End,
            OffsetLeft  = left ?  sideOffset : -sideOffset,
            OffsetRight = left ?  sideOffset : -sideOffset,
            OffsetTop = topOffset,
            OffsetBottom = topOffset,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        zone.AddThemeConstantOverride("separation", 10);
        return zone;
    }

    /// <summary>Portrait-only full-width bottom bar. Transparent backdrop
    /// (D1 spec — floating HUD, no opaque chrome). MouseFilter.Ignore so
    /// taps in the gaps between buttons fall through to the map; only the
    /// individual buttons consume clicks on their own footprint. Subclasses
    /// parent their action layout (rows / clusters) into the bar.</summary>
    public static Panel MakeBottomBar()
    {
        FourExHex.Model.LogicalSafeInsets safe = SafeArea.Current;
        var panel = new Panel
        {
            AnchorLeft = 0f, AnchorRight = 1f,
            AnchorTop = 1f, AnchorBottom = 1f,
            OffsetLeft = 0f, OffsetRight = 0f,
            // Bar overlaps the iOS home-indicator zone (same policy as the
            // legacy bar shape) so the map reclaims the safe-inset space.
            OffsetTop = -PortraitBottomBarHeight,
            OffsetBottom = 0f,
            // Pass — children (buttons) consume their own clicks; gaps in
            // the bar fall through to the map. With Stop the whole strip
            // blocked input even where no button was drawn.
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        // Transparent backdrop — the spec's "floating HUD (no opaque chrome
        // bar)". Buttons inside carry their own chip chrome.
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0f, 0f, 0f, 0f),
        };
        panel.AddThemeStyleboxOverride("panel", style);
        // Inner content sits in a VBox the subclass populates.
        return panel;
    }

    /// <summary>Landscape-only side rail — a 78-px column anchored to the
    /// left or right viewport edge. Returns (railPanel, innerVBox). The
    /// inner VBox is vertically aligned Bottom (expanded → thumb zone) or
    /// Center (compact → reachable mid-screen) per the
    /// <paramref name="alignBottom"/> flag. MouseFilter.Stop on the Panel
    /// so the column intercepts clicks.</summary>
    public static (Panel Rail, VBoxContainer Group) MakeRail(bool left, bool alignBottom)
    {
        FourExHex.Model.LogicalSafeInsets safe = SafeArea.Current;
        // Rails carry the critical action buttons (buy / build / nav / end
        // turn) — they must NEVER overlap the notch regardless of which
        // way the phone is rotated. Use max(safe.Left, safe.Right) on
        // BOTH sides so the inset is symmetric and orientation-safe; the
        // corner zones (display chips, options) skip the safe inset and
        // get the unused corner real estate instead.
        const float edgePad = 8f;
        float notchSafe = Mathf.Max(safe.Left, safe.Right);
        float sideOffset = notchSafe + edgePad;

        var rail = new Panel
        {
            AnchorLeft  = left ? 0f : 1f,
            AnchorRight = left ? 0f : 1f,
            AnchorTop = 0f, AnchorBottom = 1f,
            OffsetLeft  = left ?  sideOffset : -(RailWidth + sideOffset),
            OffsetRight = left ?  RailWidth + sideOffset : -sideOffset,
            OffsetTop = 0f, OffsetBottom = 0f,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0f, 0f, 0f, 0f),
        };
        rail.AddThemeStyleboxOverride("panel", style);

        // Inner vertical group, anchored to fill the rail. The group's own
        // alignment via SizeFlags decides where the buttons sit within the
        // rail's height (Center on compact, End on expanded).
        var group = new VBoxContainer
        {
            AnchorLeft = 0f, AnchorRight = 1f,
            AnchorTop = 0f, AnchorBottom = 1f,
            OffsetLeft = 8f, OffsetRight = -8f,
            OffsetTop = safe.Top + 10f,
            OffsetBottom = -(safe.Bottom + 10f),
            Alignment = alignBottom ? BoxContainer.AlignmentMode.End : BoxContainer.AlignmentMode.Center,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        group.AddThemeConstantOverride("separation", 8);
        rail.AddChild(group);

        return (rail, group);
    }

    /// <summary>Anchored region inside the portrait bottom bar (left / center
    /// / right) so a cluster growing or vanishing doesn't shove the others
    /// sideways. Same shape as the legacy helper.</summary>
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

    /// <summary>Paper-fill chip backdrop (spec §6: paper fill, 2 px ink
    /// border, ~10 px radius). Wraps a chip's content so it stays legible
    /// over any map color in the floating layout.</summary>
    public static StyleBoxFlat ChipStyle()
    {
        return new StyleBoxFlat
        {
            BgColor = UiPalette.ChipFill,
            BorderColor = UiPalette.ChipBorder,
            BorderWidthLeft = 2,
            BorderWidthRight = 2,
            BorderWidthTop = 2,
            BorderWidthBottom = 2,
            CornerRadiusTopLeft = 10,
            CornerRadiusTopRight = 10,
            CornerRadiusBottomLeft = 10,
            CornerRadiusBottomRight = 10,
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 8,
            ContentMarginBottom = 8,
        };
    }
}
