/// <summary>
/// A single action produced by an AI decision function. The controller
/// executes these via dedicated <c>ExecuteAi*</c> helpers that bypass
/// the pending-action/selection machinery the human flow uses.
/// </summary>
public abstract record AiAction;

/// <summary>
/// Move an existing unit from <see cref="Source"/> to
/// <see cref="Destination"/>. May be a capture (enemy tile) or a
/// tree chop (own-territory tree tile). The AI never emits a pure
/// reposition or a combine — those don't advance the AI's goals.
/// </summary>
public sealed record AiMoveAction(HexCoord Source, HexCoord Destination) : AiAction;

/// <summary>
/// Buy a unit of <see cref="Level"/> from the territory whose capital
/// is <see cref="Capital"/> and place it at <see cref="Destination"/>.
/// May be a direct capture (enemy tile) or a tree chop (own tile).
/// </summary>
public sealed record AiBuyUnitAction(
    HexCoord Capital,
    HexCoord Destination,
    UnitLevel Level) : AiAction;

/// <summary>
/// Build a tower from the territory whose capital is
/// <see cref="Capital"/> on the empty own-territory tile
/// <see cref="Destination"/>.
/// </summary>
public sealed record AiBuildTowerAction(HexCoord Capital, HexCoord Destination) : AiAction;

/// <summary>
/// Replay-script-only: a long-press rally targeting <see cref="Target"/>.
/// Heuristic/Random AIs never produce this; it exists so the
/// <c>ReplayDrivenAi</c> chooser can drive recorded human rallies on
/// non-player-0 turns during tutorial Preview through the same
/// <c>StepAiExecute</c> dispatch as ordinary AI moves.
/// </summary>
public sealed record AiLongPressRallyAction(HexCoord Target) : AiAction;

/// <summary>
/// Replay-script-only: claim victory at <see cref="ThresholdPercent"/>.
/// Game-ending. Same rationale as <see cref="AiLongPressRallyAction"/>.
/// </summary>
public sealed record AiClaimVictoryAction(int ThresholdPercent) : AiAction;

/// <summary>
/// Replay-script-only: dismiss the claim-victory prompt at
/// <see cref="ThresholdPercent"/> without claiming.
/// </summary>
public sealed record AiDismissClaimAction(int ThresholdPercent) : AiAction;

/// <summary>
/// Replay-script-only: dismiss a pending defeat overlay.
/// </summary>
public sealed record AiDismissDefeatAction : AiAction;
