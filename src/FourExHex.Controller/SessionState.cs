using System.Collections.Generic;

/// <summary>
/// UI-scoped state for the current game session: which territory the
/// player has selected, whether they're in the middle of buying a peasant
/// or moving a unit, and the turn-scoped undo/redo history. Kept separate
/// from <see cref="GameState"/> because none of this needs to be saved
/// to disk or sent over the wire — it's bookkeeping for the controller's
/// click-handling state machine.
/// </summary>
public class SessionState
{
    /// <summary>
    /// The winning player, or null if the game is still in
    /// progress. Set by <see cref="GameController"/> when a capture
    /// leaves only one player on the board. Once set, the controller
    /// short-circuits every player action until a new game is started.
    /// </summary>
    public PlayerId? Winner { get; set; }

    /// <summary>True iff the game is over (a winner has been declared).</summary>
    public bool IsGameOver => Winner.HasValue;

    /// <summary>
    /// The human player whose last capital was just captured —
    /// the HUD shows the defeat overlay while this is non-null. Set
    /// inside <see cref="GameController.HandleCapture"/> when the
    /// eliminated player is a non-AI. Cleared when the
    /// human dismisses the overlay (Continue), at which point the
    /// AI loop resumes. Never set for AI eliminations — the gong
    /// fires but no popup appears.
    /// </summary>
    public PlayerId? PendingDefeatScreen { get; set; }

    /// <summary>
    /// Color of a human player who just pressed End Turn while crossing
    /// a claim-victory tier in
    /// <see cref="WinConditionRules.ClaimVictoryThresholdsPercent"/>,
    /// paired with the threshold percent (50, 75, or 90) being prompted.
    /// The HUD shows the claim-victory overlay while this is non-null.
    /// The pending End Turn is held until the human picks Win Now or
    /// Continue Playing. Suppressed by
    /// <see cref="ClaimVictoryPromptedHighestThreshold"/> on subsequent
    /// turns so each tier fires at most once per human per game.
    /// </summary>
    public (PlayerId Player, int ThresholdPercent)? PendingClaimVictory { get; set; }

    /// <summary>
    /// Highest claim-victory tier each human player has already
    /// dismissed (via Win Now or Continue Playing). Absence means
    /// "never prompted". A player whose entry is 90 has seen all three
    /// tiers and won't be prompted again this game. Persisted across
    /// save/load (see <see cref="SaveSerializer"/>) so the
    /// once-per-tier-per-game invariant survives reloads.
    /// </summary>
    public Dictionary<PlayerId, int> ClaimVictoryPromptedHighestThreshold { get; }
        = new Dictionary<PlayerId, int>();

    public enum ActionMode
    {
        None,
        BuyingPeasant,
        BuyingSpearman,
        BuyingKnight,
        BuyingBaron,
        BuildingTower,
        MovingUnit,
    }

    /// <summary>
    /// If <paramref name="mode"/> is one of the four buy modes, return
    /// the corresponding <see cref="UnitLevel"/>; otherwise null.
    /// </summary>
    public static UnitLevel? BuyModeLevel(ActionMode mode) => mode switch
    {
        ActionMode.BuyingPeasant => UnitLevel.Peasant,
        ActionMode.BuyingSpearman => UnitLevel.Spearman,
        ActionMode.BuyingKnight => UnitLevel.Knight,
        ActionMode.BuyingBaron => UnitLevel.Baron,
        _ => null,
    };

    /// <summary>
    /// Inverse of <see cref="BuyModeLevel"/>: returns the buy mode for a
    /// given unit level.
    /// </summary>
    public static ActionMode BuyModeFor(UnitLevel level) => level switch
    {
        UnitLevel.Peasant => ActionMode.BuyingPeasant,
        UnitLevel.Spearman => ActionMode.BuyingSpearman,
        UnitLevel.Knight => ActionMode.BuyingKnight,
        UnitLevel.Baron => ActionMode.BuyingBaron,
        _ => ActionMode.None,
    };

    /// <summary>The currently highlighted territory, or null if none.</summary>
    public Territory? SelectedTerritory { get; set; }

    /// <summary>
    /// Whether the controller is waiting for the player to click a target
    /// for a pending action (buy or move).
    /// </summary>
    public ActionMode Mode { get; set; } = ActionMode.None;

    /// <summary>
    /// Source coord when Mode == MovingUnit. The unit at this coord is
    /// the one that will be moved on the next valid click.
    /// </summary>
    public HexCoord? MoveSource { get; set; }

    /// <summary>Turn-scoped undo history. Cleared at EndTurn.</summary>
    public UndoStack<UndoEntry> Undo { get; } = new UndoStack<UndoEntry>();

    /// <summary>
    /// Reset all pending-action fields to "nothing in progress". Does
    /// NOT clear the undo stack — that's its own lifecycle.
    /// </summary>
    public void ClearPendingAction()
    {
        Mode = ActionMode.None;
        MoveSource = null;
    }
}
