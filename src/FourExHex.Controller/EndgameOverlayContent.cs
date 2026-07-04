/// <summary>
/// Presentation content for the game-over overlay, decided from the declared
/// winner. Godot-free so the Victory-vs-Defeat framing is unit-testable;
/// <c>HudView</c> consumes it. A viking total wipeout (Viking Raiders mode
/// declares <see cref="PlayerId.None"/> the winner when the raiders destroy
/// every capital) is a DEFEAT for the players — no Replay offer — while a
/// roster player's win keeps the ordinary VICTORY presentation.
/// </summary>
public static class EndgameOverlayContent
{
    public sealed record Content(string Eyebrow, string Title, bool OfferReplay);

    public static Content For(PlayerId winner, string winnerName)
        => winner.IsNone
            ? new Content(
                Eyebrow: "DEFEAT",
                Title: "The Vikings have conquered the island!",
                OfferReplay: false)
            : new Content(
                Eyebrow: "VICTORY",
                Title: $"{winnerName} wins!",
                OfferReplay: true);
}
