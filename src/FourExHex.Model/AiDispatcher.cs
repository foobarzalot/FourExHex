// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;

/// <summary>
/// Dispatches AI action requests to <see cref="ComputerAi.ChooseNextAction"/>
/// for any computer-controlled player. <see cref="Main"/> hands this to
/// <see cref="GameController"/> as the single <c>aiChooser</c> delegate.
///
/// Tests can still inject their own stub chooser directly via the
/// controller constructor — this class only exists so the controller's
/// injection signature can take a single delegate that knows to return
/// null for a human slot.
/// </summary>
public static class AiDispatcher
{
    /// <summary>
    /// Route a chooser call based on the current player's kind.
    /// A human player (<see cref="PlayerKind.Human"/>) returns null
    /// immediately — the step machine should never call this for a
    /// human anyway, but the early-return is defensive.
    /// </summary>
    public static AiAction? ChooseForCurrentPlayer(
        GameState state,
        PlayerId forPlayer,
        HashSet<HexCoord> visitedCapitals,
        HashSet<HexCoord> repositionedUnits,
        Random rng)
    {
        // The controller guarantees forPlayer == CurrentPlayer.Id
        // at the time of the call, so we can look up kind off the
        // current player.
        Player current = state.Turns.CurrentPlayer;
        return current.Kind == PlayerKind.Computer
            ? ComputerAi.ChooseNextAction(state, forPlayer, visitedCapitals, repositionedUnits, rng)
            : null;
    }
}
