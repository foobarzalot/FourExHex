// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;

/// <summary>
/// Persisted replay payload: the game-start snapshot plus the ordered
/// list of every state-mutating beat. Embedded inside a save file's
/// <c>SaveData.Replay</c> by <see cref="SaveSerializer"/> so loading a
/// save preserves the history-to-here for replay. Hydrated into
/// <see cref="GameController"/> via its constructor's
/// <c>loadedReplay</c> parameter; the controller continues to append
/// to the same beat list across the resumed game.
/// </summary>
public sealed class Replay
{
    /// <summary>
    /// Deep-copy of the game state at the moment recording began. For
    /// fresh games this is captured at <see cref="GameController.StartGame"/>
    /// after starting gold is seeded. <c>BeginReplay</c> applies this
    /// snapshot back to the live state before stepping through
    /// <see cref="Beats"/>.
    /// </summary>
    public GameStateSnapshot InitialSnapshot { get; }

    /// <summary>
    /// <see cref="TurnState.TurnNumber"/> at recording start. Almost
    /// always 1 for fresh games; carried explicitly because
    /// <see cref="GameStateSnapshot"/> does not capture turn metadata.
    /// </summary>
    public int InitialTurnNumber { get; }

    /// <summary>
    /// <see cref="TurnState.CurrentPlayerIndex"/> at recording start.
    /// Almost always 0; carried explicitly for the same reason.
    /// </summary>
    public int InitialCurrentPlayerIndex { get; }

    /// <summary>
    /// Recorded beats in execution order. Each beat's
    /// <see cref="ReplayBeat.Index"/> equals its zero-based position
    /// in this list.
    /// </summary>
    public IReadOnlyList<ReplayBeat> Beats { get; }

    public Replay(
        GameStateSnapshot initialSnapshot,
        int initialTurnNumber,
        int initialCurrentPlayerIndex,
        IReadOnlyList<ReplayBeat> beats)
    {
        InitialSnapshot = initialSnapshot;
        InitialTurnNumber = initialTurnNumber;
        InitialCurrentPlayerIndex = initialCurrentPlayerIndex;
        Beats = beats;
    }
}
