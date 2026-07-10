/// <summary>
/// Process-wide facade over the user-facing string store — the single
/// source of truth for every player-visible English string (issue-tracked
/// as the localization-ready key/value store). Godot-free so it is
/// reachable from both this Controller layer (copy helpers like
/// <see cref="EndgameOverlayContent"/>) and the Godot-side view scripts
/// (HudView, MainMenuScene, editor panels).
///
/// Configured exactly once at startup by <c>LogBootstrap</c>, which reads
/// <c>res://assets/strings/en.json</c> and pushes the text in — mirroring
/// how the Godot-free <see cref="Log"/> is configured. Until configured,
/// every lookup renders its key (harmless and obvious in bare test runs).
///
/// Call sites reference keys only through <see cref="StringKeys"/> consts;
/// a parity test pins exact two-way agreement between those consts and the
/// JSON. Semantics of lookup / tokens / fallbacks live on
/// <see cref="StringTable"/>.
/// </summary>
public static class Strings
{
    private static StringTable _table = StringTable.Empty;

    /// <summary>Replace the active table. <paramref name="isMobile"/>
    /// selects the Tap/Click verb variants (data-driven; see
    /// <see cref="StringTable"/>). Throws on malformed JSON.</summary>
    public static void Configure(string json, bool isMobile)
        => _table = StringTable.Parse(json, isMobile);

    /// <summary>Resolve a key, substituting named <c>{token}</c>s.</summary>
    public static string Get(string key, params (string Name, string Value)[] tokens)
        => _table.Get(key, tokens);

    /// <summary>Display name for a unit level (<c>unit.*</c> keys) —
    /// the shared spelling for tooltips, hints, and tutorial copy.</summary>
    public static string UnitName(UnitLevel level) => Get(StringKeys.ForUnit(level));

    /// <summary>Entries in the active table (0 until configured).</summary>
    public static int Count => _table.Count;
}
