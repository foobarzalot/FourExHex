using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Dispatches AI action requests to the concrete chooser matching
/// the current player's <see cref="Player.Kind"/>. <see cref="Main"/>
/// hands this to <see cref="GameController"/> as the single
/// <c>aiChooser</c> delegate; internally it just routes to
/// <see cref="RandomAi.ChooseNextAction"/> or
/// <see cref="HeuristicAi.ChooseNextAction"/>.
///
/// Tests can still inject their own stub chooser directly via the
/// controller constructor — this class only exists because a mixed
/// game (some Random, some Heuristic players) needs per-player
/// dispatch, and the controller's injection signature takes a
/// single delegate.
/// </summary>
public static class AiDispatcher
{
    /// <summary>
    /// Route a chooser call based on the current player's AI kind.
    /// A human player (<see cref="AiKind.Human"/>) returns null
    /// immediately — the step machine should never call this for a
    /// human anyway, but the early-return is defensive.
    /// </summary>
    public static AiAction? ChooseForCurrentPlayer(
        GameState state,
        Color forPlayer,
        HashSet<HexCoord> visitedCapitals,
        Random rng)
    {
        // The controller guarantees forPlayer == CurrentPlayer.Color
        // at the time of the call, so we can look up kind off the
        // current player.
        Player current = state.Turns.CurrentPlayer;
        return current.Kind switch
        {
            AiKind.Random => RandomAi.ChooseNextAction(state, forPlayer, visitedCapitals, rng),
            AiKind.Heuristic => HeuristicAi.ChooseNextAction(state, forPlayer, visitedCapitals, rng),
            _ => null,
        };
    }
}
