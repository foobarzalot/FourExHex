using Godot;

/// <summary>
/// Build mode chrome. Phase 2 stub — bottom-anchored "Coming soon" label.
/// Phase 3 grows this into the timeline + inspector + add-beat palette
/// described in spec §"Build mode".
/// </summary>
public sealed partial class BuildPane : Control
{
    public override void _Ready()
    {
        // Stretch to fill the viewport so the placeholder label can be
        // anchored relative to it, but keep MouseFilter = Ignore so the
        // pane doesn't swallow clicks meant for the panel's HexMapView
        // underneath.
        AnchorLeft = 0f;
        AnchorTop = 0f;
        AnchorRight = 1f;
        AnchorBottom = 1f;
        MouseFilter = MouseFilterEnum.Ignore;

        var label = new Label
        {
            Text = "Build mode — Coming soon (Phase 3)",
            HorizontalAlignment = HorizontalAlignment.Center,
            AnchorLeft = 0f,
            AnchorRight = 1f,
            AnchorTop = 1f,
            AnchorBottom = 1f,
            OffsetTop = -56f,
            OffsetBottom = -16f,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", 22);
        label.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.85f));
        AddChild(label);
    }

    /// <summary>
    /// Called once by <see cref="TutorialBuilderScene._Ready"/> to hand
    /// the pane the panel it should bind to. Phase 2 ignores the
    /// reference; Phase 3 stores it for the state-after-beat-N cache.
    /// </summary>
    public void SetPanel(MapEditorPanel panel)
    {
        _ = panel;
    }
}
