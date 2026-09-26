// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
/// <summary>
/// One-shot handoff from <see cref="MainMenuScene"/> to <see cref="Main"/>
/// across <c>ChangeSceneToFile</c>. The menu deserializes a save into
/// <see cref="Pending"/> before switching scenes; <see cref="Main"/> reads
/// it in <c>_Ready</c> and clears it immediately so a subsequent
/// menu→game transition starts a fresh game.
///
/// Mirrors the static-state idiom of <see cref="GameSettings"/>.
/// </summary>
public static class LoadRequest
{
    public static LoadedSave? Pending { get; set; }

    /// <summary>
    /// Set alongside <see cref="Pending"/> to open the loaded save in replay
    /// playback instead of resuming it (the campaign sheet's Watch Replay).
    /// <c>Main</c> reads and clears it with <see cref="Pending"/>.
    /// </summary>
    public static bool WatchReplay { get; set; }
}
