using System.Collections.Generic;

/// <summary>
/// Pure-C# captor for a Record-mode session's Tutorial payload.
/// Extracted from <see cref="BuildPane"/> so the post-Stop survival
/// of the captured data is reachable from xUnit (the Godot-derived
/// BuildPane itself is test-excluded).
///
/// <para>
/// Lifecycle: <c>Begin</c> at the start of a recording session,
/// <c>SetBeats</c> as the controller's <c>_replayBeats</c> grows,
/// <c>Stop</c> when the dev exits Record mode, <c>Snapshot</c>
/// anywhere to read the current Tutorial. The Snapshot must survive
/// Stop — without that, the TutorialBuilder's Save / Preview wiring
/// reads null after a Build→Preview mode switch and Preview never
/// starts.
/// </para>
/// </summary>
public sealed class RecordingCapture
{
    private GameStateSnapshot? _initialSnapshot;
    private int _initialTurnNumber;
    private int _initialPlayerIndex;
    private IReadOnlyList<ReplayBeat>? _beats;

    /// <summary>Begin a fresh recording session.</summary>
    public void Begin(GameStateSnapshot initialSnapshot, int initialTurnNumber, int initialPlayerIndex)
    {
        _initialSnapshot = initialSnapshot;
        _initialTurnNumber = initialTurnNumber;
        _initialPlayerIndex = initialPlayerIndex;
        _beats = null;
    }

    /// <summary>Update the in-flight beat list. Caller passes a fresh
    /// snapshot of the controller's <c>_replayBeats</c>.</summary>
    public void SetBeats(IReadOnlyList<ReplayBeat> beats)
    {
        _beats = beats;
    }

    /// <summary>End the recording session. <see cref="Snapshot"/>
    /// continues returning the captured Tutorial until
    /// <see cref="Begin"/> is called again — the data must survive
    /// the controller teardown so the TutorialBuilder's Save and
    /// Preview wiring can read it after Build mode exits.</summary>
    public void Stop()
    {
        // Intentionally NO field-clearing here. The captured snapshot
        // + beats remain accessible via Snapshot() until the next
        // Begin() call replaces them.
    }

    /// <summary>Current Tutorial snapshot, or null if no recording
    /// has been Begun yet.</summary>
    public Tutorial? Snapshot()
    {
        if (_initialSnapshot == null) return null;
        return new Tutorial
        {
            Title = "",
            Replay = new Replay(
                _initialSnapshot,
                _initialTurnNumber,
                _initialPlayerIndex,
                _beats ?? new List<ReplayBeat>()),
        };
    }
}
