using System;
using System.Collections.Generic;

/// <summary>
/// Controller-owned AI decision state wrapped around an inner chooser
/// (<see cref="ComputerAi.ChooseNextAction"/> or a test stub). Sits at
/// the chooser boundary so every execution track — AI turns and the
/// human Automate loop, paced or instant — gets the same behavior with
/// one implementation. Two jobs:
///
/// 1. <b>Make-way lowering.</b> The chooser may return an
///    <see cref="AiBuildTowerAction"/> whose destination holds an own
///    unmoved unit — an <i>intent</i>, never directly executable (the
///    tower rule is universal: no actor may build on an occupied tile,
///    enforced by <c>GameOperations.ExecuteAiBuildTower</c>). The
///    wrapper lowers it into two discrete actions: this call returns
///    the reposition to <see cref="PurchaseRules.TowerPushDestination"/>;
///    the build is stashed and returned by the next call. Each action
///    executes as a first-class beat — its own undo entry during
///    automation, its own replay beat — and the reposition leaves the
///    unit's move intact (repositions never consume
///    <see cref="Unit.HasMovedThisTurn"/>).
///
/// 2. <b>Reposition loop guard.</b> Coords of units this wrapper has
///    repositioned in the current (turn, player) scope, passed into the
///    inner chooser so phase 4b skips them. This is the AI's
///    "already visited this unit" bookkeeping — pure decision state, so
///    it lives here rather than on the model's real
///    movement-consumption flag. Make-way repositions are deliberately
///    NOT recorded: the pushed unit stays eligible for a later
///    defensive reposition.
///
/// State self-resets whenever the (turn number, player) key changes;
/// <see cref="Reset"/> exists for same-turn restarts (a fresh Automate
/// press after an interruption or undo). The stashed build is
/// re-validated before redemption and silently dropped when stale
/// (undo restored the unit, gold was spent, territory changed) — the
/// inner chooser then simply re-decides from the live state.
/// </summary>
public sealed class AiActionLowering
{
    private readonly Func<GameState, PlayerId, HashSet<HexCoord>, HashSet<HexCoord>, Random, AiAction?> _inner;
    private readonly HashSet<HexCoord> _repositionedUnits = new();
    private AiBuildTowerAction? _pendingBuild;
    private int _keyTurn = -1;
    private PlayerId _keyPlayer;

    public AiActionLowering(
        Func<GameState, PlayerId, HashSet<HexCoord>, HashSet<HexCoord>, Random, AiAction?> inner)
    {
        _inner = inner;
    }

    /// <summary>Clear all decision state. Call on same-turn restarts
    /// (e.g. a fresh Automate press); turn/player changes reset
    /// automatically.</summary>
    public void Reset()
    {
        _repositionedUnits.Clear();
        _pendingBuild = null;
        _keyTurn = -1;
    }

    /// <summary>
    /// The outward chooser: same shape the AI driver and Automate loop
    /// already consume. Wraps <see cref="_inner"/> with the lowering and
    /// the loop-guard set.
    /// </summary>
    public AiAction? Choose(
        GameState state, PlayerId forPlayer, HashSet<HexCoord> visitedCapitals, Random rng)
    {
        if (_keyTurn != state.Turns.TurnNumber || _keyPlayer != forPlayer)
        {
            Reset();
            _keyTurn = state.Turns.TurnNumber;
            _keyPlayer = forPlayer;
        }

        if (_pendingBuild is AiBuildTowerAction pending)
        {
            _pendingBuild = null;
            if (PendingBuildStillValid(pending, state, forPlayer))
            {
                Log.Debug(Log.LogCategory.Ai,
                    $"[make-way] tower build at {pending.Destination} follows its make-way move");
                return pending;
            }
            Log.Debug(Log.LogCategory.Ai,
                $"[make-way] stale stashed build at {pending.Destination} dropped — re-deciding");
        }

        AiAction? action = _inner(state, forPlayer, visitedCapitals, _repositionedUnits, rng);

        if (action is AiBuildTowerAction bt && TryLowerMakeWay(bt, state, forPlayer, out AiMoveAction makeWay))
        {
            _pendingBuild = bt;
            Log.Debug(Log.LogCategory.Ai,
                $"[make-way] unit at {bt.Destination} steps aside to {makeWay.Destination}; tower next");
            return makeWay;
        }

        if (action is AiMoveAction mv
            && AiActionCore.IsRepositionTarget(mv.Destination, forPlayer, state))
        {
            // Keyed by the unit's post-move coord — that's where the
            // next chooser call will see it. (A different unit landing
            // on this coord later the same turn would be skipped by 4b
            // too; harmless, and the key resets next turn.)
            _repositionedUnits.Add(mv.Destination);
        }

        return action;
    }

    /// <summary>
    /// True iff <paramref name="intent"/> targets a tile the strict rule
    /// rejects but a make-way move legalizes (own unmoved unit with an
    /// escape — <see cref="PurchaseRules.IsValidTowerLocationWithPush"/>
    /// minus the already-strictly-valid case). Any other illegal intent
    /// passes through untouched and crashes in execution — a chooser
    /// bug, not something to paper over here.
    /// </summary>
    private static bool TryLowerMakeWay(
        AiBuildTowerAction intent, GameState state, PlayerId forPlayer, out AiMoveAction makeWay)
    {
        makeWay = default!;
        HexTile? tile = state.Grid.Get(intent.Destination);
        if (tile?.Occupant is not Unit) return false;
        Territory? territory = TerritoryLookup.FindOwnedContaining(
            state.Territories, forPlayer, intent.Destination);
        if (territory == null) return false;
        if (!PurchaseRules.IsValidTowerLocationWithPush(tile, territory, state.Grid)) return false;

        HexCoord escape = PurchaseRules.TowerPushDestination(
            intent.Destination, territory, state.Grid)!.Value;
        makeWay = new AiMoveAction(intent.Destination, escape);
        return true;
    }

    /// <summary>Re-validate a stashed build against the live state: the
    /// capital's territory must still be the player's, the destination
    /// strictly valid (the make-way move vacated it), and the tower
    /// still affordable.</summary>
    private static bool PendingBuildStillValid(
        AiBuildTowerAction pending, GameState state, PlayerId forPlayer)
    {
        Territory? territory = TerritoryLookup.FindByCapital(state.Territories, pending.Capital);
        if (territory == null || territory.Owner != forPlayer) return false;
        HexTile? dst = state.Grid.Get(pending.Destination);
        if (dst == null) return false;
        if (!PurchaseRules.IsValidTowerLocation(dst, territory, state.Grid)) return false;
        return PurchaseRules.CanAffordTower(
            territory, state.Treasury, state.DifficultyOf(forPlayer));
    }
}
