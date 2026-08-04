// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Tests for <see cref="BugReport"/>: the Godot-free formatting behind the
/// in-app "Report a Bug" flow — the <c>report.txt</c> diagnostic header, the
/// mail subject/body, the bundle file name, and the <c>mailto:</c> fallback
/// URL's percent-encoding.
/// </summary>
public class BugReportTests
{
    // A mid-game report from a device, with both attachments staged.
    private static BugReportContext InGame() => new(
        AppVersion: "v1.0 (41)",
        Platform: "iOS 18.4",
        Device: "iPhone13,1",
        Locale: "en_US",
        ReportedAtUnix: 1_770_000_000,
        Mode: "RisingTides",
        Seed: 8813,
        Turn: 27,
        HumanPlayers: 1,
        ComputerPlayers: 5,
        MapName: "atoll-6p",
        SaveBytes: 170_432,
        LogBytes: 94_468);

    // Filed from the main menu: no live game, and no log file on disk yet.
    private static BugReportContext FromMenu() => InGame() with
    {
        Mode = null,
        Seed = null,
        Turn = null,
        MapName = null,
        SaveBytes = null,
        LogBytes = null,
    };

    [Fact]
    public void BuildHeader_InGame_CarriesTheReproFacts()
    {
        string header = BugReport.BuildHeader(InGame());

        Assert.Contains("v1.0 (41)", header);
        Assert.Contains("iOS 18.4", header);
        Assert.Contains("iPhone13,1", header);
        Assert.Contains("RisingTides", header);
        Assert.Contains("8813", header);
        Assert.Contains("27", header);
        Assert.Contains("atoll-6p", header);
    }

    [Fact]
    public void BuildHeader_InGame_ReportsTheRoster()
    {
        string header = BugReport.BuildHeader(InGame());

        Assert.Contains("1 human", header);
        Assert.Contains("5 computer", header);
    }

    [Fact]
    public void BuildHeader_FormatsTheTimestampAsUtcIso8601()
    {
        // 1_770_000_000 == 2026-02-02T02:40:00Z
        string header = BugReport.BuildHeader(InGame());

        Assert.Contains("2026-02-02T02:40:00Z", header);
    }

    [Fact]
    public void BuildHeader_FromMenu_SaysNoGameRatherThanPrintingNulls()
    {
        string header = BugReport.BuildHeader(FromMenu());

        Assert.DoesNotContain("null", header);
        Assert.DoesNotContain("RisingTides", header);
        Assert.Contains("no game in progress", header);
    }

    [Fact]
    public void BuildHeader_ReportsAttachmentSizes()
    {
        string header = BugReport.BuildHeader(InGame());

        Assert.Contains("170432", header);
        Assert.Contains("94468", header);
    }

    [Fact]
    public void BuildHeader_MissingAttachment_IsCalledOutNotSilentlyOmitted()
    {
        // A staging failure must be visible in the report itself — otherwise
        // an absent log reads as "nothing was logged".
        string header = BugReport.BuildHeader(InGame() with { LogBytes = null });

        Assert.Contains(BugReport.LogEntryName, header);
        Assert.Contains("absent", header);
    }

    [Fact]
    public void SubjectLine_NamesTheGameAndVersion()
    {
        string subject = BugReport.SubjectLine("v1.0 (41)");

        Assert.Contains("FourExHex", subject);
        Assert.Contains("v1.0 (41)", subject);
    }

    [Fact]
    public void BuildBody_PromptsThePlayerAboveTheDiagnostics()
    {
        string body = BugReport.BuildBody(InGame());
        string header = BugReport.BuildHeader(InGame());

        Assert.Contains(header, body);
        // The prompt has to come first — the player types at the top of a
        // reply, and a body that opens with a wall of diagnostics reads as
        // "nothing for me to do here".
        Assert.True(body.IndexOf(header, System.StringComparison.Ordinal) > 0);
    }

    [Fact]
    public void BundleFileName_InGame_CarriesBuildSeedAndTurn()
    {
        string name = BugReport.BundleFileName(41, 8813, 27);

        Assert.Equal("fourexhex-report-b41-s8813-t27.zip", name);
    }

    [Fact]
    public void BundleFileName_NoGame_OmitsSeedAndTurn()
    {
        string name = BugReport.BundleFileName(41, null, null);

        Assert.Equal("fourexhex-report-b41.zip", name);
    }

    [Fact]
    public void BundleFileName_NegativeSeed_StaysAValidFileName()
    {
        // Master seeds come from Random.Shared.Next() and a saved seed can be
        // negative; a bare '-' would read as a separator and '_' keeps the
        // name in SaveNames' safe [A-Za-z0-9_-] alphabet.
        string name = BugReport.BundleFileName(41, -8813, 27);

        Assert.Equal("fourexhex-report-b41-s_8813-t27.zip", name);
    }

    [Fact]
    public void MailtoUrl_EncodesTheSubjectAndBody()
    {
        string url = BugReport.MailtoUrl("bugs@example.com", "a b", "c d");

        Assert.Equal("mailto:bugs@example.com?subject=a%20b&body=c%20d", url);
    }

    [Fact]
    public void MailtoUrl_EncodesNewlinesSoTheBodySurvives()
    {
        string url = BugReport.MailtoUrl("bugs@example.com", "s", "one\ntwo");

        Assert.Contains("body=one%0Atwo", url);
    }

    [Fact]
    public void MailtoUrl_EncodesAmpersandAndHashRatherThanTruncating()
    {
        // An unescaped '&' starts a new mailto field and '#' starts a
        // fragment — either silently truncates the body at that point.
        string url = BugReport.MailtoUrl("bugs@example.com", "s", "a&b#c");

        Assert.Contains("body=a%26b%23c", url);
        Assert.DoesNotContain("b#c", url);
    }

    [Fact]
    public void MailtoUrl_EncodesNonAsciiAsUtf8()
    {
        string url = BugReport.MailtoUrl("bugs@example.com", "s", "café");

        Assert.Contains("body=caf%C3%A9", url);
    }

    [Fact]
    public void MailtoUrl_EncodesSpaceAsPercent20NotPlus()
    {
        // '+' is only a space in application/x-www-form-urlencoded; in a
        // mailto: body it stays a literal plus sign.
        string url = BugReport.MailtoUrl("bugs@example.com", "s", "a b");

        Assert.DoesNotContain("+", url);
    }
}
