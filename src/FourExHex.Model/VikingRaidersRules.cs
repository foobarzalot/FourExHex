using System;
using System.Collections.Generic;

/// <summary>
/// Pure rules for the "Viking Raiders" game mode. Godot-free and integer-only
/// (the no-floats rule). Fixed escalating wave schedule: the first wave spawns
/// at the start of round <see cref="FirstWaveRound"/>, then every
/// <see cref="WaveIntervalRounds"/> rounds, <see cref="TotalWaves"/> waves in
/// all. A wave spawns raiders on coastal water coords (one per coord); on the
/// NEXT viking turn — one full round later, so players always get exactly one
/// round of warning — each sea viking disembarks onto an adjacent land tile or
/// perishes. All randomness comes from the caller-supplied seeded RNG, so runs
/// are deterministic in the master seed.
/// </summary>
public static class VikingRaidersRules
{
    /// <summary>Round (turn number) at whose viking turn the first wave spawns.</summary>
    public const int FirstWaveRound = 3;

    /// <summary>Rounds between wave spawns.</summary>
    public const int WaveIntervalRounds = 3;

    /// <summary>Total number of waves in the fixed schedule.</summary>
    public const int TotalWaves = 6;

    /// <summary>One raider per this many coastal water tiles (base wave size).</summary>
    public const int CoastalTilesPerViking = 8;

    /// <summary>A wave is never smaller than this (before the per-wave growth).</summary>
    public const int MinWaveSize = 2;

    /// <summary>
    /// Water coords that touch land — the tiles a wave can spawn on. Returned
    /// in ascending <see cref="HexCoord"/> order so a seeded draw over the
    /// list is reproducible. (The mirror of <see cref="RisingTidesRules.ShoreTilesOf"/>:
    /// that walks land looking for missing neighbours; this walks water looking
    /// for present ones.)
    /// </summary>
    public static IReadOnlyList<HexCoord> CoastalWaterCoords(GameState state)
    {
        var coastal = new List<HexCoord>();
        foreach (HexCoord water in state.WaterCoords)
        {
            foreach (HexCoord n in water.Neighbors())
            {
                if (state.Grid.Contains(n))
                {
                    coastal.Add(water);
                    break;
                }
            }
        }
        coastal.Sort();
        return coastal;
    }

    /// <summary>
    /// The wave index (0-based) that spawns at the start of <paramref name="round"/>,
    /// or null if no wave spawns that round (before the first wave, between
    /// waves, or after the schedule is exhausted).
    /// </summary>
    public static int? WaveIndexForRound(int round)
    {
        if (round < FirstWaveRound) return null;
        if ((round - FirstWaveRound) % WaveIntervalRounds != 0) return null;
        int index = (round - FirstWaveRound) / WaveIntervalRounds;
        return index < TotalWaves ? index : null;
    }

    /// <summary>
    /// True iff wave <paramref name="nextWaveIndex"/> should spawn on
    /// <paramref name="round"/>'s viking turn: the wave's scheduled round
    /// (<see cref="FirstWaveRound"/> + index × <see cref="WaveIntervalRounds"/>)
    /// has arrived or passed. The ≥ (rather than ==) makes a missed spawn
    /// (e.g. a step-capped viking turn) catch up on the next round instead of
    /// silently dropping the wave.
    /// </summary>
    public static bool WaveDue(int round, int nextWaveIndex)
        => nextWaveIndex < TotalWaves
           && round >= FirstWaveRound + nextWaveIndex * WaveIntervalRounds;

    /// <summary>
    /// The unit levels making up wave <paramref name="waveIndex"/> on a map
    /// with <paramref name="coastalCount"/> coastal water tiles. Size =
    /// max(<see cref="MinWaveSize"/>, coastalCount / <see cref="CoastalTilesPerViking"/>)
    /// + waveIndex. Levels escalate: waves 0–1 all Recruits, 2–3 mix in
    /// Soldiers, 4–5 mix in Captains. Never Commander.
    /// </summary>
    public static IReadOnlyList<UnitLevel> WaveComposition(int waveIndex, int coastalCount)
    {
        int size = System.Math.Max(MinWaveSize, coastalCount / CoastalTilesPerViking) + waveIndex;
        // Waves 0–1: all Recruits. 2–3: alternate Soldier/Recruit. 4–5 (and
        // any hypothetical later): alternate Captain/Soldier. Never Commander.
        (UnitLevel strong, UnitLevel weak) = waveIndex switch
        {
            <= 1 => (UnitLevel.Recruit, UnitLevel.Recruit),
            <= 3 => (UnitLevel.Soldier, UnitLevel.Recruit),
            _ => (UnitLevel.Captain, UnitLevel.Soldier),
        };
        var levels = new List<UnitLevel>(size);
        for (int i = 0; i < size; i++)
        {
            levels.Add(i % 2 == 0 ? strong : weak);
        }
        return levels;
    }

    /// <summary>
    /// Pick spawn coords for a wave — a "landing party" placement, one
    /// raider per coastal water coord not already holding a sea viking,
    /// placed sequentially in composition order. Each raider prefers, in
    /// strict priority order:
    ///   1. any viable landing at all for ITS level (a zero-target spot is
    ///      certain death — the true last resort),
    ///   2. a landing zone that does NOT overlap those of raiders already
    ///      placed this wave — overlapping (competing for the same beach
    ///      tile) is a LAST RESORT among landable spots, chosen only when
    ///      every landable candidate overlaps,
    ///   3. the most viable landing neighbours
    ///      (<see cref="DisembarkTargets"/> count — maximizing the odds a
    ///      landing survives the defenders' warning round),
    ///   4. the shortest hex distance to the nearest already-placed raider
    ///      (the wave clusters instead of scattering),
    /// with remaining ties broken by a seed-deterministic rng draw over the
    /// (ascending-ordered) tied candidates. Count clamps to the available
    /// coords. Returned sorted by coord.
    /// </summary>
    public static IReadOnlyList<SeaViking> ChooseSpawns(
        GameState state, IReadOnlyList<UnitLevel> composition, Random rng)
    {
        var candidates = new List<HexCoord>();
        foreach (HexCoord coastal in CoastalWaterCoords(state))
        {
            if (!state.Vikings.HasVikingAt(coastal)) candidates.Add(coastal);
        }

        int count = System.Math.Min(composition.Count, candidates.Count);
        var spawns = new List<SeaViking>(count);
        var placed = new List<HexCoord>();
        var claimedLandings = new HashSet<HexCoord>();
        var taken = new HashSet<HexCoord>();

        for (int i = 0; i < count; i++)
        {
            UnitLevel level = composition[i];
            HexCoord pick = PickSpawnCoord(
                state, candidates, taken, placed, claimedLandings, level, rng);
            taken.Add(pick);
            placed.Add(pick);
            IReadOnlyList<HexCoord> pickLandings = DisembarkTargets(state, pick, level);
            foreach (HexCoord landing in pickLandings)
            {
                if (claimedLandings.Contains(landing))
                {
                    // Overlap is a last resort — this line firing means every
                    // landable candidate overlapped the party's landing zones.
                    Log.Debug(Log.LogCategory.Viking,
                        $"[viking] spawn {pick} overlaps party landing zone at {landing} (forced)");
                    break;
                }
            }
            foreach (HexCoord landing in pickLandings)
            {
                claimedLandings.Add(landing);
            }
            spawns.Add(new SeaViking(pick, level));
        }
        spawns.Sort((a, b) => a.Coord.CompareTo(b.Coord));
        return spawns;
    }

    /// <summary>
    /// The best spawn coord for one raider under <see cref="ChooseSpawns"/>'
    /// lexicographic preference (landable first, no-overlap second,
    /// viable-landing count desc, distance-to-party asc, rng among ties).
    /// Candidates are ascending-ordered, so the tie list — and therefore
    /// the rng draw — is deterministic in the seed.
    /// </summary>
    private static HexCoord PickSpawnCoord(
        GameState state,
        List<HexCoord> candidates,
        HashSet<HexCoord> taken,
        List<HexCoord> placed,
        HashSet<HexCoord> claimedLandings,
        UnitLevel level,
        Random rng)
    {
        var ties = new List<HexCoord>();
        bool bestCanLand = false;
        int bestViable = -1;
        bool bestOverlaps = true;
        int bestDistance = int.MaxValue;

        foreach (HexCoord candidate in candidates)
        {
            if (taken.Contains(candidate)) continue;

            IReadOnlyList<HexCoord> landings = DisembarkTargets(state, candidate, level);
            int viable = landings.Count;
            bool overlaps = false;
            foreach (HexCoord landing in landings)
            {
                if (claimedLandings.Contains(landing)) { overlaps = true; break; }
            }
            // Distance to the nearest already-placed raider; 0 for the
            // party's first raider (cohesion has nothing to cohere to yet).
            int distance = 0;
            if (placed.Count > 0)
            {
                distance = int.MaxValue;
                foreach (HexCoord p in placed)
                {
                    int d = HexCoord.Distance(candidate, p);
                    if (d < distance) distance = d;
                }
            }

            bool canLand = viable > 0;
            int cmp = canLand.CompareTo(bestCanLand);
            if (cmp == 0) cmp = (!overlaps).CompareTo(!bestOverlaps);
            if (cmp == 0) cmp = viable.CompareTo(bestViable);
            if (cmp == 0) cmp = bestDistance.CompareTo(distance);
            if (cmp < 0) continue;
            if (cmp > 0)
            {
                ties.Clear();
                bestCanLand = canLand;
                bestViable = viable;
                bestOverlaps = overlaps;
                bestDistance = distance;
            }
            ties.Add(candidate);
        }

        return ties[rng.Next(ties.Count)];
    }

    /// <summary>
    /// The land tiles a level-<paramref name="level"/> sea viking at
    /// <paramref name="seaCoord"/> could disembark onto: grid neighbours that
    /// are either (a) player-owned and capturable under the ordinary threshold
    /// (<see cref="DefenseRules.Defense"/> &lt; level, radiation included), or
    /// (b) neutral (<see cref="PlayerId.None"/>-owned) with no Unit/Tower/Capital
    /// occupant — a reposition-like landing on already-neutral ground (trees and
    /// graves allowed, exactly like an ordinary in-territory reposition; no
    /// defense check against the vikings' own side). Empty ⇒ the viking perishes.
    /// </summary>
    public static IReadOnlyList<HexCoord> DisembarkTargets(
        GameState state, HexCoord seaCoord, UnitLevel level)
    {
        var targets = new List<HexCoord>();
        Dictionary<HexCoord, Territory> tileToTerritory = state.Territories.BuildTileIndex();
        foreach (HexCoord n in seaCoord.Neighbors())
        {
            HexTile? tile = state.Grid.Get(n);
            if (tile == null) continue; // water / off-map

            if (tile.Owner.IsNone)
            {
                // Reposition-like landing on already-neutral ground: blocked
                // only by exclusive occupants (fellow unit / tower; a capital
                // can't sit on neutral land). Trees and graves allow landing,
                // exactly like an ordinary in-territory reposition.
                if (tile.Occupant == null || tile.Occupant is Tree || tile.Occupant is Grave)
                {
                    targets.Add(n);
                }
                continue;
            }

            // Player-owned tile: the ordinary capture threshold, radiation
            // included (mirrors MovementRules.ValidTargets' capture branch).
            if (!tileToTerritory.TryGetValue(n, out Territory? targetTerritory)) continue;
            if (DefenseRules.Defense(n, state.Grid, targetTerritory) < (int)level)
            {
                targets.Add(n);
            }
        }
        targets.Sort();
        return targets;
    }

    /// <summary>
    /// True when this round's viking pseudo-turn is due or mid-flight:
    /// Viking Raiders mode, the schedule has started
    /// (round ≥ <see cref="FirstWaveRound"/>), this round's phase hasn't
    /// completed, and any threat remains. Pure state predicate — the
    /// controller layers its game-over gates on top
    /// (GameOperations.VikingTurnPending), and the HUD lights the neutral
    /// turn-order swatch from it.
    /// </summary>
    public static bool TurnDue(GameState state)
        => state.Mode == GameMode.VikingRaiders
           && state.Turns.TurnNumber >= FirstWaveRound
           && state.Vikings.LastCompletedRound < state.Turns.TurnNumber
           && ThreatRemains(state);

    /// <summary>
    /// True while any viking threat remains: raiders at sea, un-spawned waves
    /// in the schedule, or landed <see cref="PlayerId.None"/>-owned units on
    /// the grid. Always false outside <see cref="GameMode.VikingRaiders"/>.
    /// While true, no win condition may fire (see the mode-branched checks in
    /// GameOperations / GameController).
    /// </summary>
    public static bool ThreatRemains(GameState state)
    {
        if (state.Mode != GameMode.VikingRaiders) return false;
        if (state.Vikings.AtSea.Count > 0) return true;
        if (state.Vikings.NextWaveIndex < TotalWaves) return true;
        foreach (HexTile tile in state.Grid.Tiles)
        {
            if (tile.Occupant is Unit u && u.Owner.IsNone) return true;
        }
        return false;
    }
}
