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
│           • Procedural: Player.BuildRoster + MapGenerator.BuildInitial- │
│             Grid (CA carve → land/water + ~5% trees) →                  │
│             TerritoryFinder.Recompute → new GameState (incl. Water-     │
│             Coords).                                                    │
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
│   │    HandleCapture → TerritoryFinder.Recompute(grid, prev, treasury)   │
│   │                    (= FindAll → CapitalReconciler.Reconcile →        │
│   │                       Treasury.ReconcileAfterCapture)                │
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
│                       → _hud.SetEndTurnCta(!hasActionable)               │
│                       → _onAfterRefresh?.Invoke()  (Preview cue hook;    │
│                         null in ordinary play)                           │
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
│   │   ((Color, percent)?  │  │   ├─ RefreshOccupantVisuals(color, tr.)    │
│   │   — drives the claim- │  │   ├─ PlayDestructionEffect(coord, occ.)    │
│   │   victory overlay;    │  │   ├─ Play{UnitPlaced, TowerPlaced,         │
│   │   percent ∈ {50,75,90}│  │   │    UnitCombined, UnitDestroyed,        │
│   │   — human-only)       │  │   │    TowerDestroyed, TreeCleared,        │
│   ├─ ClaimVictoryPrompted │  │   │    CapitalDestroyed, Bankruptcy,       │
│   │   HighestThreshold    │  │   │    GameWon, Rally, PlayerDefeated}     │
│   │   (Dict<Color,int> —  │  │   │    — audio sinks routed to AudioBus    │
│   │   color→highest tier  │  │   └─ layers: borders / capitals / units /  │
│   │   dismissed; persists │  │             towers / trees / graves /     │
│   │   across save/load)   │  │             targets / highlight            │
│   ├─ SelectedTerritory    │  │                                            │
│   ├─ Mode (enum)          │  │                                            │
│   ├─ MoveSource           │  │                                            │
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
│   TerritoryFinder.Recompute(grid, prev, treasury?)                       │
│                                            ─ FindAll → CapitalReconciler │
│                                              .Reconcile → optional       │
│                                              Treasury.ReconcileAfter-    │
│                                              Capture. Single entry for   │
│                                              post-mutation rebuilds      │
│                                              (capture, edit paint, init) │
│   CapitalPlacer.Choose(coords, grid)       ─ empty > unit, lex-min       │
│   CapitalReconciler.Reconcile(raw, old, grid)                            │
│                                            ─ split/merge + stomping      │
│   PurchaseRules.CostFor / CanAfford / CanAffordTower / IsValidPeasant…   │
│   MovementRules.ValidTargets / Move / PlaceNew /                         │
│                  ArrivalConsumesAction (capture/tree/grave → true)        │
│   DefenseRules.Defense(coord, grid, territory)                           │
│   TreeRules.RunStartOfTurnGrowth / ConvertGravesToTrees /                │
│             CountIncomeProducingTiles                                    │
│   UpkeepRules.UpkeepFor / TotalUpkeepFor / ApplyUpkeep / ApplyUpkeepFor  │
│   WinConditionRules.WinnerByDomination (mid-turn)                        │
│                    .WinnerAtEndOfTurn (sole capital-bearer)              │
│                    .IsEliminated                                         │
│                    .MeetsClaimVictoryThreshold (>X%, parameterized)      │
│                    .NextClaimVictoryThreshold (50/75/90 tiers)           │
│                    .ClaimVictoryThresholdsPercent (constant: {50,75,90}) │
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
│   UserSettings — static class; SfxEnabled / VfxEnabled toggles           │
│                  persisted to user://settings.json (lazy load,           │
│                  atomic tmp+rename save); read by AudioBus +             │
│                  HexMapView, written by MainMenuScene's settings panel   │
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
│                                                                          │
│   Each AudioBus.Play* method early-returns when                          │
│   UserSettings.SfxEnabled is false — a single chokepoint that gates     │
│   both gameplay sounds and AttachClick-wired UI clicks. Destruction VFX │
│   (HexMapView.PlayDestructionEffect: flash + shockwave + shards) gates  │
│   on UserSettings.VfxEnabled. Pulse / shrink / grow-in animations are   │
│   always on — they communicate game state and disabling them would     │
│   hurt readability.                                                     │
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
event Action? NextTerritoryClicked;    // Tab hotkey equivalent;
                                       // skips singletons and any
                                       // territory with no available
                                       // action (no unmoved unit and
                                       // can't afford a peasant);
                                       // no-op if none qualify
event Action? PreviousTerritoryClicked;// Shift+Tab hotkey equivalent;
                                       // same skip rules
event Action? NextUnitClicked;         // N hotkey: cycle units in selection
event Action? PreviousUnitClicked;     // Shift+N hotkey
event Action? CancelActionPressed;     // Escape hotkey equivalent
event Action? SaveGameClicked;         // handled in Main (opens save dialog)
event Action? DefeatContinueClicked;   // dismiss defeat overlay; resume AI
event Action? ClaimVictoryWinNowClicked;   // declare win now from prompt
event Action? ClaimVictoryContinueClicked; // dismiss prompt, proceed End Turn
event Action? ReplayClicked;           // Replay button on victory overlay;
                                       // handled in Main → controller.BeginReplay

void Refresh(GameState state, SessionState session, bool hasActionableRemaining);
void SetMapLabel(string text);         // one-time after setup; "Map: foo"
                                       // for starting-map games, "Seed: N"
                                       // for procedural
void ShowTutorialMessage(string text); // bottom-anchored info popup;
                                       // click-through (MouseFilter=Ignore)
void ShowTappableTutorialMessage(string text); // same panel, but a
                                       // full-viewport invisible
                                       // overlay catches clicks anywhere
                                       // on screen and fires
                                       // TutorialMessageTapped. Used by
                                       // TutorialNarrationDriver for
                                       // display-text beats that block
                                       // until the player acknowledges.
void HideTutorialMessage();            // dismiss it (also disarms the
                                       // tap catcher) — Main / drivers
                                       // call this when input is acked
event Action? TutorialMessageTapped;   // raised by the tap catcher
                                       // while ShowTappableTutorialMessage
                                       // is active
void SetReplayAvailable(bool available); // toggle the victory-overlay
                                       // Replay button; Main flips it on
                                       // GameEnded iff the controller has
                                       // replay history from game start

// CTA-styled button highlights (white bg + black border + black text).
// SetEndTurnCta takes an extra pulse flag: game-side "out of moves"
// is steady (pulse: false), Tutorial Preview's scripted End Turn beat
// pulses (pulse: true) — a looping Tween on Modulate.a (1.0 ↔ 0.55).
// The other five tutorial CTAs are Tutorial-Preview-only and always
// pulse internally.
void SetEndTurnCta(bool isCta, bool pulse);
void SetBuyPeasantCta(bool isCta);
void SetBuildTowerCta(bool isCta);
void SetClaimVictoryWinNowCta(bool isCta);
void SetClaimVictoryContinueCta(bool isCta);
void SetDefeatContinueCta(bool isCta);

// Force-disable the Undo / Redo button row regardless of
// session.Undo state. Tutorial Preview latches this true because
// undo/redo isn't recorded as beats and would desync the script
// cursor from the player's actions.
void SetUndoRedoLocked(bool locked);

// Suppress the full-win "X wins!" overlay even when session.Winner
// is set. GameController latches this true in its constructor when
// previewMode or recordingMode is on — game-over signaling in
// tutorial modes flows through the bottom-center tutorial-message
// panel, not a click-blocking modal that would freeze the
// scripted / recording flow.
void SetVictoryOverlaySuppressed(bool suppressed);
```

The defeat overlay is part of the HUD: `Refresh` reads
`session.PendingDefeatScreen` and shows/hides a click-blocking panel
naming the eliminated player. Three buttons: **Continue** raises
`DefeatContinueClicked` (resumes the paused AI loop so the human can
watch the rest play out); **Play Again** raises `NewGameClicked`
(handled by `Main.RestartCurrentGame` — same as the Victory
overlay); **Main Menu** reuses `MainMenuClicked`.

The claim-victory overlay is the third HUD overlay: `Refresh` shows
it iff `session.PendingClaimVictory.HasValue` and neither `Winner`
nor `PendingDefeatScreen` is set (Winner > Defeat > ClaimVictory).
**Win Now** raises `ClaimVictoryWinNowClicked`; **Continue Playing**
raises `ClaimVictoryContinueClicked`. See the "Claim victory prompt"
subsection under Win conditions.

The tutorial popup is a bottom-anchored, autowrap panel managed via
`ShowTutorialMessage` / `ShowTappableTutorialMessage` /
`HideTutorialMessage` (no `Refresh`-driven state). Two interaction
modes: the default `ShowTutorialMessage` leaves the panel
click-through (`MouseFilter=Ignore`) so map clicks pass through; the
`ShowTappableTutorialMessage` variant additionally adds a
full-viewport invisible tap catcher (`MouseFilter=Stop` on top of all
HUD content) so a click anywhere on screen fires
`TutorialMessageTapped` and is otherwise swallowed — the player can't
hit Buy / End Turn / select a tile while a narration beat is gated.
It surfaces four sources of text during Tutorial Preview:

- Per-beat step instructions ("Press the Buy Peasant button.",
  "Move the selected Peasant onto the target Spearman to combine
  them into a Knight.", "Place the Peasant at the highlighted tile
  to clear the tree and capture the tile.", etc.) generated by
  `TutorialInstructionText.For(beat, state, session)` and pushed by
  `TutorialPreviewCues` at the tail of every `Apply()`. Uses
  `ShowTutorialMessage` (non-tappable).
- Authored display-text narration from `ReplayDisplayTextBeat`s,
  pushed by `TutorialNarrationDriver` via
  `ShowTappableTutorialMessage` and dismissed by the player tapping
  anywhere.
- Rejection toasts when the dev attempts a non-script action;
  `PreviewPane` subscribes to `TutorialPreview.PlayerActionRejected`.
- The terminal "Tutorial complete." toast set by
  `PreviewPane.OnFinished`.

Cues hide the panel during opponent (AI) turns mid-tutorial so the
step text doesn't linger, but leave it alone once the script is
exhausted (`NextPlayer0Beat == null`) so the completion toast
survives.

**`IAiPacer`** — schedules deferred continuations for both the AI
step machine and the replay step machine. `GodotAiPacer` schedules
via an injected `ITimerFactory` (production wires
`SceneTreeTimerFactory`, which wraps `SceneTree.CreateTimer`; tests
wire `ManualTimerFactory`, which stores callbacks for the test to
fire on demand). `SynchronousAiPacer` drains scheduled callbacks via
a FIFO trampoline (the outermost `Schedule` runs the drain loop until
empty; nested `Schedule` calls from within callbacks just enqueue and
return). The trampoline keeps the contract — every queued callback
fires before the outermost `Schedule` returns — but flattens the
stack so long AI chains under all-AI tests don't recurse
`StepAiPreview` ↔ `StepAiExecute` into a stack overflow. Used by
tests and diagnostic mode. `Cancel` drops any pending callbacks
but does **NOT** poison future `Schedule` calls — the same pacer
instance must survive Cancel-then-reuse cycles because
`BeginReplay` cancels any straggling AI step before scheduling its
first replay step. `GodotAiPacer` implements this via a generation
counter (each `Cancel` bumps the gen; each `Schedule` captures the
current gen; the timer-fired callback checks the captured gen still
matches before invoking). `Main` also calls Cancel via
`GameController.AbandonGame()` before swapping back to the menu so
an in-flight `StepAiExecute` can't fire against disposed
`Polygon2D` nodes after the scene swap.

```csharp
void Schedule(Action callback, int delayMs);
void Cancel();
```

```csharp
// Split out for testability — production = SceneTreeTimerFactory,
// tests = ManualTimerFactory.
public interface ITimerFactory { void After(int delayMs, Action callback); }
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
- **Replay log is honest about what actually happened.** Recording
  appends a `ReplayBeat` at execute time, but the undo/redo handlers
  pop matching beats off (or push them back on redo) so an undone
  move never appears in the saved replay. The log grows monotonically
  across `EndTurn` (unlike the undo stack, which is per-turn and
  cleared at `EndTurnNow`).
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

Three independent tiers, defined by the constant
`WinConditionRules.ClaimVictoryThresholdsPercent = {50, 75, 90}`. When a
**human** presses End Turn, `OnEndTurnPressed` consults
`WinConditionRules.NextClaimVictoryThreshold(color, grid, highestSeen)`,
which returns the **highest** tier the player meets that is strictly
greater than the highest tier they've already dismissed (or null).
Strict `>` semantics; water (off-map) is excluded because it isn't part
of `state.Grid.Tiles`.

If a tier is returned, `OnEndTurnPressed` sets
`SessionState.PendingClaimVictory = (color, threshold)` and refreshes
the view; the HUD shows a centered "Claim Victory?" overlay with
**Win Now** and **Continue Playing** buttons. The wording is
intentionally identical at every tier — the threshold is internal-
only — though "show only highest unseen" means a single End Turn that
crosses multiple tiers (e.g., 40% → 80%) skips straight to the topmost
unseen one (75% in that example).

The pending End Turn is held until the user picks:

- **Win Now** (`OnClaimVictoryWinNowPressed`) records
  `ClaimVictoryPromptedHighestThreshold[color] = threshold`, calls
  `DeclareWinner`, clears undo, and fires `GameEnded`.
- **Continue Playing** (`OnClaimVictoryContinuePressed`) records the
  same dismissal entry and runs `EndTurnNow()` — exactly the original
  End Turn flow. The recording is a max-update: a higher tier
  dismissed later overwrites a lower one, so each tier fires at most
  once but later tiers can still appear after lower ones are seen.

The dismissal is recorded **only on user action** (not on show), so a
save+reload while the overlay is up still re-presents the prompt at
that tier. The dictionary is persisted via `SaveSerializer` so reload
cannot reset the per-tier invariant. Older saves carrying the legacy
flat-color list (single 50% tier from the original implementation) load
with each color migrated to `→ 50`, so the new 75% and 90% prompts can
still appear after upgrade. AI players never trigger any tier;
Tutorial Preview and Record likewise suppress the prompt entirely (the
modal would interrupt the scripted / recording flow with author input
that can't be pre-recorded).

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
`WinConditionRules.IsEliminated(currentPlayer.Color, grid)` is true.
The eliminated player can't take any input or AI action, but they're
not silently skipped: each loop iteration runs a "phantom turn" that
ticks the tile-bound rules — `TreeRules.RunStartOfTurnGrowth` (turn >
1; graves on their color → trees, empty same-color cells with ≥2
neighbor trees or a tree-and-water pair spread) then
`UpkeepRules.ApplyUpkeepFor` (orphan units bankrupt into graves
because there's no capital to fund them). Income, view refresh, AI
dispatch and turn logging are skipped — a silent pass-through. Without
this, an eliminated player's lone unit on a singleton would linger
forever on a rotation that always skipped them.

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
              │     │     ├─ state.Territories = TerritoryFinder.Recompute(
              │     │     │       state.Grid, prev, state.Treasury)
              │     │     │     (= FindAll + CapitalReconciler.Reconcile +
              │     │     │       Treasury.ReconcileAfterCapture; enemy gold
              │     │     │       on captured capital tiles is forfeited)
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
  _onAfterRefresh?.Invoke()            // Preview cue paints last; safe
                                       // re-entry — TutorialPreviewCues
                                       // guards with an _applying bool
```

### Long-press → rally

```
HexMapView → TileLongClicked(target tile)
GameController.OnTileLongClicked  ── wrapped in TrackHandler:
  └─ OnTileLongClickedBody(tile)
        ├─ ignored if game over, no tile, or any pending mode
        ├─ ignored unless tile color == current player's color
        ├─ anyMoved = RallyRules.ResolveRally(grid, territory, target, color)
        │     (collects unmoved units in the territory, sorts closest-to-
        │      target first with lex-min tiebreak, greedy-repositions each
        │      to the strictly closer empty in-territory cell via
        │      MovementRules.Move on own-empty — does NOT consume the
        │      move action; shared with replay's ApplyLongPressRally)
        ├─ if anyMoved: _handlerMutatedGame = true; PlayRally;
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

### Replay turn (paced)

Mirrors the AI step machine, but consumes a recorded `ReplayBeat`
log instead of asking the AI for the next action:

```
BeginReplay (public, called from victory-overlay Replay button):
  ├─ _aiPacer.Cancel  (drop any stragglers; Cancel-then-reuse is OK)
  ├─ _replayMode = true, _replayIndex = 0, _gameEndedFired = false
  ├─ _initialSnapshot.ApplyTo(grid, treasury) → territories
  ├─ _state.Turns.Reset(initialPlayerIndex, initialTurnNumber)
  ├─ clear session: Winner, PendingDefeat, PendingClaim, pending action
  ├─ ClearUndoAndReplayBookkeeping
  ├─ map.RebuildAfterTerritoryChange + overlay clears + RefreshViews
  └─ schedule StepReplayPreview after AiBetweenPlayersDelayMs

StepReplayPreview:
  ├─ if _replayIndex >= _replayBeats.Count → EndReplay
  ├─ resolve acting territory (TerritoryLookup.FindOwnedContaining
  │     on the beat's source/capital coord)
  ├─ _map.ShowHighlight(acting); RefreshViews
  └─ schedule StepReplayExecute after AiPreviewDelayMs
       (or AiActionDelayMs if the next beat is ReplayEndTurnBeat)

StepReplayExecute:
  ├─ dispatch by record type:
  │    ReplayMoveBeat        → ExecuteAiMove(From, To)
  │    ReplayBuyBeat         → ExecuteAiBuyUnit(Capital, To, Level)
  │    ReplayBuildTowerBeat  → ExecuteAiBuildTower(Capital, To)
  │    ReplayEndTurnBeat     → ReplayApplyEndTurn (EndOfTurnProcessing
  │                            + AdvanceToNextActivePlayer + StartPlayerTurn)
  │    ReplayClaimVictoryBeat → DeclareWinner (silent — no overlay)
  │    ReplayDismissClaim    → record threshold, no advance (the
  │                            next EndTurn beat handles it)
  │    ReplayDismissDefeat   → clear PendingDefeatScreen flag (silent)
  │    ReplayLongPressRallyBeat → ApplyLongPressRally (re-derives
  │                            unit moves deterministically from state)
  │    TutorialOnlyBeat       → silently skip. These are authored-only
  │                            (e.g., display-text narration) and the
  │                            in-game Replay viewer ignores them;
  │                            Tutorial Preview consumes them through
  │                            TutorialNarrationDriver instead.
  ├─ CheckGameEndConditions; RefreshViews
  ├─ if IsGameOver → EndReplay (the recorded game-ending beat just
  │     re-fired GameEnded; Main re-runs SetReplayAvailable)
  └─ schedule next StepReplayPreview after
       AiBetweenPlayersDelayMs (if beat was EndTurn) else AiActionDelayMs
```

Replay reuses the live `ExecuteAi*` helpers — same captures, same
FX, same `HandleCapture` reconciliation — so replay fidelity comes
"for free" from converging on the live mutation paths. The actor on
each beat doesn't need to be passed through: `BeginReplay` restored
`CurrentPlayerIndex` from the initial snapshot, and every
`ReplayEndTurnBeat` steps it forward, so `_state.Turns.CurrentPlayer`
is the right player when each `ExecuteAi*` call fires.

**Recording vs. playback.** Every state-mutation site that records a
beat is gated on `!_replayMode` so replay execution doesn't
re-record the beats it's replaying. Human input handlers (all
`TrackHandler`-wrapped, plus the non-wrapped overlay handlers) all
early-return on `_replayMode`. The `StartPlayerTurn` autosave gate
also adds `&& !_replayMode` so autosave doesn't fire during
playback.

**Long-press rally** is a special case: the recorded beat carries
only the target coord, not the per-unit move list. Replay re-runs
`ApplyLongPressRally(target)` against the restored state, which
delegates to `RallyRules.ResolveRally` — the same body the live
handler calls, so live and replay rally cannot drift. The algorithm
explicitly sorts units and destinations by `(distance, lex-min
coord)`, so the re-derivation is deterministic. This matches the
existing trust model for `EndOfTurnProcessing` (tree growth, grave
aging, upkeep — also deterministic from state, triggered by a
single beat).

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
  drift from real play. `AiSimulator.Apply` throws
  `NotSupportedException` on action kinds it doesn't model (Rally,
  ClaimVictory, Dismiss*) so future drift surfaces loudly rather than
  as a silent no-op.
- **`ReplayDrivenAi`** — script-driven chooser used only by the
  TutorialBuilder's Preview mode. Replays recorded non-player-0
  `ReplayBeat`s through the standard AI step machine via a shared
  `ScriptCursor` (also referenced by `TutorialPreview` on the human
  side, so beats consumed by either advance the other). Lives in
  `scripts/Tutorial/`; plugged into `GameController` directly as
  the `aiChooser` delegate, bypassing `AiDispatcher`.
- **`AiDispatcher.ChooseForCurrentPlayer`** — routes to the per-player
  AI flavor based on `Player.Kind`. Wired into `GameController` as
  the single `aiChooser` delegate for normal play.
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
- **Claim-victory prompted tiers.** Saves carry an optional
  `ClaimVictoryPromptedHighestByColorHex` field — a hex→percent map of
  the highest claim-victory tier (50/75/90) each human color has
  already dismissed this game. Empty/missing in fresh games and
  starting maps. `Main` seeds
  `SessionState.ClaimVictoryPromptedHighestThreshold` from this on
  load so the per-tier once-per-game invariant survives reloads.

  The legacy `ClaimVictoryPromptedColorHexes` field (flat color list
  written by the single-tier 50%-only version of this feature) is
  still **read** by the deserializer — each entry maps to `→ 50` —
  but new saves never **write** it. Read precedence: the new dict
  wins if non-empty.
- **Load.** The main menu's Load button populates `LoadRequest.Pending`
  with a `LoadedSave` (state + players + master seed + max-turn cap +
  optional OriginMapName + optional claim-victory prompted tiers) and
  changes scene to `main.tscn`.
  `Main._Ready` consumes and clears the request. On the in-progress
  load path, fresh grid construction is skipped and
  `controller.Resume()` is called instead of `StartGame()`.
- **`Resume()`** reseeds the RNG for the current turn, runs any
  leading AI turns until control reaches a human (or game ends),
  refreshes the views, then fires `HumanTurnStarted` if the resumed
  player is human (so the autosave hook still runs after a load).
- **Play Again.** The Victory and Defeat overlays both raise
  `NewGameClicked`, which `Main` handles via `RestartCurrentGame`:
  write `GameSettings.MasterSeed = _controller.MasterSeed` (so a
  procedural map regenerates with the identical seed even if the
  menu never ran or chose at random), and if `_originMapName != null`
  re-populate `LoadRequest.Pending` with `SaveStore.LoadStartingMap`
  (so starting-map games reload the same hand-painted or bundled
  map instead of falling through to procedural). Finally
  `GetTree().ReloadCurrentScene()`. Failures to reload the origin
  map (e.g., file deleted between play and restart) log a warning
  and fall through to procedural with the preserved seed rather
  than crashing.

`SaveStore` reads/writes `user://saves/` (in-progress games),
`user://maps/` (starting maps from the editor), and `user://tutorials/`
(authored tutorials from the TutorialBuilder), and reads from
`res://tutorials/` (bundled maps shipped with the game — currently
just `Tutorial.json`, loaded via `LoadBundledMap`). It exposes
`WriteAutosave`, `WriteSlot`, `WriteMapSlot`, `WriteTutorial`,
`ListSlots`, `ListMaps`, `ListTutorials`, `LoadSlot`, `LoadMap`,
`LoadTutorial`, `LoadBundledMap`, `LoadStartingMap` (tries
`user://maps/` then falls back to `res://tutorials/` — used by the
Play Again restart flow), plus `SanitizeSlotName` for
filesystem-safe slot names. `SaveSerializer` is the JSON layer
(format version 4; accepts v2/v3 on read so existing autosaves keep
loading after each cutover); `Serialize` writes the player roster's
`Kind` field, `SerializeMap` omits it (the editor's saved maps
don't bake a player-kind config — roles are assigned at play time
from the menu). Both accept an optional `Tutorial` POCO that
round-trips as the top-level `"Tutorial"` block carrying just
`{ Title }` — the recorded gameplay lives in the sibling `"Replay"`
block; `Tutorial` and `Replay` must both be present on a tutorial
save (Deserialize throws otherwise). Absent on regular in-progress
saves and starting maps. `SaveSlotInfo` is the slot listing record.

**Replay block (v4+).** `Serialize` and `WriteSlot` / `WriteAutosave`
accept an optional `Replay` POCO that round-trips as the v4-only
top-level `"Replay"` block. It carries:

- `InitialState` — the per-game-start `GameStateSnapshot` (tiles +
  occupants + capital gold + territories) plus the starting
  `TurnNumber` / `CurrentPlayerIndex`. Captured by
  `GameController.StartGame` after `SeedStartingGold` and before
  `Resume`, so it represents "turn 1 as the player first saw it"
  — the same anchor `BeginReplay` rewinds to.
- `Beats` — the ordered list of recorded `ReplayBeat`s. Same
  kind-discriminated DTO pattern as tutorial beats; switches in
  `SerializeReplayBeats` / `DeserializeReplayBeats` handle each
  concrete kind (Move / BuyUnit / BuildTower / EndTurn /
  LongPressRally / ClaimVictory / DismissClaim / DismissDefeat).

The block is absent from `Map` and `Tutorial` save flavors (those
don't have player history), and null/missing in v2/v3 saves on
load. v3-save load captures a `_initialSnapshot` at load time so
future autosaves of that game can carry replay data; the controller
sets `_replayDataIsCompleteFromStart = false` so the
victory-overlay Replay button stays disabled — the recorded log
starts after the load, not at game start.

## Map editor

`MapEditorScene` (root of `res://scenes/map_editor.tscn`, reached
from the main menu's "Map Editor" button) is a separate scene that
lets the user paint a starting map by hand and save it to
`user://maps/`. It deliberately doesn't reuse `GameController` —
nothing about it is turn- or rules-driven — but it does reuse the
view layer (`HexMapView` + a sibling `MapEditorHudView`) so map
edits look identical to in-game terrain.

- **Scene/panel split.** `MapEditorScene` is a thin chrome host: it
  owns the `MapEditorHudView`, the `SaveStore`, the Save / Load
  dialogs, the `EscMenu` modal, the Escape→hand→modal ladder, and
  `ReturnToMainMenu`. The
  editor body lives in `MapEditorPanel : Node2D` — a reusable Node
  that owns the `HexMapView` instance, the draft grid/water/territory
  state, the paint-stroke state machine, the undo stack, and the
  hover tooltip. The scene wires HUD events
  (`PaletteSelectionChanged`, `GenerateRequested`, `UndoLast/All`,
  `RedoLast/All`, `SaveMapClicked`, `LoadMapClicked`) to panel
  methods (`SetSelectedPalette`, `GenerateMap`, `UndoLast/All`,
  `OpenSaveDialog`, `OpenLoadDialog`) and listens to
  `panel.UndoStateChanged` to refresh the HUD's undo-bar enable
  state. The split exists so `tutorial_builder.tscn` can host the
  same panel under different chrome (see "Tutorial builder" below)
  without forking the editor logic.

  The panel exposes `PaintingEnabled` (gates all paint events; flipped
  off by Build/Preview hosts), `SnapshotDraft` / `RestoreDraft` (used
  by Preview cloning in later phases), and `BuildLiveState` /
  `BuildSaveState` so the chrome host can serialize without poking
  panel internals.
- **HUD configurability.** `MapEditorHudView` exposes two configuration
  knobs that hosts set before `AddChild`:
  - `ShowSceneRootChrome` (default `true`) — controls whether the
    Save Map / Load Map / Exit buttons are built. The standalone
    `MapEditorScene` keeps them; `TutorialBuilderScene` sets it
    `false` and exposes Save Tutorial / Load Tutorial / Exit through
    the shared `EscMenu` modal instead. The standalone editor's
    Exit button raises `EscRequested` to open the same modal.
  - `TopOffsetPx` (default `0`) — vertical offset of the entire HUD
    strip. Both the standalone editor and TutorialBuilder use `0`
    (HUD at y=0..60); the knob remains for future hosts that want a
    stacked layout.
- **Draft state.** The panel owns a mutable `HexGrid`, water set,
  and territory list, plus an `UndoStack<EditorSnapshot>` for
  undo/redo. `EditorSnapshot.Capture` deep-copies all three; its
  `ApplyTo` rebuilds the grid from scratch (paints can both add and
  remove tiles, so `GameStateSnapshot`'s in-place tile updates aren't
  enough).
- **Push cycle.** Every paint or generate calls the panel's
  `PushState` which rebuilds a fresh `GameState`, hands it to
  `HexMapView.ReloadState` (preserving zoom/pan), and reapplies
  occupant visuals, then fires `DraftChanged` and `UndoStateChanged`
  for the host to react to. This is why `HexMapView` exposes both
  `Init` and `ReloadState`.
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
  entry. Pan-mode, no paint. Escape ladders out: a first press with
  any non-hand swatch active reselects the hand (canceling the
  paint mode); a second press with the hand already active opens
  the `EscMenu` modal (Resume / Exit).
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

## Tutorial builder

`TutorialBuilderScene` (root of `res://scenes/tutorial_builder.tscn`,
reached from the main menu's debug-only "Tutorial Builder" button —
gated on `OS.IsDebugBuild()` so release exports never see it) is a
3-mode authoring tool for tutorials. Tutorials are stored as v4 save
files in `user://tutorials/` carrying both a `Tutorial { Title }`
block and a `Replay { InitialState, Beats }` block — the same Replay
format that ships with every in-progress save.

The scene reuses the Map Editor body: a single `MapEditorPanel`
instance constructed in `_Ready` and never torn down. Mode switching
only flips `panel.PaintingEnabled` and the per-mode chrome's
`Visible`, so the painted draft survives every transition.

### Modes

`TutorialMode { MapEdit, Record, Preview }`. Mode switching, Save /
Load Tutorial, and Exit all flow through the shared `EscMenu`
modal — there is no dedicated top strip and there are no 1/2/3
hotkeys. The modal's button for the current mode is rendered
`Disabled = true`.

- **Map Edit** — `panel.PaintingEnabled = true`; chrome-trimmed
  `MapEditorHudView` (palette + seed + Generate + undo bar) visible
  at y=0..60.
- **Record** — `panel.PaintingEnabled = false`; `RecordPane` builds
  a transient `GameController` over the painted draft with all six
  players forced `AiKind.Human`. The pane's own `HudView` occupies
  y=0..60. The dev plays hot-seat for all six players; the
  controller's normal recording pipeline (`_replayBeats` via
  `TrackHandler` / `StepAiExecute`) captures game-action beats
  automatically. A small **`+ Text`** button below the HUD strip lets
  the dev author tutorial-only beats (currently just
  `ReplayDisplayTextBeat`; see "Tutorial-only beats" below) inline
  between game-action beats.
- **Preview** — `panel.PaintingEnabled = false`; `PreviewPane` builds
  a transient `GameController` where player 0 is Human (the dev plays
  Red) and players 1-5 are AI driven by a `ReplayDrivenAi` chooser
  that replays the recorded non-player-0 beats.

ESC opens the shared `EscMenu` modal in every submode. In Map Edit
submode ESC first drops a non-hand palette back to hand (mirrors the
standalone Map Editor); the second press with hand selected opens
the modal. `RecordPane` / `PreviewPane` forward their inner
`HudView`'s `EscRequested` event up to the scene so ESC inside
Record / Preview opens the same modal.

**Draft preservation across mode switches.** The panel's `_grid` is
shared with the play state Record / Preview build atop, so peasants /
towers placed during a recording mutate the same tile occupants the
panel later reads. `TutorialBuilderScene` captures an
`EditorSnapshot` of the draft on every exit from Map Edit and
restores it (`MapEditorPanel.RestoreDraft`) on every return so the
visuals snap back to the painted state. Switching to Map Edit while
a non-empty recording exists pops a "Discard recording?" confirm
dialog; on confirm, the scene calls `RecordPane.DiscardRecording`
(which calls `RecordingCapture.Reset`) before applying the switch.

**Tutorial-load realignment.** A saved tutorial's
`LoadedSave.State.Grid` reflects whatever frame the dev was sitting
on at save time — if they saved mid-Record/Preview, that frame is
post-replay, not the painted starting map. `OnLoadSlotPressed` calls
`MapEditorPanel.ResetToTutorialStart(Replay.InitialSnapshot)` right
after `LoadFromMap` so the panel's `_grid` matches the recording's
initial frame regardless of save state. The subsequent
MapEdit→Record `SnapshotDraft` then captures the painted starting
map, which is what a later Discard restores.

### Record-mode flow

`SetMode(Record)` dispatches to one of two entry points on
`RecordPane`:

- **Fresh entry** (`StartRecording`) — called whenever the previous
  mode was Map Edit (or the recording was already empty). Builds a
  controller from `panel.BuildLiveStateWith(roster)` against the
  painted draft, calls `StartGame` to capture
  `_initialSnapshot` post-`SeedStartingGold`, and starts the
  recording from beat 0.
- **Resume from Preview** (`ContinueRecording(previous)`) — called on
  `Preview → Record` when a recording already exists. Builds a
  controller with `loadedReplay: previous.Replay` (so
  `_initialSnapshot` and `_replayBeats` are seeded from the existing
  Tutorial) and calls `BeginReplay`. Under `SynchronousAiPacer`'s
  trampoline the entire replay drains inline, leaving the state at
  the recorded end-state with `_replayMode = false` and the beats
  list intact. The dev's subsequent inputs append new beats to the
  same list.

Both paths share the rest of the setup:

1. All-Human roster from the panel's colors/names.
2. `state = panel.BuildLiveStateWith(roster)` — same grid/territories
   as the panel's draft.
3. Spin up a real `HudView` + `GameController` with
   `aiChooser: null`, `aiPacer: new SynchronousAiPacer()` (no AI ever
   runs, so the pacer is unused outside the resume path's replay),
   and `recordingMode: true`. The latter gates
   `HandleCapture`'s `PendingDefeatScreen` assignment to player 0
   only — without it, every defeat in the all-Human roster pops the
   defeat overlay (Blue, Green, … all look like humans), interrupting
   the recording with toasts for slots that will be AI in the
   eventual Preview playback. It also suppresses the End-Turn
   claim-victory prompt and tells the HUD to hide the full-win
   overlay, for the same scripted-flow-can't-eat-a-modal reason as
   Preview.
4. `panel.Map.DragMode = HexDragMode.Pan` so tile clicks fire.
5. The dev plays normally. Every action goes through `TrackHandler`
   / `StepAiExecute` which record `ReplayBeat`s into `_replayBeats`.

`RecordPane.HasRecording` returns true iff there's a non-empty
captured tutorial — the TutorialBuilder reads it both to gate the
discard-confirm dialog and to decide between `StartRecording` /
`ContinueRecording`.

`RecordPane.PrimeForContinue(Tutorial)` pre-populates the capture
from a loaded Tutorial without starting a recording session. Used
by `OnLoadSlotPressed`: after a Load Tutorial the scene calls
`PrimeForContinue` (if the loaded file has beats) and then
`SetMode(TutorialMode.Record)`. `ApplyModeSwitch`'s Record branch
inspects `CurrentTutorial` (regardless of previous mode); a
non-empty tutorial triggers `ContinueRecording` so the dev resumes
authoring the loaded script, otherwise `StartRecording` runs fresh.

**Authoring tutorial-only beats.** While recording, RecordPane's chrome
includes a `+ Text` button anchored under the HUD strip. Click opens a
small modal panel (translucent backdrop catches stray clicks) with a
`LineEdit` + Insert / Cancel buttons. Submit calls
`controller.RecordTutorialOnlyBeat(new ReplayDisplayTextBeat { Text = ... })`,
which stamps `Index` + `Turn` from current state and forces
`Actor = -1`. Beats are appended at the current end of the recording
list — there's no in-line insertion / editing yet; if you want to add
narration before turn N, author it before pressing End Turn into N+1.
The button and dialog are torn down in `StopRecording`.

`RecordPane.StopRecording` (on `SetMode(out of Record)`):

- Snapshots the captured tutorial into a `RecordingCapture` helper
  BEFORE nulling the controller — the snapshot survives the
  controller teardown so `Save Tutorial` and `Preview` can read it
  after the mode switch. The captured tuple is
  `(InitialSnapshot, InitialTurn, InitialPlayer, Beats[])`.
- `controller.AbandonGame()` unsubscribes the controller from
  `panel.Map`'s `TileClicked` and every `_hud` event (the pacer
  cancel was already there). Without the unsubscribe, the abandoned
  record controller would still route shared `panel.Map` clicks into
  itself during Preview and then hit `ObjectDisposedException` on the
  freed record `HudView`.
- Drag mode restored; the panel re-`Init`s its draft view.

### Preview-mode flow

`PreviewPane.Start(tutorial)` (fired on `SetMode(Preview)`):

1. Roster: player 0 Human (the dev), players 1-5 Heuristic (any AI
   kind works — the chooser is overridden).
2. `state = panel.BuildLiveStateWith(roster)`.
3. `PreviewSetup.Apply(panel.Map, state, tutorial)` — pure-C# helper
   that:
   - Applies `tutorial.Replay.InitialSnapshot` back to the grid +
     treasury.
   - `state.Turns.Reset(initialPlayer, initialTurn)`.
   - `map.RebuildAfterTerritoryChange()` — refreshes border /
     capital / tree / grave layers that don't auto-update on
     per-tile color writes.
   - Clears highlight + every overlay (`ShowMoveTargets` empty,
     `ShowTowerTargets` empty, etc.) so prior-session leftovers
     don't bleed in.
4. A single shared `ScriptCursor` is constructed and passed to BOTH
   `ReplayDrivenAi` (AI side) and `TutorialPreview` (human side).
   Beats consumed by either side advance the other — without this,
   the AI side stayed stuck on the human's already-consumed beats
   and every AI turn no-op'd.
5. `GameController` built with:
   - `aiChooser: replayAi.ChooseNextAction`
   - `humanActionValidator: tutorialPreview.TryAccept`
   - `previewMode: true` (suppresses every `RecordBeat` call so the
     loaded script isn't polluted by the dev's playthrough; also
     skips the End-Turn claim-victory prompt and tells the HUD to
     hide the full-win overlay; does NOT block input handlers —
     Preview wants player-0 clicks through)
   - `aiPacer: new GodotAiPacer(new SceneTreeTimerFactory(GetTree()))`
   - `onAfterRefresh: () => { narration.Tick(); cues.Apply(); }`
     (driver runs first so its `IsPresenting` flag is set before
     cues check it; see Preview cues + Tutorial-only beats below)
6. `TutorialPreviewCues` + `TutorialNarrationDriver` built and wired
   into the controller via the `onAfterRefresh` callback. Forward-
   reference dance: the controller takes the callback at construction,
   but the cues + driver need a reference to the controller (for
   `SelectTerritoryForTutorial` / `CancelActionForTutorial` /
   `RefreshViewsForTutorial`). PreviewPane captures locals `cuesRef`
   and `narrationRef` in the callback closure and assigns them
   post-construction. The narration driver shares the same
   `ScriptCursor` as the AI / human sides so all three consume from
   the same totally-ordered log.
7. `hud.SetUndoRedoLocked(true)` — undo / redo aren't recorded as
   beats and would desync the script cursor from the player's
   actions, so the four undo/redo buttons stay disabled for the
   whole preview session.
8. Drag mode = Pan; `controller.StartGame()`.

While the dev plays:

- Player-0 clicks hit the controller's `ExecuteMove` /
  `ExecuteBuyAndPlace` / `ExecuteBuildTower` / End-Turn / etc.
  Each builds the prospective `ReplayBeat` and calls
  `humanActionValidator` BEFORE mutating state. On mismatch the
  action aborts and `TutorialPreview.PlayerActionRejected` fires;
  `PreviewPane` surfaces the reason via `hud.ShowTutorialMessage(...)`.
- AI turns: the controller's `StepAi*` loop asks
  `ReplayDrivenAi.ChooseNextAction`. The chooser yields the next
  beat for the current actor or null (= "this player done"), and
  the shared cursor advances.

When the final player-0 beat is consumed, `TutorialPreview`
fires `TutorialFinished` and the HUD shows "Tutorial complete."

### Preview cues

`TutorialPreviewCues` is a pure-C# helper that paints visual hints
for the one-and-only-legal-move on player 0's turn. Wired into
`GameController` via the `onAfterRefresh` callback so `Apply()` runs
at the tail of every `RefreshViews()` and again at the tail of every
human `TrackHandler` invocation (handler bodies sometimes paint
`ShowMoveTargets` AFTER their mid-body refresh — e.g.,
`OnTileClickedBody` enters MovingUnit mode and paints all valid
targets after `SetSelection` already refreshed; the tail invocation
ensures the cue paints last and wins).

`Apply()` first checks `narration.IsPresenting`: while a tutorial-only
beat (e.g., display-text narration) is showing, cues early-return so the
narration panel isn't overwritten. Otherwise it reads
`TutorialPreview.NextPlayer0Beat` (which itself returns `null` while a
`TutorialOnlyBeat` sits between the cursor and the next player-0 beat —
see "Tutorial-only beats" below) and dispatches:

- **`ReplayEndTurnBeat`** → `SetEndTurnCta(true, pulse: true)`.
- **`ReplayBuyBeat`** → auto-select capital's territory (via
  `GameController.SelectTerritoryForTutorial`). The Buy button CTA is
  on iff the player is not yet in the matching Buying mode
  (`BuyModeLevel(Mode) != bu.Level`): while they're still cycling
  presses to reach the target level, the button pulses; once they
  match, the CTA drops and `ShowMoveTargets([To], level)` highlights
  the single target tile instead.
- **`ReplayBuildTowerBeat`** → analogous; CTA pulses on Build Tower
  while `Mode != BuildingTower`, then drops in favor of single-tile
  `ShowTowerTargets([To])` once the player enters BuildingTower mode.
- **`ReplayMoveBeat`** → auto-select source territory; if
  `Mode == MovingUnit && MoveSource == mv.From`, overwrite
  `ShowMoveTargets([To], level)`; otherwise overwrite with `[From]`
  (single ring on the source) to direct the player to pick it up.
- **`ReplayLongPressRallyBeat`** → auto-select containing territory;
  `ShowMoveTargets([Target], Peasant)`.
- **`ReplayClaimVictoryBeat` / `ReplayDismissClaimBeat` /
  `ReplayDismissDefeatBeat`** → CTA on the matching overlay button.

Before dispatching, `Apply` checks mode compatibility with the next
beat. If the player is in a mode the beat can't be executed from
(e.g., still in BuyingPeasant when the next beat is End Turn,
BuildingTower when the next beat is Buy, MovingUnit with the wrong
MoveSource when the next beat is Move), `Apply` calls
`GameController.CancelActionForTutorial()` to clear `Mode` /
`MoveSource` and the associated overlays so the new cue is unambiguous.
A `_applying` re-entrancy guard short-circuits the recursive `Apply`
triggered by `CancelActionForTutorial`'s own `RefreshViews`.

Both `SelectTerritoryForTutorial` and `CancelActionForTutorial`
bypass `TrackHandler` — Tutorial Preview isn't undoable.

After dispatching the per-beat side effects (CTAs, single-tile
highlights, auto-select / auto-cancel), the tail of `ApplyCore`
pushes the human-readable step prompt:

```csharp
_hud.ShowTutorialMessage(TutorialInstructionText.For(next, _state, _session));
```

`TutorialInstructionText.For(ReplayBeat, GameState, SessionState)` is
a pure helper that switches on the next beat kind and the current
`SessionState.Mode` / destination occupant to produce sub-step-aware
strings:

- **Buy beat** — escalates with the player: Mode=None → "Press the
  Buy Peasant button."; Mode=BuyingX below target → "Now press the
  Buy Peasant button again to upgrade to a {next}."; matching mode →
  "Place the {Level} at the highlighted tile{suffix}." where the
  suffix names combine / tree-clear / grave-remove / capture (and
  combined capture-and-clear) outcomes based on the To-tile occupant
  and whether it's a same- or enemy-color tile.
- **Move beat** — pickup state ("Tap the highlighted unit to pick
  it up.") vs placement state, with placement text varying by
  destination occupant: friendly combine names the combined level;
  same-color tree / grave name the clearance; enemy-color names the
  capture (and combined capture-with-clear / capture-with-destroy
  for tree / tower).
- **BuildTower / EndTurn / Rally / Claim / Dismiss** — fixed text
  per beat kind.

When `Apply` returns early (opponent turn mid-tutorial), the cues
call `HideTutorialMessage` so the previous instruction doesn't
linger; once the script ends (`NextPlayer0Beat == null`) the panel
is left alone so PreviewPane's "Tutorial complete." survives.

### Tutorial-only beats

A second `ReplayBeat` sub-hierarchy under `TutorialOnlyBeat` carries
beats that are NOT captured from gameplay — they're authored explicitly
during Record mode and drive presentation only (no state mutation, no
player ownership). First concrete kind: `ReplayDisplayTextBeat { Text }`
(narration text). Anticipated future kinds (deliberately structured so
the dispatcher accepts them without rework): tile / territory highlight
with arrow, pan / zoom camera, HUD-element callout.

**Identity.** `TutorialOnlyBeat` carries `Actor = -1` (sentinel — no
player owns these). The base `ReplayBeat` doc marks the boundary;
the abstract `TutorialOnlyBeat` record itself is the type-system
discriminator that dispatch and the cursor skip-scan key off of.

**Cursor semantics.** The shared `ScriptCursor` is the single source of
truth. Three consumers see it:

- **`TutorialPreview.NextPlayer0Beat`** skip-scans for the next
  player-0 beat AND gates on tutorial-only beats: if any
  `TutorialOnlyBeat` sits between the cursor and the next player-0
  beat, the getter returns `null`. This keeps `TutorialPreviewCues`
  from painting a cue for the action behind the narration.
- **`TutorialPreview.TryAccept`** isn't affected — by the time the
  player can click, the narration driver has already advanced past
  any pending tutorial-only beats during the prior `onAfterRefresh`
  tick.
- **`ReplayDrivenAi.ChooseNextAction`** explicitly returns null (and
  does NOT advance) when the cursor points at a `TutorialOnlyBeat`.
  Only the narration driver advances past these.

**`TutorialNarrationDriver`.** Pure-C# helper wired into PreviewPane's
`onAfterRefresh` callback ahead of `TutorialPreviewCues.Apply()`. On
each tick:

- If `IsPresenting` is true → no-op (re-entrancy guard;
  `RefreshViews` calls during presentation must not double-fire).
- If the cursor is at end-of-script → no-op.
- If the beat at the cursor is `ReplayDisplayTextBeat dt`: call
  `hud.ShowTappableTutorialMessage(dt.Text)`, set `IsPresenting = true`,
  and arm a one-shot `hud.TutorialMessageTapped` subscription. On
  tap: detach the handler (defends against duplicate event raises),
  advance the cursor, clear `IsPresenting`, call `HideTutorialMessage`,
  and fire the refresh callback (`controller.RefreshViewsForTutorial`)
  so the next `Apply` cycle paints the cue for whatever beat follows.
- Unknown future `TutorialOnlyBeat`s fall through a `default:` arm
  that silently advances the cursor — script doesn't stall on
  unrecognized authoring.

**Cues gating.** `TutorialPreviewCues.ApplyCore` early-returns if
`narration.IsPresenting == true`, so the cues don't paint over the
narration message or run their mode-cancel logic. The driver / cues
ordering in the `onAfterRefresh` lambda matters: driver ticks first to
set the flag, then cues check it.

**Tap-anywhere dismissal.** `HudView`'s implementation of
`ShowTappableTutorialMessage` builds a single full-viewport invisible
`Control` (lazy, retained for reuse), moves it to the topmost child
position via `MoveChild`, and sets `MouseFilter = Stop`. Clicks
anywhere — HUD buttons, the map, the tutorial panel itself — are
intercepted and route to `TutorialMessageTapped`. The player can't
accidentally hit Buy Peasant or End Turn while a narration beat is
gated. `HideTutorialMessage` hides the catcher and flips its
`MouseFilter = Ignore` so normal play resumes.

**In-game Replay.** The "Replay" button on the victory overlay runs
`GameController.BeginReplay` → `StepReplayExecute`, whose switch silently
skips `TutorialOnlyBeat`s. Display-text is preview-only narration; the
in-game replay viewer ignores it.

**Recording.** `GameController.RecordTutorialOnlyBeat(TutorialOnlyBeat)`
is the public entry point. It stamps `Index` + `Turn` like the private
`RecordBeat`, but forces `Actor = -1`. Gated on `!_replayMode &&
!_previewMode` so playback and Preview can't accidentally inject
authored beats.

**Serialization.** Round-trips through the same v4 `BeatDto` pipeline:
`Kind = "DisplayText"` discriminator, with the `Text` field on
`BeatDto`. Actor is stored literally (-1) — no color-by-index lookup.

### Why no parallel gating layer

Before the rewrite, Preview wrapped the real views in
`TutorialGatedHexMapView` / `TutorialGatedHudView` and routed every
input through a `TutorialPlayer` state machine that mirrored a tiny
subset of `GameController`'s click/buy/end-turn logic. That layer
was ~300 LOC of duplicated invariants and only covered two beat
kinds (EndTurn, BuyPeasant). The new design pushes gating into
`GameController` itself via the single `humanActionValidator` hook
and reuses `_replayBeats` for the script — one source of truth for
both recording and validation.

### Tutorial file format

Same v4 schema as in-progress saves. A tutorial file is just a v4
save with BOTH a `Tutorial { Title }` block AND a `Replay { ... }`
block. Deserialize throws if the Tutorial block is present without
a Replay block. The `Tutorial` class is `{ Title, Replay }` — no
`StartTurn` / `StartPlayer` / `Beats` (the Replay carries those).

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
├─ MainMenuScene.cs       ─ landing (Play / Load / Map Editor +
│                           debug-only Tutorial Builder) + play-config
│                           panels; Load Game modal; writes
│                           GameSettings + LoadRequest
├─ MapEditorScene.cs      ─ editor scene root; chrome host (HUD, Save/Load
│                           dialogs, EscMenu modal, Escape→hand→modal ladder)
├─ MapEditorPanel.cs      ─ reusable editor body; owns HexMapView + draft
│                           grid/water/territories + UndoStack<EditorSnapshot>
│                           + paint stroke state + hover tooltip
├─ MapEditorHudView.cs    ─ editor HUD (seed entry + palette + undo/redo
│                           + Save Map / Load Map / Exit). Configurable
│                           via ShowSceneRootChrome (gate Save/Load/Exit)
│                           and TopOffsetPx (offset entire strip)
├─ TutorialBuilderScene.cs─ tutorial builder scene root; TutorialMode
│                           { MapEdit, Record, Preview } state machine;
│                           hosts MapEditorPanel + a chrome-trimmed
│                           MapEditorHudView + RecordPane + PreviewPane
│                           + EscMenu modal (mode switches + Save/Load
│                           Tutorial + Exit); captures/restores the
│                           draft EditorSnapshot around play sessions
├─ EscMenu.cs             ─ shared pause/exit modal (CanvasLayer +
│                           centered panel); host scenes call Show with
│                           a mode-aware option list. ESC closes when
│                           open. Used by Main, MapEditorScene,
│                           TutorialBuilderScene
├─ SlotPickerDialog.cs    ─ reusable Window-based load-slot picker;
│                           ShowSlots(slots, emptyMsg, labelFor,
│                           onPicked) + ShowError. Used by MainMenu-
│                           Scene, MapEditorScene, TutorialBuilderScene
├─ RecordPane.cs          ─ Record-mode chrome: spins up a real
│                           GameController over the panel's draft
│                           with all six players Human; captures the
│                           recorded tutorial via RecordingCapture.
│                           ContinueRecording resumes a Preview→Record
│                           handoff by passing the captured Replay to
│                           the controller and calling BeginReplay
├─ PreviewPane.cs         ─ Preview-mode chrome: spins up a real
│                           GameController with ReplayDrivenAi +
│                           TutorialPreview + humanActionValidator;
│                           uses PreviewSetup to reset board state
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
│                           that survive scene changes; each Play* gates
│                           on UserSettings.SfxEnabled
├─ UserSettings.cs        ─ static class; SfxEnabled / VfxEnabled
│                           toggles persisted to user://settings.json
│                           (lazy load, atomic tmp+rename save)
│
├─ AiPacer.cs             ─ IAiPacer + SynchronousAiPacer +
│                           ITimerFactory abstraction
├─ GodotAiPacer.cs        ─ Default production pacer; uses
│                           ITimerFactory + generation counter for
│                           Cancel-then-reuse safety (testable via
│                           ManualTimerFactory)
├─ SceneTreeTimerFactory.cs ─ Production ITimerFactory wrapping
│                           SceneTree.CreateTimer (test-excluded)
├─ AiAction.cs            ─ AiMoveAction / AiBuyUnitAction / …
├─ AiCommon.cs            ─ shared candidate-action enumeration
├─ AiDispatcher.cs        ─ routes by Player.Kind
├─ AiSimulator.cs         ─ Clone + apply for 1-ply lookahead;
│                           throws on unsupported AiAction kinds
├─ AiStateScorer.cs       ─ scoring function for HeuristicAi
├─ RandomAi.cs            ─ uniform-random chooser
├─ HeuristicAi.cs         ─ 1-ply best-score chooser
├─ AiLog.cs               ─ gated stdout logging
│
├─ MapGenerator.cs        ─ CA-driven land/water carve + tree scatter
├─ TerritoryFinder.cs     ─ pure rules
├─ TerritoryLookup.cs     ─ FindOwnedContaining / FindByCapital helpers
├─ CapitalPlacer.cs       ─
├─ CapitalReconciler.cs   ─
├─ DefenseRules.cs        ─
├─ MovementRules.cs       ─
├─ RallyRules.cs          ─ long-press rally: shared between live
│                           OnTileLongClickedBody and replay's
│                           ApplyLongPressRally
├─ PurchaseRules.cs       ─
├─ TreeRules.cs           ─
├─ UpkeepRules.cs         ─
├─ WinConditionRules.cs   ─
│
├─ SaveStore.cs           ─ user://saves/ + user://maps/ +
│                           user://tutorials/ slot CRUD;
│                           res://tutorials/ read-only bundled maps
├─ SaveSerializer.cs      ─ JSON (de)serializer for game state +
│                           maps + optional Tutorial block + optional
│                           Replay block (v4; still reads v2/v3)
├─ SaveSlotInfo.cs        ─ slot listing metadata
├─ Replay.cs              ─ POCO bundling InitialSnapshot + beat list,
│                           round-tripped through the v4 Replay block
├─ ReplayBeat.cs          ─ Discriminated record family:
│                           ReplayMoveBeat / ReplayBuyBeat /
│                           ReplayBuildTowerBeat / ReplayEndTurnBeat /
│                           ReplayLongPressRallyBeat /
│                           ReplayClaimVictoryBeat / ReplayDismissClaim /
│                           ReplayDismissDefeat. Plus a
│                           TutorialOnlyBeat sub-hierarchy (Actor=-1,
│                           authored not captured) with first kind
│                           ReplayDisplayTextBeat — see Tutorial-only
│                           beats subsection
├─ Tutorial/Tutorial.cs   ─ tutorial POCO { Title, Replay }
├─ Tutorial/ReplayDrivenAi.cs ─ AI chooser that replays recorded
│                           non-player-0 beats through the AI step
│                           machine; shares a ScriptCursor with
│                           TutorialPreview
├─ Tutorial/TutorialPreview.cs ─ player-0 input validator; matches
│                           attempted actions against next expected
│                           beat; fires PlayerActionRejected /
│                           TutorialFinished events
├─ Tutorial/RecordingCapture.cs ─ pure-C# captor that lets the
│                           recorded tutorial survive the record
│                           controller's teardown (used by RecordPane)
├─ Tutorial/PreviewSetup.cs ─ pure-C# helper that applies the
│                           tutorial's InitialSnapshot back to the
│                           live state + clears overlays + rebuilds
│                           border/capital layers (used by PreviewPane)
├─ Tutorial/TutorialPreviewCues.cs ─ pure-C# helper that paints the
│                           visual cue for the next required beat
│                           (CTA-styled button + auto-selected
│                           territory + single-tile map highlight)
│                           and pushes the step-text instruction via
│                           ShowTutorialMessage; wired in via the
│                           controller's onAfterRefresh callback
├─ Tutorial/TutorialInstructionText.cs ─ pure-C# lookup that maps
│                           the next ReplayBeat + GameState +
│                           SessionState to a sub-step-aware
│                           English instruction string for the
│                           tutorial popup
├─ Tutorial/TutorialNarrationDriver.cs ─ pure-C# helper that consumes
│                           TutorialOnlyBeats (e.g., display-text
│                           narration) from the shared ScriptCursor
│                           during Preview. Presents via
│                           ShowTappableTutorialMessage, gates cues
│                           via IsPresenting, advances on
│                           TutorialMessageTapped. Wired into
│                           PreviewPane's onAfterRefresh callback
│                           ahead of TutorialPreviewCues.Apply
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
├─ GameStateChecksum.cs   ─ SHA-256 digest over tiles/gold/territories/
│                           turn state; used by replay-fidelity tests
└─ UndoStack.cs           ─ generic two-sided history (used by both play
                            and editor)

scenes/
├─ main_menu.tscn         ─ initial scene (pinned in project.godot)
├─ main.tscn              ─ play scene
├─ map_editor.tscn        ─ editor scene
└─ tutorial_builder.tscn  ─ tutorial builder scene (debug-only entry)

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
                            RNG determinism, editor paint + snapshot/undo,
                            replay recording / playback / fidelity
```

`Main.cs`, `MainMenuScene.cs`, `MapEditorScene.cs`,
`MapEditorPanel.cs`, `MapEditorHudView.cs`, `TutorialBuilderScene.cs`,
`EscMenu.cs`, `RecordPane.cs`, `PreviewPane.cs`,
`HexPaletteButton.cs`, `HexHoverTooltip.cs`, `HexMapView.cs`,
`HudView.cs`, `SceneTreeTimerFactory.cs`, `HeadlessViews.cs`,
`SaveStore.cs`, `AudioBus.cs`, and `UserSettings.cs` are NOT compiled into
the test assembly — they derive from Godot nodes or depend on `SceneTree`
/ Godot `FileAccess` / autoload lifecycle. The test csproj explicitly lists
each production source file it includes, so when you add a new
testable source file you must add a matching `<Compile Include>`
entry or tests won't see it.

## Tests

Run with `dotnet test`. The suite covers every static rule class,
the `GameController` click + turn state machine (with mock views and
the synchronous pacer), `Treasury`, `UndoStack`, `GameStateSnapshot`,
both AI flavors, the editor's paint helpers + `EditorSnapshot`
round-trip, save/serialize/deserialize equivalence, RNG determinism
across save/load, replay recording + playback contracts, and a
6-heuristic-AI replay-fidelity test that hashes the live final
state, round-trips it through SaveSerializer, and asserts the
replayed state matches digest-for-digest. The view layer is
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
