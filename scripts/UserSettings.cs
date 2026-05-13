using System.Text.Json;
using Godot;

/// <summary>
/// Process-wide user preferences (currently SFX + VFX toggles). Persisted
/// to <c>user://settings.json</c> so the choice survives quitting and
/// relaunching the game. Mirrors <see cref="SaveStore"/>'s atomic-write
/// pattern (tmp + rename) for crash-safety.
///
/// Test-excluded — depends on Godot's <see cref="FileAccess"/>, same rule
/// as <see cref="SaveStore"/>. The serialization is small enough that a
/// dedicated pure-C# DTO class isn't worth splitting out yet.
/// </summary>
public static class UserSettings
{
    private const string SettingsPath = "user://settings.json";
    private const string TempPath = "user://settings.json.tmp";

    private static bool _loaded;
    // True while Load() is populating the backing fields so the public
    // setters' Save() side-effect doesn't re-enter and write the partially
    // loaded state back to disk.
    private static bool _isLoading;
    private static bool _sfxEnabled = true;
    private static bool _vfxEnabled = true;

    public static bool SfxEnabled
    {
        get
        {
            EnsureLoaded();
            return _sfxEnabled;
        }
        set
        {
            EnsureLoaded();
            if (_sfxEnabled == value) return;
            _sfxEnabled = value;
            Save();
        }
    }

    public static bool VfxEnabled
    {
        get
        {
            EnsureLoaded();
            return _vfxEnabled;
        }
        set
        {
            EnsureLoaded();
            if (_vfxEnabled == value) return;
            _vfxEnabled = value;
            Save();
        }
    }

    private sealed class SettingsDto
    {
        public bool SfxEnabled { get; set; } = true;
        public bool VfxEnabled { get; set; } = true;
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        // Mark loaded up front so a parse failure (corrupt file, missing
        // file, read error) doesn't retry on every property read — we
        // fall back to defaults and stay there until the next setter
        // overwrites the file cleanly.
        _loaded = true;
        try
        {
            if (!FileAccess.FileExists(SettingsPath)) return;
            using FileAccess f = FileAccess.Open(SettingsPath, FileAccess.ModeFlags.Read);
            if (f == null) return;
            string json = f.GetAsText();
            SettingsDto? dto = JsonSerializer.Deserialize<SettingsDto>(json);
            if (dto == null) return;
            _isLoading = true;
            _sfxEnabled = dto.SfxEnabled;
            _vfxEnabled = dto.VfxEnabled;
        }
        catch (System.Exception ex)
        {
            GD.PushWarning($"Failed to load user settings: {ex.Message}");
        }
        finally
        {
            _isLoading = false;
        }
    }

    private static void Save()
    {
        if (_isLoading) return;
        try
        {
            string json = JsonSerializer.Serialize(
                new SettingsDto { SfxEnabled = _sfxEnabled, VfxEnabled = _vfxEnabled },
                new JsonSerializerOptions { WriteIndented = true });

            // Atomic write: write to <name>.tmp then rename over the final
            // path so a crash mid-write leaves the prior file intact.
            using (FileAccess f = FileAccess.Open(TempPath, FileAccess.ModeFlags.Write))
            {
                if (f == null)
                {
                    throw new System.IO.IOException(
                        $"Could not open {TempPath} for writing: {FileAccess.GetOpenError()}");
                }
                f.StoreString(json);
            }
            if (FileAccess.FileExists(SettingsPath))
            {
                DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(SettingsPath));
            }
            Error err = DirAccess.RenameAbsolute(
                ProjectSettings.GlobalizePath(TempPath),
                ProjectSettings.GlobalizePath(SettingsPath));
            if (err != Error.Ok)
            {
                throw new System.IO.IOException(
                    $"Could not rename {TempPath} to {SettingsPath}: {err}");
            }
        }
        catch (System.Exception ex)
        {
            // In-memory state is already updated, so the toggle still
            // works for this session — we just won't persist it.
            GD.PushWarning($"Failed to save user settings: {ex.Message}");
        }
    }
}
