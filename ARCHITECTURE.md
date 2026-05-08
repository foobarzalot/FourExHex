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
│   Main (Node2D)  — play scene root (res://scenes/main.tscn)              │
│   └─ _Ready:                                                             │
│      1. Read GameSettings (player kinds + optional MasterSeed set by     │
│         the main menu; forced to all-Heuristic when FOUREXHEX_6AI set).  │
│      2. Consume LoadRequest.Pending (set by the menu's Load flow);       │
│         clear it so a subsequent menu→game transition starts fresh.      │
│      3. Pick the master seed: load wins, then GameSettings.MasterSeed,   │
│         then Random.Shared.Next(). One seed drives both map gen and      │
│         the controller's per-turn RNG.                                   │
│      4. Build the model. Three branches:                                 │
│           • In-progress save (TurnNumber > 0): state, players, max-turn │
│             cap, OriginMapName all come from the save.                  │
│           • Starting map (TurnNumber == 0 on disk): terrain (grid,      │
│             water, territories, pre-placed trees/towers/capitals)       │
│             comes from the saved map; players from GameSettings; turn   │
│             starts at 1, treasury empty. _originMapName = slot name.    │
│           • Procedural: BuildPlayers + MapGenerator.BuildInitialGrid    │
│             (CA carve → land/water + ~5% trees) → TerritoryFinder       │
│             → CapitalReconciler → new GameState (incl. WaterCoords).    │
│             _originMapName = null.                                      │
│         Then a fresh SessionState.                                       │
│      5. Pick views: real HexMapView/HudView, or HeadlessHexMapView/      │
│         HeadlessHudView when in diagnostic mode                          │
│      6. Pick pacer: GodotAiPacer (visible delays) or                     │
│         SynchronousAiPacer (diagnostic — runs inline)                    │
│      7. new GameController(state, session, map, hud,                     │
│           seed: <chosen master seed>,                                    │
│           aiChooser: AiDispatcher.ChooseForCurrentPlayer,                │
│           aiPacer:  pacer,                                               │
│           maxTurnNumber: load ? saved : (diagnostic ? 500 : int.MaxVal)) │
│      8. Wire save/load:                                                  │
│           • new SaveStore + (non-diagnostic) build the Save dialog.     │
│           • Subscribe controller.HumanTurnStarted → autosave write,    │
│             passing _originMapName so resumed games keep their map      │
│             identity.                                                   │
│           • Subscribe HUD SaveGameClicked → open the dialog.           │
│      9. controller.Resume() (in-progress load) or controller.StartGame()│
│         (fresh / starting map). Then hud.SetMapLabel("Map: <name>") for │
│         starting-map games or "Seed: <n>" for procedural.               │
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
│   ├─ injected: master seed, aiChooser delegate, IAiPacer, maxTurnNumber  │
│   ├─ exposes: MasterSeed, StartGame(), Resume(), AbandonGame()           │
│   ├─ events: GameEnded (fires once on natural game-over or turn cap),    │
│   │          HumanTurnStarted (start-of-each human turn — autosave seam) │
│   │                                                                      │
│   ├─ subscribes in ctor:                                                 │
│   │    map.TileClicked              → OnTileClicked                      │
│   │    map.TileLongClicked          → OnTileLongClicked (rally)          │
│   │    hud.BuyPeasantClicked        → OnBuyPressed (cycles unit level)   │
│   │    hud.BuildTowerClicked        → OnBuildTowerPressed                │
│   │    hud.UndoLastClicked          → OnUndoLastPressed                  │
│   │    hud.UndoTurnClicked          → OnUndoTurnPressed                  │
│   │    hud.RedoLastClicked          → OnRedoLastPressed                  │
│   │    hud.RedoAllClicked           → OnRedoAllPressed                   │
│   │    hud.EndTurnClicked           → OnEndTurnPressed                   │
│   │    hud.NextTerritoryClicked     → OnNextTerritoryPressed             │
│   │    hud.PreviousTerritoryClicked → OnPreviousTerritoryPressed         │
│   │    hud.NextUnitClicked          → OnNextUnitPressed                  │
│   │    hud.PreviousUnitClicked      → OnPreviousUnitPressed              │
│   │    hud.CancelActionPressed      → OnCancelActionPressed              │
│   │    hud.DefeatContinueClicked    → OnDefeatContinuePressed            │
│   │    hud.ClaimVictoryWinNowClicked    → OnClaimVictoryWinNowPressed    │
│   │    hud.ClaimVictoryContinueClicked  → OnClaimVictoryContinuePressed  │
│   │   (NewGameClicked / MainMenuClicked / SaveGameClicked are handled    │
│   │    in Main, not here)                                                │
│   │                                                                      │
│   ├─ click policy state machine:                                         │
│   │    OnTileClicked     → pending-mode branch (buy/build/move)          │
│   │                      → SetSelection branch                           │
│   │    OnTileLongClicked → rally: free-reposition every unmoved unit     │
│   │                        in the territory toward the long-pressed     │
│   │                        target (single undo step, fires PlayRally    │
│   │                        once if any unit moved)                       │
│   │                                                                      │
│   ├─ action handlers:                                                    │
│   │    ExecuteBuyAndPlace → debit gold + MovementRules.PlaceNew          │
│   │                       → if capture: HandleCapture                    │
│   │                       → DispatchActionSound (combine/destroy/place)  │
│   │    ExecuteMove        → MovementRules.Move                           │
│   │                       → if capture: HandleCapture                    │
│   │                       → DispatchActionSound                          │
│   │    ExecuteBuildTower  → debit gold + drop Tower + PlayTowerPlaced    │
│   │                                                                      │
│   ├─ AI loop (paced via IAiPacer):                                       │
│   │    RunAiTurnsUntilHumanOrDone → preview → execute beats              │
│   │    ExecuteAiMove / ExecuteAiBuyUnit / ExecuteAiBuildTower —          │
│   │      validate then mutate (illegal AI action throws)                 │
│   │    Pauses when SessionState.PendingDefeatScreen is set; resumes      │
│   │      from OnDefeatContinuePressed                                    │
│   │                                                                      │
│   ├─ capture reconciliation:                                             │
│   │    HandleCapture → TerritoryFinder.FindAll                           │
│   │                  → CapitalReconciler.Reconcile                       │
│   │                  → Treasury.ReconcileAfterCapture                    │
│   │                  → detect freshly-eliminated colors (had a capital   │
│   │                    before, none after) → PlayPlayerDefeated;         │
│   │                    set PendingDefeatScreen for human eliminations    │
│   │                  → _map.RebuildAfterTerritoryChange                  │
│   │                  → WinConditionRules.WinnerByDomination (mid-turn)   │
│   │                                                                      │
│   ├─ undo/redo:                                                          │
│   │    Each human handler wrapped in TrackHandler — pushes UndoEntry     │
│   │    (game + session snapshot) iff state actually changed (de-dup).    │
│   │    AI actions are NOT undoable (undo cleared at end-of-turn)         │
│   │    OnUndoLast / OnUndoTurn / OnRedoLast / OnRedoAll → ApplySnapshot  │
│   │                                                                      │
│   ├─ turn rotation:                                                      │
│   │    OnEndTurnPressed → undo.Clear                                     │
│   │                     → EndOfTurnProcessing (win check only)           │
│   │                     → AdvanceToNextActivePlayer (skip players with   │
│   │                                                  no capital)         │
│   │                     → StartPlayerTurn (reseed RNG → growth → reset → │
│   │                                        income → upkeep)              │
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
│   ├─ Territories          │  │   ├─ ReloadState(state, anim) — used by    │
│   ├─ Players              │  │   │    the editor to swap terrain in place │
│   ├─ Turns                │  │   ├─ event TileClicked(HexTile?)           │
│   ├─ Treasury             │  │   ├─ event TileLongClicked(HexTile?)       │
│   └─ WaterCoords          │  │   ├─ event CoordClicked(HexCoord) — every  │
│      (off-map blockers,   │  │   │    non-drag click; editor consumes it  │
│       renderer-only)      │  │   ├─ event CoordHovered(HexCoord?) — mouse │
│                           │  │   │    motion; null off-grid/HUD; editor-  │
│                           │  │   │    only (drives HexHoverTooltip)        │
│                           │  │   ├─ event PaintCellEntered(HexCoord) +    │
│                           │  │   │    PaintStrokeEnded — drag-paint       │
│                           │  │   │    channel; editor-only                 │
│                           │  │   ├─ DragMode (Pan | Paint) — Pan = today's│
│                           │  │   │    click+drag-pan; Paint = press fires │
│                           │  │   │    PaintCellEntered, motion fires per  │
│                           │  │   │    new cell, release fires Stroke-     │
│                           │  │   │    Ended; suppresses pan + click events│
│                           │  │   ├─ TerritoryAt(coord)                    │
│                           │  │   ├─ ShowHighlight(territory)              │
│   SessionState            │  │   ├─ ShowMoveTargets(coords, level)        │
│   ├─ Winner (Color?)      │  │   ├─ ShowTowerTargets(coords)              │
│   ├─ PendingDefeatScreen  │  │   ├─ ShowTowerCoverage(coords)             │
│   │   (Color? — drives    │  │   ├─ ShowMoveSource(coord?)                │
│   │   the defeat overlay) │  │   ├─ CenterOnTerritory(territory)          │
│   ├─ PendingClaimVictory  │  │   ├─ RebuildAfterTerritoryChange()         │
│   │   (Color? — drives    │  │   ├─ RefreshOccupantVisuals(color, tr.)    │
│   │   the claim-victory   │  │   ├─ PlayDestructionEffect(coord, occ.)    │
│   │   overlay; human-only)│  │   ├─ Play{UnitPlaced, TowerPlaced,         │
│   ├─ ClaimVictoryPrompted │  │   │    UnitCombined, UnitDestroyed,        │
│   │   Colors (HashSet —   │  │   │    TowerDestroyed, TreeCleared,        │
│   │   one-prompt-per-     │  │   │    CapitalDestroyed, Bankruptcy,       │
│   │   game gate; persists │  │   │    GameWon, Rally, PlayerDefeated}     │
│   │   across save/load)   │  │   │    — audio sinks routed to AudioBus    │
│   ├─ SelectedTerritory    │  │   └─ layers: borders / capitals / units /  │
│   ├─ Mode (enum)          │  │             towers / trees / graves /     │
│   ├─ MoveSource           │  │             targets / highlight            │
│   └─ Undo (UndoStack of   │  │                                            │
│      UndoEntry =          │  │                                            │
│      GameStateSnapshot +  │  │                                            │
│      SessionStateSnapshot)│  │                                            │
│                           │  │                                            │
│                           │  │   HudView : CanvasLayer, IHudView          │
│                           │  │   ├─ events: BuyPeasant / BuildTower /     │
│                           │  │     UndoLast / UndoTurn / RedoLast /       │
│                           │  │     RedoAll / EndTurn / NewGame /          │
│                           │  │     MainMenu / NextTerritory /             │
│                           │  │     PreviousTerritory / NextUnit /         │
│                           │  │     PreviousUnit / SaveGame / CancelAction │
│                           │  │     / DefeatContinue /                     │
│                           │  │     ClaimVictoryWinNow /                   │
│                           │  │     ClaimVictoryContinue                   │
│                           │  │   ├─ Refresh(state, session, hasAct.)      │
│                           │  │   │    (overlay priority: Winner >         │
│                           │  │   │     PendingDefeatScreen >              │
│                           │  │   │     PendingClaimVictory)               │
│                           │  │   ├─ SetMapLabel(text)  // "Map: foo" or   │
│                           │  │   │                       "Seed: 1234"     │
│                           │  │   └─ ShowTutorialMessage(text) /           │
│                           │  │      HideTutorialMessage() — bottom-       │
│                           │  │      anchored click-through info popup    │
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
│                    .MeetsClaimVictoryThreshold (>50%, claim-victory)     │
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
│   AiKind — Human, Random, Heuristic, Tutorial (tutorial-only)            │
│   TurnState — Players[], CurrentPlayerIndex, TurnNumber                  │
│   Treasury — Dictionary<HexCoord, int>; CollectIncomeFor;                │
│              ReconcileAfterCapture (forfeits enemy gold on capture)      │
│   GameStateSnapshot — deep-copy (tiles + gold + territories)             │
│   SessionStateSnapshot — selection anchor + Mode + MoveSource            │
│   UndoEntry — pair of (GameStateSnapshot, SessionStateSnapshot)          │
│   UndoStack<T> — two-sided history of T (UndoEntry for play, also reused │
│                  by the editor with EditorSnapshot)                      │
│   TerritoryLookup — FindOwnedContaining / FindByCapital helpers          │
│   MapGenerator — CA-driven land/water carve + tree scatter, seeded       │
│   GameSettings — global PlayerConfig (name, color hex) + PlayerKinds     │
│                  + optional MasterSeed; written by MainMenuScene,        │
│                  read by Main                                            │
│   LoadRequest — static one-shot handoff from menu's Load button to       │
│                 Main (consumed and cleared in _Ready)                    │
│   SaveStore — user://saves/ slot CRUD + user://maps/ for starting        │
│                maps + res://tutorials/ for bundled (read-only) maps:     │
│                WriteAutosave / WriteSlot / ListSlots / LoadSlot,         │
│                WriteMapSlot / ListMaps / LoadMap / LoadBundledMap;       │
│                reserved "autosave" slot                                  │
│   SaveSerializer — JSON (de)serializer for the full game state +         │
│                    starting maps (Kind omitted; OriginMapName carried)   │
│   LoadedSave — bundle of (state, players, master seed, max-turn cap,     │
│                slot name, optional OriginMapName)                        │
│   SaveSlotInfo — slot listing metadata (name, time, turn, isAutosave)    │
└──────────────────────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────────────────────┐
│                         AUDIO (autoload)                                 │
│                                                                          │
│   AudioBus — autoload-registered Node singleton (project.godot           │
│   [autoload] entry "AudioBus"). Owns AudioStreamPlayer instances for     │
│   every shared SFX — click, place/move (units, towers, combine,          │
│   destroy variants), tree/grave clear, capital fall, bankruptcy bell,    │
│   game-won fanfare, rally whoosh, player-defeated gong. Survives scene  │
│   changes so a button press that triggers ChangeSceneToFile still hears │
│   its click on the way out. The static AttachClick(BaseButton) /        │
│   AttachClick(HexPaletteButton) helpers wire any button's Pressed       │
│   signal to the shared click player.                                    │
│                                                                          │
│   HexMapView's Play* methods (PlayUnitPlaced, PlayBankruptcy, …)         │
│   forward to AudioBus.Instance. The interface lets controllers fire     │
│   audio without knowing about the autoload, and lets HeadlessHexMapView │
│   (test/diagnostic) stub them out.                                      │
└──────────────────────────────────────────────────────────────────────────┘
```

## Key contracts

**`IHexMapView`** — everything the controller asks the map to do:

```csharp
event Action<HexTile?>? TileClicked;
event Action<HexTile?>? TileLongClicked;     // rally
Territory? TerritoryAt(HexCoord coord);
void ShowMoveTargets(IEnumerable<HexCoord> coords, UnitLevel level);
void ShowTowerTargets(IEnumerable<HexCoord> coords);
void ShowTowerCoverage(IEnumerable<HexCoord> coords);
void ShowMoveSource(HexCoord? coord);
void ShowHighlight(Territory? selected);
void CenterOnTerritory(Territory territory);
void RebuildAfterTerritoryChange();
void RefreshOccupantVisuals(Color? currentPlayerColor, Treasury treasury);
void PlayDestructionEffect(HexCoord coord, HexOccupant destroyed);

// Audio sinks — forwarded to AudioBus.
void PlayUnitPlaced(HexCoord coord);
void PlayTowerPlaced(HexCoord coord);
void PlayUnitCombined(HexCoord coord);
void PlayUnitDestroyed(HexCoord coord);
void PlayTowerDestroyed(HexCoord coord);
void PlayTreeCleared(HexCoord coord);
void PlayCapitalDestroyed(HexCoord coord);
void PlayBankruptcy();
void PlayGameWon();
void PlayRally();
void PlayPlayerDefeated();
```

`ShowMoveTargets` takes the unit level so the preview can render at
the correct visual size (peasant=1 ring, spearman=2, knight=3,
baron=3+dot). Audio is fired from the controller right after the
mutation that produced it; `DispatchActionSound` picks one cue per
move/buy resolution (combine > destruction-by-type > generic place).

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
event Action? PreviousTerritoryClicked;// Shift+Tab hotkey equivalent
event Action? NextUnitClicked;         // N hotkey: cycle units in selection
event Action? PreviousUnitClicked;     // Shift+N hotkey
event Action? CancelActionPressed;     // Escape hotkey equivalent
event Action? SaveGameClicked;         // handled in Main (opens save dialog)
event Action? DefeatContinueClicked;   // dismiss defeat overlay; resume AI
event Action? ClaimVictoryWinNowClicked;   // declare win now from prompt
event Action? ClaimVictoryContinueClicked; // dismiss prompt, proceed End Turn

void Refresh(GameState state, SessionState session, bool hasActionableRemaining);
void SetMapLabel(string text);         // one-time after setup; "Map: foo"
                                       // for starting-map games, "Seed: N"
                                       // for procedural
void ShowTutorialMessage(string text); // bottom-anchored info popup;
                                       // click-through (MouseFilter=Ignore)
void HideTutorialMessage();            // dismiss it — Main drives this
                                       // off the first user input
```

The defeat overlay is part of the HUD: `Refresh` reads
`session.PendingDefeatScreen` and shows/hides a click-blocking panel
naming the eliminated player. Continue raises
`DefeatContinueClicked`; the overlay's "Main Menu" button reuses
`MainMenuClicked`.

The claim-victory overlay is the third HUD overlay: `Refresh` shows
it iff `session.PendingClaimVictory.HasValue` and neither `Winner`
nor `PendingDefeatScreen` is set (Winner > Defeat > ClaimVictory).
**Win Now** raises `ClaimVictoryWinNowClicked`; **Continue Playing**
raises `ClaimVictoryContinueClicked`. See the "Claim victory prompt"
subsection under Win conditions.

The tutorial popup is a separate non-interactive panel managed via
`ShowTutorialMessage` / `HideTutorialMessage` (no `Refresh`-driven
state). `Main` decides when to show it (currently: once at game
start when any player has `AiKind.Tutorial`) and dismisses it on
the first mouse-button-down or non-echo key press via its `_Input`
override. The panel itself ignores mouse input so that first click
still reaches the map / HUD as normal.

**`IAiPacer`** — schedules deferred continuations for the AI step
machine. `GodotAiPacer` schedules via the `SceneTree`; the default
`SynchronousAiPacer` runs the callback inline (used by tests and
diagnostic mode). `Cancel` drops any pending callbacks and ignores
future deliveries from already-scheduled timers — `Main` calls it
via `GameController.AbandonGame()` before swapping back to the menu
so an in-flight `StepAiExecute` can't fire against disposed
`Polygon2D` nodes after the scene swap.

```csharp
void Schedule(Action callback, int delayMs);
void Cancel();
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
- **Snapshots capture `GameState` plus the player-intent slice of
  `SessionState`** (`SelectedTerritory` anchor, `Mode`, `MoveSource`)
  via `UndoEntry` (a `(GameStateSnapshot, SessionStateSnapshot)` pair).
  `Winner`, `PendingDefeatScreen`, and the `Undo` stack itself stay
  out. Top-level human event handlers are wrapped in `TrackHandler`,
  which captures pre-state, runs the body, and pushes one `UndoEntry`
  iff state actually changed — automatic de-dup of no-op clicks.
  Exceptions inside a handler propagate without pushing.
- **`HexTile.Color` is the single source of truth for tile
  ownership.** Its setter pushes the new color into the attached
  `Polygon2D`, so the logical color and rendered fill can't drift.
- **Undo is turn-scoped.** `OnEndTurnPressed` clears the stack, so
  ending a turn commits everything.
- **AI actions are not undoable** (undo gets cleared at end-of-turn
  anyway), and the AI execute methods validate preconditions before
  mutating — an illegal AI action throws and halts the game in an
  obvious error state rather than corrupting state silently.
- **Players with no capital-bearing territory are skipped.**
  `AdvanceToNextActivePlayer` calls `TurnState.EndTurn` until it lands
  on a player whose territory list contains a capital — eliminated
  players never get a phantom turn.

## Turn structure

A turn is sandwiched between two phases:

### Start-of-turn — `StartPlayerTurn()`

Runs in this fixed order for the now-current player:

1. **Reseed RNG** — `ReseedRngForCurrentTurn` derives `_rng` from
   `(masterSeed, turnNumber, currentPlayerIndex)` so all subsequent
   RNG draws this turn are reproducible from the seed alone.
2. **Tree growth** — `TreeRules.RunStartOfTurnGrowth` (skipped during
   round 1, i.e. while `TurnNumber == 1`). Graves on the current
   player's tiles become trees; empty cells of their color with ≥2
   neighboring trees become trees.
3. **Reset movement** — `HasMovedThisTurn` cleared on the current
   player's units.
4. **Collect income** — `Treasury.CollectIncomeFor` (skipped during
   round 1; the seed from `SeedStartingGold` is the round-1 bankroll).
   Tree and grave tiles don't pay; everything else (empty, units,
   capitals, towers) pays 1 gold.
5. **Apply upkeep** — `UpkeepRules.ApplyUpkeepFor`. Per-unit costs:
   Peasant 2, Spearman 6, Knight 18, Baron 54. A territory that
   can't pay total upkeep goes bankrupt: every unit in it becomes a
   `Grave`, remaining gold stays. `PlayBankruptcy` fires once if any
   territory of this player went bankrupt (player-scoped, not
   tile-scoped).
6. **Fire `HumanTurnStarted`** if the now-current player is human and
   the game isn't over. Save/load wires the autosave path here.

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

`DeclareWinner` is the centralized setter for `SessionState.Winner`;
it fires `PlayGameWon` iff the winner is human.

### Claim victory prompt

When a **human** presses End Turn while owning strictly more than
half of all land tiles (`WinConditionRules.MeetsClaimVictoryThreshold`,
counting `state.Grid.Tiles` only — water is excluded),
`OnEndTurnPressed` short-circuits before `EndTurnNow()` runs. It sets
`SessionState.PendingClaimVictory = currentColor` and refreshes the
view; the HUD shows a centered overlay with **Win Now** and **Continue
Playing** buttons. The pending End Turn is held until the user picks:

- **Win Now** (`OnClaimVictoryWinNowPressed`) records the color in
  `SessionState.ClaimVictoryPromptedColors`, calls `DeclareWinner`,
  clears undo, and fires `GameEnded`.
- **Continue Playing** (`OnClaimVictoryContinuePressed`) records the
  color and runs `EndTurnNow()` — exactly the original End Turn flow.

The prompt fires at most **once per human per game**: the color is
added to `ClaimVictoryPromptedColors` only on dismissal (not on show),
so a save+reload while the overlay is up still re-presents the prompt.
The prompted-colors set is persisted via `SaveSerializer` (optional
`ClaimVictoryPromptedColorHexes` field — null/missing in older saves
loads as empty) so reload cannot reset the once-per-game invariant.
AI players never trigger the prompt.

### Player elimination

`HandleCapture` diffs the set of colors with capitals before vs after
the reconcile. A color that had at least one capital before and none
after has been eliminated by this capture: `PlayPlayerDefeated`
fires; if the eliminated color is human,
`SessionState.PendingDefeatScreen` is set so the HUD shows a defeat
overlay. The AI loop pauses at the next `StepAiExecute` while the
overlay is up so the human can read the result before play resumes.
`OnDefeatContinuePressed` clears the flag and re-arms the pacer.

### Rotation

`AdvanceToNextActivePlayer()` calls `TurnState.EndTurn()` (which
increments `TurnNumber` on wrap) then loops while
`WinConditionRules.IsEliminated(currentPlayer.Color, grid)` is true
— wiped-out players are skipped entirely.

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
        ├─ _map.ShowMoveTargets(ActionConsumingTargets(level, terr.), level)
        └─ _map.ShowMoveSource(tile.Coord)
```

### Click → capture

```
HexMapView → TileClicked(enemy tile)
GameController.OnTileClicked  ── wrapped in TrackHandler:
  pre = CaptureCurrentSnapshot()       // (game + session) BEFORE the body
  └─ OnTileClickedBody(tile)
        ├─ session.Mode == MovingUnit
        ├─ IsValidTarget(level, coord) == true
        └─ ExecuteMove(source, destination)
              ├─ _handlerMutatedGame = true
              ├─ wasCombine = WasFriendlyUnitAt(dst, owner)
              ├─ MovementRules.Move → dst.Color = attacker; dst.Occupant = unit
              │                      → unit.HasMovedThisTurn = true
              ├─ if WasCapture:
              │     ├─ HandleCapture(...)
              │     │     ├─ raw = TerritoryFinder.FindAll(state.Grid)
              │     │     ├─ state.Territories = CapitalReconciler.Reconcile(raw, old, grid)
              │     │     ├─ state.Treasury.ReconcileAfterCapture(old, new)
              │     │     │     (enemy gold on captured capital tiles is forfeited)
              │     │     ├─ if a color lost its last capital:
              │     │     │     PlayPlayerDefeated; for human, set PendingDefeatScreen
              │     │     ├─ _map.RebuildAfterTerritoryChange()
              │     │     └─ if WinConditionRules.WinnerByDomination → DeclareWinner, clear undo
              │     └─ RebindSelectionToContaining(destination)
              ├─ if MoveResult.Destroyed != null: _map.PlayDestructionEffect(dst, occ.)
              ├─ DispatchActionSound(dst, result, wasCombine)
              │     (combine > destroyed-by-type > generic place)
              └─ FinishPendingAction()
                    ├─ session.ClearPendingAction()
                    ├─ _map.ShowMoveTargets([], …)
                    ├─ _map.ShowMoveSource(null)
                    └─ RefreshViews()
  // Back inside TrackHandler, after the body runs:
  if !session.IsGameOver && (_handlerMutatedGame || sessionChanged):
      session.Undo.PushBefore(pre)     // single push per handler, auto-deduped
```

### Long-press → rally

```
HexMapView → TileLongClicked(target tile)
GameController.OnTileLongClicked  ── wrapped in TrackHandler:
  └─ OnTileLongClickedBody(tile)
        ├─ ignored if game over, no tile, or any pending mode
        ├─ ignored unless tile color == current player's color
        ├─ collect every unmoved current-color unit in the territory
        ├─ sort closest-to-target first (lex-min tiebreak) so far units
        │   can't leapfrog near ones
        └─ for each src: greedy free-reposition to the strictly closer
            empty in-territory cell (MovementRules.Move on own-empty
            does NOT consume the move action)
        ├─ if any moved: _handlerMutatedGame = true; PlayRally;
        │   re-select the territory
        └─ RefreshViews()
```

### End turn

```
HudView (End Turn button) → EndTurnClicked
GameController.OnEndTurnPressed
  ├─ if session.IsGameOver → return            // game already over, ignore
  ├─ session.Undo.Clear()                      // commit: no going back
  ├─ EndOfTurnProcessing()                     // end-of-turn win check
  │     └─ WinConditionRules.WinnerAtEndOfTurn → DeclareWinner if sole capital-bearer
  ├─ if session.IsGameOver:                    // win check just fired
  │     └─ CheckGameEndConditions()            // fire GameEnded once
  │ else:
  │     ├─ AdvanceToNextActivePlayer()         // skip eliminated players
  │     ├─ StartPlayerTurn()                   // reseed → growth → reset → income → upkeep
  │     │     (growth + income skipped during round 1; fires HumanTurnStarted
  │     │      if the new current player is human)
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
        ├─ state.Territories = snap.Game.ApplyTo(state.Grid, state.Treasury)
        ├─ _map.RebuildAfterTerritoryChange()
        ├─ snap.Session.ApplyTo(session, state.Territories)
        ├─ RestoreOverlaysForCurrentMode()    // re-emits highlight + targets
        └─ RefreshViews()
  └─ CenterIfSelectionChanged(...)            // pan to the restored selection
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
  ├─ if PendingDefeatScreen: pause — return without scheduling next
  │     (resumes from OnDefeatContinuePressed)
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
- **`TutorialAi`** — scripted opponent used only by the tutorial
  scenario. Currently fully passive: `ChooseNextAction` always returns
  null, which the controller's step machine reads as "this player is
  done; advance". Not selectable from the play-config menu.
- **`AiDispatcher.ChooseForCurrentPlayer`** — routes to the per-player
  AI flavor based on `Player.Kind`. Wired into `GameController` as
  the single `aiChooser` delegate.
- **`AiLog`** — `AiLog.Print` is off by default; flip
  `AiLog.Enabled = true` (or use `FOUREXHEX_6AI`) to trace every
  AI decision plus per-turn header lines and capture diffs.

## Save / load

Save/load is built around a deterministic-on-reload contract: a saved
master seed plus the `(turn, player)` tuple uniquely determines the
RNG sequence used during that player's turn, so a save records only
the seed (no RNG-consumption count) and load reproduces identical
sequences.

- **Master seed.** `GameController` takes a `seed:` constructor arg
  and exposes `MasterSeed`. Inside the controller, `_rng` is reseeded
  from `(masterSeed, turnNumber, currentPlayerIndex)` at the top of
  every `StartPlayerTurn` and on every `Resume`
  (`ReseedRngForCurrentTurn`). Map generation
  (`MapGenerator.BuildInitialGrid`) uses the same seed, so the menu's
  "Map Seed" field is reproducible end-to-end.
- **Autosave.** `Main` subscribes `controller.HumanTurnStarted` to a
  handler that writes the `autosave` slot via
  `SaveStore.WriteAutosave`. Fires once at the start of every human
  turn, after start-of-turn bookkeeping (tree growth, income, upkeep)
  so the saved state matches what the player sees. AI turns and
  game-over states are skipped.
- **Named saves.** The HUD's Save button raises `SaveGameClicked`,
  which `Main` (not the controller) handles by opening an
  `AcceptDialog` for a slot name and calling `SaveStore.WriteSlot`.
  The literal `autosave` slot name is reserved.
- **Origin map name.** Saves carry an optional `OriginMapName` field
  identifying the starting map a game descended from (or null for
  procedural games). It rides through autosave so reloads keep the
  bottom-left "Map: foo" label correct.
- **Claim-victory prompted set.** Saves carry an optional
  `ClaimVictoryPromptedColorHexes` field — the colors that have
  already dismissed the End-Turn claim-victory prompt this game.
  Empty/missing in older saves and starting maps. `Main` seeds
  `SessionState.ClaimVictoryPromptedColors` from this on load so the
  once-per-game invariant survives reloads.
- **Load.** The main menu's Load button populates `LoadRequest.Pending`
  with a `LoadedSave` (state + players + master seed + max-turn cap +
  optional OriginMapName + optional claim-victory prompted set) and
  changes scene to `main.tscn`.
  `Main._Ready` consumes and clears the request. On the in-progress
  load path, fresh grid construction is skipped and
  `controller.Resume()` is called instead of `StartGame()`.
- **`Resume()`** reseeds the RNG for the current turn, runs any
  leading AI turns until control reaches a human (or game ends),
  refreshes the views, then fires `HumanTurnStarted` if the resumed
  player is human (so the autosave hook still runs after a load).

`SaveStore` reads/writes `user://saves/` (in-progress games) and
`user://maps/` (starting maps from the editor), and reads from
`res://tutorials/` (bundled maps shipped with the game — currently
just `Tutorial.json`, loaded via `LoadBundledMap`). It exposes
`WriteAutosave`, `WriteSlot`, `WriteMapSlot`, `ListSlots`, `ListMaps`,
`LoadSlot`, `LoadMap`, `LoadBundledMap`, plus `SanitizeSlotName` for
filesystem-safe slot names. `SaveSerializer` is the JSON layer
(format version 2); `Serialize` writes the player roster's `Kind`
field, `SerializeMap` omits it (the editor's saved maps don't bake
a player-kind config — roles are assigned at play time from the
menu). `SaveSlotInfo` is the slot listing record.

## Map editor

`MapEditorScene` (root of `res://scenes/map_editor.tscn`, reached
from the main menu's "Map Editor" button) is a separate scene that
lets the user paint a starting map by hand and save it to
`user://maps/`. It deliberately doesn't reuse `GameController` —
nothing about it is turn- or rules-driven — but it does reuse the
view layer (`HexMapView` + a sibling `MapEditorHudView`) so map
edits look identical to in-game terrain.

- **Draft state.** The scene owns a mutable `HexGrid`, water set,
  and territory list, plus an `UndoStack<EditorSnapshot>` for
  undo/redo. `EditorSnapshot.Capture` deep-copies all three; its
  `ApplyTo` rebuilds the grid from scratch (paints can both add and
  remove tiles, so `GameStateSnapshot`'s in-place tile updates aren't
  enough).
- **Push cycle.** Every paint or generate calls `PushState` which
  rebuilds a fresh `GameState`, hands it to
  `HexMapView.ReloadState` (preserving zoom/pan), and reapplies
  occupant visuals. This is why `HexMapView` exposes both `Init` and
  `ReloadState`.
- **Input model.** Each palette swatch flips
  `HexMapView.DragMode` to one of two channels:
  - **Pan mode** (hand, capital): drag pans the camera; releases
    without drag fire `CoordClicked`. The hand swatch ignores the
    click; the capital swatch handles it via
    `MapEditPaint.PaintCapital`.
  - **Paint mode** (colors, water, tree, tower): drag paints a
    stroke. The view fires `PaintCellEntered` on press for the
    initial cell, again for every new cell the cursor crosses
    while held, and `PaintStrokeEnded` on release. No panning, no
    `CoordClicked` for that gesture. A sub-threshold press-release
    still produces a one-cell stroke, so single-click painting is
    just a degenerate drag.

  The editor wraps a stroke in a single undo entry: the first
  `PaintCellEntered` captures an `EditorSnapshot.Capture`,
  per-cell paints reuse it, and `PaintStrokeEnded` pushes once iff
  any cell mutated state.
- **Hand swatch.** Palette index 0, the default selection on scene
  entry. Pan-mode, no paint. Escape ladders out: a first press
  with any non-hand swatch active reselects the hand (canceling
  the paint mode); a second press with the hand already active
  exits the editor.
- **Toggle stroke locking.** Tree and tower drag-paints have an
  explicit "Add" or "Erase" mode locked at the first cell of the
  stroke. If the first cell already carries the matching occupant
  → Erase (subsequent cells skip everything except matching
  removals); else → Add (subsequent cells skip cells that already
  have the occupant). This prevents a single drag from both
  placing and clearing — a long stroke that wanders over varied
  terrain is consistent end-to-end.
- **Hover tooltip.** `HexMapView.CoordHovered` fires on mouse
  motion with the hex under the cursor (null when off the
  `Cols × Rows` rectangle or over the HUD strip). The editor wires
  it to `HexHoverTooltip`, a floating `CanvasLayer + Label` that
  appears after a ~500ms dwell and hides on motion. The label shows
  the row-major lex index (`row * Cols + col`) plus `(col, row)` —
  the lex index is the single-int handle intended for future
  tutorial scripting that refers to specific cells by number. Both
  the event and the tooltip class are editor-only; the play scene
  doesn't subscribe.
- **Palette.** `MapEditorHudView` builds a palette of
  `HexPaletteButton` swatches: one per player color, plus water,
  tree, capital, and tower toggles. The selected index is read by
  `OnCoordClicked` and dispatched to one of `MapEditPaint`'s pure
  functions (`PaintLand`, `PaintCapital`, `PaintTowerToggle`,
  `PaintTreeToggle`, `PaintWater`). Each helper mutates the grid
  in place, then re-runs `TerritoryFinder` + `CapitalReconciler`
  (except `PaintCapital`, which honors the user's exact pick rather
  than letting the placer second-guess them).
- **Save format.** Editor maps are written with `SaveSerializer.SerializeMap`
  (no `Kind` per player, `TurnNumber == 0`). At play time, `Main`
  detects `TurnNumber == 0` to branch into the "starting map" flow:
  fresh players from `GameSettings`, fresh `TurnState`, empty
  `Treasury`, but the saved grid + territories + pre-placed
  trees/towers/capitals all stick.

## Diagnostic mode (`FOUREXHEX_6AI`)

Setting the env var before launching Godot reconfigures the session
for a fully headless regression run:

- All six player slots forced to `AiKind.Heuristic` (the menu also
  detects the env var and skips itself, so the launch jumps straight
  into `Main`).
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
├─ Main.cs                ─ play scene root; wires model + views + controller
├─ MainMenuScene.cs       ─ landing (Play/Tutorial/Load/Map Editor) +
│                           play-config panels; Load Game modal; Play
│                           Tutorial bypasses config (Red=Human, others
│                           AiKind.Tutorial, loads bundled Tutorial map);
│                           writes GameSettings + LoadRequest
├─ MapEditorScene.cs      ─ editor scene root; owns the draft grid/water/
│                           territories + UndoStack<EditorSnapshot>
├─ MapEditorHudView.cs    ─ editor HUD (seed entry + palette + undo/redo
│                           + Save Map / Load Map / Exit)
├─ MapEditPaint.cs        ─ pure paint helpers (Land / Capital / Tower /
│                           Tree / Water)
├─ EditorSnapshot.cs      ─ deep copy of editor draft (grid + water + terr.)
├─ HexPaletteButton.cs    ─ hex-shaped palette swatch Control
├─ HexHoverTooltip.cs     ─ editor-only floating tooltip showing the
│                           hovered hex's lex index + (col, row)
├─ HexDragMode.cs         ─ Pan | Paint enum gating HexMapView's
│                           left-button gesture interpretation
├─ GameSettings.cs        ─ global player config (PlayerConfig, PlayerKinds,
│                           optional MasterSeed)
├─ LoadRequest.cs         ─ static one-shot handoff: menu Load → Main
├─ GameController.cs      ─ pure C# orchestration
│
├─ GameState.cs           ─ Grid, Territories, Players, Turns, Treasury,
│                           WaterCoords (off-map renderer-only set)
├─ SessionState.cs        ─ Winner, PendingDefeatScreen, Selected, Mode,
│                           MoveSource, Undo
├─ SessionStateSnapshot.cs─ player-intent slice for undo/redo
├─ UndoEntry.cs           ─ (GameStateSnapshot, SessionStateSnapshot) pair
│
├─ IHexMapView.cs         ─ map view contract (input + overlays + audio)
├─ IHudView.cs            ─ HUD view contract
├─ HexMapView.cs          ─ concrete map: rendering + input + camera pan
│                           + audio forwarding
├─ HudView.cs             ─ concrete HUD: labels + buttons + defeat overlay
│                           + bottom-anchored tutorial-message popup
├─ HeadlessViews.cs       ─ no-op view stubs for diagnostic mode
├─ AudioBus.cs            ─ autoload Node singleton: shared SFX players
│                           that survive scene changes
│
├─ AiPacer.cs             ─ IAiPacer + SynchronousAiPacer
├─ GodotAiPacer.cs        ─ SceneTree-backed pacer (with Cancel)
├─ AiAction.cs            ─ AiMoveAction / AiBuyUnitAction / …
├─ AiCommon.cs            ─ shared candidate-action enumeration
├─ AiDispatcher.cs        ─ routes by Player.Kind
├─ AiSimulator.cs         ─ Clone + apply for 1-ply lookahead
├─ AiStateScorer.cs       ─ scoring function for HeuristicAi
├─ RandomAi.cs            ─ uniform-random chooser
├─ HeuristicAi.cs         ─ 1-ply best-score chooser
├─ TutorialAi.cs          ─ scripted tutorial opponent (currently
│                           always returns null → passes turn)
├─ AiLog.cs               ─ gated stdout logging
│
├─ MapGenerator.cs        ─ CA-driven land/water carve + tree scatter
├─ TerritoryFinder.cs     ─ pure rules
├─ TerritoryLookup.cs     ─ FindOwnedContaining / FindByCapital helpers
├─ CapitalPlacer.cs       ─
├─ CapitalReconciler.cs   ─
├─ DefenseRules.cs        ─
├─ MovementRules.cs       ─
├─ PurchaseRules.cs       ─
├─ TreeRules.cs           ─
├─ UpkeepRules.cs         ─
├─ WinConditionRules.cs   ─
│
├─ SaveStore.cs           ─ user://saves/ + user://maps/ slot CRUD;
│                           res://tutorials/ read-only bundled maps
├─ SaveSerializer.cs      ─ JSON (de)serializer for game state + maps
├─ SaveSlotInfo.cs        ─ slot listing metadata
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
├─ ZoomMath.cs            ─ pixel↔hex helpers used by HexMapView
├─ GameStateSnapshot.cs   ─
└─ UndoStack.cs           ─ generic two-sided history (used by both play
                            and editor)

scenes/
├─ main_menu.tscn         ─ initial scene (pinned in project.godot)
├─ main.tscn              ─ play scene
└─ map_editor.tscn        ─ editor scene

tests/
├─ TestHelpers.cs         ─ shared fixtures
├─ MockHexMapView.cs      ─ IHexMapView in-memory impl
├─ MockHudView.cs         ─ IHudView in-memory impl
├─ QueuedAiPacer.cs       ─ IAiPacer that queues callbacks for explicit
│                           Drain() — used by tests that need to inspect
│                           intermediate AI step state
└─ *Tests.cs              ─ xUnit tests covering controller flows,
                            rules, AI, snapshot/undo, primitives,
                            save/load round-trip, autosave, abandon,
                            RNG determinism, editor paint + snapshot/undo
```

`Main.cs`, `MainMenuScene.cs`, `MapEditorScene.cs`,
`MapEditorHudView.cs`, `HexPaletteButton.cs`, `HexHoverTooltip.cs`,
`HexMapView.cs`, `HudView.cs`, `GodotAiPacer.cs`, `HeadlessViews.cs`,
`SaveStore.cs`, and `AudioBus.cs` are NOT compiled into the test assembly — they
derive from Godot nodes or depend on `SceneTree` / Godot
`FileAccess` / autoload lifecycle. The test csproj explicitly lists
each production source file it includes, so when you add a new
testable source file you must add a matching `<Compile Include>`
entry or tests won't see it.

## Tests

Run with `dotnet test`. The suite covers every static rule class,
the `GameController` click + turn state machine (with mock views and
the synchronous pacer), `Treasury`, `UndoStack`, `GameStateSnapshot`,
both AI flavors, the editor's paint helpers + `EditorSnapshot`
round-trip, save/serialize/deserialize equivalence, RNG determinism
across save/load, and all the primitive structs. The view layer is
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
