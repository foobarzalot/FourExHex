// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FourExHex
using FourExHex.Controller;
using FourExHex.Model;
using Godot;

/// <summary>
/// Scene-free core of the .fxhmap import flow: read → validate
/// (<see cref="MapImport.Validate"/>, untrusted input) → write to
/// <c>user://maps/</c> → localized outcome message. Callable from any
/// entry point (menu dialogs, drop folder, OS share/open inbox); showing
/// the outcome is the caller's job.
/// </summary>
public static class MapImportFlow
{
    public readonly struct Outcome
    {
        public bool Ok { get; init; }
        public string Message { get; init; }
    }

    /// <summary>Import the map file at <paramref name="path"/> (Godot VFS or
    /// absolute native path). Drop-folder sources are consumed on success so
    /// the folder picker doesn't re-list files already in
    /// <c>user://maps/</c>.</summary>
    public static Outcome ImportAtPath(SaveStore store, string path)
    {
        string json;
        using (FileAccess f = FileAccess.Open(path, FileAccess.ModeFlags.Read))
        {
            if (f == null)
            {
                Log.Warn(Log.LogCategory.Share,
                    $"[share] import could not read '{path}': {FileAccess.GetOpenError()}");
                return new Outcome
                {
                    Ok = false,
                    Message = Strings.Get(StringKeys.ImportErrorMalformed),
                };
            }
            json = f.GetAsText();
        }

        MapImportResult result = MapImport.Validate(json, store.UserMapNames());
        if (!result.Ok)
        {
            Log.Warn(Log.LogCategory.Share,
                $"[share] import rejected: {result.Error} — {result.ErrorDetail}");
            return new Outcome
            {
                Ok = false,
                Message = Strings.Get(MapImportStrings.KeyFor(result.Error!.Value),
                    ("problems", result.ErrorDetail ?? "")),
            };
        }

        store.ImportMap(result.NormalizedJson!, result.FinalName);
        Log.Info(Log.LogCategory.Share,
            $"[share] import '{path}' -> ok name='{result.FinalName}' " +
            $"renamed={result.Renamed}");
        if (path.StartsWith(SaveStore.ImportDirectory))
        {
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));
        }
        return new Outcome
        {
            Ok = true,
            Message = Strings.Get(
                result.Renamed ? StringKeys.ImportRenamed : StringKeys.ImportSuccess,
                ("name", result.FinalName)),
        };
    }
}
