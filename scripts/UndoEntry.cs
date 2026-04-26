/// <summary>
/// One slot in the undo/redo stack. Bundles a <see cref="GameStateSnapshot"/>
/// (board, treasury, territories) with a <see cref="SessionStateSnapshot"/>
/// (selection, action mode, move source) so undo restores both the world
/// and the player's intent at the moment the snapshot was captured.
/// </summary>
public sealed record UndoEntry(GameStateSnapshot Game, SessionStateSnapshot Session);
