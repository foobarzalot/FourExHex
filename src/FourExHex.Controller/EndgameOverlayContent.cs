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

    /// <summary>The game-over pause's top banner: the eyebrow word and the
    /// player whose color frames it.</summary>
    public sealed record Banner(string Text, PlayerId Color);

    /// <summary>
    /// Banner for the game-over pause, or null when nothing has ended. A
    /// declared winner reuses <see cref="For"/>'s framing (VICTORY in the
    /// winner's color; DEFEAT in the eliminated human's color when the AI
    /// or the Vikings ended a human's game); a mid-game human elimination
    /// with no winner yet is a DEFEAT in that human's color. An AI winning
    /// an endgame the humans were only spectating shows no banner.
    /// </summary>
    public static Banner? PauseBanner(
        PlayerId? winner, PlayerId? pendingDefeatScreen,
        System.Collections.Generic.IReadOnlyList<Player> players)
    {
        if (winner.HasValue)
        {
            Player? winnerPlayer = null;
            foreach (Player p in players)
            {
                if (p.Id == winner.Value) { winnerPlayer = p; break; }
            }
            bool winnerIsHuman = winnerPlayer != null && !winnerPlayer.IsAi;
            Player? defeatedHuman = winnerIsHuman
                ? null
                : DefeatedHumanFor(pendingDefeatScreen, players);
            // An AI outlasting an AI-vs-AI endgame (the humans fell earlier
            // and dismissed their own defeat screens) is nobody's victory
            // to trumpet at the spectator: no banner, the modal announces it.
            if (!winnerIsHuman && !winner.Value.IsNone && defeatedHuman == null) return null;
            Content content = For(
                winner.Value, winnerPlayer?.Name ?? "", winnerIsHuman, defeatedHuman?.Name);
            return new Banner(content.Eyebrow, defeatedHuman?.Id ?? winner.Value);
        }
        Player? loser = DefeatedHumanFor(pendingDefeatScreen, players);
        return loser == null
            ? null
            : new Banner(Strings.Get(StringKeys.EndgameDefeatEyebrow), loser.Id);
    }

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
        if (!winnerIsHuman && defeatedHumanName != null)
        {
            return new Content(
                Eyebrow: Strings.Get(StringKeys.EndgameDefeatEyebrow),
                Title: Strings.Get(StringKeys.EndgameDefeatedTitle, ("name", defeatedHumanName)),
                OfferReplay: false);
        }
        // An AI outlasting an AI-vs-AI endgame (the humans fell earlier and
        // dismissed their own defeat screens) is named, not celebrated: the
        // VICTORY eyebrow is reserved for a human winner.
        return new Content(
            Eyebrow: winnerIsHuman ? Strings.Get(StringKeys.EndgameVictoryEyebrow) : "",
            Title: Strings.Get(StringKeys.EndgameVictoryTitle, ("name", winnerName)),
            OfferReplay: true);
    }
}
