// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
/// <summary>
/// Copy for the Viking Raiders wave banner shown at each human turn start.
/// Godot-free so the wording rules are unit-testable; the controller hands
/// the result to <c>IHudView.ShowTransientBanner</c>. Schedule-resistant:
/// everything derives from <see cref="VikingRaidersRules.RoundsUntilWaveDue"/>
/// (itself defined via <see cref="VikingRaidersRules.WaveDue"/>),
/// <see cref="VikingRaidersRules.TotalWaves"/>, and
/// <see cref="VikingState.NextWaveIndex"/> — wave count or spacing changes
/// need no edits here.
/// </summary>
public static class VikingWaveBannerContent
{
    /// <summary>
    /// The banner text for the current state, or null when no banner should
    /// show (outside Viking Raiders, or no wave left to announce). While
    /// the wave sits offshore — it spawned at the previous round's neutral
    /// seat and lands at this round's end — the text is the arrival message
    /// ("Wave X/Y" / "Final wave"); otherwise the countdown ("Wave X/Y
    /// arriving in N turns" / "Final wave arriving in 1 turn"), where N
    /// counts to the turn the raiders become visible offshore (one round
    /// past the spawn's due round, since the wave spawns at the due round's
    /// neutral seat), with turn/turns singular-plural by N.
    /// </summary>
    public static string? For(GameState state)
    {
        if (state.Mode != GameMode.VikingRaiders) return null;
        VikingState vikings = state.Vikings;
        int total = VikingRaidersRules.TotalWaves;

        // Raiders offshore: they land at this round's end — announce THAT
        // rather than counting toward the following wave. NextWaveIndex has
        // already advanced past the spawned wave, so it IS the 1-based
        // display index.
        if (vikings.AtSea.Count > 0)
        {
            return vikings.NextWaveIndex == total
                ? Strings.Get(StringKeys.VikingWaveFinalSpawned)
                : Strings.Get(StringKeys.VikingWaveSpawned,
                    ("index", vikings.NextWaveIndex.ToString()),
                    ("total", total.ToString()));
        }

        int? rounds = VikingRaidersRules.RoundsUntilWaveDue(state);
        if (rounds == null) return null;
        // The wave spawns at the due round's neutral seat and shows offshore
        // the round after — count to when the player SEES it, so the due
        // round itself reads "arriving in 1 turn", never "in 0 turns".
        int visibleIn = rounds.Value + 1;
        string turns = visibleIn == 1
            ? Strings.Get(StringKeys.VikingTurnsOne)
            : Strings.Get(StringKeys.VikingTurnsMany, ("n", visibleIn.ToString()));
        return vikings.NextWaveIndex == total - 1
            ? Strings.Get(StringKeys.VikingWaveFinalIncoming, ("turns", turns))
            : Strings.Get(StringKeys.VikingWaveIncoming,
                ("index", (vikings.NextWaveIndex + 1).ToString()),
                ("total", total.ToString()),
                ("turns", turns));
    }
}
