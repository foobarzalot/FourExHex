// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Globalization;
using System.Text;

/// <summary>
/// Everything a player-initiated bug report needs formatted, kept
/// Godot-free so it is unit-testable: the diagnostic header that ships as
/// <c>report.txt</c> inside the bundle, the mail subject and body, the
/// bundle's file name, and the desktop <c>mailto:</c> fallback URL.
///
/// The Godot side (<c>BugReportBundle</c>, <c>MailBridge</c>) gathers the
/// facts and does the I/O; nothing here touches a file or an engine API.
/// Integer-only per the no-floats rule — the timestamp arrives as a Unix
/// second count, mirroring <c>SaveData.SavedAtUnix</c>.
/// </summary>
public static class BugReport
{
    /// <summary>Name of the diagnostic header entry inside the bundle.</summary>
    public const string HeaderEntryName = "report.txt";

    /// <summary>Name of the save entry inside the bundle.</summary>
    public const string SaveEntryName = "save.json";

    /// <summary>Name of the device-log entry inside the bundle.</summary>
    public const string LogEntryName = "godot.log";

    /// <summary>
    /// The plain-text diagnostic block. Written to
    /// <see cref="HeaderEntryName"/> in the bundle and repeated in the mail
    /// body, so a report is still readable when the attachment is stripped.
    /// </summary>
    public static string BuildHeader(BugReportContext context)
    {
        var sb = new StringBuilder();
        sb.Append("FourExHex bug report\n");
        sb.Append("--------------------\n");
        Field(sb, "App", context.AppVersion);
        Field(sb, "Platform", context.Platform);
        Field(sb, "Device", context.Device);
        Field(sb, "Locale", context.Locale);
        Field(sb, "Reported", FormatUtc(context.ReportedAtUnix));
        sb.Append('\n');
        Field(sb, "Game", DescribeGame(context));
        if (context.MapName != null)
        {
            Field(sb, "Map", context.MapName);
        }
        Field(sb, "Players", $"{context.HumanPlayers} human, " +
                             $"{context.ComputerPlayers} computer");
        sb.Append('\n');
        Field(sb, "Attached", $"{HeaderEntryName}, " +
                              $"{DescribeEntry(SaveEntryName, context.SaveBytes)}, " +
                              $"{DescribeEntry(LogEntryName, context.LogBytes)}");
        return sb.ToString();
    }

    /// <summary>The mail subject line. Deliberately plain ASCII — a
    /// non-ASCII subject triggers RFC 2047 encoded-word handling that some
    /// clients render as mojibake.</summary>
    public static string SubjectLine(string appVersion)
        => $"FourExHex bug report - {appVersion}";

    /// <summary>
    /// The mail body: a prompt for the player's own words, then the
    /// <see cref="BuildHeader"/> block below a separator.
    /// </summary>
    public static string BuildBody(BugReportContext context)
        => "Describe what went wrong here — what you did, what you expected,\n"
         + "and what happened instead.\n"
         + "\n"
         + "\n"
         + "----- diagnostics (please leave the rest in place) -----\n"
         + BuildHeader(context);

    /// <summary>
    /// The bundle's file name, e.g. <c>fourexhex-report-b41-s8813-t27.zip</c>.
    /// Seed and turn are omitted when no game is in progress.
    /// </summary>
    public static string BundleFileName(int build, int? seed, int? turn)
    {
        var sb = new StringBuilder("fourexhex-report-b");
        sb.Append(build.ToString(CultureInfo.InvariantCulture));
        if (seed.HasValue)
        {
            // A master seed can be negative (Random.Shared.Next() on a
            // reload); a bare '-' would read as one of our own separators, so
            // fold it to '_' and stay inside SaveNames' safe alphabet.
            sb.Append("-s").Append(seed.Value
                .ToString(CultureInfo.InvariantCulture).Replace('-', '_'));
        }
        if (turn.HasValue)
        {
            sb.Append("-t").Append(turn.Value.ToString(CultureInfo.InvariantCulture));
        }
        return sb.Append(".zip").ToString();
    }

    /// <summary>
    /// A <c>mailto:</c> URL with the subject and body percent-encoded.
    /// The desktop fallback rung, where no native composer exists.
    /// </summary>
    public static string MailtoUrl(string address, string subject, string body)
        // EscapeDataString is RFC 3986: space becomes %20 (not '+', which a
        // mailto: body renders literally), and '&' / '#' are escaped rather
        // than silently truncating the body at that point.
        => $"mailto:{address}?subject={Uri.EscapeDataString(subject)}" +
           $"&body={Uri.EscapeDataString(body)}";

    private static void Field(StringBuilder sb, string label, string value)
        => sb.Append(label.PadRight(9)).Append(": ").Append(value).Append('\n');

    private static string DescribeGame(BugReportContext context)
        => context.Mode == null
            ? "no game in progress"
            : $"{context.Mode}, seed {Format(context.Seed)}, turn {Format(context.Turn)}";

    /// <summary>An absent entry is named and called out rather than dropped —
    /// a silently missing log reads as "nothing was logged".</summary>
    private static string DescribeEntry(string name, long? bytes)
        => bytes.HasValue
            ? $"{name} ({bytes.Value.ToString(CultureInfo.InvariantCulture)} bytes)"
            : $"{name} (absent)";

    private static string Format(int? value)
        => value.HasValue
            ? value.Value.ToString(CultureInfo.InvariantCulture)
            : "unknown";

    private static string FormatUtc(long unixSeconds)
        => DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime
            .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}

/// <summary>
/// The facts a report carries. <see cref="Mode"/>, <see cref="Seed"/>,
/// <see cref="Turn"/> and <see cref="MapName"/> are null when the report is
/// filed from the main menu with no game in progress; <see cref="SaveBytes"/>
/// and <see cref="LogBytes"/> are null when that entry could not be staged.
/// </summary>
public sealed record BugReportContext(
    string AppVersion,
    string Platform,
    string Device,
    string Locale,
    long ReportedAtUnix,
    string? Mode,
    int? Seed,
    int? Turn,
    int HumanPlayers,
    int ComputerPlayers,
    string? MapName,
    long? SaveBytes,
    long? LogBytes);
