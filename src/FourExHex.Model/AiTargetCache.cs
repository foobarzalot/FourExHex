// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;

/// <summary>
/// Per-territory-scan memo for the level-dependent target queries the
/// AI's phase-2 enumerators share. <see cref="MovementRules.ValidTargets"/>
/// depends only on (level, territory) — never unit position — so within
/// one <see cref="ComputerAi.ChooseNextAction"/> territory scan (the
/// search only mutates clones, never the live state) there are at most
/// four distinct results per query; this cache computes each once.
/// An instance must never outlive its scan: executing an action changes
/// territories and would stale it.
/// </summary>
public sealed class AiTargetCache
{
    private readonly Territory _territory;
    private readonly GameState _state;
    private readonly IReadOnlyDictionary<HexCoord, Territory>? _tileIndex;
    private readonly Dictionary<UnitLevel, List<HexCoord>> _validTargets = new();
    private readonly Dictionary<UnitLevel, HashSet<HexCoord>> _consumingTargets = new();

    public AiTargetCache(
        Territory territory,
        GameState state,
        IReadOnlyDictionary<HexCoord, Territory>? tileIndex = null)
    {
        _territory = territory;
        _state = state;
        _tileIndex = tileIndex;
    }

    /// <summary>Memoized <see cref="MovementRules.ValidTargets"/> for the
    /// scan's territory. Callers must not mutate the returned list.</summary>
    public List<HexCoord> ValidTargets(UnitLevel level)
    {
        if (_validTargets.TryGetValue(level, out List<HexCoord>? cached)) return cached;
        List<HexCoord> targets = MovementRules.ValidTargets(
            level, _territory, _state.Grid, _state.Territories, _tileIndex);
        _validTargets[level] = targets;
        return targets;
    }

    /// <summary>Memoized movement-consuming subset (capture / chop / grave)
    /// of <see cref="ValidTargets"/>. Callers must not mutate the returned
    /// set.</summary>
    public HashSet<HexCoord> ConsumingTargets(UnitLevel level)
    {
        if (_consumingTargets.TryGetValue(level, out HashSet<HexCoord>? cached)) return cached;
        var consuming = new HashSet<HexCoord>();
        foreach (HexCoord target in ValidTargets(level))
        {
            HexTile? tile = _state.Grid.Get(target);
            if (tile == null) continue;
            if (AiCommon.IsMovementConsumingTarget(tile, _territory.Owner))
                consuming.Add(target);
        }
        _consumingTargets[level] = consuming;
        return consuming;
    }
}
