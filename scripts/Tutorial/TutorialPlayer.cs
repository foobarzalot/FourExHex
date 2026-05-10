using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Runtime state for a tutorial in Preview. Tracks the next-expected-
/// beat pointer, exposes the events the gated view wrappers fire into,
/// and provides the AI chooser delegate handed to GameController.
///
/// Phase 4 grows the single-slot <see cref="ArmedBeat"/> state machine
/// for two-event player actions (BuyPeasant: Buy button → tile click).
/// The wrapper that observed the first event (HUD click) calls
/// <see cref="TryArmBuyPeasant"/>; the wrapper that observed the second
/// event (tile click) calls <see cref="TryAdvanceForBuyPeasantTile"/>.
/// Cancel disarms via <see cref="DisarmIfAny"/>.
///
/// AI chooser still falls through to AiDispatcher (no scripted-AI logic
/// — Phase 10). Snapshots stays empty (Phase 13 populates it).
///
/// Pure C# / Godot-free (only references Godot's Color struct via
/// AiChooser's signature, which is value-type and test-friendly).
/// </summary>
public sealed class TutorialPlayer
{
    private readonly Tutorial _tutorial;
    private int _nextBeatIndex;
    private readonly List<GameStateSnapshot> _snapshots = new();
    private Beat? _armedBeat;

    public TutorialPlayer(Tutorial tutorial)
    {
        _tutorial = tutorial;
        _nextBeatIndex = 0;
        _armedBeat = null;
    }

    /// <summary>Next beat the player is expected to perform, or null if finished.</summary>
    public Beat? NextExpectedPlayerBeat =>
        _nextBeatIndex < _tutorial.Beats.Count ? _tutorial.Beats[_nextBeatIndex] : null;

    /// <summary>Index of the most-recently-applied beat, or -1 before any apply.</summary>
    public int CurrentBeatIndex => _nextBeatIndex - 1;

    /// <summary>
    /// Per-beat state snapshots for the scrubber (Phase 13). Empty in
    /// Phase 4 — population is deferred until the scrubber consumes them.
    /// </summary>
    public IReadOnlyList<GameStateSnapshot> Snapshots => _snapshots;

    /// <summary>
    /// The beat the player has issued the HUD-precursor click for and
    /// is expected to follow up with a tile click. Set by
    /// <see cref="TryArmBuyPeasant"/>; cleared by
    /// <see cref="TryAdvanceForBuyPeasantTile"/> on success and by
    /// <see cref="DisarmIfAny"/> (e.g., via Cancel).
    /// </summary>
    public Beat? ArmedBeat => _armedBeat;

    /// <summary>
    /// True iff the gated tile-click handler should route the next
    /// click through <see cref="TryAdvanceForBuyPeasantTile"/> instead
    /// of forwarding it as a passive selection.
    /// </summary>
    public bool IsArmedForBuyPeasant => _armedBeat is BuyPeasantBeat;

    /// <summary>Fires after a beat is applied. Argument is the beat's index.</summary>
    public event Action<int>? BeatApplied;

    /// <summary>Fires when the player attempts an action that doesn't match the
    /// next expected beat. The PreviewPane subscribes and shows a toast.</summary>
    public event Action<Beat?, string>? PlayerActionRejected;

    /// <summary>Fires once after the last beat is applied.</summary>
    public event Action? TutorialFinished;

    /// <summary>
    /// AI chooser delegate handed to GameController. Phase 4 (like 3c)
    /// always falls through to AiDispatcher (no scripted-AI logic).
    /// Phase 10 adds the scripted-beat-as-AiAction path here.
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
    /// Called by <see cref="TutorialGatedHudView"/> when the player
    /// clicks Buy Peasant. If the next beat is a BuyPeasantBeat AND
    /// we're not already armed, sets <see cref="ArmedBeat"/> and
    /// returns true (caller forwards the click — controller enters
    /// BuyingPeasant mode). Otherwise rejects:
    /// <list type="bullet">
    ///   <item>If already armed, rejection prevents the controller's
    ///   buy-level cycle (re-pressing Buy goes Peasant → Spearman,
    ///   which would silently break the tutorial).</item>
    ///   <item>If next beat isn't BuyPeasant, rejection signals "wrong
    ///   action right now".</item>
    /// </list>
    /// Note this advances no pointer — the actual beat completion is
    /// the follow-up tile click via
    /// <see cref="TryAdvanceForBuyPeasantTile"/>.
    /// </summary>
    public bool TryArmBuyPeasant()
    {
        if (_armedBeat == null && NextExpectedPlayerBeat is BuyPeasantBeat bpb)
        {
            _armedBeat = bpb;
            return true;
        }
        NotifyRejected("Buy Peasant");
        return false;
    }

    /// <summary>
    /// Called by <see cref="TutorialGatedHexMapView"/> when the player
    /// clicks a tile *while the player is armed for BuyPeasant*. Caller
    /// MUST gate on <see cref="IsArmedForBuyPeasant"/> first — if not
    /// armed, the wrapper should forward as passive selection rather
    /// than calling this method.
    /// On match (tile coord == beat.At): clears arm, advances pointer,
    /// fires BeatApplied (and TutorialFinished if last), returns true →
    /// caller forwards the click (controller fires ExecuteBuyAndPlace).
    /// On miss (wrong tile): keeps arm set so the dev can retry without
    /// re-clicking Buy (controller stays in BuyingPeasant mode), fires
    /// PlayerActionRejected, returns false → caller does NOT forward
    /// (preventing the controller from cancelling its pending action).
    /// </summary>
    public bool TryAdvanceForBuyPeasantTile(HexCoord at)
    {
        if (_armedBeat is BuyPeasantBeat bpb && TutorialValidator.MatchesBuyPeasant(bpb, at))
        {
            int applied = _nextBeatIndex;
            _nextBeatIndex++;
            _armedBeat = null;
            BeatApplied?.Invoke(applied);
            if (_nextBeatIndex >= _tutorial.Beats.Count)
            {
                TutorialFinished?.Invoke();
            }
            return true;
        }
        NotifyRejected($"tile click at ({at.Q},{at.R})");
        return false;
    }

    /// <summary>
    /// Clear any armed beat. Called by
    /// <see cref="TutorialGatedHudView"/>'s Cancel pass-through and by
    /// any other path that exits the controller's pending-action mode.
    /// Idempotent — safe to call when not armed.
    /// </summary>
    public void DisarmIfAny()
    {
        _armedBeat = null;
    }

    /// <summary>
    /// Fire the soft-reject event. Used by gated wrappers when an
    /// input can never match (e.g. a tile click while not armed and
    /// next beat is BuyPeasantBeat — handled by the wrapper, not here)
    /// or when the next expected beat is of a different kind.
    /// </summary>
    public void NotifyRejected(string attempted)
    {
        Beat? next = NextExpectedPlayerBeat;
        string reason = TutorialValidator.ReasonMismatch(next, attempted);
        PlayerActionRejected?.Invoke(next, reason);
    }
}
