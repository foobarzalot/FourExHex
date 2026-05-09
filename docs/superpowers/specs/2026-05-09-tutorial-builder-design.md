# TutorialBuilder — design

## Summary

Add a developer-only authoring tool — `TutorialBuilder` — for hand-crafting
on-rails tutorials that ship as `Tutorial.json` v3. The tool lives in a new
scene that hosts the existing Map Editor (refactored into a reusable
`MapEditorPanel`) plus two new modes: **Build** (author beats over a map)
and **Preview** (live-play the tutorial with input gated to the next
authored player-action).

A tutorial is an ordered list of `Beat`s. Player-action beats (`Move`,
`BuyPeasant`, `BuildTower`, `EndTurn`) advance only when the runtime
matches the player's input. Overlay beats (`Prompt`, `Highlight`,
`CameraFocus`) render UI directives that auto-dismiss per `DismissOn`. AI
players' beats either come from the script or fall back to the existing
`AiSimulator` path.

The runtime piece is a pure-C# `TutorialPlayer` that wraps `GameController`
without modifying it. The user-facing **Play Tutorial** menu entry is left
alone — it inherits scripted beats automatically once
`res://tutorials/Tutorial.json` carries them, but is not the test bed.

## Scope decisions (locked)

These resolve the README's open questions and one wireframe-vs-README
discrepancy:

1. **Beat kinds (v1)** — wireframes (S9) are canonical:
   - Player-action: `Move`, `BuyPeasant`, `BuildTower`, `EndTurn`
   - Overlay: `Prompt`, `Highlight`, `CameraFocus`
   - Dropped vs README: `Wait` (replaced by `DismissOn: Delay:Nms`),
     `Attack` and `Capture` (just `Move` semantics — `MovementRules.Move`
     already detects capture and routes through `HandleCapture`), generic
     `Buy` (use `BuyPeasant` only; level promotion happens via repeated
     buys, identical to the live game's Buy button cycle).
2. **Branching tutorials** — deferred. Linear only in v1.
3. **Localization of `Prompt.Body`** — deferred. Raw string in v1.
4. **`Matches` strictness** — exact `(Src, Dst)` for `Move`; exact `At`
   for `BuyPeasant` / `BuildTower`. No fuzzy matching.
5. **Camera state during `CameraFocus` beats** — pan changes; the dev's
   manual zoom is preserved if they have manually zoomed since the last
   camera beat.
6. **Scrubber position persistence** — deferred. In-memory only.
7. **Dev-mode gating** — the new `tutorial_builder.tscn` menu entry and
   the scrubber chrome inside Preview are both gated on
   `OS.IsDebugBuild()`. Production builds never see them.
8. **Where Save Tutorial writes** — `user://tutorials/<name>.json` for
   in-progress dev work. Promotion to the bundled
   `res://tutorials/Tutorial.json` is a manual file-copy outside the
   game (Godot can't write to `res://` at runtime).

## Architecture

The README sketches `TutorialBuilder` as "the MapEditor's chrome swapped
into a 3-mode tool." We implement that as **scene composition (Approach
B)**: a new scene root that owns the chrome and hosts the existing editor
as a panel. The current `map_editor.tscn` keeps its menu entry and stays
identical; `tutorial_builder.tscn` is its own debug-only entry.

```
┌─────────────────────────────────────────────────────────────────────┐
│                     tutorial_builder.tscn (new)                     │
│   TutorialBuilderScene : Node2D                                     │
│   ├─ TopBar (mode switcher + Save/Load Tutorial + Exit)             │
│   ├─ MapEditorPanel (reused, Phase 1 refactor target)               │
│   │     • owns draft grid/water/territories + paint                 │
│   │     • owns HexMapView instance + paint palette HUD              │
│   │     • exposes events: DraftChanged, UndoStateChanged            │
│   │     • exposes methods: LoadFromMap, SnapshotDraft,              │
│   │                        RestoreDraft, SetPaintingEnabled         │
│   ├─ BuildPane (Mode 2 chrome — timeline + inspector)               │
│   │     • shown when mode == Build                                  │
│   │     • subscribes to MapEditorPanel for the underlying map       │
│   │     • owns the in-memory Tutorial POCO                          │
│   └─ PreviewPane (Mode 3 chrome — TutorialPlayer + scrubber)        │
│         • shown when mode == Preview                                │
│         • clones the editor's draft into a transient GameState      │
│         • TutorialPlayer wraps a transient GameController           │
│         • scrubber dock (debug builds only)                         │
└─────────────────────────────────────────────────────────────────────┘
```

`MapEditorPanel` always exists as a child of the scene. Mode switching
just shows/hides the BuildPane and PreviewPane chrome and toggles the
panel's `SetPaintingEnabled` so paint clicks don't fire while in
Build/Preview. The map view is shared across modes; the dev's pan/zoom is
preserved on every transition.

**No changes to `GameController`, `Main`, or the rules layer.** The runtime
piece (`TutorialPlayer`) wraps `GameController` by injecting the
`aiChooser` delegate and providing wrapper views that intercept human
input. This is purely additive.

## Data model

All POCOs live under `scripts/Tutorial/` (new directory).

### `Tutorial`

```csharp
public sealed class Tutorial
{
    public string Title { get; init; } = "";
    public int StartTurn { get; init; } = 1;
    public int StartPlayer { get; init; } = 0;
    public IReadOnlyList<Beat> Beats { get; init; } = Array.Empty<Beat>();
}
```

### `Beat`

`Beat` is a discriminated union via abstract base + concrete records,
mirroring the `AiAction` pattern already used in the codebase. JSON
(de)serialization uses `Kind` as the discriminator.

```csharp
public abstract record Beat
{
    public int Index { get; init; }            // contiguous from 0
    public int Turn { get; init; }             // 1-based, matches TurnState.TurnNumber
    public int Actor { get; init; }            // index into Players
    public string? Narration { get; init; }    // optional caption shown in timeline
    public abstract BeatKind Kind { get; }
}

public enum BeatKind { Move, BuyPeasant, BuildTower, EndTurn,
                       Prompt, Highlight, CameraFocus }

public sealed record MoveBeat : Beat
{
    public override BeatKind Kind => BeatKind.Move;
    public required HexCoord Src { get; init; }
    public required HexCoord Dst { get; init; }
}

public sealed record BuyPeasantBeat : Beat
{
    public override BeatKind Kind => BeatKind.BuyPeasant;
    public required HexCoord At { get; init; }
}

public sealed record BuildTowerBeat : Beat
{
    public override BeatKind Kind => BeatKind.BuildTower;
    public required HexCoord At { get; init; }
}

public sealed record EndTurnBeat : Beat
{
    public override BeatKind Kind => BeatKind.EndTurn;
}

public sealed record PromptBeat : Beat
{
    public override BeatKind Kind => BeatKind.Prompt;
    public required AnchorRef Anchor { get; init; }
    public BubblePosition Position { get; init; } = BubblePosition.Auto;
    public required string Body { get; init; }
    public required DismissOn DismissOn { get; init; }
}

public sealed record HighlightBeat : Beat
{
    public override BeatKind Kind => BeatKind.Highlight;
    public required AnchorRef Target { get; init; }
    public HighlightStyle Style { get; init; } = HighlightStyle.Ring;
    public bool Pulse { get; init; } = true;
    public required DismissOn DismissOn { get; init; }
}

public sealed record CameraFocusBeat : Beat
{
    public override BeatKind Kind => BeatKind.CameraFocus;
    public required AnchorRef Target { get; init; }
    public float Zoom { get; init; } = 1.0f;          // 1.0..2.5
    public required DismissOn DismissOn { get; init; }
}

public enum BubblePosition { Auto, North, East, South, West }
public enum HighlightStyle { Ring, Spot }
```

### `AnchorRef`

```csharp
public abstract record AnchorRef
{
    public abstract AnchorKind Kind { get; }
}
public enum AnchorKind { Tile, Hud, Region, None }

public sealed record TileAnchor(HexCoord At) : AnchorRef
{
    public override AnchorKind Kind => AnchorKind.Tile;
}
public sealed record HudAnchor(string Id) : AnchorRef
{
    public override AnchorKind Kind => AnchorKind.Hud;
    // Id is one of: "hud.gold", "hud.endTurn", "hud.buyPeasant",
    //               "hud.buildTower", "hud.undoLast", "hud.nextTerritory"
    // Resolved by HudView.GetAnchorRect(string id) (new method).
}
public sealed record RegionAnchor(IReadOnlyList<HexCoord> Tiles) : AnchorRef
{
    public override AnchorKind Kind => AnchorKind.Region;
}
public sealed record NoneAnchor : AnchorRef
{
    public override AnchorKind Kind => AnchorKind.None;
    // Centered on viewport.
}
```

### `DismissOn`

```csharp
public abstract record DismissOn
{
    public sealed record Click : DismissOn;
    public sealed record NextBeat : DismissOn;
    public sealed record Delay(int Ms) : DismissOn;
}
```

### JSON v3 schema

`SaveSerializer` bumps `FormatVersion` from 2 to 3. The new top-level
field is **optional**; non-tutorial saves don't include it. v2 files load
as v3 with no `Tutorial` block (i.e. empty `Beats[]`).

```jsonc
{
  "FormatVersion": 3,
  "SavedAtUnix": 1778192679,
  "SlotName": "Tutorial",
  "MasterSeed": 479,
  "TurnNumber": 0,
  "CurrentPlayerIndex": 0,
  "MaxTurnNumber": 2147483647,
  "Players": [ /* unchanged */ ],
  "Tiles":   [ /* unchanged */ ],
  "Territories": [ /* unchanged */ ],
  "Gold":    [ /* unchanged */ ],
  "Water":   [ /* unchanged */ ],

  "Tutorial": {
    "Title": "Intro · The Basics",
    "StartTurn": 1,
    "StartPlayer": 0,
    "Beats": [
      { "Index": 0, "Turn": 1, "Actor": 0, "Kind": "BuyPeasant",
        "At": {"Q":6,"R":17} },
      { "Index": 1, "Turn": 1, "Actor": 0, "Kind": "Prompt",
        "Anchor": {"Kind":"Tile","Q":6,"R":17},
        "Position": "Auto",
        "Body": "Tap End Turn to continue.",
        "DismissOn": {"Kind":"NextBeat"} },
      { "Index": 2, "Turn": 1, "Actor": 0, "Kind": "EndTurn" }
    ]
  }
}
```

## Runtime: `TutorialPlayer`

A pure-C# class (testable via `MockHexMapView` / `MockHudView`) that
wraps a `GameController` to execute scripted beats and gate human input.

### Composition

```
PreviewPane._Ready (or Main, when scripted Play Tutorial ships):
  realMap   = HexMapView | MockHexMapView
  realHud   = HudView   | MockHudView
  gatedMap  = new TutorialGatedHexMapView(realMap, player)
  gatedHud  = new TutorialGatedHudView(realHud, player)
  player    = new TutorialPlayer(tutorial, ...)
  ctrl      = new GameController(state, session, gatedMap, gatedHud,
                                  seed, player.AiChooser, pacer, ...)
  player.Bind(ctrl, gatedMap, gatedHud)
  ctrl.StartGame()
```

### Public API

```csharp
public sealed class TutorialPlayer
{
    public TutorialPlayer(Tutorial tutorial);

    // Wired by PreviewPane after construction so the player can fire
    // events through the views and read state.
    public void Bind(GameController ctrl,
                     IHexMapView gatedMap,
                     IHudView gatedHud);

    // The aiChooser delegate handed to GameController. Returns the next
    // scripted AI beat for the current player as an AiAction, or falls
    // back to AiSimulator.ChooseForCurrentPlayer (Heuristic) if the
    // current AI player has no scripted beats remaining for this turn.
    public AiAction? AiChooser(GameState state, Color forPlayer,
                                HashSet<HexCoord> visitedCapitals,
                                Random rng);

    public Beat? NextExpectedPlayerBeat { get; }   // null when finished
    public int    CurrentBeatIndex     { get; }   // -1 before any apply

    // Per-beat snapshot stack for the scrubber (Phase 13).
    public IReadOnlyList<GameStateSnapshot> Snapshots { get; }

    public event Action<int>?   BeatApplying;
    public event Action<int>?   BeatApplied;
    public event Action<Beat>?  AwaitingPlayerAction;
    public event Action<Beat,string>? PlayerActionRejected;  // reason text
    public event Action?        TutorialFinished;
}
```

### Human-input gating (wrapper views)

The wrapper pattern avoids reordering subscribers. `TutorialGatedHexMapView`
implements `IHexMapView` by delegating most calls to the wrapped real
view; for the input events (`TileClicked`, `TileLongClicked`), it
subscribes to the real view, decides whether the click matches the next
expected player-action beat via `TutorialValidator.Matches`, and either
re-raises the event (`GameController` proceeds) or fires
`PlayerActionRejected` and shows a soft-reject toast via
`gatedHud.ShowTutorialMessage(reason)`.

`TutorialGatedHudView` does the same for `EndTurnClicked`,
`BuyPeasantClicked`, `BuildTowerClicked`. Other HUD events
(`UndoLastClicked`, `NextTerritoryClicked`, etc.) pass through unchanged
— undo/territory cycling are dev affordances during Preview.

### `TutorialValidator`

```csharp
public static class TutorialValidator
{
    // Exact match per scope decision #4.
    public static bool MatchesMove(MoveBeat beat, HexCoord src, HexCoord dst);
    public static bool MatchesBuyPeasant(BuyPeasantBeat beat, HexCoord at);
    public static bool MatchesBuildTower(BuildTowerBeat beat, HexCoord at);
    public static bool MatchesEndTurn(EndTurnBeat beat);

    // Reason strings shown in soft-reject toasts.
    public static string ReasonMismatch(Beat expected, string attempted);
}
```

### Beat pointer advancement

`TutorialPlayer` owns a single `int _nextBeatIndex`. Three paths advance
it, one per beat-source category (human action, AI action, overlay):

**Human player-action beats** — the gated view wrapper observes the click,
calls `TutorialValidator.Matches(currentBeat, attempt)`, and on a true
result:
1. Increments `_nextBeatIndex`.
2. Captures `GameStateSnapshot` and pushes onto `Snapshots` after the
   forwarded event has been processed (use a `CallDeferred` from the
   wrapper so the snapshot reflects the post-mutation state).
3. Forwards the original event to the wrapped real view's subscribers
   (i.e., `GameController`).
4. Fires `BeatApplied(_nextBeatIndex - 1)`.

If the controller throws on the forwarded event, the wrapper catches,
rolls back `_nextBeatIndex`, and re-raises (a controller exception means
our `Matches` predicate disagreed with the rules layer — a bug we want
to surface, not paper over).

**AI player-action beats** — `AiChooser` is called by `GameController`'s
`StepAiPreview`. The chooser:
1. Looks at the next beat. If `Actor == forPlayer`'s index and kind is a
   player-action, converts it to an `AiAction`, increments
   `_nextBeatIndex`, and returns the action.
2. `GameController` runs `StepAiExecute` after the chooser returns,
   mutating state. The "AI beat completed" trigger is the wrapper's
   `Play*` audio sinks: `GameController` always fires at least one
   `Play*` event per executed `AiAction` via `DispatchActionSound`. The
   wrapper intercepts the first `Play*` call following an AI chooser
   return, pushes `GameStateSnapshot.Capture(state)` onto `Snapshots`,
   and fires `BeatApplied(_nextBeatIndex - 1)`.
3. If no scripted beat matches the current AI player, return
   `AiDispatcher.ChooseForCurrentPlayer(state, color, visited, rng)` —
   the AI plays the rest of their turn under their normal `AiKind`
   (typically `Heuristic` for opponents authored as live AIs).

**Overlay beats** (`Prompt` / `Highlight` / `CameraFocus`) — these aren't
gated through any controller path; `TutorialPlayer` applies them directly:
1. Render the overlay (PreviewPane fires `BeatApplying`, mounts the
   bubble / highlight / camera move).
2. Schedule dismissal per `DismissOn`. On dismiss: increment
   `_nextBeatIndex`, push snapshot (if any state changed — usually no
   for overlays), fire `BeatApplied`.

**Pre-Phase-10 behavior**: until scripted AI beats land, AiChooser
falls through to `AiDispatcher` always. The existing passive
`TutorialAi.cs` keeps its no-op behavior for the standalone Play Tutorial
menu entry where no `TutorialPlayer` is wired in. From Phase 10, in
Preview, `TutorialPlayer.AiChooser` is the delegate handed directly to
`GameController` — we bypass `AiDispatcher` entirely. The standalone
`TutorialAi.cs` stays passive.

### Per-beat snapshots

After each beat applies (player-action or AI), `TutorialPlayer` pushes
`GameStateSnapshot.Capture(state)` onto its own list, indexed by beat.
This is independent of `session.Undo` (which is per-turn, cleared on
`EndTurn`). The scrubber (Phase 13) uses these to rewind. Memory cost is
O(beats × grid size); for typical tutorials (≤50 beats, ~80-tile maps)
this is bounded and acceptable.

## TutorialBuilder scene

### `tutorial_builder.tscn`

Root: `TutorialBuilderScene : Node2D`. Three child layers:

1. **Top bar** (`TutorialTopBar : Control`, CanvasLayer)
   - Segmented control: Map Edit (1) / Build (2) / Preview (3)
   - Save Tutorial / Load Tutorial / Exit buttons
   - Tutorial title field (editable)
   - Keyboard 1/2/3 for mode switch (suppressed when text input has
     focus per S1)
   - Event: `ModeChanged(TutorialMode)`,
     `SaveTutorialPressed`, `LoadTutorialPressed`, `ExitPressed`,
     `TitleChanged(string)`
2. **MapEditorPanel** (always present; see Phase 1 refactor below)
3. **Mode-specific chrome** (`BuildPane` and `PreviewPane`, mutually
   exclusive)

### Mode transitions

| From → To | Behavior |
|---|---|
| Map Edit → Build | Disable paint on panel; show BuildPane chrome; if no `Tutorial` loaded, instantiate empty one with `StartPlayer = 0` |
| Map Edit → Preview | Disable paint; instantiate transient `GameController` against a clone of the editor's draft; bind `TutorialPlayer`; show PreviewPane chrome |
| Build → Map Edit | Hide BuildPane; warn if `Tutorial.Beats.Count > 0` ("Map edits may invalidate beats. Continue?"); enable paint |
| Build → Preview | Hide BuildPane; instantiate transient controller (as above) |
| Preview → Map Edit | Pause `TutorialPlayer`; dispose transient controller; clear scrubber state; warn as above |
| Preview → Build | Pause; dispose; show BuildPane (with the beat-being-played selected, per S7) |

Map editing while beats exist runs the orphan-detector on commit (Phase
12).

### `MapEditorPanel` refactor (Phase 1)

Pure refactor — no behavior change. Today's `MapEditorScene` does both
"scene root" and "editor body." Phase 1 splits them:

**`MapEditorPanel : Node2D`** owns:
- `_grid`, `_water`, `_territories` (draft state)
- `_map: HexMapView` instance (mounted as child)
- `_hud: MapEditorHudView` instance (just the palette + undo bar — NOT
  the Save/Load/Exit buttons)
- Paint logic (paint stroke state machine, `MapEditPaint` calls)
- Hover tooltip
- `_undoStack: UndoStack<EditorSnapshot>`

Public API:

```csharp
public sealed partial class MapEditorPanel : Node2D
{
    public event Action? DraftChanged;
    public event Action<bool, bool>? UndoStateChanged;  // canUndo, canRedo

    public void LoadFromMap(LoadedSave loaded);   // LoadMap currently in scene
    public void GenerateMap(int seed);            // GenerateRequested currently
    public EditorSnapshot SnapshotDraft();        // for Preview cloning
    public void RestoreDraft(EditorSnapshot snap);

    public bool PaintingEnabled { get; set; }     // false in Build/Preview
    public GameState BuildLiveState();            // for Preview seeding
    public GameState BuildSaveState();            // turnNumber=0 starting map
}
```

**`MapEditorScene : Node2D`** (slimmed) owns:
- Scene-root chrome: Save Map / Load Map / Exit buttons
- Save/Load dialogs
- Hosts `MapEditorPanel`

Wires the panel's `DraftChanged` to update its own UI; wires Save Map to
`panel.BuildSaveState()` then `_saveStore.WriteMapSlot(...)`. The Map
Editor menu entry continues to launch `map_editor.tscn` and behaves
identically to today. Existing `EditorSnapshotTests` and
`EditorUndoStackTests` keep passing.

### Build mode (`BuildPane`)

A `Control` overlay shown when mode == Build. Layout:

- **Right side** — beat inspector (kind-specific fields for the selected
  beat). When no beat is selected, shows the "Add Beat" palette:
  `EndTurn`, `BuyPeasant`, `BuildTower`, `Move`, `Prompt`, `Highlight`,
  `CameraFocus`.
- **Bottom** — timeline strip. Phase 8 → flat ordered list with `(turn,
  actor)` chips. Phase 10 → upgraded to turn-lane layout with ghost AI
  beats.
- **Top-right** — validation banner area (Phase 12).

Authoring a player-action beat:
- `EndTurn`: button → immediately appends an `EndTurnBeat` to the current
  authoring lane (current `(Turn, Actor)`).
- `BuyPeasant`: button enters "pick At tile" mode; next click on a
  current-actor-owned tile creates the beat.
- `BuildTower`: same as BuyPeasant.
- `Move`: button enters "pick Src" mode; click on actor's unit, then
  destination → creates `MoveBeat`.

Authoring an overlay beat:
- `Prompt`: button opens an inline editor for body text + dismiss mode +
  anchor (default `NoneAnchor`). Anchor picker: "Anchor to..." offers
  Tile (next-click), HUD (dropdown), Region (multi-click + commit), None.
- `Highlight` / `CameraFocus`: similar.

Each commit updates the in-memory `Tutorial` and recomputes the
post-beat state cache (used by the inspector to show what the map looks
like after each beat — see "State-after-beat-N" below).

### Preview mode (`PreviewPane`)

A `Control` overlay shown when mode == Preview. Layout:

- **Center** — the live `HexMapView` (shared with the panel).
- **Bottom** — scrubber dock (Phase 13, dev-only).
- **Top-right** — current beat narration (if any) + tiny status:
  `"Beat 5/12 — actor 0 — Move"`.

On enter:
1. Clone the panel's draft via `panel.SnapshotDraft()` (separate state —
   Preview never mutates the editor's draft).
2. Build a fresh `GameState` from the clone.
3. Build wrapper views (`TutorialGatedHexMapView` /
   `TutorialGatedHudView`).
4. Construct a transient `GameController` against the wrappers, with
   `TutorialPlayer.AiChooser` and a `SynchronousAiPacer` (Preview wants
   snappy step-by-step; the scrubber controls timing, not the pacer).
5. `player.Bind(ctrl, gatedMap, gatedHud); ctrl.StartGame();`

On exit: dispose the transient controller, restore `panel.PaintingEnabled
= true`.

### State-after-beat-N (Build inspector)

When the dev selects beat N in the timeline, BuildPane shows the map
state after beats `0..N` have been applied to the starting state.

BuildPane keeps a `Dictionary<int, GameStateSnapshot>` keyed by beat
index, populated lazily from the starting state. Each beat-add appends
one snapshot. Each beat-edit invalidates every entry from that index
onward — they recompute on next selection. Single-beat application
reuses `AiSimulator.Apply`, which already knows how to mutate a cloned
`GameState` without a `GameController`.

## Save / load

### `SaveSerializer` v2 → v3

- Bump `FormatVersion = 3`.
- Add optional top-level `"Tutorial"` field (the JSON shape under "Data
  model" above).
- Backwards compat: the deserializer accepts both v2 and v3.
  v2 files load with `Tutorial = null`. v3 files without a `Tutorial`
  block load identically. The next bump (v4) is reserved for a
  non-additive change.

### `SaveStore`

- New methods: `WriteTutorial(string slotName, GameState mapState,
  Tutorial tutorial)`, `LoadTutorial(string slotName) → (LoadedSave,
  Tutorial)`, `ListTutorials() → IReadOnlyList<SaveSlotInfo>`.
- Reads/writes `user://tutorials/`. Bundled `res://tutorials/` is read
  via existing `LoadBundledMap` (now upgraded to also surface the
  `Tutorial` block if present).

### `LoadedSave`

Add `Tutorial? Tutorial { get; init; }` (nullable). Default null.

## Validation (Phase 12)

`TutorialValidator.Validate(Tutorial, GameState startingState) →
IReadOnlyList<ValidationIssue>` runs on every beat-list change in Build.

```csharp
public sealed record ValidationIssue(
    ValidationSeverity Severity,   // Error | Warning
    int? BeatIndex,                // null = tutorial-level
    string Message);
```

Rules:
- **Error** — every authored turn lane must contain ≥1 player-action beat
  for that turn's actor (otherwise the player has nothing to do).
- **Error** — `EndTurn` for actor N must be the last player-action beat in
  that actor's lane within a turn.
- **Error** — first beat must match `StartTurn` / `StartPlayer` (S9).
- **Error** — `Move`'s `Src == Dst`.
- **Warning** — anchor coords reference tiles that no longer exist on the
  current map (orphan).
- **Warning** — HUD anchor `Id` not recognized by `HudView.GetAnchorRect`.

Errors block save (toast + red banner). Warnings flag the beat yellow in
the timeline but allow save.

Cascade-fix wizard (S6, S9) is **deferred to a follow-up**. v1 surfaces
warnings inline; the dev manually re-anchors via the inspector.

## Scrubber (Phase 13)

Dev-only chrome inside Preview. `OS.IsDebugBuild()` guard around the
`AddChild(scrubber)` call.

`TutorialScrubber : Control` (CanvasLayer, bottom-anchored). Owns:
- Tick rail rendering (one tick per beat; current beat is "major")
- Drag head + ghost head (for in-progress drag)
- Play / Pause / Step (← / →) / Restart (HOME) buttons
- Speed selector (0.5×, 1×, 2× — applies only to AI-actor and overlay
  beats; player-actions still wait for input)

Events back to `PreviewPane`:

```csharp
event Action?       PlayPressed;
event Action?       PausePressed;
event Action<int>?  SteppedTo;          // beat index
event Action<int>?  ScrubStarted;       // initial beat index
event Action<int>?  ScrubPreview;       // current drag beat
event Action<int>?  ScrubCommitted;     // released
event Action?       ScrubCancelled;     // ESC mid-drag
event Action<float>? SpeedChanged;
```

`PreviewPane.OnSteppedTo(int n)`:
1. Dispose transient controller.
2. Reconstruct from the clone + apply
   `TutorialPlayer.Snapshots[n]` directly to `GameState`.
3. Reset `TutorialPlayer.CurrentBeatIndex = n`.
4. Re-arm the gated views; resume play from beat n+1.

Breadcrumb (S8): a small overlay near the head showing ±5 beats around
the current position, with undone beats at 0.45 opacity.

## Dev-mode gating

- `MainMenuScene` adds a `TutorialBuilderClicked` button visible iff
  `OS.IsDebugBuild()`. Clicked → `ChangeSceneToFile("res://scenes/tutorial_builder.tscn")`.
- `PreviewPane` checks `OS.IsDebugBuild()` before mounting the scrubber.
  Production builds run the tutorial straight through with no rewind
  affordance.
- `Tutorial.json` is shipped via `res://tutorials/`. The Play Tutorial
  menu entry is unchanged and works in production builds.

## ARCHITECTURE.md updates

After Phase 14:

- Add a `## Tutorial system` section describing `TutorialPlayer`,
  `TutorialValidator`, the gated-view wrapper pattern, and the per-beat
  snapshot stack.
- Add `tutorial_builder.tscn` and `MapEditorPanel.cs` to the file
  layout.
- Note the SaveSerializer bump (v2 → v3) and the optional `Tutorial`
  block.
- Document the `AiKind.Tutorial` repurposing (Phase 10).

Each phase that lands an architectural change updates the section
incrementally.

## Tests

Per `CLAUDE.md`'s strict-TDD rule, every logic-layer change is
test-driven. View-layer changes are validated manually.

### New unit-test files (added to `tests/FourExHex.Tests.csproj`
`<Compile Include>` list)

- `TutorialSerializerTests.cs` — JSON round-trip for every BeatKind and
  AnchorRef variant; v2-file load with `Tutorial = null`; malformed v3
  rejection.
- `TutorialValidatorTests.cs` — every rule (errors + warnings); orphan
  anchor after map mutation; first-beat-matches-start.
- `TutorialPlayerTests.cs` — uses `MockHexMapView` / `MockHudView` +
  `TutorialGatedHexMapView` / `TutorialGatedHudView` + `SynchronousAiPacer`:
  - Player-action match accepts; mismatch fires
    `PlayerActionRejected` and HUD shows toast.
  - AI-scripted beat returned via `AiChooser`; falls through to
    `AiDispatcher` when no scripted beat for current AI player.
  - `BeatApplied` ordering vs `RefreshViews()`.
  - Snapshot stack length matches applied count.
- `TutorialBeatSimulatorTests.cs` — applying each BeatKind to a cloned
  `GameState` matches what `GameController` would produce given the same
  input.

### Existing tests that must stay green

- `EditorSnapshotTests` and `EditorUndoStackTests` after Phase 1 refactor.
- `GameControllerTests` (no changes — TutorialPlayer wraps, doesn't
  modify, the controller).
- `SaveSerializerTests` (extend with v3 round-trip; old v2 cases stay).
- All rules tests (untouched).

### View-layer manual tests

Per the per-phase manual test column in the phase plan below. View
changes (`tutorial_builder.tscn`, `BuildPane`, `PreviewPane`,
`TutorialScrubber`, anchor-rect resolution in `HudView`) require launch
+ click + visual confirmation, not unit tests.

## Phases

Sequential, each manually testable. Each phase = one PR (or one
implementation-plan run by a future agent).

### Phase 1 — Refactor `MapEditorScene` → `MapEditorPanel`

- Extract draft state, paint, palette, undo into `MapEditorPanel`.
- `MapEditorScene` keeps Save Map / Load Map / Exit chrome and hosts the
  panel.
- New tests: none (refactor only). Existing editor tests must stay green.
- **Manual**: launch Map Editor, paint, save, load, undo, redo, exit.
  Identical to today.

### Phase 2 — `tutorial_builder.tscn` + 3-mode topbar

- New scene + `TutorialBuilderScene` root.
- TopBar: mode buttons + Save Tutorial / Load Tutorial / Exit (Save/Load
  are no-ops — disabled — until Phase 3).
- Map Edit hosts `MapEditorPanel`. Build / Preview show placeholder
  panes ("Coming soon — Phase N").
- Keyboard 1/2/3.
- `MainMenuScene` adds debug-only "Tutorial Builder" button.
- New tests: none.
- **Manual**: launch from menu (debug build), switch modes, paint in Map
  Edit, see placeholders in Build/Preview.

### Phase 3 — Beat schema v3 + EndTurn beat (author + preview)

- Beat / Tutorial / AnchorRef / DismissOn POCOs.
- `SaveSerializer` v2 → v3, optional Tutorial block.
- `SaveStore.WriteTutorial` / `LoadTutorial` / `ListTutorials`.
- TopBar Save / Load Tutorial enabled; lists from `user://tutorials/`.
- BuildPane minimal: empty timeline + "Add EndTurn" button + inspector
  showing only `(Turn, Actor)`.
- PreviewPane minimal: instantiates transient `GameController` +
  `TutorialPlayer`; `EndTurn` click gated.
- `TutorialPlayer` (no AI scripting yet — AI chooser falls through to
  `AiDispatcher` always).
- New tests: `TutorialSerializerTests` (round-trip + v2 fallback),
  `TutorialPlayerTests` (EndTurn match/mismatch).
- **Manual**: in Build, add EndTurn beat for actor 0 turn 1, save,
  reload, switch to Preview, click End Turn → accepted; tutorial ends.

### Phase 4 — `BuyPeasant` beat (author + preview)

- BuildPane: "Add BuyPeasant" enters "pick At" mode → next click on a
  current-actor-owned tile creates the beat.
- TutorialPlayer: gates BuyPeasant click against next expected beat.
- TutorialBeatSimulator: applies `BuyPeasantBeat` to a state.
- New tests: `TutorialPlayerTests` (BuyPeasant match/mismatch);
  `TutorialBeatSimulatorTests` (BuyPeasant apply equivalence).
- **Manual**: paint a 5-tile territory with capital in Map Edit, switch
  to Build, add BuyPeasant beat at a friendly tile, save, switch to
  Preview, click designated tile → peasant placed.

### Phase 5 — `Move` beat (author + preview)

- BuildPane: "Add Move" enters "pick Src" then "pick Dst" mode.
- TutorialPlayer: gates Move (TileClicked → TileClicked sequence).
- TutorialBeatSimulator: applies `MoveBeat`.
- New tests: as Phase 4 for Move.
- **Manual**: continue Phase 4 setup, add Move beat moving the peasant,
  Preview through.

### Phase 6 — `BuildTower` beat (author + preview)

- Mirror of Phase 4 for `BuildTowerBeat`.
- **Manual**: author + preview a BuildTower.

### Phase 7 — `Prompt` beat + bubble UI (Tile anchor only)

- New `TutorialPromptBubble : Control` (CanvasLayer overlay) with
  positioned arrow.
- BuildPane: "Add Prompt" → inline editor for body + dismiss mode +
  tile-anchor pick.
- PreviewPane: when current beat is Prompt, render bubble; auto-dismiss
  per `DismissOn`.
- New tests: `TutorialPlayerTests` (prompt show/dismiss timing,
  `BeatApplied` fires after dismiss).
- **Manual**: author Prompt anchored to a tile, Preview, bubble appears,
  dismisses correctly per Click / NextBeat / Delay.

### Phase 8 — `Highlight` beat

- Reuse `_map.ShowHighlight` overlays for tile target.
- BuildPane: "Add Highlight" → tile pick + style toggle (Ring/Spot).
- New tests: `TutorialPlayerTests` (highlight show/clear timing).
- **Manual**: author Highlight, Preview, tile pulses; auto-clears on
  next beat.

### Phase 9 — `CameraFocus` + multi-anchor (Hud / Region / None)

- New `HudView.GetAnchorRect(string id) → Rect2?` for HUD anchors.
- AnchorRef pickers in BuildPane: HUD dropdown, Region multi-click +
  commit, None.
- CameraFocus beat: pans `HexMapView` camera to center on target;
  preserves dev's manual zoom (scope decision #5).
- New tests: `TutorialBeatSimulatorTests` (no-op for these — pure UI);
  manual coverage primary.
- **Manual**: author HUD-anchored prompt (e.g., on End Turn button),
  Region highlight (3-tile region), centered prompt, CameraFocus pan;
  Preview each.

### Phase 10 — Multi-turn timeline + AI ghost beats

- Timeline upgrades to turn-lane layout; ghost beats appear in AI lanes.
- `TutorialPlayer.AiChooser` activates the scripted-beat-for-AI-actor
  branch (returns next scripted beat as `AiAction`); falls back to
  `AiDispatcher.ChooseForCurrentPlayer` if no scripted beat for current
  AI player.
- `AiKind.Tutorial` is no longer a passive stub when a `TutorialPlayer`
  is bound — TutorialPlayer's chooser is wired in directly, bypassing
  `TutorialAi.cs`. The standalone `TutorialAi.cs` keeps its passive
  behavior for the un-bound (Play Tutorial without scripted beats) case.
- New tests: `TutorialPlayerTests` (AI scripted beat dispatched; AI
  fallthrough to Heuristic when no script).
- **Manual**: author a tutorial with actor 0 (human) + actor 1 (AI)
  alternating turns; ghosts appear in actor 1 lanes; in Preview, AI
  takes its turn auto.

### Phase 11 — Beat editing (inspector / reorder / delete / right-click)

- Click beat → inspector populates with kind-specific fields; edits
  commit on blur or ENTER.
- Reorder via [/] keys or drag-within-lane.
- Delete via ⌫ or right-click → "Delete beat" (confirms).
- Right-click context menu: Edit, Re-anchor, Duplicate (⌘D), Move ↑/↓
  ([/]), Delete (⌫).
- After every edit, `Tutorial` is mutated and the post-beat state cache
  is invalidated from that index onward.
- New tests: `TutorialMutationTests` (insert/edit/reorder/delete update
  beat indices correctly).
- **Manual**: author + edit + reorder + delete beats; Preview reflects
  changes.

### Phase 12 — Validation banners + orphan handling

- `TutorialValidator.Validate` runs after every Build mutation and after
  every Map Edit commit.
- Errors → red banner + block save. Warnings → yellow flag on offending
  beat in timeline.
- New tests: `TutorialValidatorTests` (every rule).
- **Manual**: edit map to break an anchor → yellow flag; remove only
  player-action in a lane → red banner.

### Phase 13 — Scrubber + per-beat snapshots

- `TutorialScrubber : Control` (debug-only).
- `TutorialPlayer.Snapshots` populated on every BeatApplied (already
  spec'd, becomes load-bearing here).
- PreviewPane wires scrubber events.
- Step / play / pause / scrub-back / restart all functional.
- New tests: `TutorialPlayerTests` (snapshot push timing; rewind
  restores state).
- **Manual**: in Preview, scrub backward through beats, watch state
  rewind. Step ←/→. SPACE play/pause.

### Phase 14 — Polish

- Keyboard shortcuts: A (Add Player Action), T (Text Prompt), H
  (Highlight), F (Camera Focus), ⌘D (Duplicate), [/] (Move ↑↓), ⌫
  (Delete), HOME (Restart), SHIFT+drag (slow scrub), SPACE (play/pause).
- Breadcrumb (S8): ±5 beats around playhead.
- Auto-pause at last beat (Preview).
- ESC ladders out (S1: pauses preview before mode change; first ESC in
  Build cancels current placement, second ESC drops to Map Edit, third
  exits scene).
- Edge cases from S1-S9 spec panels (cross-lane reorder rejected,
  identical Src/Dst rejected, etc.).
- ARCHITECTURE.md gets the full Tutorial system section.
- **Manual**: every keyboard shortcut; auto-pause; ESC ladder.

## Out of scope (deferred)

- **Branching tutorials** — would add `OnSuccess` / `OnFail` jumps to
  Beat. Future revision.
- **Localization** — `Prompt.Body` becomes a translation key. Future.
- **Persisted dev state** — scrubber position survival across
  open/close. Future.
- **Cascade-fix wizard for orphans** — Phase 12 surfaces warnings and
  the dev manually re-anchors via inspector.
- **In-game promote-to-bundled workflow** — `res://` is read-only at
  runtime; promotion is a manual file copy.
- **Capture-as-distinct-beat-kind** — Capture is just a `Move` onto an
  enemy tile, executed identically by `MovementRules.Move`.
- **Wait beat** — replaced by `DismissOn: Delay:Nms` on overlay beats.
- **Starting-unit placement in Map Edit** — Map Edit doesn't author
  unit placements; tutorials must use `BuyPeasant` to introduce units.
  Adding starting-unit placement is a separate sub-feature.
