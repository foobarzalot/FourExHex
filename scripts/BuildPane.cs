using Godot;

/// <summary>
/// Record-mode chrome. The dev plays the game as all six humans;
/// every state-mutating action is captured automatically into the
/// controller's replay log via the normal recording pipeline. This
/// pane is a thin sidebar with Stop / Save / Discard buttons.
///
/// Pre-rewrite this class authored individual <c>Beat</c> records
/// from a palette UI. That whole approach is gone — the Record mode
/// now spins up a real <see cref="GameController"/> with six Human
/// players and lets the controller's <c>_replayBeats</c> capture
/// the script. See plan: replay-driven tutorial system.
/// </summary>
public partial class BuildPane : VBoxContainer
{
    private MapEditorPanel? _panel;

    /// <summary>
    /// Wire to the shared <see cref="MapEditorPanel"/> instance so
    /// Record mode can build a live <see cref="GameState"/> from the
    /// painted draft when the dev starts recording.
    /// </summary>
    public void SetPanel(MapEditorPanel panel)
    {
        _panel = panel;
    }

    /// <summary>
    /// In-memory tutorial captured by the most recent recording session,
    /// or null if recording hasn't happened yet. Consumed by Preview
    /// mode and by the Save Tutorial path.
    /// </summary>
    public Tutorial? CurrentTutorial { get; private set; }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(220, 0);
        AddThemeConstantOverride("separation", 8);

        var heading = new Label { Text = "Record Mode" };
        heading.AddThemeFontSizeOverride("font_size", 18);
        AddChild(heading);

        var placeholder = new Label
        {
            Text = "Recording wiring is being\nimplemented. Play the game\nas all six humans;\nevery action is captured.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        AddChild(placeholder);
    }
}
