using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Pins the controller-side chooser wrapper: make-way lowering of tower
/// intents into two discrete actions, stash validation/expiry, and the
/// reposition loop-guard set (controller-owned AI decision state —
/// repositions never touch <see cref="Unit.HasMovedThisTurn"/>).
/// </summary>
public class AiActionLoweringTests
{
    private static readonly PlayerId Red = PlayerId.FromIndex(0);
    private static readonly PlayerId Blue = PlayerId.FromIndex(1);

    /// <summary>
    /// 5x1 strip: Red owns cols 0..3 with capital at lex-min (0,0),
    /// col 4 Blue. A free Red recruit sits on (1,0); its deterministic
    /// escape is (2,0) — (0,0) holds the Capital occupant.
    /// </summary>
    private static (GameState state, HexCoord cap, HexCoord unitTile, HexCoord escape) BuildStrip()
    {
        var red = new Player("Red", Red, PlayerKind.Computer);
        var blue = new Player("Blue", Blue);
        var players = new List<Player> { red, blue };
        HexGrid grid = TestHelpers.BuildRectGrid(5, 1, Blue);
        for (int col = 0; col <= 3; col++)
            grid.Get(HexCoord.FromOffset(col, 0))!.Owner = Red;
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        HexCoord unitTile = HexCoord.FromOffset(1, 0);
        grid.Get(unitTile)!.Occupant = new Unit(Red);
        HexCoord cap = state.Territories.First(t => t.Owner == Red).Capital!.Value;
        state.Treasury.SetGold(cap, 20);
        return (state, cap, unitTile, HexCoord.FromOffset(2, 0));
    }

    /// <summary>Inner chooser returning scripted actions one per call
    /// (then null), recording the loop-guard set it was handed.</summary>
    private static (Func<GameState, PlayerId, HashSet<HexCoord>, HashSet<HexCoord>, Random, AiAction?> inner,
        List<HashSet<HexCoord>> seenSets) Script(params AiAction?[] actions)
    {
        int index = 0;
        var seen = new List<HashSet<HexCoord>>();
        return ((s, p, visited, repositioned, rng) =>
        {
            seen.Add(new HashSet<HexCoord>(repositioned));
            return index >= actions.Length ? null : actions[index++];
        }, seen);
    }

    private static readonly HashSet<HexCoord> NoVisited = new();

    [Fact]
    public void TowerIntentOnFreeUnitTile_LowersToMakeWayMove_ThenBuild()
    {
        (GameState state, HexCoord cap, HexCoord unitTile, HexCoord escape) = BuildStrip();
        var intent = new AiBuildTowerAction(cap, unitTile);
        (var inner, _) = Script(intent);
        var lowering = new AiActionLowering(inner);

        AiAction? first = lowering.Choose(state, Red, NoVisited, new Random(0));
        AiMoveAction makeWay = Assert.IsType<AiMoveAction>(first);
        Assert.Equal(unitTile, makeWay.Source);
        Assert.Equal(escape, makeWay.Destination);

        // Apply the make-way move as execution would, then redeem.
        state.Grid.Get(escape)!.Occupant = state.Grid.Get(unitTile)!.Occupant;
        state.Grid.Get(unitTile)!.Occupant = null;
        AiAction? second = lowering.Choose(state, Red, NoVisited, new Random(0));
        Assert.Same(intent, second);
    }

    [Fact]
    public void StaleStash_DestinationStillOccupied_IsDroppedAndInnerReconsults()
    {
        // The make-way move never landed (interruption/undo restored the
        // unit) — the stash fails re-validation and the inner chooser
        // decides fresh.
        (GameState state, HexCoord cap, HexCoord unitTile, _) = BuildStrip();
        var intent = new AiBuildTowerAction(cap, unitTile);
        var fallback = new AiMoveAction(unitTile, HexCoord.FromOffset(2, 0));
        (var inner, var seen) = Script(intent, fallback);
        var lowering = new AiActionLowering(inner);

        Assert.IsType<AiMoveAction>(lowering.Choose(state, Red, NoVisited, new Random(0)));
        // Unit still on unitTile (move never executed) → stash invalid.
        AiAction? second = lowering.Choose(state, Red, NoVisited, new Random(0));
        Assert.Same(fallback, second);
        Assert.Equal(2, seen.Count); // inner consulted both times
    }

    [Fact]
    public void StaleStash_TowerNoLongerAffordable_IsDropped()
    {
        (GameState state, HexCoord cap, HexCoord unitTile, HexCoord escape) = BuildStrip();
        var intent = new AiBuildTowerAction(cap, unitTile);
        (var inner, _) = Script(intent);
        var lowering = new AiActionLowering(inner);

        lowering.Choose(state, Red, NoVisited, new Random(0));
        state.Grid.Get(escape)!.Occupant = state.Grid.Get(unitTile)!.Occupant;
        state.Grid.Get(unitTile)!.Occupant = null;
        state.Treasury.SetGold(cap, 0); // gold gone between beats

        Assert.Null(lowering.Choose(state, Red, NoVisited, new Random(0)));
    }

    [Fact]
    public void OrdinaryReposition_EntersLoopGuard_MakeWayDoesNot()
    {
        (GameState state, HexCoord cap, HexCoord unitTile, HexCoord escape) = BuildStrip();
        // Script: an ordinary reposition (3,0)→(2,0)? — use the empty
        // in-territory tile (3,0) as destination of a reposition, then
        // a make-way pair, then a probe call.
        var reposition = new AiMoveAction(unitTile, HexCoord.FromOffset(3, 0));
        var intent = new AiBuildTowerAction(cap, unitTile);
        (var inner, var seen) = Script(reposition, intent);
        var lowering = new AiActionLowering(inner);

        // 1. Ordinary reposition → destination coord enters the guard.
        Assert.Same(reposition, lowering.Choose(state, Red, NoVisited, new Random(0)));
        state.Grid.Get(HexCoord.FromOffset(3, 0))!.Occupant = state.Grid.Get(unitTile)!.Occupant;
        state.Grid.Get(unitTile)!.Occupant = null;

        // 2. Make-way lowering for a fresh unit back on unitTile.
        state.Grid.Get(unitTile)!.Occupant = new Unit(Red);
        AiMoveAction makeWay = Assert.IsType<AiMoveAction>(
            lowering.Choose(state, Red, NoVisited, new Random(0)));
        Assert.Equal(escape, makeWay.Destination);

        // Inner saw the guard grow only from the ordinary reposition:
        // call 2's set contains (3,0) but never the make-way escape.
        Assert.Contains(HexCoord.FromOffset(3, 0), seen[1]);
        Assert.DoesNotContain(escape, seen[1]);
    }

    [Fact]
    public void LoopGuard_ResetsWhenActorChanges()
    {
        (GameState state, _, HexCoord unitTile, _) = BuildStrip();
        var reposition = new AiMoveAction(unitTile, HexCoord.FromOffset(3, 0));
        (var inner, var seen) = Script(reposition, null, null);
        var lowering = new AiActionLowering(inner);

        lowering.Choose(state, Red, NoVisited, new Random(0));
        lowering.Choose(state, Red, NoVisited, new Random(0));
        Assert.Contains(HexCoord.FromOffset(3, 0), seen[1]);

        // Different player → key change → guard cleared.
        lowering.Choose(state, Blue, NoVisited, new Random(0));
        Assert.Empty(seen[2]);
    }

    [Fact]
    public void ChooseNextAction_SkipsPhase4bForUnitsInLoopGuard()
    {
        // Red strip with the only border tile (5,0) empty and a unit at
        // (3,0) — far enough that its defense doesn't already radiate
        // onto the border, so repositioning there scores positive. The
        // Blue guard prevents any capture and 0 gold blocks the spend
        // phases, so 4b's reposition to the border is the only
        // candidate. With the unit's coord in the loop-guard set, the
        // chooser must return null instead of re-repositioning it.
        var grid = new HexGrid();
        for (int col = 0; col <= 5; col++)
            grid.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(6, 0), Blue));
        grid.Get(HexCoord.FromOffset(3, 0))!.Occupant = new Unit(Red);
        grid.Get(HexCoord.FromOffset(6, 0))!.Occupant = new Unit(Blue, UnitLevel.Soldier);
        var red = new Player("Red", Red, PlayerKind.Computer);
        var blue = new Player("Blue", Blue);
        var players = new List<Player> { red, blue };
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());

        AiAction? unguarded = ComputerAi.ChooseNextAction(
            state, Red, new HashSet<HexCoord>(), new HashSet<HexCoord>(), new Random(0));
        AiMoveAction move = Assert.IsType<AiMoveAction>(unguarded);
        Assert.Equal(HexCoord.FromOffset(3, 0), move.Source);

        AiAction? guarded = ComputerAi.ChooseNextAction(
            state, Red, new HashSet<HexCoord>(),
            new HashSet<HexCoord> { HexCoord.FromOffset(3, 0) }, new Random(0));
        Assert.Null(guarded);
    }
}
