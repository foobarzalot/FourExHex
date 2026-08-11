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
    /// <summary>Units of this player destroyed by any cause: capture,
    /// bankruptcy disband, tide submerge, viking conquest.</summary>
    public int UnitsLost { get; set; }

    /// <summary>Towers this player built.</summary>
    public int TowersBuilt { get; set; }

    /// <summary>Viking (<see cref="PlayerId.None"/>-owned, Viking Raiders
    /// mode) units this player destroyed.</summary>
    public int VikingKills { get; set; }

    /// <summary>Highest <see cref="UnitLevel"/> (numeric) this player has
    /// had arrive or be placed on a tile; 0 when none yet.</summary>
    public int MaxUnitLevelFielded { get; set; }

    public bool IsZero =>
        UnitsLost == 0 && TowersBuilt == 0 && VikingKills == 0 && MaxUnitLevelFielded == 0;

    public PlayerRunStats Copy() => new()
    {
        UnitsLost = UnitsLost,
        TowersBuilt = TowersBuilt,
        VikingKills = VikingKills,
        MaxUnitLevelFielded = MaxUnitLevelFielded,
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
