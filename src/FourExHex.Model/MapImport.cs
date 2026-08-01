// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;
using System.Text.Json;
using FourExHex.Model;

/// <summary>Typed failure categories for <see cref="MapImport.Validate"/>.
/// The view layer maps each to a localized message (see
/// <c>MapImportStrings</c> in the controller library).</summary>
public enum MapImportError
{
    /// <summary>Not JSON, not a FourExHex file, or fails deserialization.</summary>
    Malformed,

    /// <summary>FormatVersion is newer than this build understands.</summary>
    TooNew,

    /// <summary>An in-progress game save, not a turn-0 starting map.</summary>
    NotStartingMap,

    /// <summary>Coords outside the supported board rectangle, or too many cells.</summary>
    TooLarge,

    /// <summary>Deserializes but is not a playable map (roster/territory mismatch).</summary>
    Invalid,
}

/// <summary>Outcome of validating an untrusted <c>.fxhmap</c> payload.</summary>
public sealed class MapImportResult
{
    /// <summary>True iff the file is a safe, playable starting map.</summary>
    public bool Ok => Error == null;

    /// <summary>The fully deserialized map when <see cref="Ok"/>.</summary>
    public LoadedSave? Loaded { get; }

    /// <summary>Re-serialized map JSON to write into <c>user://maps/</c>:
    /// <c>SlotName</c> rewritten to <see cref="FinalName"/> so the list UI
    /// (which labels rows from the header) agrees with the filename.</summary>
    public string? NormalizedJson { get; }

    /// <summary>Sanitized, collision-free name to store the map under.</summary>
    public string FinalName { get; }

    /// <summary>True when a name collision forced an auto-suffix.</summary>
    public bool Renamed { get; }

    public MapImportError? Error { get; }

    /// <summary>Human-readable specifics (exception message, joined roster
    /// problems). Diagnostic; the user-facing message comes from
    /// <see cref="Error"/>.</summary>
    public string? ErrorDetail { get; }

    private MapImportResult(
        LoadedSave? loaded, string? normalizedJson, string finalName,
        bool renamed, MapImportError? error, string? errorDetail)
    {
        Loaded = loaded;
        NormalizedJson = normalizedJson;
        FinalName = finalName;
        Renamed = renamed;
        Error = error;
        ErrorDetail = errorDetail;
    }

    internal static MapImportResult Success(
        LoadedSave loaded, string normalizedJson, string finalName, bool renamed)
        => new(loaded, normalizedJson, finalName, renamed, null, null);

    internal static MapImportResult Fail(MapImportError error, string? detail = null)
        => new(null, null, "", false, error, detail);
}

/// <summary>
/// Validate-before-load for shared map files. These are untrusted input
/// on the deterministic game-state path: reject anything malformed,
/// hostile, or unplayable with a typed error instead of letting it crash
/// the view layer, and resolve name collisions without overwriting.
/// Pure, Godot-free; the file I/O shim (<c>SaveStore.ImportMap</c>) wraps it.
/// </summary>
public static class MapImport
{
    public const int MaxCols = 128;
    public const int MaxRows = 128;
    public const int MaxCells = MaxCols * MaxRows;

    /// <summary>Display cap for the free-text author field — matches the
    /// export prompt's cap, and bounds what a hostile file can smuggle
    /// into the map list.</summary>
    public const int MaxAuthorLength = 40;

    public static MapImportResult Validate(
        string json, IReadOnlyCollection<string> existingMapNames)
    {
        // 1. Well-formed JSON that binds to the SaveData schema at all.
        SaveData? data;
        try
        {
            data = JsonSerializer.Deserialize(json, FourExHexJsonContext.Default.SaveData);
        }
        catch (JsonException ex)
        {
            return MapImportResult.Fail(MapImportError.Malformed, ex.Message);
        }
        if (data == null)
        {
            return MapImportResult.Fail(MapImportError.Malformed, "empty document");
        }

        // 2. Version gate. Too-new gets its own error (the fix is "update
        // the app"); pre-v2 never existed as a real file → malformed.
        if (data.FormatVersion > SaveSerializer.CurrentFormatVersion)
        {
            return MapImportResult.Fail(MapImportError.TooNew,
                $"format {data.FormatVersion}, this build reads up to " +
                $"{SaveSerializer.CurrentFormatVersion}");
        }
        if (data.FormatVersion < 2)
        {
            return MapImportResult.Fail(MapImportError.Malformed,
                $"format {data.FormatVersion}");
        }

        // 3. Starting-map discriminator: the exporter always writes
        // turn 0 + uncapped max-turn; anything else is a game save.
        if (data.TurnNumber != 0 || data.MaxTurnNumber != int.MaxValue)
        {
            return MapImportResult.Fail(MapImportError.NotStartingMap,
                $"turn {data.TurnNumber}, maxTurn {data.MaxTurnNumber}");
        }

        // 4. Bounds, checked on the raw DTO before the full deserialize so
        // a hostile coord never reaches MapBounds.Infer / view allocation.
        // The cell-count cap also blocks duplicate-coord flooding.
        if (data.Tiles.Count + data.Water.Count > MaxCells)
        {
            return MapImportResult.Fail(MapImportError.TooLarge,
                $"{data.Tiles.Count + data.Water.Count} cells (max {MaxCells})");
        }
        foreach (TileDto tile in data.Tiles)
        {
            if (OutOfBounds(tile.Q, tile.R, out string? why))
            {
                return MapImportResult.Fail(MapImportError.TooLarge, why);
            }
        }
        foreach (CoordDto coord in data.Water)
        {
            if (OutOfBounds(coord.Q, coord.R, out string? why))
            {
                return MapImportResult.Fail(MapImportError.TooLarge, why);
            }
        }

        // 5. Full deserialize. The serializer's explicit switches reject
        // unknown occupant/kind names; duplicate coords fail the grid add.
        // Any throw here means the file is not loadable — catch everything
        // at this trust boundary rather than crash on hostile input.
        LoadedSave loaded;
        try
        {
            loaded = SaveSerializer.Deserialize(json);
        }
        catch (Exception ex)
        {
            return MapImportResult.Fail(MapImportError.Malformed, ex.Message);
        }

        // 6. Playability: baked rosters must agree with the painted
        // territory (same rule as the editor's save path); legacy no-kind
        // maps just need two landed capitals for the default roster.
        if (loaded.MapHasBakedKinds)
        {
            MapRosterRules.DeriveKindsFromLoad(loaded, out PlayerKind[] kinds, out _);
            IReadOnlyList<string> problems =
                MapRosterRules.ValidateForSave(loaded.State.Territories, kinds);
            if (problems.Count > 0)
            {
                return MapImportResult.Fail(MapImportError.Invalid,
                    string.Join(" ", problems));
            }
        }
        else
        {
            var ownersWithCapital = new HashSet<int>();
            foreach (Territory t in loaded.State.Territories)
            {
                if (!t.Owner.IsNone && t.HasCapital) ownersWithCapital.Add(t.Owner.Index);
            }
            if (ownersWithCapital.Count < 2)
            {
                return MapImportResult.Fail(MapImportError.Invalid,
                    $"a map needs at least 2 players with capitals; " +
                    $"{ownersWithCapital.Count} found");
            }
        }

        // 7. Destination name: sanitize (traversal defense), then resolve
        // collisions by suffixing, and rewrite the header's SlotName so
        // the list label matches the file. Re-serializing our own DTO also
        // normalizes the payload we persist.
        string baseName = SaveNames.Sanitize(data.SlotName);
        string finalName = ResolveName(baseName, existingMapNames);
        data.SlotName = finalName;
        // Author is untrusted free text — trim and cap it so the list UI
        // never renders a hostile payload; empty collapses to omitted.
        if (data.Author != null)
        {
            string author = data.Author.Trim();
            if (author.Length > MaxAuthorLength)
            {
                author = author.Substring(0, MaxAuthorLength);
            }
            data.Author = author.Length == 0 ? null : author;
        }
        string normalized = JsonSerializer.Serialize(
            data, FourExHexJsonContext.Default.SaveData);
        return MapImportResult.Success(
            loaded, normalized, finalName, renamed: finalName != baseName);
    }

    private static bool OutOfBounds(int q, int r, out string? why)
    {
        (int col, int row) = new HexCoord(q, r).ToOffset();
        if (col < 0 || row < 0 || col >= MaxCols || row >= MaxRows)
        {
            why = $"coord q={q} r={r} → offset ({col},{row}), " +
                $"allowed 0..{MaxCols - 1} x 0..{MaxRows - 1}";
            return true;
        }
        why = null;
        return false;
    }

    /// <summary>Resolve a name collision by auto-suffixing (<c>map</c> →
    /// <c>map-2</c> → <c>map-3</c> …), truncating the base so the result
    /// stays within <see cref="SaveNames"/>' length cap.</summary>
    public static string ResolveName(string name, IReadOnlyCollection<string> existing)
    {
        var taken = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(name)) return name;
        for (int i = 2; ; i++)
        {
            string suffix = $"-{i}";
            string basePart = name.Length + suffix.Length > SaveNames.MaxLength
                ? name.Substring(0, SaveNames.MaxLength - suffix.Length)
                : name;
            string candidate = basePart + suffix;
            if (!taken.Contains(candidate)) return candidate;
        }
    }
}
