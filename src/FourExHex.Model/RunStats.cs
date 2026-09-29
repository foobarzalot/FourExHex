// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;

/// <summary>
/// One player's observation-only counters for the current game. Mutable
/// ints incremented from the live execution paths only — never from
/// <c>AiSimulator</c> lookahead — and read once, by the achievement facts
/// assembly at game end. Deliberately absent from
/// <see cref="GameStateChecksum"/> so determinism goldens never depend
/// on them.
/// </summary>
public sealed class PlayerRunStats
{
    /// <summary>Units of this player disbanded by bankrupt upkeep.</summary>
    public int UnitsLostToBankruptcy { get; set; }

    /// <summary>Towers this player built.</summary>
    public int TowersBuilt { get; set; }

    /// <summary>Viking (<see cref="PlayerId.None"/>-owned, Viking Raiders
    /// mode) units this player destroyed.</summary>
    public int VikingKills { get; set; }

    /// <summary>Highest <see cref="UnitLevel"/> (numeric) this player has
    /// had arrive or be placed on a tile; 0 when none yet.</summary>
    public int MaxUnitLevelFielded { get; set; }

    /// <summary>1-based position of this player's elimination (loss of
    /// their last capital) in the game's elimination sequence; 0 while
    /// they have not been eliminated.</summary>
    public int EliminationOrder { get; set; }

    /// <summary>True when the elimination came from a Rising Tides
    /// submerge rather than a capture.</summary>
    public bool EliminatedByTide { get; set; }

    public bool IsZero =>
        UnitsLostToBankruptcy == 0 && TowersBuilt == 0 && VikingKills == 0 && MaxUnitLevelFielded == 0
        && EliminationOrder == 0 && !EliminatedByTide;

    public PlayerRunStats Copy() => new()
    {
        UnitsLostToBankruptcy = UnitsLostToBankruptcy,
        TowersBuilt = TowersBuilt,
        VikingKills = VikingKills,
        MaxUnitLevelFielded = MaxUnitLevelFielded,
        EliminationOrder = EliminationOrder,
        EliminatedByTide = EliminatedByTide,
    };
}

/// <summary>
/// Per-player <see cref="PlayerRunStats"/> for the current game, owned by
/// <see cref="GameState"/>. Captured by undo entries (a tower built then
/// undone must not count) and persisted in saves; replay playback starts
/// from zero (<see cref="Clear"/>) because playback re-executes real beats
/// and never awards.
/// </summary>
public sealed class RunStats
{
    private readonly Dictionary<PlayerId, PlayerRunStats> _byPlayer = new();

    /// <summary>This player's counters, created zeroed on first access.</summary>
    public PlayerRunStats For(PlayerId id)
    {
        if (!_byPlayer.TryGetValue(id, out PlayerRunStats? stats))
        {
            stats = new PlayerRunStats();
            _byPlayer[id] = stats;
        }
        return stats;
    }

    /// <summary>Mark <paramref name="victim"/> as eliminated, next in the
    /// game's elimination sequence, with its cause.</summary>
    public void RecordElimination(PlayerId victim, bool byTide)
    {
        int last = 0;
        foreach (PlayerRunStats s in _byPlayer.Values)
        {
            if (s.EliminationOrder > last) last = s.EliminationOrder;
        }
        PlayerRunStats stats = For(victim);
        stats.EliminationOrder = last + 1;
        stats.EliminatedByTide = byTide;
    }

    /// <summary>The most recently eliminated player other than
    /// <paramref name="excluding"/>; null when nobody else has been.</summary>
    public PlayerId? LastEliminated(PlayerId excluding)
    {
        PlayerId? latest = null;
        int latestOrder = 0;
        foreach (KeyValuePair<PlayerId, PlayerRunStats> kvp in _byPlayer)
        {
            if (kvp.Key == excluding || kvp.Value.EliminationOrder <= latestOrder) continue;
            latest = kvp.Key;
            latestOrder = kvp.Value.EliminationOrder;
        }
        return latest;
    }

    /// <summary>Every player with a stats entry (zero entries included).</summary>
    public IReadOnlyDictionary<PlayerId, PlayerRunStats> Entries => _byPlayer;

    /// <summary>Independent deep copy (for undo capture).</summary>
    public RunStats Copy()
    {
        var copy = new RunStats();
        foreach (KeyValuePair<PlayerId, PlayerRunStats> kvp in _byPlayer)
        {
            copy._byPlayer[kvp.Key] = kvp.Value.Copy();
        }
        return copy;
    }

    /// <summary>Replace this instance's contents with a deep copy of
    /// <paramref name="other"/> (for undo restore — the <see cref="GameState"/>
    /// reference itself never changes).</summary>
    public void RestoreFrom(RunStats other)
    {
        _byPlayer.Clear();
        foreach (KeyValuePair<PlayerId, PlayerRunStats> kvp in other._byPlayer)
        {
            _byPlayer[kvp.Key] = kvp.Value.Copy();
        }
    }

    /// <summary>Zero everything (replay rewind).</summary>
    public void Clear() => _byPlayer.Clear();
}
