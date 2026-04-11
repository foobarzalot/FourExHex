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
    public enum ActionMode
    {
        None,
        BuyingPeasant,
        MovingUnit,
    }

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
    public UndoStack Undo { get; } = new UndoStack();

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
