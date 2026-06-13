using Godot;

/// <summary>
/// Process-wide campaign progress (issue #2), persisted to
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
/// </summary>
public static class CampaignStore
{
    private const string CampaignPath = "user://campaign.json";
    private const string TempPath = "user://campaign.json.tmp";

    private static CampaignProgress? _progress;

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

    /// <summary>Mark a level won (terminal) and persist if anything
    /// changed. Called when the human wins a campaign game.</summary>
    public static void MarkWon(int level)
    {
        EnsureLoaded();
        if (!_progress!.MarkWon(level)) return;
        Log.Info(Log.LogCategory.Campaign,
            $"CampaignStore: level {CampaignProgress.LabelFor(level)} marked WON " +
            $"({_progress.WonCount}/{CampaignProgress.LevelCount})");
        Save();
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
            string json = CampaignSerializer.Serialize(_progress!);

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
            if (FileAccess.FileExists(CampaignPath))
            {
                DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(CampaignPath));
            }
            Error err = DirAccess.RenameAbsolute(
                ProjectSettings.GlobalizePath(TempPath),
                ProjectSettings.GlobalizePath(CampaignPath));
            if (err != Error.Ok)
            {
                throw new System.IO.IOException(
                    $"Could not rename {TempPath} to {CampaignPath}: {err}");
            }
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
