// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using Godot;

/// <summary>
/// Crash-safe text writes for the <c>user://</c> sidecar files
/// (<see cref="SaveStore"/> slots, <see cref="CampaignStore"/>,
/// <see cref="UserSettings"/>).
///
/// Every caller wants the same thing: never leave a half-written file
/// where a readable one used to be. This is the one place that knows how.
/// Callers own their own error policy — <see cref="Write"/> throws, and
/// each store decides whether that is a <c>GD.PushWarning</c> and carry on
/// (settings, campaign, achievements — in-memory state is already correct)
/// or a propagated failure (save slots, where the user asked for a write).
/// </summary>
public static class AtomicUserFile
{
    /// <summary>Suffix appended to the destination to form the scratch path.</summary>
    private const string TempSuffix = ".tmp";

    /// <summary>
    /// Write <paramref name="text"/> to <paramref name="path"/> via a
    /// <c>&lt;path&gt;.tmp</c> scratch file that is renamed into place, so a
    /// crash mid-write leaves the prior file intact.
    /// </summary>
    /// <exception cref="System.IO.IOException">
    /// The scratch file could not be opened, or the rename failed.
    /// </exception>
    public static void Write(string path, string text)
    {
        string tempPath = path + TempSuffix;

        using (FileAccess f = FileAccess.Open(tempPath, FileAccess.ModeFlags.Write))
        {
            if (f == null)
            {
                throw new System.IO.IOException(
                    $"Could not open {tempPath} for writing: {FileAccess.GetOpenError()}");
            }
            f.StoreString(text);
        }

        // Godot's DirAccess has no atomic rename across an existing
        // destination, so remove-then-rename is the best available without
        // P/Invoke.
        if (FileAccess.FileExists(path))
        {
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));
        }
        Error err = DirAccess.RenameAbsolute(
            ProjectSettings.GlobalizePath(tempPath),
            ProjectSettings.GlobalizePath(path));
        if (err != Error.Ok)
        {
            throw new System.IO.IOException(
                $"Could not rename {tempPath} to {path}: {err}");
        }
    }
}
