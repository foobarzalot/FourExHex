/// <summary>
/// One-shot handoff from <see cref="MainMenuScene"/> to <see cref="Main"/>
/// across <c>ChangeSceneToFile</c>. The menu deserializes a save into
/// <see cref="Pending"/> before switching scenes; <see cref="Main"/> reads
/// it in <c>_Ready</c> and clears it immediately so a subsequent
/// menu→game transition starts a fresh game.
///
/// Mirrors the static-state idiom of <see cref="GameSettings"/>.
/// </summary>
public static class LoadRequest
{
    public static LoadedSave? Pending { get; set; }
}
