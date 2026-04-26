# FourExHex Architecture

Snapshot of the architecture as it stands today. Start here if you're
new to the codebase. The MVC split (Main → GameController → views /
model / rules) is the load-bearing structure; everything else hangs
off it.

## Layered view

```
┌──────────────────────────────────────────────────────────────────────────┐
│                            SCENE ROOT (Godot)                            │
│                                                                          │
│   Main (Node2D)                                                          │
│   └─ _Ready:                                                             │
│      1. Read GameSettings (player kinds set by the main menu, or         │
│         forced to all-Heuristic when FOUREXHEX_6AI is set)               │
│      2. Build players + grid (random tiles + ~5% trees)                  │
│      3. TerritoryFinder.FindAll → CapitalReconciler.Reconcile            │
│      4. Construct GameState + SessionState                               │
│      5. Pick views: real HexMapView/HudView, or HeadlessHexMapView/      │
│         HeadlessHudView when in diagnostic mode                          │
│      6. Pick pacer: GodotAiPacer (visible delays) or                     │
│         SynchronousAiPacer (diagnostic — runs inline)                    │
│      7. new GameController(state, session, map, hud,                     │
│           aiChooser: AiDispatcher.ChooseForCurrentPlayer,                │
│           aiPacer:  pacer,                                               │
│           maxTurnNumber: diagnostic ? 500 : int.MaxValue)                │
│      8. controller.StartGame()                                           │
│   Owns no game logic, no state.                                          │
└─────────────────────────────┬────────────────────────────────────────────┘
                              │
                              ▼
┌──────────────────────────────────────────────────────────────────────────┐
│                         CONTROLLER (pure C#)                             │
│                                                                          │
│   GameController                                                         │
│   ├─ refs: IHexMapView _map, IHudView _hud                               │
│   ├─ refs: GameState _state, SessionState _session                       │
│   ├─ injected: aiChooser delegate, IAiPacer, maxTurnNumber               │
│   │                                                                      │
│   ├─ subscribes in ctor:                                                 │
│   │    map.TileClicked          → OnTileClicked                          │
│   │    hud.BuyPeasantClicked    → OnBuyPressed (cycles unit level)      │
│   │    hud.BuildTowerClicked    → OnBuildTowerPressed                    │
│   │    hud.UndoLastClicked      → OnUndoLastPressed                      │
│   │    hud.UndoTurnClicked      → OnUndoTurnPressed                      │
│   │    hud.RedoLastClicked      → OnRedoLastPressed                      │
│   │    hud.RedoAllClicked       → OnRedoAllPressed                       │
│   │    hud.EndTurnClicked       → OnEndTurnPressed                       │
│   │    hud.NextTerritoryClicked → OnNextTerritoryPressed                 │
│   │    hud.CancelActionPressed  → OnCancelActionPressed                  │
│   │   (NewGameClicked / MainMenuClicked are handled in Main, not here)   │
│   │                                                                      │
│   ├─ click policy state machine:                                         │
│   │    OnTileClicked → pending-mode branch (buy/build/move)              │
│   │                  → SetSelection branch                               │
│   │                                                                      │
│   ├─ action handlers:                                                    │
│   │    ExecuteBuyAndPlace → debit gold + MovementRules.PlaceNew          │
│   │                       → if capture: HandleCapture                    │
│   │    ExecuteMove        → MovementRules.Move                           │
│   │                       → if capture: HandleCapture                    │
│   │    ExecuteBuildTower  → debit gold + drop Tower (no capture path)    │
│   │                                                                      │
│   ├─ AI loop (paced via IAiPacer):                                       │
│   │    RunAiTurnsUntilHumanOrDone → preview → execute beats              │
│   │    ExecuteAiMove / ExecuteAiBuyUnit / ExecuteAiBuildTower —          │
│   │      validate then mutate (illegal AI action throws)                 │
│   │                                                                      │
│   ├─ capture reconciliation:                                             │
│   │    HandleCapture → TerritoryFinder.FindAll                           │
│   │                  → CapitalReconciler.Reconcile                       │
│   │                  → Treasury.ReconcileAfterCapture                    │
│   │                  → _map.RebuildAfterTerritoryChange                  │
│   │                  → WinConditionRules.WinnerByDomination (mid-turn)   │
│   │                                                                      │
│   ├─ undo/redo:                                                          │
│   │    Each human action: _session.Undo.PushBefore(snapshot)             │
│   │    AI actions are NOT undoable (undo cleared at end-of-turn)         │
│   │    OnUndoLast / OnUndoTurn / OnRedoLast / OnRedoAll → ApplySnapshot  │
│   │                                                                      │
│   ├─ turn rotation:                                                      │
│   │    OnEndTurnPressed → undo.Clear                                     │
│   │                     → EndOfTurnProcessing (win check only)           │
│   │                     → AdvanceToNextActivePlayer (skip eliminated)    │
│   │                     → StartPlayerTurn (growth → reset → income →     │
│   │                                        upkeep)                       │
│   │                     → RunAiTurnsUntilHumanOrDone                     │
│   │                                                                      │
│   └─ single UI update path:                                              │
│        RefreshViews() → _hud.Refresh(state, session, hasActionable)      │
│                       → _map.RefreshOccupantVisuals(playerColor, tr.)    │
└──────┬──────────────────────────────────┬────────────────────────────────┘
       │                                  │
       ▼                                  ▼
┌───────────────────────────┐  ┌────────────────────────────────────────────┐
│   MODEL / STATE (pure C#) │  │          VIEWS (Godot Nodes)               │
│                           │  │                                            │
│   GameState               │  │   HexMapView : Node2D, IHexMapView         │
│   ├─ Grid                 │  │   ├─ Init(state) — injected before _Ready  │
│   ├─ Territories          │  │   ├─ event TileClicked(HexTile?)           │
│   ├─ Players              │  │   ├─ TerritoryAt(coord)                    │
│   ├─ Turns                │  │   ├─ ShowHighlight(territory)              │
│   └─ Treasury             │  │   ├─ ShowMoveTargets(coords)               │
│                           │  │   ├─ ShowMoveSource(coord?)                │
│   SessionState            │  │   ├─ RebuildAfterTerritoryChange()         │
│   ├─ Winner (Color?)      │  │   ├─ RefreshOccupantVisuals(color, tr.)    │
│   ├─ SelectedTerritory    │  │   └─ layers: borders / capitals / units /  │
│   ├─ Mode (enum)          │  │             towers / trees / graves /     │
│   ├─ MoveSource           │  │             targets / highlight            │
│   └─ Undo (UndoStack)     │  │                                            │
│                           │  │   HudView : CanvasLayer, IHudView          │
│                           │  │   ├─ events: BuyPeasant / BuildTower /     │
│                           │  │     UndoLast / UndoTurn / RedoLast /       │
│                           │  │     RedoAll / EndTurn / NewGame /          │
│                           │  │     MainMenu / NextTerritory /             │
│                           │  │     CancelAction                           │
│                           │  │   └─ Refresh(state, session, hasAct.)      │
│                           │  │                                            │
│                           │  │   HeadlessHexMapView / HeadlessHudView —   │
│                           │  │   no-op stubs for diagnostic mode          │
└─────────────┬─────────────┘  └────────────────────────────────────────────┘
              │
              ▼
┌──────────────────────────────────────────────────────────────────────────┐
│                         PURE RULES (static)                              │
│                                                                          │
│   TerritoryFinder.FindAll(grid)            ─ flood-fill, no capitals     │
│   CapitalPlacer.Choose(coords, grid)       ─ empty > unit, lex-min       │
│   CapitalReconciler.Reconcile(raw, old, grid)                            │
│                                            ─ split/merge + stomping      │
│   PurchaseRules.CostFor / CanAfford / CanAffordTower / IsValidPeasant…   │
│   MovementRules.ValidTargets / Move / PlaceNew                           │
│   DefenseRules.Defense(coord, grid, territory)                           │
│   TreeRules.RunStartOfTurnGrowth / ConvertGravesToTrees /                │
│             CountIncomeProducingTiles                                    │
│   UpkeepRules.UpkeepFor / TotalUpkeepFor / ApplyUpkeep / ApplyUpkeepFor  │
│   WinConditionRules.WinnerByDomination (mid-turn)                        │
│                    .WinnerAtEndOfTurn (sole capital-bearer)              │
│                    .IsEliminated                                         │
└──────────────────────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────────────────────┐
│                         MODEL PRIMITIVES                                 │
│                                                                          │
│   HexCoord (struct, IEquatable, IComparable)                             │
│   HexGrid — Dictionary<HexCoord, HexTile>                                │
│   HexTile — Coord, Color, Visual, Occupant                               │
│   HexOccupant (abstract)                                                 │
│     ├─ Unit — Owner, Level, HasMovedThisTurn                             │
│     ├─ Capital — marker                                                  │
│     ├─ Tower — marker (defense, no upkeep)                               │
│     ├─ Tree — marker (blocks income; movement onto a tree consumes the   │
│     │         action and clears the tile)                                │
│     └─ Grave — marker (blocks income; converts to a Tree at the start    │
│                of the owning color's next turn)                          │
│   UnitLevel — Peasant=1, Spearman=2, Knight=3, Baron=4                   │
│   Territory — Owner, Coords, Capital (immutable)                         │
│   TerritoryExtensions — BuildTileIndex                                   │
│   Player — Name, Color, Kind (AiKind), IsAi                              │
│   AiKind — Human, Random, Heuristic                                      │
│   TurnState — Players[], CurrentPlayerIndex, TurnNumber                  │
│   Treasury — Dictionary<HexCoord, int>; CollectIncomeFor;                │
│              ReconcileAfterCapture (forfeits enemy gold on capture)      │
│   GameStateSnapshot — deep-copy (tiles + gold + territories)             │
│   UndoStack — two-sided history                                          │
│   GameSettings — global PlayerConfig (name, color hex) + PlayerKinds     │
│                  array; written by MainMenuScene, read by Main           │
└──────────────────────────────────────────────────────────────────────────┘
```

## Key contracts

**`IHexMapView`** — everything the controller asks the map to do:

```csharp
event Action<HexTile?>? TileClicked;
Territory? TerritoryAt(HexCoord coord);
void ShowMoveTargets(IEnumerable<HexCoord> coords);
void ShowMoveSource(HexCoord? coord);
void ShowHighlight(Territory? selected);
void RebuildAfterTerritoryChange();
void RefreshOccupantVisuals(Color? currentPlayerColor, Treasury treasury);
```

**`IHudView`** — everything the controller asks the HUD to do:

```csharp
event Action? BuyPeasantClicked;       // cycles Peasant→Spearman→Knight→Baron
event Action? BuildTowerClicked;
event Action? UndoLastClicked;
event Action? UndoTurnClicked;
event Action? RedoLastClicked;
event Action? RedoAllClicked;
event Action? EndTurnClicked;
event Action? NewGameClicked;          // handled in Main (scene reload)
event Action? MainMenuClicked;         // handled in Main (scene change)
event Action? NextTerritoryClicked;    // Tab hotkey equivalent
event Action? CancelActionPressed;     // Escape hotkey equivalent
void Refresh(GameState state, SessionState session, bool hasActionableRemaining);
```

**`IAiPacer`** — schedules deferred continuations for the AI step
machine. `GodotAiPacer` schedules via the `SceneTree`; the default
`SynchronousAiPacer` runs the callback inline (used by tests and
diagnostic mode).

```csharp
void Schedule(Action callback, int delayMs);
```

## Invariants (enforced by design)

- **Views never mutate the model.** Methods that *look* like mutations
  (`ShowHighlight`, `RebuildAfterTerritoryChange`) only touch view
  state.
- **Controller never touches Godot Nodes directly.** It talks to views
  through the interfaces above and to the event loop through
  `IAiPacer`. This is what makes the entire `GameController`
  unit-testable with mocks (see `tests/GameControllerTests.cs`).
- **Every state change funnels through `RefreshViews()`** at the end
  of the handler. One path, no drift.
- **`SessionState` never ends up in a snapshot.** Only `GameState`
  does, so undo/redo can't resurrect UI artifacts.
- **`HexTile.Color` is the single source of truth for tile
  ownership.** Its setter pushes the new color into the attached
  `Polygon2D`, so the logical color and rendered fill can't drift.
- **Undo is turn-scoped.** `OnEndTurnPressed` clears the stack, so
  ending a turn commits everything.
- **AI actions are not undoable** (undo gets cleared at end-of-turn
  anyway), and the AI execute methods validate preconditions before
  mutating — an illegal AI action throws and halts the game in an
  obvious error state rather than corrupting state silently.

## Turn structure

A turn is sandwiched between two phases:

### Start-of-turn — `StartPlayerTurn()`

Runs in this fixed order for the now-current player:

1. **Tree growth** — `TreeRules.RunStartOfTurnGrowth` (skipped during
   round 1, i.e. while `TurnNumber == 1`). Graves on the current
   player's tiles become trees; empty cells of their color with ≥2
   neighboring trees become trees.
2. **Reset movement** — `HasMovedThisTurn` cleared on the current
   player's units.
3. **Collect income** — `Treasury.CollectIncomeFor` (skipped during
   round 1; the seed from `SeedStartingGold` is the round-1 bankroll).
   Tree and grave tiles don't pay; everything else (empty, units,
   capitals, towers) pays 1 gold.
4. **Apply upkeep** — `UpkeepRules.ApplyUpkeepFor`. Per-unit costs:
   Peasant 2, Spearman 6, Knight 18, Baron 54. A territory that
   can't pay total upkeep goes bankrupt: every unit in it becomes a
   `Grave`, remaining gold stays.

The income → upkeep ordering matters: it lets the same turn's income
subsidize that turn's upkeep before bankruptcy is checked.

### End-of-turn — `EndOfTurnProcessing()`

Just the **end-of-turn win check**: `WinConditionRules.WinnerAtEndOfTurn`
returns the current player iff they're the sole owner of any
capital-bearing territory. (Orphan singletons of other colors don't
keep the game alive.)

### Win conditions

Two independent checks fire from different places:

- **Mid-turn (domination)** — `WinConditionRules.WinnerByDomination`
  fires inside `HandleCapture` after every capture. Requires that one
  color owns *every* tile on the grid. The killing blow ends the
  game immediately and clears undo.
- **End-of-turn (sole capital-bearer)** — `WinConditionRules.WinnerAtEndOfTurn`
  fires inside `EndOfTurnProcessing`. Looser: the current player
  wins if no other player still has a capital-bearing territory.
  This is the typical victory path.

### Rotation

`AdvanceToNextActivePlayer()` calls `TurnState.EndTurn()` (which
increments `TurnNumber` on wrap) then loops while
`WinConditionRules.IsEliminated(nextPlayer, grid)` is true — wiped-out
players are skipped entirely.

## Call flows

### Click → select (normal case)

```
HexMapView._UnhandledInput
  → TileClicked(tile)
GameController.OnTileClicked
  ├─ session.Mode == None → skip pending branch
  ├─ tile.territory is current player's → SetSelection(territory)
  │     ├─ session.SelectedTerritory = territory
  │     ├─ _map.ShowHighlight(territory)
  │     └─ RefreshViews()
  │           ├─ _hud.Refresh(state, session, hasActionable)
  │           └─ _map.RefreshOccupantVisuals(color, treasury)
  └─ tile has unmoved own unit → enter MovingUnit mode
        ├─ session.Mode = MovingUnit
        ├─ session.MoveSource = tile.Coord
        ├─ _map.ShowMoveTargets(CaptureTargetsOnly(level, territory))
        └─ _map.ShowMoveSource(tile.Coord)
```

### Click → capture

```
HexMapView → TileClicked(enemy tile)
GameController.OnTileClicked
  ├─ session.Mode == MovingUnit
  ├─ IsValidTarget(level, coord) == true
  └─ ExecuteMove(source, destination)
        ├─ session.Undo.PushBefore(CaptureCurrentSnapshot())
        ├─ MovementRules.Move → dst.Color = attacker; dst.Occupant = unit
        │                      → unit.HasMovedThisTurn = true
        ├─ HandleCapture(...)
        │     ├─ raw = TerritoryFinder.FindAll(state.Grid)
        │     ├─ state.Territories = CapitalReconciler.Reconcile(raw, old, grid)
        │     ├─ state.Treasury.ReconcileAfterCapture(old, new)
        │     │     (enemy gold on captured capital tiles is forfeited)
        │     ├─ _map.RebuildAfterTerritoryChange()
        │     └─ if WinConditionRules.WinnerByDomination → set Winner, clear undo
        ├─ RebindSelectionToContaining(destination)
        └─ FinishPendingAction()
              ├─ session.ClearPendingAction()
              ├─ _map.ShowMoveTargets([])
              ├─ _map.ShowMoveSource(null)
              └─ RefreshViews()
```

### End turn

```
HudView (End Turn button) → EndTurnClicked
GameController.OnEndTurnPressed
  ├─ session.Undo.Clear()                      // commit: no going back
  ├─ EndOfTurnProcessing()                     // end-of-turn win check
  │     └─ WinConditionRules.WinnerAtEndOfTurn → set Winner if sole capital-bearer
  ├─ if !IsGameOver:
  │     ├─ AdvanceToNextActivePlayer()         // skip eliminated players
  │     ├─ StartPlayerTurn()                   // growth → reset → income → upkeep
  │     │     (growth + income skipped during round 1)
  │     └─ RunAiTurnsUntilHumanOrDone()        // paced AI loop if next is AI
  ├─ CancelPendingAction(); SetSelection(null)
  └─ RefreshViews()
```

### Undo (symmetric for redo)

```
HudView (Undo Last button) → UndoLastClicked
GameController.OnUndoLastPressed
  ├─ if !session.Undo.CanUndo → no-op
  ├─ snap = session.Undo.UndoLast(CaptureCurrentSnapshot())
  └─ ApplySnapshot(snap)
        ├─ state.Territories = snap.ApplyTo(state.Grid, state.Treasury)
        ├─ _map.RebuildAfterTerritoryChange()
        ├─ SetSelection(null)
        ├─ CancelPendingAction()
        └─ RefreshViews()
```

### AI turn (paced)

`RunAiTurnsUntilHumanOrDone` sets up a step machine that alternates
preview and execute beats via `IAiPacer.Schedule`:

```
StepAiPreview:
  ├─ aiChooser(state, color, visited, rng) → action
  ├─ if action == null OR step cap reached:
  │     ├─ EndOfTurnProcessing
  │     ├─ AdvanceToNextActivePlayer + StartPlayerTurn
  │     └─ if next is AI: schedule next StepAiPreview
  ├─ _pendingAiAction = action
  ├─ _map.ShowHighlight(acting territory)
  └─ schedule StepAiExecute after AiPreviewDelayMs

StepAiExecute:
  ├─ run ExecuteAiMove / ExecuteAiBuyUnit / ExecuteAiBuildTower
  │     (each validates preconditions; throws on illegal action)
  ├─ re-highlight resulting territory (post-capture)
  └─ schedule next StepAiPreview after AiActionDelayMs
```

Tests use `SynchronousAiPacer` so the whole AI chain runs inline.

## AI subsystem

- **`AiAction`** — discriminated union: `AiMoveAction`, `AiBuyUnitAction`,
  `AiBuildTowerAction`.
- **`AiCommon.Enumerate`** — single source of legal candidate actions;
  both AIs consume it. Only this helper knows about rule legality.
- **`RandomAi`** — picks any positive-effect action uniformly.
- **`HeuristicAi`** — 1-ply lookahead via `AiSimulator.Clone` +
  `AiStateScorer.Score`. `AiSimulator` mirrors the mutation logic in
  `GameController`'s `ExecuteAi*` paths; if you add a new AI-capable
  action you must update both in lockstep, or simulated scoring will
  drift from real play.
- **`AiDispatcher.ChooseForCurrentPlayer`** — routes to the per-player
  AI flavor based on `Player.Kind`. Wired into `GameController` as
  the single `aiChooser` delegate.
- **`AiLog`** — `AiLog.Print` is off by default; flip
  `AiLog.Enabled = true` (or use `FOUREXHEX_6AI`) to trace every
  AI decision plus per-turn header lines and capture diffs.

## Diagnostic mode (`FOUREXHEX_6AI`)

Setting the env var before launching Godot reconfigures the session
for a fully headless regression run:

- All six player slots forced to `AiKind.Heuristic`.
- `AiLog.Enabled = true`.
- `SynchronousAiPacer` replaces `GodotAiPacer` — turns execute inline.
- `HeadlessHexMapView` / `HeadlessHudView` replace the real views.
- `GameController` constructed with `maxTurnNumber: 500` so stasis
  runs terminate.
- The scene subscribes to `GameController.GameEnded` and defers
  `SceneTree.Quit()` so the process exits on game-over.

Typical invocation:
```
FOUREXHEX_6AI=1 /Applications/Godot_mono.app/Contents/MacOS/Godot \
  --headless --path . 2>&1 | tee /tmp/ai-run.log
```

## File layout

```
scripts/
├─ Main.cs                ─ scene root; wires model + views + controller
├─ MainMenuScene.cs       ─ player-kind picker; writes GameSettings
├─ GameSettings.cs        ─ global player-kind config
├─ GameController.cs      ─ pure C# orchestration
│
├─ GameState.cs           ─ Grid, Territories, Players, Turns, Treasury
├─ SessionState.cs        ─ Winner, Selected, Mode, MoveSource, Undo
│
├─ IHexMapView.cs         ─ map view contract
├─ IHudView.cs            ─ HUD view contract
├─ HexMapView.cs          ─ concrete map: rendering + input
├─ HudView.cs             ─ concrete HUD: labels + buttons
├─ HeadlessViews.cs       ─ no-op view stubs for diagnostic mode
│
├─ AiPacer.cs             ─ IAiPacer + SynchronousAiPacer
├─ GodotAiPacer.cs        ─ SceneTree-backed pacer
├─ AiAction.cs            ─ AiMoveAction / AiBuyUnitAction / …
├─ AiCommon.cs            ─ shared candidate-action enumeration
├─ AiDispatcher.cs        ─ routes by Player.Kind
├─ AiSimulator.cs         ─ Clone + apply for 1-ply lookahead
├─ AiStateScorer.cs       ─ scoring function for HeuristicAi
├─ RandomAi.cs            ─ uniform-random chooser
├─ HeuristicAi.cs         ─ 1-ply best-score chooser
├─ AiLog.cs               ─ gated stdout logging
│
├─ TerritoryFinder.cs     ─ pure rules
├─ CapitalPlacer.cs       ─
├─ CapitalReconciler.cs   ─
├─ DefenseRules.cs        ─
├─ MovementRules.cs       ─
├─ PurchaseRules.cs       ─
├─ TreeRules.cs           ─
├─ UpkeepRules.cs         ─
├─ WinConditionRules.cs   ─
│
├─ HexCoord.cs            ─ model primitives
├─ HexGrid.cs             ─
├─ HexTile.cs             ─
├─ HexOccupant.cs         ─
├─ Unit.cs                ─ + UnitLevel + UnitLevelExtensions
├─ Capital.cs             ─
├─ Tower.cs               ─
├─ Tree.cs                ─
├─ Grave.cs               ─
├─ Territory.cs           ─ + TerritoryExtensions
├─ Player.cs              ─ + AiKind
├─ TurnState.cs           ─
├─ Treasury.cs            ─
├─ GameStateSnapshot.cs   ─
└─ UndoStack.cs           ─

tests/
├─ TestHelpers.cs         ─ shared fixtures
├─ MockHexMapView.cs      ─ IHexMapView in-memory impl
├─ MockHudView.cs         ─ IHudView in-memory impl
└─ *Tests.cs              ─ xUnit tests covering controller flows,
                            rules, AI, snapshot/undo, primitives
```

`Main.cs`, `HexMapView.cs`, `HudView.cs`, `MainMenuScene.cs`,
`GodotAiPacer.cs`, and `HeadlessViews.cs` are NOT compiled into the
test assembly — they derive from Godot nodes or depend on
`SceneTree`. The test csproj explicitly lists each production source
file it includes, so when you add a new testable source file you must
add a matching `<Compile Include>` entry or tests won't see it.

## Tests

Run with `dotnet test`. The suite covers every static rule class,
the `GameController` click + turn state machine (with mock views and
the synchronous pacer), `Treasury`, `UndoStack`, `GameStateSnapshot`,
both AI flavors, and all the primitive structs. The view layer is
deliberately uncovered — it depends on Godot's `Node` lifecycle, so
pin behavior in the controller and rules instead.

For coverage:

```
dotnet test --collect:"XPlat Code Coverage" --settings tests/coverlet.runsettings
```

## Rebuild-before-launch rule

Godot does not always rebuild the C# assembly when launching the
game. After editing any `.cs` file, run:

```
dotnet build FourExHex.csproj
```

before relaunching or you'll be running stale code.
