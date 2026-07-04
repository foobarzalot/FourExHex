using System.Collections.Generic;

/// <summary>
/// A viking raider waiting at sea: a unit level parked on a coastal water
/// coord. Water coords have no <see cref="HexTile"/>, so sea vikings cannot be
/// <see cref="HexOccupant"/>s — they live in <see cref="VikingState.AtSea"/>
/// instead, and become ordinary <see cref="Unit"/>s (owned by
/// <see cref="PlayerId.None"/>) only when they disembark onto land.
/// </summary>
public readonly record struct SeaViking(HexCoord Coord, UnitLevel Level);

/// <summary>
/// Viking Raiders mode state: the raiders currently at sea plus the wave
/// schedule cursor. Lives on <see cref="GameState"/> (always non-null,
/// default-empty outside the mode, mirroring <see cref="GameState.PendingTide"/>).
/// Mutated only during the viking pseudo-turn, so it is excluded from undo
/// snapshots (see GameStateSnapshot) — landed vikings are ordinary grid
/// occupants and snapshot normally.
///
/// Invariant: every sea viking disembarks or perishes on its own turn, so
/// outside the viking turn <see cref="AtSea"/> holds at most the newest wave.
/// </summary>
public class VikingState
{
    // Kept lex-sorted by Coord so iteration (disembark order, serialization,
    // checksums) is deterministic regardless of insertion order.
    private readonly List<SeaViking> _atSea = new();

    /// <summary>Raiders currently at sea, in ascending <see cref="HexCoord"/> order.</summary>
    public IReadOnlyList<SeaViking> AtSea => _atSea;

    /// <summary>
    /// Index of the next wave to spawn, 0-based; equals
    /// <see cref="VikingRaidersRules.TotalWaves"/> once the schedule is exhausted.
    /// </summary>
    public int NextWaveIndex { get; set; }

    /// <summary>
    /// The round (turn number) whose viking pseudo-turn has completed; 0
    /// initially. The controller runs the viking turn once per round by
    /// comparing this against the current turn number.
    /// </summary>
    public int LastCompletedRound { get; set; }

    /// <summary>
    /// The round the most recent wave spawned in; 0 initially. Raiders at sea
    /// disembark only when this is an EARLIER round — a freshly spawned wave
    /// (LastSpawnRound == current round) waits, giving players exactly one
    /// round of warning. A single value suffices because <see cref="AtSea"/>
    /// holds at most one wave (every earlier raider disembarked or perished
    /// before a spawn is ever chosen).
    /// </summary>
    public int LastSpawnRound { get; set; }

    // Backing store for SeaGraves, kept lex-sorted like _atSea.
    private readonly List<HexCoord> _seaGraves = new();

    /// <summary>
    /// Water coords where a raider perished this round — a purely cosmetic
    /// marker (the view draws a grave that "washes away" when the next
    /// viking turn begins; a grave on water can never become a tree).
    /// Transient: NOT saved, NOT in the checksum, NOT snapshotted — a
    /// replay re-derives it from the recorded perish beats, and a mid-round
    /// load simply starts without the markers.
    /// </summary>
    public IReadOnlyList<HexCoord> SeaGraves => _seaGraves;

    /// <summary>Mark a perish site, keeping <see cref="SeaGraves"/> sorted. Idempotent.</summary>
    public void AddSeaGrave(HexCoord coord)
    {
        int i = 0;
        while (i < _seaGraves.Count && _seaGraves[i].CompareTo(coord) < 0) i++;
        if (i < _seaGraves.Count && _seaGraves[i] == coord) return;
        _seaGraves.Insert(i, coord);
    }

    /// <summary>Wash away every sea grave (the next viking turn began, or
    /// no viking turn will ever run again).</summary>
    public void ClearSeaGraves() => _seaGraves.Clear();

    /// <summary>Add a raider at sea, keeping <see cref="AtSea"/> sorted by coord.</summary>
    public void AddAtSea(SeaViking viking)
    {
        int i = 0;
        while (i < _atSea.Count && _atSea[i].Coord.CompareTo(viking.Coord) < 0) i++;
        _atSea.Insert(i, viking);
    }

    /// <summary>Remove the raider at <paramref name="coord"/>; true iff one was there.</summary>
    public bool RemoveAtSea(HexCoord coord)
    {
        for (int i = 0; i < _atSea.Count; i++)
        {
            if (_atSea[i].Coord == coord)
            {
                _atSea.RemoveAt(i);
                return true;
            }
        }
        return false;
    }

    /// <summary>True iff a raider sits at sea on <paramref name="coord"/>.</summary>
    public bool HasVikingAt(HexCoord coord)
    {
        foreach (SeaViking v in _atSea)
        {
            if (v.Coord == coord) return true;
        }
        return false;
    }

    /// <summary>
    /// Overwrite the whole state — used by save-load and the replay rewind
    /// (mirrors <see cref="GameState.ClearSeen"/> / <see cref="GameState.RemoveWater"/>).
    /// </summary>
    public void Reset(
        IEnumerable<SeaViking> atSea, int nextWaveIndex, int lastCompletedRound, int lastSpawnRound)
    {
        _atSea.Clear();
        _seaGraves.Clear();
        foreach (SeaViking v in atSea) AddAtSea(v);
        NextWaveIndex = nextWaveIndex;
        LastCompletedRound = lastCompletedRound;
        LastSpawnRound = lastSpawnRound;
    }
}
