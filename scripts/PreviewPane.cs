using Godot;

/// <summary>
/// Preview mode chrome. Phase 2 stub — bottom-anchored "Coming soon"
/// label. Phase 3 grows this into the transient GameController +
/// TutorialPlayer host described in spec §"Preview mode".
/// </summary>
public sealed partial class PreviewPane : Control
{
    public override void _Ready()
    {
        AnchorLeft = 0f;
        AnchorTop = 0f;
        AnchorRight = 1f;
        AnchorBottom = 1f;
        MouseFilter = MouseFilterEnum.Ignore;

        var label = new Label
        {
            Text = "Preview mode — Coming soon (Phase 3)",
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
    /// the pane the panel it should bind to. Phase 3 uses this to clone
    /// the panel's draft into a transient GameController.
    /// </summary>
    public void SetPanel(MapEditorPanel panel)
    {
        _ = panel;
    }

    /// <summary>
    /// Called by the scene when leaving Preview mode. Phase 3 disposes
    /// the transient controller and clears the scrubber state.
    /// </summary>
    public void Pause()
    {
        // Intentionally empty in Phase 2.
    }
}
