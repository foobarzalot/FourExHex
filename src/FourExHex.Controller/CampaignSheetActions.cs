// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;

/// <summary>An action button on the campaign level confirm sheet.</summary>
public enum CampaignSheetAction
{
    /// <summary>Fresh launch of a level with nothing stored.</summary>
    Play,
    /// <summary>Resume the stored unfinished attempt.</summary>
    Continue,
    /// <summary>Play back the stored finished attempt.</summary>
    WatchReplay,
    /// <summary>Discard the stored attempt and launch fresh.</summary>
    Restart,
}

/// <summary>
/// The campaign confirm sheet's action set per stored-attempt state. The
/// first action is the primary (Enter / default); the sheet supplies Cancel
/// itself. Pure so the table is unit-tested; the menu maps each action to
/// a label and a handler.
/// </summary>
public static class CampaignSheetActions
{
    public static IReadOnlyList<CampaignSheetAction> For(CampaignAttemptKind kind, bool canReplay) =>
        kind switch
        {
            CampaignAttemptKind.Unfinished =>
                new[] { CampaignSheetAction.Continue, CampaignSheetAction.Restart },
            CampaignAttemptKind.Finished when canReplay =>
                new[] { CampaignSheetAction.WatchReplay, CampaignSheetAction.Restart },
            CampaignAttemptKind.Finished =>
                new[] { CampaignSheetAction.Restart },
            _ => new[] { CampaignSheetAction.Play },
        };

    /// <summary>Restart prompts first only when it would discard a game in
    /// progress; replacing a finished attempt (and its replay) is silent.</summary>
    public static bool RestartNeedsConfirm(CampaignAttemptKind kind) =>
        kind == CampaignAttemptKind.Unfinished;
}

/// <summary>
/// Level stepping on the campaign confirm sheet (swipe / arrow keys). The
/// ladder is a progression, so it clamps at both ends instead of wrapping.
/// </summary>
public static class CampaignSheetPaging
{
    /// <summary>The level one step from <paramref name="level"/>, or null
    /// when that step would leave the ladder.</summary>
    public static int? Neighbor(int level, bool forward)
    {
        int target = level + (forward ? 1 : -1);
        return target >= 0 && target < CampaignProgress.LevelCount ? target : null;
    }
}
