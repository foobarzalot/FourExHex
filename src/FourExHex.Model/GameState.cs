// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
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
    /// Selectable runtime rules variant. Defaults to
    /// <see cref="GameMode.Freeform"/> so every existing call site (which
    /// omits the new ctor arg) is unchanged.
    /// </summary>
    public GameMode Mode { get; }

    /// <summary>
    /// When true, the two selection points that were historically
    /// resolved to the lex-min coord — capital placement
    // Backing store for WaterCoords. A HashSet (not the incoming set's own
    // type) so the set can grow at runtime — Rising Tides submerges shore
    // tiles mid-game via AddWater. Exposed only as IReadOnlySet so readers
    // (renderer, snapshot, serializer) can't mutate it out of band.
    private readonly HashSet<HexCoord> _water;

    /// <summary>
    /// Coords inside the map's rectangular bounds that are water — unowned,
    /// uncapturable, blocking placement. Treated by every rule predicate
    /// exactly like off-map locations (because they are not in <see cref="Grid"/>);
    /// only the renderer reads this set, to draw black hexes for them. Grows
    /// during a Rising Tides game as shore tiles submerge (see <see cref="AddWater"/>).
    /// </summary>
    public IReadOnlySet<HexCoord> WaterCoords => _water;

    /// <summary>
    /// Mark <paramref name="coord"/> as water. Used by Rising Tides when a
    /// shore tile submerges, paired with <c>Grid.Remove(coord)</c> — the same
    /// remove-tile-then-add-water shape the map editor uses
    /// (<see cref="MapEditPaint.PaintWater"/>). Idempotent.
    /// </summary>
    public void AddWater(HexCoord coord) => _water.Add(coord);

    /// <summary>
    /// Un-mark <paramref name="coord"/> as water (it is land again). Used by the
    /// replay rewind: restoring the initial board re-adds every tile that
    /// submerged during the recorded game, so those coords must leave the water
    /// set to keep the land/water partition consistent. Idempotent.
    /// </summary>
    public void RemoveWater(HexCoord coord) => _water.Remove(coord);

    /// <summary>
    /// Current territory partition. Reassigned after any capture (via
    /// TerritoryFinder + CapitalReconciler) and after any undo/redo.
    /// The setter is intentionally public so the controller can swap it
    /// in atomically without copying individual fields.
    /// </summary>
    public IReadOnlyList<Territory> Territories { get; set; }

    /// <summary>
    /// Rising Tides: the erosion forecast for the CURRENT player's
    /// turn — the shore tiles selected at turn start that will demote/submerge at
    /// turn end. Empty outside Rising Tides, on round 1, and between turns. The
    /// view telegraphs these tiles for the whole turn; the AI weighs them (it
    /// evacuates units off them); the controller applies exactly this set at
    /// end-of-turn (no re-pick, no drift). Persisted in saves so a mid-turn
    /// save/load keeps the locked forecast.
    /// </summary>
    public IReadOnlyList<TideStep> PendingTide { get; set; } = System.Array.Empty<TideStep>();

    // Backing store for the human player's fog-of-war memory: the set of coords
    // ever within sight. Mirrors the _water pattern — a private mutable set
    // exposed read-only, mutated through MarkSeen. Grows monotonically (knowledge
    // is sticky): coords are never un-seen, which keeps fog from flickering on
    // undo. The stale tier shows only static terrain (no owner, no occupant), so
    // a coord set is all that's needed. Only used when Mode == FogOfWar; excluded
    // from undo snapshots (see GameStateSnapshot).
    private readonly HashSet<HexCoord> _seen;

    /// <summary>
    /// The human player's fog-of-war memory: every coord ever within sight.
    /// Coords absent from this set have never been seen (full fog). Empty outside
    /// Fog Of War. Persisted in saves; read only by the renderer (to draw the
    /// stale tier) — no rule branches on it, so AI behaviour and determinism are
    /// unaffected.
    /// </summary>
    public IReadOnlySet<HexCoord> Seen => _seen;

    /// <summary>Mark <paramref name="coord"/> as seen by the human. Idempotent.</summary>
    public void MarkSeen(HexCoord coord) => _seen.Add(coord);

    /// <summary>Forget all fog-of-war memory. Used by the replay rewind so a
    /// replay re-animates fog from scratch instead of inheriting the live game's
    /// accumulated exploration.</summary>
    public void ClearSeen() => _seen.Clear();

    /// <summary>True if the human has ever seen <paramref name="coord"/>.</summary>
    public bool IsSeen(HexCoord coord) => _seen.Contains(coord);

    /// <summary>Convenience: this game runs with fog-of-war visibility rules.</summary>
    public bool FogEnabled => Mode == GameMode.FogOfWar;

    /// <summary>
    /// Viking Raiders mode state: raiders at sea + the wave-schedule cursor.
    /// Always non-null; default-empty (and never mutated) outside
    /// <see cref="GameMode.VikingRaiders"/>. Persisted in saves; excluded from
    /// undo snapshots (it only mutates during the viking pseudo-turn, and the
    /// undo stack clears at end of turn).
    /// </summary>
    public VikingState Vikings { get; }

    /// <summary>
    /// Difficulty of the player owning <paramref name="id"/>, resolved by SLOT
    /// (<see cref="PlayerId.Index"/>) rather than by indexing <see cref="Players"/>
    /// at that slot. With a 2–6 player game the roster is compacted (e.g. slots
    /// 0, 2, 5), so a player's slot index is NOT its position in the list.
    /// Returns <see cref="Difficulty.Soldier"/> for neutral land or an id
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
        IReadOnlySet<HexCoord>? waterCoords = null,
        GameMode mode = GameMode.Freeform,
        IReadOnlySet<HexCoord>? seen = null,
        VikingState? vikings = null)
    {
        Grid = grid;
        Territories = territories;
        Players = players;
        Turns = turns;
        Treasury = treasury;
        _water = waterCoords is null ? new HashSet<HexCoord>() : new HashSet<HexCoord>(waterCoords);
        Mode = mode;
        _seen = seen is null ? new HashSet<HexCoord>() : new HashSet<HexCoord>(seen);
        Vikings = vikings ?? new VikingState();
    }
}
