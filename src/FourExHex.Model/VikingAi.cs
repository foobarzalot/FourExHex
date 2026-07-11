using System;
using System.Collections.Generic;

/// <summary>
/// The viking pseudo-turn sequencer. Called repeatedly by the turn driver
/// (exactly like a player AI chooser) until it returns null; each call picks
/// the next single action, in strict phase order:
///   1. Disembark: while raiders from an EARLIER round sit at sea, the first
///      (lex by coord) one lands on its best-scoring target, or perishes if
///      every neighbouring land tile is blocked.
///   2. Landed moves: <see cref="ComputerAi.ChooseNextAction"/> for
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
    /// </summary>
    public static AiAction? ChooseNext(
        GameState state, HashSet<HexCoord> visitedAnchors, Random rng)
    {
        if (state.Mode != GameMode.VikingRaiders) return null;
        int round = state.Turns.TurnNumber;

        // 1. Disembark raiders that arrived in an earlier round (a wave
        //    spawned THIS round waits — one round of warning).
        if (state.Vikings.AtSea.Count > 0 && state.Vikings.LastSpawnRound < round)
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

        // 2. Landed moves: the ordinary AI driving the neutral territories.
        //    Vikings never reposition (captures only, 4b skipped), so the
        //    loop-guard set is irrelevant — pass a throwaway.
        AiAction? landed = ComputerAi.ChooseNextAction(
            state, PlayerId.None, visitedAnchors, NoRepositionedUnits, rng);
        if (landed != null) return landed;

        // 3. Spawn a due wave LAST, so it never acts on its spawn round. The
        //    placements are drawn here (the turn's only RNG consumers) and
        //    carried in the action.
        if (VikingRaidersRules.WaveDue(round, state.Vikings.NextWaveIndex))
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
        tile.Owner = PlayerId.None;
        tile.Occupant = new Unit(PlayerId.None, level) { HasMovedThisTurn = true };
        if (wasCapture)
        {
            clone.Territories = TerritoryFinder.Recompute(
                clone.Grid, clone.Territories, clone.Treasury, clone.UseRandomizedSelection);
        }
    }
}
