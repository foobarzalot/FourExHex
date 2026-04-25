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
            newTreasury);
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
        }
    }

    private static void ApplyMove(HexCoord source, HexCoord destination, GameState state)
    {
        Territory? attacker = FindOwnedTerritoryContaining(source, state);
        if (attacker == null) return;

        MoveResult result = MovementRules.Move(source, destination, state.Grid, attacker);
        if (result.WasCapture)
        {
            Reconcile(state);
        }
    }

    private static void ApplyBuy(HexCoord capital, HexCoord destination, UnitLevel level, GameState state)
    {
        Territory? territory = FindByCapital(capital, state);
        if (territory == null) return;

        state.Treasury.SetGold(
            capital, state.Treasury.GetGold(capital) - PurchaseRules.CostFor(level));
        var unit = new Unit(territory.Owner, level);
        MoveResult result = MovementRules.PlaceNew(unit, destination, state.Grid, territory);
        if (result.WasCapture)
        {
            Reconcile(state);
        }
    }

    private static void ApplyBuildTower(HexCoord capital, HexCoord destination, GameState state)
    {
        Territory? territory = FindByCapital(capital, state);
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
        IReadOnlyList<Territory> raw = TerritoryFinder.FindAll(state.Grid);
        IReadOnlyList<Territory> reconciled = CapitalReconciler.Reconcile(
            raw, state.Territories, state.Grid);
        state.Treasury.ReconcileAfterCapture(state.Territories, reconciled);
        state.Territories = reconciled;
    }

    private static Territory? FindOwnedTerritoryContaining(HexCoord coord, GameState state)
    {
        HexTile? tile = state.Grid.Get(coord);
        if (tile == null) return null;
        Color color = tile.Color;
        foreach (Territory t in state.Territories)
        {
            if (t.Owner == color && t.Coords.Contains(coord))
            {
                return t;
            }
        }
        return null;
    }

    private static Territory? FindByCapital(HexCoord capital, GameState state)
    {
        foreach (Territory t in state.Territories)
        {
            if (t.HasCapital && t.Capital!.Value == capital)
            {
                return t;
            }
        }
        return null;
    }
}
