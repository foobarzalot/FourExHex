using System.Collections.Generic;
using System.Linq;
using Godot;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Verifies that the existing AiSimulator paths produce the same
/// post-state as the corresponding tutorial beats. Phase 11's
/// BuildPane post-beat state cache will rely on this — given a Beat,
/// it converts to the equivalent AiAction and runs it through
/// AiSimulator.Clone + Apply. The conversion is inlined here as a
/// one-line helper; a production helper is deferred until Phase 11
/// has a second consumer.
/// </summary>
public class TutorialBeatSimulatorTests
{
    [Fact]
    public void ApplyBuyPeasantEquivalent_PlacesPeasant_AndDeductsGold()
    {
        // 5x5 single-color grid → one big red territory; CapitalReconciler
        // assigns the capital somewhere inside it. State construction
        // mirrors TutorialSerializerTests.BuildMinimalState's pattern
        // (HexGrid + BuildTerritoriesFromGrid + TurnState + Treasury).
        var red = new Player("Red", new Color("e53935"), AiKind.Human);
        var players = new List<Player> { red };
        HexGrid grid = TestHelpers.BuildRectGrid(5, 5, red.Color);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var turnState = new TurnState(players, currentPlayerIndex: 0, turnNumber: 0);
        var state = new GameState(grid, territories, players, turnState, new Treasury());

        Territory redTerritory = state.Territories.First(t => t.Owner == red.Color);
        HexCoord capital = redTerritory.Capital!.Value;
        // Pick the first non-capital, currently-empty tile in the territory —
        // any such tile is a legal peasant target.
        HexCoord at = redTerritory.Coords.First(c =>
            c != capital && state.Grid.Get(c)!.Occupant == null);

        // Seed the capital with peasant-cost gold so the buy is affordable.
        state.Treasury.SetGold(capital, PurchaseRules.CostFor(UnitLevel.Peasant));
        int goldBefore = state.Treasury.GetGold(capital);

        // Equivalent of: BuyPeasantBeat { At = at } applied to this state.
        // (The conversion is intentionally inline — a helper would be
        // premature with one consumer; Phase 11 may extract it.)
        var beat = new BuyPeasantBeat { Index = 0, Turn = 1, Actor = 0, At = at };
        var action = new AiBuyUnitAction(capital, beat.At, UnitLevel.Peasant);

        AiSimulator.Apply(action, state);

        // (a) A red Peasant unit appears at At.
        HexTile after = state.Grid.Get(at)!;
        Unit placed = Assert.IsType<Unit>(after.Occupant);
        Assert.Equal(red.Color, placed.Owner);
        Assert.Equal(UnitLevel.Peasant, placed.Level);
        // (b) Capital gold deducted by peasant cost.
        Assert.Equal(goldBefore - PurchaseRules.CostFor(UnitLevel.Peasant),
                     state.Treasury.GetGold(capital));
    }
}
