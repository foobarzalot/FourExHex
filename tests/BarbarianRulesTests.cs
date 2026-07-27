// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Barbarian aggro-state rules (<see cref="BarbarianRules"/>): the
/// compromise trigger (an enemy capturing into a neutral territory flips
/// its units), aggro spread on territory joins, and the classification
/// helper the AI's provoke-avoidance uses.
/// </summary>
public class BarbarianRulesTests
{
    private static readonly PlayerId Red = PlayerId.FromIndex(0);
    private static readonly PlayerId Blue = PlayerId.FromIndex(1);

    private static GameState MakeState(HexGrid grid)
    {
        var players = new List<Player>
        {
            new Player("Red", Red),
            new Player("Blue", Blue),
        };
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        return new GameState(
            grid, territories, players,
            new TurnState(players, 0, 4), new Treasury());
    }

    /// <summary>
    /// 5x1 strip: columns 0-2 neutral with passive barbarians on 0 and 1,
    /// columns 3-4 Red.
    /// </summary>
    private static HexGrid BuildBarbarianStrip()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(5, 1, Red);
        for (int col = 0; col <= 2; col++)
            grid.Get(HexCoord.FromOffset(col, 0))!.Owner = PlayerId.None;
        grid.Get(HexCoord.FromOffset(0, 0))!.Occupant = new Unit(PlayerId.None, UnitLevel.Recruit);
        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = new Unit(PlayerId.None, UnitLevel.Soldier);
        return grid;
    }

    private static void Recompute(GameState state) =>
        state.Territories = TestHelpers.BuildTerritoriesFromGrid(state.Grid);

    // --- compromise -------------------------------------------------------

    [Fact]
    public void PropagateAggro_CompromiseFlipsSurvivingUnits()
    {
        GameState state = MakeState(BuildBarbarianStrip());
        IReadOnlyList<Territory> previous = state.Territories;

        // Red captures the empty neutral tile (2,0) — the territory is
        // compromised; both surviving barbarians flip aggro.
        state.Grid.Get(HexCoord.FromOffset(2, 0))!.Owner = Red;
        Recompute(state);

        AggroFlipResult flipped = BarbarianRules.PropagateAggro(state, previous);

        Assert.True(flipped.Any);
        Assert.True(state.Grid.Get(HexCoord.FromOffset(0, 0))!.Unit!.IsAggro);
        Assert.True(state.Grid.Get(HexCoord.FromOffset(1, 0))!.Unit!.IsAggro);
    }

    [Fact]
    public void PropagateAggro_NoChange_WhenUncompromised()
    {
        GameState state = MakeState(BuildBarbarianStrip());
        IReadOnlyList<Territory> previous = state.Territories;

        // A recompute with no ownership change (e.g. an unrelated action).
        Recompute(state);

        AggroFlipResult flipped = BarbarianRules.PropagateAggro(state, previous);

        Assert.False(flipped.Any);
        Assert.False(state.Grid.Get(HexCoord.FromOffset(0, 0))!.Unit!.IsAggro);
        Assert.False(state.Grid.Get(HexCoord.FromOffset(1, 0))!.Unit!.IsAggro);
    }

    [Fact]
    public void PropagateAggro_TideSubmerge_IsNotCompromise()
    {
        // Rising Tides removes the tile (no new owner appears): losing
        // ground to the sea is not a compromise — barbarians stay passive.
        GameState state = MakeState(BuildBarbarianStrip());
        IReadOnlyList<Territory> previous = state.Territories;

        state.Grid.Remove(HexCoord.FromOffset(2, 0));
        state.AddWater(HexCoord.FromOffset(2, 0));
        Recompute(state);

        AggroFlipResult flipped = BarbarianRules.PropagateAggro(state, previous);

        Assert.False(flipped.Any);
        Assert.False(state.Grid.Get(HexCoord.FromOffset(0, 0))!.Unit!.IsAggro);
        Assert.False(state.Grid.Get(HexCoord.FromOffset(1, 0))!.Unit!.IsAggro);
    }

    [Fact]
    public void PropagateAggro_CompromiseByNeutralCapture_DoesNotFlip()
    {
        // An aggro raider capturing a PLAYER tile between two neutral
        // territories makes the tile neutral — from the compromised-previous
        // territory's view no coord gained a real owner, so the compromise
        // pass stays quiet (the join is handled by the spread pass instead).
        GameState state = MakeState(BuildBarbarianStrip());
        IReadOnlyList<Territory> previous = state.Territories;

        // The raider's capture clears whatever occupied the tile (here the
        // Red capital the initial reconcile placed on it).
        state.Grid.Get(HexCoord.FromOffset(3, 0))!.Owner = PlayerId.None;
        state.Grid.Get(HexCoord.FromOffset(3, 0))!.Occupant = null;
        Recompute(state);

        Assert.False(BarbarianRules.PropagateAggro(state, previous).Any);
    }

    // --- spread on join ---------------------------------------------------

    [Fact]
    public void PropagateAggro_SpreadFlipsWholeNeutralTerritory()
    {
        // One aggro unit inside a neutral territory (as after an aggro
        // raider's capture merged its tile in): every unit there flips.
        HexGrid grid = BuildBarbarianStrip();
        grid.Get(HexCoord.FromOffset(2, 0))!.Owner = PlayerId.None;
        grid.Get(HexCoord.FromOffset(2, 0))!.Occupant =
            new Unit(PlayerId.None, UnitLevel.Recruit) { IsAggro = true };
        GameState state = MakeState(grid);

        AggroFlipResult flipped = BarbarianRules.PropagateAggro(state, state.Territories);

        Assert.True(flipped.Any);
        Assert.True(state.Grid.Get(HexCoord.FromOffset(0, 0))!.Unit!.IsAggro);
        Assert.True(state.Grid.Get(HexCoord.FromOffset(1, 0))!.Unit!.IsAggro);
    }

    [Fact]
    public void PropagateAggro_DoesNotSpreadAcrossDisconnectedTerritories()
    {
        // Two separate neutral territories (split by a Red column): aggro in
        // one never leaks into the other.
        HexGrid grid = TestHelpers.BuildRectGrid(5, 1, Red);
        grid.Get(HexCoord.FromOffset(0, 0))!.Owner = PlayerId.None;
        grid.Get(HexCoord.FromOffset(1, 0))!.Owner = PlayerId.None;
        grid.Get(HexCoord.FromOffset(3, 0))!.Owner = PlayerId.None;
        grid.Get(HexCoord.FromOffset(0, 0))!.Occupant =
            new Unit(PlayerId.None, UnitLevel.Recruit) { IsAggro = true };
        grid.Get(HexCoord.FromOffset(3, 0))!.Occupant =
            new Unit(PlayerId.None, UnitLevel.Recruit);
        GameState state = MakeState(grid);

        BarbarianRules.PropagateAggro(state, state.Territories);

        Assert.False(state.Grid.Get(HexCoord.FromOffset(3, 0))!.Unit!.IsAggro);
    }

    // --- classification ---------------------------------------------------

    [Fact]
    public void IsNonAggroBarbarianTerritory_TrueForPassiveNeutralUnits()
    {
        GameState state = MakeState(BuildBarbarianStrip());
        Territory neutral = state.Territories.Single(t => t.Owner.IsNone);

        Assert.True(BarbarianRules.IsNonAggroBarbarianTerritory(neutral, state.Grid));
    }

    [Fact]
    public void IsNonAggroBarbarianTerritory_FalseWhenAggroOrEmptyOrOwned()
    {
        HexGrid grid = BuildBarbarianStrip();
        GameState state = MakeState(grid);
        Territory neutral = state.Territories.Single(t => t.Owner.IsNone);
        Territory red = state.Territories.First(t => t.Owner == Red);

        // Player-owned territory: never a barbarian territory.
        Assert.False(BarbarianRules.IsNonAggroBarbarianTerritory(red, grid));

        // Any aggro unit disqualifies (post-spread the whole territory is aggro).
        grid.Get(HexCoord.FromOffset(0, 0))!.Unit!.IsAggro = true;
        Assert.False(BarbarianRules.IsNonAggroBarbarianTerritory(neutral, grid));

        // A neutral territory with no units (trees/empty) is not barbarian.
        grid.Get(HexCoord.FromOffset(0, 0))!.Occupant = null;
        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = null;
        Assert.False(BarbarianRules.IsNonAggroBarbarianTerritory(neutral, grid));
    }
}
