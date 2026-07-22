// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

public class LevelWorkspaceTests
{
    private static HexCoord At(int col, int row) => HexCoord.FromOffset(col, row);

    private static int CapitalOccupantCount(HexGrid grid) =>
        grid.Tiles.Count(t => t.Occupant is Capital);

    [Fact]
    public void BlankInit_IsAllWater()
    {
        var ws = new LevelWorkspace(6, 4);

        Assert.Empty(ws.Grid.Tiles);
        Assert.Equal(24, ws.Water.Count);
        Assert.Empty(ws.Territories);
    }

    [Fact]
    public void BlankInit_AllSlotsInactive()
    {
        var ws = new LevelWorkspace(6, 4);
        for (int slot = 0; slot < GameSettings.PlayerConfig.Length; slot++)
            Assert.Equal(PlayerKind.None, ws.KindFor(slot));
    }

    [Fact]
    public void PaintLand_CreatesTileAndRemovesWater()
    {
        var ws = new LevelWorkspace(6, 4);
        ws.PaintLand(0, At(1, 1));

        HexTile? tile = ws.Grid.Get(At(1, 1));
        Assert.NotNull(tile);
        Assert.Equal(PlayerId.FromIndex(0), tile!.Owner);
        Assert.DoesNotContain(At(1, 1), ws.Water);
    }

    [Fact]
    public void PaintLand_AutoActivatesInactiveSlotAsComputerSoldier()
    {
        var ws = new LevelWorkspace(6, 4);
        ws.PaintLand(2, At(1, 1));

        Assert.Equal(PlayerKind.Computer, ws.KindFor(2));
        Assert.Equal(Difficulty.Soldier, ws.DifficultyFor(2));
    }

    [Fact]
    public void PaintLand_DoesNotDowngradeExplicitRosterChoice()
    {
        var ws = new LevelWorkspace(6, 4);
        ws.SetSlot(0, PlayerKind.Human);
        ws.PaintLand(0, At(1, 1));

        Assert.Equal(PlayerKind.Human, ws.KindFor(0));
    }

    [Fact]
    public void PaintLand_TwoTileRegion_GetsExactlyOneCapital()
    {
        var ws = new LevelWorkspace(6, 4);
        ws.PaintLand(0, At(1, 1));
        ws.PaintLand(0, At(2, 1));

        Territory territory = Assert.Single(ws.Territories);
        Assert.True(territory.HasCapital);
        Assert.Equal(1, CapitalOccupantCount(ws.Grid));
    }

    [Fact]
    public void PaintLand_GrowingRegionTileByTile_NeverOrphansCapitals()
    {
        var ws = new LevelWorkspace(8, 4);
        ws.PaintLand(0, At(1, 1));
        ws.PaintLand(0, At(2, 1));
        ws.PaintLand(0, At(3, 1));
        ws.PaintLand(0, At(4, 1));

        Assert.Equal(1, CapitalOccupantCount(ws.Grid));
    }

    [Fact]
    public void PaintWater_RemovesLandTile()
    {
        var ws = new LevelWorkspace(6, 4);
        ws.PaintLand(0, At(1, 1));
        ws.PaintWater(At(1, 1));

        Assert.Null(ws.Grid.Get(At(1, 1)));
        Assert.Contains(At(1, 1), ws.Water);
    }

    [Fact]
    public void ToggleGoldThenMountain_AreMutuallyExclusive()
    {
        var ws = new LevelWorkspace(6, 4);
        ws.PaintLand(0, At(1, 1));
        ws.ToggleGold(At(1, 1));
        ws.ToggleMountain(At(1, 1));

        HexTile tile = ws.Grid.Get(At(1, 1))!;
        Assert.True(tile.IsMountain);
        Assert.False(tile.IsGold);
    }

    [Fact]
    public void PaintCapital_MovesCapitalToPickedCoord()
    {
        var ws = new LevelWorkspace(8, 4);
        ws.PaintLand(0, At(1, 1));
        ws.PaintLand(0, At(2, 1));
        ws.PaintLand(0, At(3, 1));

        ws.PaintCapital(At(3, 1));

        Territory territory = Assert.Single(ws.Territories);
        Assert.Equal(At(3, 1), territory.Capital);
        Assert.Equal(1, CapitalOccupantCount(ws.Grid));
    }

    [Fact]
    public void Validate_FlagsActiveSlotWithNoLand()
    {
        var ws = new LevelWorkspace(6, 4);
        ws.PaintLand(0, At(1, 1));
        ws.PaintLand(0, At(2, 1));
        ws.SetSlot(1, PlayerKind.Computer);

        IReadOnlyList<string> problems = ws.Validate();
        Assert.Contains(problems, p => p.Contains("owns no territory"));
    }

    [Fact]
    public void Validate_TwoPaintedSlots_IsClean()
    {
        var ws = new LevelWorkspace(8, 4);
        ws.PaintLand(0, At(1, 1));
        ws.PaintLand(0, At(2, 1));
        ws.PaintLand(1, At(5, 2));
        ws.PaintLand(1, At(6, 2));

        Assert.Empty(ws.Validate());
    }

    [Fact]
    public void RectCoords_IsInclusiveOfBothCorners()
    {
        List<HexCoord> coords = LevelWorkspace.RectCoords(1, 1, 2, 3).ToList();

        Assert.Equal(6, coords.Count);
        Assert.Contains(At(1, 1), coords);
        Assert.Contains(At(2, 3), coords);
    }

    [Fact]
    public void RoundTrip_PreservesBoardRosterAndMode()
    {
        var ws = new LevelWorkspace(8, 5);
        ws.PaintLand(0, At(1, 1));
        ws.PaintLand(0, At(2, 1));
        ws.ToggleGold(At(2, 1));
        ws.PaintLand(1, At(5, 3));
        ws.PaintLand(1, At(6, 3));
        ws.ToggleMountain(At(6, 3));
        ws.SetSlot(0, PlayerKind.Human);
        ws.SetSlot(1, PlayerKind.Computer, Difficulty.Commander);
        ws.Mode = GameMode.RisingTides;
        ws.MapSeed = 123;

        LevelWorkspace back = LevelWorkspace.FromJson(ws.ToJson("round-trip"));

        Assert.Equal(8, back.Cols);
        Assert.Equal(5, back.Rows);
        Assert.Equal(GameMode.RisingTides, back.Mode);
        Assert.Equal(123, back.MapSeed);
        Assert.Equal(PlayerKind.Human, back.KindFor(0));
        Assert.Equal(PlayerKind.Computer, back.KindFor(1));
        Assert.Equal(Difficulty.Commander, back.DifficultyFor(1));
        Assert.Equal(PlayerKind.None, back.KindFor(2));

        Assert.Equal(PlayerId.FromIndex(0), back.Grid.Get(At(1, 1))!.Owner);
        Assert.True(back.Grid.Get(At(2, 1))!.IsGold);
        Assert.True(back.Grid.Get(At(6, 3))!.IsMountain);
        Assert.Equal(2, back.Territories.Count);
        Assert.All(back.Territories, t => Assert.True(t.HasCapital));
        Assert.Equal(ws.Water.Count, back.Water.Count);
    }

    [Fact]
    public void ToJson_ProducesTurnZeroStartingMap()
    {
        var ws = new LevelWorkspace(6, 4);
        ws.PaintLand(0, At(1, 1));
        ws.PaintLand(0, At(2, 1));
        ws.PaintLand(1, At(4, 2));
        ws.PaintLand(1, At(3, 2));

        LoadedSave loaded = SaveSerializer.Deserialize(ws.ToJson("turnzero"));

        Assert.Equal(0, loaded.State.Turns.TurnNumber);
        Assert.Equal(int.MaxValue, loaded.MaxTurnNumber);
        Assert.True(loaded.MapHasBakedKinds);
    }

    [Fact]
    public void NewProcedural_GeneratesLandDeterministically()
    {
        var options = new MapGenOptions();
        LevelWorkspace a = LevelWorkspace.NewProcedural(
            12, 9, seed: 42, options, GameMode.Freeform, activeSlots: 3);
        LevelWorkspace b = LevelWorkspace.NewProcedural(
            12, 9, seed: 42, options, GameMode.Freeform, activeSlots: 3);

        Assert.NotEmpty(a.Grid.Tiles);
        Assert.Equal(a.RenderText(), b.RenderText());
        Assert.Equal(PlayerKind.Computer, a.KindFor(0));
        Assert.Equal(PlayerKind.Computer, a.KindFor(2));
        Assert.Equal(PlayerKind.None, a.KindFor(3));
    }
}
