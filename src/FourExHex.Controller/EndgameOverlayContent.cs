// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
/// <summary>
/// Presentation content for the game-over overlay, decided from the declared
/// winner. Godot-free so the Victory-vs-Defeat framing is unit-testable;
/// <c>HudView</c> consumes it. DEFEAT framing (no Replay offer) applies to
/// exactly two endings: a viking total wipeout (Viking Raiders mode declares
/// <see cref="PlayerId.None"/> the winner when the raiders destroy every
/// capital), and an AI winning in the same beat a human's elimination ended
/// the game — voiced like the mid-game elimination overlay
/// ("&lt;Loser&gt; defeated"). Every other winner — human, or an AI that
/// outlasted an AI-vs-AI endgame the eliminated humans were spectating —
/// gets the ordinary VICTORY announcement with the Replay offer.
/// </summary>
public static class EndgameOverlayContent
{
    public sealed record Content(string Eyebrow, string Title, bool OfferReplay);

    /// <summary>
    /// The human whose name (and color) frames the AI-winner DEFEAT
    /// overlay: the human whose elimination ended the game, read from
    /// <paramref name="pendingDefeatScreen"/> (it survives the winner
    /// declaration because the HUD only suppresses the mid-game defeat
    /// overlay, never clears the field). Null when the game ended without
    /// a fresh human elimination — the humans fell earlier, dismissed
    /// their own defeat screens, and the AI winner is announced instead.
    /// A pending id that isn't a human in the roster is ignored —
    /// defensive, the field is only ever set for humans.
    /// </summary>
    public static Player? DefeatedHumanFor(
        PlayerId? pendingDefeatScreen, System.Collections.Generic.IReadOnlyList<Player> players)
    {
        if (pendingDefeatScreen == null) return null;
        foreach (Player p in players)
        {
            if (!p.IsAi && p.Id == pendingDefeatScreen.Value) return p;
        }
        return null;
    }

    public static Content For(
        PlayerId winner, string winnerName, bool winnerIsHuman,
        string? defeatedHumanName)
    {
        if (winner.IsNone)
        {
            return new Content(
                Eyebrow: Strings.Get(StringKeys.EndgameDefeatEyebrow),
                Title: Strings.Get(StringKeys.EndgameVikingConquestTitle),
                OfferReplay: false);
        }
        return !winnerIsHuman && defeatedHumanName != null
            ? new Content(
                Eyebrow: Strings.Get(StringKeys.EndgameDefeatEyebrow),
                Title: Strings.Get(StringKeys.EndgameDefeatedTitle, ("name", defeatedHumanName)),
                OfferReplay: false)
            : new Content(
                Eyebrow: Strings.Get(StringKeys.EndgameVictoryEyebrow),
                Title: Strings.Get(StringKeys.EndgameVictoryTitle, ("name", winnerName)),
                OfferReplay: true);
    }
}
