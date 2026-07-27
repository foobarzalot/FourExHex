// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;

/// <summary>
/// Shared cursor over a recorded <see cref="ReplayBeat"/> script.
/// Both <see cref="ReplayDrivenAi"/> (AI side) and
/// <see cref="TutorialPreview"/> (human side) reference the same
/// instance so that beats consumed by one advance the other — the
/// script is one totally-ordered log, not two parallel streams.
/// </summary>
public sealed class ScriptCursor
{
    public int Index { get; private set; }
    public void Advance() => Index++;
    public void Reset() => Index = 0;
}

/// <summary>
/// AI chooser that replays a recorded <see cref="ReplayBeat"/> script
/// for players other than the human (player 0). Plugged into
/// <see cref="GameController"/>'s <c>aiChooser</c> delegate slot
/// during Tutorial Preview so the recorded non-player-0 moves drive
/// through the standard AI step machine while the human plays Red
/// manually.
///
/// <para>
/// Cursor semantics: a shared <see cref="ScriptCursor"/> tracks the
/// next script beat. When the controller asks for actor X and the
/// next beat's <see cref="ReplayBeat.Actor"/> is some other player,
/// this chooser returns <c>null</c> WITHOUT advancing — the
/// controller reads null as "X is done; end turn," advances to the
/// next player, and the chooser then finds the matching beat on the
/// next call. <see cref="TutorialPreview"/> shares the same cursor
/// so beats consumed by the human side advance the AI side too.
/// </para>
///
/// <para>
/// Beat-to-action mapping is a 7-way switch over the concrete
/// <see cref="ReplayBeat"/> subtypes. <see cref="ReplayEndTurnBeat"/>
/// returns null and advances the cursor (end-of-turn is the natural
/// null signal). Every other kind maps to one of the
/// <see cref="AiAction"/> variants — including the four script-only
/// variants (<see cref="AiLongPressRallyAction"/>,
/// <see cref="AiClaimVictoryAction"/>, etc.) added specifically so
/// recorded human-only beats can drive AI dispatch.
/// </para>
/// </summary>
public sealed class ReplayDrivenAi
{
    private readonly IReadOnlyList<ReplayBeat> _script;
    private readonly int _rosterSize;
    private readonly ScriptCursor _cursor;
    private bool _tailLogged;

    public ReplayDrivenAi(IReadOnlyList<ReplayBeat> script,
        IReadOnlyList<Player> roster, ScriptCursor? cursor = null)
    {
        _script = script;
        _cursor = cursor ?? new ScriptCursor();
        _rosterSize = roster.Count;
    }

    /// <summary>
    /// Standard AI-chooser signature. Returns null if the current
    /// actor has no more scripted action this turn (controller reads
    /// null as "end turn"); otherwise returns the mapped
    /// <see cref="AiAction"/> and advances the cursor past it.
    /// </summary>
    public AiAction? ChooseNextAction(GameState state, PlayerId forPlayer,
        HashSet<HexCoord> visitedCapitals, DeterministicRng rng)
    {
        if (_cursor.Index >= _script.Count)
        {
            if (!_tailLogged)
            {
                Log.Info(Log.LogCategory.Tutorial,
                    $"[ReplayDrivenAi] script tail reached (cursor {_cursor.Index}/{_script.Count}); no more AI beats");
                _tailLogged = true;
            }
            return null;
        }
        ReplayBeat next = _script[_cursor.Index];
        // Tutorial-only beats (e.g., narration text) are not actions
        // any player takes. The narration driver consumes them; the AI
        // chooser must not advance past them.
        if (next is TutorialOnlyBeat) return null;
        if (forPlayer.IsNone || forPlayer.Index >= _rosterSize) return null;
        int actorIndex = forPlayer.Index;
        if (next.Actor != actorIndex) return null;
        if (next is ReplayEndTurnBeat)
        {
            Log.Info(Log.LogCategory.Tutorial,
                $"[ReplayDrivenAi] executed beat #{next.Index} EndTurn actor{next.Actor} "
                + $"(cursor {_cursor.Index}→{_cursor.Index + 1}/{_script.Count})");
            _cursor.Advance();
            return null;
        }
        Log.Info(Log.LogCategory.Tutorial,
            $"[ReplayDrivenAi] executed beat #{next.Index} {next.GetType().Name} actor{next.Actor} "
            + $"(cursor {_cursor.Index}→{_cursor.Index + 1}/{_script.Count})");
        _cursor.Advance();
        return ToAiAction(next);
    }

    /// <summary>
    /// Consume script beats owned by the neutral seat once that seat's
    /// turn has elapsed. The neutral seat is a real turn in the rotation,
    /// so a recording carries a beat per neutral turn — but its live turn
    /// is driven by the viking sequencer, not by this chooser, so nothing
    /// else ever advances the cursor past those beats. Left parked, the
    /// cursor points at a non-player-0 beat when control returns to the
    /// human and every input is rejected as a desync.
    ///
    /// <para>
    /// A beat belongs to the neutral seat when its actor index is at or
    /// past the roster size: the seat always sits last, and a recording
    /// made against a wider roster (the tutorial builder's six slots vs.
    /// the trimmed play-time roster) stamps a correspondingly higher
    /// index. Players trimmed out of the play roster never acted, so they
    /// contribute no beats to confuse the test. Called from the preview's
    /// after-refresh chain, ahead of the narration driver, so the beat
    /// behind an elapsed neutral turn presents on the same refresh.
    /// </para>
    /// </summary>
    public void ConsumeElapsedNeutralSeatBeats(GameState state)
    {
        // Still the seat's own turn: its beats are not elapsed yet.
        if (state.Turns.IsNeutralSeat) return;
        while (_cursor.Index < _script.Count)
        {
            ReplayBeat beat = _script[_cursor.Index];
            if (beat is TutorialOnlyBeat || beat.Actor < _rosterSize) return;
            Log.Info(Log.LogCategory.Tutorial,
                $"[ReplayDrivenAi] consumed elapsed neutral-seat beat #{beat.Index} "
                + $"{beat.GetType().Name} actor{beat.Actor} "
                + $"(cursor {_cursor.Index}→{_cursor.Index + 1}/{_script.Count})");
            _cursor.Advance();
        }
    }

    /// <summary>Reset the cursor to the start. Used when restarting Preview.</summary>
    public void Reset() => _cursor.Reset();

    private static AiAction ToAiAction(ReplayBeat beat) => beat switch
    {
        ReplayMoveBeat mv => new AiMoveAction(mv.From, mv.To),
        ReplayBuyBeat bu => new AiBuyUnitAction(bu.Capital, bu.To, bu.Level),
        ReplayBuildTowerBeat bt => new AiBuildTowerAction(bt.Capital, bt.To),
        ReplayLongPressRallyBeat rl => new AiLongPressRallyAction(rl.Target),
        ReplayClaimVictoryBeat cv => new AiClaimVictoryAction(cv.ThresholdPercent),
        ReplayDismissClaimBeat dc => new AiDismissClaimAction(dc.ThresholdPercent),
        ReplayDismissDefeatBeat _ => new AiDismissDefeatAction(),
        ReplayEndTurnBeat _ => throw new InvalidOperationException(
            "EndTurn beats should be handled by the caller before ToAiAction."),
        _ => throw new InvalidOperationException(
            $"Unmapped replay beat kind: {beat.GetType().Name}"),
    };
}
