/// <summary>
/// Discriminated-union root for tutorial beats. Concrete kinds are
/// sealed records below. JSON (de)serialization in SaveSerializer
/// uses the <see cref="Kind"/> field as the discriminator (mirrors
/// the OccupantDto pattern — hand-written switch, no reflection).
///
/// Phase 3b shipped <see cref="EndTurnBeat"/>. Phase 4 adds
/// <see cref="BuyPeasantBeat"/>. Phase 5 adds MoveBeat, etc. Each new
/// kind appears here, in <see cref="BeatKind"/>, in SaveSerializer's
/// serialize + deserialize switches, and in any consumer that handles
/// it explicitly.
/// </summary>
public abstract record Beat
{
    public int Index { get; init; }            // contiguous from 0
    public int Turn { get; init; }             // 1-based, matches TurnState.TurnNumber
    public int Actor { get; init; }            // index into Players
    public string? Narration { get; init; }    // optional caption shown in timeline
    public abstract BeatKind Kind { get; }
}

public enum BeatKind
{
    EndTurn,
    BuyPeasant,
    // 5+ adds: Move, BuildTower, Prompt, Highlight, CameraFocus
}

public sealed record EndTurnBeat : Beat
{
    public override BeatKind Kind => BeatKind.EndTurn;
}

/// <summary>
/// Player buys a peasant and places it at <see cref="At"/>. In the
/// real game this is a two-event sequence (Buy Peasant button → enter
/// BuyingPeasant mode → tile click → ExecuteBuyAndPlace); the
/// tutorial gating layer mirrors that — see
/// <see cref="TutorialPlayer.TryArmBuyPeasant"/> /
/// <see cref="TutorialPlayer.TryAdvanceForBuyPeasantTile"/>.
/// </summary>
public sealed record BuyPeasantBeat : Beat
{
    public override BeatKind Kind => BeatKind.BuyPeasant;
    public required HexCoord At { get; init; }
}
