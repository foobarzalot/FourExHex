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
    private static readonly IReadOnlySet<HexCoord> EmptyCoords =
        new HashSet<HexCoord>();

    public HexGrid Grid { get; }
    public IReadOnlyList<Player> Players { get; }
    public TurnState Turns { get; }
    public Treasury Treasury { get; }

    /// <summary>
    /// Coords inside the map's rectangular bounds that are water — unowned,
    /// uncapturable, blocking placement. Treated by every rule predicate
    /// exactly like off-map locations (because they are not in <see cref="Grid"/>);
    /// only the renderer reads this set, to draw black hexes for them.
    /// </summary>
    public IReadOnlySet<HexCoord> WaterCoords { get; }

    /// <summary>
    /// Current territory partition. Reassigned after any capture (via
    /// TerritoryFinder + CapitalReconciler) and after any undo/redo.
    /// The setter is intentionally public so the controller can swap it
    /// in atomically without copying individual fields.
    /// </summary>
    public IReadOnlyList<Territory> Territories { get; set; }

    /// <summary>
    /// Difficulty of the player owning <paramref name="id"/>, resolved by SLOT
    /// (<see cref="PlayerId.Index"/>) rather than by indexing <see cref="Players"/>
    /// at that slot. With a 2–6 player game the roster is compacted (e.g. slots
    /// 0, 2, 5), so a player's slot index is NOT its position in the list (issue
    /// #70). Returns <see cref="Difficulty.Soldier"/> for neutral land or an id
    /// not in the roster — the baseline AIs always play at.
    /// </summary>
    public Difficulty DifficultyOf(PlayerId id)
    {
        if (!id.IsNone)
        {
            foreach (Player p in Players)
            {
                if (p.Id == id) return p.Difficulty;
            }
        }
        return Difficulty.Soldier;
    }

    public GameState(
        HexGrid grid,
        IReadOnlyList<Territory> territories,
        IReadOnlyList<Player> players,
        TurnState turns,
        Treasury treasury,
        IReadOnlySet<HexCoord>? waterCoords = null)
    {
        Grid = grid;
        Territories = territories;
        Players = players;
        Turns = turns;
        Treasury = treasury;
        WaterCoords = waterCoords ?? EmptyCoords;
    }
}
