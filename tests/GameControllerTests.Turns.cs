using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

public partial class GameControllerTests
{
    // --- End turn ---------------------------------------------------------

    [Fact]
    public void EndTurn_AdvancesPlayer()
    {
        var g = new TestGame();
        Assert.Equal(g.Red.Id, g.State.Turns.CurrentPlayer.Id);

        g.Hud.ClickEndTurn();

        Assert.Equal(g.Blue.Id, g.State.Turns.CurrentPlayer.Id);
    }

    [Fact]
    public void EndTurn_ResetsMovementForNewPlayer()
    {
        var g = new TestGame();
        var blueUnit = new Unit(g.Blue.Id) { HasMovedThisTurn = true };
        g.Tile(3, 0).Occupant = blueUnit;

        g.Hud.ClickEndTurn(); // Red -> Blue

        Assert.False(blueUnit.HasMovedThisTurn);
    }

    [Fact]
    public void EndTurn_PaysUpkeep_FromNewPlayerTerritories()
    {
        var g = new TestGame();
        // Put a Blue recruit on a non-capital Blue tile so Blue has
        // upkeep to pay when Blue's turn begins. Round 1 has no income
        // (every player's first turn skips income), so Blue's only
        // treasury change at the start of its first turn is upkeep.
        g.Tile(3, 0).Occupant = new Unit(g.Blue.Id);
        HexCoord blueCapital = g.State.Territories
            .First(t => t.Owner == g.Blue.Id).Capital!.Value;
        g.State.Treasury.SetGold(blueCapital, 20);

        g.Hud.ClickEndTurn(); // Red -> Blue: Blue pays upkeep, no income (round 1).

        Assert.Equal(20 - 2, g.State.Treasury.GetGold(blueCapital));
        // Recruit survived because Blue could afford it.
        Assert.NotNull(g.Tile(3, 0).Unit);
    }

    [Fact]
    public void EndTurn_BankruptTerritory_LeavesGraves()
    {
        var g = new TestGame();
        // Give Blue a captain (upkeep 18) it can't pay. Blue has 0 gold
        // and round 1 skips the income credit, so upkeep goes straight
        // to bankruptcy.
        g.Tile(3, 0).Occupant = new Unit(g.Blue.Id, UnitLevel.Captain);
        HexCoord blueCapital = g.State.Territories
            .First(t => t.Owner == g.Blue.Id).Capital!.Value;
        g.State.Treasury.SetGold(blueCapital, 0);

        g.Hud.ClickEndTurn(); // advance to Blue

        // Blue has 0g and owes 18 upkeep → bankrupt. Captain dies and
        // leaves a grave behind (not a null tile).
        Assert.IsType<Grave>(g.Tile(3, 0).Occupant);
    }

    [Fact]
    public void StartTurn_ConvertsGravesOnStartingPlayersTiles_OnTheirNextOwnTurn()
    {
        // Trees are now created at the START of a player's turn, on
        // tiles of that player's color, and the very first turn of
        // each player skips the phase entirely. So a grave dropped on
        // a Red tile during Red's first turn doesn't convert when
        // Red ends — it converts on Red's NEXT start-of-turn (turn 2).
        // Use (1,1) — Red's non-capital tile (CapitalPlacer puts the
        // capital on lex-min (0,1)), so we don't stomp the Capital
        // occupant.
        var g = new TestGame();
        g.Tile(1, 1).Occupant = new Grave(); // Red tile (non-capital)

        g.Hud.ClickEndTurn();           // Red -> Blue (turn 1, phase skipped)
        Assert.IsType<Grave>(g.Tile(1, 1).Occupant);

        g.Hud.ClickEndTurn();           // Blue -> Red (turn 2, phase runs)
        Assert.IsType<Tree>(g.Tile(1, 1).Occupant);
    }

    [Fact]
    public void StartTurn_SpreadsTrees_AtStartOfOwningPlayersTurn()
    {
        // New spread rule: an empty tile on the starting player's
        // color with >= 2 neighboring trees (per snapshot) becomes a
        // tree. Place a Blue-tile pair so spreading flips the empty
        // Blue tile (2,1) — which is adjacent to BOTH (2,0) and (3,0).
        // Skip + advance until Blue's second turn starts so the phase
        // actually runs on Blue tiles.
        var g = new TestGame();
        g.Tile(2, 0).Occupant = new Tree();
        g.Tile(3, 0).Occupant = new Tree();

        g.Hud.ClickEndTurn(); // Red -> Blue (turn 1, skip)
        Assert.Null(g.Tile(2, 1).Occupant);

        g.Hud.ClickEndTurn(); // Blue -> Red (turn 2, runs on Red tiles only)
        Assert.Null(g.Tile(2, 1).Occupant);

        g.Hud.ClickEndTurn(); // Red -> Blue (turn 2, runs on Blue tiles)
        Assert.IsType<Tree>(g.Tile(2, 1).Occupant);
    }

    [Fact]
    public void StartTurn_IncomeSkipsTreeTiles_WhenStartingPlayerCollects()
    {
        // Plant a tree on one Blue tile. When Blue's turn 2 begins,
        // Blue's start-of-turn income credit excludes the tree tile.
        var g = new TestGame();
        g.Tile(3, 0).Occupant = new Tree();
        int blueSize = g.State.Territories
            .First(t => t.Owner == g.Blue.Id).Size;
        HexCoord blueCapital = g.State.Territories
            .First(t => t.Owner == g.Blue.Id).Capital!.Value;
        g.State.Treasury.SetGold(blueCapital, 0);

        g.Hud.ClickEndTurn(); // Red T1 → Blue T1 (first round: no income).
        Assert.Equal(0, g.State.Treasury.GetGold(blueCapital));

        g.Hud.ClickEndTurn(); // Blue T1 → Red T2 (Red collects income, not Blue).
        Assert.Equal(0, g.State.Treasury.GetGold(blueCapital));

        g.Hud.ClickEndTurn(); // Red T2 → Blue T2 (Blue collects income now).

        // Blue has no units so upkeep is 0. Income is size minus the
        // one tree tile.
        Assert.Equal(blueSize - 1, g.State.Treasury.GetGold(blueCapital));
    }

    [Fact]
    public void StartTurn_BankruptcyGraveBecomesTreeBeforeIncomeCredit()
    {
        // End-to-end ordering check: a bankruptcy grave from Blue T1
        // is converted to a tree by tree-growth at Blue T2 start, and
        // the same turn's income credit then excludes that (now-tree)
        // tile. This pins the start-of-turn order: tree-growth →
        // income → upkeep.
        var g = new TestGame();
        g.Tile(3, 0).Occupant = new Unit(g.Blue.Id, UnitLevel.Captain);
        int blueSize = g.State.Territories
            .First(t => t.Owner == g.Blue.Id).Size;
        HexCoord blueCapital = g.State.Territories
            .First(t => t.Owner == g.Blue.Id).Capital!.Value;
        g.State.Treasury.SetGold(blueCapital, 0);

        g.Hud.ClickEndTurn(); // Red T1 → Blue T1: captain bankrupts → grave on (3,0)
        Assert.IsType<Grave>(g.Tile(3, 0).Occupant);
        Assert.Equal(0, g.State.Treasury.GetGold(blueCapital));

        g.Hud.ClickEndTurn(); // Blue T1 → Red T2 (Red collects income, not Blue).
        Assert.Equal(0, g.State.Treasury.GetGold(blueCapital));
        Assert.IsType<Grave>(g.Tile(3, 0).Occupant);

        g.Hud.ClickEndTurn(); // Red T2 → Blue T2: growth converts grave→tree, then income.

        Assert.IsType<Tree>(g.Tile(3, 0).Occupant);
        // Income excludes the (now-tree) tile. No remaining units → no upkeep.
        Assert.Equal(blueSize - 1, g.State.Treasury.GetGold(blueCapital));
    }

    [Fact]
    public void StartTurn_IncomeRunsBeforeUpkeep()
    {
        // Pin the income-vs-upkeep order. On Blue T2 start, Blue has
        // 10g and a captain (upkeep 18). Blue's territory is 8 tiles,
        // no trees → income = 8. Correct order (income before upkeep)
        // gives 10 + 8 - 18 = 0g and the captain survives. If upkeep
        // ran first the captain would bankrupt at 10 < 18 → grave.
        var g = new TestGame();
        g.Tile(3, 0).Occupant = new Unit(g.Blue.Id, UnitLevel.Captain);
        HexCoord blueCapital = g.State.Territories
            .First(t => t.Owner == g.Blue.Id).Capital!.Value;

        // Make Blue solvent through T1 (which has no income), then
        // jump to Blue T2 with the treasury exactly at 10g so the
        // ordering is what's being measured.
        g.State.Treasury.SetGold(blueCapital, 100); // survives T1 upkeep -18 fine
        g.Hud.ClickEndTurn(); // Red T1 → Blue T1: -18 upkeep, captain survives
        Assert.IsType<Unit>(g.Tile(3, 0).Occupant);
        g.Hud.ClickEndTurn(); // Blue T1 → Red T2

        // Now set Blue to exactly 10g for the differentiating turn.
        g.State.Treasury.SetGold(blueCapital, 10);

        g.Hud.ClickEndTurn(); // Red T2 → Blue T2: tree growth, +8 income, -18 upkeep.

        Assert.IsType<Unit>(g.Tile(3, 0).Occupant);
        Assert.Equal(0, g.State.Treasury.GetGold(blueCapital));
    }

    [Fact]
    public void StartTurn_BankruptGraves_BecomeTreesOnPlayersNextOwnTurn()
    {
        // Full feedback loop under the new rule:
        //   1. Blue can't afford its captain; on Blue's turn-1 START
        //      the tree-growth phase is skipped (first-turn rule),
        //      then upkeep bankrupts the captain → grave.
        //   2. Red's turn 2 starts: phase runs but only on Red tiles,
        //      so the Blue grave is unaffected.
        //   3. Blue's turn 2 starts: phase runs on Blue tiles, so
        //      the bankruptcy grave converts into a tree.
        var g = new TestGame();
        g.Tile(3, 0).Occupant = new Unit(g.Blue.Id, UnitLevel.Captain);
        HexCoord blueCapital = g.State.Territories
            .First(t => t.Owner == g.Blue.Id).Capital!.Value;
        g.State.Treasury.SetGold(blueCapital, 0);

        g.Hud.ClickEndTurn(); // Red -> Blue (turn 1): skip phase, upkeep bankrupts.
        Assert.IsType<Grave>(g.Tile(3, 0).Occupant);

        g.Hud.ClickEndTurn(); // Blue -> Red (turn 2): phase on Red tiles only.
        Assert.IsType<Grave>(g.Tile(3, 0).Occupant);

        g.Hud.ClickEndTurn(); // Red -> Blue (turn 2): phase on Blue tiles.
        Assert.IsType<Tree>(g.Tile(3, 0).Occupant);
    }

    // --- Grave-to-tree: owner-specific timing ----------------------------
    // A grave on a given player's tile only converts into a tree at the
    // START of THAT player's next turn (the grave's "owner" is the tile's
    // color). The phase is skipped on every player's first turn, so the
    // earliest possible conversion is on the owning player's turn 2.

    [Fact]
    public void StartTurn_GraveOnNonStartingPlayersTile_Survives()
    {
        // Grave on Blue tile. Phase doesn't fire for Blue's first turn
        // and never converts non-Red graves on Red's turn, so even
        // after advancing into Red's turn 2 the Blue grave persists.
        var g = new TestGame();
        g.Tile(3, 0).Occupant = new Grave();
        Assert.Equal(g.Blue.Id, g.Tile(3, 0).Owner); // sanity: Blue tile

        g.Hud.ClickEndTurn(); // Red -> Blue (turn 1, skip)
        Assert.IsType<Grave>(g.Tile(3, 0).Occupant);

        g.Hud.ClickEndTurn(); // Blue -> Red (turn 2, runs on Red tiles only)
        Assert.IsType<Grave>(g.Tile(3, 0).Occupant);
    }

    [Fact]
    public void StartTurn_GraveOnStartingPlayersTile_ConvertsToTree()
    {
        // Grave on Red tile (1,1) — the non-capital Red tile. After
        // advancing into Red's turn 2 (skip their first turn, then
        // return), the grave converts.
        var g = new TestGame();
        g.Tile(1, 1).Occupant = new Grave();
        Assert.Equal(g.Red.Id, g.Tile(1, 1).Owner);

        g.Hud.ClickEndTurn(); // Red -> Blue
        g.Hud.ClickEndTurn(); // Blue -> Red (turn 2, phase runs)

        Assert.IsType<Tree>(g.Tile(1, 1).Occupant);
    }

    [Fact]
    public void StartTurn_MixedGraves_OnlyStartingPlayersColorConverts()
    {
        // Two graves: one on a Red tile, one on a Blue tile. When
        // Red's turn 2 starts, only the Red-tile grave converts. The
        // Blue-tile grave waits for Blue's turn 2. Both target tiles
        // are non-capital (Red's capital is (0,1); Blue's is (0,0)).
        var g = new TestGame();
        g.Tile(1, 1).Occupant = new Grave(); // Red tile (non-capital)
        g.Tile(3, 0).Occupant = new Grave(); // Blue tile (non-capital)

        g.Hud.ClickEndTurn(); // Red -> Blue (turn 1, skip)
        g.Hud.ClickEndTurn(); // Blue -> Red (turn 2, runs on Red tiles)

        Assert.IsType<Tree>(g.Tile(1, 1).Occupant);
        Assert.IsType<Grave>(g.Tile(3, 0).Occupant);
    }

    [Fact]
    public void StartTurn_GraveOnBlueTile_ConvertsOnlyAtBlueStartOfTurn()
    {
        // End-to-end statement of the rule: a grave on a Blue tile
        // persists until Blue's NEXT turn starts (turn 2 here), then
        // converts. It must NOT convert on Red's turn 2 start.
        var g = new TestGame();
        g.Tile(3, 0).Occupant = new Grave();

        g.Hud.ClickEndTurn(); // Red -> Blue (turn 1, skip)
        Assert.IsType<Grave>(g.Tile(3, 0).Occupant);

        g.Hud.ClickEndTurn(); // Blue -> Red (turn 2, Red tiles only)
        Assert.IsType<Grave>(g.Tile(3, 0).Occupant);

        g.Hud.ClickEndTurn(); // Red -> Blue (turn 2, runs on Blue tiles)
        Assert.IsType<Tree>(g.Tile(3, 0).Occupant);
    }

    [Fact]
    public void StartTurn_FirstTurn_PhaseIsSkipped()
    {
        // First-turn rule: the tree-growth phase MUST NOT fire on
        // any player's first turn. Set up a Blue grave + a tree pair
        // on Blue tiles that would otherwise spread, end Red's turn
        // (Blue's first turn begins). Both rules must be no-ops.
        var g = new TestGame();
        g.Tile(3, 0).Occupant = new Grave(); // Blue tile
        g.Tile(2, 0).Occupant = new Tree();  // would seed (2,1) spread
        g.Tile(3, 1).Occupant = new Tree();  // would seed (2,1) spread

        g.Hud.ClickEndTurn(); // Red -> Blue (turn 1, skip)

        // Grave still there.
        Assert.IsType<Grave>(g.Tile(3, 0).Occupant);
        // Spread did NOT happen: (2,1) still empty.
        Assert.Null(g.Tile(2, 1).Occupant);
    }

    [Fact]
    public void StartTurn_PhaseRunsBeforeUpkeep_FreshGravesDoNotConvertSameTurn()
    {
        // Order rule: tree growth runs BEFORE upkeep on a player's
        // start of turn. If upkeep ran first, the unit it bankrupts
        // would become a grave, then the tree-growth phase would
        // immediately convert that grave into a tree this turn.
        // Correct order leaves the freshly-bankrupted unit as a grave.
        var g = new TestGame();
        // Captain on Blue tile that Blue cannot afford.
        g.Tile(3, 0).Occupant = new Unit(g.Blue.Id, UnitLevel.Captain);
        HexCoord blueCapital = g.State.Territories
            .First(t => t.Owner == g.Blue.Id).Capital!.Value;
        g.State.Treasury.SetGold(blueCapital, 0);

        // Skip Blue's first turn so the phase actually fires the
        // next time Blue starts a turn. We re-place the unbankrupted
        // captain afterward to drive bankruptcy on Blue's turn 2 with
        // the phase running first.
        g.Hud.ClickEndTurn(); // Red -> Blue (turn 1, skip; captain goes bankrupt → grave)
        Assert.IsType<Grave>(g.Tile(3, 0).Occupant);

        // Plant a fresh captain that will bankrupt on Blue's turn 2.
        // The previous bankruptcy grave is still there; on Blue's
        // turn 2 it should convert to a tree (rule 1) BEFORE upkeep
        // bankrupts the new captain. We can't put a captain directly
        // on the grave tile, so use (4,0).
        g.Tile(4, 0).Occupant = new Unit(g.Blue.Id, UnitLevel.Captain);
        g.State.Treasury.SetGold(blueCapital, 0);

        g.Hud.ClickEndTurn(); // Blue -> Red (turn 2, Red tiles only)
        // Grave still there (Red's phase doesn't touch Blue tiles).
        Assert.IsType<Grave>(g.Tile(3, 0).Occupant);

        g.Hud.ClickEndTurn(); // Red -> Blue (turn 2, runs on Blue tiles)
        // Old grave became a tree (growth ran first).
        Assert.IsType<Tree>(g.Tile(3, 0).Occupant);
        // Fresh captain became a grave (upkeep ran AFTER growth, so
        // the new grave does not get converted this turn).
        Assert.IsType<Grave>(g.Tile(4, 0).Occupant);
    }
}
