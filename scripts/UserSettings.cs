using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

/// <summary>
/// Discrete pacing presets the user picks from in the Settings panel,
/// shared by the two independent speed settings
/// (<see cref="UserSettings.AiSpeed"/> for live AI turns,
/// <see cref="UserSettings.ReplaySpeed"/> for replay playback).
///
/// Slow/Normal/Fast are delay scalars — see
/// <see cref="UserSettings.SpeedMultiplierPercent"/>. <see cref="Instant"/>
/// is NOT a multiplier: a zero delay would freeze the main thread, so
/// the controller routes Instant to a chunked, frame-yielded driver
/// that fast-forwards silently and repaints once per turn (live AI and
/// replay share that driver). Instant's delays bypass the multiplier
/// entirely via <c>IAiPacer.ScheduleUnscaled</c>, so it has no entry
/// in the multiplier table.
///
/// Member order is load-bearing: settings persist numerically (no
/// JsonStringEnumConverter), so Slow=0…Instant=3 must stay fixed for
/// existing <c>user://settings.json</c> files to keep loading.
/// </summary>
public enum PlaybackSpeed
{
    Slow,
    Normal,
    Fast,
    Instant,
}

/// <summary>
/// Process-wide user preferences (SFX + VFX toggles, AI speed). Persisted
/// to <c>user://settings.json</c> so the choice survives quitting and
/// relaunching the game. Mirrors <see cref="SaveStore"/>'s atomic-write
/// pattern (tmp + rename) for crash-safety.
///
/// Test-excluded — depends on Godot's <see cref="FileAccess"/>, same rule
/// as <see cref="SaveStore"/>. The serialization is small enough that a
/// dedicated pure-C# DTO class isn't worth splitting out yet.
/// </summary>
public static partial class UserSettings
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
    private static PlaybackSpeed _aiSpeed = PlaybackSpeed.Normal;
    private static PlaybackSpeed _replaySpeed = PlaybackSpeed.Normal;

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

    public static PlaybackSpeed AiSpeed
    {
        get
        {
            EnsureLoaded();
            return _aiSpeed;
        }
        set
        {
            EnsureLoaded();
            if (_aiSpeed == value) return;
            _aiSpeed = value;
            Save();
        }
    }

    public static PlaybackSpeed ReplaySpeed
    {
        get
        {
            EnsureLoaded();
            return _replaySpeed;
        }
        set
        {
            EnsureLoaded();
            if (_replaySpeed == value) return;
            _replaySpeed = value;
            Save();
        }
    }

    /// <summary>
    /// Scalar the pacer applies to step delays for a given speed:
    /// Slow doubles them, Normal leaves them, Fast halves them. Read on
    /// every <c>Schedule</c> call (via the <c>Main</c> lambda) so a
    /// mid-game speed change takes effect immediately.
    ///
    /// <see cref="PlaybackSpeed.Instant"/> has no arm: it never reaches
    /// here. Instant routes to the chunked frame-yielded driver, which
    /// schedules via <c>IAiPacer.ScheduleUnscaled</c> (multiplier
    /// bypassed); no scaled <c>Schedule</c> runs during an Instant
    /// game. The <c>_ =&gt; 100</c> default is a harmless safety net.
    ///
    /// Returns integer percent (50 / 100 / 200) — Slow doubles the
    /// delay, Normal is 1×, Fast halves it. Integer so the controller's
    /// GodotAiPacer can stay float-free (issue #20).
    /// </summary>
    public static int SpeedMultiplierPercent(PlaybackSpeed speed) => speed switch
    {
        PlaybackSpeed.Slow => 200,
        PlaybackSpeed.Normal => 100,
        PlaybackSpeed.Fast => 50,
        _ => 100,
    };

    private sealed class SettingsDto
    {
        public bool SfxEnabled { get; set; } = true;
        public bool VfxEnabled { get; set; } = true;
        // Property names unchanged for save-compat: existing
        // settings.json keys "AiSpeed"/"ReplaySpeed" still bind, and
        // PlaybackSpeed's numeric order matches the old enums'.
        public PlaybackSpeed AiSpeed { get; set; } = PlaybackSpeed.Normal;
        public PlaybackSpeed ReplaySpeed { get; set; } = PlaybackSpeed.Normal;
    }

    // Source-gen JsonSerializerContext for SettingsDto. Nested inside
    // UserSettings so it can reach the private SettingsDto type. Needed
    // because iOS AOT disables reflection-based JSON; the [JsonSerializable]
    // attribute below generates a JsonTypeInfo<SettingsDto> table at compile
    // time. The [JsonSourceGenerationOptions(WriteIndented = true)] mirrors
    // the option Save() historically passed, so settings.json stays formatted
    // identically across the migration.
    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(SettingsDto))]
    private partial class JsonContext : JsonSerializerContext
    {
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
            SettingsDto? dto = JsonSerializer.Deserialize(json, JsonContext.Default.SettingsDto);
            if (dto == null) return;
            _isLoading = true;
            _sfxEnabled = dto.SfxEnabled;
            _vfxEnabled = dto.VfxEnabled;
            _aiSpeed = dto.AiSpeed;
            _replaySpeed = dto.ReplaySpeed;
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
                new SettingsDto
                {
                    SfxEnabled = _sfxEnabled,
                    VfxEnabled = _vfxEnabled,
                    AiSpeed = _aiSpeed,
                    ReplaySpeed = _replaySpeed,
                },
                JsonContext.Default.SettingsDto);

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
