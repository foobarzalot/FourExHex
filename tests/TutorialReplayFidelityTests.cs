// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.IO;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Fidelity gate for the SHIPPED tutorial and help-menu demo replays in
/// <c>tutorials/*.json</c>. Each file is a save carrying a Tutorial +
/// Replay block; this test loads the real file from the repo and drains
/// a full instant replay, asserting every recorded beat stays legal (no
/// exception) and the engine's own divergence check stays silent. Any
/// rules or RNG change that would derail one of these authored
/// recordings fails here — BEFORE a player ever sees a broken tutorial.
/// </summary>
public class TutorialReplayFidelityTests
{
    public static TheoryData<string> ShippedTutorialFiles()
    {
        var data = new TheoryData<string>();
        foreach (string path in Directory.GetFiles(TutorialsDir(), "*.json"))
        {
            data.Add(Path.GetFileName(path));
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(ShippedTutorialFiles))]
    public void ShippedTutorial_ReplaysToCompletion(string fileName)
    {
        string json = File.ReadAllText(Path.Combine(TutorialsDir(), fileName));
        LoadedSave loaded = SaveSerializer.Deserialize(json);
        Assert.NotNull(loaded.Tutorial);
        Assert.NotNull(loaded.Replay);

        // Authored tutorials store the builder's authoring state, not a
        // played-out end board, so there is no end checksum to compare
        // against — the app plays them in preview mode for the same
        // reason. The contract this gate enforces is: every recorded
        // beat stays LEGAL (no exception) through a full hands-free
        // drain, and playback runs to completion. Mirrors
        // InstructionDemoView's construction: replay-playback engine
        // only — NO aiChooser (all actors' turns come from the beat
        // log; a live chooser would let AI play over the recording).
        var controller = new GameController(
            loaded.State, new SessionState(),
            new MockHexMapView(), new MockHudView(),
            seed: loaded.MasterSeed,
            aiPacer: new SynchronousAiPacer(),
            maxTurnNumber: loaded.MaxTurnNumber,
            previewMode: true,
            loadedReplay: loaded.Replay);
        bool ended = false;
        controller.ReplayEnded += () => ended = true;
        // SynchronousAiPacer drains the entire replay inline; any beat
        // made illegal by a rules/RNG change throws out of here.
        controller.BeginReplay();

        Assert.True(ended, "replay did not run to completion");
        Assert.False(controller.IsReplayMode);
    }

    /// <summary>The repo's tutorials/ directory, located by walking up
    /// from the test assembly (bin/Debug/net8.0/...).</summary>
    private static string TutorialsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "tutorials");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "Could not locate the repo tutorials/ directory above " +
            AppContext.BaseDirectory);
    }
}
