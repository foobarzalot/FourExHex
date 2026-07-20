// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;

/// <summary>
/// Single home for the step-beat cadence shared by the two step
/// machines — <see cref="AiTurnDriver.Schedule"/> (live AI) and
/// <see cref="ReplayRecorder.ScheduleNextReplayBeat"/> (replay
/// playback) — so replay stays visually equivalent to live AI and a
/// cadence change is made in one place.
/// </summary>
public static class StepPacing
{
    // Delay (milliseconds) between step beats. Each action is split
    // into a preview (highlight the acting territory) and an execute
    // (run the action, re-highlight the resulting territory) so the
    // player can see who is doing what.
    //   AiPreviewDelayMs      — pause BEFORE executing a previewed action
    //   AiActionDelayMs       — pause AFTER executing, before the next preview
    //   AiBetweenPlayersDelayMs — longer pause on player change
    public const int AiPreviewDelayMs = 350;
    public const int AiActionDelayMs = 300;
    public const int AiBetweenPlayersDelayMs = 600;
    // Demo/Instructions playback (ReplayRecorder's turn-end fast-forward):
    // the between-players redispatch delay — a tick so consecutive turns
    // don't visually blur, nothing more. Turn-end beats themselves execute
    // with no preview and zero delay in that mode.
    public const int ReplayIdleTurnSkipMs = 50;

    // Viking Raiders: how long the viking pseudo-turn stays open after the
    // wave-spawn beat, so the arrival presentation (the "ripple rise"
    // shields + rings and the longship-arrival cue — see HexMapView's
    // SeaSpawnSlowdown-scaled timings) plays out before the waiting
    // player's turn starts (auto-select, camera pan, wave banner).
    // Scheduled UNSCALED — the tweens don't stretch with the AI-speed
    // multiplier, so neither should the hold. Covers the shield rise and
    // the arrival cue in full; the ripple rings' faint tail overlaps the
    // hand-off. Keep roughly in sync with the view timings when retuning
    // either.
    public const int VikingSpawnPresentationMs = 2000;

    // Delay between a per-turn repaint and the next instant tick, so each
    // player-turn's board lingers long enough to follow (≈5 turns/sec)
    // instead of flipping past at frame rate. Still far faster than
    // Fast (~325ms/beat). Mid-turn budget yields (no repaint) use 0 —
    // an in-progress turn shouldn't be paced, only completed ones.
    public const int InstantTurnDelayMs = 200;

    /// <summary>
    /// Shared instant↔paced re-dispatch skeleton behind both step
    /// machines: run the track-transition effects (structural rebuild on
    /// instant→paced, highlight clear on paced→instant), store the new
    /// track, sync silent mode, then schedule the next beat on whichever
    /// track applies. Caller-specific concerns come in as callbacks: the
    /// instant/paced continuations, the track store, the silent-mode
    /// sync (<c>RefreshSilentMode</c> for live AI, <c>SetSilentMode</c>
    /// for replay), and the two <c>[speed]</c> transition log lines
    /// (kept as callbacks so their <c>[Conditional("DEBUG")]</c>
    /// stripping and lazy interpolation are preserved).
    /// Ordering is load-bearing: <paramref name="setTrack"/> MUST run
    /// before the pacer dispatch — under <c>SynchronousAiPacer</c> the
    /// dispatch runs the entire continuation chain inline, and nested
    /// re-dispatches read the caller's track field.
    /// </summary>
    public static void Redispatch(
        bool wasInstant, bool nowInstant, bool turnBoundary,
        IHexMapView map, IAiPacer pacer,
        Action instantTick, Action pacedStep,
        Action<bool> setTrack, Action syncSilentMode,
        Action logInstantToPaced, Action logPacedToInstant,
        int pacedBoundaryDelayMs = AiBetweenPlayersDelayMs)
    {
        if (wasInstant && !nowInstant)
        {
            logInstantToPaced();
            // Instant suppressed per-capture rebuilds; the border layer
            // is stale before the first paced render.
            map.RebuildAfterTerritoryChange();
        }
        else if (!wasInstant && nowInstant)
        {
            logPacedToInstant();
            // The instant track shows no per-action highlight, so clear
            // the acting-territory outline the paced track last drew —
            // otherwise it lingers through the fast-forward.
            map.ShowHighlight(null);
        }
        setTrack(nowInstant);
        syncSilentMode();
        // Delay belongs to whichever track we land on: instant runs at
        // its own cadence (0 mid-turn, InstantTurnDelayMs at a boundary,
        // unscaled); paced uses the multiplier-scaled step delays.
        if (nowInstant)
            pacer.ScheduleUnscaled(instantTick, turnBoundary ? InstantTurnDelayMs : 0);
        else
            pacer.Schedule(pacedStep, turnBoundary ? pacedBoundaryDelayMs : AiActionDelayMs);
    }
}
