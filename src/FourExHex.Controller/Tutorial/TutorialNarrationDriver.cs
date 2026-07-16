// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;

/// <summary>
/// Consumes tutorial-only beats (<see cref="TutorialOnlyBeat"/>) from
/// the shared <see cref="ScriptCursor"/> during Tutorial Preview. Sits
/// alongside <see cref="TutorialPreviewCues"/> in the
/// <c>onAfterRefresh</c> chain: the driver ticks first, and if the
/// cursor points at a tutorial-only beat, it presents it through the
/// HUD and gates further cues until the player acknowledges.
///
/// <para>
/// For <see cref="ReplayDisplayTextBeat"/>, presentation calls
/// <see cref="IHudView.ShowTappableTutorialMessage"/> and arms a
/// one-shot subscription to <see cref="IHudView.TutorialMessageTapped"/>.
/// On tap the cursor advances, the message is hidden, and the supplied
/// refresh callback fires so <see cref="TutorialPreviewCues"/> re-paints
/// the next action's cue.
/// </para>
///
/// <para>
/// <see cref="IsPresenting"/> is true between Tick() showing a beat and
/// the acknowledgement firing. While true, <c>TutorialPreviewCues</c>
/// early-returns so the narration isn't overwritten.
/// </para>
/// </summary>
public sealed class TutorialNarrationDriver
{
    private readonly IReadOnlyList<ReplayBeat> _script;
    private readonly ScriptCursor _cursor;
    private readonly IHudView _hud;
    private readonly Action _refresh;
    private Action? _onTap;

    public TutorialNarrationDriver(
        IReadOnlyList<ReplayBeat> script,
        ScriptCursor cursor,
        IHudView hud,
        Action refresh)
    {
        _script = script;
        _cursor = cursor;
        _hud = hud;
        _refresh = refresh;
    }

    /// <summary>
    /// True between <see cref="Tick"/> presenting a tutorial-only beat
    /// and the player tapping to acknowledge. While true,
    /// <see cref="Tick"/> is a no-op (re-entrancy guard) and
    /// <c>TutorialPreviewCues</c> suppresses its own painting so the
    /// narration isn't overwritten.
    /// </summary>
    public bool IsPresenting { get; private set; }

    /// <summary>
    /// Called from the controller's <c>onAfterRefresh</c> callback. If
    /// the cursor points at a tutorial-only beat, present it; otherwise
    /// no-op. Already-presenting calls return immediately.
    /// </summary>
    public void Tick()
    {
        if (IsPresenting) return;
        if (_cursor.Index >= _script.Count) return;
        ReplayBeat beat = _script[_cursor.Index];
        if (beat is not TutorialOnlyBeat) return;

        switch (beat)
        {
            case ReplayDisplayTextBeat dt:
                PresentDisplayText(dt);
                break;
            default:
                // Unknown tutorial-only beat: skip past it defensively
                // rather than stalling the script.
                _cursor.Advance();
                break;
        }
    }

    private void PresentDisplayText(ReplayDisplayTextBeat dt)
    {
        IsPresenting = true;
        Log.Info(Log.LogCategory.Tutorial,
            $"[Narration] presenting beat #{dt.Index} DisplayText actor{dt.Actor} "
            + $"(cursor {_cursor.Index}/{_script.Count})");
        _hud.ShowTappableTutorialMessage(dt.Text);

        // Single-fire subscription. Stash the handler in _onTap so
        // OnTap can detach it after firing — protects against duplicate
        // event raises (e.g., double-tap) and lets a future beat re-arm
        // with a fresh subscription.
        _onTap = OnTap;
        _hud.TutorialMessageTapped += _onTap;
    }

    private void OnTap()
    {
        if (_onTap != null)
        {
            _hud.TutorialMessageTapped -= _onTap;
            _onTap = null;
        }
        Log.Info(Log.LogCategory.Tutorial,
            $"[Narration] dismissed; advancing cursor {_cursor.Index}→{_cursor.Index + 1}/{_script.Count}");
        _cursor.Advance();
        IsPresenting = false;
        _hud.HideTutorialMessage();
        _refresh();
    }
}
