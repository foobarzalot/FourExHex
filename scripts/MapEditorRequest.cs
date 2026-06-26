/// <summary>
/// One-shot handoff from <see cref="MainMenuScene"/> to <see cref="MapEditorScene"/>
/// across <c>ChangeSceneToFile</c> (issue #70). The menu's "Map Editor" source
/// chooser sets <see cref="Pending"/> — either a fresh map with the per-color
/// kinds/difficulties picked on the shared player-setup screen, or a saved map
/// to open for editing — and the editor reads it in <c>_Ready</c> and clears it.
/// Mirrors the static-state idiom of <see cref="LoadRequest"/> / <see cref="GameSettings"/>.
/// </summary>
public static class MapEditorRequest
{
    public enum Source { NewMap, LoadMap }

    public sealed class Request
    {
        public Source Source { get; init; }

        /// <summary>Per-slot kinds for a NewMap request (length = PlayerConfig).</summary>
        public PlayerKind[]? Kinds { get; init; }

        /// <summary>Per-slot difficulties for a NewMap request.</summary>
        public Difficulty[]? Difficulties { get; init; }

        /// <summary>Saved map slot name for a LoadMap request.</summary>
        public string? MapName { get; init; }
    }

    public static Request? Pending { get; set; }
}
