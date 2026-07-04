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
    /// show (outside Viking Raiders, or no wave left to announce). On the
    /// round a wave just spawned (raiders offshore, landing next turn) the
    /// text is the spawn message ("Wave X/Y" / "Final wave"); otherwise the
    /// countdown ("Wave X/Y arriving in N turns" / "Final wave arriving in
    /// 1 turn"), with turn/turns singular-plural by N.
    /// </summary>
    public static string? For(GameState state)
    {
        if (state.Mode != GameMode.VikingRaiders) return null;
        VikingState vikings = state.Vikings;
        int total = VikingRaidersRules.TotalWaves;

        // Spawn round: a wave is sitting offshore (it lands next turn) —
        // announce THAT rather than counting toward the following wave.
        if (vikings.LastSpawnRound == state.Turns.TurnNumber && vikings.AtSea.Count > 0)
        {
            return vikings.NextWaveIndex == total
                ? "Final wave"
                : $"Wave {vikings.NextWaveIndex}/{total}";
        }

        int? rounds = VikingRaidersRules.RoundsUntilWaveDue(state);
        if (rounds == null) return null;
        string turns = rounds == 1 ? "1 turn" : $"{rounds} turns";
        return vikings.NextWaveIndex == total - 1
            ? $"Final wave arriving in {turns}"
            : $"Wave {vikings.NextWaveIndex + 1}/{total} arriving in {turns}";
    }
}
