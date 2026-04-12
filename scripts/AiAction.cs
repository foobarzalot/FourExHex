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
/// Buy a peasant from the territory whose capital is
/// <see cref="Capital"/> and place it at <see cref="Destination"/>.
/// May be a direct capture (enemy tile) or a tree chop (own tile).
/// </summary>
public sealed record AiBuyUnitAction(HexCoord Capital, HexCoord Destination) : AiAction;

/// <summary>
/// Build a tower from the territory whose capital is
/// <see cref="Capital"/> on the empty own-territory tile
/// <see cref="Destination"/>.
/// </summary>
public sealed record AiBuildTowerAction(HexCoord Capital, HexCoord Destination) : AiAction;
