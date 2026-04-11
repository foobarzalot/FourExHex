# FourExHex Architecture

Snapshot of the architecture after the MVC refactor. Start here if you're
new to the codebase.

## Layered view

```
┌──────────────────────────────────────────────────────────────────────────┐
│                            SCENE ROOT (Godot)                            │
│                                                                          │
│   Main (Node2D)  ~80 lines                                               │
│   └─ _Ready:                                                             │
│      1. Build players                                                    │
│      2. Build grid (flood-fill + CapitalReconciler)                      │
│      3. Construct GameState + SessionState                               │
│      4. new HexMapView + Init(state) + AddChild + position               │
│      5. new HudView + AddChild                                           │
│      6. new GameController(state, session, map, hud)                     │
│      7. controller.StartGame()                                           │
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
│   ├─ subscribes in ctor:                                                 │
│   │    map.TileClicked → OnTileClicked                                   │
│   │    hud.BuyPeasantClicked → OnBuyPeasantPressed                       │
│   │    hud.UndoLastClicked / UndoTurnClicked → OnUndo*Pressed            │
│   │    hud.RedoLastClicked / RedoAllClicked → OnRedo*Pressed             │
│   │    hud.EndTurnClicked → OnEndTurnPressed                             │
│   │                                                                      │
│   ├─ click policy state machine:                                         │
│   │    OnTileClicked → pending-mode branch → SetSelection branch         │
│   │                                                                      │
│   ├─ action handlers:                                                    │
│   │    ExecuteBuyAndPlace → PurchaseRules + MovementRules.PlaceNew       │
│   │                        → if capture: HandleCapture                   │
│   │    ExecuteMove         → MovementRules.Move                          │
│   │                        → if capture: HandleCapture                   │
│   │                                                                      │
│   ├─ capture reconciliation (inlined — not a view method):               │
│   │    HandleCapture → TerritoryFinder.FindAll                           │
│   │                  → CapitalReconciler.Reconcile                       │
│   │                  → Treasury.ReconcileAfterCapture                    │
│   │                  → _map.RebuildAfterTerritoryChange                  │
│   │                                                                      │
│   ├─ undo/redo:                                                          │
│   │    Every action: _session.Undo.PushBefore(CaptureCurrentSnapshot())  │
│   │    OnUndoLastPressed / UndoTurnPressed → ApplySnapshot               │
│   │    OnRedoLastPressed / RedoAllPressed  → ApplySnapshot               │
│   │    ApplySnapshot → snapshot.ApplyTo(grid, treasury)                  │
│   │                  → _state.Territories = returned                     │
│   │                  → _map.RebuildAfterTerritoryChange                  │
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
│                           │  │   ├─ RebuildAfterTerritoryChange()         │
│   SessionState            │  │   ├─ RefreshOccupantVisuals(color, tr.)    │
│   ├─ SelectedTerritory    │  │   └─ layers: borders / capitals / units /  │
│   ├─ Mode (enum)          │  │             targets / highlight            │
│   ├─ MoveSource           │  │                                            │
│   └─ Undo (UndoStack)     │  │   HudView : CanvasLayer, IHudView          │
│                           │  │   ├─ events: BuyPeasant / UndoLast /       │
│                           │  │     UndoTurn / RedoLast / RedoAll /        │
│                           │  │     EndTurn                                │
│                           │  │   └─ Refresh(state, session, hasAct.)      │
└─────────────┬─────────────┘  └────────────────────────────────────────────┘
              │
              ▼
┌──────────────────────────────────────────────────────────────────────────┐
│                         PURE RULES (static)                              │
│                                                                          │
│   TerritoryFinder.FindAll(grid) ─── pure flood-fill, no capitals         │
│   CapitalPlacer.Choose(coords, grid) ─ empty > unit, lex-min             │
│   CapitalReconciler.Reconcile(raw, old, grid) ─ split/merge + stomping   │
│   PurchaseRules.CanAffordPeasant / IsValidPeasantTarget / BuyPeasant     │
│   MovementRules.ValidTargets / Move / PlaceNew                           │
│   DefenseRules.Defense(coord, grid, territory)                           │
└──────────────────────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────────────────────┐
│                         MODEL PRIMITIVES                                 │
│                                                                          │
│   HexCoord (struct, IEquatable, IComparable)                             │
│   HexGrid — Dictionary<HexCoord, HexTile>                                │
│   HexTile — Coord, Color, Visual, Occupant (Unit | Capital)              │
│   HexOccupant (abstract)                                                 │
│     ├─ Unit — Owner, HasMovedThisTurn                                    │
│     └─ Capital — marker                                                  │
│   Territory — Owner, Coords, Capital (immutable)                         │
│   TerritoryExtensions — BuildTileIndex                                   │
│   Player — Name, Color                                                   │
│   TurnState — Players[], CurrentPlayerIndex, TurnNumber                  │
│   Treasury — Dictionary<HexCoord, int>                                   │
│   GameStateSnapshot — deep-copy (tiles + gold + territories)             │
│   UndoStack — two-sided history                                          │
└──────────────────────────────────────────────────────────────────────────┘
```

## Key contracts

**`IHexMapView`** — everything the controller asks the map to do:

```csharp
event Action<HexTile?>? TileClicked;
Territory? TerritoryAt(HexCoord coord);
void ShowMoveTargets(IEnumerable<HexCoord> coords);
void ShowHighlight(Territory? selected);
void RebuildAfterTerritoryChange();
void RefreshOccupantVisuals(Color? currentPlayerColor, Treasury treasury);
```

**`IHudView`** — everything the controller asks the HUD to do:

```csharp
event Action? BuyPeasantClicked;
event Action? UndoLastClicked;
event Action? UndoTurnClicked;
event Action? RedoLastClicked;
event Action? RedoAllClicked;
event Action? EndTurnClicked;
void Refresh(GameState state, SessionState session, bool hasActionableRemaining);
```

## Invariants (enforced by design)

- **Views never mutate the model.** Methods that *look* like mutations
  (`ShowHighlight`, `RebuildAfterTerritoryChange`) only touch view state —
  they don't reach into `GameState`.
- **Controller never touches Godot Nodes directly.** It talks to views
  through the interfaces above, which means the full state machine is
  unit-testable with mocks (see `tests/GameControllerTests.cs`).
- **Every state change funnels through `RefreshViews()`** at the end of the
  handler. One path, no drift.
- **`SessionState` never ends up in a snapshot.** Only `GameState` does, so
  undo/redo can't resurrect UI artifacts.
- **`HexTile.Color` is the single source of truth for tile ownership.** Its
  setter pushes the new color into the attached `Polygon2D`, so the logical
  color and rendered fill can't drift.

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
        └─ _map.ShowMoveTargets(CaptureTargetsOnly(territory))
```

### Click → capture

```
HexMapView → TileClicked(enemy tile)
GameController.OnTileClicked
  ├─ session.Mode == MovingUnit
  ├─ IsValidMoveOrPlacementTarget(coord) == true
  └─ ExecuteMove(source, destination)
        ├─ session.Undo.PushBefore(CaptureCurrentSnapshot())
        ├─ MovementRules.Move → dst.Color = attacker; dst.Occupant = unit
        │                      → unit.HasMovedThisTurn = true
        ├─ HandleCapture()
        │     ├─ raw = TerritoryFinder.FindAll(state.Grid)
        │     ├─ state.Territories = CapitalReconciler.Reconcile(raw, old, grid)
        │     ├─ state.Treasury.ReconcileAfterCapture(old, new)
        │     └─ _map.RebuildAfterTerritoryChange()
        └─ FinishPendingAction(clearSelection: true)
              ├─ session.ClearPendingAction()
              ├─ _map.ShowMoveTargets([])
              ├─ SetSelection(null)
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

### End turn

```
HudView (End Turn button) → EndTurnClicked
GameController.OnEndTurnPressed
  ├─ session.Undo.Clear()                  // commit: no going back
  ├─ state.Turns.EndTurn()                 // advance / wrap
  ├─ ResetMovementFor(currentPlayer, grid) // reset HasMovedThisTurn
  ├─ state.Treasury.CollectIncomeFor(currentPlayer, territories)
  ├─ CancelPendingAction()
  ├─ SetSelection(null)
  └─ RefreshViews()
```

## File layout

```
scripts/
├─ Main.cs                ─ scene root, ~80 lines of wiring
├─ GameController.cs      ─ pure C# orchestration, ~300 lines
│
├─ GameState.cs           ─ model bundle: Grid, Territories, Players, Turns, Treasury
├─ SessionState.cs        ─ UI state: Selected, Mode, MoveSource, Undo
│
├─ IHexMapView.cs         ─ map view contract
├─ IHudView.cs            ─ HUD view contract
├─ HexMapView.cs          ─ concrete map: rendering + input
├─ HudView.cs             ─ concrete HUD: labels + buttons
│
├─ TerritoryFinder.cs     ─ pure rules
├─ CapitalPlacer.cs       ─
├─ CapitalReconciler.cs   ─
├─ DefenseRules.cs        ─
├─ MovementRules.cs       ─
├─ PurchaseRules.cs       ─
│
├─ HexCoord.cs            ─ model primitives
├─ HexGrid.cs             ─
├─ HexTile.cs             ─
├─ HexOccupant.cs         ─
├─ Unit.cs                ─
├─ Capital.cs             ─
├─ Territory.cs           ─ (+ TerritoryExtensions)
├─ Player.cs              ─
├─ TurnState.cs           ─
├─ Treasury.cs            ─
├─ GameStateSnapshot.cs   ─
└─ UndoStack.cs           ─

tests/
├─ TestHelpers.cs         ─ shared fixtures
├─ MockHexMapView.cs      ─ IHexMapView in-memory impl
├─ MockHudView.cs         ─ IHudView in-memory impl
├─ GameControllerTests.cs ─ 23 tests on the click state machine
├─ coverlet.runsettings   ─ coverage config
└─ *Tests.cs              ─ 14 test files, 187 tests total
```

## Tests + coverage

**187 xUnit tests**, **99.7% line coverage / 95.1% branch coverage** on the
production code that lives in the test assembly (everything except the
Godot-Node classes: `Main`, `HexMapView`, `HudView`, and the trivial
occupant markers/interfaces).

Run with coverage:

```
dotnet test --collect:"XPlat Code Coverage" --settings tests/coverlet.runsettings
```

Every core class (`GameController`, `CapitalReconciler`, `TerritoryFinder`,
`MovementRules`, `PurchaseRules`, `DefenseRules`, `Treasury`, `UndoStack`,
`GameStateSnapshot`, etc.) is at 100% line coverage. The remaining
uncovered lines are four deliberate non-test cases:

- `HexCoord.ToString()` — diagnostic only
- `GameState.Players` getter — not read by any test
- `MovementRules` defensive `TryGetValue` branch — unreachable given the
  flood-fill invariant
- `GameStateSnapshot.CloneOccupant` default-throw arm — guards against
  future programmer error when a new `HexOccupant` subtype is added

## Rebuild-before-launch rule

Godot does not always rebuild the C# assembly when launching the game.
After editing any `.cs` file, run:

```
dotnet build FourExHex.csproj
```

before relaunching or you'll be running stale code.
