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
    private readonly ScriptCursor _cursor;

    public TutorialPreview(IReadOnlyList<ReplayBeat> script, GameState state,
        ScriptCursor? cursor = null)
    {
        _script = script;
        _state = state;
        _cursor = cursor ?? new ScriptCursor();
    }

    /// <summary>Fires when the dev attempts an action that doesn't
    /// match the next expected player-0 beat (or when no player-0
    /// turn is active). Payload: the expected beat (or null at
    /// end-of-script) and a human-readable reason.</summary>
    public event Action<ReplayBeat?, string>? PlayerActionRejected;

    /// <summary>Fires once when the final beat in the script is consumed
    /// (cursor reaches the tail), signalling the tutorial is genuinely
    /// over. NOT fired merely because no further player-0 beats remain —
    /// a pending narration beat makes <see cref="NextPlayer0Beat"/> null
    /// without the tutorial being complete.</summary>
    public event Action? TutorialFinished;

    /// <summary>True once every beat in the script has been consumed
    /// (the shared cursor has advanced past the tail). This — not
    /// <see cref="NextPlayer0Beat"/> being null — is the authoritative
    /// "tutorial done" signal.</summary>
    public bool IsComplete => _cursor.Index >= _script.Count;

    /// <summary>
    /// The next expected player-0 beat, or null if no further player-0
    /// beats remain in the script. Skip-scans past beats for other
    /// actors. Returns null if a <see cref="TutorialOnlyBeat"/> sits
    /// between the cursor and the next player-0 beat — that beat must
    /// be consumed by <c>TutorialNarrationDriver</c> first, and the
    /// gate keeps <c>TutorialPreviewCues</c> from painting a cue for
    /// the action beat behind the narration. Used by the UI to surface
    /// "next expected" hint text.
    /// </summary>
    public ReplayBeat? NextPlayer0Beat
    {
        get
        {
            for (int i = _cursor.Index; i < _script.Count; i++)
            {
                if (_script[i] is TutorialOnlyBeat) return null;
                if (_script[i].Actor == 0) return _script[i];
            }
            return null;
        }
    }

    /// <summary>
    /// Pre-placement guard for the four Buy radio buttons. Returns true
    /// iff the next expected player-0 beat is a <see cref="ReplayBuyBeat"/>
    /// at the proposed level. When the script expects a different level
    /// (or a non-buy beat, or the tutorial is complete), the controller
    /// refuses the mode switch — the dev must follow the script exactly
    /// and can't pre-select a stronger unit before the placement click.
    /// Non-mutating: does NOT advance the cursor; that happens only when
    /// a complete action beat lands in <see cref="TryAccept"/>.
    /// </summary>
    public bool AllowBuyLevel(UnitLevel level)
    {
        return NextPlayer0Beat is ReplayBuyBeat buy && buy.Level == level;
    }

    public bool TryAccept(ReplayBeat attempted)
    {
        if (_state.Turns.CurrentPlayerIndex != 0)
        {
            Reject(NextPlayer0Beat,
                "Wait for your turn (you play Red — player 0).");
            return false;
        }

        // The shared cursor points to the next un-consumed script
        // beat. Control is on player 0, so the next beat MUST be
        // player-0-owned for the script to stay in sync.
        if (_cursor.Index >= _script.Count)
        {
            Reject(null,
                "Tutorial already complete — no further moves expected.");
            return false;
        }
        ReplayBeat expected = _script[_cursor.Index];
        if (expected.Actor != 0)
        {
            Reject(expected,
                "Cursor desync: expected a player-0 beat next but the script "
                + $"points to actor {expected.Actor}.");
            return false;
        }
        if (!BeatsMatch(expected, attempted))
        {
            Reject(expected,
                $"Expected {DescribeBeat(expected)}; got {DescribeBeat(attempted)}.");
            return false;
        }

        // Match — advance the shared cursor. The AI side picks up
        // from the new index on its next call.
        Log.Info(Log.LogCategory.Tutorial,
            $"[TutorialPreview] executed player-0 beat #{expected.Index} "
            + $"{DescribeBeat(expected)} (cursor {_cursor.Index}→{_cursor.Index + 1}/{_script.Count})");
        _cursor.Advance();
        if (IsComplete)
        {
            Log.Info(Log.LogCategory.Tutorial,
                $"[TutorialPreview] script tail reached ({_cursor.Index}/{_script.Count}); firing TutorialFinished");
            TutorialFinished?.Invoke();
        }
        return true;
    }

    // Log every rejection (with cursor + expected-beat context) before
    // surfacing it on screen, so a stalled preview is diagnosable from
    // the captured stdout, not just the transient on-screen toast.
    private void Reject(ReplayBeat? expected, string reason)
    {
        Log.Warn(Log.LogCategory.Tutorial,
            $"[TutorialPreview] REJECTED at cursor {_cursor.Index}/{_script.Count} "
            + $"(currentPlayer {_state.Turns.CurrentPlayerIndex}, "
            + $"expected {(expected == null ? "none" : "#" + expected.Index + " " + DescribeBeat(expected))}): {reason}");
        PlayerActionRejected?.Invoke(expected, reason);
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
