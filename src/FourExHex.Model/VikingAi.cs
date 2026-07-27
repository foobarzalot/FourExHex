// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;

/// <summary>
/// The viking pseudo-turn sequencer. Called repeatedly by the turn driver
/// (exactly like a player AI chooser) until it returns null; each call picks
/// the next single action, in strict phase order:
///   1. Disembark: while raiders from an EARLIER round sit at sea, the first
///      (lex by coord) one lands on its best-scoring target, or perishes if
///      every neighbouring land tile is blocked.
///   2. Landed moves, split by <see cref="Unit.IsAggro"/>:
///      passive barbarian territories wander — each unit takes at most one
///      random in-territory reposition per turn (staying put is part of the
///      draw; a tide-doomed unit always retreats), never a capture — while
///      aggro territories run <see cref="ComputerAi.ChooseNextAction"/> for
///      <see cref="PlayerId.None"/> — captures only on the capital-less
///      neutral territories (no buys/towers/combines/defensive repositions,
///      and trees are chopped only as captures of enemy land); a raider
///      with no capture holds.
///   3. Spawn: if a wave is due (<see cref="VikingRaidersRules.WaveDue"/>),
///      spawn it LAST — a fresh wave never acts on its spawn round, so
///      players always get exactly one round of warning.
/// Deterministic: the only RNG draws are the spawn placement (carried inside
/// the returned <see cref="VikingSpawnWaveAction"/>) via the caller's seeded rng.
/// </summary>
public static class VikingAi
{
    // Vikings never reposition, so ComputerAi's loop-guard parameter is
    // moot for them; one shared empty set avoids a per-call allocation.
    private static readonly HashSet<HexCoord> NoRepositionedUnits = new();

    /// <summary>
    /// Pick the next viking action, or null when the viking turn is over.
    /// <paramref name="visitedAnchors"/> is the same per-turn exhausted-set
    /// the player AI uses (keyed by territory anchor coord); mutated.
    /// <paramref name="wanderedUnits"/> is the passive-wander loop guard
    /// (one beat per unit per turn, keyed by the unit's post-beat coord;
    /// caller-owned, reset each turn) — without it a reposition, which
    /// never sets <see cref="Unit.HasMovedThisTurn"/>, would be re-chosen
    /// until the driver's step backstop.
    /// </summary>
    public static AiAction? ChooseNext(
        GameState state, HashSet<HexCoord> visitedAnchors, DeterministicRng rng,
        HashSet<HexCoord> wanderedUnits)
    {
        int round = state.Turns.TurnNumber;
        // Only Viking Raiders runs the sea half of the invasion — the wave
        // schedule and the disembark beat. Every other mode reaches this
        // sequencer solely for raiders an authored map placed on land, which
        // move through step 2 exactly as landed raiders always have.
        bool seaborne = state.Mode == GameMode.VikingRaiders;

        // 1. Disembark raiders that arrived in an earlier round (a wave
        //    spawned THIS round waits — one round of warning).
        if (seaborne && state.Vikings.AtSea.Count > 0 && state.Vikings.LastSpawnRound < round)
        {
            SeaViking viking = state.Vikings.AtSea[0];
            IReadOnlyList<HexCoord> targets =
                VikingRaidersRules.DisembarkTargets(state, viking.Coord, viking.Level);
            if (targets.Count == 0)
            {
                return new VikingPerishAtSeaAction(viking.Coord);
            }
            return new VikingDisembarkAction(viking.Coord, BestLanding(state, viking, targets));
        }

        // 2a. Passive barbarians wander (or hold) within their own
        //     territory — never expanding.
        AiAction? wander = ChooseWander(state, wanderedUnits, rng);
        if (wander != null) return wander;

        // 2b. Aggro landed moves: the ordinary AI driving the neutral
        //     territories. Passive territories are masked off via the
        //     visited set so ComputerAi never enumerates their captures.
        //     Aggro vikings never reposition (captures only, 4b skipped),
        //     so ComputerAi's loop-guard set is irrelevant — pass a
        //     throwaway.
        foreach (Territory t in state.Territories)
        {
            if (t.Owner.IsNone && BarbarianRules.IsNonAggroBarbarianTerritory(t, state.Grid))
            {
                visitedAnchors.Add(TerritoryLookup.AnchorCoord(t));
            }
        }
        AiAction? landed = ComputerAi.ChooseNextAction(
            state, PlayerId.None, visitedAnchors, NoRepositionedUnits, rng);
        if (landed != null) return landed;

        // 3. Spawn a due wave LAST, so it never acts on its spawn round. The
        //    placements are drawn here (the turn's only RNG consumers) and
        //    carried in the action.
        if (seaborne && VikingRaidersRules.WaveDue(round, state.Vikings.NextWaveIndex))
        {
            int waveIndex = state.Vikings.NextWaveIndex;
            IReadOnlyList<HexCoord> coastal = VikingRaidersRules.CoastalWaterCoords(state);
            IReadOnlyList<UnitLevel> composition =
                VikingRaidersRules.WaveComposition(waveIndex);
            IReadOnlyList<SeaViking> spawns =
                VikingRaidersRules.ChooseSpawns(state, composition, rng);
            if (spawns.Count > 0)
            {
                return new VikingSpawnWaveAction(waveIndex, spawns);
            }
            // No coastal water at all (fully landlocked map) — the wave has
            // nowhere to spawn; report it spent so the schedule advances.
            Log.Info(Log.LogCategory.Viking,
                $"[viking] wave {waveIndex} has no coastal spawn sites — skipped");
            return new VikingSpawnWaveAction(waveIndex, spawns);
        }

        return null;
    }

    /// <summary>
    /// One passive-wander beat: the first (territory order, then lex coord)
    /// passive barbarian not yet in <paramref name="wanderedUnits"/> draws
    /// uniformly among its legal in-territory destinations plus one "hold"
    /// slot. A tide-doomed unit never draws hold and never flees onto a
    /// doomed tile (the truly cornered case was already aggroed at the
    /// seat's turn start). Holds mark the unit spent and keep scanning;
    /// a move is returned as a plain <see cref="AiMoveAction"/> (executed
    /// and replayed through the existing viking-move path).
    /// </summary>
    private static AiAction? ChooseWander(
        GameState state, HashSet<HexCoord> wanderedUnits, DeterministicRng rng)
    {
        HashSet<HexCoord> doomed = BarbarianRules.TideDoomedCoords(state);
        foreach (Territory territory in state.Territories)
        {
            if (!territory.Owner.IsNone) continue;
            if (!BarbarianRules.IsNonAggroBarbarianTerritory(territory, state.Grid)) continue;
            foreach (HexCoord coord in territory.Coords)
            {
                if (state.Grid.Get(coord)?.Occupant is not Unit { IsAggro: false } unit) continue;
                if (wanderedUnits.Contains(coord)) continue;

                List<HexCoord> destinations = BarbarianRules.WanderDestinations(
                    state, territory, coord, unit.Level, doomed);
                if (destinations.Count == 0)
                {
                    wanderedUnits.Add(coord);
                    Log.Debug(Log.LogCategory.Viking,
                        $"[barb] hold unit={coord} (no open ground)");
                    continue;
                }

                bool unitDoomed = doomed.Contains(coord);
                int pick = unitDoomed
                    ? rng.NextBounded(destinations.Count)
                    : rng.NextBounded(destinations.Count + 1);
                if (pick == destinations.Count)
                {
                    wanderedUnits.Add(coord);
                    Log.Debug(Log.LogCategory.Viking, $"[barb] hold unit={coord}");
                    continue;
                }

                HexCoord dest = destinations[pick];
                wanderedUnits.Add(dest);
                Log.Debug(Log.LogCategory.Viking,
                    $"[barb] wander unit={coord} -> {dest}" +
                    (unitDoomed ? " (tide retreat)" : ""));
                return new AiMoveAction(coord, dest);
            }
        }
        return null;
    }

    /// <summary>
    /// Score each candidate landing by clone + apply + score from the
    /// neutral perspective (the same 1-ply lookahead the player AI uses) and
    /// return the best; ties resolve to the first (lex-min) target.
    /// </summary>
    private static HexCoord BestLanding(
        GameState state, SeaViking viking, IReadOnlyList<HexCoord> targets)
    {
        HexCoord best = targets[0];
        int bestScore = int.MinValue;
        foreach (HexCoord target in targets)
        {
            GameState clone = AiSimulator.Clone(state);
            ApplyDisembarkTo(clone, target, viking.Level);
            int score = AiStateScorer.Score(clone, PlayerId.None);
            if (score > bestScore)
            {
                bestScore = score;
                best = target;
            }
        }
        return best;
    }

    /// <summary>
    /// The bare disembark mutation, applied to a simulation clone: the tile
    /// turns neutral and gains a spent viking unit; a capture (owner actually
    /// changed) re-partitions territories exactly like
    /// <see cref="AiSimulator"/>'s capture reconcile. The live-play envelope
    /// (GameOperations.ExecuteVikingDisembark) performs the same mutation
    /// plus view/defeat/win effects.
    /// </summary>
    private static void ApplyDisembarkTo(GameState clone, HexCoord land, UnitLevel level)
    {
        HexTile tile = clone.Grid.Get(land)!;
        bool wasCapture = !tile.Owner.IsNone;
        IReadOnlyList<Territory> previous = clone.Territories;
        tile.Owner = PlayerId.None;
        tile.Occupant = new Unit(PlayerId.None, level)
        {
            HasMovedThisTurn = true,
            IsAggro = true,
        };
        if (wasCapture)
        {
            clone.Territories = TerritoryFinder.Recompute(
                clone.Grid, previous, clone.Treasury, randomizeCapital: true);
        }
        // Mirror the live envelope's barbarian aggro pass (HandleCapture on
        // a capture, the explicit spread on a neutral landing).
        BarbarianRules.PropagateAggro(clone, previous);
    }
}
