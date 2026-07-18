// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Pins the silent-mode seam at its new home: <see cref="GameOperations.IsSilent"/>
/// decides whether a per-action cue reaches the view, and
/// <see cref="GameOperations.EmitSound"/> / <see cref="GameOperations.EmitDestruction"/>
/// drop while silent. The views (<c>HexMapView</c>, <see cref="MockHexMapView"/>)
/// no longer self-gate — they play whatever they're handed — so this test
/// exercises the controller-layer decision directly.
/// </summary>
public class GameOperationsSilentGateTests
{
    // Build a bare GameOperations wired to a MockHexMapView, with the
    // silent inputs the gate reads under our control: the aiSilentMode
    // predicate, the replay-instant predicate, and which player's turn it
    // is (only an AI turn can be silenced by aiSilentMode).
    private static (GameOperations Ops, MockHexMapView Map, GameState State) BuildOps(
        bool aiSilent, bool replayInstant, bool currentPlayerIsAi)
    {
        var red = new Player("Red", PlayerId.FromIndex(0), PlayerKind.Human);
        var blue = new Player("Blue", PlayerId.FromIndex(1), PlayerKind.Computer);
        var players = new List<Player> { red, blue };
        int current = currentPlayerIsAi ? 1 : 0;

        HexGrid grid = TestHelpers.BuildRectGrid(2, 1, players[current].Id);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(
            grid, territories, players,
            new TurnState(players, current, turnNumber: 1), new Treasury());

        var map = new MockHexMapView();
        var ops = new GameOperations(
            state, new SessionState(), map, new MockHudView(),
            recordingMode: false,
            previewMode: false,
            isReplayMode: () => false,
            aiSilentMode: () => aiSilent,
            isReplayInstantActive: () => replayInstant,
            clearUndoAndReplayBookkeeping: () => { },
            onGameEnded: () => { },
            onHumanTurnStarted: () => { },
            maxTurnNumber: int.MaxValue,
            masterSeed: 0,
            onAfterRefresh: null);
        return (ops, map, state);
    }

    [Fact]
    public void EmitSound_DropsWhileInstantAiTurn()
    {
        (GameOperations ops, MockHexMapView map, GameState _) =
            BuildOps(aiSilent: true, replayInstant: false, currentPlayerIsAi: true);

        Assert.True(ops.IsSilent());
        ops.EmitSound(SoundEffect.UnitPlaced, HexCoord.FromOffset(0, 0));
        ops.EmitDestruction(HexCoord.FromOffset(0, 0), new Unit(PlayerId.FromIndex(0)));

        Assert.Empty(map.UnitPlacedSounds);
        Assert.Empty(map.DestructionEffects);
    }

    [Fact]
    public void EmitSound_DropsDuringInstantReplay()
    {
        // Replay-instant silences even on a human turn (playback re-runs
        // recorded human actions).
        (GameOperations ops, MockHexMapView map, GameState _) =
            BuildOps(aiSilent: false, replayInstant: true, currentPlayerIsAi: false);

        Assert.True(ops.IsSilent());
        ops.EmitSound(SoundEffect.Bankruptcy);

        Assert.Equal(0, map.BankruptcySoundCount);
    }

    [Fact]
    public void EmitSound_PlaysWhenNotSilent()
    {
        (GameOperations ops, MockHexMapView map, GameState _) =
            BuildOps(aiSilent: false, replayInstant: false, currentPlayerIsAi: true);

        Assert.False(ops.IsSilent());
        ops.EmitSound(SoundEffect.UnitPlaced, HexCoord.FromOffset(1, 0));
        ops.EmitDestruction(HexCoord.FromOffset(1, 0), new Unit(PlayerId.FromIndex(0)));

        Assert.Single(map.UnitPlacedSounds);
        Assert.Single(map.DestructionEffects);
    }

    [Fact]
    public void EmitSound_PlaysOnHumanTurnEvenWhenAiSilentPredicateOn()
    {
        // The Instant setting is on (aiSilent true) but it's a human's own
        // turn — they always hear their own cues.
        (GameOperations ops, MockHexMapView map, GameState _) =
            BuildOps(aiSilent: true, replayInstant: false, currentPlayerIsAi: false);

        Assert.False(ops.IsSilent());
        ops.EmitSound(SoundEffect.GameWon);

        Assert.Equal(1, map.GameWonSoundCount);
    }

    // --- Terrain-capture FX gate (issue #155) -----------------------------

    [Fact]
    public void EmitTerrainCaptureFx_DropsWhileInstantAiTurn()
    {
        (GameOperations ops, MockHexMapView map, GameState state) =
            BuildOps(aiSilent: true, replayInstant: false, currentPlayerIsAi: true);
        state.Grid.Get(HexCoord.FromOffset(0, 0))!.IsGold = true;
        state.Grid.Get(HexCoord.FromOffset(1, 0))!.IsMountain = true;

        Assert.True(ops.IsSilent());
        ops.EmitTerrainCaptureFx(HexCoord.FromOffset(0, 0));
        ops.EmitTerrainCaptureFx(HexCoord.FromOffset(1, 0));

        Assert.Empty(map.TerrainCaptureEffects);
        Assert.Empty(map.GoldCapturedSounds);
        Assert.Empty(map.MountainCapturedSounds);
    }

    [Fact]
    public void EmitTerrainCaptureFx_PlaysWhenNotSilent()
    {
        (GameOperations ops, MockHexMapView map, GameState state) =
            BuildOps(aiSilent: false, replayInstant: false, currentPlayerIsAi: true);
        state.Grid.Get(HexCoord.FromOffset(0, 0))!.IsGold = true;
        state.Grid.Get(HexCoord.FromOffset(1, 0))!.IsMountain = true;

        Assert.False(ops.IsSilent());
        ops.EmitTerrainCaptureFx(HexCoord.FromOffset(0, 0));
        ops.EmitTerrainCaptureFx(HexCoord.FromOffset(1, 0));

        Assert.Equal(
            new[]
            {
                (HexCoord.FromOffset(0, 0), TerrainFeature.Gold),
                (HexCoord.FromOffset(1, 0), TerrainFeature.Mountain),
            },
            map.TerrainCaptureEffects);
        Assert.Single(map.GoldCapturedSounds);
        Assert.Equal(HexCoord.FromOffset(0, 0), map.GoldCapturedSounds[0]);
        Assert.Single(map.MountainCapturedSounds);
        Assert.Equal(HexCoord.FromOffset(1, 0), map.MountainCapturedSounds[0]);
    }

    [Fact]
    public void EmitMountainTowerFx_MountainTile_FiresShakeAndThud()
    {
        (GameOperations ops, MockHexMapView map, GameState state) =
            BuildOps(aiSilent: false, replayInstant: false, currentPlayerIsAi: true);
        state.Grid.Get(HexCoord.FromOffset(1, 0))!.IsMountain = true;

        ops.EmitMountainTowerFx(HexCoord.FromOffset(1, 0));

        (HexCoord Coord, TerrainFeature Terrain) fx = Assert.Single(map.TerrainCaptureEffects);
        Assert.Equal(TerrainFeature.Mountain, fx.Terrain);
        Assert.Single(map.MountainCapturedSounds);
    }

    [Fact]
    public void EmitMountainTowerFx_PlainTile_FiresNothing()
    {
        (GameOperations ops, MockHexMapView map, GameState _) =
            BuildOps(aiSilent: false, replayInstant: false, currentPlayerIsAi: true);

        ops.EmitMountainTowerFx(HexCoord.FromOffset(1, 0));

        Assert.Empty(map.TerrainCaptureEffects);
        Assert.Empty(map.MountainCapturedSounds);
    }

    [Fact]
    public void EmitMountainTowerFx_DropsWhileInstantAiTurn()
    {
        (GameOperations ops, MockHexMapView map, GameState state) =
            BuildOps(aiSilent: true, replayInstant: false, currentPlayerIsAi: true);
        state.Grid.Get(HexCoord.FromOffset(1, 0))!.IsMountain = true;

        Assert.True(ops.IsSilent());
        ops.EmitMountainTowerFx(HexCoord.FromOffset(1, 0));

        Assert.Empty(map.TerrainCaptureEffects);
        Assert.Empty(map.MountainCapturedSounds);
    }

    [Fact]
    public void EmitTerrainCaptureFx_PlainTile_FiresBaselineEffectNoSound()
    {
        (GameOperations ops, MockHexMapView map, GameState _) =
            BuildOps(aiSilent: false, replayInstant: false, currentPlayerIsAi: true);

        ops.EmitTerrainCaptureFx(HexCoord.FromOffset(0, 0));

        (HexCoord Coord, TerrainFeature Terrain) fx = Assert.Single(map.TerrainCaptureEffects);
        Assert.Equal(TerrainFeature.None, fx.Terrain);
        Assert.Empty(map.GoldCapturedSounds);
        Assert.Empty(map.MountainCapturedSounds);
    }
}
