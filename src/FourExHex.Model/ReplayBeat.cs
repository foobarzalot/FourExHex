/// <summary>
/// One recorded entry in a game's replay log. Most beats describe a
/// state-mutating action an actor (player) took — a typed outcome,
/// never a raw input — so replay can re-execute through the same
/// controller paths the live game used. Recorded into
/// <c>GameController._replayBeats</c> at the moment the action is
/// applied; serialized into the save's <c>ReplayDto</c> and replayed
/// via <c>GameController.BeginReplay</c>.
///
/// <para>
/// Selection-only clicks, mode-entry presses, undo/redo, and territory/
/// unit cycling are <b>not</b> recorded — they don't mutate game state.
/// AI actions and human actions use the same beat kinds with different
/// <see cref="Actor"/> values; only <see cref="LongPressRally"/>,
/// <see cref="ClaimVictory"/>, <see cref="DismissClaim"/>, and
/// <see cref="DismissDefeat"/> are human-only because the corresponding
/// gestures and overlays have no AI counterpart.
/// </para>
///
/// <para>
/// Tutorial-only beats (<see cref="TutorialOnlyBeat"/> and subclasses)
/// are NOT captured from gameplay; they are authored explicitly during
/// Record mode (e.g. narration text). They carry <see cref="Actor"/> ==
/// -1 and are consumed only by <c>TutorialNarrationDriver</c> during
/// Tutorial Preview — never by <c>TutorialPreview.TryAccept</c> or
/// <c>ReplayDrivenAi.ChooseNextAction</c>.
/// </para>
/// </summary>
public abstract record ReplayBeat
{
    /// <summary>Zero-based position in the replay log; stamped by
    /// <c>GameController.RecordBeat</c> at append time so deserialized
    /// beats round-trip identically.</summary>
    public int Index { get; init; }

    /// <summary>1-based <see cref="TurnState.TurnNumber"/> at the moment
    /// the beat was recorded. Captured before the beat is applied — an
    /// EndTurn beat carries the turn number of the player whose turn is
    /// ending, not the next player's.</summary>
    public int Turn { get; init; }

    /// <summary>Index into <see cref="GameState.Turns"/>'s player list
    /// for the player who took the action.</summary>
    public int Actor { get; init; }
}

/// <summary>
/// Move an existing unit from <see cref="From"/> to <see cref="To"/>.
/// Replay dispatches via <c>GameController.ExecuteAiMove</c>, which is
/// actor-agnostic — the live human and AI paths converge on the same
/// helper for captures, FX, and reconciliation.
/// </summary>
public sealed record ReplayMoveBeat : ReplayBeat
{
    public HexCoord From { get; init; }
    public HexCoord To { get; init; }
}

/// <summary>
/// Buy a unit of <see cref="Level"/> from the territory whose capital
/// is <see cref="Capital"/> and place it at <see cref="To"/>. Replay
/// dispatches via <c>GameController.ExecuteAiBuyUnit</c>.
/// </summary>
public sealed record ReplayBuyBeat : ReplayBeat
{
    public HexCoord Capital { get; init; }
    public HexCoord To { get; init; }
    public UnitLevel Level { get; init; }
}

/// <summary>
/// Build a tower on <see cref="To"/> from the territory whose capital
/// is <see cref="Capital"/>. Replay dispatches via
/// <c>GameController.ExecuteAiBuildTower</c>.
/// </summary>
public sealed record ReplayBuildTowerBeat : ReplayBeat
{
    public HexCoord Capital { get; init; }
    public HexCoord To { get; init; }
}

/// <summary>
/// End the current player's turn. Human End-Turn and the AI's implicit
/// end-of-turn (chooser returned null or step cap hit) both produce
/// this beat. Replay drives the same EndOfTurnProcessing →
/// AdvanceToNextActivePlayer → StartPlayerTurn chain.
/// </summary>
public sealed record ReplayEndTurnBeat : ReplayBeat
{
}

/// <summary>
/// Human-only: long-press rally at <see cref="Target"/>. Replay
/// re-invokes the rally body, which is deterministic from current
/// state (explicit lex-min tiebreaks in unit selection and destination
/// choice). One beat per rally regardless of how many units moved.
/// </summary>
public sealed record ReplayLongPressRallyBeat : ReplayBeat
{
    public HexCoord Target { get; init; }
}

/// <summary>
/// Human-only: Win Now press on the claim-victory overlay at
/// <see cref="ThresholdPercent"/>. Game-ending. Replay applies it
/// silently (no overlay during playback).
/// </summary>
public sealed record ReplayClaimVictoryBeat : ReplayBeat
{
    public int ThresholdPercent { get; init; }
}

/// <summary>
/// Human-only: Continue Playing press on the claim-victory overlay at
/// <see cref="ThresholdPercent"/>. Followed in the log by an
/// <see cref="ReplayEndTurnBeat"/> that performs the deferred turn
/// advance. Replay applies it silently.
/// </summary>
public sealed record ReplayDismissClaimBeat : ReplayBeat
{
    public int ThresholdPercent { get; init; }
}

/// <summary>
/// Human-only: Continue press on the defeat overlay. Re-arms the AI
/// loop in live play; replay clears the flag silently to keep playback
/// flowing.
/// </summary>
public sealed record ReplayDismissDefeatBeat : ReplayBeat
{
}

/// <summary>
/// Viking Raiders: a landed viking's ordinary land move (capture or
/// defensive reposition). A distinct beat kind from <see cref="ReplayMoveBeat"/>
/// because that one replays as the CURRENT player's move, while viking
/// moves belong to the neutral owner (whose pseudo-turn runs while the
/// current player waits). Replay dispatches via
/// <c>GameOperations.ExecuteVikingMove</c>.
/// </summary>
public sealed record ReplayVikingMoveBeat : ReplayBeat
{
    public HexCoord From { get; init; }
    public HexCoord To { get; init; }
}

/// <summary>
/// Viking Raiders: the sea raider at <see cref="Sea"/> disembarks onto
/// <see cref="Land"/>. Replay dispatches via
/// <c>GameOperations.ExecuteVikingDisembark</c>.
/// </summary>
public sealed record ReplayVikingDisembarkBeat : ReplayBeat
{
    public HexCoord Sea { get; init; }
    public HexCoord Land { get; init; }
}

/// <summary>
/// Viking Raiders: the sea raider at <see cref="Sea"/> perished (no
/// landing site). Replay dispatches via
/// <c>GameOperations.ExecuteVikingPerish</c>.
/// </summary>
public sealed record ReplayVikingPerishBeat : ReplayBeat
{
    public HexCoord Sea { get; init; }
}

/// <summary>
/// Viking Raiders: wave <see cref="WaveIndex"/> spawned as
/// <see cref="Spawns"/>. The placements are explicit (drawn live from the
/// vikings' RNG stream at choose time) so replay consumes no randomness.
/// Replay dispatches via <c>GameOperations.ExecuteVikingSpawnWave</c>.
/// </summary>
public sealed record ReplayVikingSpawnBeat : ReplayBeat
{
    public int WaveIndex { get; init; }
    public System.Collections.Generic.IReadOnlyList<SeaViking> Spawns { get; init; }
        = System.Array.Empty<SeaViking>();
}

/// <summary>
/// Viking Raiders: the viking pseudo-turn ended. Replay runs the same
/// completion the live driver does — <c>CompleteVikingTurn</c> plus the
/// deferred <c>StartPlayerTurn</c> for the waiting (non-eliminated)
/// player. The live side records it from the driver's EndVikingPhaseCore.
/// </summary>
public sealed record ReplayVikingTurnEndBeat : ReplayBeat
{
}

/// <summary>
/// Tutorial-only beats: not captured from gameplay, authored explicitly
/// during Record mode (e.g., narration text inserted between game-action
/// beats). <see cref="ReplayBeat.Actor"/> is always -1 (sentinel — no
/// player owns these). During Tutorial Preview, they are consumed by
/// <c>TutorialNarrationDriver</c> via the shared <c>ScriptCursor</c>;
/// <c>TutorialPreview</c> and <c>ReplayDrivenAi</c> skip past them
/// without advancing the cursor. The in-game Replay button silently
/// skips them — they exist only for the authored Preview experience.
/// </summary>
public abstract record TutorialOnlyBeat : ReplayBeat
{
}

/// <summary>
/// Tutorial-only: show authored narration text in the tutorial-message
/// panel and block until the player taps to continue. Authored from
/// RecordPane's "+ Text" button. Presented by <c>TutorialNarrationDriver</c>.
/// </summary>
public sealed record ReplayDisplayTextBeat : TutorialOnlyBeat
{
    public string Text { get; init; } = "";
}
