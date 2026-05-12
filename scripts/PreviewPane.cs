using Godot;

/// <summary>
/// Preview-mode chrome. The dev plays as player 0 (Red); a
/// <c>ReplayDrivenAi</c> chooser plays the other five players'
/// recorded moves through the standard AI step machine. Mismatched
/// inputs are rejected by a <c>humanActionValidator</c> hook on
/// the <see cref="GameController"/>.
///
/// Pre-rewrite this class wrapped the real views in
/// <c>TutorialGatedHexMapView</c> / <c>TutorialGatedHudView</c> and
/// drove a hand-authored Beat list through <c>TutorialPlayer</c>.
/// That whole approach is gone — see plan: replay-driven tutorial
/// system.
/// </summary>
public sealed partial class PreviewPane : Control
{
    private MapEditorPanel? _panel;

    public void SetPanel(MapEditorPanel panel)
    {
        _panel = panel;
    }

    public void Start(Tutorial tutorial)
    {
        // Preview wiring: instantiate ReplayDrivenAi + TutorialPreview
        // + a GameController with player 0 as Human and players 1-5
        // as Heuristic (chooser overridden to the replay-driven one).
        // To be implemented.
    }

    public void Pause()
    {
        // Tear down the preview controller and restore the panel's
        // draft view. To be implemented.
    }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(220, 0);

        var heading = new Label
        {
            Text = "Preview Mode",
            Position = new Vector2(12, 8),
        };
        heading.AddThemeFontSizeOverride("font_size", 18);
        AddChild(heading);

        var placeholder = new Label
        {
            Text = "Preview wiring is being\nimplemented. You will play\nas Red; the AI will replay\nthe other recorded moves.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Position = new Vector2(12, 40),
            Size = new Vector2(200, 200),
        };
        AddChild(placeholder);
    }
}
