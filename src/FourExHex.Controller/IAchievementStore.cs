// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot

/// <summary>
/// The authoritative local achievement record, seen from the controller.
/// The Godot side implements this over <c>user://achievements.json</c>;
/// Controller and Model never name a platform.
///
/// This is the first-party seam. Adding Game Center / Play Games later
/// means the Godot implementation also mirrors these two writes outward —
/// <see cref="ReportProgress"/> carries <c>(current, target)</c> because
/// that is what both platforms need (Game Center a percentage, Play Games
/// incremental steps), and <see cref="Unlock"/> is append-only because
/// neither platform can revoke. Nothing above this interface changes.
/// </summary>
public interface IAchievementStore
{
    /// <summary>True iff the achievement has already been earned.</summary>
    bool IsUnlocked(string id);

    /// <summary>Best progress recorded so far, or 0.</summary>
    int ProgressFor(string id);

    /// <summary>Record progress toward an achievement and persist it.
    /// Called only while <paramref name="current"/> is rising and the
    /// achievement is still locked.</summary>
    void ReportProgress(string id, int current, int target);

    /// <summary>Mark the achievement earned and persist it. Idempotent —
    /// an unlock is never revoked.</summary>
    void Unlock(string id);
}
