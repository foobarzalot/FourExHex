// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
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

    /// <summary>
    /// Number of seats in one full round: every colored player plus the
    /// trailing neutral seat (<see cref="Player.Neutral"/>).
    /// </summary>
    public int SeatCount => Players.Count + 1;

    /// <summary>
    /// True while the rotation sits on the neutral seat — the round's final
    /// seat, where tree growth, upkeep, and viking-raider actions run.
    /// </summary>
    public bool IsNeutralSeat => CurrentPlayerIndex == Players.Count;

    public Player CurrentPlayer =>
        IsNeutralSeat ? Player.Neutral : Players[CurrentPlayerIndex];

    public TurnState(IReadOnlyList<Player> players)
        : this(players, currentPlayerIndex: 0, turnNumber: 1)
    {
    }

    /// <summary>
    /// Restore a turn state from a saved value. Only used by the load path —
    /// fresh games construct via the no-arg constructor which starts on
    /// player 0, turn 1.
    /// </summary>
    public TurnState(IReadOnlyList<Player> players, int currentPlayerIndex, int turnNumber)
    {
        Players = players;
        CurrentPlayerIndex = currentPlayerIndex;
        TurnNumber = turnNumber;
    }

    /// <summary>
    /// Advance to the next seat. The rotation runs every colored player and
    /// then the neutral seat; wrapping from the neutral seat back to the
    /// first player increments <see cref="TurnNumber"/>, so neutral's turn
    /// is the final seat of its round.
    /// </summary>
    public void EndTurn()
    {
        CurrentPlayerIndex++;
        if (CurrentPlayerIndex >= SeatCount)
        {
            CurrentPlayerIndex = 0;
            TurnNumber++;
        }
    }

    /// <summary>
    /// Force the turn counter and current-player index back to specific
    /// values. Used by <c>GameController.BeginReplay</c> to rewind to the
    /// game's initial state without replacing the <c>TurnState</c>
    /// reference (which any view caching the original would miss). Not
    /// part of normal gameplay — every other code path mutates only via
    /// <see cref="EndTurn"/>.
    /// </summary>
    public void Reset(int currentPlayerIndex, int turnNumber)
    {
        CurrentPlayerIndex = currentPlayerIndex;
        TurnNumber = turnNumber;
    }
}
