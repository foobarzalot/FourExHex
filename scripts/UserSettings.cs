using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

/// <summary>
/// Discrete pacing presets the user picks from in the Settings panel,
/// shared by the three independent speed settings
/// (<see cref="UserSettings.AiSpeed"/> for live AI turns,
/// <see cref="UserSettings.ReplaySpeed"/> for replay playback,
/// <see cref="UserSettings.AutomateSpeed"/> for human-turn automation).
///
/// Slow/Normal/Fast are delay scalars — see
/// <see cref="UserSettings.SpeedMultiplierPercent"/>. <see cref="Instant"/>
/// is NOT a multiplier: a zero delay would freeze the main thread, so
/// the controller routes Instant to a chunked, frame-yielded driver
/// that fast-forwards silently and repaints once per batch (live AI,
/// replay, and Automate each wrap that driver). Instant's delays bypass
/// the multiplier entirely via <c>IAiPacer.ScheduleUnscaled</c>, so it
/// has no entry in the multiplier table.
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
    private static PlaybackSpeed _aiSpeed = PlaybackSpeed.Fast;
    private static PlaybackSpeed _replaySpeed = PlaybackSpeed.Fast;
    private static PlaybackSpeed _automateSpeed = PlaybackSpeed.Normal;
    // One-time "seen the intro overlay" flags per special game mode (issue
    // #96). Persisted so the mode explainer never re-shows after the player
    // dismisses it once. Freeform has no intro and is never tracked here.
    private static bool _seenRisingTidesIntro;
    private static bool _seenFogOfWarIntro;
    private static bool _seenVikingRaidersIntro;
    // One-time "seen the terrain intro overlay" flags per teachable feature
    // (issue #53): the first map that contains gold / a mountain pops a short
    // explainer + camera pan, then never again. None is never tracked here.
    private static bool _seenGoldIntro;
    private static bool _seenMountainIntro;

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
    /// Pacing for the human-turn Automate loop — independent of
    /// <see cref="AiSpeed"/> (opponent turns) so a player can keep
    /// opponents brisk while watching their own automated moves, or
    /// vice-versa.
    /// </summary>
    public static PlaybackSpeed AutomateSpeed
    {
        get
        {
            EnsureLoaded();
            return _automateSpeed;
        }
        set
        {
            EnsureLoaded();
            if (_automateSpeed == value) return;
            _automateSpeed = value;
            Save();
        }
    }

    /// <summary>
    /// Whether the player has already dismissed the one-time intro overlay
    /// for <paramref name="mode"/> (issue #96). Freeform has no intro, so it
    /// reports "seen" — the caller never shows an overlay for it.
    /// </summary>
    public static bool HasSeenModeIntro(GameMode mode)
    {
        EnsureLoaded();
        return mode switch
        {
            GameMode.RisingTides => _seenRisingTidesIntro,
            GameMode.FogOfWar => _seenFogOfWarIntro,
            GameMode.VikingRaiders => _seenVikingRaidersIntro,
            _ => true,
        };
    }

    /// <summary>
    /// Record that the player has seen the intro overlay for
    /// <paramref name="mode"/> so it won't re-show. No-op (and no write) for
    /// Freeform or an already-seen mode.
    /// </summary>
    public static void MarkModeIntroSeen(GameMode mode)
    {
        EnsureLoaded();
        switch (mode)
        {
            case GameMode.RisingTides:
                if (_seenRisingTidesIntro) return;
                _seenRisingTidesIntro = true;
                break;
            case GameMode.FogOfWar:
                if (_seenFogOfWarIntro) return;
                _seenFogOfWarIntro = true;
                break;
            case GameMode.VikingRaiders:
                if (_seenVikingRaidersIntro) return;
                _seenVikingRaidersIntro = true;
                break;
            default:
                return;
        }
        Save();
    }

    /// <summary>
    /// Whether the player has already dismissed the one-time intro overlay for
    /// terrain <paramref name="feature"/> (issue #53). <see cref="TerrainFeature.None"/>
    /// has no intro, so it reports "seen" — the caller never shows one for it.
    /// </summary>
    public static bool HasSeenTerrainIntro(TerrainFeature feature)
    {
        EnsureLoaded();
        return feature switch
        {
            TerrainFeature.Gold => _seenGoldIntro,
            TerrainFeature.Mountain => _seenMountainIntro,
            _ => true,
        };
    }

    /// <summary>
    /// Record that the player has seen the intro overlay for terrain
    /// <paramref name="feature"/> so it won't re-show. No-op (and no write) for
    /// <see cref="TerrainFeature.None"/> or an already-seen feature.
    /// </summary>
    public static void MarkTerrainIntroSeen(TerrainFeature feature)
    {
        EnsureLoaded();
        switch (feature)
        {
            case TerrainFeature.Gold:
                if (_seenGoldIntro) return;
                _seenGoldIntro = true;
                break;
            case TerrainFeature.Mountain:
                if (_seenMountainIntro) return;
                _seenMountainIntro = true;
                break;
            default:
                return;
        }
        Save();
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
    /// GodotAiPacer can stay float-free.
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
        // Keys AiSpeed/ReplaySpeed bind by name; PlaybackSpeed's numeric
        // order is load-bearing for save-compat.
        public PlaybackSpeed AiSpeed { get; set; } = PlaybackSpeed.Fast;
        public PlaybackSpeed ReplaySpeed { get; set; } = PlaybackSpeed.Fast;
        // One-time mode-intro "seen" flags (issue #96). Absent from older
        // settings.json files → default false → intro shows once, as intended.
        public bool SeenRisingTidesIntro { get; set; }
        public bool SeenFogOfWarIntro { get; set; }
        public bool SeenVikingRaidersIntro { get; set; }
        // One-time terrain-intro "seen" flags (issue #53). Same absent→false→
        // shows-once semantics as the mode flags above. Appended last to keep
        // the DTO order stable for save-compat.
        public bool SeenGoldIntro { get; set; }
        public bool SeenMountainIntro { get; set; }
        // Human-turn Automate pacing. Appended last for save-compat; absent
        // from older settings.json files → binds the Normal default.
        public PlaybackSpeed AutomateSpeed { get; set; } = PlaybackSpeed.Normal;
    }

    // Source-gen JsonSerializerContext for SettingsDto. Nested inside
    // UserSettings so it can reach the private SettingsDto type. Needed
    // because iOS AOT disables reflection-based JSON; the [JsonSerializable]
    // attribute below generates a JsonTypeInfo<SettingsDto> table at compile
    // time. The [JsonSourceGenerationOptions(WriteIndented = true)] keeps
    // settings.json indented.
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
            _automateSpeed = dto.AutomateSpeed;
            _seenRisingTidesIntro = dto.SeenRisingTidesIntro;
            _seenFogOfWarIntro = dto.SeenFogOfWarIntro;
            _seenVikingRaidersIntro = dto.SeenVikingRaidersIntro;
            _seenGoldIntro = dto.SeenGoldIntro;
            _seenMountainIntro = dto.SeenMountainIntro;
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
                    AutomateSpeed = _automateSpeed,
                    SeenRisingTidesIntro = _seenRisingTidesIntro,
                    SeenFogOfWarIntro = _seenFogOfWarIntro,
                    SeenVikingRaidersIntro = _seenVikingRaidersIntro,
                    SeenGoldIntro = _seenGoldIntro,
                    SeenMountainIntro = _seenMountainIntro,
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
