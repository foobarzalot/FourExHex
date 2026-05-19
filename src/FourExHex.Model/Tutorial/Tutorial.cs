/// <summary>
/// Top-level POCO for an authored tutorial: a display title plus a
/// <see cref="Replay"/> payload that carries the full recorded
/// playthrough (initial snapshot + every state-mutating beat). The
/// tutorial replaces the old hand-authored Beats list — the
/// TutorialBuilder's Record mode now plays a real game as all six
/// humans and the controller's replay-recording machinery captures
/// the script automatically.
///
/// Serialized as an optional <c>"Tutorial"</c> block alongside the
/// <c>"Replay"</c> block under <see cref="SaveData"/>; absent on
/// regular saves and starting maps. No namespace — production
/// scripts in this codebase are all top-level (only tests use
/// <c>namespace FourExHex.Tests</c>).
/// </summary>
public sealed class Tutorial
{
    public string Title { get; init; } = "";
    public Replay Replay { get; init; } = null!;
}
