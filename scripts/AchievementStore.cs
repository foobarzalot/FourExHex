// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using Godot;

/// <summary>
/// Process-wide achievement record, persisted to
/// <c>user://achievements.json</c> — a sidecar independent of game saves,
/// so deleting saves never costs the player an earned achievement. Mirrors
/// <see cref="CampaignStore"/>: lazy load on first access, atomic
/// tmp+rename writes via <see cref="AtomicUserFile"/>, fall back to a fresh
/// record on a corrupt or missing file (the next unlock overwrites it
/// cleanly).
///
/// All serialization logic lives in the Godot-free model
/// (<see cref="AchievementSerializer"/> / <see cref="AchievementRecord"/>)
/// where it is unit-tested; this class is thin file I/O and is
/// test-excluded for the same reason as <see cref="SaveStore"/>.
///
/// Writes happen immediately on every change — never "on app exit" — so a
/// crash or force-quit can't lose an unlock.
/// </summary>
public static class AchievementStore
{
    private const string AchievementPath = "user://achievements.json";

    private static AchievementRecord? _record;

    /// <summary>The loaded (or fresh) record. Mutate only via
    /// <see cref="Unlock"/> / <see cref="SetProgress"/> so changes hit disk.</summary>
    public static AchievementRecord Record
    {
        get
        {
            EnsureLoaded();
            return _record!;
        }
    }

    /// <summary>Mark an achievement earned and persist if anything changed.</summary>
    public static void Unlock(string id)
    {
        EnsureLoaded();
        if (!_record!.Unlock(id)) return;
        Log.Info(Log.LogCategory.Achieve,
            $"[store] unlocked {id} (#{_record.UnlockedInOrder.Count})");
        Save();
    }

    /// <summary>Raise recorded progress toward an achievement and persist
    /// if anything changed.</summary>
    public static void SetProgress(string id, int current)
    {
        EnsureLoaded();
        if (!_record!.SetProgress(id, current)) return;
        Log.Debug(Log.LogCategory.Achieve, $"[store] progress {id} = {current}");
        Save();
    }

    /// <summary>Clear every unlock and all progress, then persist the empty
    /// record. Debug tooling (the cheat menu) so achievement-award and toast
    /// behaviour can be re-tested without a fresh install; writing an empty
    /// record rather than deleting the file keeps the in-memory and on-disk
    /// state in step.</summary>
    public static void Reset()
    {
        _record = new AchievementRecord();
        Log.Info(Log.LogCategory.Achieve, "[store] reset — record cleared");
        Save();
    }

    private static void EnsureLoaded()
    {
        if (_record != null) return;
        // Assign a fresh record up front so a parse failure doesn't retry
        // on every access — we fall back and stay there until the next
        // unlock overwrites the file cleanly.
        _record = new AchievementRecord();
        try
        {
            if (!FileAccess.FileExists(AchievementPath))
            {
                Log.Debug(Log.LogCategory.Achieve,
                    "[store] no achievements.json — starting fresh");
                return;
            }
            using FileAccess f = FileAccess.Open(AchievementPath, FileAccess.ModeFlags.Read);
            if (f == null) return;
            _record = AchievementSerializer.Deserialize(f.GetAsText());
            Log.Info(Log.LogCategory.Achieve,
                $"[store] loaded — {_record.UnlockedInOrder.Count} unlocked");
        }
        catch (System.Exception ex)
        {
            GD.PushWarning($"Failed to load achievements: {ex.Message}");
        }
    }

    private static void Save()
    {
        try
        {
            AtomicUserFile.Write(AchievementPath, AchievementSerializer.Serialize(_record!));
            Log.Debug(Log.LogCategory.Achieve, "[store] saved achievements.json");
        }
        catch (System.Exception ex)
        {
            // In-memory state is already updated, so the session still
            // shows the achievement — we just won't persist it.
            GD.PushWarning($"Failed to save achievements: {ex.Message}");
        }
    }
}
