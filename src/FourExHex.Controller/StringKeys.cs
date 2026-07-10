/// <summary>
/// The canonical list of user-facing string keys — one <c>const</c> per
/// entry in <c>assets/strings/en.json</c>. Call sites pass these to
/// <see cref="Strings.Get"/> instead of raw string literals so a typo is a
/// compile error, and <c>StringKeysParityTests</c> pins exact two-way
/// agreement between these consts and the JSON (an entry added on one side
/// only fails <c>dotnet test</c>).
///
/// Naming: dotted lowercase, area-prefixed (<c>hud.tooltip.*</c>,
/// <c>menu.*</c>, <c>endgame.*</c>, <c>editor.*</c>, <c>tutorial.*</c>).
/// Const identifiers PascalCase the key path.
/// </summary>
public static class StringKeys
{
    // Platform-aware interaction verb (built-in {Verb}/{verb} tokens —
    // see StringTable). Data replaces the old code branch.
    public const string VerbCapitalizedDesktop = "verb.capitalized.desktop";
    public const string VerbCapitalizedMobile = "verb.capitalized.mobile";
    public const string VerbLowercaseDesktop = "verb.lowercase.desktop";
    public const string VerbLowercaseMobile = "verb.lowercase.mobile";
}
