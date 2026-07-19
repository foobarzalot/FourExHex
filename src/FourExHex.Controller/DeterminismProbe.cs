// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;

/// <summary>
/// Result of one determinism probe run — the cross-platform fingerprint
/// triple (map-gen stream hash, final state checksum, cumulative RNG
/// digest) plus context for display.
/// </summary>
public sealed record DeterminismProbeResult(
    int Seed,
    ulong MapGenRngStreamHash,
    string FinalChecksum,
    ulong RngStreamDigest,
    int Turns,
    int WinnerIndex);

/// <summary>
/// In-process determinism check: runs the same seeded all-AI quick game
/// as the <c>FOUREXHEX_6AI_QUICK</c> headless mode (18×13, six Computer
/// players at Soldier, Freeform, turn cap 200) entirely inline via
/// <see cref="SynchronousAiPacer"/>, and returns the digest triple.
/// Identical output on every machine and runtime is issue #59's
/// empirical cross-platform proof; the cheat menu surfaces it on
/// targets where env vars can't (iOS/Android). Touches no global state
/// (no <c>GameSettings</c>, no <c>Log</c> levels).
/// </summary>
public static class DeterminismProbe
{
    public const int DefaultSeed = 42;

    // The FOUREXHEX_6AI_QUICK configuration, replicated exactly so the
    // probe's fingerprint equals the headless run's digest lines.
    private const int Cols = 18;
    private const int Rows = 13;
    private const int MaxTurns = 200;
    private const int PlayerCount = 6;

    public static DeterminismProbeResult Run(
        IHexMapView map, IHudView hud, int seed = DefaultSeed)
    {
        var players = new List<Player>(PlayerCount);
        for (int i = 0; i < PlayerCount; i++)
        {
            players.Add(new Player($"P{i}", PlayerId.FromIndex(i),
                PlayerKind.Computer, Difficulty.Soldier));
        }

        GameState state = ProceduralGame.Build(
            Cols, Rows, players, seed, out ulong mapGenRngStreamHash);
        var session = new SessionState();
        var controller = new GameController(
            state, session, map, hud,
            seed: seed,
            aiPacer: new SynchronousAiPacer(),
            aiChooser: AiDispatcher.ChooseForCurrentPlayer,
            maxTurnNumber: MaxTurns);
        // All-AI + synchronous pacer: the whole game runs inline and
        // StartGame returns at GameEnded (win or turn cap).
        controller.StartGame();

        return new DeterminismProbeResult(
            seed,
            mapGenRngStreamHash,
            GameStateChecksum.Compute(state),
            controller.RngStreamDigest,
            state.Turns.TurnNumber,
            session.Winner?.Index ?? -1);
    }
}
