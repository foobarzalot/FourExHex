using System.Text.Json;
using Godot;

/// <summary>
/// Discrete AI-turn pacing presets the user picks from in the Settings
/// panel. <see cref="UserSettings.AiSpeedMultiplier"/> turns the choice
/// into a scalar the pacer applies to every delay; <see cref="Instant"/>
/// (multiplier 0) additionally tells the controller to put the view in
/// silent mode so per-action effects don't reach the player.
/// </summary>
public enum AiSpeed
{
    Slow,
    Normal,
    Fast,
    Instant,
}

/// <summary>
/// Discrete pacing presets for replay playback — separate from
/// <see cref="AiSpeed"/> because watching a recorded game is the
/// point of replay. <see cref="Instant"/> is NOT a smaller delay
/// multiplier (that would trampoline the pacer and freeze the main
/// thread); the controller routes it to a separate chunked,
/// frame-yielded driver (<c>InstantReplayTick</c>) that fast-forwards
/// silently. So Instant's <see cref="UserSettings.ReplaySpeedMultiplier"/>
/// is 1f and never actually consulted on that path.
/// </summary>
public enum ReplaySpeed
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
    private static AiSpeed _aiSpeed = AiSpeed.Normal;
    private static ReplaySpeed _replaySpeed = ReplaySpeed.Normal;

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

    public static AiSpeed AiSpeed
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

    public static ReplaySpeed ReplaySpeed
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
    /// Multiplier applied to AI step delays. Slow doubles them, Fast
    /// halves them, Instant returns 0 (which the pacer interprets as
    /// "run inline, no scheduling"). Read on every Schedule call so a
    /// mid-game change to <see cref="AiSpeed"/> takes effect immediately.
    /// </summary>
    public static float AiSpeedMultiplier => AiSpeed switch
    {
        AiSpeed.Slow => 2f,
        AiSpeed.Normal => 1f,
        AiSpeed.Fast => 0.5f,
        AiSpeed.Instant => 0f,
        _ => 1f,
    };

    /// <summary>Same shape as <see cref="AiSpeedMultiplier"/> but for
    /// replay playback. Instant maps to 1f (NOT 0f): a 0 multiplier
    /// would push <see cref="GodotAiPacer"/> into its inline trampoline
    /// and block the main thread for the whole recording. Instant
    /// instead takes the controller's chunked frame-yielded path, so
    /// this value is inert for it.</summary>
    public static float ReplaySpeedMultiplier => ReplaySpeed switch
    {
        ReplaySpeed.Slow => 2f,
        ReplaySpeed.Normal => 1f,
        ReplaySpeed.Fast => 0.5f,
        ReplaySpeed.Instant => 1f,
        _ => 1f,
    };

    private sealed class SettingsDto
    {
        public bool SfxEnabled { get; set; } = true;
        public bool VfxEnabled { get; set; } = true;
        public AiSpeed AiSpeed { get; set; } = AiSpeed.Normal;
        public ReplaySpeed ReplaySpeed { get; set; } = ReplaySpeed.Normal;
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
