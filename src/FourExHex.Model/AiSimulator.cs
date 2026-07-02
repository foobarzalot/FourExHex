using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Pure-logic simulator for AI action scoring. Given a current
/// <see cref="GameState"/> and a legal <see cref="AiAction"/>, it can
/// either clone the state before applying (so the caller gets a
/// fresh state to score without disturbing the real one) or apply
/// in place (used by the clone path itself).
///
/// The bare mutations are <see cref="AiActionCore"/>, shared with the
/// live <c>GameOperations.ExecuteAi*</c> paths so simulated futures
/// match what the real harness produces. The simulator's own envelope
/// is thin: Recompute-only capture reconciliation
/// (<see cref="Reconcile"/>) and unconditional reposition-marking.
/// Skips the paranoid validation — actions passed here are expected to
/// come from <see cref="AiCommon.Enumerate"/>, which only emits legal
/// ones.
/// </summary>
public static class AiSimulator
{
    /// <summary>
    /// Deep-clone <paramref name="original"/> so it can be mutated
    /// without affecting the live game. Players and TurnState are
    /// shared (they're not mutated during a single AI action); the
    /// grid, treasury, and territory list are independent copies.
    /// </summary>
    public static GameState Clone(GameState original)
    {
        // Build a fresh grid with shell tiles at every coord in the
        // original. GameStateSnapshot.ApplyTo walks the snapshot's
        // coords and mutates grid.Get(coord) in place, so the new
        // grid needs matching coord entries before the apply.
        var newGrid = new HexGrid();
        foreach (HexTile tile in original.Grid.Tiles)
        {
            newGrid.Add(new HexTile(tile.Coord, tile.Owner));
        }

        var newTreasury = new Treasury();

        GameStateSnapshot snap = GameStateSnapshot.Capture(
            original.Grid, original.Treasury, original.Territories);
        IReadOnlyList<Territory> territories = snap.ApplyTo(newGrid, newTreasury);

        // Mode is intentionally left at its default here (the simulator never
        // runs tide logic); the randomized-selection flag must ride along so a
        // simulated capture picks the same replacement capital as real play.
        return new GameState(
            newGrid,
            territories,
            original.Players,
            original.Turns,
            newTreasury,
            original.WaterCoords,
            useRandomizedSelection: original.UseRandomizedSelection);
    }

    /// <summary>
    /// Apply <paramref name="action"/> to <paramref name="state"/>
    /// in place. For captures this rebuilds the territory list via
    /// <see cref="TerritoryFinder"/> + <see cref="CapitalReconciler"/>
    /// and reconciles the treasury so splits/merges / bankrupt
    /// sub-territories surface in the resulting state.
    /// </summary>
    public static void Apply(AiAction action, GameState state)
    {
        switch (action)
        {
            case AiMoveAction mv:
                ApplyMove(mv.Source, mv.Destination, state);
                break;
            case AiBuyUnitAction bu:
                ApplyBuy(bu.Capital, bu.Destination, bu.Level, state);
                break;
            case AiBuildTowerAction bt:
                ApplyBuildTower(bt.Capital, bt.Destination, state);
                break;
            case AiBuyCombineAction bc:
                ApplyBuyCombine(bc.Capital, bc.CombineTarget, bc.BuyLevel, state);
                break;
            default:
                // Rally / ClaimVictory / DismissClaim / DismissDefeat are
                // replay-script-only actions emitted by ReplayDrivenAi,
                // not by AiCommon.Enumerate. Scoring a future built on
                // them would silently disagree with live play, so any
                // attempt to simulate one is a programmer error.
                throw new NotSupportedException(
                    $"AiSimulator.Apply does not model {action.GetType().Name}; " +
                    "this kind is only produced by ReplayDrivenAi and never reaches simulation.");
        }
    }

    private static void ApplyMove(HexCoord source, HexCoord destination, GameState state)
    {
        HexTile? srcTile = state.Grid.Get(source);
        if (srcTile == null) return;
        Territory? attacker = TerritoryLookup.FindOwnedContaining(
            state.Territories, srcTile.Owner, source);
        if (attacker == null) return;

        AiApplyResult r = AiActionCore.Move(source, destination, state, attacker);
        if (r.Move.WasCapture)
        {
            Reconcile(state);
        }

        if (r.WasReposition)
        {
            // Unconditional mark — the simulator only ever runs for
            // Computer actors, so no live-play kind-gate is needed here.
            AiActionCore.MarkUnitMoved(destination, state);
        }
    }

    private static void ApplyBuy(HexCoord capital, HexCoord destination, UnitLevel level, GameState state)
    {
        Territory? territory = TerritoryLookup.FindByCapital(state.Territories, capital);
        if (territory == null) return;

        AiApplyResult r = AiActionCore.Buy(capital, destination, level, state, territory);
        if (r.Move.WasCapture)
        {
            Reconcile(state);
        }

        if (r.WasReposition)
        {
            AiActionCore.MarkUnitMoved(destination, state);
        }
    }

    private static void ApplyBuyCombine(HexCoord capital, HexCoord combineTarget, UnitLevel level, GameState state)
    {
        Territory? territory = TerritoryLookup.FindByCapital(state.Territories, capital);
        if (territory == null) return;
        // The combined unit inherits the dest unit's HasMovedThisTurn=false,
        // so no MarkUnitMoved — the combined unit remains actionable for
        // a subsequent phase-1 capture.
        AiActionCore.BuyCombine(capital, combineTarget, level, state, territory);
    }

    private static void ApplyBuildTower(HexCoord capital, HexCoord destination, GameState state)
    {
        Territory? territory = TerritoryLookup.FindByCapital(state.Territories, capital);
        if (territory == null) return;
        if (state.Grid.Get(destination) == null) return;

        AiActionCore.BuildTower(capital, destination, state, territory);
    }

    /// <summary>
    /// Rebuild territories and reconcile the treasury after a
    /// capture. The same <see cref="TerritoryFinder.Recompute"/> call
    /// sits at the heart of <c>GameOperations.HandleCapture</c>, so
    /// simulated and real captures produce identical states; the live
    /// path merely layers view/defeat/win effects on top.
    /// </summary>
    private static void Reconcile(GameState state)
    {
        state.Territories = TerritoryFinder.Recompute(
            state.Grid, state.Territories, state.Treasury, state.UseRandomizedSelection);
    }

}
