// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using Godot;

/// <summary>
/// Stages the zip a bug report carries: the <c>report.txt</c> diagnostic
/// header, the player's save, and the device log. Godot-side because it does
/// file I/O and reads platform facts; all the formatting it needs lives in
/// the unit-tested <see cref="BugReport"/>.
///
/// The save is whatever is in the autosave slot, so callers with a live game
/// should write a fresh autosave immediately before calling
/// <see cref="Stage"/> — that way the attachment is the moment the player hit
/// the button, not the start of their turn. Called from the main menu with no
/// game in progress, the last autosave on disk is still worth having.
///
/// A missing save or log degrades the bundle rather than failing it; the
/// header names the absent entry so a gap is visible rather than looking like
/// there was nothing to report.
/// </summary>
public static class BugReportBundle
{
    /// <summary>Where the device log rotations live (Godot's own
    /// <c>debug/file_logging</c> output, enabled in <c>project.godot</c>).</summary>
    private const string LogDirectory = "user://logs/";

    /// <summary>Zip, so one attachment carries all three entries and a
    /// ~170 KB save travels as ~10 KB.</summary>
    public const string MimeType = "application/zip";

    /// <summary>
    /// Build the bundle and return everything the compose step needs.
    /// Throws <see cref="System.IO.IOException"/> if the zip itself cannot be
    /// written — the caller surfaces that in the modal.
    /// </summary>
    public static StagedBugReport Stage(BugReportGameFacts? game)
    {
        byte[]? save = ReadIfExists(
            SaveStore.SaveDirectory + SaveStore.AutosaveSlotName + ".json");
        byte[]? log = ReadNewestLog();

        var context = new BugReportContext(
            AppVersion: AppVersion.Display,
            Platform: $"{OS.GetName()} {OS.GetVersion()}",
            Device: OS.GetModelName(),
            Locale: OS.GetLocale(),
            // Godot hands this back as a double; the model side is
            // integer-only, so the cast happens here on the view side.
            ReportedAtUnix: (long)Time.GetUnixTimeFromSystem(),
            Mode: game?.Mode,
            Seed: game?.Seed,
            Turn: game?.Turn,
            HumanPlayers: game?.HumanPlayers ?? 0,
            ComputerPlayers: game?.ComputerPlayers ?? 0,
            MapName: game?.MapName,
            SaveBytes: save?.LongLength,
            LogBytes: log?.LongLength);

        string header = BugReport.BuildHeader(context);
        string fileName = BugReport.BundleFileName(
            AppVersion.Build, game?.Seed, game?.Turn);

        SaveStore.EnsureDirectory(SaveStore.ExportDirectory);
        string path = SaveStore.ExportDirectory + fileName;
        Pack(path, header, save, log);

        string absolutePath = ProjectSettings.GlobalizePath(path);
        Log.Info(Log.LogCategory.Report,
            $"[report] bundle staged -> '{absolutePath}' " +
            $"(save {Describe(save)}, log {Describe(log)})");

        return new StagedBugReport(
            absolutePath,
            BugReport.SubjectLine(context.AppVersion),
            BugReport.BuildBody(context));
    }

    private static void Pack(string path, string header, byte[]? save, byte[]? log)
    {
        using var packer = new ZipPacker();
        Error err = packer.Open(path);
        if (err != Error.Ok)
        {
            throw new System.IO.IOException(
                $"Could not open {path} for writing: {err}");
        }
        AddEntry(packer, BugReport.HeaderEntryName,
            System.Text.Encoding.UTF8.GetBytes(header));
        if (save != null) AddEntry(packer, BugReport.SaveEntryName, save);
        if (log != null) AddEntry(packer, BugReport.LogEntryName, log);
        packer.Close();
    }

    private static void AddEntry(ZipPacker packer, string name, byte[] data)
    {
        Error err = packer.StartFile(name);
        if (err != Error.Ok)
        {
            throw new System.IO.IOException(
                $"Could not start zip entry {name}: {err}");
        }
        packer.WriteFile(data);
        packer.CloseFile();
    }

    /// <summary>
    /// The newest rotation in <c>user://logs/</c>. Godot appends the live
    /// session to <c>godot.log</c> and rotates prior sessions to timestamped
    /// names, so newest-by-mtime picks the session the player is reporting
    /// from. Null when file logging has not produced anything yet.
    /// </summary>
    private static byte[]? ReadNewestLog()
    {
        if (!DirAccess.DirExistsAbsolute(LogDirectory))
        {
            Log.Warn(Log.LogCategory.Report,
                $"[report] no log directory at {LogDirectory} — bundle omits the log");
            return null;
        }
        string? newest = null;
        ulong newestTime = 0;
        foreach (string name in DirAccess.GetFilesAt(LogDirectory))
        {
            if (!name.EndsWith(".log", StringComparison.Ordinal)) continue;
            string candidate = LogDirectory + name;
            ulong modified = FileAccess.GetModifiedTime(candidate);
            if (newest != null && modified <= newestTime) continue;
            newest = candidate;
            newestTime = modified;
        }
        if (newest == null)
        {
            Log.Warn(Log.LogCategory.Report,
                "[report] log directory holds no .log files — bundle omits the log");
            return null;
        }
        Log.Debug(Log.LogCategory.Report, $"[report] newest log is '{newest}'");
        return ReadIfExists(newest);
    }

    private static byte[]? ReadIfExists(string path)
    {
        if (!FileAccess.FileExists(path))
        {
            Log.Warn(Log.LogCategory.Report,
                $"[report] '{path}' not found — bundle omits it");
            return null;
        }
        using FileAccess f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null)
        {
            Log.Warn(Log.LogCategory.Report,
                $"[report] could not read '{path}': {FileAccess.GetOpenError()}");
            return null;
        }
        return f.GetBuffer((long)f.GetLength());
    }

    private static string Describe(byte[]? data)
        => data == null ? "absent" : $"{data.LongLength} bytes";
}

/// <summary>What a live game contributes to a report. Null at the call site
/// means the player filed from the main menu.</summary>
public sealed record BugReportGameFacts(
    string Mode,
    int Seed,
    int Turn,
    string? MapName,
    int HumanPlayers,
    int ComputerPlayers);

/// <summary>A staged bundle plus the prefilled mail text that goes with it.</summary>
public sealed record StagedBugReport(
    string AbsolutePath,
    string Subject,
    string Body);
