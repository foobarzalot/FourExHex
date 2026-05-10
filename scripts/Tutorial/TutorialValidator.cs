/// <summary>
/// Decides whether a player input matches the next expected
/// scripted beat. Static — no per-instance state. Phase 3c ships
/// only <see cref="MatchesEndTurn"/>; Phase 4 adds MatchesBuyPeasant /
/// MatchesBuildTower, Phase 5 adds MatchesMove.
/// </summary>
public static class TutorialValidator
{
    /// <summary>
    /// EndTurnBeat carries no per-beat data (no At, no Src/Dst), so
    /// any End-Turn click matches if the next beat is an EndTurnBeat.
    /// The wrapper checks the kind first; this method is the explicit
    /// "yes, this is a match" symbol that mirrors the spec's
    /// MatchesMove / MatchesBuyPeasant / MatchesBuildTower triplet.
    /// </summary>
    public static bool MatchesEndTurn(EndTurnBeat beat) => true;

    /// <summary>
    /// Build the soft-reject message shown via
    /// <c>IHudView.ShowTutorialMessage</c> when the player attempts
    /// an action that doesn't match the next expected beat.
    /// </summary>
    public static string ReasonMismatch(Beat? expected, string attempted)
    {
        if (expected == null)
        {
            return "Tutorial complete — no further actions expected.";
        }
        return $"Expected {expected.Kind} (turn {expected.Turn}, actor {expected.Actor}); got {attempted}.";
    }
}
