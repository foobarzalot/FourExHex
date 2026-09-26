// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using Godot;

/// <summary>
/// Process-wide campaign progress, persisted to
/// <c>user://campaign.json</c> — a sidecar independent of game saves, so
/// deleting saves never touches ladder progress. Mirrors
/// <see cref="UserSettings"/>: lazy load on first access, atomic
/// tmp+rename writes, fall back to fresh progress on a corrupt or
/// missing file (the next status change overwrites it cleanly).
///
/// All serialization logic lives in the Godot-free model
/// (<see cref="CampaignSerializer"/> / <see cref="CampaignProgress"/>)
/// where it is unit-tested; this class is thin file I/O and is
/// test-excluded for the same reason as <see cref="SaveStore"/>.
///
/// Writes happen immediately on every status transition (level launch,
/// game end) — never "on app exit" — so a crash or force-quit can't
/// lose a result.
///
/// Also owns the per-level <b>attempt</b> lifecycle: each level's most
/// recent game lives as a gzipped save in
/// <see cref="SaveStore.CampaignAttemptsDirectory"/> (see
/// <see cref="CampaignAttempts"/>), with a recency index sidecar at
/// <c>user://campaign_attempts.json</c> that the landing Resume pick reads
/// so it never has to inflate every attempt file. The attempt file is the
/// authority whenever a level is opened; the index is a cache that is
/// rebuilt from the files when missing or unreadable.
/// </summary>
public static class CampaignStore
{
    private const string CampaignPath = "user://campaign.json";
    private const string AttemptIndexPath = "user://campaign_attempts.json";

    private static CampaignProgress? _progress;
    private static CampaignAttemptIndex? _attempts;
    private static readonly SaveStore Saves = new();

    /// <summary>The loaded (or fresh) campaign progress. Mutate only via
    /// <see cref="MarkAttempted"/> / <see cref="MarkWon"/> so changes hit disk.</summary>
    public static CampaignProgress Progress
    {
        get
        {
            EnsureLoaded();
            return _progress!;
        }
    }

    /// <summary>Mark a level attempted (Untried → Lost, Won terminal) and
    /// persist if anything changed. Called at campaign-level launch.</summary>
    public static void MarkAttempted(int level)
    {
        EnsureLoaded();
        if (!_progress!.MarkAttempted(level)) return;
        Log.Info(Log.LogCategory.Campaign,
            $"CampaignStore: level {CampaignProgress.LabelFor(level)} marked attempted (lost until won)");
        Save();
    }

    /// <summary>
    /// Configure <see cref="GameSettings"/> for a campaign launch and mark
    /// the level attempted: master seed taken from the level's baked
    /// winnable-seed table entry, roster locked to 1 Human + 5 Computer with
    /// the human's handicap set to the tier difficulty (AIs stay Soldier), any stale
    /// starting-map handoff cleared. Shared by the campaign screen's Play
    /// button and the victory overlay's "Next unbeaten level" button; the
    /// caller performs the scene change.
    /// </summary>
    public static void PrepareLaunch(int level)
    {
        GameSettings.CampaignLevel = level;
        GameSettings.MasterSeed = CampaignProgress.SeedForLevel(level);
        // The campaign roster is built from the level in Main (Player.
        // BuildCampaignRoster), NOT written into the shared freeform
        // GameSettings.PlayerKinds/Difficulties — otherwise a campaign launch
        // would clobber the New Game default for the rest of the session.
        // CampaignLevel above is the only handoff Main needs.
        int humanSlot = CampaignProgress.HumanColorSlotForLevel(level);
        int playerCount = CampaignProgress.PlayerCountForLevel(level);
        MapGenOptions options = CampaignProgress.MapGenOptionsForLevel(level);
        LoadRequest.Pending = null;
        LoadRequest.WatchReplay = false;
        // A fresh launch replaces whatever attempt the level held (the
        // sheet's Restart, after its own confirm when that attempt was
        // unfinished).
        DiscardAttempt(level);
        MarkAttempted(level);
        Log.Info(Log.LogCategory.Campaign,
            $"CampaignStore: launching level {CampaignProgress.LabelFor(level)} " +
            $"(seed {GameSettings.MasterSeed}, {playerCount} players, human slot {humanSlot} " +
            $"({GameSettings.PlayerConfig[humanSlot].Name}), " +
            $"human difficulty {CampaignProgress.DifficultyForLevel(level)}, " +
            $"mode {CampaignProgress.ModeForLevel(level)}, " +
            $"clumping {options.ClumpingFactor}, neutral {options.NeutralDensity}%)");
    }

    /// <summary>Mark a level won (terminal) and persist if anything
    /// changed. Called when the human wins a campaign game. Returns true
    /// iff the level was NEWLY won (the campaign-achievement trigger —
    /// re-winning an already-won level returns false).</summary>
    public static bool MarkWon(int level)
    {
        EnsureLoaded();
        if (!_progress!.MarkWon(level)) return false;
        Log.Info(Log.LogCategory.Campaign,
            $"CampaignStore: level {CampaignProgress.LabelFor(level)} marked WON " +
            $"({_progress.WonCount}/{CampaignProgress.LevelCount})");
        Save();
        return true;
    }

    // --- Attempts -----------------------------------------------------------

    /// <summary>The recency index: one entry per level with a stored attempt.</summary>
    public static CampaignAttemptIndex AttemptIndex
    {
        get
        {
            EnsureAttemptsLoaded();
            return _attempts!;
        }
    }

    /// <summary>
    /// The level's stored attempt, or null when none (or none readable — a
    /// failed read is logged, treated as none, and its index entry dropped).
    /// </summary>
    public static LoadedSave? LoadAttempt(int level)
    {
        LoadedSave? save = Saves.TryLoadCampaignAttempt(level);
        string label = CampaignProgress.LabelFor(level);
        if (save == null)
        {
            if (AttemptIndex.Remove(level))
            {
                Log.Warn(Log.LogCategory.Campaign,
                    $"CampaignStore: attempt load level {label} -> none, index entry dropped");
                SaveAttemptIndex();
            }
            else
            {
                Log.Info(Log.LogCategory.Campaign, $"CampaignStore: attempt load level {label} -> none");
            }
            return null;
        }
        Log.Info(Log.LogCategory.Campaign,
            $"CampaignStore: attempt load level {label} -> " +
            (save.IsFinished
                ? $"finished(winner={save.WinnerIndex}, replay={(CampaignAttempts.CanReplay(save) ? "yes" : "no")})"
                : $"unfinished(turn {save.State.Turns.TurnNumber})"));
        return save;
    }

    /// <summary>
    /// Store the level's current game as its attempt (file, then index).
    /// Called on every human turn start and once at game end (with
    /// <paramref name="winnerIndex"/> set). Throws on I/O failure so the
    /// caller can surface it like an autosave failure.
    /// </summary>
    public static void RecordAttempt(
        int level,
        GameState state,
        int masterSeed,
        System.Collections.Generic.IReadOnlyList<Player> players,
        int maxTurnNumber,
        System.Collections.Generic.IReadOnlyDictionary<PlayerId, int>? claimVictoryPromptedHighestThreshold,
        Replay? replay,
        int? winnerIndex)
    {
        (int raw, int packed) = Saves.WriteCampaignAttempt(level, state, masterSeed, players,
            maxTurnNumber, claimVictoryPromptedHighestThreshold, replay, winnerIndex);
        AttemptIndex.Set(new CampaignAttemptEntry(
            level,
            System.DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            state.Turns.TurnNumber,
            winnerIndex));
        SaveAttemptIndex();
        Log.Info(Log.LogCategory.Campaign,
            $"CampaignStore: attempt write level {CampaignProgress.LabelFor(level)} " +
            $"turn {state.Turns.TurnNumber} " +
            $"winner={(winnerIndex is int w ? (w < 0 ? "none" : w.ToString()) : "in-progress")} " +
            $"raw={raw} gz={packed}");
    }

    /// <summary>Delete the level's attempt file and index entry. No-op when none.</summary>
    public static void DiscardAttempt(int level)
    {
        try
        {
            Saves.DeleteCampaignAttempt(level);
        }
        catch (System.Exception ex)
        {
            GD.PushWarning($"Failed to discard campaign attempt: {ex.Message}");
        }
        if (AttemptIndex.Remove(level)) SaveAttemptIndex();
        Log.Info(Log.LogCategory.Campaign,
            $"CampaignStore: attempt discard level {CampaignProgress.LabelFor(level)}");
    }

    private static void EnsureAttemptsLoaded()
    {
        if (_attempts != null) return;
        _attempts = new CampaignAttemptIndex();
        bool readable = false;
        try
        {
            if (FileAccess.FileExists(AttemptIndexPath))
            {
                using FileAccess f = FileAccess.Open(AttemptIndexPath, FileAccess.ModeFlags.Read);
                if (f != null)
                {
                    _attempts = CampaignAttemptIndexSerializer.Deserialize(f.GetAsText());
                    readable = true;
                    Log.Debug(Log.LogCategory.Campaign,
                        $"CampaignStore: attempt index loaded ({_attempts.Entries.Count} entries)");
                }
            }
        }
        catch (System.Exception ex)
        {
            GD.PushWarning($"Failed to load campaign attempt index: {ex.Message}");
        }
        if (!readable) RebuildAttemptIndexFromFiles();
    }

    // Index missing or unreadable: inflate each attempt file once and
    // reconstruct it, so a lost sidecar never hides a resumable game.
    private static void RebuildAttemptIndexFromFiles()
    {
        System.Collections.Generic.IReadOnlyList<int> levels = Saves.ListCampaignAttemptLevels();
        foreach (int level in levels)
        {
            LoadedSave? save = Saves.TryLoadCampaignAttempt(level);
            if (save == null) continue;
            _attempts!.Set(new CampaignAttemptEntry(
                level,
                Saves.CampaignAttemptSavedAtUnix(level),
                save.State.Turns.TurnNumber,
                save.WinnerIndex));
        }
        if (levels.Count > 0) SaveAttemptIndex();
        Log.Debug(Log.LogCategory.Campaign,
            $"CampaignStore: attempt index rebuilt from files ({_attempts!.Entries.Count} entries)");
    }

    private static void SaveAttemptIndex()
    {
        try
        {
            AtomicUserFile.Write(AttemptIndexPath, CampaignAttemptIndexSerializer.Serialize(_attempts!));
        }
        catch (System.Exception ex)
        {
            GD.PushWarning($"Failed to save campaign attempt index: {ex.Message}");
        }
    }

    private static void EnsureLoaded()
    {
        if (_progress != null) return;
        // Assign fresh progress up front so a parse failure doesn't retry
        // on every access — we fall back and stay there until the next
        // mark overwrites the file cleanly.
        _progress = new CampaignProgress();
        try
        {
            if (!FileAccess.FileExists(CampaignPath))
            {
                Log.Debug(Log.LogCategory.Campaign,
                    "CampaignStore: no campaign.json — starting fresh");
                return;
            }
            using FileAccess f = FileAccess.Open(CampaignPath, FileAccess.ModeFlags.Read);
            if (f == null) return;
            _progress = CampaignSerializer.Deserialize(f.GetAsText());
            Log.Info(Log.LogCategory.Campaign,
                $"CampaignStore: loaded — {_progress.WonCount}/{CampaignProgress.LevelCount} won, " +
                $"next up {(_progress.NextUp is int n ? CampaignProgress.LabelFor(n) : "none")}");
        }
        catch (System.Exception ex)
        {
            GD.PushWarning($"Failed to load campaign progress: {ex.Message}");
        }
    }

    private static void Save()
    {
        try
        {
            AtomicUserFile.Write(CampaignPath, CampaignSerializer.Serialize(_progress!));
            Log.Debug(Log.LogCategory.Campaign, "CampaignStore: saved campaign.json");
        }
        catch (System.Exception ex)
        {
            // In-memory state is already updated, so the session still
            // sees the result — we just won't persist it.
            GD.PushWarning($"Failed to save campaign progress: {ex.Message}");
        }
    }
}
