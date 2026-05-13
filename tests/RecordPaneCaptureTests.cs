using System.Collections.Generic;
using Godot;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Regression: RecordPane snapshots its captured tutorial when
/// recording stops so it survives the controller teardown. Without
/// this the live bug was: dev records, switches to Preview,
/// TutorialBuilderScene calls StopRecording (nulls the controller)
/// then reads RecordPane.CurrentTutorial (returns null because
/// _controller is gone) and PreviewPane.Start never runs — the dev
/// sees the post-recording state, not the reset-to-initial state.
///
/// Logic extracted into <see cref="RecordingCapture"/> so this is
/// reachable from xUnit (RecordPane itself is test-excluded).
/// </summary>
public class RecordPaneCaptureTests
{
    private static GameStateSnapshot DummySnapshot()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(2, 2, new Color(1, 0, 0));
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        return GameStateSnapshot.Capture(grid, new Treasury(), territories);
    }

    [Fact]
    public void Snapshot_AfterRecording_ReturnsTutorial()
    {
        var capture = new RecordingCapture();
        capture.Begin(DummySnapshot(), 1, 0);
        capture.SetBeats(new List<ReplayBeat>
        {
            new ReplayEndTurnBeat { Index = 0, Turn = 1, Actor = 0 },
        });
        Tutorial? snap = capture.Snapshot();
        Assert.NotNull(snap);
        Assert.Single(snap!.Replay.Beats);
    }

    [Fact]
    public void Snapshot_AfterStop_StillReturnsTutorial()
    {
        // This is the live-bug regression: post-Stop, the snapshot
        // must survive so the Preview wiring can read it.
        var capture = new RecordingCapture();
        capture.Begin(DummySnapshot(), 1, 0);
        capture.SetBeats(new List<ReplayBeat>
        {
            new ReplayEndTurnBeat { Index = 0, Turn = 1, Actor = 0 },
        });
        capture.Stop();
        Tutorial? snap = capture.Snapshot();
        Assert.NotNull(snap);
        Assert.Single(snap!.Replay.Beats);
    }

    [Fact]
    public void Snapshot_BeforeAnyRecording_ReturnsNull()
    {
        var capture = new RecordingCapture();
        Assert.Null(capture.Snapshot());
    }

    [Fact]
    public void Snapshot_AfterReset_ReturnsNull()
    {
        // Reset is the discard path the TutorialBuilder uses when the
        // dev confirms "switch to Map Edit and clear the recording".
        // Different from Stop (which preserves Snapshot) — Reset is a
        // true erase.
        var capture = new RecordingCapture();
        capture.Begin(DummySnapshot(), 1, 0);
        capture.SetBeats(new List<ReplayBeat>
        {
            new ReplayEndTurnBeat { Index = 0, Turn = 1, Actor = 0 },
        });
        capture.Reset();
        Assert.Null(capture.Snapshot());
    }

    [Fact]
    public void Snapshot_AfterRestart_ReturnsFreshTutorial()
    {
        // Begin a recording, stop it (snapshot exists), then begin a
        // new recording. The new session should start from a blank
        // state — old captured tutorial cleared.
        var capture = new RecordingCapture();
        capture.Begin(DummySnapshot(), 1, 0);
        capture.SetBeats(new List<ReplayBeat>
        {
            new ReplayEndTurnBeat { Index = 0, Turn = 1, Actor = 0 },
        });
        capture.Stop();
        Assert.NotNull(capture.Snapshot());

        // Restart with a new (different) snapshot reference.
        GameStateSnapshot freshSnapshot = DummySnapshot();
        capture.Begin(freshSnapshot, 1, 0);
        capture.SetBeats(new List<ReplayBeat>());
        Tutorial? fresh = capture.Snapshot();
        Assert.NotNull(fresh);
        Assert.Empty(fresh!.Replay.Beats);
    }
}
