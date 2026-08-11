// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
/// <summary>
/// One slot in the undo/redo stack. Bundles a <see cref="GameStateSnapshot"/>
/// (board, treasury, territories) with a <see cref="SessionStateSnapshot"/>
/// (selection, action mode, move source) and a <see cref="RunStats"/> copy
/// (a tower built then undone must not count toward achievements) so undo
/// restores the world, the player's intent, and the observation counters
/// at the moment the snapshot was captured.
/// </summary>
public sealed record UndoEntry(
    GameStateSnapshot Game, SessionStateSnapshot Session, RunStats Stats);
