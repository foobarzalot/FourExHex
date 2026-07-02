/// <summary>
/// One applied AI action's outcome: the <see cref="MovementRules"/>
/// result plus the two pre-move flags the live envelope needs. Both
/// flags are computed BEFORE the mutation — a reposition target is
/// same-owner-and-empty, and a combine target holds a friendly unit,
/// only until the arriving unit lands.
/// </summary>
public readonly record struct AiApplyResult(MoveResult Move, bool WasReposition, bool WasCombine);

/// <summary>
/// The bare mutation core for AI actions — the single source of truth
/// shared by <see cref="AiSimulator"/> (1-ply lookahead scoring) and
/// <c>GameOperations.ExecuteAi*</c> (live play + replay), so simulated
/// futures and real play cannot drift.
///
/// Deliberately does ONLY the mutation: reposition/combine detection,
/// gold deduction (owner-based difficulty via
/// <see cref="GameState.DifficultyOf"/>), and
/// <see cref="MovementRules"/> placement. Everything caller-specific
/// stays with the caller: legality validation (the simulator trusts
/// <see cref="AiCommon.Enumerate"/> and early-returns; GameOperations
/// throws), capture reconciliation (Recompute-only vs the full
/// <c>HandleCapture</c> envelope), the reposition-mark gate
/// (unconditional in simulation; live play skips non-Computer actors so
/// a replayed human reposition stays actionable), and all view effects.
/// </summary>
public static class AiActionCore
{
    /// <summary>Move the unit at <paramref name="source"/> to
    /// <paramref name="destination"/>. Caller resolves and validates
    /// <paramref name="attacker"/> (the source's owning territory).</summary>
    public static AiApplyResult Move(
        HexCoord source, HexCoord destination, GameState state, Territory attacker)
    {
        Log.Trace(Log.LogCategory.Ai, $"[core] move {source}→{destination}");
        bool wasReposition = IsRepositionTarget(destination, attacker.Owner, state);
        bool wasCombine = IsFriendlyUnitAt(destination, attacker.Owner, state);
        MoveResult result = MovementRules.Move(source, destination, state.Grid, attacker);
        return new AiApplyResult(result, wasReposition, wasCombine);
    }

    /// <summary>Buy a <paramref name="level"/> unit from
    /// <paramref name="capital"/>'s treasury and place it at
    /// <paramref name="destination"/>. Caller resolves and validates
    /// <paramref name="territory"/> (the capital's territory).</summary>
    public static AiApplyResult Buy(
        HexCoord capital, HexCoord destination, UnitLevel level,
        GameState state, Territory territory)
    {
        Log.Trace(Log.LogCategory.Ai, $"[core] buy {level}@{capital}→{destination}");
        bool wasReposition = IsRepositionTarget(destination, territory.Owner, state);
        bool wasCombine = IsFriendlyUnitAt(destination, territory.Owner, state);
        DeductGold(capital, PurchaseRules.CostFor(level, state.DifficultyOf(territory.Owner)), state);
        var unit = new Unit(territory.Owner, level);
        MoveResult result = MovementRules.PlaceNew(unit, destination, state.Grid, territory);
        return new AiApplyResult(result, wasReposition, wasCombine);
    }

    /// <summary>Buy a <paramref name="level"/> unit and combine it onto
    /// the friendly unit at <paramref name="combineTarget"/> (PlaceNew
    /// onto a friendly unit performs the combine). Never a reposition;
    /// the combined unit inherits the target's
    /// <c>HasMovedThisTurn=false</c> so it stays actionable.</summary>
    public static MoveResult BuyCombine(
        HexCoord capital, HexCoord combineTarget, UnitLevel level,
        GameState state, Territory territory)
    {
        Log.Trace(Log.LogCategory.Ai, $"[core] buy-combine {level}@{capital}→{combineTarget}");
        DeductGold(capital, PurchaseRules.CostFor(level, state.DifficultyOf(territory.Owner)), state);
        var unit = new Unit(territory.Owner, level);
        return MovementRules.PlaceNew(unit, combineTarget, state.Grid, territory);
    }

    /// <summary>Buy a tower from <paramref name="capital"/>'s treasury
    /// and drop it at <paramref name="destination"/>. Caller validates
    /// the location (in-territory, on-map, unoccupied).</summary>
    public static void BuildTower(
        HexCoord capital, HexCoord destination, GameState state, Territory territory)
    {
        Log.Trace(Log.LogCategory.Ai, $"[core] tower @{capital}→{destination}");
        DeductGold(capital, PurchaseRules.TowerCostFor(state.DifficultyOf(territory.Owner)), state);
        state.Grid.Get(destination)!.Occupant = new Tower();
    }

    /// <summary>
    /// True iff <paramref name="destination"/> is a same-owner empty
    /// tile — the signature of a pure reposition that
    /// <see cref="MovementRules.ResolveArrival"/> leaves unmarked.
    /// Must be evaluated BEFORE the move is applied.
    /// </summary>
    public static bool IsRepositionTarget(HexCoord destination, PlayerId owner, GameState state)
    {
        HexTile? dst = state.Grid.Get(destination);
        return dst != null && dst.Owner == owner && dst.Occupant == null;
    }

    /// <summary>
    /// True iff <paramref name="coord"/>'s tile is owned by
    /// <paramref name="owner"/> AND occupied by a Unit — the destination
    /// state right before a Move/PlaceNew that triggers MovementRules'
    /// combine branch. Must be evaluated BEFORE the move is applied.
    /// </summary>
    public static bool IsFriendlyUnitAt(HexCoord coord, PlayerId owner, GameState state)
    {
        HexTile? tile = state.Grid.Get(coord);
        return tile != null && tile.Owner == owner && tile.Occupant is Unit;
    }

    /// <summary>
    /// AI-side rule: once the AI commits a unit to a reposition, that
    /// unit is done for the turn. The game's underlying movement rule
    /// leaves repositioned units actionable (so a human can
    /// micromanage), but the AI would otherwise re-enumerate the same
    /// unit each call and ping-pong it between border tiles. The gate
    /// deciding WHEN to mark is caller-side: the simulator marks
    /// unconditionally (it only ever simulates Computer actors); live
    /// play marks only for Computer actors so a replayed human
    /// reposition doesn't consume the move.
    /// </summary>
    public static void MarkUnitMoved(HexCoord destination, GameState state)
    {
        Unit? unit = state.Grid.Get(destination)?.Unit;
        if (unit != null) unit.HasMovedThisTurn = true;
    }

    private static void DeductGold(HexCoord capital, int cost, GameState state)
        => state.Treasury.SetGold(capital, state.Treasury.GetGold(capital) - cost);
}
