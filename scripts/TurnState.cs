using System.Collections.Generic;

/// <summary>
/// Tracks whose turn it is and which turn number we're on. Pure logic, no
/// Godot dependencies — the HUD binds to this state and reacts to changes.
/// </summary>
public class TurnState
{
    public IReadOnlyList<Player> Players { get; }
    public int CurrentPlayerIndex { get; private set; }
    public int TurnNumber { get; private set; }

    public Player CurrentPlayer => Players[CurrentPlayerIndex];

    public TurnState(IReadOnlyList<Player> players)
    {
        Players = players;
        CurrentPlayerIndex = 0;
        TurnNumber = 1;
    }

    /// <summary>
    /// Advance to the next player. Wrapping back to the first player
    /// increments <see cref="TurnNumber"/>.
    /// </summary>
    public void EndTurn()
    {
        CurrentPlayerIndex++;
        if (CurrentPlayerIndex >= Players.Count)
        {
            CurrentPlayerIndex = 0;
            TurnNumber++;
        }
    }
}
