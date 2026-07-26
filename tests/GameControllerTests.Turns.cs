// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

public partial class GameControllerTests
{
    // --- Neutral-ground growth -------------------------------------------

    [Fact]
    public void NeutralGrowth_FiresOncePerRound_NotOncePerPlayer()
    {
        // Neutral ground grows once per round, anchored to slot 0
        // (Red), NOT once per player. Distinguishing cascade scenario:
        //   P = neutral tree at (1,0), G = neutral grave at (3,0),
        //   E = empty neutral tile at (2,0), adjacent to BOTH P and G.
        // Round 2 slot 0 (Red): snapshot = {P}, so G rots to a tree but
        // E (1 snapshot tree) stays empty. If growth also ran on Blue's
        // turn (slot 1) the snapshot would now be {P, G-as-tree} and E
        // would spread that same round — so E staying empty through
        // Blue's turn proves growth did NOT run per player. E only
        // spreads on round 3 slot 0.
        var g = new TestGame();
        g.Tile(1, 0).Owner = PlayerId.None; // P
        g.Tile(2, 0).Owner = PlayerId.None; // E
        g.Tile(3, 0).Owner = PlayerId.None; // G
        g.Tile(1, 0).Occupant = new Tree();
        g.Tile(3, 0).Occupant = new Grave();

        g.Hud.ClickEndTurn(); // Red t1 -> Blue t1  (TurnNumber 1, no growth)

        Assert.IsType<Grave>(g.Tile(3, 0).Occupant);
        Assert.Null(g.Tile(2, 0).Occupant);

        g.Hud.ClickEndTurn(); // Blue t1 -> Red t2 (slot 0): neutral phase R2

        Assert.IsType<Tree>(g.Tile(3, 0).Occupant); // grave rotted
        Assert.Null(g.Tile(2, 0).Occupant);         // E still empty (1 snapshot tree)

        g.Hud.ClickEndTurn(); // Red t2 -> Blue t2 (slot 1): NO neutral phase

        Assert.Null(g.Tile(2, 0).Occupant);         // proves slot 1 did not grow

        g.Hud.ClickEndTurn(); // Blue t2 -> Red t3 (slot 0): neutral phase R3

        Assert.IsType<Tree>(g.Tile(2, 0).Occupant);  // now P + G = 2 neighbors -> spreads
    }

    [Fact]
    public void BrushedRaider_ActsOnNeutralSeat_NotBeforeFirstPlayer()
    {
        // Authored Freeform map carrying a landed raider (map-editor unit
        // brush): neutral is a real seat at the END of the rotation, so the
        // raider must not act at game start — Red moves first, then Blue,
        // and only then does neutral's turn move the raider (issue #186).
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        HexCoord raiderCoord = HexCoord.FromOffset(4, 0);
        ControllerHarness h = TestHelpers.BuildControllerGame(
            players: new List<Player> { red, blue },
            ownerOverrides: new[]
            {
                (0, 1, red.Id), (1, 1, red.Id),
                (4, 0, PlayerId.None),
            },
            beforeTerritories: g => g.Get(raiderCoord)!.Occupant =
                new Unit(PlayerId.None, UnitLevel.Soldier));

        // Game start: Red's turn, the raider has not moved.
        Assert.Equal(red.Id, h.State.Turns.CurrentPlayer.Id);
        Assert.Equal(1, h.State.Turns.TurnNumber);
        Assert.IsType<Unit>(h.State.Grid.Get(raiderCoord)!.Occupant);

        h.Hud.ClickEndTurn(); // Red T1 → Blue T1: still no raider activity.
        Assert.IsType<Unit>(h.State.Grid.Get(raiderCoord)!.Occupant);

        h.Hud.ClickEndTurn(); // Blue T1 → neutral seat (raider acts) → Red T2.

        Assert.Equal(2, h.State.Turns.TurnNumber);
        Assert.Equal(red.Id, h.State.Turns.CurrentPlayer.Id);
        // The raider captured an adjacent tile: it left its brush tile and
        // neutral now owns two tiles (the vacated one and the captured one).
        Assert.Null(h.State.Grid.Get(raiderCoord)!.Occupant);
        int neutralTiles = 0;
        foreach (HexTile t in h.State.Grid.Tiles)
        {
            if (t.Owner.IsNone) neutralTiles++;
        }
        Assert.Equal(2, neutralTiles);
    }

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
        // Put a Blue recruit on a non-capital Blue tile so Blue has upkeep
        // to pay when Blue's turn begins. Round 1 is free, so the charge
        // lands on Blue's T2 — alongside that turn's income credit.
        g.Tile(3, 0).Occupant = new Unit(g.Blue.Id);
        int blueSize = g.State.Territories
            .First(t => t.Owner == g.Blue.Id).Size;
        HexCoord blueCapital = g.State.Territories
            .First(t => t.Owner == g.Blue.Id).Capital!.Value;

        g.Hud.ClickEndTurn(); // Red T1 -> Blue T1 (free)
        g.Hud.ClickEndTurn(); // Blue T1 -> Red T2
        g.State.Treasury.SetGold(blueCapital, 20);

        g.Hud.ClickEndTurn(); // Red T2 -> Blue T2: Blue collects income, then pays upkeep.

        Assert.Equal(20 + blueSize - 2, g.State.Treasury.GetGold(blueCapital));
        // Recruit survived because Blue could afford it.
        Assert.NotNull(g.Tile(3, 0).Unit);
    }

    [Fact]
    public void StartTurn_Round1_ChargesNoUpkeep()
    {
        // Editor-placed starting garrisons are free through round 1 and
        // start paying at the start of round 2 — matching the income and
        // tree-growth gates. Blue holds a captain (upkeep 18) with only
        // 20g: it survives round 1 untouched, then pays on Blue's T2.
        var g = new TestGame();
        g.Tile(3, 0).Occupant = new Unit(g.Blue.Id, UnitLevel.Captain);
        HexCoord blueCapital = g.State.Territories
            .First(t => t.Owner == g.Blue.Id).Capital!.Value;
        g.State.Treasury.SetGold(blueCapital, 20);

        g.Hud.ClickEndTurn(); // Red T1 -> Blue T1: no income, no upkeep.

        Assert.Equal(20, g.State.Treasury.GetGold(blueCapital));
        Assert.IsType<Unit>(g.Tile(3, 0).Occupant);

        g.Hud.ClickEndTurn(); // Blue T1 -> Red T2
        g.Hud.ClickEndTurn(); // Red T2 -> Blue T2: income (8) then upkeep (18).

        Assert.Equal(20 + 8 - 18, g.State.Treasury.GetGold(blueCapital));
        Assert.IsType<Unit>(g.Tile(3, 0).Occupant);
    }

    [Fact]
    public void EndTurn_BankruptTerritory_LeavesGraves()
    {
        var g = new TestGame();
        // Give Blue a captain (upkeep 18) it can't pay. Round 1 is free;
        // on Blue's T2 the income credit (8 tiles) still falls short.
        g.Tile(3, 0).Occupant = new Unit(g.Blue.Id, UnitLevel.Captain);
        HexCoord blueCapital = g.State.Territories
            .First(t => t.Owner == g.Blue.Id).Capital!.Value;
        g.State.Treasury.SetGold(blueCapital, 0);

        g.Hud.ClickEndTurn(); // Red T1 -> Blue T1 (free)
        g.Hud.ClickEndTurn(); // Blue T1 -> Red T2
        g.Hud.ClickEndTurn(); // Red T2 -> Blue T2

        // Blue has 8g of income against 18 owed → bankrupt. Captain dies
        // and leaves a grave behind (not a null tile).
        Assert.IsType<Grave>(g.Tile(3, 0).Occupant);
    }

    [Fact]
    public void StartTurn_ConvertsGravesOnStartingPlayersTiles_OnTheirNextOwnTurn()
    {
        // Trees are created at the START of a player's turn, on
        // tiles of that player's color; each player's first turn
        // skips the phase entirely. So a grave dropped on
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
        // Spread rule: an empty tile on the starting player's
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
        // End-to-end ordering check: a bankruptcy grave from Blue T2
        // is converted to a tree by tree-growth at Blue T3 start, and
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

        g.Hud.ClickEndTurn(); // Red T1 → Blue T1: round 1 is free, captain survives.
        Assert.IsType<Unit>(g.Tile(3, 0).Occupant);

        g.Hud.ClickEndTurn(); // Blue T1 → Red T2
        g.Hud.ClickEndTurn(); // Red T2 → Blue T2: income falls short → grave on (3,0).
        Assert.IsType<Grave>(g.Tile(3, 0).Occupant);

        g.Hud.ClickEndTurn(); // Blue T2 → Red T3 (Red collects income, not Blue).
        Assert.IsType<Grave>(g.Tile(3, 0).Occupant);

        // Zero the treasury so the final assertion measures the T3 credit alone.
        g.State.Treasury.SetGold(blueCapital, 0);
        g.Hud.ClickEndTurn(); // Red T3 → Blue T3: growth converts grave→tree, then income.

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
        // Full feedback loop:
        //   1. Blue can't afford its captain, but round 1 charges no
        //      upkeep — the captain survives Blue's turn 1.
        //   2. Blue's turn 2 starts: income falls short of the captain's
        //      upkeep, so it bankrupts → grave. The tree-growth phase ran
        //      before upkeep, so the fresh grave stays a grave.
        //   3. Red's turn 3 starts: phase runs but only on Red tiles,
        //      so the Blue grave is unaffected.
        //   4. Blue's turn 3 starts: phase runs on Blue tiles, so
        //      the bankruptcy grave converts into a tree.
        var g = new TestGame();
        g.Tile(3, 0).Occupant = new Unit(g.Blue.Id, UnitLevel.Captain);
        HexCoord blueCapital = g.State.Territories
            .First(t => t.Owner == g.Blue.Id).Capital!.Value;
        g.State.Treasury.SetGold(blueCapital, 0);

        g.Hud.ClickEndTurn(); // Red -> Blue (turn 1): no upkeep, captain survives.
        Assert.IsType<Unit>(g.Tile(3, 0).Occupant);

        g.Hud.ClickEndTurn(); // Blue -> Red (turn 2).
        g.Hud.ClickEndTurn(); // Red -> Blue (turn 2): upkeep bankrupts.
        Assert.IsType<Grave>(g.Tile(3, 0).Occupant);

        g.Hud.ClickEndTurn(); // Blue -> Red (turn 3): phase on Red tiles only.
        Assert.IsType<Grave>(g.Tile(3, 0).Occupant);

        g.Hud.ClickEndTurn(); // Red -> Blue (turn 3): phase on Blue tiles.
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

        // Round 1 is free, so drive the first bankruptcy on Blue's turn 2.
        // We re-place an unbankrupted captain afterward to drive a second
        // bankruptcy on Blue's turn 3 with the phase running first.
        g.Hud.ClickEndTurn(); // Red -> Blue (turn 1, no upkeep; captain survives)
        g.Hud.ClickEndTurn(); // Blue -> Red (turn 2)
        g.Hud.ClickEndTurn(); // Red -> Blue (turn 2; captain goes bankrupt → grave)
        Assert.IsType<Grave>(g.Tile(3, 0).Occupant);

        // Plant a fresh captain that will bankrupt on Blue's turn 3.
        // The previous bankruptcy grave is still there; on Blue's
        // turn 3 it should convert to a tree (rule 1) BEFORE upkeep
        // bankrupts the new captain. We can't put a captain directly
        // on the grave tile, so use (4,0).
        g.Tile(4, 0).Occupant = new Unit(g.Blue.Id, UnitLevel.Captain);
        g.State.Treasury.SetGold(blueCapital, 0);

        g.Hud.ClickEndTurn(); // Blue -> Red (turn 3, Red tiles only)
        // Grave still there (Red's phase doesn't touch Blue tiles).
        Assert.IsType<Grave>(g.Tile(3, 0).Occupant);

        g.Hud.ClickEndTurn(); // Red -> Blue (turn 3, runs on Blue tiles)
        // Old grave became a tree (growth ran first).
        Assert.IsType<Tree>(g.Tile(3, 0).Occupant);
        // Fresh captain became a grave (upkeep ran AFTER growth, so
        // the new grave does not get converted this turn).
        Assert.IsType<Grave>(g.Tile(4, 0).Occupant);
    }
}
