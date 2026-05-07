using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// AI used by tutorial scenarios. Currently fully passive — every
/// query returns null, which the controller's step machine reads as
/// "this player is done; advance to the next player". Will grow
/// scripted behavior as tutorial steps demand it.
/// </summary>
public static class TutorialAi
{
    public static AiAction? ChooseNextAction(
        GameState state,
        Color forPlayer,
        HashSet<HexCoord> visitedCapitals,
        Random rng)
    {
        return null;
    }
}
