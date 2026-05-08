# Claim Victory prompt — design

## Summary

When a human player presses **End Turn** while owning more than 50% of all
land tiles on the grid, intercept the press and show a "Claim Victory"
overlay with two buttons:

- **Win Now** — declare that human as the winner; transition to the
  existing victory screen.
- **Continue Playing** — proceed with the End Turn (advance to next
  player, run AI loop) exactly as if no prompt had appeared.

Fires at most **once per human player per game**. The prompt is suppressed
for AI players. State persists across save/load so that loading a saved
game cannot reset the "already prompted" status.

## Trigger

The check runs at the very top of `OnEndTurnPressed`, before
`Undo.Clear()` and any end-of-turn processing. Conditions (all required):

1. `_session.IsGameOver` is false.
2. `_state.Turns.CurrentPlayer.IsAi` is false.
3. The current player's color is **not** in
   `_session.ClaimVictoryPromptedColors`.
4. `WinConditionRules.MeetsClaimVictoryThreshold(currentColor, _state.Grid)`
   returns true (strictly more than half of all `state.Grid.Tiles` carry
   `tile.Color == currentColor`; water is not counted because it is not
   in the grid).

If triggered: set `_session.PendingClaimVictory = currentColor`, refresh
views, and return. The rest of `OnEndTurnPressed` is extracted into a
helper `EndTurnNow()` that runs after the user dismisses the overlay.

## Dismissal

- **Win Now** (`HudView.ClaimVictoryWinNowClicked` →
  `OnClaimVictoryWinNowPressed`): record the color in
  `ClaimVictoryPromptedColors`, clear `PendingClaimVictory`, call
  `DeclareWinner(color)` (sets `Winner`, fires `PlayGameWon`), then
  `CheckGameEndConditions()` (fires `GameEnded`), then `RefreshViews()`.
  The HUD's `Refresh` then shows the victory overlay (Winner takes
  precedence over PendingClaimVictory in the overlay-priority order).
- **Continue Playing** (`HudView.ClaimVictoryContinueClicked` →
  `OnClaimVictoryContinuePressed`): record the color in
  `ClaimVictoryPromptedColors`, clear `PendingClaimVictory`, call
  `EndTurnNow()` (which runs the original End Turn flow).

The "record on dismissal" ordering is deliberate: if the prompt is
**shown** but never **dismissed** (e.g., manual save followed by quit and
reload), the player should still see the prompt on their next End Turn.
Recording only on dismissal means a save mid-prompt drops the
`PendingClaimVictory` flag (it isn't persisted) but does **not** mark the
color as already prompted — so the prompt re-appears on next End Turn,
which is the correct behavior.

## State

### Additions to `SessionState`

```csharp
// Color of a human currently being prompted to claim victory; null when
// no overlay is up. Mirrors PendingDefeatScreen exactly.
public Color? PendingClaimVictory { get; set; }

// Human colors that have already dismissed the prompt this game. Once
// in this set, the color never sees the prompt again (even after
// dropping below and re-crossing the threshold).
public HashSet<Color> ClaimVictoryPromptedColors { get; } = new();
```

Both are excluded from `SessionStateSnapshot` (same as `Winner` and
`PendingDefeatScreen` — overlay-state isn't snapshot/undoable, and undo
is cleared at end-of-turn anyway).

### Additions to `WinConditionRules`

```csharp
public static bool MeetsClaimVictoryThreshold(Color color, HexGrid grid)
{
    int owned = 0;
    int total = 0;
    foreach (HexTile tile in grid.Tiles)
    {
        total++;
        if (tile.Color == color) owned++;
    }
    return owned * 2 > total;  // strictly > 50%, integer-safe
}
```

## HUD changes

### `IHudView`

Two new events:

```csharp
event Action? ClaimVictoryWinNowClicked;
event Action? ClaimVictoryContinueClicked;
```

### `HudView`

- Add private fields `_claimVictoryOverlay`, `_claimVictoryLabel`.
- New `BuildClaimVictoryOverlay(viewport)`, structurally a clone of
  `BuildDefeatOverlay`. Two buttons: **Win Now** (raises
  `ClaimVictoryWinNowClicked`) and **Continue Playing** (raises
  `ClaimVictoryContinueClicked`). Label: `"You control most of the map!"`
  with a colored sub-line showing the player's name.
- `Refresh` overlay-priority order:
  1. Winner overlay (highest)
  2. Defeat overlay (suppressed when Winner set)
  3. Claim-victory overlay (suppressed when Winner OR Defeat set)

  Implementation: claim-victory overlay shown iff
  `session.PendingClaimVictory.HasValue && !session.Winner.HasValue
   && !session.PendingDefeatScreen.HasValue`.

### `MockHudView` and `HeadlessHudView`

Add the two new events. `MockHudView` exposes them so tests can invoke
the dismissal handlers; `HeadlessHudView` is a no-op stub.

## Save/load

The "exactly once per game" invariant must survive save/load. Without
persistence, save → load → end-turn-at->50% would re-prompt.

### Format change

`SaveSerializer` format version bumped **2 → 3**. New optional field at
the top level:

```json
"claimVictoryPromptedColors": ["#ff0000", "#0000ff"]
```

(Hex strings, lowercase, in any order — round-tripped through the same
color-parse helpers used elsewhere in the serializer.)

### Backwards compatibility

Format-2 saves load with an empty `claimVictoryPromptedColors`. Net
effect: a player who'd been prompted in a v2 save can be re-prompted
once after upgrading. Acceptable: v2 saves don't carry the data, so
there's nothing to recover.

### Plumbing

- `LoadedSave` gains `IReadOnlyList<Color> ClaimVictoryPromptedColors`
  (default empty list).
- `SaveStore.WriteAutosave` and `WriteSlot` take a
  `IReadOnlyCollection<Color> claimVictoryPromptedColors` parameter
  (read from the live `SessionState` at call time).
- `Main`'s autosave subscriber passes
  `_session.ClaimVictoryPromptedColors` through.
- `Main`'s load-resume path (`TurnNumber > 0` branch) seeds
  `_session.ClaimVictoryPromptedColors` from the loaded save **before**
  `controller.Resume()`.
- Starting maps (`TurnNumber == 0`) and procedural games always start
  with an empty set.

`PendingClaimVictory` is **not** serialized. Autosave fires on
`HumanTurnStarted` (start-of-turn, before any End Turn), so it can never
observe a non-null value. Manual-save behavior while the overlay is up:
on reload, the flag is null and the color isn't in the prompted set, so
the next End Turn re-shows the prompt — correct.

## Tests

### `WinConditionRulesTests` (new entries)

- `MeetsClaimVictoryThreshold` returns false at exactly 50% ownership
  (e.g., 4 of 8 tiles).
- Returns true at strictly above 50% (e.g., 5 of 9, 5 of 8, 1 of 1).
- Returns false at strictly below 50%.
- Single-tile grid: owner returns true; non-owner returns false.

### `GameControllerTests` / new `ClaimVictoryTests`

1. Human at exactly 50% on End Turn → no prompt; turn advances normally.
2. Human at 51%+ on End Turn (first time) → `PendingClaimVictory` set,
   turn does **not** advance, no AI invoked, no end-of-turn processing.
3. AI at >50% on End Turn → no prompt; turn advances normally.
4. **Win Now** dismissal: `Winner` set to that color, `GameEnded` fired
   exactly once, color recorded in `ClaimVictoryPromptedColors`.
5. **Continue Playing** dismissal: prompt cleared, color recorded in
   `ClaimVictoryPromptedColors`, turn advances, AI runs as normal.
6. After a Continue Playing dismissal, the same human at >50% on a
   later End Turn does **not** re-prompt.
7. Hot-seat: a second human at >50% on their End Turn still gets their
   own prompt independently.
8. Game-over short-circuit: if `Winner` already set when End Turn is
   pressed (unreachable in practice but defensive), no prompt.

### Save/load round-trip tests

- Mid-game save with one color in `ClaimVictoryPromptedColors`: serialize
  → deserialize → set restored on the loaded session; that color does
  not re-prompt at >50%.
- v2-format JSON (no field) loads with empty set; legacy save still
  works.

## Headless / diagnostic mode

`FOUREXHEX_6AI` forces all six players to AI, so the prompt never fires
in diagnostic mode. No special-casing required.

## ARCHITECTURE.md updates

After implementation:

- Append `ClaimVictoryWinNowClicked` and `ClaimVictoryContinueClicked`
  to the `IHudView` event list.
- Add a "Claim victory prompt" subsection under "Win conditions"
  describing the per-human-once trigger and the overlay-priority
  ordering (Winner > Defeat > ClaimVictory).
- Note the save format bump to v3 in the Save/load section, with a
  one-line description of the new field.
