# FourExHex Architecture

Snapshot of the architecture as it stands today. Start here if you're
new to the codebase. The MVC split (Main → GameController → views /
model / rules) is the load-bearing structure; everything else hangs
off it.

## Project structure & the Godot-free model (read this first)

The codebase is split across **four C# projects**, layered
Model → Controller → game (with the test project alongside):

- **`src/FourExHex.Model/FourExHex.Model.csproj`** — a plain
  `Microsoft.NET.Sdk` class library with **no GodotSharp reference and
  not a Godot SDK project**, and **no reference to the controller
  layer**. It holds the pure model: state types, the static rule
  classes, the AI subsystem (incl. `AiDispatcher`), the generic
  `UndoStack<T>` + `GameStateSnapshot`, save serialization
  (`SaveSerializer`, `Replay`, `ReplayBeat`, the `Tutorial` POCO),
  `MapGenerator` / `MapEditPaint` / `EditorSnapshot`, and `ProceduralGame`
  (the shared "build a fresh `GameState` from a seed" pipeline that both the
  play scene and the main-menu map thumbnail call — see "New Game setup &
  map thumbnail" below).
- **`src/FourExHex.Controller/FourExHex.Controller.csproj`** — a plain
  `Microsoft.NET.Sdk` class library that `<ProjectReference>`s **only**
  `FourExHex.Model` (one-way). It holds the orchestration layer:
  `GameController` (input + AI scheduling), `GameOperations` (the
  mutation/orchestration helpers — see "GameController ↔ GameOperations
  split" below), and `ReplayRecorder` (the recording + playback
  subsystem — see "GameController ↔ ReplayRecorder split" below); the
  UI-scoped `SessionState` + `SessionStateSnapshot` + `UndoEntry`;
  the top-level `InstantStep` enum (shared between the AI and replay
  instant step machines); the `IHexMapView` / `IHudView` / `IAiPacer`
  view-boundary interfaces; the AI pacers (`AiPacer` / `GodotAiPacer`);
  and the `Tutorial/` Record/Preview scripting helpers (everything in
  `Tutorial/` except the model-side `Tutorial` POCO).
- Because GodotSharp is on neither library's reference graph, model
  and controller code are both *physically incapable* of depending on
  Godot — `using Godot;` anywhere in either fails to compile. And
  because Model has no reference to Controller, model code is
  *physically incapable* of naming `GameController` / `SessionState` /
  the view interfaces — a stray reference fails the build with
  `CS0246`. Both are load-bearing invariants enforced by the compiler,
  not by a hand-maintained file list.
- **`src/FourExHex.ViewMath/FourExHex.ViewMath.csproj`** — a plain
  `Microsoft.NET.Sdk` class library, **no GodotSharp**, one-way
  `<ProjectReference>` to `FourExHex.Model` (for shared primitives
  like `HexCoord`). Holds Godot-free view-side math that legitimately
  needs floating-point precision: `DisplayScaleMath`, `SafeAreaMath`,
  `MapPlacement`, `ZoomMath`, `ScreenLayout`, `HudPanelMath` (floating
  HUD-panel sizing: width clamped to the viewport, height grown to fit
  wrapped text — tutorial box, bankruptcy toast, endgame overlays),
  `KeyboardAvoidance` (panel lift so a focused text field clears the
  mobile on-screen keyboard — see "Mobile keyboard avoidance" below),
  `MultiTouchTapDetector` (3-finger-tap recognition for the debug
  cheat menu — fires once when the third concurrent touch lands,
  re-arms only after all touches lift; no floats, but lives here
  because it's view-input logic that must stay unit-testable),
  `EditorPaletteLayout` (map-editor paint-tool grid wrapping: how many
  columns the brush grid uses so it spills to a 2nd row/column on
  compact phones — issue #45), `ThumbnailLayout` (the "contain" fit that
  sizes the New Game map-thumbnail's offscreen viewport — see "New Game
  setup & map thumbnail" below), and
  the fractional cube-rounding helper `HexRounding.Round(float, float)`. The
  pressure-relief valve for the no-floats rule in Model + Controller
  (see "No floating-point in Model or Controller" below).
- **`FourExHex.csproj`** (`Godot.NET.Sdk`) — the game.
  `<ProjectReference>`s **all three** Godot-free libraries
  (`FourExHex.Model`, `FourExHex.Controller`, `FourExHex.ViewMath`),
  and adds `src/**/*` to `DefaultItemExcludes` (the Godot glob must
  not also compile the moved sources — that would duplicate every
  type; the single `src/**` exclude already covers the
  `src/FourExHex.Controller/` and `src/FourExHex.ViewMath/` subdirs).
  Holds only Godot `Node`/scene/view code that stays in `scripts/`:
  scene roots, `HexMapView`/`HudView`, the editor and tutorial-builder
  panels, `SaveStore` (filesystem), `AudioBus`, `SceneTreeTimerFactory`,
  `HeadlessViews`, and the two view-boundary adapters below.
- **`tests/FourExHex.Tests.csproj`** — `<ProjectReference>`s **all
  three** of `FourExHex.Model`, `FourExHex.Controller`,
  `FourExHex.ViewMath`, with **no GodotSharp and no per-file
  `<Compile Include>` list**. That the suite compiles and passes
  (961+) with zero Godot on its reference graph is the compile-time
  purity proof.

### No floating-point in Model or Controller

`float` and `double` are not deterministic across platforms,
compilers, and JIT levels, so any floating-point on the game-state
code path is a desync time bomb for networked multiplayer (#6).
**Both `FourExHex.Model` and `FourExHex.Controller` are integer-only
assemblies** — no `float`/`double` fields, properties, parameters,
return types, or method-body locals. AI scoring (`AiStateScorer`,
`ComputerAi`), map-generation probability (`MapGenerator`), AI-pacer
timing multipliers (`GodotAiPacer`), and every rule helper use `int`
or `long`. Fractional values are expressed as fixed-point integers
(e.g. percent: `InitialLandPercent = 65`, speed multipliers
`50/100/200` for Fast/Normal/Slow).

The rule is enforced at test time by
`tests/NoFloatsInModelOrControllerTests.cs`. It reflects over the
two assemblies and asserts that no member's signature or method body
mentions `float`/`double` (including via `Nullable<>`, arrays, and
generic args). The test fails `dotnet test` if a floating-point
escape ever lands in either project, listing every offender in one
message so cleanup is straightforward.

The legitimate float math that view code needs — DPI scaling,
safe-area insets, pixel/hex geometry, zoom-level smoothing — lives
in `FourExHex.ViewMath`, which is the "Godot-free, float-allowed"
peer of Model. The game and tests reference all three of Model,
Controller, ViewMath; Model and Controller do not reference ViewMath
(one-way layering, compiler-enforced).

Consequences for the rest of this doc:

- **Player identity is `PlayerId`**, a Godot-free `readonly struct`
  (roster index; `PlayerId.None` == default == "unowned", encodes as
  owner-index `-1`). The model never carries a color; every
  owner/winner/actor field — `HexTile.Owner`, `Player.Id`,
  `Territory.Owner`, `SessionState.Winner`, `PendingDefeatScreen`,
  `PendingClaimVictory`, etc. — is a `PlayerId`.
- **Color is a pure view concern.** `scripts/PlayerPalette.cs` (Godot
  side) maps `PlayerId → Godot.Color` (and back, for old-save loading
  and editor painting) from `GameSettings.PlayerConfig` hex strings.
- **Pixel projection is view-side.** Fractional cube-rounding lives
  in `FourExHex.ViewMath` (`HexRounding.Round(float qFrac, float
  rFrac) -> HexCoord`) — the float→int boundary point that lets
  `HexCoord` itself stay integer-only in `FourExHex.Model`.
  `scripts/HexPixel.cs` (Godot side) owns `ToPixel`/`FromPixel` and
  calls back into `HexRounding.Round`.
- **`Log` is Godot-free** — the master logging system routes through
  an injectable `Log.Sink` that `Main` wires to `GD.Print`. See
  **Logging** below.
- **Save format is v9.** Ownership is a player index on the wire (−1 =
  `None`); claim-victory tiers are persisted by player index
  (palette-independent). v2–v9 still load; v2–v4 migrate their legacy
  color-hex claim data via `GameSettings` palette matching. v6 renamed
  the unit levels (Peasant/Spearman/Knight/Baron →
  Recruit/Soldier/Captain/Commander); pre-v6 level names still load via
  `SaveSerializer.ParseUnitLevel`. v7 added the per-player `Difficulty`
  field (see **Difficulty** below); absent in pre-v7 saves and starting
  maps → defaults to `Soldier` via `SaveSerializer.ParseDifficulty`. v8
  added the optional `CampaignLevel` pointer. v9 added the per-tile
  `IsGold` flag (gold tiles, issue #45); v10 added the per-tile
  `IsMountain` flag (mountain tiles, issue #37); both absent in pre-bump
  saves → `false` (an ordinary tile). All added fields are default-absent, so pre-bump
  files load unchanged.
- **`.cs.uid` sidecars**: the moved model files are not Godot
  resources, so theirs were removed; `src/**` is `.gdignore`d. Files
  still in `scripts/` keep their tracked `.cs.uid`.

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
│      6. Pick pacer: GodotAiPacer (visible delays, scaled by              │
│         UserSettings.SpeedMultiplier) or SynchronousAiPacer             │
│         (diagnostic — runs inline)                                       │
│      7. new GameController(state, session, map, hud,                     │
│           seed: <chosen master seed>,                                    │
│           aiChooser: AiDispatcher.ChooseForCurrentPlayer,                │
│           aiPacer:  pacer,                                               │
│           maxTurnNumber: load ? saved : (diagnostic ? 500 : int.MaxVal), │
│           aiSilentMode: () => !IsReplayMode &&                           │
│             UserSettings.AiSpeed == PlaybackSpeed.Instant,               │
│           replayIsInstantMode: () =>                                     │
│             UserSettings.ReplaySpeed == PlaybackSpeed.Instant)           │
│      8. Wire save/load + pause coordinator:                              │
│           • new SaveStore + (non-diagnostic) build the Save +           │
│             Load dialogs and a shared SettingsPanel.                    │
│           • Subscribe controller.HumanTurnStarted → autosave write,    │
│             passing _originMapName so resumed games keep their map      │
│             identity.                                                   │
│           • Subscribe HUD EscRequested → EnterPause (sets               │
│             GetTree().Paused = true, shows EscMenu with                 │
│             Resume / Save / Load / Settings / Exit options).            │
│           • Subscribe EscMenu.EscapeClosed → ExitPause (Escape-key      │
│             dismissal unpauses; button callbacks manage pause state    │
│             themselves).                                                │
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
│   ├─ injected: master seed, aiChooser delegate, IAiPacer, maxTurnNumber, │
│   │             aiSilentMode (Func<bool>; true → tells the view to mute  │
│   │             per-action AI effects/sounds and lets the controller     │
│   │             skip per-beat highlight/RefreshViews calls),             │
│   │             replayIsInstantMode (Func<bool>; instant replay path)    │
│   ├─ exposes: MasterSeed, StartGame(), Resume(), AbandonGame()           │
│   ├─ events: GameEnded (fires once on natural game-over or turn cap),    │
│   │          HumanTurnStarted (start-of-each human turn — autosave seam) │
│   │                                                                      │
│   ├─ subscribes in ctor:                                                 │
│   │    map.TileClicked              → OnTileClicked                      │
│   │    map.TileLongClicked          → OnTileLongClicked (rally)          │
│   │    hud.BuyRecruitClicked        → OnBuyPressed (U-hotkey: cycle     │
│   │                                    Recruit→Soldier→Captain→Commander→   │
│   │                                    None; no wrap)                    │
│   │    hud.BuyUnitClicked            → OnBuyUnitPressed (per-button     │
│   │                                    radio click: enter that specific │
│   │                                    buy mode; re-click active level   │
│   │                                    toggles it off / cancels)         │
│   │    hud.BuildTowerClicked        → OnBuildTowerPressed                │
│   │    hud.UndoLastClicked          → OnUndoLastPressed                  │
│   │    hud.UndoTurnClicked          → OnUndoTurnPressed                  │
│   │    hud.RedoLastClicked          → OnRedoLastPressed                  │
│   │    hud.RedoAllClicked           → OnRedoAllPressed                   │
│   │    hud.EndTurnClicked           → OnEndTurnPressed                   │
│   │    hud.NextTerritoryClicked     → OnNextTerritoryPressed             │
│   │                                    (Tab: descending-size cycle,      │
│   │                                     capital coord tie-breaker;       │
│   │                                     unvisited-this-turn first, then  │
│   │                                     a fresh round — see #35)         │
│   │    hud.PreviousTerritoryClicked → OnPreviousTerritoryPressed         │
│   │    hud.NextUnitClicked          → OnNextUnitPressed (N: power-order  │
│   │                                    cycle Recruit→Soldier→Captain→   │
│   │                                    Commander, lex within tier;       │
│   │                                    also enters repeated-movement)    │
│   │    hud.PreviousUnitClicked      → OnPreviousUnitPressed (Shift+N:    │
│   │                                    same cycle backward)              │
│   │    hud.CancelActionPressed      → OnCancelActionPressed              │
│   │    hud.DefeatContinueClicked    → OnDefeatContinuePressed            │
│   │    hud.ClaimVictoryWinNowClicked    → OnClaimVictoryWinNowPressed    │
│   │    hud.ClaimVictoryContinueClicked  → OnClaimVictoryContinuePressed  │
│   │   (NewGameClicked / MainMenuClicked / EscRequested are handled       │
│   │    in Main, not here — Main's pause coordinator drives Save /        │
│   │    Load / Settings from the EscMenu's option callbacks)              │
│   │                                                                      │
│   ├─ click policy state machine:                                         │
│   │    OnTileClicked     → pending-mode branch (buy/build/move)          │
│   │                      → SetSelection branch                           │
│   │                      (rejected clicks split: in-range near-miss      │
│   │                       flashes + stays in mode; out-of-range flashes  │
│   │                       + cancels mode + reselects. "In range" for     │
│   │                       buy/move = in own territory or shares a border │
│   │                       with it; for tower = in own territory only)    │
│   │    OnTileLongClicked → rally: free-reposition every unmoved unit     │
│   │                        in the territory toward the long-pressed     │
│   │                        target (single undo step, fires             │
│   │                        PlaySound(Rally)                              │
│   │                        once if any unit moved)                       │
│   │                                                                      │
│   ├─ action handlers:                                                    │
│   │    ExecuteBuyAndPlace → debit gold + MovementRules.PlaceNew          │
│   │                       → if capture: HandleCapture                    │
│   │                       → DispatchActionSound (combine/destroy/place)  │
│   │    ExecuteMove        → MovementRules.Move                           │
│   │                       → if capture: HandleCapture                    │
│   │                       → DispatchActionSound                          │
│   │    ExecuteBuildTower  → debit gold + drop Tower +                   │
│   │                          PlaySound(TowerPlaced)                      │
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
│   │                    before, none after) →                            │
│   │                    PlaySound(PlayerDefeated);                        │
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
│                       → _map.RefreshOccupantVisuals(currentPlayer, tr.)  │
│                       → _hud.SetCta(EndTurn, !hasActionable)            │
│                       → _hud.SetCta(NextTerritory,                       │
│                          isHuman && hasActionable && selExhausted)       │
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
│                           │  │   ├─ ShowHighlight(territory)              │
│   SessionState            │  │   ├─ ShowMoveTargets(coords, level)        │
│   ├─ Winner (PlayerId?)   │  │   ├─ ShowTowerTargets(coords)              │
│   ├─ PendingDefeatScreen  │  │   ├─ ShowTowerCoverage(coords)             │
│   │   (PlayerId? — drives │  │   ├─ ShowMoveSource(coord?)                │
│   │   the defeat overlay) │  │   ├─ CenterOnTerritory(territory)          │
│   ├─ PendingClaimVictory  │  │   ├─ RebuildAfterTerritoryChange()         │
│   │   ((PlayerId,percent)?│  │   ├─ RefreshOccupantVisuals(color, tr.)    │
│   │   — drives the claim- │  │   ├─ PlayDestructionEffect(coord, occ.)    │
│   │   victory overlay;    │  │   ├─ Play{UnitPlaced, TowerPlaced,         │
│   │   percent ∈ {50,75,90}│  │   │    UnitCombined, UnitDestroyed,        │
│   │   — human-only)       │  │   │    TowerDestroyed, TreeCleared,        │
│   ├─ ClaimVictoryPrompted │  │   │    CapitalDestroyed, Bankruptcy,       │
│   │   HighestThreshold    │  │   │    GameWon, Rally, PlayerDefeated}     │
│   │   (Dict<PlayerId,int> │  │   │    — audio sinks routed to AudioBus    │
│   │   — player→top tier   │  │   └─ layers: borders / gold / capitals /  │
│   │   dismissed; persists │  │             units / towers / trees /      │
│   │   across save/load)   │  │             graves / targets / highlight  │
│   ├─ SelectedTerritory    │  │                                            │
│   ├─ Mode (enum)          │  │                                            │
│   ├─ MoveSource           │  │                                            │
│   ├─ VisitedTerritory     │  │                                            │
│   │   Capitals (per-turn  │  │                                            │
│   │   Tab-cycle tour set) │  │                                            │
│   └─ Undo (UndoStack of   │  │                                            │
│      UndoEntry =          │  │                                            │
│      GameStateSnapshot +  │  │                                            │
│      SessionStateSnapshot)│  │                                            │
│                           │  │                                            │
│                           │  │   HudView : CanvasLayer, IHudView          │
│                           │  │   ├─ events: BuyRecruit (U-key cycle) /    │
│                           │  │     BuyUnit(level) (per-button radio       │
│                           │  │     click) / BuildTower / UndoLast /       │
│                           │  │     UndoTurn / RedoLast / RedoAll /        │
│                           │  │     EndTurn / NewGame / MainMenu /         │
│                           │  │     NextTerritory / PreviousTerritory /    │
│                           │  │     NextUnit / PreviousUnit /              │
│                           │  │     CancelAction /                         │
│                           │  │     EscRequested (Options button + ESC) / │
│                           │  │     DefeatContinue /                       │
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
│                           │  │   Buttons are HudIconButton (Button +      │
│                           │  │   _Draw override) painting glyphs via the  │
│                           │  │   shared HudIcons helpers. Static tooltips │
│                           │  │   come from HudIconButton.DefaultTooltip;  │
│                           │  │   Buy/Build override dynamically per state.│
│                           │  │   The Buy row is four always-visible       │
│                           │  │   radio buttons (Recruit / Soldier /      │
│                           │  │   Captain / Commander); per-level Disabled and  │
│                           │  │   Selected mirror BuyModeLevel and         │
│                           │  │   affordability. Disabled-reason tooltips  │
│                           │  │   name the blocker (no selection / no      │
│                           │  │   capital / can't afford <level> (Ng)).    │
│                           │  │   While in a buy or move mode the active   │
│                           │  │   button's tooltip is cleared and the      │
│                           │  │   bottom panel surfaces "Click to place a  │
│                           │  │   X" / "Click to move the X" (gated by an  │
│                           │  │   _externalMessageActive flag so it can't  │
│                           │  │   clobber tutorial step text or the AI-    │
│                           │  │   batch announcement).                     │
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
│                                            ─ split/merge + stomping;     │
│                                              None-owned (neutral)         │
│                                              territories stay capital-    │
│                                              less (throws on a capital    │
│                                              found on neutral land)       │
│   PurchaseRules.CostFor / CanAfford / CanAffordTower / IsValidRecruit…   │
│   MovementRules.ValidTargets / Move / PlaceNew /                         │
│                  ArrivalConsumesAction (capture/tree/grave → true)        │
│   DefenseRules.Defense(coord, grid, territory)                           │
│   TreeRules.RunStartOfTurnGrowth / ConvertGravesToTrees /                │
│             CountIncomeProducingTiles / CountGoldIncomeTiles             │
│   IncomeRules.IncomeFor (base tiles + GoldTileBonus per gold tile)       │
│   UpkeepRules.UpkeepFor / TotalUpkeepFor / ApplyUpkeep / ApplyUpkeepFor  │
│               / ForecastBankruptNextTurn / Classify -> EconomyOutlook    │
│                          (Healthy / NegativeDelta / BankruptNextTurn)    │
│               / SurvivesNextUpkeep(gold, netIncome) — shared solvency    │
│                          primitive used by AI scorer + enumerator        │
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
│   HexTile — Coord, Owner, Occupant, IsGold, IsMountain (pure model)      │
│   HexOccupant (abstract)                                                 │
│     ├─ Unit — Owner, Level, HasMovedThisTurn                             │
│     ├─ Capital — marker                                                  │
│     ├─ Tower — marker (defense, no upkeep)                               │
│     ├─ Tree — marker (blocks income; movement onto a tree consumes the   │
│     │         action and clears the tile)                                │
│     └─ Grave — marker (blocks income; converts to a Tree at the start    │
│                of the owning player's next turn)                         │
│   UnitLevel — Recruit=1, Soldier=2, Captain=3, Commander=4                   │
│   Territory — Owner, Coords, Capital (immutable)                         │
│   TerritoryExtensions — BuildTileIndex                                   │
│   Player — Name, Id, Kind (PlayerKind), IsAi                             │
│   PlayerKind — Human, Computer                                           │
│   TurnState — Players[], CurrentPlayerIndex, TurnNumber                  │
│   Treasury — Dictionary<HexCoord, int>; CollectIncomeFor;                │
│              ReconcileAfterCapture (forfeits enemy gold on capture)      │
│   GameStateSnapshot — deep-copy (tiles + gold + territories)             │
│   SessionStateSnapshot — selection anchor + Mode + MoveSource +          │
│                          RepeatedMovement flag + visited capitals        │
│                          (sorted; hand-written sequence equality)        │
│   UndoEntry — pair of (GameStateSnapshot, SessionStateSnapshot)          │
│   UndoStack<T> — two-sided history of T (UndoEntry for play, also reused │
│                  by the editor with EditorSnapshot)                      │
│   TerritoryLookup — FindContaining / FindOwnedContaining /              │
│                     FindByCapital / OwnedCapitalBearing helpers         │
│   MapGenerator — CA land/water carve + tree scatter + optional neutral   │
│                  mountain-range / gold-cluster passes (MapGenOptions)     │
│   GameSettings — global PlayerConfig (name, color hex) + PlayerKinds     │
│                  + Difficulties (per-slot) + optional MasterSeed;        │
│                  written by MainMenuScene, read by Main                   │
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
│   UserSettings — static class; SfxEnabled / VfxEnabled / AiSpeed /       │
│                  ReplaySpeed preferences persisted to                    │
│                  user://settings.json (lazy load, atomic tmp+rename      │
│                  save); read by AudioBus + HexMapView + GodotAiPacer +   │
│                  GameController, written by SettingsPanel. AiSpeed and   │
│                  ReplaySpeed are two independent settings of one         │
│                  shared enum PlaybackSpeed {Slow,Normal,Fast,Instant}    │
│                  (member order is load-bearing — settings persist        │
│                  numerically). SpeedMultiplier(PlaybackSpeed) → 2/1/0.5  │
│                  for Slow/Normal/Fast; Instant has NO arm: it routes     │
│                  to the chunked frame-yielded driver via the pacer's     │
│                  ScheduleUnscaled (multiplier never consulted).          │
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
│   HexMapView.PlaySound(SoundEffect, HexCoord?) is the single sound      │
│   sink the controller calls — a switch on the SoundEffect enum forwards │
│   to the matching AudioBus.Play* method. The interface lets controllers │
│   fire audio without knowing about the autoload, and lets               │
│   HeadlessHexMapView (test/diagnostic) stub it out with a single no-op. │
│                                                                          │
│   Each AudioBus.Play* method early-returns when                          │
│   UserSettings.SfxEnabled is false — a single chokepoint that gates     │
│   both gameplay sounds and AttachClick-wired UI clicks. Destruction VFX │
│   (HexMapView.PlayDestructionEffect: flash + shockwave + shards) gates  │
│   on UserSettings.VfxEnabled. Pulse / shrink / grow-in animations are   │
│   always on — they communicate game state and disabling them would     │
│   hurt readability.                                                     │
│                                                                          │
│   HexMapView also carries a _silentMode flag (toggled by                 │
│   GameController via IHexMapView.SetSilentMode when an AI player runs   │
│   under PlaybackSpeed.Instant, OR for a ReplaySpeed.Instant             │
│   fast-forward — RefreshSilentMode ORs in _replayInstantActive so a    │
│   turn boundary can't un-silence it). A second gate inside PlaySound   │
│   that drops every per-action cue AND the tree/grave grow/shrink tweens │
│   in RefreshOccupantVisuals AND the tree/grave teardown inside          │
│   RebuildAfterTerritoryChange (per-capture teardown would flash trees   │
│   off-and-on as captures fire mid-batch; the end-of-batch refresh's    │
│   diff loop frees only the trees actually chopped).                     │
│   Every PlaySound cue — including SoundEffect.Bankruptcy and            │
│   SoundEffect.GameWon — obeys the silent gate with NO exceptions, so a  │
│   silent AI-Instant batch or an instant replay is a fully silent        │
│   fast-forward. A human still hears their own bankruptcy / game-won     │
│   because a human-controlled turn is never silent (the flag is set      │
│   only while an AI acts under Instant, or across an instant replay).    │
│   The same all-cues policy is mirrored in MockHexMapView so             │
│   integration tests can verify end-to-end silence.                      │
└──────────────────────────────────────────────────────────────────────────┘
```

## Gold tiles (issue #45)

A **gold tile** is an income hotspot that pays its controlling player double
the per-turn income of an ordinary tile (2 gp vs 1). Implemented as a single
per-tile attribute that threads through every layer:

- **Model.** `HexTile.IsGold` (bool, defaults false) — a terrain attribute
  orthogonal to `Owner` and `Occupant`: a gold tile can be owned by any player
  or neutral and hold any occupant.
- **Income.** The 2× bonus lives in the single income chokepoint
  `IncomeRules.IncomeFor` = `TreeRules.CountIncomeProducingTiles` +
  `CountGoldIncomeTiles · IncomeRules.GoldTileBonus` (bonus = 1, the one knob
  to retune the gold earn rate). A gold tile under a `Tree`/`Grave` pays
  nothing — it's excluded from both counts, same as any dead-ground tile. Real
  play (`Treasury.CollectIncomeFor`) and the AI lookahead
  (`AiStateScorer`) both route through `IncomeFor`, so the AI values gold tiles
  for free. The starting-gold seed (`SeedStartingGold`, 5×tile-count) is
  deliberately NOT boosted — gold affects recurring income only.
- **Persistence + undo.** Carried as `TileDto.IsGold` (save format v9),
  through replay-initial snapshots (`GameStateSnapshot.EnumerateTiles`), and in
  both deep-copy snapshots (`GameStateSnapshot` / `EditorSnapshot`).
- **Authoring.** Gold tiles are placed via the map editor — a toggle
  brush (`MapEditPaint.PaintGoldToggle`, palette glyph `HexPaletteIcon.Gold`)
  that flips `IsGold` without disturbing owner/occupant, with the same
  drag-stroke add/erase locking as the tree/tower brushes — **and**
  procedurally by `MapGenerator` when `MapGenOptions.IncludeGold` is set (see
  "Procedural mountains & gold" below). Generated gold is sparse **neutral**
  clusters.
- **Rendering.** `HexMapView`'s `GoldBordersLayer` (a `TriangleSoup` batch)
  draws an inset gold hex-ring band per gold tile, layered above the territory
  borders but below all occupants so it coexists with any player color and any
  tree/tower/unit/capital. Drawn as filled mitered quads (one per edge, sharing
  corner vertices) rather than a multiline stroke so the corners have no gaps.
- **Responsive palette.** Adding a 5th paint brush (gold) overflowed the
  editor palette on compact phones, so `_paintCluster` became a `GridContainer`
  whose column count comes from `EditorPaletteLayout.PaintColumns` (ViewMath,
  unit-tested): it stays one line on roomy screens and wraps to a 2nd row
  (portrait) / column (landscape) on compact. The portrait bottom bar grows and
  the landscape left rail widens (via the new `HudBars.MakeRail` `width` param +
  the `OrientationHud.LeftRailWidth` virtual hook) to fit the wrapped grid.

## Mountain tiles (issues #37, #47)

A **mountain tile** is high ground: it gives **no defense on its own**, but a
unit or tower standing on it gains a **+1 bonus that radiates** to friendly
neighbors. Capturable without being destroyed; an *empty* mountain is
defenseless. Like gold it is a single per-tile attribute threaded through every
layer, but defensive rather than economic — the two flags are independent (a
tile can be a gold mountain).

- **Model.** `HexTile.IsMountain` (bool, defaults false) — orthogonal to
  `Owner`, `Occupant`, and `IsGold`. A mountain can be neutral or owned by any
  player and is **passable**: units move onto, through, and die on it. It has
  no income behavior of its own (a controlled mountain pays the ordinary 1 gp;
  a gold mountain still pays the gold bonus).
- **Defense.** `DefenseRules.Defense` gives a `Unit` or `Tower` on a mountain
  `DefenseRules.MountainBonus` (+1) on top of its contribution (folded in by the
  private `ContributionAt` helper); an **empty mountain contributes nothing**.
  The boosted value radiates to same-territory neighbors like any other
  defender, so a Soldier/Tower on a mountain protects at 3 and a Commander at 5.
  Contributions are still `max`, not cumulative. Because empty mountains no
  longer defend, an empty neutral mountain is capturable by any level (even a
  Recruit), while a defended one raises the capture threshold by 1.
  `BlockingDefenders` mirrors this (via the same `ContributionAt`) for the
  view's red-flash. Capture (`MovementRules.ResolveArrival`) transfers ownership
  but leaves `IsMountain` set — the terrain persists, so the new owner's
  occupant earns the bonus; a `Log.LogCategory.Capture` line records it.
- **Rule guards.** Trees never spread onto mountains
  (`TreeRules.RunStartOfTurnGrowth`); **towers may be built on mountains**
  (`PurchaseRules.IsValidTowerLocation` — the +1 is the point), with a
  `Log.LogCategory.Capture` `[tower] placed on mountain` line on the human
  build path; a unit that dies on a mountain leaves **no grave**
  (`UpkeepRules.ApplyUpkeep` empties the tile instead).
- **No-capital edge case.** Capitals are never placed on mountains
  (`CapitalPlacer.Choose` skips them). A connected same-owner region made
  **entirely** of mountains therefore has no legal capital site, so
  `CapitalReconciler` leaves it capital-less — it is *not* a territory in the
  economic sense (no treasury/income/upkeep, skipped by the AI), exactly like
  the existing singleton / neutral capital-less regions, yet it still renders
  in the owner's color and still radiates its mountain defense. Every
  territory consumer already guards on `HasCapital`, so this needed no new
  branch beyond the `CapitalPlacer` skip. A `Turn` log marks the case.
- **Persistence + undo.** Carried as `TileDto.IsMountain` (save format **v10**;
  absent in pre-v10 saves → false), through replay-initial snapshots
  (`GameStateSnapshot.EnumerateTiles`), and in both deep-copy snapshots
  (`GameStateSnapshot` / `EditorSnapshot`).
- **Authoring.** Placed **only via the map editor** — a toggle brush
  (`MapEditPaint.PaintMountainToggle`, palette glyph `HexPaletteIcon.Mountain`)
  with the same drag-stroke add/erase locking as the tree/tower/gold brushes.
  Mountain and **tree** are mutually exclusive (painting one clears the other);
  a **tower coexists** with a mountain (#47 — neither brush clears the other);
  the capital brush refuses a mountain tile and vice-versa; gold is
  independent. Mountains are **also** generated procedurally by `MapGenerator`
  when `MapGenOptions.IncludeMountains` is set (see "Procedural mountains &
  gold" below); generated ranges are **neutral**.
- **Editor undo/sound for flag paints.** Mountain and gold paints leave the
  territory partition untouched, so the editor's old "territory-list reference
  changed" heuristic missed them. The undo push now compares the pre-stroke
  snapshot against the live grid via `EditorSnapshot.DiffersFromGrid` (a pure,
  unit-tested grid diff over owner/occupant/gold/mountain/water), and the
  per-cell placement sound additionally checks the painted tile's gold/mountain
  flags. Both flag brushes now record undo and play the sound.
- **Rendering.** `HexMapView`'s `MountainsLayer` (`Node2D`) draws one
  `Polygon2D` peak glyph per mountain tile (`CreateMountainVisual`): a
  translucent dark-tinted outlined peak (no snow cap) so the tile's owner color
  shows through, layered above the gold borders but below occupants so a
  unit/capital on the tile draws on top. Counter-rotated by `ApplyGlyphUpright`
  to stay upright on a rotated board. The silhouette is shared with the editor
  brush button (issue #52): the geometry — `HudIcons.MountainPeakVerts` — is the
  single source of truth both consume, so the two shapes can't drift. They
  differ only in color: the tile uses `HudIcons.MountainFill` (translucent) +
  `MountainStroke` (BgDeep); the immediate-mode button (`HudIcons.DrawMountain`,
  a squared slate `HexPaletteButton`) draws the same peak in opaque grey so it
  reads against the dark slate backdrop.

## Procedural mountains & gold (issue #48)

`MapGenerator.BuildInitialGrid` can scatter mountains and gold onto a
freshly-generated map, each gated by a flag on the `MapGenOptions` record
(`MapGenOptions(IncludeMountains, IncludeGold)`, both default **off**). The
record threads through `BuildInitialGrid(... , MapGenOptions? options = null)`
and `ProceduralGame.Build(... , options)`; the no-options overload (tests,
replay) and both-off are **byte-identical to the pre-#48 baseline** — each pass
is fully gated so a disabled pass makes zero RNG draws, preserving the #20
determinism reference. All scatter math is integer (no floats — Model rule) and
deterministic in the seed.

- **Mountains** — `ScatterMountainRanges`: a biased random-walk "ridge agent"
  per range (pick a hex direction, walk mostly-straight with occasional ±1
  veers, dropping an occasional perpendicular foothill → 1–2-wide ranges, not
  speckle), to ~9% of land. `MarkMountain` sets `IsMountain`, **forfeits
  ownership (`PlayerId.None`)**, and clears any tree.
- **Gold** — `ScatterGoldClusters` (runs after mountains, before the tree
  scatter): sparse (~3% of land) small **neutral** clusters (a seed tile grown
  into a 2–4-tile blob). When mountains were also generated, cluster seeds are
  biased (~55%) toward mountain tiles, so gold tends to co-locate / overlap with
  ranges (a valid gold-on-mountain — the flags are independent). `MarkGold` sets
  `IsGold` + `PlayerId.None`.
- Generated mountains and gold are **neutral terrain players must capture**
  (a neutral gold tile pays nobody until owned). They flow through
  `TerritoryFinder` / `CapitalReconciler` as capital-less neutral regions;
  `CapitalPlacer` already skips neutral and mountain tiles, so no capital lands
  on them. The tree scatter skips mountain and gold tiles so both stay readable.
- **Surfacing.** A shared `MapGenSettingsPanel` (Godot modal, opened by a serif
  "?" chip — `HudIconButton` text mode) carries the two toggles, summoned from
  both the New Game map-setup page and the map editor (next to the die). It
  reads/writes the process-wide `GameSettings.IncludeMountains` /
  `IncludeGold`; `Main`, the map thumbnail, and the editor die build their
  `MapGenOptions` from those for **freeform** games. The square gold-✓ toggle
  rows come from the shared `UiToggle` helper (also used by `SettingsPanel`).
- **Campaign terrain is per-level, not the freeform toggles.**
  `CampaignProgress.MapGenOptionsForLevel(level)` derives a level's mountains/gold
  mix deterministically from the level number (≈55% / ≈45%, independent), so a
  level's terrain is fixed and reproducible regardless of UI state. `Main` uses
  it whenever `GameSettings.CampaignLevel` is set (freeform falls back to the
  toggles), and the campaign confirm-sheet preview renders the same derivation
  via `MapThumbnailView.RequestRandom(seed, options)`.

## Display scaling (autoload)

`DisplayScale` — autoload-registered Node (`project.godot` `[autoload]` entry
"DisplayScale", ordered after `LogBootstrap` so `Log` is wired). Keeps on-screen
UI at a roughly constant *physical* size across resolutions/densities (the
motivating case: HUD buttons too small to tap on high-DPI phones). It reads the
active screen's DPI and drives the root `Window.ContentScaleFactor`:

- The pure clamp math lives in the Godot-free model assembly —
  `DisplayScaleMath.FactorForDpi(logicalDpi, minFactor)` =
  `clamp(logicalDpi / 160, max(minFactor, 1.0), 3.0)` (160 = Android mdpi
  baseline; floored at the caller-supplied `minFactor`, never below `MinFactor`
  = 1.0 so design size is the minimum; capped at 3.0). It's unit-tested; the
  autoload is the thin Godot adapter that reads `DisplayServer.ScreenGetDpi` /
  `ScreenGetScale` and applies the result.
- **Logical DPI, not raw DPI.** Platforms like macOS render in OS-scaled logical
  points, so the adapter divides raw DPI by `ScreenGetScale` before applying the
  baseline — a 2× retina Mac (256 dpi ÷ 2 = 128 logical) floors to factor 1.0 and
  is unchanged. Android *also* reports a non-1.0 `ScreenGetScale` that **varies by
  orientation** (verified on a Galaxy S9: 1.35 portrait / 1.8 landscape), so the
  *natural* factor differs by orientation — ≈ 2.22 portrait / 1.67 landscape on
  the S9. See `RELEASE.md` §5 for the device data.
- **Per-platform mobile formula** (the same naïve `logicalDpi / 160` formula
  mis-counted iOS's retina pixel multiplier — `ScreenGetScale = 3` on iPhone is
  the retina factor, not a system density choice — so iPhone factor floored
  to 1.0 and got lifted by the floor to 2.22, ending up physically smaller than
  the S9 at the same logical factor):
  - **iOS:** `DisplayScaleMath.FactorForRawMobileDpi(rawDpi, MobileMinFactor)`
    = `clamp(rawDpi / MobileReferenceDpi, MobileMinFactor, 3.0)`, where
    `MobileReferenceDpi = 180` is reverse-engineered from S9 FHD+ portrait
    at the shipped 2.22 factor (401 raw / 2.22 ≈ 180). iPhone 13 mini's
    raw DPI 476 → factor 2.64, matching S9's physical button size.
  - **Android (and other mobile):** the legacy `FactorForDpi(logicalDpi,
    MobileMinFactor)` path. Android's `ScreenGetScale` represents a system
    density bucket (xxhdpi etc.), so dividing by it IS correct there.
    S9 portrait (logicalDpi ≈ 355) lands at 2.22 naturally; S9 landscape
    (≈1.67 natural) lifts to the `MobileMinFactor = 2.22` floor.
  - **Desktop:** non-mobile, unchanged — `FactorForDpi(logicalDpi)` floors to
    1.0 so design size is preserved. The mobile floor doesn't apply.
- **Why a unified mobile floor still exists.** `MobileMinFactor = 2.2222` is the
  safety net for low-density Android phones (and a sanity floor on iOS even
  though iPhones we test all clear it). Without it, a 160-DPI Android phone
  would compute factor 1.0 and render unusably small buttons.
- **Local repro / override.** `DisplayScale.Apply()` honors a
  `FOUREXHEX_UI_SCALE` env var that bypasses the DPI computation and forces a
  specific factor on any platform (takes precedence over the mobile floor).
  Used to reproduce a device's pixel-for-pixel layout on the dev Mac without
  shipping to the device — see RELEASE.md §6 Option B.
- **Why it just works with the existing HUD.** `ContentScaleFactor` doesn't only
  enlarge 2D content — it also sets the GUI's logical layout size to
  `window / factor`. So `GetViewport().GetVisibleRect().Size` (read by
  `OrientationHud` / `HexMapView` for orientation + layout) returns the *logical*
  size, and the anchor-based HUD reflows correctly with no per-widget changes,
  even with stretch mode left `disabled`. Set once at startup and re-applied on
  `SizeChanged` (rotation / monitor move), with an equality guard against the
  resize feedback loop.
- **Consequence for narrow viewports.** Scaling up shrinks the logical canvas
  (a high-density portrait phone lands near ~400–500 logical px wide). Centered
  fixed-width HUD panels therefore cap their width to the viewport
  (`HudView.PositionTutorialOverlay` / `PositionBankruptToast`, shared
  `HudPanelSideMargin`). The win/defeat/claim overlays are container-based
  (eyebrow + DM Serif title + gold rule + an `HFlowContainer` button row that
  wraps to a second line when too narrow), built by a shared
  `HudView.BuildEndgameOverlay`; `HudView.PositionEndgameOverlays` clamps each
  panel's width to `min(designW, viewport − 2·HudPanelSideMargin)` and re-runs
  on `OnViewportMetricsChanged`. The shared modals (`SettingsPanel`,
  `CreditsPanel`) keep a single-column layout in both orientations and
  scale-to-fit: `FitPanel` applies a uniform `Control.Scale` (clamped ≤ 1) so
  the whole panel shrinks to the safe viewport on a short landscape instead of
  scrolling or clipping — the same shrink-to-fit as `MainMenuScene.ScaleToFit`.
  (CreditsPanel keeps its own inner `ScrollContainer` for the long blurb; its
  body label is `MouseFilter = Pass` so a touch-drag reaches the scroll.) Issue
  #17.
- **Mobile keyboard avoidance (Map Seed field, issue #4).** The main menu's
  seed `LineEdit` is the one mobile text input. While it has focus,
  `MainMenuScene` polls `DisplayServer.VirtualKeyboardGetHeight()` per frame
  (`SetProcess` gated on `FocusEntered`/`FocusExited` — the keyboard animates
  in and Godot has no height-changed signal) and translates the
  center-anchored play-config panel up via its anchor offsets by
  `KeyboardAvoidance.LiftFor(fieldBottomY, viewportHeight,
  keyboardPhysicalHeight ÷ ContentScaleFactor, margin)` (ViewMath, unit-tested;
  the field's unlifted bottom is measured by adding back the applied lift so
  the lift never feeds back into its own input). `ScaleToFit` only touches
  `Scale`/`PivotOffset`, so the two never fight. The field sets
  `SelectAllOnFocus` (tap replaces the old seed); on mobile, Return releases
  focus (dismisses the keyboard, stays on the config screen) instead of
  starting the game — desktop keeps Enter-starts-game; a press outside the
  focused field also releases focus (handled in `_Input`, not consumed,
  because the root Control's `MouseFilter.Stop` keeps outside taps from ever
  reaching `_UnhandledInput`). `FOUREXHEX_FAKE_KB=<physical px>` fakes a
  keyboard height on desktop and forces the mobile Return branch so the whole
  flow is testable on the dev Mac. Instrumented under `Display:Debug`
  (focus/lift transitions) and `Input:Debug` (Return / tap-outside dismissal).

## Safe-area handling (autoload)

`SafeArea` — peer autoload to `DisplayScale` (`project.godot` `[autoload]`
entry "SafeArea", ordered after `DisplayScale` so `ContentScaleFactor` is
settled before insets are computed). Keeps HUD chrome out of the iOS notch /
Dynamic Island / home-indicator zones on devices that have them.

- The pure math lives in the Godot-free model assembly —
  `SafeAreaMath.InsetsFor(physicalWindow, physicalSafeRect, contentScaleFactor)`
  returns a `LogicalSafeInsets(Top, Bottom, Left, Right)` record by clamping the
  gap between safe rect and window to ≥ 0 and dividing by the scale factor.
  Unit-tested in `tests/SafeAreaMathTests.cs`; the autoload is the thin Godot
  adapter that reads `DisplayServer.GetDisplaySafeArea` and applies the result.
- **Mobile-only gate.** On non-mobile (`!OS.HasFeature("mobile")`) the autoload
  returns `LogicalSafeInsets.Zero` regardless of what Godot reports, because
  `GetDisplaySafeArea` on desktop reports the *screen* safe area (e.g.
  excluding the macOS menu bar) in screen — not window — coordinates, which
  isn't a useful inset for a sub-screen window. Desktops have no notch /
  home indicator to compensate for, so Zero is correct and identical to the
  pre-safe-area layout. Android with cutouts benefits from the same code
  path as iOS.
- **Bar overlaps iOS chrome (map reclaims safe-inset space).**
  `HudBars.MakeBarPanel` builds a bar of exactly `height` logical px (no
  safe-inset growth). The bar stays anchored to the viewport edge, so on a
  notched device the bar's top edge (top bar) or bottom edge (bottom bar)
  visually sits *under* the notch / home indicator — the iOS chrome carves
  into the bar's slate fill, not into the map. `MakeBarFrame` is a plain
  symmetric 8-px chrome inset; it no longer takes safe-area parameters.
  `ComputeInsets` returns just `barHeight` (no `+ safe.Bottom` fold), so the
  map reclaims the safe-inset vertical space that was previously reserved
  for the bar's extension. The pre-existing `topOffset` / `bottomOffset`
  params keep their structural-inset meaning (tutorial builder hosting the
  editor HUD below its own topbar). The same "no safe-area fold" rule applies
  to overlay positioning helpers (`PositionTutorialOverlay`,
  `PositionBankruptToast`, the seed label drop position).
- **Notch-aware widget tweaks for top-bar elements that risk being clipped.**
  On `SafeArea.Current.Top > 0` (iOS portrait), the gameplay-HUD top bar
  drops the frame's 8-px bottom chrome inset (`topFrame.OffsetBottom = 0f`)
  and bottom-aligns the gold chip
  (`_goldChip.SizeFlagsVertical = ShrinkEnd`) so its body sits flush with
  the bar's bottom edge — below the notch overhang. Same treatment in
  `MapEditorHudView.BuildPortraitBars` for the seed pill + die in the top
  bar's center cluster. On non-notched devices both elements stay
  `ShrinkCenter` and the symmetric chrome inset is preserved.
- **Re-layout on inset change.** `OrientationHud` subscribes to
  `SafeArea.Changed` and triggers an `ApplyLayout` + `PublishInsets` pass
  when the OS reports a different safe rect (e.g. status-bar show/hide,
  rotation crossing the notch axis). The `hasTopNotch` conditional above
  re-evaluates on each rebuild. The shared modals (`SettingsPanel`,
  `CreditsPanel`) likewise subscribe to `SafeArea.Changed` and
  `GetViewport().SizeChanged`, re-running `FitPanel` so their scale-to-fit
  stays inside the safe viewport (they read `SafeArea.Current` for the
  top/bottom/left/right insets); both unsubscribe in `_ExitTree`.

## GameController ↔ GameOperations split

The CONTROLLER box above predates a `GameController` → `GameController` +
`GameOperations` split. The mutation/orchestration core (anything that
both live AI and replay playback need) was extracted into
`src/FourExHex.Controller/GameOperations.cs` so a future
`ReplayRecorder` extraction won't create a circular dependency. Method
ownership today:

- **`GameOperations`** owns the mutation and turn-lifecycle helpers
  that both live AI and replay drive into:
  - Per-action execute helpers — `ExecuteAiMove`, `ExecuteAiBuyUnit`,
    `ExecuteAiBuyCombine`, `ExecuteAiBuildTower`, `ApplyLongPressRally`
  - Capture aftermath — `HandleCapture` (+ private
    `SnapshotCapitals` / `ColorsWithCapital` / `LogCaptureDiff`),
    `DispatchActionSound`, `DeclareWinner`
  - Turn transitions — `ReseedRngForCurrentTurn` (+ static `MixSeed`),
    `EndOfTurnProcessing` (+ private `LogGameEndDiagnostics`),
    `AdvanceToNextActivePlayer`, `StartPlayerTurn` (+ static
    `ResetMovementFor`, private `LogTurnStart`)
  - Game-end signaling — `CheckGameEndConditions` (fires `GameEnded`
    via the `onGameEnded` ctor callback; controller still owns the
    public event)
  - View sync — `RefreshViews`, `InvokeAfterRefresh`, private
    `HasAnyActionableForCurrentPlayer`
  - Silent-mode coordination — `RefreshSilentMode`, `InSilentAiBatch`
  - Small helpers — `WasFriendlyUnitAt`
  - Mutable shared state — `Rng` (read-only getter), `GameEndedFired`,
    `HumanTurnFiredForCurrentTurn`, `SuppressMapRebuild` (public
    properties; written by the controller's instant driver / replay
    reset paths)

- **`GameController`** retains the input + scheduling surface:
  - All `IHexMapView` / `IHudView` event handlers (`OnTileClicked`,
    `OnEndTurnPressed`, the Undo/Redo handlers, etc.) and the
    `TrackHandler` wrapper
  - Human execute helpers (`ExecuteMove`, `ExecuteBuyAndPlace`,
    `ExecuteBuildTower`, `RebindSelectionToContaining`) — these don't
    participate in replay and stay alongside the input dispatcher
  - AI step machine — `StepAiPreview` / `StepAiExecute` /
    `ApplyAiActionCore` / `EndCurrentAiPlayerTurnCore` /
    `ScheduleAiTurn` / `RunAiTurnsUntilHumanOrDone`
  - Replay step machine — `StepReplayPreview` / `StepReplayExecute` /
    `ExecuteReplayBeat` / `ReplayApplyEndTurn` / `BeginReplay` /
    `EndReplay` / `ClearUndoAndReplayBookkeeping`
  - Instant driver — `RunInstantTick`, `InstantAiTick` /
    `AiInstantStep`, `InstantReplayTick` / `ReplayInstantStep`
  - `RecordBeat` and undo/redo bookkeeping (`_undoBeatCounts`,
    `_redoBeatLists`, `_pendingHumanBeat`)
  - Public surface — `StartGame`, `Resume`, `AbandonGame`,
    `BeginReplay`, the four `*ForTutorial` methods,
    `RecordTutorialOnlyBeat`, the readonly replay-state properties,
    and the `GameEnded` / `HumanTurnStarted` events

Construction: `GameController`'s constructor builds the `GameOperations`
instance and passes in callbacks for the things `GameOperations` can't
own (the public events, `ClearUndoAndReplayBookkeeping`, `_replayMode`,
`_replayInstantActive`). After that, `GameController` calls into the
operations through `_ops.X(...)`. The reverse edge is constrained to
those callbacks; `GameOperations` does not name `GameController`.

`AbandonGame`'s unsubscribe behaviour, the click policy state machine,
and the `RefreshViews` invariant are unchanged — the split is a
re-homing of methods, not a behaviour change. Existing tests pin the
boundary (984/984 green throughout the extraction).

## GameController ↔ ReplayRecorder split

A second extraction lifted the replay subsystem out of `GameController`
into `src/FourExHex.Controller/ReplayRecorder.cs`. Same one-way layering
as the GameOperations split: `ReplayRecorder → GameOperations` for every
mutation; the recorder does not reference `GameController`. The
recorder owns recording, paced playback, and the instant-step function.

### What lives on ReplayRecorder

- **Recording state**: `_replayBeats`, `_initialSnapshot`,
  `_initialTurnNumber`, `_initialCurrentPlayerIndex`,
  `_replayDataIsCompleteFromStart`, `_replayMode`, `_replayIndex`,
  `_replayInstantActive`, `_undoBeatCounts`, `_redoBeatLists`,
  `_replayIsInstantMode`.
- **Recording methods**: `RecordBeat`, `RecordTutorialOnlyBeat`,
  `CaptureInitialSnapshot`.
- **Undo/redo coordinator**: the session undo stack and the parallel
  beat stacks must move in lockstep (one beat batch per undo entry),
  and the recorder owns both sides as single atomic operations —
  `CommitHumanHandler(pre, beatsBefore)` (push session entry + stamp
  pre-handler beat count + clear the redo stash), `UndoOneStep` /
  `RedoOneStep` (pop/restore one beat batch + the matching session
  pop, returning the restored `UndoEntry` for the caller to apply),
  and `ClearUndoAndBookkeeping` (drop both sides; the beat log itself
  is committed history). The single-side steps are private, so a
  caller cannot half-do the pairing. Every operation ends with an
  always-on `ValidateBeatStacksInSync` that throws (with all four
  counts) on divergence — crash at the cause instead of silently
  trimming the wrong tail of the replay log. Pinned by
  `UndoReplayBeatSyncTests` (depth equality after every step of
  scripted + 400-op random-stress flows, via the read-only
  `UndoBatchDepth` / `RedoBatchDepth` properties) and by
  `ReplayPlaybackTests.Replay_AfterUndoRedoChurn_ProducesSameFinalState`
  (the beat log stays replay-faithful through undo/redo churn).
  Instrumented under `Log.LogCategory.Undo`.
- **Playback methods**: `BeginReplay`, `EndReplay`,
  `StepReplayPreview`, `StepReplayExecute`, `ExecuteReplayBeat`,
  `ReplayApplyEndTurn`, `ReplayInstantStep` (the step function consumed
  by `RunInstantTick`), `ScheduleNextReplayBeat(turnBoundary)` (the
  re-dispatching scheduler — replay's mirror of `ScheduleAiTurn`: it
  re-reads `_replayIsInstantMode` each beat so a mid-replay Replay-Speed
  change switches the paced↔instant track, drives `SetSilentMode`
  directly, and forces the structural rebuild on an instant→paced
  transition; called by `StepReplayExecute` and by `RunInstantTick`'s
  `reschedule` callback for instant replay), private
  `ResolveReplayActingTerritory`.
- **Public read surface** (consumed by `Main.cs` and `RecordPane.cs` via
  thin forwarders on `GameController`): `Beats`, `BeatsCount`,
  `InitialSnapshot`, `InitialTurnNumber`, `InitialCurrentPlayerIndex`,
  `IsCompleteFromStart`, `HasInitialSnapshot`, `IsReplaying`,
  `IsInstantModeActive`, plus the bookkeeping depths `UndoBatchDepth` /
  `RedoBatchDepth` (forwarded as `UndoBeatBatchDepth` /
  `RedoBeatBatchDepth`; consumed by the sync tests).

### What stays on GameController

- All input event handlers and the `TrackHandler` wrapper. The
  per-handler `_pendingHumanBeat` buffer stays alongside the handlers;
  `TrackHandler` post-body calls `_recorder.CommitHumanHandler(pre,
  beatsBefore)` (the atomic session-push + beat-bookkeeping commit —
  see the undo/redo coordinator above) and `_recorder.RecordBeat(...)`.
- AI step machine (`StepAiPreview` / `StepAiExecute` /
  `ApplyAiActionCore` / `EndCurrentAiPlayerTurnCore` / `ScheduleAiTurn`
  / `RunAiTurnsUntilHumanOrDone` / `InstantAiTick` / `AiInstantStep` /
  `EndInstantAiBatch`).
- `RunInstantTick` (shared chunked driver for AI + replay instant)
  and the `InstantReplayTick` one-line wrapper that targets it for
  replay (the step + finish are on the recorder).
- The `InstantStep` enum (`Continued` / `TurnBoundary` / `Exhausted`)
  was lifted out of `GameController` as a top-level type in the
  Controller assembly so both the AI step and the recorder's
  `ReplayInstantStep` can return it.
- Undo/redo input handlers (`OnUndoLastPressed`, etc.) — gating,
  `ApplySnapshot`, and view centering only; the stack mechanics are
  one `_recorder.UndoOneStep` / `RedoOneStep` call per step.
- `ClearUndoAndReplayBookkeeping()` — one-line forwarder to
  `_recorder.ClearUndoAndBookkeeping()` (kept on `GameController` as
  the ctor callback target for `GameOperations`).
- Public events (`GameEnded`, `HumanTurnStarted`).
- Public API forwarders to the recorder: `BeginReplay`,
  `RecordTutorialOnlyBeat`, `ReplayBeats`, `InitialReplaySnapshot`,
  `InitialReplayTurnNumber`, `InitialReplayCurrentPlayerIndex`,
  `ReplayDataIsCompleteFromStart`, `IsReplayMode`.

### Construction

`GameController`'s constructor creates `_ops` first, then `_recorder`.
`GameOperations`' `isReplayMode` and `isReplayInstantActive` predicates
are closures over the `_recorder` field; they read
`_recorder?.IsReplaying ?? false` / `_recorder?.IsInstantModeActive ??
false` so the static analyzer is satisfied and the predicates are safe
to invoke at any later time. The recorder is constructed with refs to
`_state`, `_session`, `_map`, `_ops`, `_aiPacer`, the
`replayIsInstantMode` predicate from `Main`, the `InstantReplayTick`
entry callback (which the recorder schedules into
`_aiPacer.ScheduleUnscaled` for instant playback), and `loadedReplay`
(for save-load bootstrap of `_initialSnapshot` + `_replayBeats`).

## Key contracts

**`IHexMapView`** — everything the controller asks the map to do:

```csharp
event Action<HexTile?>? TileClicked;          // fires only for in-grid clicks
event Action<HexTile?>? TileLongClicked;      // rally
event Action<HexCoord>? OffGridClicked;       // water / map-edge clicks; carries
                                              // the raw coord so the controller
                                              // can anchor rejection feedback
void ShowMoveTargets(IEnumerable<HexCoord> coords, UnitLevel level);
void ShowTowerTargets(IEnumerable<HexCoord> coords);
void ShowTowerCoverage(IEnumerable<HexCoord> coords);
void ShowMoveSource(HexCoord? coord);
void ShowHighlight(Territory? selected);
void CenterOnTerritory(Territory territory);
void RebuildAfterTerritoryChange();
void RefreshOccupantVisuals(PlayerId? currentPlayer, Treasury treasury);
void PlayDestructionEffect(HexCoord coord, HexOccupant destroyed);

// Rejection feedback (forbidden-slash on target + animated arrows
// from each blocking defender; defended-clang or generic-thunk sound).
void FlashRejection(HexCoord target, RejectionShape shape, IEnumerable<HexCoord> blockingDefenders);

// Audio sink — forwarded to AudioBus. The SoundEffect enum
// (UnitPlaced, TowerPlaced, UnitCombined, UnitDestroyed,
// TowerDestroyed, TreeCleared, CapitalDestroyed, Bankruptcy, GameWon,
// Rally, PlayerDefeated) picks which cue. The optional coord is
// reserved for a future positional implementation. ALL cues
// (including Bankruptcy and GameWon) drop while the view is in
// silent mode — a silent AI-Instant batch or an instant replay is
// fully silent. A human still hears their own bankruptcy / game-won
// because a human-controlled turn is never silent.
void PlaySound(SoundEffect kind, HexCoord? at = null);
```

`HexMapView._UnhandledInput` routes a left-click to exactly one of
the three click events: an in-grid hit fires `TileClicked(tile)`; an
off-grid coord (water, render-only water rim, past the map) fires
`OffGridClicked(coord)`; a long-press fires `TileLongClicked` instead
of either. The split means the controller never receives
`TileClicked(null)` from real input, so it can give rejection
feedback anchored to the raw coord on water clicks instead of falling
into the legacy "click outside grid → deselect" branch.

`FlashRejection` is the single sink for rejected-click feedback. The
view draws the forbidden-slash overlay (the unit/tower silhouette in
red, with a black-outlined red circle + diagonal slash on top so the
"no" symbol stays legible on red tiles), animates a black arrow from
each blocking defender to the target, and plays either
`AudioBus.PlayRejectDefended()` or `PlayRejectGeneric()` depending on
whether the defender set is non-empty. Overlays live in a persistent
`_rejectionsLayer` that `RefreshOccupantVisuals` does not clear, so
mid-pulse tweens survive subsequent refreshes; each ghost / arrow
`QueueFree`s itself on its tween's `Finished` signal. The audio
assets are `assets/audio/reject_generic.wav` (soft wooden thunk) and
`assets/audio/reject_defended.wav` (metallic sword-on-shield clang).

**Invalid-tap policy (flash then cancel).** A tap on an invalid target
while a buy / build-tower / move action is pending no longer keeps the
mode alive for re-aiming. The controller flashes the rejection feedback
above, then calls `CancelPendingAction()` (clears `Mode` / `MoveSource`
+ the preview overlays, same as Escape) and — for in-grid taps — falls
through to the normal-selection block so the tap is re-processed as a
fresh click (tapping your own other territory switches selection / picks
up its unmoved unit; tapping enemy/empty/off-grid deselects). Off-grid
(water / map-edge) taps cancel then deselect. This applies in both
`OnTileClickedBody` and `OnOffGridClickedBody`.

`ShowMoveTargets` takes the unit level so the preview can render at
the correct visual size (recruit=1 ring, soldier=2, captain=3,
commander=3+dot). Audio is fired from the controller right after the
mutation that produced it; `DispatchActionSound` picks one cue per
move/buy resolution (combine > destruction-by-type > generic place).

**Unit visual language.** A placed unit reads as one of three states,
all set in `RefreshOccupantVisuals` and `ShowMoveSource`:

- **Actionable** — current player's unit with `!HasMovedThisTurn`:
  white rings + scale pulse (`PulseAmplitude` / `PulseRate` in
  `HexMapView`).
- **Selected** — the picked-up move-source, a strict subset of
  actionable: white rings (unchanged), pulse suppressed, and a
  tile-sized black hex backdrop inserted underneath the rings in
  `_unitsLayer` so the rings sit on jet black instead of the
  territory's player color. Built by `ApplySelectionAffordance`, torn
  down by `ClearSelectionAffordance`. The single field
  `_selectionBackdrop` tracks the live backdrop node; the next
  `RefreshOccupantVisuals` re-runs `ApplySelectionAffordance` after
  the units layer is rebuilt, so the backdrop survives a refresh
  while a selection is live.
- **Idle** (everything else — opponent unit, current player's unit
  that has already moved this turn, or any unit between turns):
  black rings, no pulse, no backdrop.

`IsActionableUnit(HexCoord)` is the shared predicate. It reads
`_currentPlayer` (cached by `RefreshOccupantVisuals`) so
`ShowMoveSource` can decide whether to re-add a just-deselected coord
to `_pulsingUnits` without the controller passing the player in
again.

**`IHudView`** — everything the controller asks the HUD to do:

```csharp
event Action? BuyRecruitClicked;       // U-hotkey: cycle through
                                       // affordable levels
                                       // (Recruit→Soldier→Captain→Commander),
                                       // exit at top instead of wrap
event Action<UnitLevel>? BuyUnitClicked;// per-button radio click: enter
                                       // that specific buy mode directly
                                       // (toggle — re-clicking the active
                                       // button cancels the mode, like Esc;
                                       // clicking a different level switches)
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
                                       // can't afford a recruit);
                                       // no-op if none qualify
event Action? PreviousTerritoryClicked;// Shift+Tab hotkey equivalent;
                                       // same skip rules
event Action? NextUnitClicked;         // N hotkey: cycle units in selection
                                       // by (Level, HexCoord) — Recruits,
                                       // then Soldiers, then Captains,
                                       // then Commanders, lex within
                                       // each tier; wraps. First press
                                       // (and every successful pick) also
                                       // turns on
                                       // SessionState.RepeatedMovement.
event Action? PreviousUnitClicked;     // Shift+N hotkey — same cycle
                                       // walked backward
event Action? CancelActionPressed;     // Escape hotkey while a Buy/
                                       // Build/Move action is pending
event Action? EscRequested;            // Options button OR Escape with
                                       // no pending action; handled in
                                       // Main → EnterPause → EscMenu
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
// The CtaButton enum (BuyRecruit, EndTurn, BuildTower,
// ClaimVictoryWinNow, ClaimVictoryContinue, DefeatContinue,
// NextTerritory) picks the target. The pulse flag governs animation:
// game-side calls set steady (pulse: false) — EndTurn when the human
// is out of moves, NextTerritory when the human has an actionable
// territory to jump to but their current selection is exhausted (or
// they have no selection); Tutorial Preview's scripted beats pulse
// (pulse: true) — a looping Tween on Modulate.a (1.0 ↔ 0.55). The
// four claim/defeat/build CTAs are Tutorial-Preview-only and default
// to pulse: true.
void SetCta(CtaButton button, bool isCta, bool pulse = true);

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

- Per-beat step instructions ("Press the Buy Recruit button.",
  "Move the selected Recruit onto the target Soldier to combine
  them into a Captain.", "Place the Recruit at the highlighted tile
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

**HUD icon layer.** Both the play HUD and the map-editor HUD render
their action buttons through a shared `HudIconButton : Button` that
overrides `_Draw` to paint a programmatic glyph. Glyph helpers live
in the static `HudIcons` class — `DrawUnit` (1/2/3 rings + Commander
dot, mirroring `HexMapView`'s in-map unit visuals), `DrawTower`,
`DrawTree`, `DrawCapital`, `DrawHand` (all reused by
`HexPaletteButton`), `DrawCurvedArrow` (single + nested-concentric
doubled variants for Undo Last / Undo All / Redo Last / Redo All),
`DrawEndTurnTriangle`, `DrawGear`. The two "next ..." buttons
(`DrawNextUnit`, `DrawNextTerritory`) share an arrow-above-symbol
composition via the private `DrawNextArrow` helper: a horizontal
math-vector arrow (line + filled triangular arrowhead, same
construction as `DrawSingleCurvedArrow`'s arrowhead and sized to
match the doubled-undo outer arrow — `headLen = 0.468r`,
`headHalf = 0.255r`) at the top of the button, the per-button
symbol (Recruit ring vs gold capital star, both at their original
full size and shifted down `0.20r`) below it. Stroke-only glyphs
(recruit ring, undo/redo arrows, the next-arrow line, End Turn
triangle) paint white on the dark HUD bar and flip to black via
`HudIconButton.CtaActive` while the End Turn CTA stylebox is on
(the bg goes white during pulse).

The play HUD's right-side controls cluster orders
`NextUnit → NextTerritory → EndTurn (→ Options in landscape)`.
`NextUnit` fires the same `NextUnitClicked` event as the N hotkey;
its `Selected` mirrors `SessionState.RepeatedMovement` (gated on
the button also being enabled — a disabled button never shows the
white `Selected` ring), and its `Disabled` flag mirrors
`MovementRules.HasUnmovedUnitsOwnedBy` on the selected territory —
greyed out with the disabled-reason tooltip "No unmoved units to
cycle" when there's nothing to walk.

Static tooltips ("`<label> — <hotkey>`") are owned by
`HudIconButton.DefaultTooltip(HudIcon)` — a single source of truth
the play HUD, map editor, and `HudView.Refresh`'s dynamic
fallback all consume. The four Buy buttons and Build Tower
override the tooltip live in `Refresh` to show "Buy `<level>`
(Ng) — U" / "Build Tower (15g) — T" when enabled, or the
*reason they're disabled* ("No territory selected", "Selected
territory has no capital", "Selected territory can't afford a
captain (30g)"). Buy and Build are always visible — the
disabled-with-reason tooltip replaces the old visibility toggle
so the layout doesn't shift. The Turn and Gold text labels plus
the player-swatch bar have fixed/reserved `CustomMinimumSize.X`
(the swatch bar reserves every slot at the enlarged width so the
highlight can move without changing width) so the buttons after
them never reflow.

The Buy row is four always-visible radio buttons (Recruit /
Soldier / Captain / Commander) packed in a nested `HBoxContainer`.
Each `HudIconButton` carries a fixed `BuyLevel`; `Selected`
mirrors `SessionState.BuyModeLevel` so exactly one is highlighted
at a time. Clicking a button fires `IHudView.BuyUnitClicked(level)`
for direct entry into that mode; re-clicking the already-active level
toggles the mode back off (cancel), while clicking a different level
switches. The U hotkey fires `BuyRecruitClicked` which
`GameController.OnBuyPressed` resolves as a cycle through affordable
levels, *exiting at the top* (the most-expensive affordable level
cycles back to `ActionMode.None` instead of wrapping to Recruit).
Build Tower stays a single button, and re-clicking it while already in
BuildingTower toggles that mode off too.

While the player is in a buy or move mode, the active button's
tooltip is cleared and the bottom-anchored tutorial-message panel
surfaces "Click to place a `<level>`" / "Click to move the
`<level>`". `HudView` tracks an `_externalMessageActive` flag set
by `ShowTutorialMessage` / `ShowTappableTutorialMessage` and
cleared by `HideTutorialMessage`; the action-hint pass in
`Refresh` only writes to the panel when that flag is false, so
tutorial step text and the AI-batch "Opponents are taking their
turns…" announcement always win over the generic placement hint.

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

`GodotAiPacer` additionally takes an optional `Func<float>`
`delayMultiplier` (`Main` wires
`() => IsReplayMode ? SpeedMultiplier(ReplaySpeed) : SpeedMultiplier(AiSpeed)`).
Read on every `Schedule` call so a mid-game speed change takes effect
on the next beat — Slow doubles delays, Fast halves them, Normal
passes through. **Instant is not a multiplier**: it routes to the
chunked frame-yielded driver (`InstantAiTick` / `InstantReplayTick`,
see "Instant fast-forward" below) which schedules via the second
method, `ScheduleUnscaled` — a frame-yielded callback whose delay
bypasses the multiplier entirely. Both methods share `Cancel`'s
generation guard via one private `ScheduleTimer` helper; nothing runs
inline (the old multiplier-0 FIFO trampoline and `_inlineQueue` were
removed — the chunked driver owns stack depth by returning between
ticks). `SynchronousAiPacer` drains both methods inline (tests +
diagnostic). `AbandonGame` / `BeginReplay` call `Cancel` so an
in-flight tick can't fire against disposed nodes.

```csharp
void Schedule(Action callback, int delayMs);          // multiplier-scaled
void ScheduleUnscaled(Action callback, int delayMs);  // exact, frame-yielded
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
  unit-testable with mocks (see the `tests/GameControllerTests.*.cs`
  partial-class domain files; the shared `TestGame` fixture lives in
  `tests/GameControllerTests.cs`).
- **Every state change funnels through `RefreshViews()`** at the end
  of the handler. One path, no drift.
- **Snapshots capture `GameState` plus the player-intent slice of
  `SessionState`** (`SelectedTerritory` anchor, `Mode`, `MoveSource`,
  `RepeatedMovement` flag, `VisitedTerritoryCapitals`) via `UndoEntry`
  (a `(GameStateSnapshot, SessionStateSnapshot)` pair). `Winner`,
  `PendingDefeatScreen`, and the `Undo` stack itself stay out.
  Top-level human event handlers are wrapped in `TrackHandler`, which
  captures pre-state, runs the body, and pushes one `UndoEntry` iff
  state actually changed — automatic de-dup of no-op clicks (the
  visited set is compared by sorted-sequence equality in
  `SessionStateSnapshot.Equals`, not reference, to keep that de-dup
  honest). Exceptions inside a handler propagate without pushing.
- **Visited-territory cycling (#35)**: `SessionState.
  VisitedTerritoryCapitals` records the capital of every territory the
  human selects (Tab / Shift+Tab / click / post-capture rebind) this
  turn. `StepTerritorySelection` re-sorts by descending size on every
  press — and size mutates as the player acts — so the walk alone
  can't guarantee a fair tour; pass 1 stops only on actionable
  *unvisited* territories, and when all are toured, pass 2 resets the
  set and starts a fresh round (each round visits every actionable
  territory at most once before any repeat). The set clears on
  `EndTurnNow` and round-trips through `SessionStateSnapshot` so undo
  rewinds visits along with the selection. AI turns never touch it
  (AI executes via `GameOperations.ExecuteAi*`, not `SetSelection`).
- **Repeated-movement** is a sticky bit on `SessionState` that drives
  the N-hotkey's auto-advance behaviour. `StepUnitSelection` turns it
  on whenever it successfully picks a different unit. While on, the
  tail of `ExecuteMove` calls `AutoAdvanceAfterMove(level, source,
  destination)`: power-then-coord sort of remaining movables in the
  (possibly capture-rebound) selected territory, with the destination
  coord excluded (so an in-territory reposition — which leaves
  `HasMovedThisTurn=false` — doesn't re-pick the same unit at its new
  spot). The flag clears on Esc/cancel, entry into any non-None
  `ActionMode` (buy or build), a user-click selection change to a
  different territory, a long-press rally (treated as a deliberate
  override of the passive sticky intent — buy/build/non-chained
  MovingUnit pending intents are still protected by rally's own
  `Mode != None` guard), End Turn, game-over (any path through
  `GameOperations.DeclareWinner` — claim-victory WinNow, capture of
  the last enemy capital, turn-cap domination — clears pending action +
  flag + map overlays so the win overlay isn't undercut by a stale
  "Click to place a ..." action hint), or an auto-advance that finds
  no remaining movables. `ClearPendingAction` deliberately does NOT
  clear it — `ExecuteMove`'s `FinishPendingAction` must run with the
  flag alive so the auto-advance hook can read it. Round-trips
  through `SessionStateSnapshot` so a single Undo rewinds both the
  move and the auto-advance back to the prior `MoveSource`. Capture-
  rebind preserves the flag (the user didn't manually re-select).
- **`HexTile` is a pure model — no view coupling.** `HexTile.Owner`
  is plain state; it does NOT push into a `Polygon2D` (the old
  setter side-effect + `HexTile.Visual` were removed). The view owns
  the tile→fill map (`HexMapView._tileVisuals`) and resyncs every
  fill from `_state` inside `RebuildAfterTerritoryChange()` — the
  single coalesced repaint path. This is why an instant fast-forward
  no longer leaks per-action recolors: model captures mutate
  `tile.Owner` with zero view effect; the screen only catches up when
  the driver calls `RebuildAfterTerritoryChange` (once per turn /
  at batch end).
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
5. **Apply upkeep** — `UpkeepRules.ApplyUpkeepFor`. Per-unit costs
   come from the owner's difficulty via `DifficultyRules.UnitUpkeep`
   (the Soldier baseline — the default for every slot — is Recruit 2,
   Soldier 6, Captain 18, Commander 54; see **Difficulty** below). A
   territory that can't pay total upkeep goes bankrupt: every unit in
   it becomes a `Grave`, remaining gold stays. `PlaySound(Bankruptcy)`
   fires once if any territory of this player went bankrupt
   (player-scoped, not tile-scoped).
6. **Fire `HumanTurnStarted`** if the now-current player is human and
   the game isn't over. Save/load wires the autosave path here.

The income → upkeep ordering matters: it lets the same turn's income
subsidize that turn's upkeep before bankruptcy is checked.

### Bankruptcy warning surfaces

The upkeep step above wipes every unit in a territory that can't pay;
without warning, the human only sees it after it lands. The forecast
pipeline that surfaces it ahead of time:

- **Pure rule (`UpkeepRules.Classify`)** — returns one of three
  `EconomyOutlook` values for a given territory:
  - `BankruptNextTurn` — `gold + income < upkeep` (every unit will die
    at the owner's next turn-start).
  - `NegativeDelta` — `income < upkeep` but reserves still cover next
    turn (bleeding down toward eventual bankruptcy).
  - `Healthy` — otherwise; also returned when there is no capital or
    no upkeep (no label is ever shown anyway).
  Mirrors the real start-of-turn sequence (income then `ApplyUpkeep`,
  bankrupt iff `available < owed`). Does not model start-of-turn tree
  growth or intervening captures. `ForecastBankruptNextTurn` is the
  same predicate exposed as a single bit for callers that only need
  it (HUD panel text, `AiStateScorer`).
- **HUD label (`HudView.Refresh`)** — colors `_goldLabel` red on
  `BankruptNextTurn`, yellow on `NegativeDelta`, clears the override
  otherwise. Only painted when the selected territory is human-owned;
  AI territories never tint the label.
- **Tap-summoned alert notice (`HudView._bankruptToast`)** — a
  dedicated pill anchored 16 px below the HUD bar, top-center, built
  once in `BuildBankruptToast` and hosting **both** the red
  `BankruptNextTurn` and yellow `NegativeDelta` variants of the
  warning. Driven by a private `_summonedAlertCoord: HexCoord?` field:
  the panel is visible iff that field is set. The controller's
  `OnTileClicked` path summons by calling `IHudView.SummonCapitalAlertNotice(coord, outlook)`
  when the tap lands on the current human player's own capital and
  `UpkeepRules.Classify(...)` returns a non-`Healthy` outlook; tapping
  the same capital again is a toggle-off. Every other top-level human
  handler (TrackHandler-wrapped or otherwise) calls
  `DismissCapitalAlertNotice()` at entry so the notice clears on Buy /
  Build / End Turn / Undo / Redo / Cancel / Defeat / Claim-Victory etc.
  `Refresh` also stale-guards: if the summoned coord no longer resolves
  to a human capital with the originally-summoned outlook (e.g., the
  capital was captured or recovered), it dismisses. Visibility is
  **never** driven by Refresh itself — purely tap-summoned.
  Red palette: dark-red bg (oklch 0.30 0.10 25 ≈ #4a2620) at 92 %
  alpha, 1 px brighter-red border; title "Bankrupt next turn" over
  subtitle "All units in this territory will die". Yellow palette:
  dark-olive bg `(0.290, 0.260, 0.110, 0.92)` with `BoardPalette.WarnYellow`
  border; title "Losing gold" over subtitle "This territory spends
  more than it earns each turn". Both share the 8 px radius, Geist
  24/21 px ink/ink-mute typography, and the `TriangleWarningBadge`
  glyph (red+white for BankruptNextTurn, yellow+black for
  NegativeDelta via `SetVariant`). State lives on `IHudView`
  (`SummonedCapitalAlertCoord` / `SummonCapitalAlertNotice` /
  `DismissCapitalAlertNotice`) — view-layer only, never reflected in
  `GameState` or `SessionState`, so summon/dismiss never push undo
  entries (the old auto-show toast had this property by accident; the
  new design makes it explicit). Logging through
  `Log.LogCategory.Hud` (`[AlertNotice] summon …` / `dismiss …`).
- **Map badge (`HexMapView.RedrawWarningBadges`)** — a top-most
  `WarningBadgesLayer` (drawn above units, capitals, and the highlight
  border) holds warning-sign triangles stamped on the capital of every
  affected territory belonging to the current player: red triangle with
  white border + exclamation for `BankruptNextTurn`; yellow with black
  for `NegativeDelta`. Runs every `RefreshOccupantVisuals`, clears the
  layer, returns immediately if `state.Turns.CurrentPlayer.IsAi`, and
  otherwise iterates `state.Territories`. AI players never get badges,
  ever — the layer is empty for the duration of any AI turn. Selection
  is irrelevant; every affected current-player territory is flagged.
- **Instrumentation** — when the HUD warning path fires it emits
  `Log.Debug(Log.LogCategory.Turn, "[economy] …")` with the gold /
  income / upkeep numbers, for `FOUREXHEX_LOG="Turn:Debug"`
  verification.

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
it fires `PlaySound(GameWon)` iff the winner is human.

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
after has been eliminated by this capture: `PlaySound(PlayerDefeated)`
fires; if the eliminated color is human,
`SessionState.PendingDefeatScreen` is set so the HUD shows a defeat
overlay. The AI loop pauses at the next `StepAiExecute` while the
overlay is up so the human can read the result before play resumes.
`OnDefeatContinuePressed` clears the flag and re-arms the pacer.

### Rotation

`AdvanceToNextActivePlayer()` calls `TurnState.EndTurn()` (which
increments `TurnNumber` on wrap) then loops while
`WinConditionRules.IsEliminated(currentPlayer.Id, grid)` is true.
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

## Difficulty (a per-player economic handicap)

Difficulty (issue #11) is an **economic handicap on whoever owns it**,
stored per slot and selectable per Human row on the New Game panel
(issue #38); everything defaults to the `Soldier` baseline. Levels are
named after the unit ranks — `Recruit` (easiest) … `Commander`
(hardest) — and the one-sentence mechanism is: *higher difficulty
makes that player's own units cost more to buy and to keep.* Computer
slots always play `Soldier`: handicapping an AI inverts the framing
(raising its level *weakens* it — calibration: a Commander AI scores
0/10 where a Recruit AI scores 3/10 vs the ~1.7 null) and a
handicapped AI doesn't adapt its strategy — it buys to the solvency
edge and doom-spirals into bankruptcies — so the UI locks AI rows to
the baseline (the model still supports per-slot AI levels for the
`FOUREXHEX_DIFFICULTY` diagnostics). Income is **never**
difficulty-scaled (an earn-rate lever was implemented, measured, and
removed: land-proportional bonuses compound and proved knife-edged to
tune, where upkeep/cost engage from turn 1 and scale with army size).

All tuning lives in `DifficultyRules` (Model) as hand-picked integer
tables — retuning a level is a one-table edit:

| your difficulty | unit upkeep/turn (per unit tier) | unit cost (`UnitBaseCost` × tier 1–4) | tower |
|---|---|---|---|
| Recruit   | 1 / 4 / 13 / 40 | 8 / 16 / 24 / 32  | 12 |
| Soldier   | 2 / 6 / 18 / 54 | 10 / 20 / 30 / 40 | 15 |
| Captain   | 3 / 8 / 23 / 68 | 13 / 26 / 39 / 52 | 18 |
| Commander | 3 / 9 / 27 / 81 | 15 / 30 / 45 / 60 | 20 |

- **Plumbing.** `Difficulty` is a per-player field (`Player.Difficulty`,
  default `Soldier`), populated by `Player.BuildRoster` from
  `GameSettings.Difficulties`. The New Game panel gives every player
  row its own Recruit/Soldier/Captain/Commander dropdown; on a
  Computer row it's pinned to Soldier and disabled, and flipping a
  row Human→Computer resets any other level to Soldier (the reset
  sticks — flipping back doesn't restore it; see
  `MainMenuScene.ApplyDifficultyLock`). `OnStartPressed` writes each
  row's choice straight into `GameSettings.Difficulties[i]`. The
  Type/Difficulty dropdowns live on the **player-setup page** of the
  paged New Game flow (see "New Game setup & map thumbnail" below):
  landscape lays each player out as one row (swatch | name | role |
  difficulty) under column headers; portrait uses a two-line block
  (swatch + name, then role and difficulty side-by-side). A viewport
  resize that flips `ScreenLayout.Resolve` rebuilds the panel in place,
  round-tripping the dropdown selections through the `GameSettings`
  arrays. Rows initialize from `GameSettings.Difficulties`, so loaded
  saves / Play Again reflect each slot's level.
- **Lockstep invariant.** `UpkeepRules` and `PurchaseRules` take a
  `Difficulty` parameter with **no default**, so the compiler surfaces
  every consumer. Real charging (`ApplyUpkeepFor` uses
  `player.Difficulty`; controller buy paths use the current player's),
  the AI solvency gates (`AiCommon.EconomyBefore` + per-unit deltas),
  `AiSimulator`'s deductions, `AiStateScorer`, and the HUD economy
  label / buy-button prices all derive from the same tables — an
  all-Soldier game is byte-identical before/after the subsystem's
  refactors, which is the regression check used when touching it.
- **Persistence.** Saved per player in save format v7 (`PlayerDto.
  Difficulty`); missing (pre-v7 saves, starting maps) defaults to
  `Soldier`. Load paths mirror it into `GameSettings.Difficulties`
  before `BuildRoster`, so resumes and replays reproduce.
- **Diagnostics.** `FOUREXHEX_DIFFICULTY="recruit,…,commander"` sets
  per-slot levels in the 6AI harness (a non-Soldier AI slot is
  *handicapped* and should underperform). The `GameController`
  constructor logs a one-shot `difficulties: Red=…, …` line
  (`Turn:Info`) whenever any slot is non-Soldier.

## New Game setup & map thumbnail (issues #40, #5)

The New Game flow in `MainMenuScene` is **two paged screens** toggled by
`_playConfigPage` (`PlayerSetup` / `MapSetup`); both page contents are built
up front and their visibility flipped (so selections survive paging), with
`Enter`/`Esc` and Back/Next wired per page (`GoToMapPage` / `GoToPlayerPage`).
The **player-setup** page holds the six role + difficulty rows; the
**map-setup** page holds the map selector, the seed field + a **re-roll die**
button (`HudIconButton(HudIcon.Die)`, modeled on the map editor's — #5), and a
live board thumbnail.

- **Fill-to-cap surface (both orientations).** Both the portrait and landscape
  panels are now the same centered `LandscapeMenuChrome` surface that *fills*
  the safe area up to a cap, sized by the single `ApplyPlayConfigLayout` path
  (shared by `FitPanels`, the `SafeArea.Changed` hook, and the keyboard-lift
  path). Landscape caps at `920×520`; portrait at the 90° transpose `520×920`.
  This replaced the old fixed `624×1100` portrait panel that was uniformly
  `ScaleToFit`-shrunk — which left the portrait dialog smaller than landscape
  on a phone. The pages are container-based (`VBox`/`HBox`/`ScrollContainer`),
  so they fill the surface; portrait players use a two-line block (swatch +
  name, then the dropdowns side-by-side) to fit the narrow width, and the
  player lists carry no `ScrollContainer` (six 40-px rows fit both caps).

- **Live thumbnail = offscreen `HexMapView` snapshot.** `scripts/
  MapThumbnailView.cs` renders the *real* `HexMapView` into a hidden
  `SubViewport` and snapshots it to a static `ImageTexture` shown in a
  `TextureRect` — pixel-identical to what Start Game produces, but the heavy
  view only renders on change. `RequestRandom(seed)` builds the board via the
  shared `ProceduralGame.Build` (the same pipeline `Main` uses, so the preview
  can't drift from the real game); `RequestMap(name)` loads a map-editor map
  via `SaveStore.LoadMap(name).State` (full `GameState`, so neutral / gold /
  mountain tiles preview); `RequestSlot(name)` loads an in-progress save via
  `SaveStore.LoadSlot(name).State` (the Load Game dialog, #55). Requests are
  coalesced by a token so rapid seed typing only snapshots the latest. Refreshed
  on re-roll, seed change, and map selection; instrumented under `Display:Debug`.

- **Stable, sharp, oriented framing.** The `SubViewport` is sized to the
  *nominal grid* aspect (seed-independent, via `ThumbnailLayout.FitInside`),
  and `HexMapView.FrameWholeGrid` frames the whole grid rectangle (not the
  per-seed land box) so re-rolling a seed keeps the board at a fixed scale /
  position. A **portrait** menu gives the viewport a tall aspect, which makes
  `HexMapView` rotate the board −90° to match the in-game portrait map. The
  viewport renders at the displayed size × the window `ContentScaleFactor`
  (DisplayScale) × a 3× **supersample**, clamped to ~1600 px, then downsamples
  through a mip-mapped `TextureRect` — SSAA standing in for the 2D MSAA the
  GLES3 compatibility renderer lacks. The top hex-tessellation row is cropped
  off the snapshot for a clean straight top edge.

- **Campaign reuse (issue #51).** Tapping a level on the campaign ladder opens
  `CampaignConfirmSheet` — a confirm dialog (`MainMenuScene.OnCampaignLevelTapped`)
  that embeds the same `MapThumbnailView`, previewing the level's board via
  `RequestRandom(CampaignProgress.SeedForLevel(level))` (campaign maps are
  procedural: level N = seed N, the exact board `Main` launches). The sheet
  reuses the `LandscapeMenuChrome` fill-to-cap surface, so it fills a phone (big,
  legible preview) but caps to the New Game dialog footprint on desktop; portrait
  is a single centered column and landscape mirrors the map-config page's
  rail-beside-thumbnail. `Escape` cancels the sheet and, on the ladder itself,
  backs out to the landing menu.

- **Load Game preview (issue #55).** `SlotPickerDialog` (the modal shared by
  main-menu / in-game Load Game, map-editor Load Map, tutorial-builder Load
  Tutorial) has two bodies, chosen per-open by `ShowSlots`'s optional
  `thumbnailStore`. **Text-only** (editor / tutorial hosts, no store) keeps the
  small fixed centered modal of click-to-load buttons, scale-to-fit on a narrow
  viewport. **Preview** (the two game-save hosts pass `_saveStore`) switches to a
  `LandscapeMenuChrome` fill-to-cap surface mirroring the map-config page: a
  selectable slot list (toggle buttons in a `ButtonGroup`) beside one large
  `MapThumbnailView` of the selected save (`RequestSlot`), plus Cancel / Load.
  Like the New Game page it has distinct portrait (list-above-preview) and
  landscape (list-rail | preview) layouts, rebuilt on an orientation flip and
  capped at `520×920` / `920×520`. Selecting a slot re-points the single preview;
  the preview render is deferred one frame so it sizes against its laid-out rect.
  A missing/corrupt save degrades to a blank preview (the row stays loadable) via
  `MapThumbnailView`'s existing log-and-bail.

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
              ├─ MovementRules.Move → dst.Owner = attacker; dst.Occupant = unit
              │                      → unit.HasMovedThisTurn = true
              ├─ if WasCapture:
              │     ├─ HandleCapture(...)
              │     │     ├─ state.Territories = TerritoryFinder.Recompute(
              │     │     │       state.Grid, prev, state.Treasury)
              │     │     │     (= FindAll + CapitalReconciler.Reconcile +
              │     │     │       Treasury.ReconcileAfterCapture; enemy gold
              │     │     │       on captured capital tiles is forfeited)
              │     │     ├─ if a color lost its last capital:
              │     │     │     PlaySound(PlayerDefeated); for human, set PendingDefeatScreen
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

### Click → rejection feedback

```
HexMapView → TileClicked(tile)  OR  OffGridClicked(coord)
GameController  ── wrapped in TrackHandler:
  pre = CaptureCurrentSnapshot()
  └─ body (one of):
        OnTileClickedBody(tile)  — in-grid click
          ├─ session.Mode == BuyingX/MovingUnit/BuildingTower
          ├─ rule check fails (IsValidTarget / IsValidTowerTarget)
          └─ EmitRejection(level, tile.Coord) → return  // STAY in mode
        OnOffGridClickedBody(coord)  — water / off-grid click
          ├─ session.Mode != None
          └─ EmitRejection(level, coord) → return       // STAY in mode
                (no mode → SetSelection(null) instead, preserving the
                 long-standing "click outside to deselect" UX)
  EmitRejection(level, coord):
    ├─ targetTerritory = TerritoryLookup.FindContaining(state.Territories, coord)
    ├─ inFrontier = coord is in or neighbors SelectedTerritory.Coords
    ├─ defenders = (inFrontier && targetTerritory is enemy's)
    │     ? DefenseRules.BlockingDefenders(coord, level, grid, targetTerritory)
    │     : []
    │   // "too far" wins over "defended": a non-adjacent click never
    │   // reports defenders, even if the far hex happens to be defended.
    └─ _map.FlashRejection(coord, shape, defenders)
          ├─ forbidden-slash overlay at target (silhouette + red circle/slash,
          │   black-outlined, two-pulse fade over ~1.3 s)
          ├─ for each defender ≠ target: black arrow defender→target
          │   (grow 0.4 s → hold 0.18 s → fade 0.32 s, then QueueFree)
          └─ defenders.Any() ? PlayRejectDefended() : PlayRejectGeneric()
  // TrackHandler: no mutation, no undo push.
```

`DefenseRules.BlockingDefenders` is the static helper backing the
defender set: it walks the target tile itself plus every adjacent
same-territory tile and yields every coord whose `ContributionOf`
meets or exceeds the attacker level. Mirrors the iteration in
`Defense(...)` but collects coords instead of taking a max.

Rejected clicks deliberately keep the player in their pending mode
(buy / move / build-tower), preserve `SelectedTerritory`, keep
`MoveSource` set, and leave move-target / tower-target / tower-coverage
previews onscreen — so the next click is just another attempt without
re-pressing Buy or re-picking up the unit.

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
        ├─ if anyMoved: _handlerMutatedGame = true; PlaySound(Rally);
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

### AI turn

`RunAiTurnsUntilHumanOrDone` resets the per-player AI bookkeeping and
calls `ScheduleAiTurn(turnBoundary)` — the single **re-dispatching**
decision point that picks the pacing path *every* beat. It re-reads
`aiSilentMode()` on each call: under `PlaybackSpeed.Instant` it
schedules the chunked `InstantAiTick` via `ScheduleUnscaled` (delay
`InstantTurnDelayMs`/0); otherwise the paced `StepAiPreview` via the
multiplier-scaled `Schedule` (delay `AiBetweenPlayersDelayMs`/
`AiActionDelayMs`). Because *all* continuation points route through it
— the next-AI-player hop, the post-execute hop (`StepAiExecute`), the
instant driver's own reschedule (`RunInstantTick`'s `reschedule`
callback), and the overlay-resume sites (`OnDefeatContinuePressed`,
claim-victory continue → `EndTurnNow`) — a mid-turn Ai-Speed change
**switches tracks at the next beat**. The one exception is the
preview→execute hop (`StepAiPreview` → `StepAiExecute`), which stays a
direct `Schedule`: `_pendingAiAction` is already chosen there, so a
track switch would re-draw RNG for it; the switch lands at the next
action boundary instead. `ScheduleAiTurn` also calls
`RefreshSilentMode` each time (syncing the silent flag + "Opponents…"
overlay to the live setting) and, on an instant→paced transition,
forces a `RebuildAfterTerritoryChange` to refresh borders the instant
track's suppressed per-capture rebuilds left stale. `_aiTrackInstant`
holds the previous track so the transition can be detected; it is
seeded in `RunAiTurnsUntilHumanOrDone` so the first dispatch never
registers a spurious transition.

**Paced (Slow/Normal/Fast)** — a preview/execute step machine:

```
StepAiPreview: StepAiPreviewAfterChoose(aiChooser(state,color,visited,rng), color)

StepAiPreviewAfterChoose(action, color):
  ├─ defensive re-checks (game over? player changed? still AI?)
  ├─ if action == null OR step cap reached:
  │     ├─ EndCurrentAiPlayerTurnCore(action)   ── shared mutation core
  │     │     (EndOfTurnProcessing; advance + StartPlayerTurn;
  │     │      reset _aiVisited/_aiStepsThisPlayer/_pendingAiAction)
  │     ├─ ShowHighlightAndRefresh(null)
  │     └─ if next is AI: schedule next StepAiPreview
  ├─ _pendingAiAction = action
  ├─ ShowHighlightAndRefresh(acting territory)
  └─ schedule StepAiExecute after AiPreviewDelayMs

StepAiExecute:
  ├─ ApplyAiActionCore(action)   ── shared mutation core: record beat
  │     (live only) + ExecuteAiMove/BuyUnit/BuildTower/… ; returns
  │     result coord (null = unrecognised → defensive return)
  ├─ CheckGameEndConditions; ShowHighlightAndRefresh(resulting terr.)
  ├─ if PendingDefeatScreen: RefreshSilentMode + RefreshViews, return
  │     without scheduling — dismissal handler resumes via ScheduleAiTurn
  └─ schedule next StepAiPreview after AiActionDelayMs
```

**Instant fast-forward (shared driver).** Live AI Instant and
instant replay share one chunked, frame-yielded loop,
`RunInstantTick(active, step, onExhausted, reschedule)`:

```
RunInstantTick:
  ├─ _suppressMapRebuild = true
  ├─ loop step():  Continued → keep draining
  │                TurnBoundary → break (a turn just completed)
  │                Exhausted → _suppressMapRebuild=false; onExhausted()
  │                budget (InstantBudgetMs, 8 ms) → break, no repaint
  ├─ _suppressMapRebuild = false
  ├─ if turnBoundary: _map.RebuildAfterTerritoryChange + RefreshViews
  └─ reschedule(turnBoundary)   ── caller's re-dispatching scheduler,
        NOT a fixed self-reschedule, so a mid-run speed change can
        switch OFF the instant track here (AI → ScheduleAiTurn,
        replay → ScheduleNextReplayBeat; each owns its per-track delay)
```

Two thin wrappers feed it:

- **`InstantReplayTick`** — `step` = `ReplayInstantStep` (pop a beat,
  `ExecuteReplayBeat`, game-end check; `TurnBoundary` on
  `ReplayEndTurnBeat`); `onExhausted` = `EndReplay`.
- **`InstantAiTick`** — `step` = `AiInstantStep` (call the chooser;
  `ApplyAiActionCore` or, on null/step-cap, `EndCurrentAiPlayerTurnCore`;
  `TurnBoundary` when an AI turn completes and the next player is also
  AI; `Exhausted` on game-over, hand-back to a human, or a pending
  defeat/claim overlay); `onExhausted` = `EndInstantAiBatch` (final
  rebuild + lift silent + one paint; or, if an overlay is pending,
  lift silent + RefreshViews and let the dismiss handler resume).

The chooser cost is paid inline within the 8 ms budget; the driver
yields a real frame between ticks (`ScheduleUnscaled` → timer, not
inline) so pan/zoom/input stay live. Per-capture
`HandleCapture.RebuildAfterTerritoryChange` is `_suppressMapRebuild`-
gated, so the structural redraw + tile-fill resync is coalesced to
the driver's turn-boundary / batch-end repaint — captures no longer
recolor tile-by-tile (the `HexTile` purity invariant above is what
makes this hold). Live AI Instant is thus 1:1 with instant replay,
with one deliberate difference: the "Opponents are taking their
turns…" overlay stays for live play (driven by `RefreshSilentMode`),
which replay leaves off. `ApplyAiActionCore` / `EndCurrentAiPlayerTurnCore`
are shared with the paced path so the two can't drift (pinned by
`InstantAiTests.InstantAi_SameBeatsAndFinalStateAsPaced`).

`InSilentAiBatch()` =
`aiSilentMode() && currentPlayer.IsAi && !PendingDefeatScreen`
(`aiSilentMode` = `!IsReplayMode && AiSpeed == PlaybackSpeed.Instant`).
It no longer gates rendering (the driver owns coalescing); it remains
the **input gate** and the silent-flag source. Every top-level human
input handler (`TrackHandler`-wrapped click/key handlers, plus
`OnEndTurnPressed`, `OnUndo*`, `OnRedo*`, `OnDefeatContinuePressed`,
`OnClaimVictory*`) short-circuits on it so input can't mutate
`SessionState` between the instant driver's frame yields.
`PendingDefeatScreen.HasValue` flips it false mid-batch so the
overlay paints and `OnDefeatContinuePressed` can dispatch; the
dismiss handler resumes via `ScheduleAiTurn`. Game-end branches
ignore the silent flag and always refresh.

The **"Opponents are taking their turns…" overlay is decoupled from
silence**: `RefreshSilentMode` shows it whenever an AI is acting in
live play at *any* speed (`!IsReplayMode && !GameEndedFired &&
!IsGameOver && currentPlayer.IsAi && !PendingDefeatScreen`), tracked by
`_aiBatchOverlayShown` — so a paced (Slow/Normal/Fast) AI turn shows
the indicator too, even though only the Instant batch is silenced.
(Replay never shows it; the input gate above still only fires for the
Instant batch.)

Tests use `SynchronousAiPacer` (both `Schedule` and `ScheduleUnscaled`
drain inline) or `QueuedAiPacer` (`DrainAll`) to step the driver
deterministically.

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
  ├─ _replayInstantActive = replayIsInstantMode?()  (UserSettings
  │     .ReplaySpeed == Instant; injected by Main)
  ├─ if instant: _map.SetSilentMode(true)  (sound/VFX/tweens off)
  ├─ map.RebuildAfterTerritoryChange + overlay clears + RefreshViews
  └─ if instant: ScheduleUnscaled(InstantReplayTick, 0)
       else schedule StepReplayPreview after AiBetweenPlayersDelayMs

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

**Instant replay (`ReplaySpeed.Instant`).** `BeginReplay` schedules
`InstantReplayTick` via `ScheduleUnscaled` — the thin replay wrapper
over the shared `RunInstantTick` driver documented under "Instant
fast-forward" above (`ReplayInstantStep` drains beats and reports
`TurnBoundary` on each `ReplayEndTurnBeat`; `onExhausted` = `EndReplay`).
It trades the paced preview/execute cadence for a silent, per-turn-
sampled fast-forward.

Why not the multiplier: a zero multiplier would (historically) have
trampolined the pacer and frozen the main thread for the whole
recording — the original "hang". That inline path is gone entirely.
Instant instead bypasses the multiplier via `ScheduleUnscaled`
(`SpeedMultiplier` has no Instant arm) and yields a real timer/frame
each tick, so pan/zoom and input stay responsive. The dominant
per-beat view cost — `HandleCapture`'s full-map
`RebuildAfterTerritoryChange` (`DrawTerritoryBorders` re-tessellates
every tile **and** resyncs every tile fill) — is suppressed via
`_suppressMapRebuild` and coalesced into one rebuild + refresh per
player-turn (`InstantBudgetMs` 8 ms wall-clock per tick;
`InstantTurnDelayMs` 200 ms between turn repaints). `RefreshSilentMode`
ORs in `_replayInstantActive` so a `ReplayEndTurnBeat` →
`StartPlayerTurn` can't un-silence playback mid-stream; `EndReplay`
lifts silent mode and does one final `RebuildAfterTerritoryChange`
(per-capture ones were skipped) before the closing refresh. Fidelity
is identical to paced replay — the model-mutation order is unchanged;
only view work is deferred. Live AI Instant uses the *same*
`RunInstantTick` driver (wrapper `InstantAiTick`), so the two instant
experiences are 1:1 by construction.

Replay reuses the live `ExecuteAi*` helpers — same captures, same
FX, same `HandleCapture` reconciliation — so replay fidelity comes
"for free" from converging on the live mutation paths. The actor on
each beat doesn't need to be passed through: `BeginReplay` restored
`CurrentPlayerIndex` from the initial snapshot, and every
`ReplayEndTurnBeat` steps it forward, so `_state.Turns.CurrentPlayer`
is the right player when each `ExecuteAi*` call fires.

**Invariant — no AI-only rules in the replay execute path.** The
`ExecuteAi*` helpers replay *every* recorded beat, including ones the
human performed. So those helpers must enforce only genuine game
legality, never AI *selection* heuristics — the human action paths
don't apply them, so a faithfully-recorded human beat would throw on
replay. Two such heuristics were found and excluded (the
`about_to_win` desync): (1) tower spacing — `AiCommon.MeetsAiTowerSpacing`
is filtered in `AiCommon.EnumeratePhase4Towers` (AI candidate
generation), NOT gated in `ExecuteAiBuildTower`; humans may bunch
towers. (2)
"a reposition onto own-empty consumes the unit's move" — an AI-loop
guard so the chooser doesn't re-pick the same unit. Gated on
**actor kind** (`CurrentPlayer.Kind == PlayerKind.Computer`) via the
`ConsumeRepositionMoveIfAi` helper shared by `ExecuteAiMove` and
`ExecuteAiBuyUnit`. The original gate was `&& !_replayMode`, but that
diverged live↔replay when the actor was AI in BOTH paths (AI live set
the flag, AI replay didn't) — pinned by `ReplayFidelityTests`. Actor-
kind is correct in both paths because the replay step machine advances
turn state before each action beat fires, so `CurrentPlayer` is the
original actor regardless of which path is executing. New AI-only
constraints must follow the same rule: enforce at candidate enumeration
or via an actor-kind gate in the shared execute path, never via a
replay-mode gate (that conflates "the AI is acting" with "we are live").

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
  `AiBuildTowerAction`, `AiBuyCombineAction` (buy a unit and combine it
  onto an existing friendly unit to unlock a new movement-consuming
  target — phase 2b below).
- **`AiCommon` phase-split enumeration** — the single source of legal
  candidate actions, split into one enumerator per stepwise-greedy phase
  (see `ComputerAi` below): `EnumeratePhase1ForUnit` (free
  captures/chops/grave-clears), `EnumeratePhase2aForUnit`
  (combine-to-unlock, existing units), `EnumeratePhase2b`
  (buy-and-combine-to-unlock), `EnumeratePhase3` (buy-to-capture/chop),
  `EnumeratePhase4Towers`, and `EnumeratePhase4bForUnit` (defensive
  repositions to border tiles). The shared unlock filter
  `UnlocksMovementConsumingTarget` admits a combine for phase 2a/2b only
  when the combined level reaches a movement-consuming target that
  neither source level could. Only these helpers know about rule
  legality. **Solvency gating is scoped to upkeep-increasing actions
  only** — buys, combines, and towers defer to
  `UpkeepRules.SurvivesNextUpkeep(gold, netIncome)` (treasury +
  `UpkeepHorizon`×netIncome ≥ 0, horizon currently 5). Phase-1
  captures/chops/grave-clears are **never** solvency-gated: they don't
  change upkeep and can only improve the economy, so a bankrupt
  territory must still be allowed to attack/chop (gating them caused
  stalemates). `AiStateScorer`'s bankruptcy lookahead uses the same
  `SurvivesNextUpkeep` predicate, so a buy/combine the scorer would
  approve is never silently dropped by the enumerator. Treasury-aware
  solvency + the removal of the standing `GoldWeight` term close #19's
  hoarding; the multi-turn horizon closes #22's doom-spiral bankruptcies.
- **`ComputerAi`** — the game's only AI (drives every `PlayerKind.Computer`
  slot). 1-ply lookahead via `AiSimulator.Clone` + `AiStateScorer.Score`.
  **Stepwise-greedy phase ordering (#26):** each `ChooseNextAction` call
  picks the largest non-exhausted owned territory (descending cell-count,
  capital coord tie-breaker) and tries phases **1 → 2a → 2b → 3 → 4a → 4b**
  in order, committing to the first phase that yields an action; a
  territory is marked visited only when *all* phases come up empty. Within
  a phase, units are iterated in power-then-coord order and all candidates
  scored, best delta wins.
  **Phases 1 and 2a take their best legal candidate regardless of delta
  sign** (`BestPositiveDelta` called with `threshold = int.MinValue`) — a
  free capture/chop/grave-clear or an unlock-combine is never declined in
  favor of the status quo, even when border-exposure makes the immediate
  delta ≤ 0. Phases 2b/3/4 keep the strictly-positive (`> 0`) gate: 2b/3
  are always-positive under the scorer anyway, and 4a/4b (towers,
  defensive repositions) are genuinely optional. Ties resolve to the
  first-yielded candidate, so equal-delta candidates from later
  territories/units can't displace an earlier winner.
  `AiSimulator` mirrors the mutation logic in `GameOperations`'
  `ExecuteAi*` paths (including `ExecuteAiBuyCombine`); if you add a new
  AI-capable action you must update both in lockstep, or simulated scoring
  will drift from real play. The lockstep is pinned by
  `AiSimulatorDriftTests`: every action the enumerators emit from a rich
  fixture is applied through both paths and the resulting
  `GameStateChecksum` canonical strings must match (plus a
  clone-fidelity check and a fixture-rot guard asserting all four
  action kinds stay covered). `AiSimulator.Apply` throws
  `NotSupportedException` on action kinds it doesn't model (Rally,
  ClaimVictory, Dismiss*) so future drift surfaces loudly rather than as
  a silent no-op.
- **`AiStateScorer`** — pure `GameState → int` scoring (self value minus
  enemy values). Key tuned constants: `TileWeight` 10, `NetIncomeWeight`
  1, `FragmentationPenalty` 15, `EnemyEdgePenalty` 3,
  `UndefendedBorderPenalty` 10, and **`OwnTreePenalty` 35** (raised from
  20). The tree penalty is set above 3× `UndefendedBorderPenalty` so a
  chop (worth `OwnTreePenalty` for removing the tree, on a bankrupt
  territory the +1 income is clamped) stays positive even when the
  chopping unit uncovers up to three border tiles — i.e. chops dominate
  the border-exposure they incur, fixing the tree-spread "treeopocalypse"
  stalemates. Gold contributes zero to standing value (see #19).
- **`ReplayDrivenAi`** — script-driven chooser used only by the
  TutorialBuilder's Preview mode. Replays recorded non-player-0
  `ReplayBeat`s through the standard AI step machine via a shared
  `ScriptCursor` (also referenced by `TutorialPreview` on the human
  side, so beats consumed by either advance the other). Lives in
  `scripts/Tutorial/`; plugged into `GameController` directly as
  the `aiChooser` delegate, bypassing `AiDispatcher`.
- **`AiDispatcher.ChooseForCurrentPlayer`** — returns `ComputerAi`'s
  choice for a `Computer` slot and null for a `Human` one, based on
  `Player.Kind`. Wired into `GameController` as the single `aiChooser`
  delegate for normal play.
- **AI tracing** lives in the `Log.LogCategory.Ai` / `Turn` /
  `Capture` categories (`ComputerAi` candidate diagnostics,
  per-turn header + end-turn + action lines, capture diffs). Off by
  default; enable via `FOUREXHEX_LOG` or the `FOUREXHEX_6AI` implied
  defaults. See **Logging** below.

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
- **Named saves.** The pause menu's **Save Game** option (see
  "Pause / Options menu" below) opens an `AcceptDialog` for a slot
  name and calls `SaveStore.WriteSlot`. The literal `autosave` slot
  name is reserved.
- **In-game load.** The pause menu's **Load Game** option opens the
  shared `SlotPickerDialog` populated from `SaveStore.ListSlots`.
  Picking a slot sets `LoadRequest.Pending`, cancels in-flight AI
  timers via `_controller.AbandonGame`, unpauses (since
  `GetTree().Paused` persists across scenes), and changes scene to
  `main.tscn` — same final-step path the main menu's Load button
  uses.
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
- **Campaign level pointer (v8).** Saves carry an optional
  `CampaignLevel` (0..255) for games launched from the campaign screen
  (see "Campaign mode" below); null/missing for freeform games and
  pre-v8 saves. It rides through autosave so a resumed campaign game
  still knows which ladder level it is and can record the win on
  game-over. `Main._Ready` restores it into `GameSettings.CampaignLevel`
  (or clears it for a freeform/starting-map/diagnostic load).
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
(format version 8; accepts v2–v7 on read so existing autosaves keep
loading after each cutover); `Serialize` writes the player roster's
`Kind` and `Difficulty` fields, `SerializeMap` omits both (the
editor's saved maps don't bake a player config — roles and
difficulty are assigned at play time from the menu).

**iOS AOT constraint: source-generated `JsonSerializerContext`.** iOS
forbids JIT, so .NET on iOS is AOT-compiled and `System.Text.Json`'s
reflection-based path throws "Reflection-based serialization has been
disabled for this application." Every `JsonSerializer.Serialize`/
`Deserialize` call therefore routes through a source-generated
`JsonTypeInfo<T>`: `src/FourExHex.Model/FourExHexJsonContext.cs` declares
the top-level context with `[JsonSerializable(typeof(SaveData))]` (used
by `SaveSerializer` and `SaveStore`'s SavedAt-header read) and
`[JsonSerializable(typeof(CampaignData))]` (used by `CampaignSerializer`,
see "Campaign mode" below); `scripts/UserSettings.cs` nests its own
`JsonContext` so the generator can reach the file's
`private sealed class SettingsDto`. The
`[JsonSourceGenerationOptions]` attribute carries the historical
`WriteIndented` / `WhenWritingNull` settings, so the JSON wire format is
unchanged and pre-source-gen saves load through the new path with no
migration. Adding a new top-level serialized type means adding it to
the context's `[JsonSerializable]` list — the deliberate
discriminator-string-plus-hand-switch shape (see `SerializeOccupant` /
`SerializeReplayBeats`) keeps the surface tiny. Both accept an optional `Tutorial` POCO that
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

## Campaign mode (issue #2)

A fixed ladder of **256 levels** (`00`–`FF`) reachable from the main
menu's **Campaign** button, with persistent per-level win/loss
tracking. Levels split into four tiers of 64 that line up with the
high hex digit and map straight onto the existing `Difficulty` enum:
Recruit `00–3F`, Soldier `40–7F`, Captain `80–BF`, Commander `C0–FF`.
Every level is one Human (Red) + five Computer on a procedural map; the
**human's difficulty handicap = the tier** (AIs stay Soldier), and the
level→seed mapping is identity (`MasterSeed = level`). Same handicap
machinery as the per-player difficulty lever — no new rules.

The feature spans all four layers, respecting the one-way dependency
graph:

- **Model (Godot-free, unit-tested):**
  - `CampaignProgress` (`src/FourExHex.Model/CampaignProgress.cs`) —
    256 `CampaignLevelStatus` values (`Untried`/`Lost`/`Won`, member
    order load-bearing because statuses persist numerically). Exposes
    `StatusOf`, `MarkAttempted` (Untried→Lost, Won terminal), `MarkWon`
    (terminal), `WonCount`, `TierWonCount`, `NextUp` (lowest non-won,
    null when all won), and the statics `DifficultyForLevel`
    (`(Difficulty)(level / 64)`), `LabelFor` (`"4F"`), `SeedForLevel`
    (identity). **Mark-at-launch semantics:** starting a level marks it
    Lost so an abandon or crash already counts as an attempt with no
    extra bookkeeping; winning flips it to Won, which a later loss can't
    revert.
  - `CampaignSerializer` + `CampaignData` (same file family) — JSON
    `{ FormatVersion, Statuses[] }`, registered on `FourExHexJsonContext`
    for iOS AOT. Tolerant on read: short arrays pad with Untried, extras
    past 256 are ignored, out-of-range ints degrade to Untried, unknown
    versions throw (the store catches → fresh progress).
- **ViewMath (floats OK, unit-tested):** `CampaignGridMath`
  (`src/FourExHex.ViewMath/CampaignGridMath.cs`) — pointy-top honeycomb
  geometry: `CellCenter` (odd rows shift half a step, rows interlock at
  0.75×height pitch), `BlockSize`, and `HitTest` (exact point-in-hexagon
  so the overlapping rows resolve to the right cell). Drives both the
  draw and the tap path, so they can't drift.
- **Scripts (Godot view layer, test-excluded):**
  - `CampaignStore` (`scripts/CampaignStore.cs`) — static persistence to
    the `user://campaign.json` **sidecar** (independent of game saves;
    deleting saves never touches progress). Mirrors `UserSettings`: lazy
    load, atomic tmp+rename write **immediately on every status
    transition** (never "on exit", so a crash can't lose a result),
    `GD.PushWarning` + fresh fallback on a corrupt/missing file.
    `PrepareLaunch(level)` centralizes the seed/roster/difficulty setup
    and the mark-attempted, shared by both launch entry points.
  - `CampaignPanel` (`scripts/CampaignPanel.cs`) — the campaign screen:
    fixed header (back, `won / 256`, progress bar) over a
    `ScrollContainer` of four tier sections. Each tier is **one**
    custom-drawn `TierGrid` control (64 hexes in `_Draw` via
    `CampaignGridMath`, taps in `_GuiInput`) — far lighter than 256
    Button nodes, and the 8↔16 column reflow is just a rebuild. Status
    drives hex styling: green fill = won, red outline = lost, gray
    outline = untried. (The design's "next up" thick-outline state was
    dropped — it masked the lost styling of the lowest unbeaten hex.)
  - `MainMenuScene` — **Campaign** button on the landing panel; the
    campaign panel is the third toggled panel (same `Visible` pattern as
    landing/play-config), rebuilt on an orientation flip like the
    play-config panel. Tapping a hex opens a `ConfirmModal` (level, tier,
    status, Play/Cancel); Play calls `CampaignStore.PrepareLaunch` and
    changes scene to `main.tscn`. The one-shot static
    `MainMenuScene.OpenCampaignOnArrival` makes the menu open straight to
    the campaign screen when returning from a campaign game.

**Win-flow call path.** `Main._Ready` reads `GameSettings.CampaignLevel`
(set by the menu, or restored from the loaded save) into `_campaignLevel`
and, for campaign games, wires the `HudView` campaign events. On
`GameController.GameEnded`, `Main.OnGameEndedRecordCampaignResult` marks
the level Won iff the winner is the human (any other outcome leaves the
launch-time Lost mark) — recorded **before** the controller's trailing
`RefreshViews`, so the overlay reads updated totals. `HudView.Refresh`
then shows the **campaign victory overlay** ("Level XX — won", updated
`N / 256`) instead of the standard one, with **Next unbeaten level**
(`Main.LaunchNextUnbeatenCampaignLevel` → `PrepareLaunch(NextUp)`) and
**Back to campaign** (sets `OpenCampaignOnArrival`, then
`AbandonAndReturnToMenu`). An AI win shows the standard overlay. The
campaign overlay is a Main-facing extension of the concrete `HudView`
(like `NewGameClicked`/`MainMenuClicked`), **not** part of the
`IHudView` contract — `GameController` and its tests are untouched.

## Pause / Options menu

A single **Options** button on each scene's HUD (and the Escape key
when no Buy/Build/Move is pending) opens that scene's `EscMenu`
populated with the scene's own option list. Three scenes use this
pattern: gameplay (`Main`), map editor (`MapEditorScene`), and
tutorial builder (`TutorialBuilderScene`).

### Gameplay pause coordinator (`Main`)

`Main` owns `_isPaused` plus three helpers — `EnterPause`,
`ExitPause`, `ShowPauseMenu`. Entering pause sets
`GetTree().Paused = true`, which halts every `SceneTreeTimer` (the
heartbeat of `GodotAiPacer`) so the AI loop freezes mid-step. The
pause menu offers:

- **Resume** — `ExitPause`.
- **Save Game** — `OpenSaveDialogFromPause`: opens the same
  `AcceptDialog` the autosave path uses; on Confirmed/Canceled
  re-calls `ShowPauseMenu`. Pause stays on throughout.
- **Load Game** — `OpenLoadDialogFromPause`: opens `SlotPickerDialog`.
  Cancelling re-shows the pause menu (`VisibilityChanged → Visible=false`
  unless a slot was just picked); picking a slot sets
  `LoadRequest.Pending`, `_controller.AbandonGame`s the in-flight
  AI step, `ExitPause`s (since `GetTree().Paused` persists across
  scenes), then `ChangeSceneToFile("res://scenes/main.tscn")`.
- **Settings** — opens the shared `SettingsPanel`; on `Closed`
  re-shows the pause menu.
- **Exit Game** — `ExitPause` then `AbandonAndReturnToMenu`.

`EscMenu.EscapeClosed` is a sibling event added next to `Closed`
that fires immediately before `Hide` when the user presses Escape
on an open menu. `Main` hooks it to `ExitPause` — the button-click
path already manages pause state from inside each option callback,
so `EscapeClosed` is the only path that needs the unpause hook.
`Closed` still fires on every close (button-click or Escape);
nothing else in the codebase listens to it for the pause flow.

### Reusable `SettingsPanel`

`SettingsPanel` (CanvasLayer modal — backdrop + centered panel +
SFX/VFX `CheckBox` rows + AI Turn Speed and Replay Speed radio rows
+ Back button) is the single Settings UI for both the main menu and
the in-game pause flow. SFX/VFX toggles bind directly to
`UserSettings.SfxEnabled` / `UserSettings.VfxEnabled` via `Toggled`.
Both speed rows are four `Button`s over the shared
`PlaybackSpeed` enum (`Slow`/`Normal`/`Fast`/`Instant`, one
`SpeedOrder` array + one `SpeedLabel`) in `ToggleMode` sharing a
`ButtonGroup` (radio semantics). The AI Turn Speed row's `Pressed`
handler writes `UserSettings.AiSpeed`; the Replay Speed row's writes
`UserSettings.ReplaySpeed` — two independent settings of the same
type. Godot's
default toggle visuals are subtle, so `ApplySpeedButtonStyle` paints
a solid white + dark-text stylebox on the pressed button and a dim
dark-background + light-text stylebox on the others; `Toggled` fires
on both the just-pressed and just-unpressed siblings, so a single
handler restyle keeps every button in sync. `Open()` re-syncs every
control from `UserSettings` so external writes are reflected. Back
or Escape calls `Close`, which fires `Closed`. The previous inline
`MainMenuScene.BuildSettingsPanel` has been deleted — main menu
instantiates the same component and opens it as a modal overlay on
top of the landing page.

A **Credits** button sits just above Back. It opens `CreditsPanel`
(`scripts/CreditsPanel.cs`) — a sibling CanvasLayer modal at
`Layer = 101`, one above `SettingsPanel`'s `100`, so it draws on top
while Settings stays visible underneath. `SettingsPanel` owns the
instance (created in `_Ready`, added as a child), so Credits is
reachable from both the main menu and in-game pause with no per-scene
wiring. `CreditsPanel` mirrors the modal shell (backdrop + centered
`PanelContainer` + serif title + gold rule + a `ScrollContainer`
holding the credits body + Back button) and its vbox uses the same
`(420, 570)` min size as `SettingsPanel` so the box doesn't resize on
open; the scroll area `ExpandFill`s to absorb the slack. The body is a
BBCode `RichTextLabel` (not a plain `Label`) so the author name
"FooBarzalot" is a gold `[url]` link to the repo; `MetaClicked` hands
the URL to `OS.ShellOpen`. Back or Escape calls `Close`. `SettingsPanel.Close` also calls
`_creditsPanel.Close()` (a separate CanvasLayer wouldn't hide on its
own), and `SettingsPanel._UnhandledInput` early-returns while
`_creditsPanel.IsOpen` so Escape closes only Credits, not Settings —
the same guard `MainMenuScene` uses for the settings panel.

### Quitting from the main menu (`ConfirmModal`)

The landing page has an **Exit** button at the bottom of the button
stack (desktop builds only). Both the Exit
button and Escape on the landing page route to `OnExitPressed`, which
opens a quit-confirmation modal rather than calling `GetTree().Quit()`
outright; the actual quit lives in `OnQuitConfirmed`, wired to the
modal's `Confirmed` event.

The confirmation uses `ConfirmModal` (`scripts/ConfirmModal.cs`) — a
reusable yes/no dialog in the `ModalChrome` family (dim backdrop +
centered slate panel + serif title + gold rule + message + Cancel /
confirm buttons), built to replace Godot's unstyled
`ConfirmationDialog` so it matches Settings / Credits / the slot
picker. Title, message, and confirm-label are constructor args, so the
same shell serves any prompt. Cancel or Escape raises `Canceled` and
closes; the confirm button **or Enter** raises `Confirmed`.
`MainMenuScene._UnhandledInput` early-returns while
`_quitConfirmModal.IsOpen` (the same modal-open guard used for
Settings) so an open dialog owns its own Escape/Enter instead of the
landing handler re-triggering.

### ProcessMode rules

The pause modal must stay interactive while
`GetTree().Paused == true`, so each modal node opts out of the
freeze: `EscMenu`, `SettingsPanel`, `CreditsPanel`, `SlotPickerDialog`
(and its sibling error dialog), and `Main`'s `_saveDialog` /
`_saveErrorDialog` all set `ProcessMode = ProcessModeEnum.Always`.
`Always` is a superset of the unpaused-host scenes' needs (map
editor / tutorial builder / main menu), so the same setting works
in every host — earlier `WhenPaused` attempts broke the unpaused
hosts because `WhenPaused` *only* processes while paused.

Conversely, `SceneTreeTimerFactory.After` passes
`processAlways: false` to `SceneTree.CreateTimer`. Without that
override, Godot's default keeps the timer firing during pause; the
AI loop wouldn't actually freeze under an earlier iteration of the
pause coordinator until this was added.

### Map editor / Tutorial builder

Map editor's `EscMenu` carries **Resume / Save Map / Load Map /
Exit** — Save Map and Load Map were previously HUD buttons and are
now menu options invoked through `OpenSaveDialog` / `OpenLoadDialog`
in `MapEditorScene`. Tutorial builder's `EscMenu` carries the
mode-switch buttons + Save Tutorial / Load Tutorial / Exit; the
target mode's button is rendered `Disabled = true`. Neither scene
calls `GetTree().Paused` — they have no AI loop running in the
background, so cosmetic-only "pause" is fine.

`MapEditorHudView.ShowSceneRootChrome` now gates a single button:
when `true` (the default and used by both `MapEditorScene` and
`TutorialBuilderScene`'s Map Edit mode), the HUD's right strip
ends with an **Options** button that raises `EscRequested`. The
host scene's `OpenEscMenu` decides what the menu contains. Record
and Preview submodes of the tutorial builder hide the
`MapEditorHudView` and rely on the nested `HudView`'s own Options
button (it raises `EscRequested` too, forwarded to the same
`OpenEscMenu`).

### Debug cheat menu (`CheatMenu`)

`scripts/CheatMenu.cs` (issue #7) is a Debug-only button modal
summonable over any screen: backquote on desktop, 3-finger tap on
touch (recognized by `MultiTouchTapDetector` in ViewMath). The whole
file is wrapped in `#if DEBUG`, and every scene root
(`MainMenuScene`, `Main`, `MapEditorScene`, `TutorialBuilderScene`,
`PlayTutorialScene`) calls `CheatMenu.Attach(this)` from `_Ready`
inside its own `#if DEBUG` block — there is **no autoload
registration**, so a Release build contains no listener, no menu,
and no call sites (the formal Release-strip check is deferred to
#6). `Attach` also runtime-guards on `OS.IsDebugBuild()`.

The node itself is a thin input listener (`_Input`, not
`_UnhandledInput`, so the summon gesture wins even over focused
Controls — a deliberate dev-tool tradeoff) that owns a private
`EscMenu` instance for the modal chrome. Current entries: **Tutorial
Builder** (`ChangeSceneToFile` to the builder, no in-progress-game
guard — it's a dev tool) and **Close**. Adding a cheat = adding an
`EscMenu.Option` to the list in `Toggle`. Instrumented under
`Log.LogCategory.Cheat` (attach / open / close / button presses).

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
  `RedoLast/All`, `EscRequested`) to panel methods
  (`SetSelectedPalette`, `GenerateMap`, `UndoLast/All`) and to
  `OpenEscMenu` (which lists Resume / Save Map / Load Map / Exit
  and dispatches to `OpenSaveDialog` / `OpenLoadDialog`
  internally), then listens to `panel.UndoStateChanged` to refresh
  the HUD's undo-bar enable state. The split exists so `tutorial_builder.tscn` can host the
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
    HUD's right strip ends with an **Options** button that raises
    `EscRequested`. Both `MapEditorScene` and `TutorialBuilderScene`
    set this `true`; each scene's `OpenEscMenu` decides what the
    `EscMenu` contains (map editor → Resume / Save Map / Load Map /
    Exit; tutorial builder → mode switches + Save Tutorial / Load
    Tutorial / Exit). Save Map / Load Map were previously HUD
    buttons exposed via `SaveMapClicked` / `LoadMapClicked` events;
    those events have been removed.
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
  the `EscMenu` modal (Resume / Save Map / Load Map / Exit).
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
  tutorial scripting that refers to specific cells by number. The
  tooltip is a *mouse-only, editing-mode* aid: `MapEditorPanel` always
  subscribes to `CoordHovered`, but `OnCoordHovered` feeds `null`
  (which dismisses + suppresses the tooltip) when either `PaintingEnabled`
  is false *or* `DisplayServer.IsTouchscreenAvailable()` is true. So it
  shows in the standalone editor and the tutorial-builder Map Edit mode
  on a pointer device, but not in Record / Preview / Play Tutorial (which
  reuse the same panel with `PaintingEnabled = false`), and not on a
  touchscreen at all — Android emulates mouse-motion from touch
  (`emulate_mouse_from_touch`), so a tap/drag would otherwise fire
  `CoordHovered` and a parked finger would dwell into a sticky tooltip.
- **Palette.** `MapEditorHudView` builds a palette of
  `HexPaletteButton` swatches: one per player color, a **neutral
  (unowned land)** swatch, plus water, tree, capital, and tower
  toggles. The selected index is read by
  `OnCoordClicked` and dispatched to one of `MapEditPaint`'s pure
  functions (`PaintLand`, `PaintNeutral`, `PaintCapital`,
  `PaintTowerToggle`, `PaintTreeToggle`, `PaintWater`). Each helper
  mutates the grid in place, then re-runs `TerritoryFinder` +
  `CapitalReconciler` (except `PaintCapital`, which honors the user's
  exact pick rather than letting the placer second-guess them).
- **Neutral hexes (issue #39).** A neutral hex is land owned by
  `PlayerId.None` — part of the map but belonging to no player,
  capturable by any adjacent player exactly like enemy territory
  (`tile.Owner != attackerTerritory.Owner` is already the capture
  predicate), and once captured it becomes ordinary owned land.
  Neutral placement is **editor-only** — `MapGenerator` never produces
  a `None`-owned tile. `PaintNeutral` sets the tile's owner to `None`
  and **clears its occupant** (a neutral hex is empty unowned land), so
  a player-bound occupant can't be stranded there; the tower tool can
  then add a tower on top, which protects/radiates over the neutral
  region as usual. Neutral tiles flood-fill into their own `None`-owned
  `Territory`, which generates no income (`Treasury.CollectIncomeFor`
  skips non-owned, capital-less territories) and never gets a capital —
  `CapitalReconciler.Reconcile` short-circuits a `None`-owned territory
  to capital-less and **throws** if a `Capital` occupant is ever found
  on neutral land (an upstream paint bug, surfaced not papered over). A
  neutral capture is instrumented under `Log.LogCategory.Capture`
  (`[capture] neutral hex {coord} -> {player}` from
  `GameOperations.HandleCapture`). Save/load round-trips neutral tiles
  with no version bump (ownership already encodes `None` as wire index
  `-1`).
- **Responsive land swatches.** The land-color swatches plus the
  neutral swatch (the "owner" group) collapse
  to a single cycling `HexPaletteButton` when the viewport is narrow
  (e.g. mobile portrait) — the editor analogue of HudView's
  player-swatch-bar compacting. The full `_landRow` and the lone
  `_landCycleButton` live side-by-side in the slate land panel; the
  `OnViewportMetricsChanged` override (inherited hook from
  `OrientationHud`) toggles which is visible by width threshold
  (`FullLandRowWidth{Portrait,Landscape}`). The collapsed button is
  *select-first-then-cycle*: when land isn't the active tool a press
  just selects it at the remembered color (`_lastLandPaletteIndex`);
  once land is active each press advances to the next owner state,
  cycling through the player colors and then the neutral slot before
  wrapping back to the first color. Its `FillColor` (a settable property
  on `HexPaletteButton`; the neutral state shows `PlayerPalette.Neutral`
  gray) and selection outline track that state via
  `RefreshLandCycleVisual`. Only the owner group collapses — water,
  tree, capital, tower, and hand stay individual.
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

### Playing a tutorial (end-user `play_tutorial.tscn`)

`PlayTutorialScene` (root of `res://scenes/play_tutorial.tscn`, reached
from the main menu's **always-visible** "Play Tutorial" button) lets an
end-user *play* a tutorial without the authoring tool. It's a chrome-free
host that reuses the playback machinery: in `_Ready` it builds a
`MapEditorPanel` (roster set to `Player.BuildAllHumanRoster()` BEFORE
`AddChild`, as the panel asserts) + a `PreviewPane` + a shared `EscMenu`,
loads the bundled tutorial via `SaveStore.LoadBundledTutorial("full_tutorial")`,
then `panel.LoadFromMap` → `panel.ResetToTutorialStart(InitialSnapshot)`
→ `preview.Start(tutorial)` — the same load sequence
`TutorialBuilderScene.OnLoadSlotPressed` uses, ending in `Start` instead
of `SetMode(Record)`. ESC raises `PreviewPane.EscRequested` → a minimal
`EscMenu` (Resume / Main Menu). The end-of-tutorial victory overlay's
Replay / Play Again / Main Menu buttons are handled inside `PreviewPane`
itself (no host wiring). The played tutorial ships in the repo at
`tutorials/full_tutorial.json` (= `res://tutorials/`, the same
`SaveStore.BundledMapsDirectory` bundled maps use). Since `.json` isn't a
Godot resource, `export_presets.cfg` carries `include_filter="tutorials/*.json"`
on every preset so the bundled tutorials/maps ship in the packaged PCK
(the `export_filter="all_resources"` mode alone would skip them).

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
  players forced `PlayerKind.Human`. The pane's own `HudView` occupies
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
shared with the play state Record / Preview build atop, so recruits /
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

- **`ReplayEndTurnBeat`** → `SetCta(EndTurn, true, pulse: true)`.
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
  `ShowMoveTargets([Target], Recruit)`.
- **`ReplayClaimVictoryBeat` / `ReplayDismissClaimBeat` /
  `ReplayDismissDefeatBeat`** → CTA on the matching overlay button.

Before dispatching, `Apply` checks mode compatibility with the next
beat. If the player is in a mode the beat can't be executed from
(e.g., still in BuyingRecruit when the next beat is End Turn,
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
  Buy Recruit button."; Mode=BuyingX below target → "Now press the
  Buy Recruit button again to upgrade to a {next}."; matching mode →
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
accidentally hit Buy Recruit or End Turn while a narration beat is
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
kinds (EndTurn, BuyRecruit). The new design pushes gating into
`GameController` itself via the single `humanActionValidator` hook
and reuses `_replayBeats` for the script — one source of truth for
both recording and validation.

### Tutorial file format

Same v4 schema as in-progress saves. A tutorial file is just a v4
save with BOTH a `Tutorial { Title }` block AND a `Replay { ... }`
block. Deserialize throws if the Tutorial block is present without
a Replay block. The `Tutorial` class is `{ Title, Replay }` — no
`StartTurn` / `StartPlayer` / `Beats` (the Replay carries those).

## Renderer

The project is pinned to **GL Compatibility** (`project.godot` lines
16 & 38: `config/features` contains `"GL Compatibility"`,
`rendering/renderer/rendering_method="gl_compatibility"`). Switched
from Forward Plus on 2026-05-21.

Rationale: the game is 2D-only and draws with `Polygon2D` tile fills
and batched immediate-mode line/triangle primitives (see "Draw-call
batching" below) — no custom shaders, no 3D, no Forward-Plus-specific
features. Compatibility is the more portable choice: it runs on a wider
range of hardware, has a smaller runtime, and is the renderer required
for any future web export. The visual delta on macOS/Apple Silicon is
indistinguishable in practice for this rendering surface (per the manual
desktop test on the switch commit; log header confirms `OpenGL API 4.1
Metal - Compatibility`).

2D MSAA is enabled at 2× (`project.godot`
`rendering/anti_aliasing/quality/msaa_2d=1`) so the batched, non-AA
border/outline lines (below) stay smooth — per-primitive antialiasing
is off because it defeats batching.

One-renderer-everywhere is intentional: no per-platform override.
This means desktop and any future web build will draw identically,
avoiding the "looks fine on desktop, broken in browser" class of
regression.

A web export was scoped on the same date but is blocked engine-side
— Godot 4.6.1 .NET (mono) does not ship Web export templates (the
non-mono build's web templates target the GDScript-only runtime and
cannot run a C# project). Recording what's already done toward the
eventual web build so the work isn't repeated when a Godot version
with .NET web export lands: the renderer was switched to GL
Compatibility (independently the right choice for a 2D game), the
4.6.1 mono export-templates archive is installed under
`~/Library/Application Support/Godot/export_templates/`, and a
code-surface audit found no web-export risk surfaces (no threading,
no DllImport, no native deps, no runtime NuGet packages, no custom
shaders). Before retrying, reconfirm the new templates archive
actually contains `web_*.zip` files.

### Draw-call batching (Android performance)

In GL Compatibility every visible `CanvasItem` issues its own draw
call every frame, and neither `Polygon2D` nor antialiased lines batch.
A naïve "one node per shape" map hit **~6,500 draw calls/frame**, which
on a mid-range Android device (S9, OpenGL ES) turned every capture into
a ~300 ms multi-frame stall: a capture dirties the canvas and the
renderer re-processes all ~6,500 items. Diagnosed 2026-05-27 (the cost
is draw-call count, **not** C# / node churn — the C# rebuild is ~1 ms).
Two pieces in `HexMapView` collapse that to **~180–256 draws/frame**:

- **`PolylineBatch`** (one per layer, for territory borders and per-tile
  outlines): instead of a `Line2D` per edge, all edge segments are drawn
  in a single `DrawMultiline` (borders, uniform color) or
  `DrawMultilineColors` (outlines, player-dark per tile). `Draw*` is
  non-antialiased so it batches to ~1 call; 2D MSAA smooths it.
  `DrawTerritoryBorders` / `PopulateOutlinesLayer` build the segment
  arrays and `QueueRedraw()` on territory change.
- **`TriangleSoup` + `TriangleSoupBuilder`**: the water cells, rim water,
  and shoreline foam are **static** (never change after init) yet were
  ~1,870 `Polygon2D`. They're baked once into a single vertex-colored,
  indexed triangle array (`TriangleSoupBuilder` triangulates each source
  polygon via `Geometry2D.TriangulatePolygon`, preserving Polygon2D's
  `Color × VertexColors` shading) and drawn in one
  `RenderingServer.CanvasItemAddTriangleArray` call.

Tile fills remain one `Polygon2D` each (recolored, not recreated, on
capture) — they weren't the bottleneck. The remaining per-capture cost
is CPU-side `RefreshOccupantVisuals` recreating all occupant nodes every
refresh; making that incremental is a known candidate optimization if
the cost ever resurfaces (the related method-split refactor is
issue #10). Diagnostic instrumentation lives behind the `[hitch]` log
prefix (`Log.Since` timings, the `LogLongFrame` CPU/draw-call split in
`_Process`, and the one-shot `DumpSceneComposition`), all
`[Conditional("DEBUG")]` so they're stripped from Release.

## Visual / UI theme

The visual look is owned by three pieces on the Godot side, all in
the view layer (Model and Controller stay color-free):

- **`theme/fourexhex_theme.tres`** — the project-default `Theme`
  resource, set as `gui/theme/custom` in `project.godot`. Defines
  the slate `Panel` / `PanelContainer` / `PopupPanel` / `PopupMenu`
  styleboxes everything modal renders against, the `Button` /
  `OptionButton` normal/hover/pressed/disabled/focus styleboxes,
  `LineEdit` normal + focus, `CheckBox` + `Label` font colors,
  and the `TooltipLabel` font (Geist) + size (28). `Window` and
  `AcceptDialog` deliberately have no theme entries — Godot 4
  silently ignores `embedded_border` overrides on those, so
  modals are rebuilt on the `CanvasLayer` + `PanelContainer`
  shell instead (see below). A `PrimaryButton` `theme_type_variation`
  was added for brass-gold action buttons but is no longer used
  anywhere; the dead variation stays in the file for now.
- **`scripts/UiPalette.cs`** — static C# class exposing the same
  design tokens as `oklch`-style constants for view code that needs
  to paint directly (HexMapView's water + per-tile borders, HUD bg
  Panels with custom StyleBoxFlat overrides, gold rule decorations
  under dialog titles). Groups: surfaces (`BgDeep`, `BgPanel`,
  `BgElev`, `BgRow`, `BgRowH`, `HudBar` — the in-game/editor HUD
  bar, a touch darker than `BgDeep`), lines (`Line`, `LineSoft`,
  `LineHard`), ink (`Ink`, `InkSoft`, `InkMute`, `InkFaint`),
  brass (`Gold`, `GoldDeep`, `GoldDim`), water (`Water`,
  `WaterDeep`), plus the `ModalBackdrop` dim-scrim used by every
  CanvasLayer modal. The values match the heraldic-board-game
  palette the redesign settled on after a 50 % lerp back toward
  the original saturated primaries.
- **`fonts/`** — three OFL font files imported as Godot
  `FontFile` resources, loaded by view code via `GD.Load<FontFile>`
  and applied via `AddThemeFontOverride`. DM Serif Display
  (display titles — wordmark, dialog titles, end-game text),
  Geist (UI body — buttons, labels, eyebrows), JetBrains Mono
  (numerics — turn number, gold value, seed input).

**Player palette** lives in `scripts/PlayerPalette.cs`, separate
from the chrome palette because it depends on the roster:
`ColorFor(PlayerId)` reads `GameSettings.PlayerConfig` for the
fill, and `DarkColorFor(PlayerId)` returns a per-slot darker
companion used for the 1.5-px per-tile hex border stroke in
`HexMapView.PopulateOutlinesLayer`. The darks are ~ fill × 0.45
so per-tile borders stay visible within a single-owner territory
(rather than fading into isoluminance with the fill).

**Board palette** lives in `scripts/BoardPalette.cs`, a third
fixed-color class distinct from both `UiPalette` (UI chrome) and
`PlayerPalette` (roster-driven). It holds the colors of the board
itself: `RejectRed` (illegal-action ghost / forbidden slash),
`ForestCanopy` / `ForestTrunk` (conifer art, shared by HexMapView's
on-tile tree and `HudIcons.DrawTree`'s swatch), `CastleFill`,
`GraveCross`, and the economy-status hues `WarnRed` / `WarnYellow`
(selected-territory gold label + on-tile bankruptcy badge). Single
source so the on-tile rendering and HUD swatches stay in sync.

### Modal-dialog shell pattern

Every modal (Settings, EscMenu / pause, SlotPickerDialog for Save /
Load / Tutorial pickers) is built on the same three-piece shell:

1. **`CanvasLayer`** with `Layer = 100`, `Visible = false`,
   `ProcessMode = Always` so the modal stays interactive while
   the tree is paused (pause coordinator) AND while it isn't
   (main menu / map editor hosts).
2. **`ColorRect`** backdrop sized to the viewport, painted
   `UiPalette.ModalBackdrop` with `MouseFilter = Stop` so clicks
   behind the modal don't bleed through. (`SlotPickerDialog` wires
   the backdrop's `GuiInput` to close the modal on click;
   `SettingsPanel`, `CreditsPanel`, and `EscMenu` don't.)
3. **`PanelContainer`** centered via `AnchorLeft = AnchorRight =
   AnchorTop = AnchorBottom = 0.5` + `GrowDirection.Both`,
   picking up the theme's slate `Panel/styles/panel` stylebox
   automatically. Content lives in a `VBoxContainer` child.

The shared builders live in **`scripts/ModalChrome.cs`** (a static
class, no `using` needed): `BuildBackdrop(viewport)`,
`BuildCenteredPanel(panelW, panelH)` (fixed pixel size — the slot
picker) and its parameterless overload `BuildCenteredPanel()`
(content-sized — Settings / Credits / EscMenu, whose inner vbox
`CustomMinimumSize` drives the dimensions), and `BuildPanelHead`
(uppercase title + close × + 1-px line-soft divider). All four
modals call these so the shell can't drift. `ModalChrome` also
exposes `PalettePanelStyle()`, the rounded slate `StyleBoxFlat`
shared by HudView's and MapEditorHudView's palette-group panels.

The old `Window` / `AcceptDialog` modal shape (used by
`SlotPickerDialog` before the redesign) didn't pick up the theme
— Godot 4 silently dropped the `embedded_border` override — so
that path was replaced. `Window`-class modals are out of the
codebase.

### HUD shape

The play HUD (`HudView`) is a set of widget *clusters* parented into
floating zones (no opaque chrome bar — design D1 "Roles Split (floating)"
from `delivery/HUD_Spec_Issue3.md`). The map fills the viewport; the
HUD chips and buttons sit on top in fixed zones, and only the buttons /
chips themselves block clicks. The clusters:

- **Status chip** — `_statusChip` `PanelContainer` (semi-transparent
  slate, 75% opacity, line-soft border, 8-px radius) wrapping the
  `_statusCluster` HBox: `TURN` gold eyebrow + turn number
  (JetBrains Mono 36) and the **player-swatch bar**
  (`scripts/PlayerSwatchBar.cs`) — a custom-drawn `Control` showing one
  swatch per player in movement (turn) order, with the current player's
  swatch enlarged + white-outlined and eliminated players (no capital,
  detected via `WinConditionRules.IsEliminated`) dimmed in place. Collapses
  to a single active-swatch + bare turn number in compact mode (see
  "Responsive layout"). `MouseFilter = Ignore` cascaded over the chip
  and its children — taps fall through to the map.
- **Gold chip** — same chip styling as the status chip, with the gold
  total + income breakdown in JetBrains Mono 36, hidden when no capital
  territory is selected. Click-through.
- **Action cluster** — `_actionCluster` is a `BoxContainer` (Vertical
  flipped per orientation by `SetClusterVertical`) holding the four buy
  buttons (Recruit / Soldier / Captain / Commander) as a flippable
  `_paletteRow` AND a single collapsed cycle button (`_collapsedBuyButton`).
  Exactly one is visible — driven by `Compact` in `OnViewportMetricsChanged`,
  not a per-orientation width threshold. The cycle button fires the same
  `BuyRecruitClicked` event as the `U` hotkey
  (`GameController.OnBuyPressed`). The `_buildTowerButton` sits in the
  cluster alongside the palette.
- **Controls cluster** — `_controlsCluster` `BoxContainer` (Vertical
  flips per orientation) holding `_nextUnitButton` + `_nextTerritoryButton`.
  `_endTurnButton` is NOT in this cluster — it's placed at the row /
  rail level so it can anchor independently (bottom-right corner in
  landscape, end of bottom-bar row 2 in portrait).
- **Undo cluster** — `_undoCluster` HBox with the Undo / Redo ghost
  icon buttons. Long-press fires Undo All / Redo All.
- **Options** — gear cog (raises `EscRequested`).

Every action / chrome button is a `HudIconButton` rendered at **68×68
logical px** with a 2-px black border, 10-px rounded corners, dark-slate
fill (or terracotta "Hero" fill via `HudIconButton.Hero`; the white CTA
stylebox layered on top via `HudView.ApplyCtaStyle` keeps the same shape
and reapplies hero on CTA-off via `ReapplyHero`). Selected state draws
a cool-blue `UiPalette.SelectionRing` outline (replaces the previous
white ring) so "this mode is engaged" reads distinctly from the warm
hero accent.

`HudView.HudHeight = 96f` is preserved as a layout token for the tutorial
builder / record pane chrome that nests above the editor HUD; the
gameplay HUD itself no longer renders a 96-px slate bar. The portrait
bottom-bar height is `HudBars.PortraitBottomBarHeight = 200f`, sized for
two rows of 68-px buttons + 8-px row separation + 10-px top/bottom
padding.

The map editor HUD (`MapEditorHudView`) follows the same shell and
clusters: `_landCluster` (rounded slate `PanelContainer` wrapping the
six land swatches as a `BoxContainer`), `_landCycleButton` (standalone
squared swatch for compact — sibling of `_landCluster`, not nested
inside the panel), `_paintCluster` (water / tree / capital / tower as
**squared** `HexPaletteButton`s with the same 68×68 chrome as the die),
`_toolsCluster` (hand / pan + die / random), plus the undo/redo cluster
and Options gear. The die is the lone randomize trigger — pressing it
picks a fresh random seed and regenerates, then drops back to the hand
tool. The legacy seed `LineEdit` is gone.

### Responsive layout (landscape / portrait, compact / expanded)

Both gameplay and editor screens reflow between landscape ↔ portrait
**and** between compact (phone) ↔ expanded (tablet / desktop). Two
pure decisions, both Godot-free + unit-tested:

- **`ScreenLayout.Resolve(width, height)`** → `Landscape` when
  `width >= height`, else `Portrait` (square ties to landscape).
- **`ScreenLayout.IsCompact(width, height, prevWasCompact, deadBand)`** →
  true when the shorter viewport edge falls below
  `ScreenLayout.CompactBreakpointPx = 700` logical px (±32 px dead-band
  hysteresis around the boundary so a window resize through the line
  can't thrash the layout). Calibrated so every phone we test lands in
  compact and every tablet in expanded (iPhone 13 mini on-device min=507
  ✓ compact, S9 portrait min=486 ✓ compact, iPad mini min=768 ✓ expanded).
- **`ScreenLayout.ComputeInsets`** still exists for callers that
  reserve map space for an opaque bar; the gameplay / editor HUDs return
  `(0, 0)` — D1 is a true floating overlay.

**Orientation + compact lifecycle** lives in **`OrientationHud :
CanvasLayer`** (Template Method). The base owns five **zone**
containers, recreated on every layout flip:

| Zone | Type | Present | Role |
|---|---|---|---|
| `TopLeftZone` | content-sized HBox anchored top-left | both orientations | Read-only chips (status, gold) |
| `TopRightZone` | content-sized HBox anchored top-right | both | undo / redo / options |
| `BottomBar` | full-width Panel anchored bottom | portrait only | Action button rows |
| `LeftRail` (+ `LeftRailGroup` inner VBox) | 78-px wide Panel anchored left, full height | landscape only | Create / paint cluster |
| `RightRail` (+ `RightRailGroup` inner VBox) | mirror of LeftRail anchored right | landscape only | Command / tools cluster |

`Compact` is a public `bool` on `OrientationHud`; subclasses read it in
`OnViewportMetricsChanged` to swap the collapsed↔expanded palette /
roster variants. Rails are vertically `Center`-aligned on compact and
`End`-aligned on expanded — the spec's "lower-corner thumb zone" for
tablets.

Subclasses (`HudView`, `MapEditorHudView`) override `DetachClusters`,
`BuildLandscapeBars`, `BuildPortraitBars`, `ComputeInsets`, plus the
virtual `OnLayoutApplied` (post-flip) and `OnViewportMetricsChanged`
(every resize). They never call `AddChild` on a fresh zone — they
just parent their persistent clusters into the zone the base prepared.
`ApplyLayout` rebuilds zones whenever EITHER `Orientation` OR `Compact`
flips, so the rails' alignment + the palette / roster collapse update
in lockstep.

**Z-order matters.** `ApplyLayout` adds the rails / bottom bar FIRST,
then the corner zones — corner buttons (Options, undo/redo) must
intercept clicks before the rail's full-height Panel does, or taps in
the top-right column would be eaten by the rail. The corner zones are
`MouseFilter.Pass`; only the chips / buttons inside block clicks. The
portrait `BottomBar` is also `MouseFilter.Pass`, so the gap between
the left action cluster and End Turn on the right falls through to the
map (the player can tap a tile beneath the empty space).

**Safe-area policy** — split between "critical" buttons and "corner"
chrome:
- *Rails* (the critical action buttons: buy, build, nav, end turn)
  use `max(safe.Left, safe.Right) + edgePad` on BOTH sides so they
  NEVER overlap the notch regardless of orientation.
- *Corner zones* (status / gold readout chips, Options, undo/redo)
  and the bottom-right pinned End Turn button use no horizontal safe
  inset — they claim the corner real estate the rails leave behind.
  On iPhone landscape this means the corner chrome may overlap the
  notch or home-indicator visually, but iOS still routes taps through.
- `OrientationHud` subscribes to `SafeArea.Changed` so a status-bar
  show/hide or rotation crossing the notch axis triggers a relayout.

**Cluster placement per (orientation × variant) — gameplay:**

| | Compact (phone) | Expanded (tablet / desktop) |
|---|---|---|
| Portrait TopLeft | `_statusChip` (1-swatch active) over `_goldChip` | Same, with 6-roster swatch bar |
| Portrait TopRight | `_undoCluster` + `_optionsButton` | Same |
| Portrait BottomBar | Row 1: nav cluster (left). Row 2 (space-between): `_actionCluster` (buy cycle + Build Tower) left, `_endTurnButton` right | Row 1 same; Row 2 buy palette expands to 1×4 radio |
| Landscape TopLeft | `_statusChip` (1-swatch) + `_goldChip` inline | Same, expanded swatches |
| Landscape TopRight | undo + options | Same |
| Landscape LeftRail | `_actionCluster` (buy cycle + Build Tower) vertically centered | Buy palette expands to 1×4 vertical |
| Landscape RightRail | `_controlsCluster` (nav) vertically centered | Vertically end-anchored (with End Turn clearance pushed up) |
| Landscape End Turn | Pinned bottom-right corner (anchored directly to `HudView`, outside the rails) | Same; right rail's group pushed up by `endTurnClearance = 88px` so it doesn't collide |

**Cluster placement — editor:**

| | Compact | Expanded |
|---|---|---|
| Portrait TopLeft | *(empty)* | *(empty)* |
| Portrait TopRight | undo + options | Same |
| Portrait BottomBar | Row 1: tools (hand + die). Row 2: `_landCycleButton` + paint tools (water/tree/capital/tower) | Row 2: 1×6 land panel + paint tools |
| Landscape LeftRail | `_landCycleButton` + paint tools, vertically stacked | `_landCluster` (1×6 vertical line inside the slate panel) + paint tools |
| Landscape RightRail | hand + die | Same |

The `_landCluster` PanelContainer (slate frame around the 1×6 land row)
is fully hidden in compact mode — the bare `_landCycleButton` stands
alone as its sibling so the cycle button never sits inside the frame.

**Map reserves nothing in D1** (`HexMapView`). `MapInsetsChanged` still
fires from `OrientationHud`, but both subclasses' `ComputeInsets` return
`(0, 0)` — the map fills the viewport edge to edge, and the floating
chips / buttons overlay it. Portrait board rotation
(−90° to fit a wide map into a tall viewport) still runs via
`ScreenLayout.Resolve`. The pan / center / zoom math is unchanged
(see "Content-aware centering" below).

- **Map reserves the bars + rotates in portrait** (`HexMapView`). The
  view is a pure consumer of layout: `SetMapInsets(top, bottom)` (pushed
  by the HUD via a `MapInsetsChanged` event that `Main` /
  `MapEditorScene` relay) tells it how much vertical space the bars take;
  it re-centers within that. Separately, `HexMapView` resolves its own
  rotation from the viewport aspect (`ScreenLayout.Resolve`): **portrait
  ⇒ the board node rotates −90° (CCW)** so a wide map fills the tall
  viewport. Icon glyphs with an "up" (units, capitals, towers, trees,
  graves, warning badges, tower-placement previews) are counter-rotated
  by `ApplyGlyphUpright()` so they render upright at their rotated tile
  positions; the capital warning badge additionally counter-rotates its
  upper-left *corner offset* (`-_mapAngleRad`) so it stays up-left on
  screen instead of following the board into a down-left corner. Hex-cell-
  aligned overlays (tile fills, per-tile outlines,
  territory borders, water, shore foam, tower coverage, selection
  highlight, the symmetric move-target rings) and the directional
  rejection arrows rotate with the board. The pan / center / zoom-fit /
  zoom-anchor math runs through the pure, unit-tested
  **`MapPlacement.RotatedBoardBox(w, h, zoom, angleRad)`** (the on-screen
  AABB of the scaled+rotated board); at angle 0 it reduces exactly to the
  legacy landscape behavior, which stays pixel-identical. The tree glyph
  is split into a center-pivot placement node (counter-rotated) and an
  inner trunk-base anchor (the grow animation) so rotation and the
  rise-from-ground pivot don't fight.

- **Content-aware centering (centering only, not clamping).** *Centering*
  frames the *playable content* (the land tiles, `_state.Grid.Tiles` — water is
  separate, off-grid), not the padded nominal `Cols×Rows` grid: `HexMapView`
  caches the content's unscaled pixel box
  (`MapPlacement.ContentPixelBounds(landCoords, hexSize)`, recomputed on
  `Init`/`ReloadState`) and `RecenterMap` centers on that box's center via
  **`MapPlacement.RotatedRectBox(left, top, right, bottom, zoom, angleRad)`** —
  the offset-rect generalization of `RotatedBoardBox` (which now delegates to
  it). Without this, a level whose tiles sit off-center in a larger grid
  (notably the tutorial map) frames off-center. **Pan-clamping, by contrast,
  frames the full nominal grid** (`ClampPan` → `RotatedBoardBox(PixelSize…)`),
  *not* the content box: clamping to content would lock panning whenever the
  content is smaller than the viewport (egregiously, a sparsely-painted editor
  map with a few cells couldn't pan at all) and tighten it everywhere else, so
  the clamp deliberately keeps the pre-content-framing pan freedom. **Edge-
  scroll pad (issue #16):** the clamp box is widened by `ScrollPaddingPx` (300
  board-local px pre-zoom, symmetric) applied *after* `RotatedBoardBox` — in
  viewport space, since a rotated symmetric pad is still symmetric. This lets
  edge hexes pan out from under the D1 floating-HUD chips (top) and button bar
  (bottom) which would otherwise permanently occlude the outermost ring. The
  rendered water rim's depth in tiles is derived from the same constant
  (`ceil(ScrollPaddingPx / (1.5·HexSize)) + 1`) so the visible water always
  covers the reachable scroll area; the legacy hardcoded `WaterRimMargin = 4`
  is gone. Initial framing in `RecenterMap` is unchanged — it still centers on
  the content box, so the player starts looking at land, not the pad. Zoom-fit
  (`ZoomMath.ComputeZoomMin`) likewise uses the full grid, so the zoom range is
  unchanged. **Insets must reach the map:** the HUD's
  `MapInsetsChanged` is relayed to `HexMapView.SetMapInsets` by *both* `Main`
  (play) and `PreviewPane` (tutorial); without that relay the map keeps its
  default insets and portrait content is pushed down. (Landscape now reserves
  the bottom strip — `ComputeInsets` returns `top=0, bottom=barHeight` — since
  the bar moved to the bottom.) `RecenterMap` logs its inputs + resulting on-screen rect at
  `Render:Debug` for regression diagnosis. **Hand-tuned opening framing
  (#14):** `HexMapView.SetCamera(zoom, contentCenterOffset)` is the public
  alternative to the `RecenterMap` fit default — it clamps the zoom, re-syncs
  the discrete level index, and centers the view `contentCenterOffset` away
  from the content-box center. `PreviewPane.Start` uses it (deferred, so it
  lands after the `ReloadState`-queued recenter) to open *landscape* tutorial
  playback zoomed out slightly with the board shifted up clear of the
  bottom-hugging narration box; portrait keeps the fit default. Every user
  pan/zoom (and `SetCamera` itself) logs a `Render:Debug` `camera
  pan/zoom/set` line with the zoom + content point under the viewport center,
  which is exactly the pair needed to capture a manual framing as a new
  default.

`project.godot` is unchanged (default stretch, resizable); the responsive
behavior is all in the view layer. Real mobile-export settings (handheld
orientation lock, DPI stretch mode) are a later concern. Verify by
launching with `--resolution 720x1280` (portrait) vs `1280x720`
(landscape) and resizing across the square boundary for a live flip.
**Do not switch `window/stretch/mode` to `canvas_items`/`expand`** — the
view-layer layout already scales from the real viewport size, so a stretch
mode double-applies scaling and shrinks everything (regressed once in
portrait, then reverted).

**Touch input.** Touchscreen support is additive — mouse/trackpad stay
fully functional. Single-finger gestures need no special code or project
setting: Godot's default `emulate_mouse_from_touch` synthesizes mouse
events from finger 0, so **tap = left-click, drag = pan, press-and-hold =
long-press (rally)** all flow through the existing `HexMapView` mouse path.
The one genuinely-new path is **two-finger pinch-to-zoom**: touchscreens do
*not* emit the macOS-trackpad `InputEventMagnifyGesture`/`PanGesture` (those
keep their own handlers), so `HexMapView._UnhandledInput` also handles
`InputEventScreenTouch`/`InputEventScreenDrag`, tracking active fingers in
`_touchPoints` and feeding the new pure, unit-tested `ZoomMath.PinchZoom`
(zoom × new-sep/prev-sep) into the existing `ApplyZoom(newZoom, midpoint)`.
A second finger landing cancels the in-flight finger-0 drag, and a
`_gestureWasPinch` flag swallows the trailing emulated finger-0 release so
ending a pinch never registers a spurious tap/rally. Pinch begin/update/end
log under `Log.LogCategory.Input`. The gesture state machine is view-layer
(test-excluded); only `PinchZoom` is unit-tested, and the on-device pinch is
verifiable only on real touch hardware (Mac trackpad exercises the
`MagnifyGesture` path, not this one).

## Platform builds & orientation

Build/export **mechanics** for all four targets — the `export_presets.cfg`
presets, the `tools/build_{macos,windows,android,ios}.sh` scripts, the common
`dotnet build -c Debug` + `-c ExportDebug`/`ExportRelease` + headless-export
shape, the net8-vs-net9 gradle workaround on Android (and the corresponding
*non*-issue on iOS, where the generated Xcode project's build phases run
`dotnet publish` against net8 directly), APK signing, and the iOS chain
(xcodebuild archive → exportArchive → altool for TestFlight or devicectl for
tethered USB install, with Team ID sed-injected into the empty preset slot
and restored on EXIT so secrets stay out of the repo) — all live in
**`RELEASE.md`**, alongside the on-device install / log-reading /
scale-reproduction workflow. This section keeps only the architectural pieces
that the build docs reference.

### Orientation

`project.godot` sets `display/window/handheld/orientation=6` (Godot
"Sensor" → Android manifest `screenOrientation="13"` / `fullUser`), so
the app follows the device through all four orientations when the
phone's auto-rotate is on. No code change was needed: the
`OrientationHud` layer (see *Responsive layout* above) resolves
orientation from the live viewport size and relayouts on every
`SizeChanged`, so a rotation that resizes the viewport flips the board
and HUD automatically. **Gotcha:** the setting key is `handheld`, not
`handle` — Godot silently ignores an unknown key and keeps the default
landscape (0).

### Rotation transition (`RotationFix` Android plugin)

A rotation triggers an Android display **freeze**:
`startFreezingDisplayLocked` snapshots the old-orientation frame and
stretches that snapshot into the new screen bounds until the app
redraws — one visibly distorted frame per rotation. The snapshot is
taken *before* the app is notified (config change / `SizeChanged`), so
nothing in `OrientationHud` / `HexMapView` can pre-empt it — their
relayout already settles in ~6ms (see the `resize@frame` / `settled`
`Render`-category logs in each one's `OnViewportResized`, kept as
permanent instrumentation). The clean fixes don't apply: there is no
`android:windowRotationAnimation` theme attribute (aapt rejects it),
and the only rotation mode that skips the snapshot (`SEAMLESS`)
requires an opaque fullscreen window, which Godot's translucent GL
`SurfaceView` prevents (a plugin can't force it opaque).

So the workaround is a small **Godot v2 Android plugin, `RotationFix`**:

- **Source:** `android_plugin/rotationfix/` — a Kotlin
  `RotationFixPlugin : GodotPlugin`, built to an AAR by
  `tools/build_android_plugin.sh` (its own gradle project, compiles
  against `org.godotengine:godot:4.6.1.stable`, mirrors the build
  template's SDK/AGP/Kotlin versions).
- **Wiring:** `addons/rotationfix/` — `plugin.cfg` + an
  `EditorExportPlugin` (`rotation_fix_export.gd`) whose
  `_get_android_libraries` links the AAR into the gradle build;
  enabled in `project.godot` `[editor_plugins]`.
  `tools/build_android.sh` auto-builds the AAR on first run if it's
  missing (it's a gitignored `bin/` artifact). The plugin class is
  discovered via the AAR manifest's
  `org.godotengine.plugin.v2.RotationFix` meta-data.
- **Behavior:** the plugin watches the **physical orientation sensor**
  (`OrientationEventListener`) — the only signal that arrives before
  the freeze — and, on crossing an orientation band, drops an opaque
  black `TYPE_APPLICATION_PANEL` window over the surface, so the OS
  snapshots black (a stretched black is invisible). It's removed
  `DISPLAY_SETTLE_MS` (600ms) after the rotation actually lands
  (`DisplayManager.DisplayListener.onDisplayChanged`), with a
  `FALLBACK_MS` (1000ms) safety net for tilts that never complete a
  rotation. Self-skips when auto-rotate is off.

This is a heuristic (hand-tuned hold, can blank on an incomplete tilt).
Its limitations — and a recorded dead end (a Godot-frame-driven removal
can't work: the stretch is gated by the OS freeze *thaw*, which lands
well after Godot's resize callback and isn't observable from the render
loop) — are recorded in issue #9.

## Logging (`Log`)

`src/FourExHex.Model/Log.cs` is the master logging system — one
Godot-free static class shared by Model, Controller, and the Godot
`scripts/` layer (it has no namespace, so call sites need no `using`).
It replaces the old `AiLog`.

- **Two independent gates.** (1) Compile-time: `Log.Trace` / `Debug` /
  `Info` are `[Conditional("DEBUG")]`, so the C# compiler removes the
  call *and its argument evaluation* (interpolated strings included)
  from Release/exported builds — instrumentation can be left in the
  code permanently and is provably inactive in a shipping build.
  `Log.Warn` / `Error` always compile (genuine anomalies + the
  headless-run terminator survive). (2) Runtime: each
  `Log.LogCategory` (`Ai`, `Turn`, `Capture`, `Tutorial`, `Render`,
  `Input`, `Display`, `Hud`, `Undo`, `Cheat`, `Campaign`) has an
  independent minimum `Log.LogLevel`; a message emits only if its level ≥
  the category threshold.
- **Default is silent.** Every category defaults to `Off`, so normal
  dev play prints nothing until configured.
- **Configuration.** `Main` calls `Log.Configure(OS.GetEnvironment(
  "FOUREXHEX_LOG"))`, parsing a spec like
  `"Ai:Debug,Turn:Info,*:Warn"` (comma-separated `category:level`,
  `*` = all; case-insensitive; unknown tokens ignored; never throws).
  No UserSettings/UI exposure.
- **Helpers that pre-compute** (`GameController.LogTurnStart`,
  `LogAction`, `LogGameEndDiagnostics`, `LogCaptureDiff`) are
  themselves marked `[Conditional("DEBUG")]` so the body — not just
  the print — strips in Release. `Warn`/`Error` sites keep their
  precompute (they must run in shipping).
- `GD.PushWarning` / `GD.PushError` (user-facing save/load failures)
  are deliberately **not** routed through `Log` — they are not gated
  instrumentation.

## Diagnostic mode (`FOUREXHEX_6AI`)

Setting the env var before launching Godot reconfigures the session
for a fully headless regression run:

- All six player slots forced to `PlayerKind.Computer` (the menu also
  detects the env var and skips itself, so the launch jumps straight
  into `Main`).
- After parsing `FOUREXHEX_LOG`, `Main` pins `Log` to the verbose
  AI/turn output the old `AiLog.Enabled = true` produced —
  `Ai:Debug`, `Turn:Info`, `Capture:Debug` — set *after* `Configure`
  so a stray `FOUREXHEX_LOG=*:Off` can't silence the harness.
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

Files are grouped by responsibility below. The **project** a file
belongs to follows the split in "Project structure & the Godot-free
model" above. Three source trees:

- `scripts/` (the `FourExHex` Godot project) — Godot
  `Node`/scene/view/filesystem code plus the `PlayerPalette` /
  `HexPixel` view adapters.
- `src/FourExHex.Model/` (the `FourExHex.Model` library) — pure model,
  rules, AI (incl. `AiDispatcher`), `UndoStack<T>` +
  `GameStateSnapshot`, save serialization (`SaveSerializer`, `Replay`,
  `ReplayBeat`, the `Tutorial` POCO), `MapGenerator` / `MapEditPaint`
  / `EditorSnapshot`, `PlayerId`.
- `src/FourExHex.Controller/` (the `FourExHex.Controller` library,
  references Model one-way) — `GameController`, `SessionState` /
  `SessionStateSnapshot` / `UndoEntry`, the `IHexMapView` /
  `IHudView` / `IAiPacer` interfaces, `AiPacer` / `GodotAiPacer`, and
  the `Tutorial/` Record/Preview scripting helpers (everything in
  `Tutorial/` except the `Tutorial` POCO).

The tree below keeps the historical `scripts/` prefix only as a
grouping label; the per-file project is per the lists above.

```
scripts/  (split: see the three source trees listed just above)
├─ Main.cs                ─ play scene root; wires model + views + controller
├─ MainMenuScene.cs       ─ landing (Resume / Play / Play Tutorial /
│                           Load / Map Editor / Settings + desktop-only
│                           Exit) + the paged New Game flow (player setup
│                           / map setup, fill-to-cap surface; see "New
│                           Game setup & map thumbnail"); Load Game modal;
│                           instantiates SettingsPanel as a modal
│                           overlay; Exit / landing-Escape open a
│                           ConfirmModal before quitting; writes
│                           GameSettings + LoadRequest
├─ MapThumbnailView.cs    ─ New Game live board preview: renders the real
│                           HexMapView into a hidden SubViewport and
│                           snapshots it to a TextureRect (see "New Game
│                           setup & map thumbnail")
├─ PlayTutorialScene.cs   ─ end-user "Play Tutorial" scene root; hosts
│                           MapEditorPanel + PreviewPane + EscMenu,
│                           loads bundled full_tutorial and plays it
│                           (Esc → Resume / Main Menu)
├─ MapEditorScene.cs      ─ editor scene root; chrome host (HUD,
│                           Save/Load dialogs, EscMenu modal with
│                           Resume / Save Map / Load Map / Exit
│                           options, Escape→hand→modal ladder)
├─ MapEditorPanel.cs      ─ reusable editor body; owns HexMapView + draft
│                           grid/water/territories + UndoStack<EditorSnapshot>
│                           + paint stroke state + hover tooltip
├─ MapEditorHudView.cs    ─ editor HUD (seed entry + palette + undo/redo
│                           + single Options button). Configurable
│                           via ShowSceneRootChrome (gate the Options
│                           button) and TopOffsetPx (offset entire
│                           strip). Save Map / Load Map live in the
│                           EscMenu now, wired by the host scene
├─ TutorialBuilderScene.cs─ tutorial builder scene root; TutorialMode
│                           { MapEdit, Record, Preview } state machine;
│                           hosts MapEditorPanel + a MapEditorHudView
│                           (ShowSceneRootChrome = true so its Options
│                           button opens the menu) + RecordPane +
│                           PreviewPane + EscMenu modal (mode switches
│                           + Save/Load Tutorial + Exit); captures/
│                           restores the draft EditorSnapshot around
│                           play sessions
├─ EscMenu.cs             ─ shared pause/exit modal (CanvasLayer +
│                           centered panel; ProcessMode = Always so it
│                           works in both paused and unpaused hosts);
│                           host scenes call Show with a mode-aware
│                           option list. ESC closes when open and fires
│                           EscapeClosed (separate from the generic
│                           Closed) so the pause coordinator can
│                           distinguish "user backed out" from button
│                           clicks. Used by Main, MapEditorScene,
│                           TutorialBuilderScene, CheatMenu
├─ CheatMenu.cs           ─ Debug-only cheat menu (#if DEBUG; thin
│                           input listener owning an EscMenu modal);
│                           backquote / 3-finger tap toggles it over
│                           any screen; Tutorial Builder + Close
│                           entries; scene roots opt in via
│                           CheatMenu.Attach(this) — no autoload
├─ SettingsPanel.cs       ─ shared Settings modal (CanvasLayer +
│                           backdrop + SFX/VFX checkboxes + speed rows
│                           + Credits + Back); Open() / Close() / Closed
│                           event; owns + opens the CreditsPanel. Used by
│                           MainMenuScene's landing Settings button
│                           and Main's pause-menu Settings option
├─ CreditsPanel.cs        ─ Credits modal (CanvasLayer at Layer 101,
│                           one above SettingsPanel; backdrop + centered
│                           PanelContainer + scrollable BBCode credits
│                           (author name links to the repo via
│                           MetaClicked → OS.ShellOpen) + Back);
│                           Open() / Close() / Closed event.
│                           Owned + opened by SettingsPanel
├─ ConfirmModal.cs        ─ reusable yes/no confirm modal in the
│                           ModalChrome family (backdrop + centered
│                           panel + serif title + gold rule + message +
│                           Cancel / confirm buttons); ctor takes
│                           title/message/confirm-label; Confirmed /
│                           Canceled events; Escape cancels, Enter
│                           confirms. Used by MainMenuScene's Exit flow
├─ SlotPickerDialog.cs    ─ reusable load-slot picker built on the
│                           shared modal shell (CanvasLayer + dim
│                           ColorRect backdrop + centered PanelContainer
│                           with the theme's slate Panel stylebox);
│                           ShowSlots(slots, emptyMsg, labelFor,
│                           onPicked) + ShowError (inline error panel);
│                           ProcessMode = Always so it works during
│                           in-game pause. Builds its shell from
│                           ModalChrome (shared with the other modals).
│                           Used by MainMenuScene,
│                           MapEditorScene, TutorialBuilderScene, and
│                           Main's pause-menu Load Game option
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
├─ MapEditPaint.cs        ─ pure paint helpers (Land / Neutral / Capital /
│                           Tower / Tree / Water)
├─ EditorSnapshot.cs      ─ deep copy of editor draft (grid + water + terr.)
├─ HexPaletteButton.cs    ─ hex-shaped palette swatch Control;
│                           delegates Tree/Capital/Tower/Hand glyphs
│                           to HudIcons helpers (shared with HudView)
├─ HexHoverTooltip.cs     ─ editor-only floating tooltip showing the
│                           hovered hex's lex index + (col, row)
├─ HexDragMode.cs         ─ Pan | Paint enum gating HexMapView's
│                           left-button gesture interpretation
├─ GameSettings.cs        ─ global player config (PlayerConfig, PlayerKinds,
│                           optional MasterSeed)
├─ LoadRequest.cs         ─ static one-shot handoff: menu Load → Main
├─ GameController.cs      ─ pure C# orchestration: input event
│                           handlers, AI/replay step machines, instant
│                           driver, recording/undo bookkeeping
├─ GameOperations.cs      ─ mutation/orchestration core shared by live
│                           AI and replay drive: ExecuteAi*, HandleCapture,
│                           DeclareWinner, DispatchActionSound, ApplyLong-
│                           PressRally, EndOfTurnProcessing, Advance-
│                           ToNextActivePlayer, StartPlayerTurn, Refresh-
│                           Views, CheckGameEndConditions, Refresh-
│                           SilentMode, etc. See "GameController ↔
│                           GameOperations split" above
├─ ReplayRecorder.cs      ─ replay subsystem: the beat log, initial
│                           snapshot, undo/redo beat-stack bookkeeping,
│                           paced + instant playback step machines.
│                           RecordBeat, BeginReplay/EndReplay/Step-
│                           Replay*, ExecuteReplayBeat, ReplayApply-
│                           EndTurn, ReplayInstantStep. Calls into
│                           GameOperations one-way. Hosts the top-level
│                           InstantStep enum shared with GameController's
│                           InstantAiTick. See "GameController ↔
│                           ReplayRecorder split" above
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
├─ HudView.cs             ─ concrete HUD: 96-px slate bar (bottom in
│                           landscape; split display-top / controls-
│                           bottom in portrait, see Responsive layout)
│                           + defeat / claim-victory /
│                           victory overlays + bottom-anchored
│                           tutorial-message popup + top-anchored
│                           bankruptcy toast (red pill with the
│                           same triangle warning glyph the map's
│                           capital badge uses). Buy/Build always
│                           visible; tooltips name the reason when
│                           disabled.
├─ HudIconButton.cs       ─ Button subclass painting a programmatic
│                           glyph via _Draw; carries Selected (mode
│                           cue), CtaActive (CTA stylebox color flip),
│                           BuyLevel (recruit→commander icon escalation).
│                           DefaultTooltip(HudIcon) is the single
│                           source for "<label> — <hotkey>" strings
│                           shared by HudView + MapEditorHudView.
├─ HudIcons.cs            ─ static glyph helpers shared by
│                           HudIconButton + HexPaletteButton (tree,
│                           capital, tower, hand, unit rings, curved
│                           arrow ± nested, end-turn triangle, gear,
│                           isometric d6 die for map-editor Generate)
├─ UiPalette.cs           ─ static design-token C# constants (surfaces
│                           incl. HudBar, lines, ink, brass, water, the
│                           ModalBackdrop scrim) consumed by view code
│                           that paints directly (HexMapView water +
│                           per-tile borders, HUD bg + chip Panels,
│                           dialog gold-rule decorations). Heraldic
│                           board-game palette lerped 50% back toward
│                           the original saturated primaries.
├─ BoardPalette.cs        ─ static fixed colors for the board itself
│                           (RejectRed, ForestCanopy/Trunk, CastleFill,
│                           GraveCross, WarnRed/Yellow); shared by
│                           HexMapView's on-tile art + HudIcons swatches.
│                           Distinct from UiPalette (chrome) + PlayerPalette
│                           (roster).
├─ ModalChrome.cs         ─ static builders for the CanvasLayer modal
│                           shell (BuildBackdrop, fixed + content-sized
│                           BuildCenteredPanel, BuildPanelHead) plus
│                           PalettePanelStyle(); shared by SlotPickerDialog,
│                           SettingsPanel, CreditsPanel, ConfirmModal,
│                           EscMenu, and the HUD palette-group panels.
├─ HeadlessViews.cs       ─ no-op view stubs for diagnostic mode
├─ AudioBus.cs            ─ autoload Node singleton: shared SFX players
│                           that survive scene changes; each Play* gates
│                           on UserSettings.SfxEnabled
├─ UserSettings.cs        ─ static class; SfxEnabled / VfxEnabled /
│                           AiSpeed / ReplaySpeed preferences persisted
│                           to user://settings.json (lazy load, atomic
│                           tmp+rename save). AiSpeed/ReplaySpeed are
│                           two settings of one shared PlaybackSpeed
│                           enum (numeric-persisted; order fixed).
│                           SpeedMultiplier maps Slow/Normal/Fast →
│                           2/1/0.5; Instant has no arm (chunked
│                           driver via ScheduleUnscaled instead).
│
├─ AiPacer.cs             ─ IAiPacer (Schedule + ScheduleUnscaled +
│                           Cancel) + SynchronousAiPacer (drains both
│                           inline) + ITimerFactory abstraction
├─ GodotAiPacer.cs        ─ Default production pacer; uses
│                           ITimerFactory + generation counter for
│                           Cancel-then-reuse safety (testable via
│                           ManualTimerFactory). One ScheduleTimer
│                           helper: Schedule scales by the optional
│                           Func<float> delayMultiplier (Slow/Normal/
│                           Fast); ScheduleUnscaled passes the delay
│                           through. Always frame-yields — no inline
│                           trampoline (the chunked driver owns stack
│                           depth by returning between ticks).
├─ SceneTreeTimerFactory.cs ─ Production ITimerFactory wrapping
│                           SceneTree.CreateTimer (test-excluded).
│                           Passes processAlways: false so AI pacing
│                           halts when Main's pause coordinator sets
│                           GetTree().Paused = true
├─ AiAction.cs            ─ AiMoveAction / AiBuyUnitAction / …
├─ AiCommon.cs            ─ shared candidate-action enumeration
├─ AiDispatcher.cs        ─ routes by Player.Kind
├─ AiSimulator.cs         ─ Clone + apply for 1-ply lookahead;
│                           throws on unsupported AiAction kinds
├─ AiStateScorer.cs       ─ scoring function for ComputerAi
├─ ComputerAi.cs          ─ 1-ply best-score chooser
├─ Log.cs                 ─ master logging (category × level,
│                           [Conditional("DEBUG")] strip)
│
├─ MapGenerator.cs        ─ CA-driven land/water carve + tree scatter
├─ TerritoryFinder.cs     ─ pure rules
├─ TerritoryLookup.cs     ─ FindContaining / FindOwnedContaining /
│                           FindByCapital / OwnedCapitalBearing helpers
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
├─ HexTile.cs             ─ pure model: Coord, Owner, Occupant (no
│                           Godot/view ref — fills owned by HexMapView)
├─ HexOccupant.cs         ─
├─ Unit.cs                ─ + UnitLevel + UnitLevelExtensions
├─ Capital.cs             ─
├─ Tower.cs               ─
├─ Tree.cs                ─
├─ Grave.cs               ─
├─ Territory.cs           ─ + TerritoryExtensions
├─ Player.cs              ─ + PlayerKind
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
`EscMenu.cs`, `CheatMenu.cs`, `SettingsPanel.cs`, `CreditsPanel.cs`, `ConfirmModal.cs`,
`SlotPickerDialog.cs`,
`RecordPane.cs`, `PreviewPane.cs`, `HexPaletteButton.cs`,
`HexHoverTooltip.cs`, `HexMapView.cs`, `HudView.cs`,
`SceneTreeTimerFactory.cs`, `HeadlessViews.cs`, `SaveStore.cs`,
`AudioBus.cs`, and `UserSettings.cs` are NOT compiled into
the test assembly — they derive from Godot nodes or depend on `SceneTree`
/ Godot `FileAccess` / autoload lifecycle, so they stay in the
`FourExHex` (Godot) project. The test project `<ProjectReference>`s
both `src/FourExHex.Model` and `src/FourExHex.Controller` and has NO
per-file `<Compile Include>` list and NO GodotSharp reference: a new
testable source file is picked up automatically as long as it lives in
`src/FourExHex.Model/` or `src/FourExHex.Controller/`. If it needs
Godot it does not belong in either library — put it in `scripts/` and
test the Godot-free logic it calls instead.

## Tests

Run with `dotnet test`. The suite covers every static rule class,
the `GameController` click + turn state machine (with mock views and
the synchronous pacer), `Treasury`, `UndoStack`, `GameStateSnapshot`,
both AI flavors, the editor's paint helpers + `EditorSnapshot`
round-trip, save/serialize/deserialize equivalence, RNG determinism
across save/load, replay recording + playback contracts, and a
6-heuristic-AI replay-fidelity test that hashes the live final
state, round-trips it through SaveSerializer, and asserts the
replayed state matches digest-for-digest. Also covers `PlayerId`
semantics, the `Log` category/level gate, `HexCoord.Round`, and v2→v7 save
migration (`SaveMigrationTests`). The view layer is deliberately
uncovered — it depends on Godot's `Node` lifecycle, so pin behavior
in the controller and rules instead.

That `dotnet test` builds and passes against `FourExHex.Model` +
`FourExHex.Controller` with **zero GodotSharp on the reference graph**
is itself the purity test: if either library ever takes a Godot
dependency — or if model code ever names a controller-layer type —
the build stops compiling and the whole suite goes red.

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
