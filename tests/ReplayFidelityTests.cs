using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// End-to-end replay fidelity: six computer AI players run a full
/// game under a synchronous pacer, the live final state is hashed,
/// the game is serialized and deserialized, and the deserialized
/// controller's replay is hashed. All three checksums (live, saved,
/// replayed) must match — proves the recorded beat log plus the
/// initial-state snapshot reconstitute the live game exactly, and
/// that the JSON round-trip preserves both.
///
/// Heavier than <see cref="ReplayPlaybackTests"/>'s 2-player setups:
/// hundreds of beats across move/buy/build/end-turn/capture flows
/// with deterministic per-turn-reseed RNG.
/// </summary>
public class ReplayFidelityTests
{
    [Fact]
    public void Replay_SixComputerPlayers_MatchesSavedStateChecksum()
    {
        const int MasterSeed = 12345;
        const int MaxTurns = 30;
        const int Cols = 18;
        const int Rows = 13;

        // --- Phase 1: live game ----------------------------------------------
        IReadOnlyList<Player> players = BuildSixComputerPlayers();
        (GameState liveState, var liveController, var liveMap, var liveHud) =
            BuildHeadlessGame(players, MasterSeed, MaxTurns, Cols, Rows);
        liveController.StartGame();
        // All-AI + SynchronousAiPacer → StartGame returns when GameEnded
        // (natural win) or the turn cap fires.

        string liveChecksum = GameStateChecksum.Compute(liveState);
        Assert.True(liveController.ReplayBeats.Count > 0,
            $"Expected the heuristic game to produce beats; got 0.");

        // --- Phase 2: serialize → deserialize round-trip ---------------------
        Replay replayPayload = new Replay(
            liveController.InitialReplaySnapshot!,
            liveController.InitialReplayTurnNumber,
            liveController.InitialReplayCurrentPlayerIndex,
            liveController.ReplayBeats);
        string json = SaveSerializer.Serialize(liveState, MasterSeed, players,
            "fidelity", MaxTurns, replay: replayPayload);
        LoadedSave loaded = SaveSerializer.Deserialize(json);
        Assert.NotNull(loaded.Replay);

        string savedChecksum = GameStateChecksum.Compute(loaded.State);
        Assert.Equal(liveChecksum, savedChecksum);

        // --- Phase 3: rebuild from save → BeginReplay → checksum ------------
        // Construct a fresh controller with the loaded state + replay.
        // Don't call StartGame/Resume — both would run start-of-turn
        // bookkeeping or re-fire AI turns. BeginReplay alone restores
        // the initial snapshot and steps through the beat log.
        var replayMap = new MockHexMapView();
        var replayHud = new MockHudView();
        var replayController = new GameController(
            loaded.State, new SessionState(),
            replayMap, replayHud,
            seed: loaded.MasterSeed,
            aiPacer: new SynchronousAiPacer(),
            aiChooser: AiDispatcher.ChooseForCurrentPlayer,
            maxTurnNumber: loaded.MaxTurnNumber,
            loadedReplay: loaded.Replay);
        replayController.BeginReplay();
        // SynchronousAiPacer drains the entire replay inline before
        // BeginReplay returns.

        string replayChecksum = GameStateChecksum.Compute(loaded.State);
        Assert.Equal(liveChecksum, replayChecksum);

        // A faithful replay reproduces the recorded end board, so the
        // engine's own divergence check (recorded-vs-replayed checksum,
        // #77) must flag nothing.
        Assert.Null(replayController.LastReplayDivergence);
    }

    /// <summary>
    /// Rising Tides (issues #56/#85) replay fidelity: the same end-to-end check
    /// as the freeform case above, but the per-turn tide erosion must reproduce
    /// exactly under replay. The first player's turn-1 forecast is seeded in
    /// <see cref="GameController.Resume"/> on a live fresh start; replay restores
    /// the initial snapshot via <see cref="GameController.BeginReplay"/>, which
    /// must seed that same turn-1 forecast — otherwise the very first end-of-turn
    /// tide submerges different tiles, the board diverges, and a recorded AI
    /// action later lands on a now-submerged tile.
    /// </summary>
    [Fact]
    public void Replay_SixComputerPlayers_RisingTides_MatchesSavedStateChecksum()
    {
        const int MasterSeed = 12345;
        const int MaxTurns = 30;
        const int Cols = 18;
        const int Rows = 13;

        IReadOnlyList<Player> players = BuildSixComputerPlayers();
        (GameState liveState, var liveController, _, _) =
            BuildHeadlessGame(players, MasterSeed, MaxTurns, Cols, Rows,
                mode: GameMode.RisingTides);
        liveController.StartGame();

        string liveChecksum = GameStateChecksum.Compute(liveState);
        Assert.True(liveController.ReplayBeats.Count > 0,
            "Expected the Rising Tides game to produce beats; got 0.");

        Replay replayPayload = new Replay(
            liveController.InitialReplaySnapshot!,
            liveController.InitialReplayTurnNumber,
            liveController.InitialReplayCurrentPlayerIndex,
            liveController.ReplayBeats);
        string json = SaveSerializer.Serialize(liveState, MasterSeed, players,
            "tides-fidelity", MaxTurns, replay: replayPayload);
        LoadedSave loaded = SaveSerializer.Deserialize(json);
        Assert.NotNull(loaded.Replay);

        var replayController = new GameController(
            loaded.State, new SessionState(),
            new MockHexMapView(), new MockHudView(),
            seed: loaded.MasterSeed,
            aiPacer: new SynchronousAiPacer(),
            aiChooser: AiDispatcher.ChooseForCurrentPlayer,
            maxTurnNumber: loaded.MaxTurnNumber,
            loadedReplay: loaded.Replay);
        replayController.BeginReplay();

        string replayChecksum = GameStateChecksum.Compute(loaded.State);
        Assert.Equal(liveChecksum, replayChecksum);
        Assert.Null(replayController.LastReplayDivergence);
    }

    /// <summary>
    /// #77: a replay that does NOT reproduce the recorded final board is
    /// detected. We can't stage a real rule change inside one binary, so we
    /// simulate it: load a save, then mutate the recorded end state
    /// (<see cref="GameController.BeginReplay"/> captures its checksum as the
    /// baseline) before replaying. The rewind+beats reproduce the original
    /// board, which now differs from the tampered baseline, so the controller
    /// must surface a <see cref="ReplayDivergence"/>.
    /// </summary>
    [Fact]
    public void Replay_DivergentEndState_IsDetected()
    {
        const int MasterSeed = 12345;
        const int MaxTurns = 30;
        const int Cols = 18;
        const int Rows = 13;

        IReadOnlyList<Player> players = BuildSixComputerPlayers();
        (GameState liveState, var liveController, _, _) =
            BuildHeadlessGame(players, MasterSeed, MaxTurns, Cols, Rows);
        liveController.StartGame();
        Assert.True(liveController.ReplayBeats.Count > 0);

        Replay replayPayload = new Replay(
            liveController.InitialReplaySnapshot!,
            liveController.InitialReplayTurnNumber,
            liveController.InitialReplayCurrentPlayerIndex,
            liveController.ReplayBeats);
        string json = SaveSerializer.Serialize(liveState, MasterSeed, players,
            "divergence", MaxTurns, replay: replayPayload);
        LoadedSave loaded = SaveSerializer.Deserialize(json);
        Assert.NotNull(loaded.Replay);

        // Tamper the recorded end board with a checksum-visible change that
        // the beat log will NOT reproduce: bump a capital's treasury gold.
        HexCoord capital = FindAnyCapital(loaded.State);
        loaded.State.Treasury.SetGold(capital,
            loaded.State.Treasury.GetGold(capital) + 9999);
        string tamperedChecksum = GameStateChecksum.Compute(loaded.State);

        var replayController = new GameController(
            loaded.State, new SessionState(),
            new MockHexMapView(), new MockHudView(),
            seed: loaded.MasterSeed,
            aiPacer: new SynchronousAiPacer(),
            aiChooser: AiDispatcher.ChooseForCurrentPlayer,
            maxTurnNumber: loaded.MaxTurnNumber,
            loadedReplay: loaded.Replay);
        replayController.BeginReplay();

        ReplayDivergence? divergence = replayController.LastReplayDivergence;
        Assert.NotNull(divergence);
        // Baseline (Expected) is the tampered board; replay (Actual) is the
        // faithful reproduction — they must differ.
        Assert.Equal(tamperedChecksum, divergence!.Expected);
        Assert.NotEqual(divergence.Expected, divergence.Actual);
    }

    private static HexCoord FindAnyCapital(GameState state)
    {
        foreach (Territory t in state.Territories)
        {
            if (t.HasCapital) return t.Capital!.Value;
        }
        throw new System.InvalidOperationException(
            "Expected at least one territory with a capital.");
    }

    private static IReadOnlyList<Player> BuildSixComputerPlayers()
    {
        // Match the FOUREXHEX_6AI palette / naming convention so the
        // map generator's player-aware territory seeding behaves the
        // same as the diagnostic launch.
        var list = new List<Player>(GameSettings.PlayerConfig.Length);
        for (int i = 0; i < GameSettings.PlayerConfig.Length; i++)
        {
            (string name, _) = GameSettings.PlayerConfig[i];
            list.Add(new Player(name, PlayerId.FromIndex(i), PlayerKind.Computer));
        }
        return list;
    }

    private static (GameState State, GameController Controller, MockHexMapView Map, MockHudView Hud)
        BuildHeadlessGame(IReadOnlyList<Player> players, int masterSeed,
            int maxTurns, int cols, int rows, GameMode mode = GameMode.Freeform)
    {
        MapGenResult mapGen = MapGenerator.BuildInitialGrid(cols, rows, players, masterSeed);
        IReadOnlyList<Territory> raw = TerritoryFinder.FindAll(mapGen.Grid);
        IReadOnlyList<Territory> territories = CapitalReconciler.Reconcile(
            raw, new List<Territory>(), mapGen.Grid);
        var state = new GameState(mapGen.Grid, territories, players,
            new TurnState(players), new Treasury(), mapGen.WaterCoords, mode: mode);
        var map = new MockHexMapView();
        var hud = new MockHudView();
        var controller = new GameController(state, new SessionState(),
            map, hud,
            seed: masterSeed,
            aiPacer: new SynchronousAiPacer(),
            aiChooser: AiDispatcher.ChooseForCurrentPlayer,
            maxTurnNumber: maxTurns);
        return (state, controller, map, hud);
    }
}
