using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// AI chooser that replays a recorded <see cref="ReplayBeat"/> script
/// for players other than the human (player 0). Plugged into
/// <see cref="GameController"/>'s <c>aiChooser</c> delegate slot
/// during Tutorial Preview so the recorded non-player-0 moves drive
/// through the standard AI step machine while the human plays Red
/// manually.
///
/// <para>
/// Cursor semantics: a single global <c>_cursor</c> tracks the next
/// script beat. When the controller asks for actor X and the next
/// beat's <see cref="ReplayBeat.Actor"/> is some other player, this
/// chooser returns <c>null</c> WITHOUT advancing — the controller
/// reads null as "X is done; end turn," advances to the next player,
/// and the chooser then finds the matching beat on the next call.
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
    private readonly Dictionary<Color, int> _indexByColor;
    private int _cursor;

    public ReplayDrivenAi(IReadOnlyList<ReplayBeat> script, IReadOnlyList<Player> roster)
    {
        _script = script;
        _indexByColor = new Dictionary<Color, int>(roster.Count);
        for (int i = 0; i < roster.Count; i++)
        {
            _indexByColor[roster[i].Color] = i;
        }
    }

    /// <summary>
    /// Standard AI-chooser signature. Returns null if the current
    /// actor has no more scripted action this turn (controller reads
    /// null as "end turn"); otherwise returns the mapped
    /// <see cref="AiAction"/> and advances the cursor past it.
    /// </summary>
    public AiAction? ChooseNextAction(GameState state, Color forPlayer,
        HashSet<HexCoord> visitedCapitals, Random rng)
    {
        if (_cursor >= _script.Count) return null;
        ReplayBeat next = _script[_cursor];
        if (!_indexByColor.TryGetValue(forPlayer, out int actorIndex)) return null;
        if (next.Actor != actorIndex) return null;
        if (next is ReplayEndTurnBeat)
        {
            _cursor++;
            return null;
        }
        _cursor++;
        return ToAiAction(next);
    }

    /// <summary>Reset the cursor to the start. Used when restarting Preview.</summary>
    public void Reset() => _cursor = 0;

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
