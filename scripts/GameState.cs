using System.Collections.Generic;

/// <summary>
/// The game world. Single source of truth for the current state of the
/// map, players, turn order, and economy. Owned by the controller layer
/// (currently Main); views read from it. Pure data — no Godot Nodes.
///
/// Not included here: selection, pending-action mode, or undo history —
/// those are session state (UI-scoped) and live in SessionState.
/// </summary>
public class GameState
{
    public HexGrid Grid { get; }
    public IReadOnlyList<Player> Players { get; }
    public TurnState Turns { get; }
    public Treasury Treasury { get; }

    /// <summary>
    /// Current territory partition. Reassigned after any capture (via
    /// TerritoryFinder + CapitalReconciler) and after any undo/redo.
    /// The setter is intentionally public so the controller can swap it
    /// in atomically without copying individual fields.
    /// </summary>
    public IReadOnlyList<Territory> Territories { get; set; }

    public GameState(
        HexGrid grid,
        IReadOnlyList<Territory> territories,
        IReadOnlyList<Player> players,
        TurnState turns,
        Treasury treasury)
    {
        Grid = grid;
        Territories = territories;
        Players = players;
        Turns = turns;
        Treasury = treasury;
    }
}
