using System;
using System.Collections.Generic;

/// <summary>
/// Player-0 input validator for Tutorial Preview. Tracks the next
/// expected scripted beat for player 0 (the human in Preview) and
/// matches attempted actions against it. Replaces the old
/// <c>TutorialPlayer</c> + gated-view wrappers — the
/// <see cref="GameController"/> now invokes this directly via its
/// <c>humanActionValidator</c> hook, eliminating the parallel gating
/// layer.
///
/// <para>
/// <see cref="TryAccept"/> returns true iff:
///   • The current player is player 0 (mid-turn input always belongs
///     to Red in Preview), AND
///   • The next script beat owned by player 0 matches the attempted
///     beat by kind + key fields.
/// On match the cursor advances past every script beat consumed; if
/// no further player-0 beats remain, <see cref="TutorialFinished"/>
/// fires. On mismatch <see cref="PlayerActionRejected"/> fires and
/// the caller (controller) aborts the action.
/// </para>
/// </summary>
public sealed class TutorialPreview
{
    private readonly IReadOnlyList<ReplayBeat> _script;
    private readonly GameState _state;
    private int _cursor;

    public TutorialPreview(IReadOnlyList<ReplayBeat> script, GameState state)
    {
        _script = script;
        _state = state;
    }

    /// <summary>Fires when the dev attempts an action that doesn't
    /// match the next expected player-0 beat (or when no player-0
    /// turn is active). Payload: the expected beat (or null at
    /// end-of-script) and a human-readable reason.</summary>
    public event Action<ReplayBeat?, string>? PlayerActionRejected;

    /// <summary>Fires once after the final player-0 beat is consumed.</summary>
    public event Action? TutorialFinished;

    /// <summary>
    /// The next expected player-0 beat, or null if no further player-0
    /// beats remain in the script. Skip-scans past beats for other
    /// actors. Used by the UI to surface "next expected" hint text.
    /// </summary>
    public ReplayBeat? NextPlayer0Beat
    {
        get
        {
            for (int i = _cursor; i < _script.Count; i++)
            {
                if (_script[i].Actor == 0) return _script[i];
            }
            return null;
        }
    }

    public bool TryAccept(ReplayBeat attempted)
    {
        if (_state.Turns.CurrentPlayerIndex != 0)
        {
            PlayerActionRejected?.Invoke(NextPlayer0Beat,
                "Wait for your turn (you play Red — player 0).");
            return false;
        }

        // Scan to the next player-0 beat, treating off-actor beats
        // as already-applied background context (the AI driver
        // handles them).
        int scan = _cursor;
        while (scan < _script.Count && _script[scan].Actor != 0)
        {
            scan++;
        }
        if (scan >= _script.Count)
        {
            PlayerActionRejected?.Invoke(null,
                "Tutorial already complete — no further moves expected.");
            return false;
        }

        ReplayBeat expected = _script[scan];
        if (!BeatsMatch(expected, attempted))
        {
            PlayerActionRejected?.Invoke(expected,
                $"Expected {DescribeBeat(expected)}; got {DescribeBeat(attempted)}.");
            return false;
        }

        // Match — advance the cursor past the matched beat.
        _cursor = scan + 1;
        if (NextPlayer0Beat == null)
        {
            TutorialFinished?.Invoke();
        }
        return true;
    }

    /// <summary>Compare two beats by kind + key fields. Stamped
    /// metadata (Index/Turn/Actor) is ignored — only the action
    /// semantics matter.</summary>
    private static bool BeatsMatch(ReplayBeat a, ReplayBeat b)
    {
        return (a, b) switch
        {
            (ReplayMoveBeat x, ReplayMoveBeat y) => x.From.Equals(y.From) && x.To.Equals(y.To),
            (ReplayBuyBeat x, ReplayBuyBeat y) => x.Capital.Equals(y.Capital) && x.To.Equals(y.To) && x.Level == y.Level,
            (ReplayBuildTowerBeat x, ReplayBuildTowerBeat y) => x.Capital.Equals(y.Capital) && x.To.Equals(y.To),
            (ReplayEndTurnBeat _, ReplayEndTurnBeat _) => true,
            (ReplayLongPressRallyBeat x, ReplayLongPressRallyBeat y) => x.Target.Equals(y.Target),
            (ReplayClaimVictoryBeat x, ReplayClaimVictoryBeat y) => x.ThresholdPercent == y.ThresholdPercent,
            (ReplayDismissClaimBeat x, ReplayDismissClaimBeat y) => x.ThresholdPercent == y.ThresholdPercent,
            (ReplayDismissDefeatBeat _, ReplayDismissDefeatBeat _) => true,
            _ => false,
        };
    }

    private static string DescribeBeat(ReplayBeat beat) => beat switch
    {
        ReplayMoveBeat mv => $"Move {mv.From}→{mv.To}",
        ReplayBuyBeat bu => $"Buy {bu.Level} at {bu.To} from {bu.Capital}",
        ReplayBuildTowerBeat bt => $"Build Tower at {bt.To} from {bt.Capital}",
        ReplayEndTurnBeat _ => "End Turn",
        ReplayLongPressRallyBeat rl => $"Rally toward {rl.Target}",
        ReplayClaimVictoryBeat cv => $"Claim Victory at {cv.ThresholdPercent}%",
        ReplayDismissClaimBeat dc => $"Dismiss Claim at {dc.ThresholdPercent}%",
        ReplayDismissDefeatBeat _ => "Dismiss Defeat",
        _ => beat.GetType().Name,
    };
}
