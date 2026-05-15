using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Pure-logic simulator for AI action scoring. Given a current
/// <see cref="GameState"/> and a legal <see cref="AiAction"/>, it can
/// either clone the state before applying (so the caller gets a
/// fresh state to score without disturbing the real one) or apply
/// in place (used by the clone path itself).
///
/// Mirrors the mutation logic in <see cref="GameController"/>'s
/// <c>ExecuteAiMove</c> / <c>ExecuteAiBuyUnit</c> /
/// <c>ExecuteAiBuildTower</c> methods so simulated futures match
/// what the real harness would produce. Skips the paranoid
/// validation — actions passed here are expected to come from
/// <see cref="AiCommon.Enumerate"/>, which only emits legal ones.
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
            newGrid.Add(new HexTile(tile.Coord, tile.Color));
        }

        var newTreasury = new Treasury();

        GameStateSnapshot snap = GameStateSnapshot.Capture(
            original.Grid, original.Treasury, original.Territories);
        IReadOnlyList<Territory> territories = snap.ApplyTo(newGrid, newTreasury);

        return new GameState(
            newGrid,
            territories,
            original.Players,
            original.Turns,
            newTreasury,
            original.WaterCoords);
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
            state.Territories, srcTile.Color, source);
        if (attacker == null) return;

        bool wasReposition = IsRepositionTarget(destination, attacker.Owner, state);

        MoveResult result = MovementRules.Move(source, destination, state.Grid, attacker);
        if (result.WasCapture)
        {
            Reconcile(state);
        }

        if (wasReposition)
        {
            MarkAiUnitMoved(destination, state);
        }
    }

    private static void ApplyBuy(HexCoord capital, HexCoord destination, UnitLevel level, GameState state)
    {
        Territory? territory = TerritoryLookup.FindByCapital(state.Territories, capital);
        if (territory == null) return;

        bool wasReposition = IsRepositionTarget(destination, territory.Owner, state);

        state.Treasury.SetGold(
            capital, state.Treasury.GetGold(capital) - PurchaseRules.CostFor(level));
        var unit = new Unit(territory.Owner, level);
        MoveResult result = MovementRules.PlaceNew(unit, destination, state.Grid, territory);
        if (result.WasCapture)
        {
            Reconcile(state);
        }

        if (wasReposition)
        {
            MarkAiUnitMoved(destination, state);
        }
    }

    /// <summary>
    /// True iff <paramref name="destination"/> is a same-owner empty
    /// tile — the signature of a pure reposition that
    /// <see cref="MovementRules.ResolveArrival"/> leaves unmarked.
    /// Must be evaluated BEFORE the move is applied.
    /// </summary>
    private static bool IsRepositionTarget(HexCoord destination, Color owner, GameState state)
    {
        HexTile? dst = state.Grid.Get(destination);
        return dst != null && dst.Color == owner && dst.Occupant == null;
    }

    /// <summary>
    /// AI-side rule: once the AI commits a unit to a reposition,
    /// that unit is done for the turn. The game's underlying
    /// movement rule leaves repositioned units actionable (so a
    /// human can micromanage), but the AI would otherwise re-enumerate
    /// the same unit each call and ping-pong it between border
    /// tiles. <see cref="GameController.ExecuteAiMove"/> mirrors this
    /// so the live state matches what the simulator predicts.
    /// </summary>
    private static void MarkAiUnitMoved(HexCoord destination, GameState state)
    {
        Unit? unit = state.Grid.Get(destination)?.Unit;
        if (unit != null) unit.HasMovedThisTurn = true;
    }

    private static void ApplyBuildTower(HexCoord capital, HexCoord destination, GameState state)
    {
        Territory? territory = TerritoryLookup.FindByCapital(state.Territories, capital);
        if (territory == null) return;
        HexTile? dst = state.Grid.Get(destination);
        if (dst == null) return;

        state.Treasury.SetGold(
            capital, state.Treasury.GetGold(capital) - PurchaseRules.TowerCost);
        dst.Occupant = new Tower();
    }

    /// <summary>
    /// Rebuild territories and reconcile the treasury after a
    /// capture. Matches <c>GameController.HandleCapture</c> exactly
    /// so simulated and real captures produce identical states.
    /// </summary>
    private static void Reconcile(GameState state)
    {
        state.Territories = TerritoryFinder.Recompute(
            state.Grid, state.Territories, state.Treasury);
    }

}
