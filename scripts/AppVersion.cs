/// <summary>
/// Single canonical app version. Bump these two values to release a new build;
/// the Settings panel label reads them directly so the displayed value can
/// never be a hand-copied second copy.
///
/// Lives in scripts/ (Godot-side), deliberately NOT in FourExHex.Model or
/// FourExHex.Controller — version-string handling stays out of those
/// assemblies.
///
/// Cross-platform schema mapping (export_presets.cfg — syncing the presets to
/// read from here is a deferred follow-up):
///   Marketing -> iOS CFBundleShortVersionString (application/short_version)
///             -> Android versionName            (version/name)
///             -> desktop short_version
///   Build     -> iOS CFBundleVersion            (application/version)
///             -> Android versionCode            (version/code)
///             -> desktop version
/// </summary>
public static class AppVersion
{
    public const string Marketing = "1.0";
    public const int Build = 21;         // monotonic; bumped per TestFlight upload

    /// <summary>Human-readable stamp, e.g. <c>v1.0 (6)</c>.</summary>
    public static string Display => $"v{Marketing} ({Build})";
}
