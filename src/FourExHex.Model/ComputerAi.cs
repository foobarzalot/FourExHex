// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

/// <summary>
/// Stepwise-greedy 1-ply AI. For each call, picks the
/// largest non-exhausted owned territory then tries phases 1→4 in
/// order:
///   1. Free-unit captures, chops, grave clears (existing units).
///   2a. Combine-to-unlock (existing units; unlock filter required).
///   2b. Buy-and-combine-to-unlock (unlock filter required).
///   3. Buy-to-capture / buy-to-chop.
///   4a. Tower placement.
///   4b. Defensive reposition (existing units to border tiles).
///
/// Vikings (forPlayer == <see cref="PlayerId.None"/>) run phase 1 only,
/// restricted to captures: they have no economy (2a–4a) and never make a
/// defensive-only move (4b) — a landed raider with no capture holds.
///
/// The offensive phases (1 and 3) iterate tiers weakest→strongest so
/// the cheapest sufficient unit takes each tile: phase 1 scans one
/// representative unit per distinct level (same-level units share an
/// identical candidate set — see
/// <see cref="AiCommon.MovableUnitTiersWeakestFirst"/>), phase 3 walks
/// purchase levels Recruit→Commander, and both commit to the first
/// tier that yields any candidate — a tier that yields nothing is
/// skipped for the rest of the call. Because the driver re-enters
/// ChooseNextAction after every applied action, the scan naturally
/// restarts from the weakest tier on each capture. The
/// strength-concentrating phases (2a combines, 4b defensive
/// repositions) iterate units in power-then-coord order instead.
/// Within a tier or unit, all candidates are scored
/// (clone+apply+score) and the best one wins. The first phase × tier
/// combination that yields an action returns immediately — lower
/// phases never run for that territory in that call.
///
/// Phases 1, 2a, 2b, and 3 (free captures/chops/grave-clears,
/// combines, and buy-to-attack) commit to their best legal candidate
/// REGARDLESS of delta sign — an offensive or unlock action is never
/// declined in favor of the status quo, even when exposing a border
/// or forfeiting an enemy fragment's negative value makes the
/// immediate delta ≤ 0. The enumerators' solvency gates
/// (CanAfford + SurvivesNextUpkeep), not the score, are what guard
/// the economics of the spend phases. Only phases 4a/4b (towers,
/// defensive repositions) keep the strictly-positive (> 0) gate:
/// defense is genuinely optional, so doing nothing is a valid choice.
///
/// A territory is marked visited (exhausted) only when all phases
/// produce no candidate, ensuring that a later territory's action
/// can't retroactively unlock a skipped one.
/// </summary>
public static class ComputerAi
{
    /// <summary>
    /// Pick the best single action for <paramref name="forPlayer"/>
    /// under the stepwise-greedy phase ordering, or null if no
    /// unvisited territory yields an action (no legal candidate in
    /// phases 1–3, no positive-delta candidate in phase 4).
    /// <paramref name="visitedCapitals"/> is mutated: territories
    /// yielding no action in any phase are recorded so subsequent
    /// calls skip them. <paramref name="repositionedUnits"/> is
    /// caller-owned AI decision state (the controller's loop guard):
    /// coords of units already repositioned this turn, excluded from
    /// phase 4b so the search can't ping-pong a unit between border
    /// tiles — repositions never set <see cref="Unit.HasMovedThisTurn"/>
    /// (that flag is the real movement-consumption rule), so without
    /// this set 4b would re-enumerate them forever.
    /// </summary>
    public static AiAction? ChooseNextAction(
        GameState state,
        PlayerId forPlayer,
        HashSet<HexCoord> visitedCapitals,
        HashSet<HexCoord> repositionedUnits,
        DeterministicRng rng)
    {
        long methodStart = Log.Stamp();
        var prof = new AiSearchProfile();

        long scoreT = Log.Stamp();
        int baseScore = AiStateScorer.Score(state, forPlayer);
        prof.ScoreTicks += Log.Stamp() - scoreT;

        // Whole-board tile->territory lookup shared by every phase
        // enumeration below. Valid for the entire decision: the search
        // only mutates clones, never `state` itself.
        Dictionary<HexCoord, Territory> tileIndex = state.Territories.BuildTileIndex();

        foreach (Territory t in state.Territories
            .OrderByDescending(terr => terr.Size)
            .ThenBy(terr => TerritoryLookup.AnchorCoord(terr)))
        {
            if (t.Owner != forPlayer) continue;
            // Vikings (forPlayer == None) drive capital-less neutral
            // territories; every other player needs a live capital.
            if (!t.HasCapital && !forPlayer.IsNone) continue;
            HexCoord anchor = TerritoryLookup.AnchorCoord(t);
            if (visitedCapitals.Contains(anchor)) continue;
            Log.Debug(Log.LogCategory.Ai,
                $"[territory-order] capital={anchor} size={t.Size}");

            // Phase 1: free unit captures / chops / grave clears —
            // weakest tier first, one representative unit per tier.
            // Vikings capture only — an own-territory tree/grave is harmless
            // to an upkeep-free force, so they never chop "for its own sake"
            // (an enemy tile carrying a tree still classifies as Capture).
            {
                List<(UnitLevel Level, HexCoord Coord)> tiers =
                    AiCommon.MovableUnitTiersWeakestFirst(t, forPlayer, state.Grid);
                AiAction? p1 = TryTiersWeakestFirst("p1",
                    tiers.Select(tier => tier.Level),
                    level =>
                    {
                        HexCoord unitCoord = tiers.First(tier => tier.Level == level).Coord;
                        Unit unit = state.Grid.Get(unitCoord)!.Unit!;
                        IEnumerable<AiCandidate> candidates =
                            AiCommon.EnumeratePhase1ForUnit(unitCoord, unit, t, state, tileIndex);
                        return forPlayer.IsNone
                            ? candidates.Where(c => c.Kind == AiActionKind.Capture)
                            : candidates;
                    },
                    methodStart, baseScore, forPlayer, state, prof);
                if (p1 != null) { _ = rng; return p1; }
            }

            // Phases 2a–4b run only for real players. 2a–4a are the economy:
            // combines and gold spends — vikings have neither (no combining,
            // no capital → no treasury). 4b is defense — vikings are a pure
            // raiding force and never make a defensive-only move: a landed
            // raider with no capture holds.
            if (!forPlayer.IsNone)
            {
                // Shared per-scan memo for the phase-2 target queries —
                // ValidTargets depends only on (level, territory), so one
                // instance serves every unit × level pair below. Must not
                // outlive this territory scan (#150).
                var targetCache = new AiTargetCache(t, state, tileIndex);

                // Phase 2a: combine-to-unlock (existing units)
                foreach (HexCoord unitCoord in MovementRules.MovableUnitsInPowerOrder(t, forPlayer, state.Grid))
                {
                    Unit unit = state.Grid.Get(unitCoord)!.Unit!;
                    // never decline an unlock-combine for the status quo
                    AiAction? p2a = TryPhase("p2a", AiCommon.EnumeratePhase2aForUnit(unitCoord, unit, t, state, tileIndex, targetCache),
                        int.MinValue, methodStart, baseScore, forPlayer, state, prof);
                    if (p2a != null) { _ = rng; return p2a; }
                }

                // Phase 2b: buy-and-combine-to-unlock — never declined for
                // the status quo (solvency lives in the enumerator's gates)
                {
                    AiAction? p2b = TryPhase("p2b", AiCommon.EnumeratePhase2b(t, state, tileIndex, targetCache),
                        int.MinValue, methodStart, baseScore, forPlayer, state, prof);
                    if (p2b != null) { _ = rng; return p2b; }
                }

                // Phase 3: buy-to-capture / buy-to-chop — cheapest tier
                // first, committing to the first tier with any candidate;
                // never declined for the status quo (solvency lives in the
                // enumerator's gates)
                {
                    AiAction? p3 = TryTiersWeakestFirst("p3",
                        new[] { UnitLevel.Recruit, UnitLevel.Soldier, UnitLevel.Captain, UnitLevel.Commander },
                        level => AiCommon.EnumeratePhase3ForLevel(t, state, level, tileIndex),
                        methodStart, baseScore, forPlayer, state, prof);
                    if (p3 != null) { _ = rng; return p3; }
                }

                // Phase 4a: tower placement (optional — doing nothing is valid)
                {
                    AiAction? p4a = TryPhase("p4a", AiCommon.EnumeratePhase4Towers(t, state),
                        0, methodStart, baseScore, forPlayer, state, prof);
                    if (p4a != null) { _ = rng; return p4a; }
                }

                // Phase 4b: defensive repositions (optional — doing nothing is valid)
                foreach (HexCoord unitCoord in MovementRules.MovableUnitsInPowerOrder(t, forPlayer, state.Grid))
                {
                    // Loop guard: a unit the caller already repositioned
                    // this turn is settled — never re-reposition it.
                    if (repositionedUnits.Contains(unitCoord)) continue;
                    Unit unit = state.Grid.Get(unitCoord)!.Unit!;
                    AiAction? p4b = TryPhase("p4b", AiCommon.EnumeratePhase4bForUnit(unitCoord, unit, t, state, tileIndex),
                        0, methodStart, baseScore, forPlayer, state, prof);
                    if (p4b != null) { _ = rng; return p4b; }
                }
            }
            else
            {
                Log.Debug(Log.LogCategory.Ai,
                    $"[viking-hold] territory={anchor} — no capture, holding (phase 4b skipped)");
            }

            // All phases exhausted for this territory.
            if (prof.TotalCandidates > 0)
            {
                Log.Debug(Log.LogCategory.Ai,
                    $"[heuristic] {forPlayer} territory={anchor} has {prof.TotalCandidates} candidates " +
                    $"({prof.PositiveCandidates} positive); best delta = " +
                    $"{prof.ObservedBestDelta:+#;-#;0} ({prof.ObservedBestKind?.ToString() ?? "?"})");
            }
            visitedCapitals.Add(anchor);
        }

        _ = rng;
        EmitProfile(methodStart, prof.CloneTicks, prof.ApplyTicks, prof.ScoreTicks, prof.TotalCandidates);
        return null;
    }

    /// <summary>
    /// The shared weakest-first tier loop behind both offensive phases
    /// (1 free captures, 3 buy-to-capture). Walks
    /// <paramref name="tiersAscending"/> weakest→strongest and commits to
    /// the best candidate of the first tier that yields any (threshold
    /// <see cref="int.MinValue"/> — an offensive action is never declined
    /// for the status quo, so "tier returned null" means "tier has zero
    /// candidates"). A dry tier is skipped for the rest of this call; the
    /// driver's re-entry after each applied action is what restarts the
    /// scan from the weakest tier once the board changes.
    /// </summary>
    private static AiAction? TryTiersWeakestFirst(
        string phase,
        IEnumerable<UnitLevel> tiersAscending,
        Func<UnitLevel, IEnumerable<AiCandidate>> enumerateTier,
        long methodStart, int baseScore, PlayerId forPlayer, GameState state, AiSearchProfile prof)
    {
        foreach (UnitLevel level in tiersAscending)
        {
            Log.Debug(Log.LogCategory.Ai, $"[tier] {phase} level={level} scanning");
            AiAction? action = TryPhase(phase, enumerateTier(level),
                int.MinValue, methodStart, baseScore, forPlayer, state, prof);
            if (action != null) return action;
            Log.Debug(Log.LogCategory.Ai,
                $"[tier-skip] {phase} level={level} no targets — advancing");
        }
        return null;
    }

    /// <summary>Run one phase's candidate search and, when it yields an action,
    /// emit the per-action profiling line before returning it. Returns null when
    /// the phase produced no winning candidate. Accumulates counters into
    /// <paramref name="prof"/> (shared across all phases of this call).</summary>
    private static AiAction? TryPhase(
        string phase, IEnumerable<AiCandidate> candidates, int threshold,
        long methodStart, int baseScore, PlayerId forPlayer, GameState state, AiSearchProfile prof)
    {
        AiAction? action = BestPositiveDelta(phase, candidates, threshold, baseScore, forPlayer, state, prof);
        if (action != null)
        {
            Log.Debug(Log.LogCategory.Ai,
                $"[chose] {forPlayer} phase={phase} kind={prof.ChosenKind} {action} delta={prof.ChosenDelta}");
            EmitProfile(methodStart, prof.CloneTicks, prof.ApplyTicks, prof.ScoreTicks, prof.TotalCandidates);
        }
        return action;
    }

    /// <summary>Mutable per-<see cref="ChooseNextAction"/> search accumulators —
    /// timing buckets, candidate counts, and the best observed delta — passed
    /// through every phase so they accumulate across the whole call.</summary>
    private sealed class AiSearchProfile
    {
        public long CloneTicks;
        public long ApplyTicks;
        public long ScoreTicks;
        public int TotalCandidates;
        public int PositiveCandidates;
        public int ObservedBestDelta = int.MinValue;
        public AiActionKind? ObservedBestKind;
        // Winning candidate of the phase that returns an action — the last
        // accepted candidate is by construction the one TryPhase returns.
        public AiActionKind? ChosenKind;
        public int ChosenDelta;
    }

    /// <summary>
    /// Score every candidate and return the action with the best delta
    /// that strictly exceeds <paramref name="threshold"/>, or null if
    /// none does. Pass <c>0</c> for the strictly-positive gate (a
    /// candidate must improve the score to win) — used by the
    /// defense-only phases 4a/4b, where doing nothing is valid; pass
    /// <see cref="int.MinValue"/> to force the best legal candidate
    /// regardless of sign — used by phases 1, 2a, 2b, and 3 so an
    /// offensive / unlock action is never declined in favor of the
    /// status quo.
    /// Ties resolve to the first-yielded candidate (preserving
    /// power-then-coord order). Accumulates profiling counters in the
    /// caller's locals via ref.
    /// </summary>
    private static AiAction? BestPositiveDelta(
        string phase,
        IEnumerable<AiCandidate> candidates,
        int threshold,
        int baseScore,
        PlayerId forPlayer,
        GameState state,
        AiSearchProfile prof)
    {
        AiAction? best = null;
        int bestDelta = threshold;

        foreach (AiCandidate candidate in candidates)
        {
            prof.TotalCandidates++;

            long cloneT = Log.Stamp();
            GameState clone = AiSimulator.Clone(state);
            prof.CloneTicks += Log.Stamp() - cloneT;

            long applyT = Log.Stamp();
            AiSimulator.Apply(candidate.Action, clone);
            prof.ApplyTicks += Log.Stamp() - applyT;

            long scoreT = Log.Stamp();
            int afterScore = AiStateScorer.Score(clone, forPlayer);
            prof.ScoreTicks += Log.Stamp() - scoreT;
            int delta = afterScore - baseScore;

            if (candidate.Action is AiBuildTowerAction bt)
                delta += AiStateScorer.BuildTowerBonus(bt.Destination, state, forPlayer);
            // Rising Tides: reward moving a unit off a tile that will
            // submerge this turn (and never onto one), so the defensive phase
            // evacuates a unit that would otherwise sit still and drown.
            if (candidate.Action is AiMoveAction mv)
                delta += AiStateScorer.EvacuationBonus(mv, state, forPlayer);

            if (delta > 0) prof.PositiveCandidates++;
            if (delta > prof.ObservedBestDelta)
            {
                prof.ObservedBestDelta = delta;
                prof.ObservedBestKind = candidate.Kind;
            }
            bool accepted = delta > bestDelta;
            // Per-candidate verdict — the ground truth for "why did the
            // AI decline this action". A candidate is rejected either by
            // the phase threshold (strictly-positive gate for the defense
            // phases) or by a better sibling in the same phase. Trace,
            // not Debug: one line per scored candidate is a firehose
            // that would bloat FOUREXHEX_6AI logs (which pin Ai:Debug);
            // opt in with FOUREXHEX_LOG="Ai:Trace". The per-territory
            // [heuristic] summary remains the Debug-level view.
            Log.Trace(Log.LogCategory.Ai,
                $"[candidate] {phase} {candidate.Kind} {candidate.Action} " +
                $"delta={delta} threshold={threshold} → " +
                (accepted ? "best-so-far" : "rejected"));
            if (accepted)
            {
                bestDelta = delta;
                best = candidate.Action;
                prof.ChosenKind = candidate.Kind;
                prof.ChosenDelta = delta;
            }
        }

        return best;
    }

    private static void EmitProfile(
        long methodStart, long cloneTicks, long applyTicks, long scoreTicks, int totalCandidates)
    {
        long totalTicks = Log.Stamp() - methodStart;
        long ToMs(long t) => t * 1000L / Stopwatch.Frequency;
        long otherTicks = totalTicks - cloneTicks - applyTicks - scoreTicks;
        Log.Info(Log.LogCategory.Ai,
            $"[ai-prof] cand={totalCandidates} " +
            $"clone={ToMs(cloneTicks)}ms " +
            $"apply={ToMs(applyTicks)}ms " +
            $"score={ToMs(scoreTicks)}ms " +
            $"other={ToMs(otherTicks)}ms " +
            $"total={ToMs(totalTicks)}ms");
    }
}
