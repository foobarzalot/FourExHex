using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;

/// <summary>
/// File-system layer for save games. Wraps Godot's <see cref="FileAccess"/>
/// and <see cref="DirAccess"/> against the <c>user://saves/</c> folder
/// (resolves to <c>~/Library/Application Support/Godot/app_userdata/FourExHex/saves/</c>
/// on macOS).
///
/// Test-excluded — depends on Godot's resource paths. The pure C# work
/// (DTOs, JSON encode/decode) lives in <see cref="SaveSerializer"/> and
/// IS tested.
/// </summary>
public sealed class SaveStore
{
    public const string SaveDirectory = "user://saves/";
    public const string MapsDirectory = "user://maps/";
    public const string TutorialsDirectory = "user://tutorials/";
    // Read-only directory shipped with the game (e.g. tutorial map).
    // res:// resolves into the PCK in exported builds and into the
    // project tree in the editor — FileAccess handles both transparently.
    public const string BundledMapsDirectory = "res://tutorials/";
    public const string AutosaveSlotName = "autosave";
    private const string SaveExtension = ".json";
    private const string TempExtension = ".json.tmp";

    /// <summary>
    /// Write the autosave slot. Called from <see cref="Main"/> on every
    /// <see cref="GameController.HumanTurnStarted"/> event.
    /// </summary>
    public void WriteAutosave(
        GameState state,
        int masterSeed,
        IReadOnlyList<Player> players,
        int maxTurnNumber,
        string? originMapName = null,
        IReadOnlyDictionary<PlayerId, int>? claimVictoryPromptedHighestThreshold = null,
        Replay? replay = null)
    {
        WriteSlot(AutosaveSlotName, state, masterSeed, players, maxTurnNumber,
            originMapName, claimVictoryPromptedHighestThreshold, replay);
    }

    /// <summary>
    /// Write a manual save slot. <paramref name="slotName"/> is sanitized
    /// to safe filename chars; the displayed slot name and file basename
    /// are the sanitized form so the load picker matches what the user
    /// typed (modulo special chars).
    /// </summary>
    public void WriteSlot(
        string slotName,
        GameState state,
        int masterSeed,
        IReadOnlyList<Player> players,
        int maxTurnNumber,
        string? originMapName = null,
        IReadOnlyDictionary<PlayerId, int>? claimVictoryPromptedHighestThreshold = null,
        Replay? replay = null)
    {
        WriteSlotIn(SaveDirectory, slotName, state, masterSeed, players,
            maxTurnNumber, originMapName, claimVictoryPromptedHighestThreshold, replay);
    }

    /// <summary>
    /// Persist a "starting map" from the editor. Same JSON format as a
    /// regular save but written to <see cref="MapsDirectory"/> so it
    /// shows up separately, and the caller is expected to construct
    /// <paramref name="state"/> with TurnNumber == 0 — the on-disk
    /// marker that distinguishes a starting map from an in-progress
    /// game when we add a "Load Map" entry point.
    /// </summary>
    public void WriteMapSlot(
        string slotName,
        GameState state,
        int masterSeed,
        IReadOnlyList<Player> players)
    {
        EnsureDirectory(MapsDirectory);
        string sanitized = SanitizeSlotName(slotName);
        string json = SaveSerializer.SerializeMap(state, masterSeed, players, sanitized);
        AtomicWrite(MapsDirectory, sanitized, json);
    }

    /// <summary>
    /// Persist a tutorial to <see cref="TutorialsDirectory"/>. Mirrors
    /// <see cref="WriteMapSlot"/>'s shape — same JSON format (turn 0
    /// marker via <see cref="SaveSerializer.SerializeMap"/>) plus the
    /// optional Tutorial block. The map state is the *starting* map
    /// the tutorial plays out on; the dev paints it in Map Edit mode.
    /// </summary>
    public void WriteTutorial(
        string slotName,
        GameState mapState,
        int masterSeed,
        IReadOnlyList<Player> players,
        Tutorial tutorial)
    {
        EnsureDirectory(TutorialsDirectory);
        string sanitized = SanitizeSlotName(slotName);
        string json = SaveSerializer.SerializeMap(mapState, masterSeed, players, sanitized, tutorial);
        AtomicWrite(TutorialsDirectory, sanitized, json);
    }

    private static void WriteSlotIn(
        string directory,
        string slotName,
        GameState state,
        int masterSeed,
        IReadOnlyList<Player> players,
        int maxTurnNumber,
        string? originMapName,
        IReadOnlyDictionary<PlayerId, int>? claimVictoryPromptedHighestThreshold,
        Replay? replay)
    {
        EnsureDirectory(directory);
        string sanitized = SanitizeSlotName(slotName);
        string json = SaveSerializer.Serialize(
            state, masterSeed, players, sanitized, maxTurnNumber,
            originMapName, claimVictoryPromptedHighestThreshold,
            tutorial: null,
            replay: replay);
        AtomicWrite(directory, sanitized, json);
    }

    private static void AtomicWrite(string directory, string sanitized, string json)
    {
        // Atomic write: produce <name>.json.tmp first, then rename to
        // <name>.json. A crash mid-write leaves the prior save intact.
        string tempPath = directory + sanitized + TempExtension;
        string finalPath = directory + sanitized + SaveExtension;

        using (FileAccess f = FileAccess.Open(tempPath, FileAccess.ModeFlags.Write))
        {
            if (f == null)
            {
                throw new System.IO.IOException(
                    $"Could not open {tempPath} for writing: {FileAccess.GetOpenError()}");
            }
            f.StoreString(json);
        }
        // Remove any old final file then rename. Godot's DirAccess
        // doesn't offer atomic rename across an existing destination,
        // so the two-step is the best we can do without P/Invoke.
        if (FileAccess.FileExists(finalPath))
        {
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(finalPath));
        }
        Error err = DirAccess.RenameAbsolute(
            ProjectSettings.GlobalizePath(tempPath),
            ProjectSettings.GlobalizePath(finalPath));
        if (err != Error.Ok)
        {
            throw new System.IO.IOException(
                $"Could not rename {tempPath} to {finalPath}: {err}");
        }
    }

    /// <summary>
    /// List every slot in the save directory. Sorted by save time
    /// descending (most recent first), with the autosave slot
    /// always at the top regardless of timestamp.
    /// </summary>
    public IReadOnlyList<SaveSlotInfo> ListSlots() => ListSlotsIn(SaveDirectory);

    /// <summary>List every starting map in <see cref="MapsDirectory"/>.</summary>
    public IReadOnlyList<SaveSlotInfo> ListMaps() => ListSlotsIn(MapsDirectory);

    private IReadOnlyList<SaveSlotInfo> ListSlotsIn(string directory)
    {
        var infos = new List<SaveSlotInfo>();
        if (!DirAccess.DirExistsAbsolute(directory))
        {
            return infos;
        }

        using DirAccess dir = DirAccess.Open(directory)!;
        dir.ListDirBegin();
        for (string name = dir.GetNext(); name.Length > 0; name = dir.GetNext())
        {
            if (dir.CurrentIsDir()) continue;
            if (!name.EndsWith(SaveExtension)) continue;
            string slot = name.Substring(0, name.Length - SaveExtension.Length);
            SaveSlotInfo? info = TryReadHeader(directory, slot);
            if (info != null) infos.Add(info);
        }
        dir.ListDirEnd();

        infos.Sort((a, b) =>
        {
            if (a.IsAutosave && !b.IsAutosave) return -1;
            if (!a.IsAutosave && b.IsAutosave) return 1;
            return b.SavedAtUnix.CompareTo(a.SavedAtUnix);
        });
        return infos;
    }

    /// <summary>
    /// Load a slot by sanitized name. Throws if the file is missing or
    /// the format version doesn't match.
    /// </summary>
    public LoadedSave LoadSlot(string slotName) => LoadSlotIn(SaveDirectory, slotName);

    /// <summary>Load a starting map by sanitized name from <see cref="MapsDirectory"/>.</summary>
    public LoadedSave LoadMap(string slotName) => LoadSlotIn(MapsDirectory, slotName);

    /// <summary>Load a map shipped with the game from <see cref="BundledMapsDirectory"/>.</summary>
    public LoadedSave LoadBundledMap(string slotName) => LoadSlotIn(BundledMapsDirectory, slotName);

    /// <summary>Load a tutorial by sanitized name from <see cref="TutorialsDirectory"/>.</summary>
    public LoadedSave LoadTutorial(string slotName) => LoadSlotIn(TutorialsDirectory, slotName);

    /// <summary>List every tutorial in <see cref="TutorialsDirectory"/>.</summary>
    public IReadOnlyList<SaveSlotInfo> ListTutorials() => ListSlotsIn(TutorialsDirectory);

    /// <summary>
    /// Load a starting map by name regardless of whether it lives in the
    /// user maps directory or the bundled tutorials directory. User maps
    /// win on name collision; falls through to bundled if not found in
    /// <see cref="MapsDirectory"/>.
    /// </summary>
    public LoadedSave LoadStartingMap(string slotName)
    {
        string sanitized = SanitizeSlotName(slotName);
        string userPath = MapsDirectory + sanitized + SaveExtension;
        return FileAccess.FileExists(userPath)
            ? LoadMap(slotName)
            : LoadBundledMap(slotName);
    }

    private LoadedSave LoadSlotIn(string directory, string slotName)
    {
        string sanitized = SanitizeSlotName(slotName);
        string path = directory + sanitized + SaveExtension;
        if (!FileAccess.FileExists(path))
        {
            throw new System.IO.FileNotFoundException(
                $"Slot '{sanitized}' not found at {path}.");
        }
        using FileAccess f = FileAccess.Open(path, FileAccess.ModeFlags.Read)!;
        if (f == null)
        {
            throw new System.IO.IOException(
                $"Could not open {path} for reading: {FileAccess.GetOpenError()}");
        }
        string json = f.GetAsText();
        return SaveSerializer.Deserialize(json);
    }

    private SaveSlotInfo? TryReadHeader(string directory, string slotName)
    {
        string path = directory + slotName + SaveExtension;
        try
        {
            using FileAccess f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            if (f == null) return null;
            string json = f.GetAsText();
            // Reuse Deserialize for header info — saves are small
            // enough that we don't need a streaming header parser.
            LoadedSave save = SaveSerializer.Deserialize(json);
            // Pull SavedAt out of the raw DTO since LoadedSave doesn't
            // surface it (we only need it here for slot listing).
            using System.IO.MemoryStream ms = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
            SaveData? data = System.Text.Json.JsonSerializer.Deserialize<SaveData>(ms);
            long savedAt = data?.SavedAtUnix ?? 0;
            return new SaveSlotInfo(
                slotName: save.SlotName,
                savedAtUnix: savedAt,
                turnNumber: save.State.Turns.TurnNumber,
                isAutosave: save.SlotName == AutosaveSlotName);
        }
        catch (System.Exception ex)
        {
            GD.PushWarning($"Failed to read save header for '{slotName}': {ex.Message}");
            return null;
        }
    }

    private static void EnsureDirectory(string directory)
    {
        if (DirAccess.DirExistsAbsolute(directory)) return;
        Error err = DirAccess.MakeDirRecursiveAbsolute(directory);
        if (err != Error.Ok)
        {
            throw new System.IO.IOException(
                $"Could not create directory {directory}: {err}");
        }
    }

    /// <summary>
    /// Replace anything that isn't <c>[A-Za-z0-9_-]</c> with an
    /// underscore. Keeps slot names safe for file systems and avoids
    /// directory traversal attempts. Truncates to 64 chars; refuses
    /// empty results (caller falls back to a default).
    /// </summary>
    public static string SanitizeSlotName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "save";
        string cleaned = Regex.Replace(raw.Trim(), "[^A-Za-z0-9_-]", "_");
        if (cleaned.Length > 64) cleaned = cleaned.Substring(0, 64);
        if (cleaned.Length == 0) cleaned = "save";
        return cleaned;
    }
}
