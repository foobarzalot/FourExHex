// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

public partial class GameControllerTests
{
    // --- Terrain-capture effects (issue #155) -----------------------------
    //
    // Every actual ownership change fires a one-shot capture effect on the
    // view: a coin flourish for gold, a shake + dust puff for mountains, a
    // baseline flash + ring for plain tiles. Gold and mountain also fire a
    // terrain sound cue that LAYERS on top of the action's occupant cue
    // (place thud / chop / squelch) — terrain is orthogonal to the
    // destroyed occupant. Nothing fires on repositions within own
    // territory, repaints, or undo/redo.

    [Fact]
    public void Move_CaptureGoldTile_FiresGoldEffectAndChime()
    {
        var g = new TestGame();
        g.Tile(2, 1).IsGold = true;
        g.Tile(1, 1).Occupant = new Unit(g.Red.Id);

        g.Map.SimulateClick(g.Tile(1, 1));
        g.Map.SimulateClick(g.Tile(2, 1)); // capture empty Blue gold tile

        // Sanity: the capture happened.
        Assert.Equal(g.Red.Id, g.Tile(2, 1).Owner);

        (HexCoord Coord, TerrainFeature Terrain) fx = Assert.Single(g.Map.TerrainCaptureEffects);
        Assert.Equal(HexCoord.FromOffset(2, 1), fx.Coord);
        Assert.Equal(TerrainFeature.Gold, fx.Terrain);
        Assert.Single(g.Map.GoldCapturedSounds);
        Assert.Empty(g.Map.MountainCapturedSounds);
    }

    [Fact]
    public void Move_CaptureMountainTile_FiresMountainEffectAndThud()
    {
        var g = new TestGame();
        g.Tile(2, 1).IsMountain = true;
        g.Tile(1, 1).Occupant = new Unit(g.Red.Id);

        g.Map.SimulateClick(g.Tile(1, 1));
        g.Map.SimulateClick(g.Tile(2, 1)); // capture empty Blue mountain tile

        Assert.Equal(g.Red.Id, g.Tile(2, 1).Owner);

        (HexCoord Coord, TerrainFeature Terrain) fx = Assert.Single(g.Map.TerrainCaptureEffects);
        Assert.Equal(HexCoord.FromOffset(2, 1), fx.Coord);
        Assert.Equal(TerrainFeature.Mountain, fx.Terrain);
        Assert.Single(g.Map.MountainCapturedSounds);
        Assert.Empty(g.Map.GoldCapturedSounds);
    }

    [Fact]
    public void Move_CapturePlainTile_FiresBaselineEffectNoTerrainSound()
    {
        var g = new TestGame();
        g.Tile(1, 1).Occupant = new Unit(g.Red.Id);

        g.Map.SimulateClick(g.Tile(1, 1));
        g.Map.SimulateClick(g.Tile(2, 1)); // capture empty Blue plain tile

        Assert.Equal(g.Red.Id, g.Tile(2, 1).Owner);

        (HexCoord Coord, TerrainFeature Terrain) fx = Assert.Single(g.Map.TerrainCaptureEffects);
        Assert.Equal(HexCoord.FromOffset(2, 1), fx.Coord);
        Assert.Equal(TerrainFeature.None, fx.Terrain);
        // The plain capture keeps its existing place-thud as the audio cue;
        // no terrain sound layers on top.
        Assert.Empty(g.Map.GoldCapturedSounds);
        Assert.Empty(g.Map.MountainCapturedSounds);
        Assert.Single(g.Map.UnitPlacedSounds);
    }

    [Fact]
    public void Move_RepositionOntoOwnGoldTile_FiresNothing()
    {
        // Reposition needs a third Red tile so the unit has somewhere
        // empty to land within its own territory.
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        ControllerHarness h = TestHelpers.BuildControllerGame(
            players: new List<Player> { red, blue },
            ownerOverrides: new[] { (0, 1, red.Id), (1, 1, red.Id), (2, 1, red.Id) });
        MockHexMapView map = h.Map;
        HexGrid grid = h.State.Grid;

        grid.Get(HexCoord.FromOffset(2, 1))!.IsGold = true;
        grid.Get(HexCoord.FromOffset(1, 1))!.Occupant = new Unit(red.Id);

        map.SimulateClick(grid.Get(HexCoord.FromOffset(1, 1))); // pick up
        map.SimulateClick(grid.Get(HexCoord.FromOffset(2, 1))); // reposition, no capture

        // Sanity: the unit moved but ownership never changed.
        Assert.NotNull(grid.Get(HexCoord.FromOffset(2, 1))!.Unit);
        Assert.Empty(map.TerrainCaptureEffects);
        Assert.Empty(map.GoldCapturedSounds);
        Assert.Empty(map.MountainCapturedSounds);
    }

    [Fact]
    public void BuyRecruit_CaptureGoldTile_FiresGoldEffectAndChime()
    {
        var g = new TestGame();
        g.Tile(2, 1).IsGold = true;
        HexCoord redCapital = g.RedTerritory.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 25);

        g.Map.SimulateClick(g.Tile(0, 1));
        g.Hud.ClickBuyRecruit();
        g.Map.SimulateClick(g.Tile(2, 1)); // place-capture the Blue gold tile

        Assert.Equal(g.Red.Id, g.Tile(2, 1).Owner);

        (HexCoord Coord, TerrainFeature Terrain) fx = Assert.Single(g.Map.TerrainCaptureEffects);
        Assert.Equal(TerrainFeature.Gold, fx.Terrain);
        Assert.Single(g.Map.GoldCapturedSounds);
    }

    [Fact]
    public void BuyRecruit_CaptureMountainTile_FiresMountainEffectAndThud()
    {
        var g = new TestGame();
        g.Tile(2, 1).IsMountain = true;
        HexCoord redCapital = g.RedTerritory.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 25);

        g.Map.SimulateClick(g.Tile(0, 1));
        g.Hud.ClickBuyRecruit();
        g.Map.SimulateClick(g.Tile(2, 1)); // place-capture the Blue mountain tile

        Assert.Equal(g.Red.Id, g.Tile(2, 1).Owner);

        (HexCoord Coord, TerrainFeature Terrain) fx = Assert.Single(g.Map.TerrainCaptureEffects);
        Assert.Equal(TerrainFeature.Mountain, fx.Terrain);
        Assert.Single(g.Map.MountainCapturedSounds);
    }

    [Fact]
    public void Move_CaptureGoldTileWithTree_LayersChimeOverChop()
    {
        // The conflict case: capturing a gold tile that holds a tree
        // destroys the tree (chop cue + debris) AND completes a gold
        // capture (flourish + chime). Audio policy is LAYER: both cues
        // fire for the one action.
        var g = new TestGame();
        g.Tile(2, 1).IsGold = true;
        g.Tile(2, 1).Occupant = new Tree();
        g.Tile(1, 1).Occupant = new Unit(g.Red.Id);

        g.Map.SimulateClick(g.Tile(1, 1));
        g.Map.SimulateClick(g.Tile(2, 1)); // capture the treed gold tile

        Assert.Equal(g.Red.Id, g.Tile(2, 1).Owner);

        (HexCoord Coord, TerrainFeature Terrain) fx = Assert.Single(g.Map.TerrainCaptureEffects);
        Assert.Equal(TerrainFeature.Gold, fx.Terrain);
        Assert.Single(g.Map.GoldCapturedSounds);
        // The occupant cue still fires — the chime layers, not replaces.
        Assert.Single(g.Map.TreeClearedSounds);
        // And the tree's destruction burst still plays alongside.
        Assert.Single(g.Map.DestructionEffects);
    }

    [Fact]
    public void BuildTower_OnMountainTile_FiresMountainShakeAndThud()
    {
        // A tower landing on a mountain rocks the tile just like a
        // capture — same visual, same thud, layered over the TowerPlaced
        // clack.
        var g = new TestGame();
        g.Tile(1, 1).IsMountain = true;
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 35);

        g.Hud.ClickBuildTower();
        g.Map.SimulateClick(g.Tile(1, 1));

        Assert.IsType<Tower>(g.Tile(1, 1).Occupant);

        (HexCoord Coord, TerrainFeature Terrain) fx = Assert.Single(g.Map.TerrainCaptureEffects);
        Assert.Equal(HexCoord.FromOffset(1, 1), fx.Coord);
        Assert.Equal(TerrainFeature.Mountain, fx.Terrain);
        Assert.Single(g.Map.MountainCapturedSounds);
        Assert.Single(g.Map.TowerPlacedSounds);
    }

    [Fact]
    public void BuildTower_OnPlainTile_FiresNoTerrainFx()
    {
        var g = new TestGame();
        g.Map.SimulateClick(g.Tile(0, 1));
        HexCoord redCapital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(redCapital, 35);

        g.Hud.ClickBuildTower();
        g.Map.SimulateClick(g.Tile(1, 1));

        Assert.IsType<Tower>(g.Tile(1, 1).Occupant);
        Assert.Empty(g.Map.TerrainCaptureEffects);
        Assert.Empty(g.Map.MountainCapturedSounds);
        Assert.Single(g.Map.TowerPlacedSounds);
    }

    [Fact]
    public void AiTurn_BuildTowerOnMountain_FiresMountainShakeAndThud()
    {
        (GameState state, MockHexMapView map, MockHudView hud) = BuildAiFixture();
        // (0,1) is Red-owned, empty, non-capital — a legal tower site.
        state.Grid.Get(HexCoord.FromOffset(0, 1))!.IsMountain = true;
        HexCoord cap = RedCapital(state);
        state.Treasury.SetGold(cap, 100);

        var build = new AiBuildTowerAction(cap, HexCoord.FromOffset(0, 1));
        GameController c = BuildHarnessWithStubAi(state, map, hud, build);

        c.StartGame();

        Assert.IsType<Tower>(state.Grid.Get(HexCoord.FromOffset(0, 1))!.Occupant);

        (HexCoord Coord, TerrainFeature Terrain) fx = Assert.Single(map.TerrainCaptureEffects);
        Assert.Equal(HexCoord.FromOffset(0, 1), fx.Coord);
        Assert.Equal(TerrainFeature.Mountain, fx.Terrain);
        Assert.Single(map.MountainCapturedSounds);
    }

    [Fact]
    public void AiTurn_CaptureGoldTile_FiresGoldEffectAndChime()
    {
        // AI move-captures a gold tile — the terrain effect fires the
        // same as on the human path. Stub chooser pins the action.
        (GameState state, MockHexMapView map, MockHudView hud) = BuildAiFixture();
        state.Grid.Get(HexCoord.FromOffset(2, 1))!.IsGold = true;

        var move = new AiMoveAction(HexCoord.FromOffset(1, 1), HexCoord.FromOffset(2, 1));
        GameController c = BuildHarnessWithStubAi(state, map, hud, move);

        c.StartGame();

        Assert.Equal(state.Players[0].Id, state.Grid.Get(HexCoord.FromOffset(2, 1))!.Owner);

        (HexCoord Coord, TerrainFeature Terrain) fx = Assert.Single(map.TerrainCaptureEffects);
        Assert.Equal(HexCoord.FromOffset(2, 1), fx.Coord);
        Assert.Equal(TerrainFeature.Gold, fx.Terrain);
        Assert.Single(map.GoldCapturedSounds);
    }
}
