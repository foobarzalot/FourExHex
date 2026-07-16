// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
/// <summary>
/// A single action produced by an AI decision function. The controller
/// executes these via dedicated <c>ExecuteAi*</c> helpers that bypass
/// the pending-action/selection machinery the human flow uses.
/// </summary>
public abstract record AiAction;

/// <summary>
/// Move an existing unit from <see cref="Source"/> to
/// <see cref="Destination"/>. In phase 1: a capture (enemy tile),
/// tree chop, or grave clear. In phase 2a: a combine that unlocks
/// a new movement-consuming target. In phase 4b: a defensive
/// reposition to an empty border tile.
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
/// Buy a unit of <see cref="BuyLevel"/> from the territory whose capital
/// is <see cref="Capital"/> and combine it onto the existing friendly unit
/// at <see cref="CombineTarget"/>. Only emitted when the combined unit
/// unlocks a movement-consuming target (capture/chop/grave) that neither
/// the bought unit nor the target unit could reach at their original levels
/// — phase-2b of the stepwise-greedy AI.
/// </summary>
public sealed record AiBuyCombineAction(
    HexCoord Capital,
    HexCoord CombineTarget,
    UnitLevel BuyLevel) : AiAction;

/// <summary>
/// Replay-script-only: a long-press rally targeting <see cref="Target"/>.
/// <see cref="ComputerAi"/> never produces this; it exists so the
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

/// <summary>
/// Viking Raiders: the sea viking at <see cref="Sea"/> disembarks onto the
/// adjacent land tile <see cref="Land"/> — a capture when the tile is
/// player-owned, a reposition-like landing when it is already neutral.
/// Produced only by <see cref="VikingAi.ChooseNext"/> during the viking
/// pseudo-turn; never simulated by <see cref="AiSimulator"/>.
/// </summary>
public sealed record VikingDisembarkAction(HexCoord Sea, HexCoord Land) : AiAction;

/// <summary>
/// Viking Raiders: the sea viking at <see cref="Sea"/> has no landing site
/// (every adjacent land tile too defended or blocked) and dies at sea.
/// </summary>
public sealed record VikingPerishAtSeaAction(HexCoord Sea) : AiAction;

/// <summary>
/// Viking Raiders: spawn wave <see cref="WaveIndex"/> as <see cref="Spawns"/>.
/// The placements are drawn (with the turn RNG) at CHOOSE time and carried in
/// the action, so preview/execute and replay re-execution consume no further
/// randomness. Always the LAST action of a viking turn — a fresh wave never
/// acts on its spawn round, giving players exactly one round of warning.
/// </summary>
public sealed record VikingSpawnWaveAction(
    int WaveIndex,
    System.Collections.Generic.IReadOnlyList<SeaViking> Spawns) : AiAction;
