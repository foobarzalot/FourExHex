using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Runtime state for a tutorial in Preview. Tracks the next-expected-
/// beat pointer, exposes the events the gated view wrappers fire into,
/// and provides the AI chooser delegate handed to GameController.
///
/// Phase 3c minimal scope: only EndTurnBeat is gated; AI chooser
/// always falls through to AiDispatcher (no scripted-AI logic);
/// Snapshots stays empty (Phase 13 populates it). Phase 13 will also
/// add a Bind(GameController, ...) method so the player can pull
/// state for snapshot capture.
///
/// Pure C# / Godot-free (only references Godot's Color struct via
/// AiChooser's signature, which is value-type and test-friendly).
/// </summary>
public sealed class TutorialPlayer
{
    private readonly Tutorial _tutorial;
    private int _nextBeatIndex;
    private readonly List<GameStateSnapshot> _snapshots = new();

    public TutorialPlayer(Tutorial tutorial)
    {
        _tutorial = tutorial;
        _nextBeatIndex = 0;
    }

    /// <summary>Next beat the player is expected to perform, or null if finished.</summary>
    public Beat? NextExpectedPlayerBeat =>
        _nextBeatIndex < _tutorial.Beats.Count ? _tutorial.Beats[_nextBeatIndex] : null;

    /// <summary>Index of the most-recently-applied beat, or -1 before any apply.</summary>
    public int CurrentBeatIndex => _nextBeatIndex - 1;

    /// <summary>
    /// Per-beat state snapshots for the scrubber (Phase 13). Empty in
    /// Phase 3c — population is deferred until the scrubber consumes them.
    /// </summary>
    public IReadOnlyList<GameStateSnapshot> Snapshots => _snapshots;

    /// <summary>Fires after a beat is applied. Argument is the beat's index.</summary>
    public event Action<int>? BeatApplied;

    /// <summary>Fires when the player attempts an action that doesn't match the
    /// next expected beat. The PreviewPane subscribes and shows a toast.</summary>
    public event Action<Beat?, string>? PlayerActionRejected;

    /// <summary>Fires once after the last beat is applied.</summary>
    public event Action? TutorialFinished;

    /// <summary>
    /// AI chooser delegate handed to GameController. Phase 3c always
    /// falls through to AiDispatcher (no scripted-AI logic). Phase 10
    /// adds the scripted-beat-as-AiAction path here.
    /// </summary>
    public AiAction? AiChooser(GameState state, Color forPlayer,
                                HashSet<HexCoord> visitedCapitals, Random rng)
        => AiDispatcher.ChooseForCurrentPlayer(state, forPlayer, visitedCapitals, rng);

    /// <summary>
    /// Called by <see cref="TutorialGatedHudView"/> when the player
    /// clicks End Turn. If the next beat is an EndTurnBeat, advances
    /// the pointer + fires events + returns true (caller forwards the
    /// click to the controller). If the next beat is anything else,
    /// fires PlayerActionRejected and returns false (caller does NOT
    /// forward).
    /// </summary>
    public bool TryAdvanceForEndTurn()
    {
        if (NextExpectedPlayerBeat is EndTurnBeat etb && TutorialValidator.MatchesEndTurn(etb))
        {
            int applied = _nextBeatIndex;
            _nextBeatIndex++;
            BeatApplied?.Invoke(applied);
            if (_nextBeatIndex >= _tutorial.Beats.Count)
            {
                TutorialFinished?.Invoke();
            }
            return true;
        }
        NotifyRejected("End Turn");
        return false;
    }

    /// <summary>
    /// Fire the soft-reject event. Used by gated wrappers when an
    /// input can never match (e.g. a tile click in 3c, where no
    /// tile-action beats exist yet) or when the next expected beat is
    /// of a different kind.
    /// </summary>
    public void NotifyRejected(string attempted)
    {
        Beat? next = NextExpectedPlayerBeat;
        string reason = TutorialValidator.ReasonMismatch(next, attempted);
        PlayerActionRejected?.Invoke(next, reason);
    }
}
