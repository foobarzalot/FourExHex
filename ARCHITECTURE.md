# FourExHex Architecture

Current snapshot; start here. MVC split (Main → GameController → views / model / rules) is the load-bearing structure.

## Project structure & the Godot-free model (read this first)

Four C# projects, layered Model → Controller → game (test project alongside):

- **`src/FourExHex.Model/FourExHex.Model.csproj`** — plain `Microsoft.NET.Sdk`, **no GodotSharp, no controller reference**. Pure model: state types, static rules, AI subsystem (incl. `AiDispatcher`), generic `UndoStack<T>` + `GameStateSnapshot`, save serialization (`SaveSerializer`, `Replay`, `ReplayBeat`, `Tutorial` POCO), `MapGenerator` / `MapEditPaint` / `EditorSnapshot`, and `ProceduralGame` (shared seed→`GameState` pipeline for play scene + menu thumbnail).
- **`src/FourExHex.Controller/FourExHex.Controller.csproj`** — plain `Microsoft.NET.Sdk`, `<ProjectReference>`s **only** `FourExHex.Model` (one-way). Orchestration: `GameController` (input handling + turn rotation), `GameOperations`, `AiTurnDriver`, `ReplayRecorder`, `StepPacing` (shared step-beat delay constants + instant↔paced re-dispatch skeleton); UI-scoped `SessionState` + `SessionStateSnapshot` + `UndoEntry`; `InstantStep` enum (shared by AI + replay step machines); `IHexMapView` / `IHudView` / `IAiPacer` interfaces; AI pacers (`AiPacer` / `GodotAiPacer`); the user-facing string store (`Strings` facade + `StringTable` + `StringKeys` — see **User-facing strings**); `Tutorial/` Record/Preview helpers (all of `Tutorial/` except the model-side `Tutorial` POCO).
- GodotSharp on neither graph → both physically can't depend on Godot (`using Godot;` won't compile). Model lacks a Controller reference → can't name `GameController` / `SessionState` / view interfaces (`CS0246`). Both compiler-enforced.
- **`src/FourExHex.ViewMath/FourExHex.ViewMath.csproj`** — plain `Microsoft.NET.Sdk`, **no GodotSharp**, one-way `<ProjectReference>` to `FourExHex.Model` (primitives like `HexCoord`). Godot-free view math needing floats: `DisplayScaleMath`, `SafeAreaMath`, `MapPlacement`, `PanMath` (inset-aware play-area center + pan clamp: centered-axis lock / padded-rotated-AABB clamp), `ZoomMath`, `ScreenLayout`, `HudPanelMath` (HUD-panel sizing: width clamped to viewport, height to fit wrapped text), `KeyboardAvoidance` (mobile keyboard panel lift), `MultiTouchTapDetector` (3-finger-tap debug cheat menu; fires on third concurrent touch, re-arms after all lift), `EditorPaletteLayout` (brush-grid column wrapping), `ThumbnailLayout` (contain-fit for offscreen board viewports — `FitInside` plus `OrientedFit`, which swaps the grid aspect tall in portrait so the hosted HexMapView rotates like the in-game map; shared by the New Game thumbnail and the Instructions demo view), `StepperMath` (integer −/value/+ stepper logic: snap-to-step clamp, neighbour offset, digit parse), `HudTourSteps` (wrap-around cursor over the ordered guided-UI-tour steps — `Next`/`Prev` wrap, `JumpTo(step)` for click-to-jump), `PanelFitMath` (centered-panel shrink-to-fit: `AvailableBox` / `ScaleToFit` (never upscale) / `WidthFitWithHeightCap` (Credits) / `CappedFill` (landscape chrome) — shared by MainMenuScene, SlotPickerDialog, SettingsPanel, CreditsPanel, LandscapeMenuChrome), `HitTestMath` (`InOffsetBounds(col, row, cols, rows)`: is an offset coord on the Cols×Rows board — hover/paint gating + water-rim skip), `EasingMath` (`SmoothStep(t)` clamped-`[0,1]` ease-in/out + `Lerp(a, b, t)`, driving HexMapView's camera-pan animation), `HexRounding.Round(float, float)`. Pressure-relief valve for the no-floats rule below.
- **`FourExHex.csproj`** (`Godot.NET.Sdk`) — the game. `<ProjectReference>`s **all three** Godot-free libs; adds `src/**/*` to `DefaultItemExcludes` (else the Godot glob recompiles moved sources, duplicating types). Only Godot `Node`/scene/view code in `scripts/`: scene roots, `HexMapView`/`HudView`, editor + tutorial-builder panels, `SaveStore`, `AudioBus`, `SceneTreeTimerFactory`, `HeadlessViews`, the two view-boundary adapters below.
- **`tests/FourExHex.Tests.csproj`** — `<ProjectReference>`s **all three**, **no GodotSharp, no `<Compile Include>`**. Compiling/passing with zero Godot on its graph is the compile-time purity proof.

### No floating-point in Model or Controller

`float`/`double` are non-deterministic across platforms/compilers/JIT, so any on the game-state path is a multiplayer desync time bomb. **Both `FourExHex.Model` and `FourExHex.Controller` are integer-only** — no `float`/`double` fields, properties, parameters, return types, or locals. AI scoring (`AiStateScorer`, `ComputerAi`), map-gen probability (`MapGenerator`), pacer timing (`GodotAiPacer`), and rule helpers use `int`/`long`. Fractionals are fixed-point ints (`InitialLandPercent = 65`; speed multipliers `50/100/200` for Fast/Normal/Slow).

Enforced by `tests/NoFloatsInModelOrControllerTests.cs`: reflects over both assemblies, asserts no signature or method body mentions `float`/`double` (incl. `Nullable<>`, arrays, generic args), failing `dotnet test` with every offender listed.

**Game-state RNG is `DeterministicRng`** (`src/FourExHex.Model/DeterministicRng.cs`): a splitmix64 generator with unbiased integer-only bounded draws (Lemire multiply-high with rejection) — `NextUInt()`, `NextBounded(max)`, `NextBounded(min, max)`, `NextFullRangeSeed()`, plus a `StreamHash` FNV-1a-64 digest of the consumption trace. `System.Random` is banned from Model and Controller (its `Next(max)` overloads hide a BCL `double` multiply), enforced by `tests/NoSystemRandomInModelOrControllerTests.cs` with one allowlisted exception: `SeedFormat.NextSeed(Random)`, the off-path fresh-master-seed fallback that `scripts/` feeds `Random.Shared` into. Bit-exact output vectors are pinned by `DeterministicRngTests` — they ARE the cross-runtime determinism contract.

Legitimate view-side float math (DPI scaling, safe-area insets, pixel/hex geometry, zoom smoothing) lives in `FourExHex.ViewMath`. Game + tests reference all three; Model + Controller don't reference ViewMath (one-way, compiler-enforced).

Consequences:

- **Player identity is `PlayerId`**, a Godot-free `readonly struct` (roster index; `PlayerId.None` == default == "unowned", encodes as owner-index `-1`). Model carries no color; every owner/winner/actor field — `HexTile.Owner`, `Player.Id`, `Territory.Owner`, `SessionState.Winner`, `PendingDefeatScreen`, `PendingClaimVictory`, etc. — is a `PlayerId`.
- **Color is view-only.** `scripts/PlayerPalette.cs` maps `PlayerId ↔ Godot.Color` from `GameSettings.PlayerConfig` hex strings.
- **Pixel projection is view-side.** `HexRounding.Round(float qFrac, float rFrac) -> HexCoord` in `FourExHex.ViewMath` is the float→int boundary keeping `HexCoord` integer-only in Model. `scripts/HexPixel.cs` owns `ToPixel`/`FromPixel`, calls `HexRounding.Round`.
- **`Log` is Godot-free** — routes through injectable `Log.Sink`, wired by `Main` to `GD.Print`. See **Logging**.
- **Save format is v19.** Ownership is a player index on the wire (−1 = `None`); claim-victory tiers persist by index. Gold/mountain are mutually exclusive on the wire (two bools); a tile with both normalizes to mountain-only on load; in-memory it's one `HexTile.Feature` enum that can't hold both (see **Mountain tiles**). Saves v2–v18 still load, absent fields defaulting (`IsGold`/`IsMountain` → `false`, `Difficulty` → `Soldier`, Rising Tides `Mode`/`PendingTide` → off/empty, Fog Of War `Seen` → empty, Viking Raiders `VikingAtSea`/`VikingNextWave`/`VikingLastRound`/`VikingLastSpawnRound` → empty/0), with legacy color-hex claim data and pre-v6 unit-level names migrated via `GameSettings` palette matching and `SaveSerializer.ParseUnitLevel`. A save carrying a `Replay` block also carries `ReplayVersion` (`SaveSerializer.CurrentReplayVersion`); on load a mismatched/absent stamp drops the replay (game state loads normally — the board is baked into the save), except a `Tutorial` save, whose replay is its content: that fails loudly.
- **`.cs.uid` sidecars**: model files under `src/` aren't Godot resources → no `.cs.uid`; `src/**` is `.gdignore`d. `scripts/` files keep their tracked `.cs.uid`.

## Layered view

```
SCENE ROOT (Godot) ─ Main (Node2D), play scene root (res://scenes/main.tscn). Owns no game logic/state.
  _Ready:
    1. Read GameSettings (player kinds + optional MasterSeed; forced all-Computer when FOUREXHEX_6AI).
    2. Consume + clear LoadRequest.Pending (menu Load flow).
    3. Pick master seed: load > GameSettings.MasterSeed > SeedFormat.NextSeed(Random.Shared). One seed drives map gen + per-turn RNG.
    4. Build model, three branches:
         • In-progress save (TurnNumber > 0): state, players, max-turn cap, OriginMapName from save.
         • Starting map (TurnNumber == 0 on disk): saved terrain; players from GameSettings; turn 1; empty
           treasury; _originMapName = slot name.
         • Procedural: Player.BuildRoster + MapGenerator.BuildInitialGrid → TerritoryFinder.Recompute →
           new GameState (incl. WaterCoords); _originMapName = null.
       Then a fresh SessionState.
    5. Pick views: real HexMapView/HudView, or Headless* in diagnostic.
    6. Pick pacer: GodotAiPacer (delays scaled by UserSettings.SpeedMultiplier) or SynchronousAiPacer (inline).
    7. new GameController(state, session, map, hud,
         seed: <master seed>, aiChooser: AiDispatcher.ChooseForCurrentPlayer, aiPacer: pacer,
         maxTurnNumber: load ? saved : (diagnostic ? 500 : int.MaxVal),
         aiSilentMode: () => !IsReplayMode && UserSettings.AiSpeed == PlaybackSpeed.Instant,
         replayIsInstantMode: () => UserSettings.ReplaySpeed == PlaybackSpeed.Instant)
    8. Wire save/load + pause coordinator:
         • new SaveStore + (non-diagnostic) Save/Load dialogs + shared SettingsPanel.
         • controller.HumanTurnStarted → autosave write (passes _originMapName so resumes keep map identity).
         • HUD EscRequested → EnterPause (GetTree().Paused = true, EscMenu: Resume/Save/Load/Settings/Restart/Exit;
           Restart opens a ConfirmModal — confirm → RestartCurrentGame, cancel → back to the pause menu).
         • EscMenu.EscapeClosed → ExitPause.
    9. controller.Resume() (in-progress load) or StartGame() (fresh/starting map). Then
       hud.SetMapLabel(campaign "Level XX" | save name with underscores→spaces | "" for procedural).

CONTROLLER (pure C#) ─ GameController
  refs: IHexMapView _map, IHudView _hud, GameState _state, SessionState _session
  injected: master seed, aiChooser, IAiPacer, maxTurnNumber,
    aiSilentMode (Func<bool>; mute per-action AI effects, skip per-beat highlight/RefreshViews),
    replayIsInstantMode (Func<bool>; instant replay path),
    autoSelectFirstTerritory (bool, default true; off for tutorial record/preview + mechanics tests)
  exposes: MasterSeed, StartGame(), Resume(), AbandonGame()
  events: GameEnded (once on game-over or turn cap), HumanTurnStarted (auto-select first territory + autosave seam)

  subscribes in ctor:
    map.TileClicked              → OnTileClicked
    map.TileLongClicked          → OnTileLongClicked (rally)
    hud.BuyRecruitClicked        → OnBuyPressed (U: cycle Recruit→Soldier→Captain→Commander→None; no wrap)
    hud.BuyUnitClicked           → OnBuyUnitPressed (radio: enter that buy mode; re-click toggles off)
    hud.BuildTowerClicked        → OnBuildTowerPressed
    hud.UndoLastClicked          → OnUndoLastPressed
    hud.UndoTurnClicked          → OnUndoTurnPressed
    hud.RedoLastClicked          → OnRedoLastPressed
    hud.RedoAllClicked           → OnRedoAllPressed
    hud.EndTurnClicked           → OnEndTurnPressed
    hud.NextTerritoryClicked     → OnNextTerritoryPressed (Tab: descending-size, capital-coord tie-break;
                                   unvisited-this-turn first, then fresh round)
    hud.PreviousTerritoryClicked → OnPreviousTerritoryPressed
    hud.NextUnitClicked          → OnNextUnitPressed (N: weakest-first cycle, lex within tier; enters repeated-move)
    hud.NextUnitTierClicked      → OnNextUnitTierPressed (next-unit button long-press: jump to the next-higher
                                   tier's first unit, wrapping; no-op under two movable tiers)
    hud.PreviousUnitClicked      → OnPreviousUnitPressed (Shift+N: strongest-first)
    hud.CancelActionPressed      → OnCancelActionPressed
    hud.AutomateClicked          → OnAutomatePressed (toggle; see "Automate")
    hud.DefeatContinueClicked    → OnDefeatContinuePressed
    hud.ClaimVictoryWinNowClicked    → OnClaimVictoryWinNowPressed
    hud.ClaimVictoryContinueClicked  → OnClaimVictoryContinuePressed
    (NewGame/MainMenu/EscRequested handled in Main; Main's pause coordinator drives Save/Load/Settings
     from EscMenu callbacks)

  click policy state machine:
    OnTileClicked → pending-mode branch (buy/build/move) or SetSelection branch. Rejected clicks: in-range
      near-miss flashes + stays in mode; out-of-range flashes + cancels + reselects. "In range" for
      buy/move = own territory or border-adjacent; for tower = own territory only.
    OnTileLongClicked → rally: free-reposition every unmoved unit toward target (single undo step,
      PlaySound(Rally) once if any moved)

  action handlers:
    ExecuteBuyAndPlace → debit gold + MovementRules.PlaceNew → if capture: HandleCapture + EmitTerrainCaptureFx → DispatchActionSound
    ExecuteMove        → MovementRules.Move → if capture: HandleCapture + EmitTerrainCaptureFx → DispatchActionSound
    ExecuteBuildTower  → debit gold + drop Tower + PlaySound(TowerPlaced) + EmitMountainTowerFx

  AI loop (AiTurnDriver, paced via IAiPacer):
    _aiDriver.RunUntilHumanOrDone → preview → execute beats (or chunked instant track)
    GameOperations.ExecuteAiMove / ExecuteAiBuyUnit / ExecuteAiBuildTower — validate then mutate (illegal action throws)
    Pauses on SessionState.PendingDefeatScreen; resumes from OnDefeatContinuePressed → _aiDriver.Schedule

  capture reconciliation:
    HandleCapture → TerritoryFinder.Recompute(grid, prev, treasury) (= FindAll → CapitalReconciler.Reconcile
      → Treasury.ReconcileAfterCapture)
      → detect freshly-eliminated colors (capital before, none after) → PlaySound(PlayerDefeated); set
        PendingDefeatScreen for human eliminations
      → _map.RebuildAfterTerritoryChange
      → WinConditionRules.WinnerByDomination (mid-turn)

  undo/redo:
    Each human handler wrapped in TrackHandler — pushes UndoEntry (game + session snapshot) iff state changed.
    AI actions NOT undoable (undo cleared at end-of-turn).
    OnUndoLast / OnUndoTurn / OnRedoLast / OnRedoAll → ApplySnapshot

  turn rotation:
    OnEndTurnPressed → undo.Clear → EndOfTurnProcessing (win check only)
      → AdvanceToNextActivePlayer (skip capital-less)
      → StartPlayerTurn (reseed RNG → growth → reset → income → upkeep)
      → _aiDriver.RunUntilHumanOrDone

  single UI update path:
    RefreshViews() → _hud.Refresh(state, session, hasActionable)
      → _map.RefreshOccupantVisuals(currentPlayer, tr., visitedCapitals)
           // visitedCapitals = VisitedThisTurnCapitals minus the selected
           // first-visit ("worked") territory's capital — its pulse stays
           // until exhausted or deselected; revisits stay suppressed
      → _hud.SetCta(EndTurn, isHuman && (shouldLight || EndTurnCtaLatched))
           // shouldLight = !hasActionable || (all actionable territories
           // visited && (selection exhausted/null || automate-exhausted));
           // first light sets EndTurnCtaLatched (sticky; see SessionState)
      → _hud.SetCta(NextTerritory, isHuman && hasActionable
           && (selExhausted || SelectionWasRevisit) && !endTurnCta)
      → _onAfterRefresh?.Invoke() (Preview cue hook; null in ordinary play)

MODEL / STATE (pure C#)
  GameState ─ Grid, Territories, Players, Turns, Treasury, WaterCoords (off-map blockers, renderer-only)
  SessionState ─
    Winner (PlayerId?)
    PendingDefeatScreen (PlayerId? — defeat overlay)
    PendingClaimVictory ((PlayerId,percent)? — claim overlay; percent∈{50,75,90}; human-only)
    ClaimVictoryPromptedHighestThreshold (Dict<PlayerId,int>; player→top tier dismissed; persists across save/load)
    SelectedTerritory, Mode (enum), MoveSource
    VisitedTerritoryCapitals (Tab-cycle round set; resets when a full cycle wraps)
    VisitedThisTurnCapitals (turn-scoped visited set — capital-highlight
      suppression + all-visited End Turn CTA; cleared only at StartPlayerTurn
      or by undo restore)
    SelectionWasRevisit (current selection targeted an already-visited
      territory — lights the Next-Territory CTA)
    EndTurnCtaLatched (sticky End Turn CTA: set on first light, cleared only
      by StartPlayerTurn or undo unwinding past the lighting step)
    Undo (UndoStack of UndoEntry = GameStateSnapshot + SessionStateSnapshot)

VIEWS (Godot Nodes)
  HexMapView : Node2D, IHexMapView
    Init(state) — injected before _Ready
    ReloadState(state, anim) — editor terrain swap in place
    event TileClicked(HexTile?)
    event TileLongClicked(HexTile?)
    event CoordClicked(HexCoord) — every non-drag click; editor consumes
    event CoordHovered(HexCoord?) — null off-grid/HUD; editor-only (HexHoverTooltip)
    event PaintCellEntered(HexCoord) + PaintStrokeEnded — editor drag-paint
    DragMode (Pan | Paint) — Pan = click+drag-pan; Paint fires per cell, suppresses pan
    ShowHighlight(territory), ShowMoveTargets(coords, level), ShowTowerTargets(coords),
      ShowTowerCoverage(coords), ShowMoveSource(coord?), CenterOnTerritory(territory) (eases the
        camera to the anchor via EasingMath.SmoothStep in _Process; retargets on rapid re-center,
        bails on any manual pan/zoom, snaps instantly under _silentMode),
      RebuildAfterTerritoryChange(), RefreshOccupantVisuals(color, tr., visitedCapitals),
      PlayDestructionEffect(coord, occ.)
    Play{UnitPlaced, TowerPlaced, UnitCombined, UnitDestroyed, TowerDestroyed, TreeCleared, CapitalDestroyed,
      Bankruptcy, GameWon, Rally, PlayerDefeated} — audio sinks → AudioBus
    layers: borders / gold / capitals / units / towers / trees / graves / targets / highlight

  HudView : CanvasLayer, IHudView
    events: BuyRecruit (U cycle) / BuyUnit(level) (radio) / BuildTower / UndoLast / UndoTurn / RedoLast /
      RedoAll / EndTurn / NewGame / MainMenu / NextTerritory / PreviousTerritory / NextUnit / PreviousUnit /
      CancelAction / Automate / EscRequested (Options + ESC) / DefeatContinue / ClaimVictoryWinNow / ClaimVictoryContinue
    Refresh(state, session, hasAct.) (overlay priority: Winner > PendingDefeatScreen > PendingClaimVictory)
    SetMapLabel(text) // campaign "Level XX" | save name (underscores→spaces) | "" procedural
    ShowTutorialMessage(text) / HideTutorialMessage() — bottom-anchored click-through popup
    Buttons are HudIconButton (Button + _Draw) painting glyphs via HudIcons. Static tooltips from
      HudIconButton.DefaultTooltip; Buy/Build override per state. Buy row = four always-visible radio buttons
      (Recruit/Soldier/Captain/Commander); per-level Disabled + Selected mirror BuyModeLevel + affordability.
      Disabled-reason tooltips name the blocker (no selection / no capital / can't afford <level>). In buy/move
      mode the active button's tooltip clears; bottom panel shows "Click to place a X" / "Click to move the X"
      (gated by _externalMessageActive so it can't clobber tutorial / AI-batch text).
    Non-human-turn lockdown: while an AI acts (and the game isn't over) Refresh
      force-disables every action button and clears their active rings — only
      the Options gear stays live; overlay buttons gate on their own state.
      Logs "[hud] ai-turn lockdown engaged/released" (Hud:Debug) on transition.
    Help menu ("?" button, serif-glyph HudIconButton beside Options; H hotkey):
      OpenHelpMenu shows a HudView-owned EscMenu (SwallowAllKeysWhileOpen — an
      _Input override eats every key while open since the HUD doesn't pause; Escape
      closes) with three options: Instructions (opens InstructionsPanel — see the
      Instructions viewer section), UI Guided Tour (EnterTour), Close. Logs
      "[help] menu open / chose … / close" (Hud).
    Guided UI tour (from the Help menu):
      EnterTour raises TourStartRequested (HudView-only, like EscRequested; Main →
      GameController.EnsureTerritorySelectedForTour picks a territory without an undo
      entry so the gold chip renders), then shows HudTour — a CanvasLayer with a
      full-viewport click-catcher (hit-tests each element's rect → jump; eats clicks
      so real buttons and the click-through readouts don't fire), a pulsing gold ring
      tracked to the current element in _Process, and a Back/Next/Close dialog. Opens
      on the nodeless Intro page (Entry.Node null — no ring; explains how to drive
      the tour), then the ? button second, then the play controls; a _mapDim scrim
      (HudView's first child, behind the widgets) dims the board while toured
      elements stay bright; Escape/keys swallowed while up. Step list + copy built
      in HudView.BuildTourSteps (intro always present; visibility-filtered elements;
      buy row collapses to one step); order cursor is HudTourSteps (ViewMath). Logs
      "[tour] enter/step/exit" (Hud).
    HeadlessHexMapView / HeadlessHudView — no-op stubs for diagnostic mode

PURE RULES (static)
  TerritoryFinder.FindAll(grid) ─ flood-fill, no capitals
  TerritoryFinder.Recompute(grid, prev, treasury?, randomizeCapital=false, originCapital=null) ─ FindAll →
    CapitalReconciler.Reconcile → optional Treasury.ReconcileAfterCapture. Single entry for
    post-mutation rebuilds. Every in-game reconcile passes randomizeCapital: true and threads
    originCapital (the acting territory's capital) from HandleCapture / AiSimulator; the
    lex-min default is the editor/fixture path.
  CapitalPlacer.Choose(coords, grid, rng?) ─ tier (empty>unit>grave>tree>tower); in-tier pick is
    lex-min when rng null, else a random candidate from rng
  CapitalReconciler.Reconcile(raw, old, grid, randomize=false, originCapital=null) ─ split/merge +
    stomping; None-owned (neutral) stay capital-less (throws on a capital on neutral land). A merge
    keeps originCapital when it is among the merged capitals; otherwise the largest old territory's
    capital wins. randomize → fresh placement and the equal-old-size merge tiebreak draw from a
    DeterministicRng seeded by SeedFromCoords(coords). Logs each merge decision ([reconcile] merge, Capture:Debug).
  PurchaseRules.CostFor / CanAfford / CanAffordTower / IsValidRecruit…
  MovementRules.ValidTargets / Move / PlaceNew / ArrivalConsumesAction (capture/tree/grave → true)
  DefenseRules.Defense(coord, grid, territory)
  TreeRules.RunStartOfTurnGrowth / ConvertGravesToTrees / CountIncomeProducingTiles / CountGoldIncomeTiles
  IncomeRules.IncomeFor (base tiles + GoldTileBonus per gold tile)
  UpkeepRules.UpkeepFor / TotalUpkeepFor / ApplyUpkeep / ApplyUpkeepFor /
    Classify -> EconomyOutlook (Healthy / NegativeDelta / BankruptNextTurn) /
    SurvivesNextUpkeep(gold, netIncome) — shared solvency primitive (AI scorer + enumerator)
  WinConditionRules.WinnerByDomination (mid-turn) / .WinnerAtEndOfTurn (sole capital-bearer) / .IsEliminated /
    .NextClaimVictoryThreshold (50/75/90 tiers) /
    .ClaimVictoryThresholdsPercent (constant: {50,75,90})

MODEL PRIMITIVES
  HexCoord (struct, IEquatable, IComparable)
  HexGrid — Dictionary<HexCoord, HexTile>
  HexTile — Coord, Owner, Occupant, Feature (None/Gold/Mountain enum; IsGold/IsMountain accessors)
  HexOccupant (abstract):
    Unit — Owner, Level, HasMovedThisTurn
    Capital — marker
    Tower — marker (defense, no upkeep)
    Tree — blocks income; movement onto a tree consumes action + clears tile
    Grave — blocks income; converts to Tree at start of owner's next turn
  UnitLevel — Recruit=1, Soldier=2, Captain=3, Commander=4
  Territory — Owner, Coords, Capital (immutable)
  TerritoryExtensions — BuildTileIndex
  Player — Name, Id, Kind (PlayerKind), IsAi
  PlayerKind — Human, Computer, None (None = absent)
  TurnState — Players[], CurrentPlayerIndex, TurnNumber
  Treasury — Dictionary<HexCoord, int>; CollectIncomeFor; ReconcileAfterCapture (forfeits enemy gold on capture)
  GameStateSnapshot — deep-copy (tiles + gold + territories)
  SessionStateSnapshot — selection anchor + Mode + MoveSource + RepeatedMovement flag + visited capitals
    (sorted; hand-written sequence equality)
  UndoEntry — pair of (GameStateSnapshot, SessionStateSnapshot)
  UndoStack<T> — two-sided history of T (UndoEntry for play; reused by editor with EditorSnapshot)
  TerritoryLookup — FindContaining / FindOwnedContaining / FindByCapital / OwnedCapitalBearing
  MapGenerator — CA land/water carve + density-driven tree/mountain/gold scatter (MapGenOptions densities)
  GameSettings — PlayerConfig (name, color hex) + PlayerKinds + Difficulties (per-slot) + optional MasterSeed;
    written by MainMenuScene, read by Main
  LoadRequest — static one-shot handoff from menu Load to Main (consumed + cleared in _Ready)
  SaveStore — user://saves/ slot CRUD + user://maps/ starting maps + res://tutorials/ bundled (read-only):
    WriteAutosave / WriteSlot / ListSlots / LoadSlot, WriteMapSlot / ListMaps / LoadMap / LoadBundledMap;
    reserved "autosave" slot
  SaveSerializer — JSON (de)serializer for full state + starting maps (v11: maps bake Kind+Difficulty;
    slot-keyed owners for 2–6 players; OriginMapName carried)
  LoadedSave — (state, players, master seed, max-turn cap, slot name, OriginMapName?, MapHasBakedKinds)
  SaveSlotInfo — slot listing metadata (name, time, turn, isAutosave)
  UserSettings — static; SfxEnabled / VfxEnabled / AiSpeed / AutomateSpeed / ReplaySpeed persisted to
    user://settings.json (lazy load, atomic tmp+rename); read by AudioBus + HexMapView + GodotAiPacer +
    GameController, written by SettingsPanel. AiSpeed + AutomateSpeed + ReplaySpeed are independent settings
    of one shared enum PlaybackSpeed {Slow,Normal,Fast,Instant} (member order load-bearing — persists
    numerically). SpeedMultiplierPercent → 200/100/50 for Slow/Normal/Fast; Instant routes AI, replay, AND
    Automate to the chunked frame-yielded driver via the pacer's ScheduleUnscaled (multiplier unused —
    "Instant is not a multiplier"; see "Automate" for the automate wrapper).

AUDIO (autoload)
  AudioBus — autoload-registered Node singleton (project.godot [autoload] "AudioBus"). Owns AudioStreamPlayer
  instances for every shared SFX — click, place/move (units, towers, combine, destroy variants), tree/grave
  clear, capital fall, bankruptcy, game-won peal, rally, player-defeated gong. All cues are one sonic family
  (the Guildhall palette — see docs/SOUND_DESIGN.md; generator source tools/sfx-engine.js), normalized to
  −3 dBFS so each player's VolumeDb is the only gain knob. Survives scene
  changes so a ChangeSceneToFile button click still plays. Static AttachClick(BaseButton) /
  AttachClick(HexPaletteButton) wire a button's Pressed signal to the shared click player.

  HexMapView.PlaySound(SoundEffect, HexCoord?) is the single sound sink the controller calls — switches on
  SoundEffect, forwards to the matching AudioBus.Play* method. The interface lets controllers fire audio
  without knowing the autoload, and lets HeadlessHexMapView stub it out.

  Each AudioBus.Play* early-returns when UserSettings.SfxEnabled is false — a single chokepoint gating gameplay
  sounds + AttachClick UI clicks. Destruction VFX (HexMapView.PlayDestructionEffect: flash + shockwave +
  shards) gates on UserSettings.VfxEnabled. Pulse/shrink/grow-in animations are always on (communicate state).

  Terrain-capture feedback: every ownership change fires IHexMapView.PlayTerrainCaptureEffect(coord,
  TerrainFeature) — gold shower (flash + gold ring burst + coin sparks) for Gold, tile shake for Mountain
  (this tile's ring is omitted from the baked mountain batch while a ring copy on the deaths layer plus the
  occupant glyph judder in sync — see _shakeSuppressedMountains / _hexAlignedDeathFx; the spawn is deferred to
  end-of-frame so it grabs the glyph RefreshOccupantVisuals just rebuilt — plus dust puffs and rock chips), and
  a baseline flash + white ring for None. A tower landing on a mountain fires the same mountain effect
  (EmitMountainTowerFx; plain-tile tower builds fire nothing). Gold/mountain also fire a terrain sound cue
  (SoundEffect.GoldCaptured glockenspiel chime / MountainCaptured deep drum thud) that LAYERS on top of the
  action's occupant cue
  — the deliberate exception to DispatchActionSound's one-cue-per-action policy, since terrain is orthogonal to
  the destroyed occupant. All of it emits through the silent-gated GameOperations wrappers and scales with the
  HexMapView.CaptureFxIntensity export (design values authored against a 104px hex, scaled by HexSize/52).

  Silent-mode (Instant AI batch / instant replay / Instant automate) is decided controller-side.
  GameOperations.IsSilent() (= aiSilentMode() && currentPlayer.IsAi, OR _replayInstantActive, OR
  InSilentAutomateBatch()) gates the controller's own per-action cues via
  EmitSound / EmitDestruction; the views play whatever they're handed, so HexMapView.PlaySound /
  PlayDestructionEffect carry NO silent gate and MockHexMapView records every cue unconditionally (the seam it
  verifies is the controller's decision, not a mirrored view policy). The one view-owned exception is the
  pinned mute (HexMapView.SetMutePinned): a hard audio-off on PlaySound / FlashRejection's AudioBus dispatch,
  set by the Instructions demo board so its looping playback never sounds over the live game — audio only,
  animations/VFX unaffected, orthogonal to _silentMode. Every cue incl. Bankruptcy/GameWon obeys
  the gate with no exceptions, so a silent batch is fully silent; a manually played human turn is never silent —
  silence covers only the fast-forwards. IsSilent() omits the PendingDefeatScreen term
  that InSilentAiBatch() carries, so the AI blow that destroys a human's capital stays silent even as it queues
  the defeat overlay.

  HexMapView still carries _silentMode (set via IHexMapView.SetSilentMode from RefreshSilentMode = InSilentAiBatch
  || _replayInstantActive || InSilentAutomateBatch) for the view-internal suppression that genuinely needs node/tween access: the
  tree/grave grow-in tweens (RefreshOccupantVisuals) + tree/grave teardown (RebuildAfterTerritoryChange), the
  Rising-Tides FX stashing (CaptureRisingTidesFx / submerge + demote), and the pan-snap shortcut.
```

## Gold tiles

A **gold tile** is an income hotspot paying its controller 5 gp/turn (vs 1). A single per-tile attribute threaded through every layer:

- **Model.** `HexTile.IsGold` — a terrain attribute orthogonal to `Owner`/`Occupant`. **Mutually exclusive with `IsMountain`**: an accessor over the single `HexTile.Feature` enum (`None`/`Gold`/`Mountain`), so setting it `true` retargets `Feature` to `Gold` and clears any mountain.
- **Income.** The 5× bonus lives in the single chokepoint `IncomeRules.IncomeFor` = `TreeRules.CountIncomeProducingTiles` + `CountGoldIncomeTiles · IncomeRules.GoldTileBonus` (bonus = 4). A gold tile under a `Tree`/`Grave` pays nothing (excluded from both counts). Real play (`Treasury.CollectIncomeFor`) and AI lookahead (`AiStateScorer`) both route through `IncomeFor`. Starting-gold seed (`SeedStartingGold`, 5×tile-count) is NOT boosted — gold affects recurring income only.
- **Persistence + undo.** Carried as `TileDto.IsGold`, through replay-initial snapshots (`GameStateSnapshot.EnumerateTiles`) and both deep-copy snapshots (`GameStateSnapshot`/`EditorSnapshot`).
- **Authoring.** Placed via map editor toggle brush (`MapEditPaint.PaintGoldToggle`, glyph `HexPaletteIcon.Gold`) — flips `IsGold` without disturbing owner/occupant, same drag-stroke add/erase locking as tree/tower brushes — and procedurally by `MapGenerator` when `MapGenOptions.GoldDensity > 0`. Generated gold is sparse **neutral** clusters.
- **Rendering.** `HexMapView`'s `GoldBordersLayer` (a `TriangleSoup` batch) draws an inset gold hex-ring band per gold tile, above territory borders but below all occupants. Filled mitered quads (one per edge, sharing corner vertices) so corners have no gaps.
- **Responsive palette.** `_paintCluster` is a `GridContainer` whose column count comes from `EditorPaletteLayout.PaintColumns` (ViewMath, unit-tested): one line on roomy screens, wraps to a 2nd row (portrait) / column (landscape) on compact. The portrait bottom bar grows and the landscape left rail widens (`HudBars.MakeRail` `width` param + `OrientationHud.LeftRailWidth` hook) to fit the wrapped grid.

## Mountain tiles

A **mountain tile** is high ground: **no defense on its own**, but any defender on it gains a **+1 bonus that radiates** to friendly neighbors. Capturable without destruction; an *empty* mountain is defenseless. A single per-tile terrain attribute (defensive, not economic). Gold and mountain are **mutually exclusive**; trees, graves, units, towers, capitals all coexist.

- **Model.** `HexTile.Feature` is the single source of truth: enum `TerrainFeature` (`None`/`Gold`/`Mountain`). `IsGold`/`IsMountain` accessors retarget `Feature`, so setting one clears the other. A mountain can be neutral or player-owned and is **passable** (units move onto, through, and die on it). No income of its own (a controlled mountain pays 1 gp).
- **Defense.** `DefenseRules.Defense` gives **any defender** (`Unit`/`Tower`/`Capital`) on a mountain `DefenseRules.MountainBonus` (+1) on top of its contribution (folded in by private `ContributionAt`, applied to any positive-contribution occupant); an **empty mountain — or one holding only a tree/grave — contributes nothing**. Boosted value radiates to same-territory neighbors like any defender (Soldier/Tower → 3, Commander → 5, Capital → 2). Contributions are `max`, not cumulative. An empty neutral mountain is capturable by any level (even Recruit); a defended one raises the capture threshold by 1. `BlockingDefenders` mirrors this (same `ContributionAt`) for the view's red-flash. Capture (`MovementRules.ResolveArrival`) transfers ownership but leaves the mountain set.
- **Rule guards.** Trees, graves, towers, **and capitals all coexist** with a mountain: trees spread onto mountains (`TreeRules.RunStartOfTurnGrowth`), a unit dying leaves a grave (`UpkeepRules.ApplyUpkeep`), towers may be built (`PurchaseRules.IsValidTowerLocation`), a capital may sit. Gold is the only exclusion.
- **Capital placement.** Capitals sit on mountains like any terrain (`CapitalPlacer.Choose`), so any 2+ owned region gets a capital; `CapitalReconciler`'s null guard only covers the impossible all-Capital case. The occupant-tier priority always holds; the **in-tier** pick (and the equal-old-size merge tiebreak) is a seed-deterministic random candidate in every game (see **Randomized selection**); the lex-min null-rng path serves editor paints and test fixtures. Which capital survives a same-owner **merge** follows the origin-capital rule (see **Origin-capital merges**).
- **Persistence + undo.** Carried as `TileDto.IsMountain`, through replay-initial snapshots (`GameStateSnapshot.EnumerateTiles`) and both deep-copy snapshots (`GameStateSnapshot`/`EditorSnapshot`). A tile with **both** gold and mountain set normalizes to mountain-only on load (mountain wins — see **Save format**).
- **Authoring.** Map editor toggle brush (`MapEditPaint.PaintMountainToggle`, glyph `HexPaletteIcon.Mountain`), same drag-stroke add/erase locking. Painting a mountain leaves tree/grave/tower/capital in place and **clears any gold** (and `PaintGoldToggle` clears any mountain) — mutual exclusion falls out of the `Feature` accessor. Also generated procedurally by `MapGenerator` when `MapGenOptions.MountainDensity > 0`; generated ranges are **neutral**, and generated gold skips mountain tiles.
- **Editor undo/sound for flag paints.** Mountain/gold paints leave the territory partition untouched. The undo push compares the pre-stroke snapshot against the live grid via `EditorSnapshot.DiffersFromGrid` (pure, unit-tested grid diff over owner/occupant/gold/mountain/water); the per-cell placement sound also checks gold/mountain flags. Both flag brushes record undo and play the sound.
- **Rendering.** No peak glyph on the map. `HexMapView`'s `MountainBordersLayer` (a `TriangleSoup` batch, same z-band as gold) draws an inset hex-ring band per mountain (`DrawMountains`), **differentially shaded as a raised "plateau"**: a near-black outer drop-shadow skirt under a bright inner top rim brightening toward a top-left light, via the per-vertex colors `TriangleSoupBuilder.AddPolygon` supports. The light is baked **screen-fixed** (counter-rotated by map angle, rebaked on portrait/landscape flip) so the highlight stays top-left in both orientations. The band sits below occupants. The editor brush **button** keeps its peak glyph (`HudIcons.MountainPeakVerts`/`DrawMountain`) — peak appears only on the button, not the map.

## Procedural trees, mountains, gold & territory clumping

`MapGenerator.BuildInitialGrid` scatters trees, mountains, and gold onto a fresh map, each driven by an integer **density** (percent of land) on `MapGenOptions`, plus **`ClumpingFactor`** (0..100) shaping how player ownership is assigned (`MapGenOptions(TreeDensity = 5, MountainDensity = 0, GoldDensity = 0, ClumpingFactor = 0)`). The record threads through `BuildInitialGrid(..., MapGenOptions? options = null)` and `ProceduralGame.Build(..., options)`. With `MapGenOptions.None` (and the no-options overload for tests/replay): density 0 gates the mountain/gold passes (zero RNG draws), `ClumpingFactor 0` gates per-cell-random owner assignment, and the **tree default 5%** scatters `land.Count * 5 / 100`. All densities are percent of `land.Count`; all scatter/clumping math is integer (no floats) and deterministic in the seed.

- **Trees** — places `land.Count * TreeDensity / 100` trees (default 5%), skipping gold/occupied tiles. **May** land on mountains. Density 0 places none.
- **Mountains** — `ScatterMountainRanges(grid, land, density, rng)`: a biased random-walk "ridge agent" per range (hex direction, mostly-straight with ±1 veers, occasional perpendicular foothill → 1–2-wide ranges), to `MountainDensity`% of land. `MarkMountain` sets the feature and **forfeits ownership (`PlayerId.None`)** (occupant left in place). Gated on `MountainDensity > 0`.
- **Gold** — `ScatterGoldClusters(grid, land, density, rng)` (after mountains, before trees): sparse small **neutral** clusters (a seed grown into a 2–4-tile blob), to `GoldDensity`% of land. `MarkGold` **skips mountain tiles** (mountain wins) and sets the gold feature + `PlayerId.None`. Gated on `GoldDensity > 0`.
- Generated mountains/gold are **neutral terrain players must capture**. They flow through `TerritoryFinder`/`CapitalReconciler` as capital-less neutral regions; `CapitalPlacer` skips neutral and mountain tiles. Tree scatter skips mountain/gold tiles.
- **Clumping** — `ClumpingFactor` controls the **owner-assignment** step (after land shape, before scatter), sparse↔clumped. `0` is per-cell uniform-random ("salt-and-pepper"), fully gated (zero extra RNG draws). `> 0` runs `AssignClumpedOwners` — a **seed-flood Voronoi**: pick a seed count interpolating with the factor (`100` → one seed/player, lower → toward `land.Count`), place seeds **farthest-point apart**, assign owners round-robin, then multi-source BFS floods every land cell to its nearest seed. In the **few-seeds regime** (`land ≥ seeds × 6`) two **Lloyd relaxation** passes re-center each seed on its region centroid and re-flood, so Voronoi **areas** come out near-equal. Regions stay contiguous and capital-placeable. Deterministic: candidate cells are sorted so every tie breaks lex-min, and the only RNG draw is the first seed. Reuses `HexCoord.Distance`. Instrumented `[mapgen] clumped owners: factor=… seeds=… lloyd=…` Debug line (category `MapGen`).
- **Surfacing.** A shared `MapGenSettingsPanel` (Godot modal, opened by a serif "?" chip — `HudIconButton` text mode) carries three **density steppers** (Trees/Mountains/Gold, 0..25% in steps of 5) plus a **Territories** dropdown, summoned from the New Game map-setup page and the map editor. Reads/writes process-wide `GameSettings.TreeDensity`/`MountainDensity`/`GoldDensity`/`ClumpingFactor`; `Main`, the map thumbnail, and the editor die build their `MapGenOptions` from those for **freeform** games. The `−`/value/`+` density rows (value editable by typing, clamped+snapped on commit) come from the shared `UiStepper` helper (sibling of `UiToggle`). The Territories row comes from the sibling `UiDropdown` helper — a caption + `OptionButton` whose entries name the clumping levels **many→one** (Many/Several/Some/Few/Very Few/One at factors 0/50/75/90/95/100), each item's id its factor value so the selection round-trips by value. Those nonlinear stops `{0, 50, 75, 90, 95, 100}` live as the single source of truth in `MapGenOptions.ClumpingFactorStops`.
- **Campaign seeds come from the baked winnable table.** `CampaignProgress.SeedForLevel(level)` reads `CampaignSeeds.ByLevel` — a generated 256-entry table where every seed carries a proof that the level's human slot can win an all-AI game at Soldier (levels whose original map, seed = level number, already proves this keep it). Regeneration/verification runbook: `CAMPAIGN_SEEDS.md`; harness: `tests/CampaignWinnerSweepTests.cs`.
- **Campaign terrain is per-level, not the freeform steppers.** `CampaignProgress.MapGenOptionsForLevel(level)` derives a level's densities deterministically from the level number: mountains present ≈55% (density 10 else 0), gold present ≈45% (density 5 else 0), trees vary across {0, 5, 10}%, and **clumping drawn from the shared `ClumpingFactorStops`** — so terrain and feel are fixed and reproducible (same level → same options → same seed → same map). The clumping draw is sequenced last so it doesn't perturb tree/mountain/gold values. `Main` uses it whenever `GameSettings.CampaignLevel` is set (freeform falls back to the steppers); the campaign confirm-sheet preview renders the same derivation via `MapThumbnailView.RequestRandom(seed, options)`.

## Rising Tides game mode

A selectable game mode, distinct from freeform-vs-campaign (`GameSettings.CampaignLevel`). `GameMode { Freeform, RisingTides, FogOfWar, VikingRaiders }` (Model) on `GameState.Mode` (default `Freeform`); Fog Of War and Viking Raiders are their own sections below. The sea eats the map; game ends only when one player remains.

**Forecast at turn start, submerge at turn end** — erosion telegraphed a turn ahead. Split in `RisingTidesRules` (Model, integer-only):

- `ForecastSubmerge(state, owner, budget, rng?)` selects shore tiles, mutates nothing, returns `IReadOnlyList<TideStep>` (`TideStep { HexCoord Coord; bool DemoteOnly }`). Plan locked on `GameState.PendingTide`.
- `ApplyForecast(state, owner, plan)` demotes/submerges those coords + `TerritoryFinder.Recompute` (the remove-tile→add-water→recompute path of `MapEditPaint.PaintWater`). `SubmergeStep` = forecast-then-apply, for phantom turns of neutral/eliminated colors.
- A **shore** tile has **<6 in-grid neighbours** (`ShoreTilesOf`). Selection always takes the most exposed tiles first — highest `WaterBorderWeight(grid, coord) = 6 − in-grid neighbours`; a more-exposed tile is never passed over. Only the **equal-exposure tie-break** varies: ascending `HexCoord` when `rng` is null, else a seed-deterministic shuffle within each exposure band (see **Randomized selection**). `GameOperations.ForecastTideForCurrentPlayer` / `MaybeRiseTidesFor` always pass `_rng`. A **mountain** shore *demotes* (`IsMountain=false`) without sinking; a non-mountain shore is removed + watered. Budget **1**.
- Timing (`GameOperations`): `StartPlayerTurn` calls `ForecastTideForCurrentPlayer` (no `TurnNumber` gate — runs from turn 1). The first player's turn-1 forecast is computed in `GameController.Resume(freshStart:true)` since `StartPlayerTurn` isn't called for the initial player (a load passes `freshStart:false`, restoring `PendingTide` from save). `EndOfTurnProcessing` runs `ApplyPendingTide` (apply + structural rebuild + defeat) **before** the win check. Phantom turns forecast+apply inline via `MaybeRiseTidesFor`.

`GameState.WaterCoords` is a mutable `HashSet` (exposed `IReadOnlySet`) with `AddWater(coord)` so it grows at runtime. Forecast gated by `Mode == RisingTides`.

**Win = last player standing.** The mid-turn check is `WinnerByDomination` in every mode (own every non-water tile — submerged tiles aren't in the grid), so a capture that only orphans an opponent into capital-less singletons does *not* force a win. Only `EndOfTurnProcessing`'s sole-capital check differs: it routes to `WinConditionRules.LastPlayerStanding(territories)`, which returns the sole capital-bearing owner (else null) regardless of whose turn ended — so an end-of-turn flood that drowns the current player's *own* last capital still crowns the surviving opponent. It runs *after* `ApplyPendingTide`. Only forced end.

**Claim-victory tiers apply.** The 50/75/90% offer (`OnEndTurnPressed`) is not suppressed. Percentage measured against current non-sunk tiles: a submerged tile is `Grid.Remove`'d, so `NextClaimVictoryThreshold` (counts `state.Grid.Tiles`) tracks the shrinking board. See *Claim victory prompt*.

**Defeat at turn end.** The flood can eliminate the player whose turn just ended, including a human. `ApplyPendingTide` calls `HandleNewlyDefeated(before)` (shared with `HandleCapture`): plays the defeat cue and raises `PendingDefeatScreen` for a human; the win check then declares any sole survivor. Only the current player can be flooded by their own tide, so `AdvanceToNextActivePlayer` skips them, and the AI loop + `OnDefeatContinuePressed` gate on `PendingDefeatScreen`.

**Telegraph (view `HexMapView`).** `IHexMapView.ShowTideForecast(IEnumerable<TideStep>)` draws the locked forecast each `RefreshViews`. A **submerging** tile cross-fades on one alpha tween: full water-color (`UiPalette.WaterDeep`) fill, cover quads hiding old foam, new foam strips. A **demote-only** mountain fades its ring band toward land color. Two guards: suppressed at Instant speed (`_silentMode`); else rebuilt only when the forecast set changes (`_shownTideForecast` diff).

**VFX/SFX at apply** (view). The submerge needs a structural repaint (`RebuildAfterTerritoryChange`): re-bake water/foam soup (`BuildWaterFoamSoup` → `_waterFoamBake.SetTriangles`) and reconcile land fills with the grid (`SyncTileFillsToGridAndRebakeWater`), at turn end. That reconcile is symmetric on grid size: it drops a drowned tile's fill when the grid shrinks, and — on a **replay/undo reset that re-grows the grid** — recreates each restored tile's fill (inserted into the fill z-band under the gold-border layer) and re-bakes water, so a resurfaced tile draws as land instead of a stale water hex under freshly-stroked borders. Effects detected up front (`CaptureRisingTidesFx`) and flushed after `ClearLayer(_deathsLayer)` (`FlushRisingTidesFx`): ripple + sink-fade + `tile_submerged` for a submerge; destruction burst (`SpawnDestruction`) + `TowerDestroyed` for a demote. Gated by `_silentMode`. `tile_submerged.wav` from `tools/generate_sounds_eleven.py`.

**Turn-start focus (human).** At every human turn start with a pending forecast, `GameController.TryFocusPendingTide` (branched to from `AutoSelectFirstTerritoryForHuman`) selects the territory containing the doomed tile — in place of the largest-actionable pick — and centers the camera on the tile via `IHexMapView.CenterOnCoord`, so the per-turn erosion is never off-screen. The territory is selected even when it has nothing actionable; a capital-less singleton pans without selecting; mountain-demote steps focus the same way. Logs `[tide] turn-start focus` under `Tide`.

**AI (tide-aware evacuation).** AI reads `GameState.PendingTide`. A move taking a unit OFF a doomed tile earns `AiStateScorer.EvacuationBonus` — a per-move delta in `ComputerAi.BestPositiveDelta` like `BuildTowerBonus`, leaving absolute `Score` untouched — and phase-4b reposition enumeration is broadened so a doomed unit may flee inland.

**Selection & round-trip.** Freeform picks the mode from the **Game Mode** selector on the Configure Game page (`GameSettings.Mode`, shared with the map editor's new-map flow); Quick Play resets to Freeform. The editor threads `_mapMode` into `BuildSaveState`; `Main`'s starting-map load forwards `pendingLoad.State.Mode`. Mode, grown water set, and `PendingTide` persist through the **v16** save format (see *Save / load*); `FOUREXHEX_MODE=RisingTides` forces it for headless 6AI runs.

**Replay fidelity (shrunken-grid rewind).** Replay rewinds to the recorded initial snapshot and re-runs every beat, recomputing the tide each turn. Since the board shrinks mid-game (`Grid.Remove`'d tiles), the rewind rebuilds the full board: (1) `GameStateSnapshot.ApplyTo` re-adds any captured tile missing from the live grid; (2) `ReplayRecorder.BeginReplay` drops re-grown coords from the water set (`GameState.RemoveWater`) and re-seeds the first player's turn-1 `PendingTide` (`ForecastTideForCurrentPlayer`), mirroring `Resume(freshStart:true)` — `StartPlayerTurn` re-forecasts later turns. Covered by the Rising Tides `ReplayFidelityTests` checksum.

**Campaign.** `CampaignProgress.ModeForLevel(level)` (deterministic, integer-only) assigns complication modes by quota: every **Soldier-tier-and-above** tier holds exactly `ComplicationLevelsPerModePerTier` (6) levels of each mode in the `ComplicationModes` rotation (Rising Tides, Fog Of War, Viking Raiders), taken as consecutive slices of a tier-seeded Fisher–Yates shuffle — appending a future mode consumes the next slice without moving existing assignments; never at Recruit. Viking Raiders levels additionally clamp their `MapGenOptionsForLevel` clumping draw to ≥90 (fragmented starts are near-unwinnable in that mode — every fragment is coastline). `Main` derives a level's mode; the confirm sheet shows a gold mode line (`MapInfoSheet`'s optional `gameMode` row), and the campaign grid marks complication levels behind the level number (`CampaignPanel` draw loop) — a **blue** circle for Rising Tides, **black** for Fog Of War, **blood-red** for Viking Raiders.

## Fog Of War game mode

A view-only restriction (`GameMode.FogOfWar` on `GameState.Mode`): rules, AI, and determinism are identical to Freeform — nothing in `GameOperations`/`WinConditionRules` branches on it. Requires **exactly one human**; freeform Configure Game locks the roster to red-human + five computers (`MainMenuScene.ApplyGameModeRoleLock`, only the human's difficulty editable), and the single-human campaign roster satisfies it for free.

**Visibility model (`VisibilityRules`, Model, integer-only).** Three tiers from the human's perspective: `VisibilityTier { Fog, Stale, Visible }`. `ComputeVisible(state, human)` = every coord in the human's **capital-bearing** territories plus each tile's neighbours (a one-hex ring, incl. water/off-map coords) — a singleton (no capital) grants no sight. `UpdateSeen` marks the visible set into `GameState.Seen` (a grow-only `HashSet<HexCoord>` exposed `IReadOnlySet`, with `MarkSeen`/`IsSeen`/`ClearSeen`, mirroring `WaterCoords`). `TierOf` = visible → Visible; else seen → Stale; else Fog. Seen memory is **excluded from undo snapshots** (sticky, like water) and so does not perturb the game-state checksum.

**Projection push.** `GameOperations.ComputeFogView` calls `VisibilityRules.BuildProjection(state)` → `FogView { IReadOnlySet<HexCoord> Visible; IReadOnlySet<HexCoord> Seen }`, pushed via `IHexMapView.ShowFog(FogView?)` each `RefreshViews` **before** occupants so the occupant pass honours visibility. Keyed off the single human (not the current player), so fog is stable through AI turns. `null` = render normally (no fog). `BuildProjection` returns `null` (full reveal) when fog is off, when there isn't exactly one human, or when the human is **eliminated**; `ComputeFogView` also returns `null` once `SessionState.IsGameOver` — so **victory or defeat reveals the whole map**.

**Rendering (`HexMapView`, all guarded so non-fog modes are unchanged).** Fills, outlines, and territory borders paint from `EffectiveOwner` — live owner when the tile is in current sight, else neutral `None` (stale terrain reads grey; fog tiles are hidden anyway). The occupant pass skips non-visible tiles (no owners/occupants leak). One baked `TriangleSoup` `_fogLayer` (topmost) covers the full water extent: opaque cool **mist** over never-seen cells, a translucent cool **memory wash** over stale cells; static terrain/water/decorations show (dimmed) under stale and are hidden under fog. Tier frontiers are **feathered** via per-vertex alpha, drawn as a fan from a full-tint centre vertex (`TriangleSoupBuilder.AddFan`) so the gradient stays spike-free. `ShowFog` diffs the visible set and only repaints (`RepaintFogVisuals`) when it changes.

**Undo/redo disabled.** Because Seen is sticky across undo, undoing a capture after it revealed tiles would scout for free, so the four undo/redo handlers no-op and the HUD buttons grey out when `Mode == FogOfWar`.

**Save / replay / thumbnail.** `Seen` round-trips as a coord list (v16, omitted when empty; reuses the water DTO shape). `ReplayRecorder.BeginReplay` calls `ClearSeen` alongside the water reset so a replay re-fogs and re-animates from the initial sight. `MapThumbnailView.RenderAsync` pushes `BuildProjection` so previews render fogged; the deepest `RequestRandom(seed, options, roster, mode)` takes an explicit mode — freeform passes `GameSettings.Mode`, the campaign confirm sheet passes `ModeForLevel(level)` — so each preview matches the board it launches.

## Viking Raiders game mode

A selectable mode (`GameMode.VikingRaiders`): neutral raiders arrive at the island's shores in a fixed escalating wave schedule; **no one can win until every raider — at sea, landed, or still scheduled — is gone**, after which ordinary Freeform rules apply. Normal multiplayer configuration otherwise (no roster lock).

**State.** `GameState.Vikings` (`VikingState`, always non-null, default-empty outside the mode): `AtSea` (lex-sorted `SeaViking(Coord, Level)` list — water coords have no `HexTile`, so raiders at sea live off-grid), `NextWaveIndex`, `LastCompletedRound`, `LastSpawnRound`. Excluded from undo snapshots (it mutates only during the viking pseudo-turn and the undo stack clears at end of turn; landed vikings are ordinary grid occupants and snapshot normally); the replay rewind resets it via `VikingState.Reset`.

**Schedule & rules (`VikingRaidersRules`, Model, integer-only).** Waves at rounds 3, 6, …, 18 (`FirstWaveRound`/`WaveIntervalRounds`/`TotalWaves`; `WaveDue` catches up a missed spawn next round); composition is a fixed table independent of map size (`WaveComposition`, totals 10 → 15): 5 Recruits every wave (`WaveRecruits`), Soldiers 5/6/7/8 then frozen (`WaveBaseSoldiers`), Captains 1/2 from wave 5 (`FirstCaptainWaveIndex`), never Commander, strongest listed first so they claim the best landing spots; spawns clamp to available coastal water. `CoastalWaterCoords` (water coords with a grid neighbour) hosts `ChooseSpawns` — a "landing party" placement, sequential per raider with a lexicographic preference: any viable landing at all for the raider's level first (a zero-target spot is certain death), then a landing zone that does NOT overlap the raiders already placed this wave (overlap is a last resort among landable spots), then the most viable landing neighbours, then shortest distance to the party, remaining ties drawn from the seeded rng; one raider per coord. `DisembarkTargets`: player-owned neighbours under the ordinary capture threshold (`DefenseRules.Defense < level`, radiation included), plus already-neutral neighbours with no Unit/Tower (trees/graves land like a reposition — no defense check against the vikings' own side). `ThreatRemains` = raiders at sea ∨ waves pending ∨ landed None-owned units; always false outside the mode.

**The viking pseudo-turn.** Runs once per round at the round boundary, between `AdvanceToNextActivePlayer` and a **deferred** `StartPlayerTurn` — all three Advance→Start seams (`EndTurnNow`, the AI driver's end-of-turn, replay's end-turn beat) check `GameOperations.VikingTurnPending`. `AiTurnDriver` drives the phase through the same paced preview/execute machinery (and the chunked Instant track) with `VikingAi.ChooseNext` as the chooser, in strict order: disembark earlier-round raiders (best landing by 1-ply clone+score; `VikingPerishAtSeaAction` when every landing is blocked) → landed moves via `ComputerAi.ChooseNextAction(state, PlayerId.None, …)` (capital gate bypassed for None; captures only — own-territory trees are never chopped, and the economy (2a–4a) and defensive-reposition (4b) phases are skipped, so a raider with no capture holds; `MovementRules` bans None combines; `UpkeepRules.TotalUpkeepFor` returns 0 for None so vikings never bankrupt into graves; `AiStateScorer` skips the own-tree penalty and the bankruptcy zeroing for None, so viking units read as real threats in every player's scoring) → spawn a due wave **last** (`VikingState.LastSpawnRound` keeps a fresh wave from acting on its spawn round — exactly one round of warning). `BeginVikingTurn` reseeds the turn RNG from `MixSeed(masterSeed, turn, −1)` (the vikings' own stream) and un-spends None units; `CompleteVikingTurn` stamps `LastCompletedRound` and, if no capital survives, declares `PlayerId.None` the winner ("The Vikings have conquered the island!"). A viking capture that eliminates a human raises the ordinary defeat overlay and pauses the phase; Continue re-arms it (`OnDefeatContinuePressed` schedules the driver while `VikingPhaseActive`). `GameOperations.HumanInputLocked` (= `InSilentAiBatch() || VikingPhaseActive`) gates every mutating human handler mid-phase; overlay-dismiss handlers stay live.

**Win gating.** While `ThreatRemains`: `EndOfTurnProcessing` returns no winner, `HandleCapture`'s domination check is suppressed (sea/scheduled raiders aren't tiles, so the explicit gate is needed on top of neutral tiles blocking sole ownership), and `OnEndTurnPressed` never offers the claim-victory prompt. A one-shot `[viking] threat cleared` Info line (category `Viking`) marks the transition back to ordinary rules.

**Persistence & replay.** Save v17 carries `VikingAtSea`/`VikingNextWave`/`VikingLastRound`/`VikingLastSpawnRound` (all omitted at defaults, so non-viking saves' wire format is unchanged). Replay records viking beats — `VikingMove`/`VikingDisembark`/`VikingPerish`/`VikingSpawn` (explicit placements; playback consumes no RNG) and `VikingTurnEnd` (drives `CompleteVikingTurn` + the deferred `StartPlayerTurn`); `ReplayApplyEndTurn` enters the phase via `BeginVikingTurn` when pending, and `BeginReplay` rewinds `VikingState` to defaults. `GameStateChecksum` appends a viking block only when non-default. Pinned by the VikingRaiders `ReplayFidelityTests` rows.

**Rendering & shell.** Viking units carry a "painted shield" glyph (`HexMapView.CreateVikingShieldVisual`, per `VIKING_UNIT_GLYPH.md`): a top-down cream shield with an ink rim and center boss, rank shown by a doubling ladder of painted ink segments — half-painted (Recruit), quartered (Soldier), eight segments (Captain) — in a fixed ink `#111111` / cream `#EFE6D4` palette that is never player- or actionability-colored. Identical on land and at sea; raiders at sea render in `_seaVikingsLayer` via `IHexMapView.ShowSeaVikings` (pushed each `RefreshViews`) directly on the water. A perish leaves a grave marker on the water (`VikingState.SeaGraves` — transient: not saved, not checksummed, not snapshotted; replay re-derives it from perish beats) drawn with the land bankruptcy choreography (shield shrinks out on the deaths layer, grave grows in staggered beneath) and the Rising Tides `TileSubmerged` "bloop"; the graves wash away when the next viking turn begins (or at phase end if the threat just cleared — a grave on water never becomes a tree). A wave's spawn plays the "ripple rise" entrance (`VIKING_SPAWN_ANIMATION.md`): each shield scales in from nothing with a Back-ease overshoot while fading in, two flat white rings ripple outward beneath it (`SeaSpawnRippleRings`, constant-stroke `_Draw` arcs; rings gated on `UserSettings.VfxEnabled`), staggered 0.15s per raider, suppressed under silent mode and for one pass after a full rebuild (`_animateSeaSpawns`); the arrival is sounded once per wave with the ElevenLabs-generated creaking-longship cue (`SoundEffect.VikingArrival` → `assets/audio/viking_arrival.wav`, manifest entry in `tools/generate_sounds_eleven.py`). The turn-order swatch bar gains one extra grey swatch (this mode only) for the neutral raiders: highlighted while `VikingRaidersRules.TurnDue` (the same pure predicate behind `GameOperations.VikingTurnPending`), dimmed like an eliminated player once the threat clears. At every human turn start the controller shows a transient wave banner (`VikingWaveBannerContent.For`, unit-tested copy: "Wave X/Y arriving in N turns" countdown / "Wave X/Y" on the spawn round / "Final wave" variants) via `IHudView.ShowTransientBanner` — a click-through top-center toast that fades in, holds, and fades out on its own; schedule-resistant because the countdown derives from `VikingRaidersRules.RoundsUntilWaveDue`, itself defined purely through `WaveDue`, and turn-1 sequencing after the one-time mode intro falls out of `Main` deferring game start until the intro is dismissed. First-encounter explainer via `GameModeIntro`; selectable from the **Game Mode** dropdown; `FOUREXHEX_MODE=VikingRaiders` forces it for headless runs.

## Randomized selection

Every game breaks selection ties with seed-deterministic random picks (integer-only RNG) at two selection points: the **in-tier capital choice** (`CapitalPlacer.Choose`) and its equal-old-size merge tiebreak (`CapitalReconciler.Reconcile`), and the **equal-exposure Rising Tides submerge tie-break** (`RisingTidesRules.ForecastSubmerge`). Priority order always holds — capitals respect the occupant tier, tides take the most-exposed tile; only the choice *among equals* randomizes. The lex-min null-rng path in `CapitalPlacer`/`CapitalReconciler` remains for editor paints and test fixtures; every in-game reconcile passes `randomize: true`.

- **Two RNG sources, by constraint.** Capital placement seeds a local `DeterministicRng` from the territory's own coords (`CapitalReconciler.SeedFromCoords`, FNV-1a + splitmix32 avalanche), so it consumes nothing from the live per-turn `_rng` stream and the AI's cloned 1-ply simulation reproduces the identical replacement capital (`AiSimulatorDriftTests` pins sim == real). The tide tie-break draws from `GameOperations._rng` at forecast time — the first per-turn RNG consumer, right after `ReseedRngForCurrentTurn`, so it lands at a fixed stream offset and reproduces on resume/replay; the simulator never touches tides. Still seed-deterministic since the board itself evolves from the master seed.
- **Replay compatibility is version-gated.** A recording's capitals/tides are re-derived during playback, so a replay is only faithful under the tie-break rules that recorded it — `ReplayVersion` (see *Save / load*) drops replays stamped by a different generation instead of replaying them divergently. The `(mode)` matrix in `ReplayFidelityTests` pins record → serialize → replay checksum equality; `TutorialReplayFidelityTests` drains every shipped `tutorials/*.json` demo to completion.

## Origin-capital merges

When a capture merges two (or more) same-owner territories, the capital of the territory the acting unit **originated from** wins — for a move, the source territory; for a buy, the purchasing capital's territory. Merged gold sums onto the survivor as always (`Treasury.ReconcileAfterCapture` credits whatever capital the reconciler chose); losers are demoted off the grid.

- **Threading.** The origin is the acting territory's capital, resolved before the mutation, and rides `HandleCapture(actionDesc, originCapital)` → `TerritoryFinder.Recompute(..., originCapital)` → `CapitalReconciler.Reconcile(..., originCapital)`. Callers: `GameController.ExecuteMove`/`ExecuteBuyAndPlace` (human), `GameOperations.ExecuteMoveCore`/`ExecuteAiBuyUnit` (AI + replay), and `AiSimulator.ApplyMove`/`ApplyBuy` (the 1-ply clone, so sim == live — pinned by `AiSimulatorDriftTests`).
- **Fallback.** When no origin capital is among the merged capitals (capital-less origin singleton, editor paints, any merge not caused by a unit action), the **largest old territory's** capital wins; equal-largest ties break seed-random (lex-min when the reconcile runs without an rng — editor/fixtures).
- **Instrumentation.** Every merge decision logs `[reconcile] merge … candidates … origin … winner … rule=origin|largest|tiebreak-…` (category `Capture`, Debug).

`DisplayScale` — autoload Node (`project.godot` `[autoload]` "DisplayScale", ordered after `LogBootstrap`). Keeps UI at a roughly constant physical size by reading screen DPI and driving root `Window.ContentScaleFactor`:

- Pure clamp math in Model — `DisplayScaleMath.FactorForDpi(logicalDpi, minFactor)` = `clamp(logicalDpi / 160, max(minFactor, 1.0), 3.0)` (160 = Android mdpi baseline; never below `MinFactor` = 1.0; capped at 3.0). The autoload is the thin adapter reading `DisplayServer.ScreenGetDpi` / `ScreenGetScale`.
- **Logical DPI, not raw.** macOS renders in logical points, so the adapter divides raw DPI by `ScreenGetScale`. Android's `ScreenGetScale` varies by orientation (Galaxy S9: 1.35 portrait / 1.8 landscape). See `RELEASE.md` §5.
- **Per-platform mobile formula** — iOS's `ScreenGetScale = 3` is a retina multiplier not a density bucket, so iOS keys off raw DPI, Android off logical:
  - **iOS:** `DisplayScaleMath.FactorForRawMobileDpi(rawDpi, MobileMinFactor)` = `clamp(rawDpi / MobileReferenceDpi, MobileMinFactor, 3.0)`, `MobileReferenceDpi = 180` (S9 FHD+ portrait: 401 / 2.22 ≈ 180). iPhone 13 mini raw 476 → 2.64.
  - **Android (and other mobile):** `FactorForDpi(logicalDpi, MobileMinFactor)`; dividing by the density-bucket `ScreenGetScale` is correct. S9 portrait (logical ≈355) → 2.22; landscape (≈1.67) lifts to the `MobileMinFactor = 2.22` floor.
  - **Desktop:** non-mobile `FactorForDpi(logicalDpi)` floors to 1.0; mobile floor doesn't apply.
- **Unified mobile floor.** `MobileMinFactor = 2.2222` — safety net for low-density Android phones; without it a 160-DPI phone computes 1.0 and renders unusably small buttons.
- **Override.** `DisplayScale.Apply()` honors `FOUREXHEX_UI_SCALE` to bypass DPI and force a factor on any platform (precedence over the mobile floor). See RELEASE.md §6 Option B.
- **Works with the existing HUD.** `ContentScaleFactor` also sets GUI logical layout size to `window / factor`, so `GetViewport().GetVisibleRect().Size` (read by `OrientationHud` / `HexMapView`) returns logical size and the anchor-based HUD reflows with no per-widget changes, even with stretch mode `disabled`. Set once at startup, re-applied on `SizeChanged`, with an equality guard against the resize feedback loop.
- **Narrow viewports.** Scaling up shrinks the logical canvas. Centered fixed-width HUD panels cap width to the viewport (`HudView.PositionTutorialOverlay` / `PositionBankruptToast`, shared `HudPanelSideMargin`). Win/defeat/claim overlays are container-based (eyebrow + DM Serif title + gold rule + an `HFlowContainer` button row that wraps), built by `HudView.BuildEndgameOverlay`; `HudView.PositionEndgameOverlays` clamps each width to `min(designW, viewport − 2·HudPanelSideMargin)` and re-runs on `OnViewportMetricsChanged`. Shared modals (`SettingsPanel`, `CreditsPanel`) keep single-column layout; `FitPanel` applies a uniform `Control.Scale` (clamped ≤ 1) to shrink to the safe viewport — same as `MainMenuScene.ScaleToFit`. (CreditsPanel keeps its own `ScrollContainer`; body label is `MouseFilter = Pass` so touch-drag reaches the scroll.)
- **Mobile keyboard avoidance.** `SaveNameModal`'s save-name `LineEdit` is the mobile text input that can sit under the on-screen keyboard. While focused, the modal polls per frame (`SetProcess` gated on `FocusEntered`/`FocusExited`) through the shared `KeyboardLiftController` (`scripts/KeyboardLiftController.cs`), which reads `DisplayServer.VirtualKeyboardGetHeight()` and computes the lift via `KeyboardAvoidance.LiftFor(fieldBottomY, viewportHeight, keyboardPhysicalHeight ÷ ContentScaleFactor, margin)` (ViewMath, unit-tested; the unlifted bottom adds back the applied lift so it never feeds back); the modal applies it by translating the center-anchored panel up via anchor offsets. Enter/Return releases focus rather than saving — an explicit Save-button tap commits. `FOUREXHEX_FAKE_KB=<physical px>` fakes a keyboard height on desktop. Instrumented under `Input:Debug`.

## Safe-area handling (autoload)

`SafeArea` — peer autoload to `DisplayScale` (`project.godot` `[autoload]`, ordered after `DisplayScale` so `ContentScaleFactor` is settled first). Keeps HUD chrome out of the iOS notch / Dynamic Island / home-indicator zones.

- Pure math in the Godot-free model assembly: `SafeAreaMath.InsetsFor(physicalWindow, physicalSafeRect, contentScaleFactor)` returns a `LogicalSafeInsets(Top, Bottom, Left, Right)` record by clamping the safe-rect/window gap to ≥ 0 and dividing by scale. Tested in `tests/SafeAreaMathTests.cs`; the autoload is the adapter reading `DisplayServer.GetDisplaySafeArea`.
- **Mobile-only gate.** On `!OS.HasFeature("mobile")` returns `LogicalSafeInsets.Zero`. Android cutouts share the iOS path.
- **Bar overlaps iOS chrome (map reclaims safe-inset space).** `HudBars.MakeBarPanel` builds a bar of exactly `height` logical px, anchored to the viewport edge, so iOS chrome carves into the bar's slate fill, not the map. `MakeBarFrame` is a symmetric 8-px chrome inset. Same "no safe-area fold" rule applies to `PositionTutorialOverlay`, `PositionBankruptToast`, and the seed-label drop position.
- **Notch-aware top-bar tweaks.** On `SafeArea.Current.Top > 0` (iOS portrait), the gameplay-HUD top bar drops the frame's 8-px bottom inset (`topFrame.OffsetBottom = 0f`) and bottom-aligns the gold chip (`_goldChip.SizeFlagsVertical = ShrinkEnd`). Same in `MapEditorHudView.BuildPortraitBars` for the seed pill + die. Non-notched: both `ShrinkCenter` with the symmetric inset.
- **Re-layout on inset change.** `OrientationHud` subscribes to `SafeArea.Changed`, runs `ApplyLayout` + `PublishInsets`; `hasTopNotch` re-evaluates each rebuild. Modals (`SettingsPanel`, `CreditsPanel`) subscribe to `SafeArea.Changed` and `GetViewport().SizeChanged`, re-running `FitPanel` (reading `SafeArea.Current`); both unsubscribe in `_ExitTree`.

## GameController ↔ GameOperations split

Mutation/orchestration core (what both live AI and replay need) lives in `src/FourExHex.Controller/GameOperations.cs`, separate from `GameController` so the `ReplayRecorder` extraction creates no cycle.

- **`GameOperations`** owns mutation + turn-lifecycle helpers:
  - Per-action execute — `ExecuteAiMove`, `ExecuteAiBuyUnit`, `ExecuteAiBuyCombine`, `ExecuteAiBuildTower`, `ApplyLongPressRally` (validation + view/capture envelope; the bare mutation is `AiActionCore` in Model, shared with `AiSimulator`)
  - Capture aftermath — `HandleCapture` (+ private `SnapshotCapitals` / `ColorsWithCapital` / `LogCaptureDiff`), `DispatchActionSound`, `DeclareWinner`
  - Turn transitions — `ReseedRngForCurrentTurn` (+ static `MixSeed`), `EndOfTurnProcessing` (+ private `LogGameEndDiagnostics`), `AdvanceToNextActivePlayer`, `StartPlayerTurn` (+ static `ResetMovementFor`, private `LogTurnStart`)
  - Game-end — `CheckGameEndConditions` (fires `GameEnded` via the `onGameEnded` ctor callback; controller owns the public event)
  - View sync — `RefreshViews` (also pushes the Automate button state via `IHudView.SetAutomateState`, reading the `isAutomating` / `isAutomateExhausted` ctor callbacks), `ShowHighlightAndRefresh`, `InvokeAfterRefresh`, private `HasAnyActionableForCurrentPlayer`
  - Shared instant loop — `RunInstantTick(active, step, onExhausted, reschedule)`, the chunked frame-yielded fast-forward behind both live-AI instant (`AiTurnDriver`) and instant replay (`ReplayRecorder`); owns `InstantBudgetMs`
  - Silent-mode — `IsSilent` (per-action cue gate) + `EmitSound` / `EmitDestruction` / `EmitTerrainCaptureFx` / `EmitMountainTowerFx` (silent-gated wrappers over `_map.PlaySound` / `PlayDestructionEffect` / `PlayTerrainCaptureEffect`, the only path controllers use); `RefreshSilentMode` (drives the view's `_silentMode` flag for view-internal tween/tide suppression), `InSilentAiBatch` (input gate)
  - Helpers — `WasFriendlyUnitAt`
  - Mutable shared state (public properties; written by the instant loop / replay reset paths) — `Rng` (read-only getter), `GameEndedFired`, `HumanTurnFiredForCurrentTurn`, `SuppressMapRebuild`

- **`GameController`** retains input + turn rotation:
  - All `IHexMapView` / `IHudView` event handlers (`OnTileClicked`, `OnEndTurnPressed`, Undo/Redo, etc.) and the `TrackHandler` wrapper (with its `_pendingHumanBeat` buffer)
  - Human execute helpers (`ExecuteMove`, `ExecuteBuyAndPlace`, `ExecuteBuildTower`, `RebindSelectionToContaining`) — no replay role
  - Turn rotation (`EndTurnNow`) and the entry points into the AI driver (`_aiDriver.RunUntilHumanOrDone` / `Schedule`) and the recorder
  - Public surface — `StartGame`, `Resume`, `AbandonGame`, `BeginReplay`, `ResumeAiTurnsAfterReplayPause`, the four `*ForTutorial` methods, `RecordTutorialOnlyBeat`, the readonly replay-state properties, `GameEnded` / `HumanTurnStarted` events

Construction: `GameController`'s ctor builds the `GameOperations` instance and passes callbacks for what it can't own (public events, `ClearUndoAndReplayBookkeeping`, `_replayMode`, `_replayInstantActive`), then calls via `_ops.X(...)`. `GameOperations` does not name `GameController`.

## GameController ↔ AiTurnDriver split

The live-AI turn driver lives in `src/FourExHex.Controller/AiTurnDriver.cs`. Same one-way layering as the recorder: every mutation goes through `GameOperations`, every replay beat through `ReplayRecorder`; the driver does not reference `GameController`.

### What lives on AiTurnDriver

- **Scratch state** (persists across paced step invocations, resets when control advances to a new player): `_aiVisited`, `_aiStepsThisPlayer`, `_pendingAiAction`, `_aiTrackInstant` (previous pacing track, for instant↔paced transition detection).
- **Pacing**: the step-beat delay constants live once in `StepPacing` (`AiPreviewDelayMs` 350 / `AiActionDelayMs` 300 / `AiBetweenPlayersDelayMs` 600 / `InstantTurnDelayMs` 200), shared with `ReplayRecorder` so replay matches AI cadence by construction. The driver keeps only the `MaxAiStepsPerPlayer` (64) safety cap.
- **The single game-over/human gate**: private `RunHalted` (`GameEndedFired || IsGameOver || !CurrentPlayer.IsAi`) consulted at every step-machine entry; the two paced beats that additionally clear the highlight on game-over keep that branch inline.
- **Paced step machine**: `Schedule(turnBoundary)` (the single re-dispatching decision point; re-reads `aiSilentMode()` per beat and delegates the shared transition/dispatch skeleton to `StepPacing.Redispatch`, supplying the driver-specific callbacks: track store, `RefreshSilentMode` sync, `Ai:Debug` transition logs), private `StepAiPreview` / `StepAiPreviewAfterChoose` / `StepAiExecute`, `ResolveAiActingTerritory`.
- **Instant driver**: private `InstantAiTick` (thin wrapper over `GameOperations.RunInstantTick`), `AiInstantStep`, `EndInstantAiBatch`.
- **Shared mutation cores** (used by both tracks so they can't drift): `ApplyAiActionCore` (records the replay beat live-only, dispatches to the matching `_ops.ExecuteAi*` / `ApplyLongPressRally` / `DeclareWinner`; `internal` — the controller's Automate loop executes its per-move core through it too, as does `ResolveAiActingTerritory` for the preview highlight), `EndCurrentAiPlayerTurnCore` (end-of-turn beat + `EndOfTurnProcessing` + advance/`StartPlayerTurn` + scratch reset), `[Conditional("DEBUG")]` `LogAction`.
- **Public entry points**: `RunUntilHumanOrDone()` (fresh run from a turn boundary; resets scratch, seeds the track), `Schedule(turnBoundary)` (defeat-dismiss resume), `ResumeAfterReplayPause()` (Tutorial-Preview narration dismissed; resumes mid-stream without resetting scratch).

### What stays on GameController

- The call sites: `Resume` / `EndTurnNow` → `RunUntilHumanOrDone`, `OnDefeatContinuePressed` → `Schedule(turnBoundary: false)`, and the public `ResumeAiTurnsAfterReplayPause` forwarder (kept so `PreviewPane` has a single controller entry point).
- `AbandonGame` → `_aiPacer.Cancel()` (the controller shares the pacer instance with the driver and recorder).

### Construction

Built last in `GameController`'s ctor (after `_ops` and `_recorder`, both of which it holds) with refs to `_state`, `_session`, `_map`, `_ops`, `_recorder`, `_aiPacer`, and the injected `aiChooser` / `aiSilentMode` / `isReplayPaused` functions (defaults `ComputerAi.ChooseNextAction` / `() => false` / `() => false`).

## GameController ↔ ReplayRecorder split

The replay subsystem lives in `src/FourExHex.Controller/ReplayRecorder.cs`. Same one-way layering: `ReplayRecorder → GameOperations` for every mutation; the recorder does not reference `GameController`. It owns recording, paced playback, and the instant-step function.

### What lives on ReplayRecorder

- **Recording state**: `_replayBeats`, `_initialSnapshot`, `_initialTurnNumber`, `_initialCurrentPlayerIndex`, `_replayDataIsCompleteFromStart`, `_replayMode`, `_replayIndex`, `_replayInstantActive`, `_undoBeatCounts`, `_redoBeatLists`, `_replayIsInstantMode`.
- **Recording methods**: `RecordBeat`, `RecordTutorialOnlyBeat`, `CaptureInitialSnapshot`.
- **Undo/redo coordinator**: session undo stack and parallel beat stacks move in lockstep (one beat batch per undo entry); the recorder owns both sides atomically — `CommitHumanHandler(pre, beatsBefore)` (push session entry + stamp pre-handler beat count + clear redo stash), `UndoOneStep` / `RedoOneStep` (pop/restore one beat batch + matching session pop, returning the restored `UndoEntry`), `ClearUndoAndBookkeeping` (drop both sides; beat log is committed history). Single-side steps are private. Every op ends with always-on `ValidateBeatStacksInSync` that throws (with all four counts) on divergence. Pinned by `UndoReplayBeatSyncTests` (depth equality via read-only `UndoBatchDepth` / `RedoBatchDepth`) and `ReplayPlaybackTests.Replay_AfterUndoRedoChurn_ProducesSameFinalState`. Under `Log.LogCategory.Undo`.
- **Playback methods**: `BeginReplay`, `EndReplay`, `StepReplayPreview`, `StepReplayExecute`, `ExecuteReplayBeat`, `ReplayApplyEndTurn`, `ReplayInstantStep` + the private `InstantReplayTick` wrapper over `GameOperations.RunInstantTick`, `ScheduleNextReplayBeat(turnBoundary)` (mirror of `AiTurnDriver.Schedule`: re-reads `_replayIsInstantMode` each beat and delegates the shared transition/dispatch skeleton to `StepPacing.Redispatch`, supplying the replay-specific callbacks: track store, `SetSilentMode` sync, `Turn:Info` transition logs; called by `StepReplayExecute` and `RunInstantTick`'s `reschedule` callback), private `ResolveReplayActingTerritory`.
- **Divergence detection**: a replay re-executes beats through *current* rules, so a rule change since recording can land on a different board or throw. `BeginReplay` captures the recorded end board's `GameStateChecksum` once before the rewind (`_expectedEndChecksum`, guarded `??=` so re-replay still compares against the original; skipped in `_previewMode`). The recorded board is the already-loaded top-level `GameState` (`loaded.State`, or finished live board). `EndReplay` recomputes the replayed checksum on a clean finish only (all beats consumed or a beat ended the game); on mismatch sets `LastDivergence` (an `Expected`/`Actual` `ReplayDivergence` record) and logs `Log.LogCategory.Replay` Warn + first-differing-line Debug; a faithful replay clears it to null. Both checksums from the same binary, so additive changes to `GameStateChecksum.Stringify` cancel. Developer-facing; pinned by `ReplayFidelityTests`.
- **Public read surface** (consumed by `Main.cs` / `RecordPane.cs` via thin `GameController` forwarders): `Beats`, `BeatsCount`, `InitialSnapshot`, `InitialTurnNumber`, `InitialCurrentPlayerIndex`, `IsCompleteFromStart`, `HasInitialSnapshot`, `IsReplaying`, `IsInstantModeActive`, `LastDivergence` (forwarded as `LastReplayDivergence`), plus `UndoBatchDepth` / `RedoBatchDepth` (forwarded as `UndoBeatBatchDepth` / `RedoBeatBatchDepth`).
- **`ReplayEnded` event** — raised at the end of `EndReplay` (log exhausted, game-over, or aborted), forwarded as `GameController.ReplayEnded`; the Instructions demo player loops on it.

### What stays on GameController

- All input event handlers and the `TrackHandler` wrapper. The `_pendingHumanBeat` buffer stays with the handlers; `TrackHandler` post-body calls `_recorder.CommitHumanHandler(pre, beatsBefore)` and `_recorder.RecordBeat(...)`.
- Undo/redo input handlers (`OnUndoLastPressed`, etc.) — gating, `ApplySnapshot`, view centering only; mechanics are one `_recorder.UndoOneStep` / `RedoOneStep` per step.
- `ClearUndoAndReplayBookkeeping()` — forwarder to `_recorder.ClearUndoAndBookkeeping()` (ctor callback target for `GameOperations`).
- Public events (`GameEnded`, `HumanTurnStarted`, and the `ReplayEnded` forward).
- Public API forwarders: `BeginReplay`, `RecordTutorialOnlyBeat`, `ReplayBeats`, `InitialReplaySnapshot`, `InitialReplayTurnNumber`, `InitialReplayCurrentPlayerIndex`, `ReplayDataIsCompleteFromStart`, `IsReplayMode`, `LastReplayDivergence`.

### Construction

`GameController`'s ctor creates `_ops` first, then `_recorder`, then `_aiDriver`. `GameOperations`' `isReplayMode` / `isReplayInstantActive` predicates are closures over `_recorder` reading `_recorder?.IsReplaying ?? false` / `_recorder?.IsInstantModeActive ?? false` (safe at any later time). The recorder is built with refs to `_state`, `_session`, `_map`, `_ops`, `_aiPacer`, the `replayIsInstantMode` predicate from `Main`, and `loadedReplay` (save-load bootstrap of `_initialSnapshot` + `_replayBeats`). The `InstantStep` enum (`Continued` / `TurnBoundary` / `Exhausted`) is a top-level Controller type (hosted in `ReplayRecorder.cs`) so both `AiInstantStep` and `ReplayInstantStep` return it.

## Key contracts

**`IHexMapView`** — what the controller asks the map to do:

```csharp
event Action<HexTile?>? TileClicked;          // in-grid only
event Action<HexTile?>? TileLongClicked;      // rally
event Action<HexCoord>? OffGridClicked;       // water / map-edge; raw coord
void ShowMoveTargets(IEnumerable<HexCoord> coords, UnitLevel level);
void ShowTowerTargets(IEnumerable<HexCoord> coords);
void ShowTowerCoverage(IEnumerable<HexCoord> coords);
void ShowMoveSource(HexCoord? coord);
void ShowHighlight(Territory? selected);
void CenterOnTerritory(Territory territory);
void RebuildAfterTerritoryChange();
void RefreshOccupantVisuals(PlayerId? currentPlayer, Treasury treasury,
                            IReadOnlySet<HexCoord> visitedCapitals);
     // capitals in visitedCapitals never get the actionable white/pulse
     // treatment — the highlight only points at unvisited territories
void ShowTideForecast(IEnumerable<TideStep> steps);  // Rising Tides telegraph
void ShowSeaVikings(IReadOnlyList<SeaViking> atSea);  // Viking Raiders: raiders waiting at sea
void ShowFog(FogView? fog);                           // Fog Of War projection (null = no fog)
void PlayDestructionEffect(HexCoord coord, HexOccupant destroyed);
void PlayTerrainCaptureEffect(HexCoord coord, TerrainFeature terrain);
     // capture / tower-on-mountain feedback: gold shower, mountain shake,
     // plain flash+ring — see the Sound & FX section
void FlashRejection(HexCoord target, RejectionShape shape, IEnumerable<HexCoord> blockingDefenders);

// Audio sink → AudioBus. SoundEffect enum (UnitPlaced, TowerPlaced,
// UnitCombined, UnitDestroyed, TowerDestroyed, TreeCleared, CapitalDestroyed,
// Bankruptcy, GameWon, Rally, PlayerDefeated, TileSubmerged, VikingArrival,
// GoldCaptured, MountainCaptured) picks the cue; coord reserved.
// Plays unconditionally — silent-mode gating is controller-side (GameOperations.EmitSound).
void PlaySound(SoundEffect kind, HexCoord? at = null);
```

`HexMapView._UnhandledInput` routes a left-click to one event: in-grid → `TileClicked(tile)`; off-grid (water, water rim, past map) → `OffGridClicked(coord)`; long-press → `TileLongClicked`. The controller never gets `TileClicked(null)` from real input, so it anchors water-click rejection to the raw coord.

`FlashRejection` is the single sink for rejected-click feedback: draws the forbidden-slash overlay (red silhouette + outlined-red circle + diagonal slash), animates a black arrow from each blocking defender, and plays `AudioBus.PlayRejectDefended()` / `PlayRejectGeneric()` per whether the defender set is non-empty. Overlays live in a persistent `_rejectionsLayer` that `RefreshOccupantVisuals` does not clear, so mid-pulse tweens survive refreshes; each ghost/arrow `QueueFree`s itself on its tween's `Finished`. Assets: `assets/audio/reject_generic.wav`, `reject_defended.wav`.

**Invalid-tap policy (flash then cancel).** A tap on an invalid target with a buy/build-tower/move action pending flashes rejection, then calls `CancelPendingAction()` (clears `Mode`/`MoveSource` + preview overlays, like Escape). In-grid taps fall through to the normal-selection block, re-processed as a fresh click; off-grid taps cancel then deselect. Applies in both `OnTileClickedBody` and `OnOffGridClickedBody`.

`ShowMoveTargets` takes the unit level so the preview renders at the correct size (recruit=1 ring, soldier=2, captain=3, commander=3+dot). Audio fires from the controller right after the mutation; `DispatchActionSound` picks one cue per resolution (combine > destruction-by-type > generic place).

**Unit visual language.** A placed unit reads as one of three states, set in `RefreshOccupantVisuals` and `ShowMoveSource`:

- **Actionable** — current player's unit with `!HasMovedThisTurn`: white rings + scale pulse (`PulseAmplitude`/`PulseRate`).
- **Selected** — the picked-up move-source, a subset of actionable: white rings, pulse suppressed, plus a tile-sized black hex backdrop under the rings in `_unitsLayer`. Built by `ApplySelectionAffordance`, torn down by `ClearSelectionAffordance`. Field `_selectionBackdrop` tracks the live node; the next `RefreshOccupantVisuals` re-runs `ApplySelectionAffordance` after the units layer rebuilds, so the backdrop survives refresh.
- **Idle** (everything else): black rings, no pulse, no backdrop.

`IsActionableUnit(HexCoord)` is the shared predicate. It reads `_currentPlayer` (cached by `RefreshOccupantVisuals`) so `ShowMoveSource` decides re-adding a just-deselected coord to `_pulsingUnits` without the controller passing the player again.

**`IHudView`** — what the controller asks the HUD to do:

```csharp
event Action? BuyRecruitClicked;       // U: cycle affordable levels
                                       // (Recruit→Soldier→Captain→Commander),
                                       // exit at top instead of wrap
event Action<UnitLevel>? BuyUnitClicked;// radio: enter that buy mode directly
                                       // (re-click cancels; other level switches)
event Action? BuildTowerClicked;
event Action? UndoLastClicked;
event Action? UndoTurnClicked;
event Action? RedoLastClicked;
event Action? RedoAllClicked;
event Action? EndTurnClicked;
event Action? NewGameClicked;          // Main: scene reload
event Action? MainMenuClicked;         // Main: scene change
event Action? NextTerritoryClicked;    // Tab; skips singletons + no-action
                                       // territories; no-op if none
event Action? PreviousTerritoryClicked;// Shift+Tab; same skip rules
event Action? NextUnitClicked;         // N: cycle selection by (Level, HexCoord)
                                       // — Recruits→Soldiers→Captains→Commanders,
                                       // lex within tier; wraps. Each pick turns
                                       // on SessionState.RepeatedMovement.
event Action? PreviousUnitClicked;     // Shift+N — backward
event Action? CancelActionPressed;     // Escape with Buy/Build/Move pending
event Action? AutomateClicked;         // G; Automate toggle: start AI-driven
                                       // play of the human's remaining moves,
                                       // or stop a running loop (see "Automate")
event Action? EscRequested;            // Options OR Escape with nothing pending;
                                       // Main → EnterPause → EscMenu
event Action? DefeatContinueClicked;   // dismiss defeat overlay; resume AI
event Action? ClaimVictoryWinNowClicked;   // declare win now
event Action? ClaimVictoryContinueClicked; // dismiss, proceed End Turn
event Action? ReplayClicked;           // victory overlay; Main → BeginReplay

void Refresh(GameState state, SessionState session, bool hasActionableRemaining);
void SetMapLabel(string text);         // one-time; "Level XX" / save name / "" (procedural)
void ShowTutorialMessage(string text); // bottom popup; click-through (Ignore)
void ShowTappableTutorialMessage(string text); // same panel + full-viewport tap
                                       // catcher firing TutorialMessageTapped;
                                       // TutorialNarrationDriver uses it for
                                       // display-text beats that block until ack
void HideTutorialMessage();            // dismiss (disarms tap catcher)
event Action? TutorialMessageTapped;   // raised by the tap catcher
void SetReplayAvailable(bool available); // toggle victory-overlay Replay button;
                                       // Main flips on GameEnded iff replay
                                       // history exists from game start

// CTA highlights (white bg + black border + black text). CtaButton enum
// (BuyRecruit, EndTurn, BuildTower, ClaimVictoryWinNow, ClaimVictoryContinue,
// DefeatContinue, NextTerritory). pulse: game-side steady (false) — EndTurn
// when the human is out of moves OR has visited every actionable territory
// and finished/left the last one (sticky once lit; see EndTurnCtaLatched),
// NextTerritory when an actionable territory exists and the selection is
// exhausted or a revisit — suppressed while the EndTurn CTA holds; Tutorial
// Preview beats pulse (Tween on Modulate.a, 1.0↔0.55). claim/defeat/build
// CTAs are Preview-only, default true. Both game-side CTAs are human-only.
void SetCta(CtaButton button, bool isCta, bool pulse = true);

// Force-disable the Undo/Redo row regardless of session.Undo. Tutorial
// Preview latches true — undo/redo isn't recorded as beats and would desync
// the script cursor.
void SetUndoRedoLocked(bool locked);

// Suppress the "X wins!" overlay even when session.Winner is set.
// GameController latches true in its ctor when previewMode/recordingMode is on
// — tutorial game-over flows through the bottom tutorial panel instead.
void SetVictoryOverlaySuppressed(bool suppressed);

// Automate toggle state, pushed from GameOperations.RefreshViews (the single
// refresh path). visible = false in tutorial Preview/Record — the button isn't
// drawn at all there. enabled = visible, human turn, not replay/game-over, not
// exhaustion-latched, and (actions remain OR running). running flips the
// button to pressed-in (Selected ring) with the pause glyph.
void SetAutomateState(bool enabled, bool running, bool visible);
```

Defeat overlay: `Refresh` reads `session.PendingDefeatScreen` and shows/hides a click-blocking panel naming the eliminated player. **Continue** → `DefeatContinueClicked` (resumes the paused AI loop); **Restart** → `NewGameClicked` (`Main.RestartCurrentGame`, unconfirmed same-seed reload); **Main Menu** → `MainMenuClicked`.

Claim-victory overlay: `Refresh` shows it iff `session.PendingClaimVictory.HasValue` and neither `Winner` nor `PendingDefeatScreen` is set (Winner > Defeat > ClaimVictory). **Win Now** → `ClaimVictoryWinNowClicked`; **Continue Playing** → `ClaimVictoryContinueClicked`. See "Claim victory prompt" under Win conditions.

Tutorial popup: bottom-anchored autowrap panel via `ShowTutorialMessage` / `ShowTappableTutorialMessage` / `HideTutorialMessage` (no `Refresh` state). Default is click-through (`MouseFilter=Ignore`); the tappable variant adds a full-viewport catcher (`MouseFilter=Stop` over all HUD content) so a click anywhere fires `TutorialMessageTapped` and is swallowed — the player can't act while a narration beat is gated. Four text sources during Tutorial Preview:

- Per-beat step instructions from `TutorialInstructionText.For(beat, state, session)`, pushed by `TutorialPreviewCues` at the tail of every `Apply()`. Non-tappable.
- Authored narration from `ReplayDisplayTextBeat`s, pushed by `TutorialNarrationDriver` via `ShowTappableTutorialMessage`, dismissed by a tap.
- Rejection toasts on non-script actions; `PreviewPane` subscribes to `TutorialPreview.PlayerActionRejected`.
- The terminal "Tutorial complete." toast from `PreviewPane.OnFinished`.

Cues hide the panel during AI turns mid-tutorial, but leave it once the script is exhausted (`NextPlayer0Beat == null`) so the completion toast survives.

**HUD icon layer.** Play HUD and map-editor HUD render action buttons through a shared `HudIconButton : Button` overriding `_Draw` to paint a programmatic glyph. Helpers live in static `HudIcons` — `DrawUnit` (1/2/3 rings + Commander dot), `DrawTower`, `DrawTree`, `DrawCapital`, `DrawHand` (all reused by `HexPaletteButton`), `DrawCurvedArrow` (single + nested-doubled for Undo Last/All / Redo Last/All), `DrawEndTurnTriangle`, `DrawGear`, `DrawAutomate` (the gear plus an enlarged dark hub holding a play triangle, or pause bars while `HudIconButton.AutomateRunning` is set). The two "next" buttons (`DrawNextUnit`, `DrawNextTerritory`) share an arrow-above-symbol composition via private `DrawNextArrow`: a horizontal math-vector arrow (line + filled arrowhead, `headLen = 0.468r`, `headHalf = 0.255r`) atop the per-button symbol (Recruit ring vs gold capital star, shifted down `0.20r`). Stroke-only glyphs (recruit ring, undo/redo arrows, next-arrow line, End Turn triangle) paint white on the dark bar, flipping black via `HudIconButton.CtaActive` while the End Turn CTA stylebox is on.

Play HUD's right-side cluster orders `NextUnit → NextTerritory → EndTurn (→ Options in landscape)`. `NextUnit` fires `NextUnitClicked` (same as N); its `Selected` mirrors `SessionState.RepeatedMovement` (gated on the button being enabled), `Disabled` mirrors `MovementRules.HasUnmovedUnitsOwnedBy` on the selected territory — greyed with tooltip "No unmoved units to cycle".

Static tooltips ("`<label> — <hotkey>`") owned by `HudIconButton.DefaultTooltip(HudIcon)` — the single icon→tooltip mapping for the play HUD, map editor, and `HudView.Refresh`'s dynamic fallback, with the wording itself resolved from the string store (`hud.tooltip.*`). The four Buy buttons and Build Tower override the tooltip live in `Refresh`: "Buy `<level>` (Ng) — U" / "Build Tower (15g) — T" when enabled, else the disabled reason ("No territory selected", "Selected territory has no capital", "Selected territory can't afford a captain (30g)"). Buy and Build stay visible with a disabled-with-reason tooltip so layout doesn't shift. The Turn/Gold labels and player-swatch bar have fixed `CustomMinimumSize.X` (swatch bar reserves every slot at enlarged width so the highlight moves without changing width) so later buttons never reflow.

The Buy row is four always-visible radio buttons (Recruit/Soldier/Captain/Commander) in a nested `HBoxContainer`. Each `HudIconButton` carries a fixed `BuyLevel`; `Selected` mirrors `SessionState.BuyModeLevel` so exactly one highlights. Clicking fires `IHudView.BuyUnitClicked(level)`; re-clicking the active level toggles off, a different level switches. The U hotkey fires `BuyRecruitClicked`, resolved by `GameController.OnBuyPressed` as a cycle through affordable levels, *exiting at the top* (most-expensive affordable → `ActionMode.None`, not wrapping). Build Tower is a single button; re-clicking it in BuildingTower toggles off.

In a buy/move mode the active button's tooltip is cleared and the bottom tutorial panel shows "Click to place a `<level>`" / "Click to move the `<level>`". `HudView` tracks `_externalMessageActive` (set by `ShowTutorialMessage`/`ShowTappableTutorialMessage`, cleared by `HideTutorialMessage`); the action-hint pass in `Refresh` writes only when that flag is false, so tutorial step text and the "Opponents are taking their turns…" announcement win over the generic hint.

**`IAiPacer`** — schedules deferred continuations for the AI and replay step machines. `GodotAiPacer` schedules via injected `ITimerFactory` (production `SceneTreeTimerFactory` wrapping `SceneTree.CreateTimer`; tests `ManualTimerFactory` storing callbacks to fire on demand). `SynchronousAiPacer` drains via a FIFO trampoline (outermost `Schedule` runs the drain loop; nested calls enqueue and return) — every queued callback fires before the outermost `Schedule` returns, but the flattened stack avoids overflow on long `StepAiPreview` ↔ `StepAiExecute` chains. `Cancel` drops pending callbacks but does **NOT** poison future `Schedule` — the same instance must survive Cancel-then-reuse because `BeginReplay` cancels straggling AI steps before scheduling. `GodotAiPacer` uses a generation counter (each `Cancel` bumps it; each `Schedule` captures it; the fired callback checks the captured gen still matches). `Main` also calls Cancel via `GameController.AbandonGame()` before swapping to the menu so an in-flight `StepAiExecute` can't fire against disposed nodes.

`GodotAiPacer` also takes an optional `Func<int>` `delayMultiplierPercent` (`Main` wires `IsReplayMode → ReplaySpeed; IsAutomating → AutomateSpeed (Instant → 0); else AiSpeed`, each through `UserSettings.SpeedMultiplierPercent`), read on every `Schedule` so a mid-game speed change takes effect next beat — Slow doubles (200), Fast halves (50), Normal passes through (100). **Instant is not a multiplier**: it routes to the chunked frame-yielded driver (`InstantAiTick` / `InstantReplayTick`) scheduling via `ScheduleUnscaled` — exact delay, bypasses the multiplier. Both methods share `Cancel`'s generation guard via one private `ScheduleTimer`; nothing runs inline (the chunked driver owns stack depth by returning between ticks). `SynchronousAiPacer` drains both inline. `AbandonGame` / `BeginReplay` call `Cancel` so an in-flight tick can't fire against disposed nodes.

```csharp
void Schedule(Action callback, int delayMs);          // multiplier-scaled
void ScheduleUnscaled(Action callback, int delayMs);  // exact, frame-yielded
void Cancel();
```

```csharp
// Split for testability — production = SceneTreeTimerFactory, tests = ManualTimerFactory.
public interface ITimerFactory { void After(int delayMs, Action callback); }
```

## Invariants (enforced by design)

- **Views never mutate the model.** View-looking methods (`ShowHighlight`, `RebuildAfterTerritoryChange`) touch only view state.
- **Controller never touches Godot Nodes directly.** It talks to views via the interfaces and to the event loop via `IAiPacer`, making `GameController` unit-testable with mocks (`tests/GameControllerTests.*.cs` partials; `TestGame` fixture in `tests/GameControllerTests.cs`).
- **Every state change funnels through `RefreshViews()`** at handler end. One path, no drift.
- **Snapshots capture `GameState` plus the player-intent slice of `SessionState`** (`SelectedTerritory`, `Mode`, `MoveSource`, `RepeatedMovement`, `VisitedTerritoryCapitals`, `VisitedThisTurnCapitals`, `SelectionWasRevisit`, `EndTurnCtaLatched`) via `UndoEntry` = `(GameStateSnapshot, SessionStateSnapshot)`. `Winner`, `PendingDefeatScreen`, and the `Undo` stack stay out. Top-level human handlers wrap in `TrackHandler`: capture pre-state, run body, push one `UndoEntry` iff state changed (visited sets compared by sorted-sequence equality in `SessionStateSnapshot.Equals`). Exceptions propagate without pushing.
- **Visited-territory cycling**: `SessionState.VisitedTerritoryCapitals` records the capital of every territory the human selects this turn. `StepTerritorySelection` re-sorts by descending size each press; pass 1 stops only on actionable *unvisited* territories, pass 2 resets the set for a fresh round. Cleared per-turn at the top of `StartPlayerTurn` (before that turn's auto-selection marks its pick); round-trips through `SessionStateSnapshot`. AI never touches it (runs via `GameOperations.ExecuteAi*`, not `SetSelection`).
- **Turn-scoped visited state (the CTA driver)**: `MarkSelectedVisited` (the `SetSelection` funnel — click, Tab, rally, auto-select, automate) also adds the capital to `SessionState.VisitedThisTurnCapitals` and maintains `SelectionWasRevisit` (true iff the selection *changed* onto an already-visited territory; same-territory re-clicks preserve it, null/capital-less selections clear it). Unlike the cycle set, this one never resets mid-turn — only `StartPlayerTurn` and undo restore shrink it. It suppresses visited capitals' pending-action pulse (except the currently-worked first-visit selection), lights Next-Territory on revisits, and — once every actionable territory is visited and the selected one is exhausted/deselected (or automation exhausted) — lights the End Turn CTA, which latches via `EndTurnCtaLatched` until the turn ends or undo unwinds past the lighting step. A capital-less singleton holding an unmoved unit can't be visited, so it conservatively holds the End Turn CTA off until the unit moves.
- **Repeated-movement** is a sticky bit on `SessionState` driving N-hotkey auto-advance. Both unit steppers set it on picking a unit — `StepUnitSelection` (short press / N, one unit at a time) and `StepUnitTierSelection` (next-unit button long-press, tier jump) — which share the `EnterMovingUnitOn` tail (mode, source, flag, move-target ring). While on, `ExecuteMove`'s tail calls `AutoAdvanceAfterMove(level, source, destination)`: weakest-first sort (`MovementRules.MovableUnitsWeakestFirst`, level ascending then coord-lex — the same order as the N-cycle and the AI's capture phases) of remaining movables in the (capture-rebound) selected territory, destination excluded, picking the first strictly after the moved unit's (level, source) key. Clears on Esc/cancel, entry into any non-None `ActionMode`, click selection change to a different territory, long-press rally, End Turn, game-over (`GameOperations.DeclareWinner`), or auto-advance with no movables left. `ClearPendingAction` does NOT clear it — `ExecuteMove`'s `FinishPendingAction` must run with the flag alive for the auto-advance hook. Round-trips through `SessionStateSnapshot`; capture-rebind preserves it.
- **`HexTile` is a pure model — no view coupling.** `HexTile.Owner` is plain state. The view owns the tile→fill map (`HexMapView._tileVisuals`) and resyncs fills from `_state` in `RebuildAfterTerritoryChange()`, the single coalesced repaint path. Model captures mutate `tile.Owner` with no view effect; the screen catches up only on `RebuildAfterTerritoryChange`.
- **Undo is turn-scoped.** `OnEndTurnPressed` clears the stack — ending a turn commits everything.
- **AI actions are not undoable**; AI execute methods validate preconditions before mutating — an illegal action throws and halts rather than corrupting state.
- **Replay log is honest.** Recording appends a `ReplayBeat` at execute time; undo/redo handlers pop matching beats (or push back on redo) so an undone move never appears in the saved replay. Grows monotonically across `EndTurn`.
- **Players with no capital-bearing territory are skipped.** `AdvanceToNextActivePlayer` calls `TurnState.EndTurn` until it lands on a player whose territory list contains a capital.

## Turn structure

A turn is sandwiched between two phases.

### Start-of-turn — `StartPlayerTurn()`

Fixed order for the current player:

1. **Reseed RNG** — `ReseedRngForCurrentTurn` derives `_rng` from `(masterSeed, turnNumber, currentPlayerIndex)`; turn RNG is reproducible from the seed. Also clears `VisitedTerritoryCapitals` (per-turn Tab tour reset), `VisitedThisTurnCapitals`, `SelectionWasRevisit`, and `EndTurnCtaLatched` before any auto-selection in step 6 marks the picked territory.
2. **Tree growth** — `TreeRules.RunStartOfTurnGrowth` (skipped while `TurnNumber == 1`). Graves on the player's tiles become trees; empty same-color cells with ≥2 neighboring trees become trees. **Neutral ground** (`PlayerId.None`) owns ground but no capital, so it takes a **phantom turn** (`RunPhantomTurnFor`: tree growth + no-op upkeep + log) once per round. `RunNeutralPhantomTurnIfRoundStart` anchors it to slot 0's visit, gated to `TurnNumber > 1 && CurrentPlayerIndex == 0` so it doesn't grow N× faster. Stateless — `TurnState` reconstructs the anchor across save/load and undo. Logged once per round under `Log.LogCategory.Turn` as "Neutral".
3. **Reset movement** — `HasMovedThisTurn` cleared on the player's units.
4. **Collect income** — `Treasury.CollectIncomeFor` (skipped while `TurnNumber == 1`; `SeedStartingGold` is the round-1 bankroll). Tree and grave tiles don't pay; everything else pays 1 gold.
5. **Apply upkeep** — `UpkeepRules.ApplyUpkeepFor`. Per-unit costs from the flat `UpkeepRules.UpkeepFor` table (Recruit 2, Soldier 6, Captain 18, Commander 54). A territory that can't pay total upkeep goes bankrupt: every unit becomes a `Grave`, remaining gold stays. `PlaySound(Bankruptcy)` fires once per player if any of its territories went bankrupt.
6. **Human hand-off** (`RaiseHumanTurnStarted`) if the now-current player is human and the game isn't over: **auto-select their first territory** then fire `HumanTurnStarted`. Auto-select (`AutoSelectFirstTerritoryForHuman`) clears any stale selection, then branches: in Rising Tides with a pending forecast, `TryFocusPendingTide` selects the territory containing the doomed tile and centers the camera on it (see *Rising Tides — Turn-start focus*); otherwise it reuses the Next-Territory picker — `StepTerritorySelection(forward:true)` lands on the largest actionable territory (capital-coord tie-break), a no-op leaving the selection null when nothing is actionable. Gated off for AI, replay, game-over, and when the injected `autoSelectFirstTerritory` flag is false (tutorial record/preview and mechanics tests). Autosave also wires to `HumanTurnStarted`.

Income → upkeep ordering lets the turn's income subsidize upkeep before bankruptcy is checked.

### Bankruptcy warning surfaces

Forecast pipeline surfacing upkeep wipeout ahead of time:

- **Pure rule (`UpkeepRules.Classify`)** — returns one of three `EconomyOutlook` values:
  - `BankruptNextTurn` — `gold + income < upkeep`.
  - `NegativeDelta` — `income < upkeep` but reserves still cover next turn.
  - `Healthy` — otherwise; also when no capital or no upkeep.
  Mirrors the real sequence (income then `ApplyUpkeep`, bankrupt iff `available < owed`); ignores tree growth and intervening captures.
- **HUD label (`HudView.Refresh`)** — colors `_goldLabel` red on `BankruptNextTurn`, yellow on `NegativeDelta`, clears otherwise. Only when the selected territory is human-owned.
- **Tap-summoned alert notice (`HudView._bankruptToast`)** — a pill below the HUD bar, built once in `BuildBankruptToast`, hosting `BankruptNextTurn` (red) and `NegativeDelta` (yellow) variants. Driven by `_summonedAlertCoord: HexCoord?`: visible iff set. `OnTileClicked` summons via `IHudView.SummonCapitalAlertNotice(coord, outlook)` when the tap hits the current human's own capital and `UpkeepRules.Classify(...)` is non-`Healthy`; re-tap toggles off. Every other top-level human handler calls `DismissCapitalAlertNotice()` at entry. `Refresh` stale-guards (dismisses if the coord no longer resolves to a human capital with the originally-summoned outlook) but never drives visibility. Red title "Bankrupt next turn"; yellow title "Losing gold", `BoardPalette.WarnYellow` border; shared `TriangleWarningBadge` glyph via `SetVariant`. State on `IHudView` (`SummonedCapitalAlertCoord` / `SummonCapitalAlertNotice` / `DismissCapitalAlertNotice`) — view-only, never in `GameState`/`SessionState`, so summon/dismiss never push undo. Logs `Log.LogCategory.Hud` (`[AlertNotice] summon/dismiss`).
- **Map badge (`HexMapView.RedrawWarningBadges`)** — a top-most `WarningBadgesLayer` stamps triangles on the capital of every affected current-player territory: red for `BankruptNextTurn`, yellow for `NegativeDelta`. Runs every `RefreshOccupantVisuals`; clears and returns if `state.Turns.CurrentPlayer.IsAi`, else iterates `state.Territories`. Selection-independent.
- **Instrumentation** — the HUD warning path emits `Log.Debug(Log.LogCategory.Turn, "[economy] …")` with gold/income/upkeep, for `FOUREXHEX_LOG="Turn:Debug"`.

### End-of-turn — `EndOfTurnProcessing()`

Just the **end-of-turn win check**: `WinConditionRules.WinnerAtEndOfTurn` returns the current player iff they're the sole owner of any capital-bearing territory. Orphan singletons of other colors don't keep the game alive.

### Win conditions

Two independent checks from different places:

- **Mid-turn (domination)** — `WinConditionRules.WinnerByDomination` fires in `HandleCapture` after every capture. Requires one color own *every* tile. Ends the game immediately, clears undo.
- **End-of-turn (sole capital-bearer)** — `WinConditionRules.WinnerAtEndOfTurn` fires in `EndOfTurnProcessing`. Looser, typical path: current player wins if no other player has a capital-bearing territory.

Viking Raiders suppresses **both** checks (and the claim-victory prompt) while `VikingRaidersRules.ThreatRemains` — see its section. `DeclareWinner` is the centralized setter for `SessionState.Winner`; fires `PlaySound(GameWon)` iff the winner is human.

**Game-over presentation** (`EndgameOverlayContent`, unit-tested, consumed by `HudView.Refresh`): DEFEAT framing (no Replay offer) applies to exactly two endings — a viking total wipeout (winner `PlayerId.None`, matches no roster player, no sound, "The Vikings have conquered the island!"), and an AI winning in the same beat a human's elimination ended the game, voiced like the mid-game elimination overlay ("&lt;Loser&gt; defeated" in the loser's color; `EndgameOverlayContent.DefeatedHumanFor` reads the loser from `SessionState.PendingDefeatScreen`, which survives the winner declaration because the HUD only suppresses the mid-game defeat overlay, never clears the field). Every other winner — a human, or an AI that outlasted an AI-vs-AI endgame after the eliminated humans dismissed their own defeat screens — gets the ordinary VICTORY announcement ("&lt;Winner&gt; wins!" in the winner's color, Replay offered). The overlay choice logs under `Render` as `HudView: game-over overlay …`.

### Claim victory prompt

Three tiers from `WinConditionRules.ClaimVictoryThresholdsPercent = {50, 75, 90}`. When a **human** presses End Turn, `OnEndTurnPressed` consults `WinConditionRules.NextClaimVictoryThreshold(color, grid, highestSeen)`, returning the highest tier met strictly greater than the highest already dismissed (or null). Water excluded (not in `state.Grid.Tiles`). Fires in both modes: in Rising Tides the `state.Grid.Tiles` denominator is current non-sunk tiles, so a player claims once their share of the *remaining* board crosses a tier.

If a tier returns, `OnEndTurnPressed` sets `SessionState.PendingClaimVictory = (color, threshold)` and refreshes; the HUD shows a centered "Claim Victory?" overlay with **Win Now** and **Continue Playing**. Wording is identical at every tier; one End Turn crossing multiple tiers skips to the topmost unseen. Pending End Turn is held until the user picks:

- **Win Now** (`OnClaimVictoryWinNowPressed`) records `ClaimVictoryPromptedHighestThreshold[color] = threshold`, calls `DeclareWinner`, clears undo, fires `GameEnded`.
- **Continue Playing** (`OnClaimVictoryContinuePressed`) records the same dismissal and runs `EndTurnNow()`. Max-update: a higher tier dismissed later overwrites a lower one — each tier fires at most once.

Dismissal records only on user action (not on show), so a save+reload with the overlay up re-presents the prompt. The dictionary persists via `SaveSerializer`. AI never triggers any tier; Tutorial Preview and Record suppress it entirely.

### Player elimination

`HandleCapture` diffs colors-with-capitals before vs after reconcile. A color with ≥1 capital before and none after was eliminated: `PlaySound(PlayerDefeated)` fires; if human, `SessionState.PendingDefeatScreen` is set so the HUD shows a defeat overlay. The AI loop pauses at the next `StepAiExecute` while the overlay is up; `OnDefeatContinuePressed` clears the flag and re-arms the pacer.

### Rotation

`AdvanceToNextActivePlayer()` calls `TurnState.EndTurn()` (increments `TurnNumber` on wrap) then runs `SkipEliminatedCurrentPlayers()`: loop while `WinConditionRules.IsEliminated(currentPlayer.Id, grid)`. The eliminated player takes no input or AI action but isn't silently skipped: each iteration runs a "phantom turn" ticking tile-bound rules — `TreeRules.RunStartOfTurnGrowth` then `UpkeepRules.ApplyUpkeepFor` (orphan units bankrupt into graves). Income, view refresh, AI dispatch, and turn logging are skipped. Without this, an eliminated player's lone unit on a singleton would linger forever. In Viking Raiders, the round boundary additionally inserts the viking pseudo-turn between the advance and the new player's `StartPlayerTurn` (see that section); `SkipEliminatedCurrentPlayers` also runs standalone when the raiders eliminate the just-advanced player mid-phase.

## Difficulty (a per-player economic handicap)

Economic handicap on whoever owns it, per slot, selectable per Human row; defaults `Soldier`. Levels by unit rank: `Recruit` (easiest) … `Commander` (hardest); higher = your own units and towers cost more to buy. Computer slots always `Soldier` — the UI locks AI rows to baseline (model still supports per-slot AI levels for `FOUREXHEX_DIFFICULTY`). Purchase cost is the **only** lever: income and upkeep are never scaled (upkeep is the flat per-level table in `UpkeepRules.UpkeepFor`).

Tuning lives in `DifficultyRules` (Model) as integer tables:

| your difficulty | unit cost (`UnitBaseCost` × tier 1–4) | tower |
|---|---|---|
| Recruit   | 8 / 16 / 24 / 32  | 12 |
| Soldier   | 10 / 20 / 30 / 40 | 15 |
| Captain   | 13 / 26 / 39 / 52 | 18 |
| Commander | 15 / 30 / 45 / 60 | 20 |

- **Plumbing.** `Player.Difficulty` (default `Soldier`), populated by `Player.BuildRoster` from `GameSettings.Difficulties`. Each player row gets a level dropdown; a Computer row pins to Soldier disabled, Human→Computer resets others to Soldier (`MainMenuScene.ApplyDifficultyLock`). `OnStartPressed` writes each row into `GameSettings.Difficulties[i]`. Dropdowns live on the player-setup page: landscape = one row each (swatch | name | role | difficulty); portrait = two-line block. A resize flipping `ScreenLayout.Resolve` rebuilds in place, round-tripping selections through the `GameSettings` arrays.
- **Lockstep invariant.** `PurchaseRules` takes a `Difficulty` parameter with **no default**, surfacing every consumer. Real buys (the current player's `Difficulty`), the AI buy gates (`AiCommon`), `AiActionCore`'s gold deduction, and the HUD buy-button prices all derive from the same tables via `GameState.DifficultyOf`.
- **Persistence.** Saved per player in save v7 (`PlayerDto.Difficulty`); missing defaults `Soldier`. Load mirrors it into `GameSettings.Difficulties` before `BuildRoster`.
- **Diagnostics.** `FOUREXHEX_DIFFICULTY="recruit,…,commander"` sets per-slot levels in the 6AI harness. `GameController` ctor logs a one-shot `difficulties: Red=…` line (`Turn:Info`) when any slot is non-Soldier.

## New Game setup & map thumbnail

**Play Game** opens a **source chooser** (reused `EscMenu` modal, `_sourceChooser`): **Configure Game** (fresh procedural), **Load Starting Map** (saved map, baked roster), **Quick Play** (`OnQuickPlay` skips both setup pages: Red human + 5 Computer / all Soldier, default densities, clears `CampaignLevel`, fresh `MasterSeed`, `LaunchGameScene`). Map Editor opens the same idiom (**New Map** / **Load Map**), sharing chooser and player-setup screen.

**Configure Game** runs **two paged screens** in `MainMenuScene` toggled by `_playConfigPage` (`PlayerSetup` / `MapSetup`); both built up front, visibility flipped (selections survive paging), `Enter`/`Esc` + Back/forward per page. Player-setup holds six role + difficulty rows; map-setup is **procedural-only** — a **re-roll die** button (`HudIconButton(HudIcon.Die)`) + live thumbnail. The map seed is not user-editable: `_previewSeed` (an `int` field seeded once in `_Ready`, persisting across orientation-flip rebuilds) drives the thumbnail and is handed to `GameSettings.MasterSeed` on Start; the die re-rolls it.

- **Per-slot role incl. `None`, min 2.** Each role dropdown offers Human / Computer / **None**; `None` excludes the slot. Forward (`OnPlayerPageForward`) gated to **≥2 active** (`Enter` guards too). Selections persist into `GameSettings.PlayerKinds` / `Difficulties` via `PersistRosterSelections` at every forward step, so the thumbnail (`Player.BuildRoster()`) reflects active colors.

- **Shared player-setup screen.** Same page (`_playConfigPurpose` = `NewGame` | `EditorNewMap`) feeds the procedural map page ("Next") or a new editor session ("Create Map" → `LaunchEditorNewMap`, handing kinds/difficulties via `MapEditorRequest`). Only the forward action differs.

- **Load Starting Map / Load Map.** Both use the same picker (`SlotPickerDialog`, `previewMaps: true`) and launch straight into play (`LoadRequest.Pending` → game scene, baked roster) or editor (`MapEditorRequest.Pending = LoadMap`). No confirm.

- **Fill-to-cap surface (both orientations).** Portrait/landscape panels are a centered `LandscapeMenuChrome` surface filling the safe area up to a cap, sized by the single `ApplyPlayConfigLayout` path (shared by `FitPanels` and the `SafeArea.Changed` hook). Landscape caps `920×520`; portrait the transpose `520×920`. Container-based (`VBox`/`HBox`/`ScrollContainer`); portrait players use a two-line block, lists carry no `ScrollContainer` (six 40-px rows fit).

- **Live thumbnail = offscreen `HexMapView` snapshot.** `scripts/MapThumbnailView.cs` renders the real `HexMapView` into a hidden `SubViewport`, snapshots to a static `ImageTexture` in a `TextureRect` — pixel-identical to Start Game, rendering only on change. `RequestRandom(seed)` builds via shared `ProceduralGame.Build`; `RequestMap(name)` loads an editor map via `SaveStore.LoadMap(name).State`; `RequestSlot(name)` an in-progress save via `SaveStore.LoadSlot(name).State`. Requests coalesced by a token so rapid requests snapshot only the latest. Refreshed on re-roll / map selection; under `Display:Debug`.

- **Stable, sharp, oriented framing.** `SubViewport` sized to the *nominal grid* aspect (seed-independent, via `ThumbnailLayout.FitInside`); `HexMapView.FrameWholeGrid` frames the whole grid rectangle so re-rolling keeps fixed scale/position. A portrait menu gives a tall aspect, so `HexMapView` rotates the board −90°. Renders at displayed size × window `ContentScaleFactor` × a 3× **supersample**, clamped ~1600 px, downsampled through a mip-mapped `TextureRect` (SSAA standing in for the 2D MSAA the GLES3 compatibility renderer lacks). Top hex-tessellation row cropped for a straight edge.

- **`MapInfoSheet` — the shared "play this board?" sheet.** `scripts/MapInfoSheet.cs` is the reusable confirm dialog: serif title, status line, a **"who you're playing as"** block (**one / many / none** human identities — tinted sentence, swatch+name chips, or all-Computer note), a large `MapThumbnailView`, Cancel/confirm. Caller supplies title, status, human list, and a thumbnail-request delegate (no seed-vs-saved-map knowledge). `CampaignConfirmSheet` is a thin **factory** (`CampaignConfirmSheet.Create(level)`) building a `MapInfoSheet` with the level's single human and `RequestRandom(seed, opts)` preview (campaign maps procedural, level N = seed N). Reuses the `LandscapeMenuChrome` fill-to-cap surface. `Escape` cancels.

- **Load Game / Load Map preview.** `SlotPickerDialog` (shared by main-menu / in-game Load Game, Load Starting Map / Load Map, editor Load Map, tutorial-builder Load Tutorial) has two bodies, chosen per-open by `ShowSlots`'s optional `thumbnailStore`. **Text-only** (no store): a small fixed centered modal of click-to-load buttons. **Preview** (hosts pass `_saveStore`): a `LandscapeMenuChrome` fill-to-cap surface — a selectable slot list (toggle buttons in a `ButtonGroup`) beside one large `MapThumbnailView`, plus Cancel / Load. The `previewMaps` flag picks the directory: `RequestMap` (`user://maps/`) else `RequestSlot` (`user://saves/`). Distinct portrait (list-above-preview) and landscape (list-rail | preview) layouts, rebuilt on orientation flip, capped `520×920` / `920×520`. Selecting re-points the single preview; render deferred one frame. A missing/corrupt save degrades to a blank preview (row stays loadable) via `MapThumbnailView`'s log-and-bail.

## Player roster (2–6 players, `PlayerKind.None`)

`PlayerKind` is `{ Human, Computer, None }`. The roster is a **variable-length list of *active* players**; almost everything keys off it, not a fixed 6:

- **`Player.BuildRoster()`** iterates six `GameSettings.PlayerConfig` slots but **skips `None`**, returning a compact 2–6 list. Each survivor keeps its **original slot index** via `PlayerId.FromIndex(slot)`, so color = slot (`PlayerPalette.ColorFor` indexes `PlayerConfig[id.Index]`) regardless of compaction. A `None` player never enters a live `TurnState`. Turn rotation, `CapitalPlacer`, `WinConditionRules`, and `MapGenerator` owner assignment (draws `rng.Next(players.Count)`) consume the roster as-is.
- **Slot ≠ list position.** Roster compacts (e.g. slots `0,2,5`), so never index it by *slot*. Tile-owner difficulty resolves via **`GameState.DifficultyOf(PlayerId)`**, matching `id` across the roster (Soldier for neutral / not-found). All AI scoring/simulation (`AiStateScorer`, `AiSimulator`, `AiCommon`) and `HudView` go through it.
- **`Player.BuildAllHumanRoster()`** (all six Human) — tutorial builder's preview/record harness.
- **`Player.BuildCampaignRoster(level)`** builds the level's deterministic 2–6 player campaign roster *from the level alone*, so a campaign launch never touches the freeform `GameSettings.PlayerKinds`.
- **Validation.** `MapRosterRules.ValidateForSave(territories, kinds)` (pure, Model) is the editor's save gate: a color owning land must be active, every active color must own land, every active color owning land must hold ≥1 capital, ≥2 must be active. Capital check is mutually exclusive with owns-no-land (a landless slot flagged once). See *Map editor*.

Save-format consequences (decoupling list position from color slot, baking map kinds, `None` on load) are in *Save / load*.

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
  pre = CaptureCurrentSnapshot()       // game + session, BEFORE body
  └─ OnTileClickedBody(tile)
        ├─ session.Mode == MovingUnit
        ├─ IsValidTarget(level, coord) == true
        └─ ExecuteMove(source, destination)
              ├─ _handlerMutatedGame = true
              ├─ wasCombine = WasFriendlyUnitAt(dst, owner)
              ├─ MovementRules.Move → dst.Owner = attacker; dst.Occupant = unit;
              │                      unit.HasMovedThisTurn = true
              ├─ if WasCapture:
              │     ├─ HandleCapture(...)
              │     │     ├─ state.Territories = TerritoryFinder.Recompute(
              │     │     │       state.Grid, prev, state.Treasury)
              │     │     │     (FindAll + CapitalReconciler.Reconcile +
              │     │     │       Treasury.ReconcileAfterCapture)
              │     │     ├─ if a color lost its last capital:
              │     │     │     PlaySound(PlayerDefeated); human → PendingDefeatScreen
              │     │     ├─ _map.RebuildAfterTerritoryChange()
              │     │     └─ if WinConditionRules.WinnerByDomination → DeclareWinner, clear undo
              │     └─ RebindSelectionToContaining(destination)
              ├─ if MoveResult.Destroyed != null: _map.PlayDestructionEffect(dst, occ.)
              ├─ DispatchActionSound(dst, result, wasCombine)
              └─ FinishPendingAction()
                    ├─ session.ClearPendingAction()
                    ├─ _map.ShowMoveTargets([], …)
                    ├─ _map.ShowMoveSource(null)
                    └─ RefreshViews()
  // TrackHandler, after body:
  if !session.IsGameOver && (_handlerMutatedGame || sessionChanged):
      session.Undo.PushBefore(pre)     // single push, auto-deduped
  _onAfterRefresh?.Invoke()            // TutorialPreviewCues paints last
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
                (no mode → SetSelection(null))
  EmitRejection(level, coord):
    ├─ targetTerritory = TerritoryLookup.FindContaining(state.Territories, coord)
    ├─ inFrontier = coord in/neighbors SelectedTerritory.Coords
    ├─ defenders = (inFrontier && targetTerritory is enemy's)
    │     ? DefenseRules.BlockingDefenders(coord, level, grid, targetTerritory)
    │     : []   // "too far" wins over "defended"
    └─ _map.FlashRejection(coord, shape, defenders)
          ├─ forbidden-slash overlay at target
          ├─ for each defender ≠ target: black arrow defender→target, then QueueFree
          └─ defenders.Any() ? PlayRejectDefended() : PlayRejectGeneric()
  // TrackHandler: no mutation, no undo push.
```

`DefenseRules.BlockingDefenders` walks the target tile plus every adjacent same-territory tile and yields every coord whose `ContributionOf` >= attacker level. Mirrors `Defense(...)` but collects coords instead of taking a max.

Rejected clicks keep the pending mode, `SelectedTerritory`, `MoveSource`, and move/tower/coverage previews — so the next click is another attempt.

### Long-press → rally

```
HexMapView → TileLongClicked(target tile)
GameController.OnTileLongClicked  ── wrapped in TrackHandler:
  └─ OnTileLongClickedBody(tile)
        ├─ ignored if game over, no tile, or any pending mode
        ├─ ignored unless tile color == current player's
        ├─ anyMoved = RallyRules.ResolveRally(grid, territory, target, color)
        │     (unmoved units, sorted closest-to-target w/ lex-min tiebreak,
        │      greedy-repositioned to strictly-closer empty in-territory cell
        │      via MovementRules.Move; does NOT consume the move action;
        │      shared with replay's ApplyLongPressRally)
        ├─ if anyMoved: _handlerMutatedGame = true; PlaySound(Rally); re-select
        └─ RefreshViews()
```

### End turn

```
HudView (End Turn button) → EndTurnClicked
GameController.OnEndTurnPressed
  ├─ if session.IsGameOver → return
  ├─ session.Undo.Clear()                      // commit: no going back
  ├─ EndOfTurnProcessing()
  │     └─ WinConditionRules.WinnerAtEndOfTurn → DeclareWinner if sole capital-bearer
  ├─ if session.IsGameOver:
  │     └─ CheckGameEndConditions()            // fire GameEnded once
  │ else:
  │     ├─ AdvanceToNextActivePlayer()         // skip eliminated
  │     ├─ StartPlayerTurn()                   // reseed → growth → reset → income → upkeep
  │     │     (growth + income skipped round 1; human hand-off auto-selects + fires HumanTurnStarted)
  │     └─ _aiDriver.RunUntilHumanOrDone()     // AI loop if next is AI
  ├─ CancelPendingAction()
  ├─ if AI-next / game-over / auto-select off: SetSelection(null)   // human-next keeps the auto-selection
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
  └─ CenterIfSelectionChanged(...)            // pan to restored selection
```

### AI turn

The whole loop lives on `AiTurnDriver` (see "GameController ↔ AiTurnDriver split"). `RunUntilHumanOrDone` resets per-player bookkeeping and calls `Schedule(turnBoundary)` — the single **re-dispatching** point picking the pacing path each beat. Re-reads `aiSilentMode()`: `Instant` → `InstantAiTick` via `ScheduleUnscaled` (`InstantTurnDelayMs`/0); else paced `StepAiPreview` via multiplier-scaled `Schedule` (`AiBetweenPlayersDelayMs`/`AiActionDelayMs`). All continuations route through it (next-AI-player hop, `StepAiExecute`, the instant `reschedule`, overlay-resume sites `OnDefeatContinuePressed` / claim-victory → `EndTurnNow`) — so a mid-turn speed change **switches tracks at the next beat**. Exception: the preview→execute hop is a direct pacer `Schedule` (`_pendingAiAction` already chosen; switch lands at the next action boundary, avoiding RNG re-draw). The transition/dispatch skeleton is `StepPacing.Redispatch` (shared with replay's `ScheduleNextReplayBeat`): it syncs silent mode, forces `RebuildAfterTerritoryChange` on instant→paced, clears the highlight on paced→instant, and stores the track **before** dispatching (load-bearing under `SynchronousAiPacer`, where the dispatch runs the continuation chain inline). `_aiTrackInstant` holds the previous track to detect the transition.

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
  ├─ ApplyAiActionCore(action)   ── shared mutation core: record beat (live only)
  │     + ExecuteAiMove/BuyUnit/BuildTower/… ; returns result coord
  │     (null = unrecognised → defensive return)
  ├─ CheckGameEndConditions; ShowHighlightAndRefresh(resulting terr.)
  ├─ if PendingDefeatScreen: RefreshSilentMode + RefreshViews, return
  │     without scheduling — dismissal handler resumes via Schedule
  └─ schedule next StepAiPreview after AiActionDelayMs
```

**Instant fast-forward (shared loop).** Live AI Instant and instant replay share one chunked, frame-yielded loop, `GameOperations.RunInstantTick(active, step, onExhausted, reschedule)`:

```
RunInstantTick:
  ├─ _suppressMapRebuild = true
  ├─ loop step():  Continued → keep draining
  │                TurnBoundary → break (turn completed)
  │                Exhausted → _suppressMapRebuild=false; onExhausted()
  │                budget (InstantBudgetMs, 8 ms) → break, no repaint
  ├─ _suppressMapRebuild = false
  ├─ if turnBoundary: _map.RebuildAfterTerritoryChange + RefreshViews
  └─ reschedule(turnBoundary)   ── caller's re-dispatching scheduler, so a
        mid-run speed change can switch OFF the instant track here (AI →
        AiTurnDriver.Schedule, replay → ScheduleNextReplayBeat; each owns its delay)
```

Two wrappers feed it:

- **`ReplayRecorder.InstantReplayTick`** — `step` = `ReplayInstantStep` (pop a beat, `ExecuteReplayBeat`, game-end check; `TurnBoundary` on `ReplayEndTurnBeat`); `onExhausted` = `EndReplay`.
- **`AiTurnDriver.InstantAiTick`** — `step` = `AiInstantStep` (chooser; `ApplyAiActionCore`, or on null/step-cap `EndCurrentAiPlayerTurnCore`; `TurnBoundary` when an AI turn completes and the next player is AI; `Exhausted` on game-over, hand-back to human, or pending defeat/claim overlay); `onExhausted` = `EndInstantAiBatch` (final rebuild + lift silent + one paint; or if overlay pending, lift silent + RefreshViews, dismiss handler resumes).

Chooser cost is inline within the 8 ms budget; the driver yields a real frame between ticks (`ScheduleUnscaled` → timer) so pan/zoom/input stay live. `HandleCapture.RebuildAfterTerritoryChange` is `_suppressMapRebuild`-gated, coalescing redraw + tile-fill resync to the turn-boundary / batch-end repaint. Live AI Instant is 1:1 with instant replay; one difference: the "Opponents are taking their turns…" overlay stays for live play (via `RefreshSilentMode`), replay leaves off. `ApplyAiActionCore` / `EndCurrentAiPlayerTurnCore` are shared with paced (pinned by `InstantAiTests.InstantAi_SameBeatsAndFinalStateAsPaced`).

`InSilentAiBatch()` = `aiSilentMode() && currentPlayer.IsAi && !PendingDefeatScreen` (`aiSilentMode` = `!IsReplayMode && AiSpeed == PlaybackSpeed.Instant`). The **input gate** and view-flag source (`RefreshSilentMode` pushes `InSilentAiBatch || _replayInstantActive` to `_map.SetSilentMode`): every top-level human handler (`TrackHandler`-wrapped click/key, plus `OnEndTurnPressed`, `OnUndo*`, `OnRedo*`, `OnDefeatContinuePressed`, `OnClaimVictory*`) short-circuits on it so input can't mutate `SessionState` between frame yields. `PendingDefeatScreen.HasValue` flips it false mid-batch so the overlay paints and `OnDefeatContinuePressed` dispatches; dismiss handler resumes via `AiTurnDriver.Schedule`. Game-end branches ignore it and always refresh. (The per-action **cue** gate is the sibling `IsSilent()`, which drops the `!PendingDefeatScreen` term so the AI's capital-destroying blow stays silent as it queues the overlay — see the audio section.)

The **overlay is decoupled from silence**: `RefreshSilentMode` shows it whenever an AI acts in live play at *any* speed (`!IsReplayMode && !GameEndedFired && !IsGameOver && currentPlayer.IsAi && !PendingDefeatScreen`), tracked by `_aiBatchOverlayShown` — so paced AI turns show it too, though only the Instant batch is silenced. (Replay never shows it.)

### Automate (AI plays the human's remaining turn)

The HUD's Automate toggle (`AutomateClicked`, gear-with-play glyph beside End Turn; `G` hotkey) runs AI decisions for the *current human* slot until the chooser runs dry — without ending the turn. The loop lives on `GameController` (it needs `TrackHandler`). Between moves it dispatches through `ScheduleAutomateStep` — the automate analog of `AiTurnDriver.Schedule`, built on the same `StepPacing.Redispatch` — which re-reads the `automateIsInstantMode` probe (`UserSettings.AutomateSpeed == Instant`, injected by `Main`) so a mid-run Settings change switches tracks at the next beat. Paced speeds run a two-beat step machine mirroring the AI driver's cadence; Instant drains through the shared chunked loop:

```
OnAutomatePressed (toggle):
  ├─ gates: replay / silent batch / preview / recording / game-over / AI turn / overlay pending
  ├─ if already automating: StopAutomation("user")
  ├─ TrackHandler(OnCancelActionPressedBody)   // cancel any open buy/build/move intent (own undo entry)
  ├─ _automating = true; reset _automateVisited/_automateSteps; RefreshViews (button → running)
  └─ ScheduleAutomateStep()   // paced → StepAutomatePreview; Instant → InstantAutomateTick

StepAutomatePreview:  halt checks → action = _automateChooser(state, me, _automateVisited, ops.Rng)
  ├─ null → StopAutomation("exhausted", latch + mark all actionable visited) ;
  │     step-cap (64) → StopAutomation("step-cap")
  ├─ SelectActingTerritoryForAutomate(acting, paint: true)   // select the territory about to act —
  │     // its own UndoEntry, like the player pressing Next Territory; no-op if already selected
  ├─ CenterOnTerritory(acting)   // ease the camera to it, same feel as Next Territory
  └─ highlight acting territory → pacer.Schedule(StepAutomateExecute, AiPreviewDelayMs)

StepAutomateExecute:
  ├─ ApplyOneAutomateMove(action)   // shared with the instant drain:
  │     // TrackHandler(() => { _handlerMutatedGame = true; ApplyAiActionCore(action); })
  │     //   — one UndoEntry per move; the beat ApplyAiActionCore records lands in the entry's
  │     //     batch, keeping the recorder's undo↔beat lockstep intact
  │     // RebindSelectionAfterAutomateMove()  — capture recompute leaves session.SelectedTerritory
  │     //   stale; re-resolve the same logical territory by capital (ExecuteAi* never rebinds)
  │     // CheckGameEndConditions
  ├─ halt checks (overlay / game-over stop the loop)
  └─ highlight result territory → ScheduleAutomateStep()

InstantAutomateTick = ops.RunInstantTick(active: _automating, step: AutomateInstantStep,
                                         onExhausted: no-op, reschedule: ScheduleAutomateStep)
AutomateInstantStep:  halt / step-cap / chooser-null → StopAutomation(...) → Exhausted
  ├─ SelectActingTerritoryForAutomate(acting, paint: false)   // same UndoEntry, no view work
  ├─ ApplyOneAutomateMove(action) → post-move halt check → Exhausted or Continued
  └─ never TurnBoundary (automation never ends the turn): budget breaks yield a bare frame,
        the single repaint happens at the stop
```

- **Chooser**: the `automateChooser` ctor param, default `ComputerAi.ChooseNextAction` — `AiDispatcher.ChooseForCurrentPlayer` would return null for a Human slot. Wrapped in its own `AiActionLowering` instance (make-way lowering + reposition loop-guard; `Reset()` on every Automate press), so a make-way tower lands as two individually-undoable moves. Tests inject scripted queues. Null = "nothing left to automate" (there is no EndTurn `AiAction`).
- **Every automated step is individually undoable** — selections and moves alike; the standard undo handlers walk them back one at a time (undo re-centers the camera through the acting territories). Automation is a human-turn flow, so the "AI actions are not undoable" invariant is untouched (that covers AI *players'* turns). Both tracks record identical undo/beat stacks — Instant skips the presentation, not the bookkeeping (pinned by `Automate_Instant_SameFinalStateAndUndoDepthAsPaced`).
- **Interruption is a flag, never a cancel**: `_automating` is checked at every beat entry (and gates the instant drain via `RunInstantTick`'s `active`); `StopAutomation` clears it and stale scheduled beats no-op (the shared pacer is never cancelled). Every `TrackHandler`-wrapped human input stops a running loop at handler entry (`_inAutomateStep` exempts the loop's own selection + move steps); the non-wrapped handlers (undo/redo ×4, End Turn, defeat/claim) carry explicit `StopAutomation("input")` calls. Stops always land *between* moves. When the stop ends an instant batch (`_automateTrackInstant`), `StopAutomation` also runs the batch-end cleanup: structural rebuild (the drain suppressed per-capture rebuilds) + `RefreshSilentMode` (lifts the view's silent flag).
- **Exhaustion latch** (`_automateExhausted`): set only by the chooser-null stop; the button greys out (re-pressing would no-op) even if manual actions remain. Cleared by `ApplySnapshot` (any undo/redo), a manual game-mutating `TrackHandler` push (which triggers one extra refresh — the body's own refresh ran pre-clear), and `EndTurnNow`. User interrupts don't latch.
- **Exhaustion lights End Turn**: the chooser-null stop also unions every still-actionable territory's capital into `VisitedThisTurnCapitals` (one `TrackHandler` entry, undoable; no-op when the loop's own selections already visited them), and the latch counts as "finished the last territory" in the End Turn CTA condition — so a completed automation run always leaves End Turn lit even when the chooser declined an affordable action or left an actionable territory selected. Logged as `[automate] exhausted → turn-visited += N`.
- **Pacing**: `UserSettings.AutomateSpeed`, its own Settings row, independent of AiSpeed/ReplaySpeed. The shared pacer's multiplier closure in `Main` branches on `GameController.IsAutomating` for the paced speeds; Instant never reaches the multiplier — `ScheduleAutomateStep` routes it to the unscaled chunked track, making Automate the third `RunInstantTick` wrapper alongside live-AI Instant and instant replay.
- **Silence**: `GameOperations.InSilentAutomateBatch()` (= `automateSilentMode() && isAutomating()`) joins the cue gate (`IsSilent`) and the view flag (`RefreshSilentMode`), so an Instant batch plays no sounds/VFX/tweens and never pans. It deliberately does NOT join `HumanInputLocked` — input between the drain's frame yields stays live so it can stop the loop. Paced automate is never silent: per-move sounds and the camera pan play at every paced speed.
- **Instrumentation**: `Log.LogCategory.Automate` Debug — start (turn/player/undo depth), one `pan ->` line per paced preview, one line per move (step index, action, undo depth after push), track transitions (`paced→instant` / `instant→paced`), stop with reason (`user` / `input` / `exhausted` / `step-cap` / `overlay` / `game-over` / `not-human`), and `instant batch end` with the move count.
- Pinned by `tests/GameControllerTests.Automate.cs` (undo walk-back incl. selection entries, interrupt-between-moves via `GodotAiPacer` + `ManualTimerFactory`, beat-stack sync, exhaustion-latch lifecycle, selection rebind, camera pan, instant silence/no-pan/parity/overlay-stop).

Tests use `SynchronousAiPacer` (`Schedule` + `ScheduleUnscaled` drain inline) or `QueuedAiPacer` (`DrainAll`).

### Replay turn (paced)

Mirrors the AI step machine, consuming a recorded `ReplayBeat` log instead of asking the AI:

```
BeginReplay (public, called from victory-overlay Replay button):
  ├─ _aiPacer.Cancel  (drop stragglers)
  ├─ _replayMode = true, _replayIndex = 0, _gameEndedFired = false
  ├─ _initialSnapshot.ApplyTo(grid, treasury) → territories
  ├─ _state.Turns.Reset(initialPlayerIndex, initialTurnNumber)
  ├─ clear session: Winner, PendingDefeat, PendingClaim, pending action
  ├─ ClearUndoAndReplayBookkeeping
  ├─ _replayInstantActive = replayIsInstantMode?()  (UserSettings.ReplaySpeed
  │     == Instant; injected by Main)
  ├─ if instant: _map.SetSilentMode(true)  (sound/VFX/tweens off)
  ├─ if paced AND the log carries a ReplayDemoStartBeat: fast-forward —
  │     execute every beat up to and including the first marker inline,
  │     silent + rebuild-suppressed, then one RebuildAfterTerritoryChange
  │     (the author's staging; each loop restart re-runs this)
  ├─ map.RebuildAfterTerritoryChange + overlay clears + RefreshViews
  └─ if instant: ScheduleUnscaled(InstantReplayTick, 0)
       else schedule StepReplayPreview after AiBetweenPlayersDelayMs

StepReplayPreview:
  ├─ if _replayIndex >= _replayBeats.Count → EndReplay
  ├─ resolve acting territory (TerritoryLookup.FindOwnedContaining
  │     on beat's source/capital coord; select beats resolve their anchor's
  │     territory so back-to-back selects don't flicker)
  ├─ _map.ShowHighlight(acting)
  ├─ Move / RejectedMove beats: _map.ShowMoveSource(From) — the live
  │     pickup pulse, so playback reads "select, then move"
  ├─ RefreshViews
  └─ schedule StepReplayExecute after AiPreviewDelayMs
       (or AiActionDelayMs if beat is EndTurn / SelectTerritory)

StepReplayExecute:
  ├─ dispatch by record type:
  │    ReplayMoveBeat        → ExecuteAiMove(From, To)
  │    ReplayBuyBeat         → ExecuteAiBuyUnit(Capital, To, Level)
  │    ReplayBuildTowerBeat  → ExecuteAiBuildTower(Capital, To)
  │    ReplayEndTurnBeat     → ReplayApplyEndTurn (EndOfTurnProcessing
  │                            + AdvanceToNextActivePlayer + StartPlayerTurn)
  │    ReplayClaimVictoryBeat → DeclareWinner (silent — no overlay)
  │    ReplayDismissClaim    → record threshold, no advance (next EndTurn beat handles it)
  │    ReplayDismissDefeat   → clear PendingDefeatScreen flag (silent)
  │    ReplayLongPressRallyBeat → ApplyLongPressRally (re-derives moves
  │                            deterministically from state)
  │    ReplaySelectTerritoryBeat → resolve territory containing Anchor;
  │                            session.SelectedTerritory + ShowHighlight
  │                            (authored selection, like a live click)
  │    ReplayRejectedMoveBeat → FlashRejection at To (shape from the unit at
  │                            From, defenders re-derived via DefenseRules);
  │                            mutates nothing
  │    other TutorialOnlyBeat → silently skip (narration + demo-start marker;
  │                            Tutorial Preview consumes narration via
  │                            TutorialNarrationDriver)
  ├─ Move / RejectedMove: _map.ShowMoveSource(null)  (pickup done)
  ├─ CheckGameEndConditions; RefreshViews
  ├─ if IsGameOver → EndReplay (recorded game-ending beat re-fired GameEnded;
  │     Main re-runs SetReplayAvailable)
  └─ schedule next StepReplayPreview after
       AiBetweenPlayersDelayMs (if beat was EndTurn) else AiActionDelayMs

EndReplay raises ReplayEnded (recorder event, forwarded as
GameController.ReplayEnded) — the Instructions demo player loops by
re-calling BeginReplay on it.
```

**Instant replay (`ReplaySpeed.Instant`).** `BeginReplay` schedules `InstantReplayTick` via `ScheduleUnscaled` — the replay wrapper over `RunInstantTick` (`ReplayInstantStep` drains beats, `TurnBoundary` on each `ReplayEndTurnBeat`; `onExhausted` = `EndReplay`). Silent, per-turn-sampled fast-forward.

Bypasses the multiplier via `ScheduleUnscaled` (no Instant arm) and yields a real frame each tick. The dominant per-beat cost — `HandleCapture`'s full-map `RebuildAfterTerritoryChange` — is `_suppressMapRebuild`-suppressed and coalesced into one rebuild + refresh per player-turn (`InstantBudgetMs` 8 ms/tick; `InstantTurnDelayMs` 200 ms between turn repaints). `RefreshSilentMode` ORs in `_replayInstantActive` so a `ReplayEndTurnBeat` → `StartPlayerTurn` can't un-silence mid-stream; `EndReplay` lifts silent mode and does one final `RebuildAfterTerritoryChange`. Fidelity identical to paced — mutation order unchanged, only view work deferred. Live AI Instant uses the same `RunInstantTick` (wrapper `InstantAiTick`).

Replay reuses the live `ExecuteAi*` helpers — same captures, FX, `HandleCapture` reconciliation. The actor per beat isn't passed: `BeginReplay` restored `CurrentPlayerIndex`, and every `ReplayEndTurnBeat` steps it forward, so `_state.Turns.CurrentPlayer` is right when each `ExecuteAi*` fires.

**Invariant — no AI-only rules in the replay execute path.** `ExecuteAi*` replay *every* beat, including human ones, so they enforce only game legality, never AI *selection* heuristics (else a recorded human beat would throw). The canonical example is the make-way tower build: `ComputerAi` may emit an `AiBuildTowerAction` *intent* targeting a tile that holds an own unmoved unit, but that intent never reaches execution — `AiActionLowering` (the controller-side chooser wrapper) lowers it into two ordinary discrete actions, a reposition to `PurchaseRules.TowerPushDestination` followed by the build on the vacated tile, each recorded as its own replay beat and (during automation) its own undo entry. `ExecuteAiBuildTower` enforces the universal strict rule (`PurchaseRules.IsValidTowerLocation`: empty + in-territory) for every actor. Related boundary: `Unit.HasMovedThisTurn` is the *game's* movement-consumption rule (set only by movement-consuming arrivals in `MovementRules.ResolveArrival`); repositions never set it, for any actor, so replay needs no AI knowledge — the AI's own "already repositioned this unit, don't loop" bookkeeping is `AiActionLowering`'s controller-owned set, passed into the chooser. New AI-only constraints: enforce at candidate enumeration or in the chooser wrapper — never a replay-mode gate, never model state.

**Recording vs. playback.** Every beat-recording site is gated on `!_replayMode`. Human input handlers (`TrackHandler`-wrapped + overlay handlers) early-return on `_replayMode`. The `StartPlayerTurn` autosave gate adds `&& !_replayMode`.

**Long-press rally** special case: the beat carries only the target coord, not the per-unit move list. Replay re-runs `ApplyLongPressRally(target)`, delegating to `RallyRules.ResolveRally` — the same body the live handler calls. Sorts units and destinations by `(distance, lex-min coord)`, so re-derivation is deterministic. Matches the trust model for `EndOfTurnProcessing` (tree growth, grave aging, upkeep — deterministic from state, one beat).

## AI subsystem

- **`AiAction`** — discriminated union: `AiMoveAction`, `AiBuyUnitAction`,
  `AiBuildTowerAction`, `AiBuyCombineAction` (buy a unit and combine onto a
  friendly unit to unlock a new movement-consuming target; phase 2b).
- **`AiCommon` phase-split enumeration** — single source of legal candidates,
  one enumerator per phase: `EnumeratePhase1ForUnit`
  (captures/chops/grave-clears), `EnumeratePhase2aForUnit` (combine-to-unlock,
  existing units), `EnumeratePhase2b` (buy-and-combine-to-unlock),
  `EnumeratePhase3ForLevel` (buy-to-capture/chop for one purchase tier; the
  all-tiers `EnumeratePhase3` wrapper walks levels cheapest-first with a
  min-sufficient-level `covered` dedup for its non-phase consumers),
  `EnumeratePhase4Towers` (solvent tower sites on border tiles — empty, or
  a make-way *intent* on a tile holding an own unmoved unit with an escape,
  per `PurchaseRules.IsValidTowerLocationWithPush`; `AiActionLowering`
  lowers a chosen intent into the reposition-then-build pair, and the
  simulator's `ApplyBuildTower` mirrors that pair atomically so 1-ply
  scoring predicts the two real beats; proximity to existing towers is
  never filtered — overlap is discounted by
  `AiStateScorer.BuildTowerBonus`), `EnumeratePhase4bForUnit` (defensive
  repositions to border tiles; units in the caller-owned
  `repositionedUnits` loop-guard set are skipped). Shared filter
  `UnlocksMovementConsumingTarget` admits a 2a/2b
  combine only when the combined level reaches a movement-consuming target
  neither source could; **both combine phases require an unmoved target** —
  the combined unit inherits the destination's `HasMovedThisTurn`
  (`ResolveArrival`), so merging into an exhausted unit would bill the upkeep
  increase immediately for zero benefit this turn (deferring the combine
  dominates). The phase-2 enumerators and the unlock filter take an optional
  **`AiTargetCache`** — a per-territory-scan memo of `ValidTargets` and its
  movement-consuming subset, keyed by level (sound because `ValidTargets`
  depends only on level + territory and the search never mutates live state;
  an instance must not outlive its scan). `ComputerAi` shares one instance
  across a scan's phase-2 unit × level pairs; callers that pass none get a
  transient one. `MovableUnitTiersWeakestFirst` yields one (level, coord) entry
  per distinct movable tier, ascending — one representative suffices because
  `ValidTargets` depends only on level + territory, never unit position. Only
  these helpers know rule legality. **Solvency gating applies to
  upkeep-increasing actions only** — buys/combines/towers defer to
  `UpkeepRules.SurvivesNextUpkeep(gold, netIncome)` (treasury +
  `UpkeepHorizon`×netIncome ≥ 0, horizon 5); phase-1 actions are never gated.
  `AiStateScorer`'s bankruptcy lookahead uses the same `SurvivesNextUpkeep`, so
  a scorer-approved buy/combine is never dropped by the enumerator.
- **`ComputerAi`** — the only AI (drives every `PlayerKind.Computer` slot).
  1-ply lookahead via `AiSimulator.Clone` + `AiStateScorer.Score`.
  **Stepwise-greedy:** each `ChooseNextAction` picks the largest non-exhausted
  owned territory (descending cell-count, capital-coord tie-break) and tries
  phases **1 → 2a → 2b → 3 → 4a → 4b**, committing to the first yielding an
  action; a territory is visited only when all phases empty. The offensive
  phases (1, 3) route through the shared `TryTiersWeakestFirst` loop: tiers
  walk weakest→strongest (phase 1 one representative unit per movable tier
  via `MovableUnitTiersWeakestFirst`, phase 3 purchase levels
  Recruit→Commander via `EnumeratePhase3ForLevel`), committing to the best
  candidate of the first tier that yields any — the cheapest sufficient unit
  takes each tile, a dry tier is skipped for the rest of the call
  (`[tier]`/`[tier-skip]` at Ai:Debug), and the driver's re-entry per applied
  action restarts the scan from the weakest tier once the board changes. The
  strength-concentrating phases (2a, 4b) iterate units power-then-coord
  (`MovableUnitsInPowerOrder`). Within a tier or unit, all candidates are
  scored and the best delta wins.
  **Phases 1, 2a, 2b, and 3 take their best legal candidate regardless of
  delta sign** (`BestPositiveDelta` with `threshold = int.MinValue`) — an
  offensive/unlock action is never declined for the status quo; the spend
  phases' economics are guarded by the enumerators' solvency gates, not the
  score. Only the defense-only phases 4a/4b keep the strictly-positive (`> 0`)
  gate (doing nothing is a valid defensive choice). Ties resolve to the
  first-yielded candidate.
  `BestPositiveDelta` logs one `[candidate]` verdict per scored candidate
  (phase, action, delta, threshold, accepted/rejected) at **Ai:Trace** — the
  firehose tier, deliberately below the `Ai:Debug` that `FOUREXHEX_6AI` pins;
  the per-territory `[heuristic]` best-delta summary stays at Debug.
  The bare mutation per action kind lives once in **`AiActionCore`**
  (Model: reposition/combine detection, owner-difficulty gold deduction,
  `MovementRules` placement); `AiSimulator.Apply*` and
  `GameOperations.ExecuteAi*` both call it and differ only in their
  envelopes — the simulator early-returns on bad lookups and reconciles
  captures with a bare `TerritoryFinder.Recompute`; the live path
  validates and throws, runs the full `HandleCapture` envelope, and
  fires view effects. `Unit.HasMovedThisTurn` is set exclusively by
  movement-consuming arrivals inside `MovementRules.ResolveArrival` —
  repositions leave it false on every path, so simulator, live play,
  and replay agree by construction.
  **A new AI-capable action adds its mutation to `AiActionCore` and an
  envelope to each caller.** Envelope equivalence pinned by
  `AiSimulatorDriftTests`: every enumerated action applied through both
  paths must produce matching `GameStateChecksum` canonical strings
  (plus clone-fidelity and fixture-rot guards over all four action kinds;
  make-way tower intents run the real side as the lowered two-beat pair).
  `AiSimulator.Apply` throws `NotSupportedException` on unmodeled kinds (Rally,
  ClaimVictory, Dismiss*) so drift surfaces loudly.
- **`AiStateScorer`** — pure `GameState → int` (self value minus enemy values).
  Constants: `TileWeight` 10, `NetIncomeWeight` 1, `FragmentationPenalty` 15,
  `EnemyEdgePenalty` 3, `UndefendedBorderPenalty` 10, `OwnTreePenalty` 35. Tree
  penalty sits above 3× `UndefendedBorderPenalty` so a chop stays positive even
  uncovering three border tiles. Gold hoards contribute zero standing value.
  **Gold tiles** carry a two-sided premium `TileWeight × IncomeRules.GoldTileBonus`
  per income-producing gold tile (5× ordinary), added in `TerritoryValue`
  (subtracted for enemies), un-gated by bankruptcy, counted via
  `TreeRules.CountGoldIncomeTiles` — a tree-blocked gold tile reads as ordinary
  until chopped, making gold-trees the most desirable chops.
  **Mountains** valued via a one-sided defense term: each own tile bordering an
  enemy adds `ContestedDefenseWeight` (2) × `min(Defense, ContestedDefenseCap 3)`
  in `Score()`. Free because `DefenseRules.Defense` bakes in the `+1`
  high-ground (no `IsMountain` reference). Cap (≥ 3) clamps over-garrison; cancels
  in the 1-ply diff when border defense is unchanged.
  **Towers** valued per-action by `BuildTowerBonus` (added to the candidate's
  delta in `ComputerAi`, like `EvacuationBonus`; absolute `Score` untouched).
  Each covered border tile (placement + same-territory neighbors) whose
  *committed* defense is below tower grade (`TowerGradeDefense` 2) earns
  `BuildTowerCoverageBonus` 10 — committed = towers, capitals, and
  already-moved units (`DefenseRules.CommittedDefense`, placement coord
  excluded so a candidate never disqualifies its own coverage); a free-move
  unit is transient and never counts. A qualifying tile that nonetheless reads
  as defended in `Score` (free-move unit, capital, weak locked unit) adds
  `TransientDefenseCredit` 10, compensating for the `UndefendedBorderPenalty`
  relief the diff won't show — so transiently-defended borders are worth the
  same as naked ones, and tiles already covered by a tower-grade committed
  defender are worth nothing.
- **`ReplayDrivenAi`** — script-driven chooser, used only by TutorialBuilder
  Preview. Replays recorded non-player-0 `ReplayBeat`s through the AI step
  machine via a shared `ScriptCursor` (also referenced by `TutorialPreview`, so
  beats consumed by either advance the other). In `scripts/Tutorial/`; plugged
  into `GameController` directly as `aiChooser`, bypassing `AiDispatcher`.
- **`AiDispatcher.ChooseForCurrentPlayer`** — returns `ComputerAi`'s choice for
  a `Computer` slot, null for a `Human` one, by `Player.Kind`. Wired into
  `GameController` as the single `aiChooser` for normal play.
- **`AiActionLowering`** (Controller) — `GameController` wraps every injected
  chooser (AI-turn and Automate tracks, one instance each) with this class;
  the drivers consume its 4-arg `Choose` while the inner chooser takes the
  5-arg form ending in the `repositionedUnits` set. It owns the AI's
  per-(turn, player) decision state: the reposition loop-guard set (fed to
  phase 4b; ordinary repositions enter it, make-way moves deliberately
  don't) and the make-way stash — a chosen tower intent on an own unmoved
  unit's tile comes back as the reposition first, then the build on the
  next call, re-validated against live state and dropped if stale.
  `[make-way]` logs at Ai:Debug. Automate's start pressing `Reset()` covers
  same-turn restarts; turn/player key changes reset automatically. Vikings
  bypass it (`VikingAi`; they never build).
- **AI tracing** lives in `Log.LogCategory.Ai` / `Turn` / `Capture` (candidate
  diagnostics, per-turn headers + end-turn + action lines, capture diffs). Off
  by default; enable via `FOUREXHEX_LOG` or `FOUREXHEX_6AI`. See **Logging**.

## Save / load

Deterministic-on-reload contract: a saved master seed plus `(turn, player)`
uniquely determines the RNG sequence for that turn, so a save records only the
seed (no consumption count) and load reproduces it.

- **Master seed.** `GameController` takes a `seed:` ctor arg, exposes
  `MasterSeed`. `_rng` (a `DeterministicRng`) reseeds from
  `(masterSeed, turnNumber, currentPlayerIndex)` via the splitmix32 `MixSeed`
  at the top of every `StartPlayerTurn` and every `Resume`
  (`ReseedRngForCurrentTurn`). `MapGenerator.BuildInitialGrid` uses the same
  seed; the menu's previewed seed is reproducible end-to-end.
  `GameOperations.RngStreamDigest` folds every per-turn sub-seed and retired
  stream hash into one cumulative digest; game end logs
  `[determinism] final checksum=<GameStateChecksum> rngStreamHash=<digest>`
  (category `Determinism`, plus a `[determinism] mapgen` line from
  `MapGenerator`) — the one-line cross-run/cross-platform determinism diff.
  `DeterminismProbe.Run` (Controller) reproduces the same fingerprint
  in-process (the cheat menu's Determinism Check); its expected values are
  pinned by `DeterminismProbeTests`, which fails on any machine or runtime
  that computes a different game.
- **Autosave.** `Main` subscribes `controller.HumanTurnStarted` to a handler
  writing the `autosave` slot via `SaveStore.WriteAutosave`. Fires once per
  human turn, after start-of-turn bookkeeping; AI turns and game-over states
  skipped.
- **Named saves.** Pause menu's **Save Game** opens an `AcceptDialog` for a slot
  name and calls `SaveStore.WriteSlot`. The `autosave` slot is reserved.
- **In-game load.** Pause menu's **Load Game** opens the shared
  `SlotPickerDialog` from `SaveStore.ListSlots`. Picking a slot sets
  `LoadRequest.Pending`, cancels in-flight AI timers via
  `_controller.AbandonGame`, unpauses (`GetTree().Paused` persists across
  scenes), and changes scene to `main.tscn` — same final path as the menu's Load
  button.
- **Origin map name.** Optional `OriginMapName` identifies the starting map a
  game descended from (null for procedural); rides autosave to keep the
  bottom-left save-name label correct (shown with underscores→spaces).
- **Claim-victory prompted tiers.** Optional
  `ClaimVictoryPromptedHighestByColorHex` — hex→percent map of the highest tier
  (50/75/90) each human color has dismissed; empty/missing in fresh games and
  starting maps. `Main` seeds
  `SessionState.ClaimVictoryPromptedHighestThreshold` from it on load so the
  per-tier once-per-game invariant survives reloads.
- **Campaign level pointer.** Optional `CampaignLevel` (0..255) for campaign
  games; null/missing for freeform. Rides autosave so a resumed campaign game can
  record the win on game-over. `Main._Ready` restores it into
  `GameSettings.CampaignLevel` (or clears it for freeform/starting-map/diagnostic
  loads), and also drives the bottom-left label as `"Level XX"`
  (`CampaignProgress.LabelFor`, 2-digit uppercase hex).
- **Game mode.** Optional `Mode` (`GameMode`); null/missing = `Freeform`. Only
  Rising Tides writes it. Grown water rides the existing `Water` field, so flood
  progress round-trips. Deserialize feeds it into the `GameState` ctor; the
  starting-map load path forwards it too.
- **Tide forecast.** Optional `PendingTide` (list of `{Q, R, DemoteOnly}`);
  null/missing = empty. Only a mid-turn Rising Tides save writes it. Can't be
  recomputed on load (RNG advanced, grid may have changed), so it's persisted
  and restored onto `GameState.PendingTide`.
- **Replay version.** A save with a `Replay` block carries `ReplayVersion`
  (`SaveSerializer.CurrentReplayVersion`) — the stamp of the RNG + rules
  generation that recorded it. Bumped whenever a change would derail recorded
  beats (PRNG swap, capital/tide selection rules, movement legality). On load a
  mismatched/absent stamp drops the replay and loads the game state normally;
  a `Tutorial` save (replay IS the content) fails loudly instead.
- **Load.** Main menu's Load button populates `LoadRequest.Pending` with a
  `LoadedSave` (state + players + master seed + max-turn cap + optional
  OriginMapName + claim-victory tiers) and changes scene to `main.tscn`.
  `Main._Ready` consumes and clears it. On the in-progress path, fresh grid
  construction is skipped and `controller.Resume()` runs instead of
  `StartGame()`.
- **`Resume()`** reseeds the RNG, runs leading AI turns until control reaches a
  human (or game ends), refreshes views, then fires `HumanTurnStarted` if the
  resumed player is human (so the autosave hook runs after a load).
- **Play Again.** Victory/Defeat overlays raise `NewGameClicked`, handled by
  `Main.RestartCurrentGame`: write
  `GameSettings.MasterSeed = _controller.MasterSeed` (procedural map regenerates
  with identical seed), and if `_originMapName != null` re-populate
  `LoadRequest.Pending` with `SaveStore.LoadStartingMap`, then
  `GetTree().ReloadCurrentScene()`. Origin-map reload failures log a warning and
  fall through to procedural with the preserved seed.

`SaveStore` reads/writes `user://saves/`, `user://maps/`, `user://tutorials/`,
and reads `res://tutorials/` (bundled, read-only: `Tutorial.json` starting map,
`full_tutorial.json`, and the `instr_*.json` Instructions demos). Exposes
`WriteAutosave`, `WriteSlot`, `WriteMapSlot`,
`WriteTutorial`, `ListSlots`, `ListMaps`, `ListTutorials`, `LoadSlot`, `LoadMap`,
`LoadTutorial`, `LoadBundledMap`, `LoadBundledTutorial`, `LoadStartingMap`
(tries `user://maps/` then
`res://tutorials/`; used by Play Again), `SanitizeSlotName`. `SaveSerializer` is
the JSON layer. Both `Serialize` (in-progress) and `SerializeMap` (starting
maps) write each player's `Kind` and `Difficulty`, so a saved map **bakes its
exact roster**. Older saves load with absent fields defaulting (`Mode` →
`Freeform`; `PendingTide`/`CampaignLevel`/`OriginMapName` empty/null).

**Variable player count.** Two coupled mechanisms let a save hold a 2–6 player
game:

- **Slot, not list position.** A `PlayerDto`'s `Index`/`ColorHex` derive from
  the player's **slot** (`PlayerId.Index`), not roster position;
  `OwnerIndexToId` resolves a tile's stored owner-slot by **matching slot**, not
  list-indexing (owner-slot absent from active roster → neutral).
- **`None` + baked maps.** `SerializeMap` serializes all six colors' kinds
  (including `None`); `DeserializePlayers` **excludes `None`** and sets
  `LoadedSave.MapHasBakedKinds`. The starting-map load path (`Main`) plays
  `loaded.Players` when kinds were baked, else `Player.LegacyDefaultRoster`
  (6 players, Red human, rest Computer, Soldier).

**iOS AOT constraint: source-generated `JsonSerializerContext`.** iOS forbids
JIT (AOT-compiled), so `System.Text.Json`'s reflection path throws
"Reflection-based serialization has been disabled." Every
`JsonSerializer.Serialize`/`Deserialize` routes through a source-generated
`JsonTypeInfo<T>`: `src/FourExHex.Model/FourExHexJsonContext.cs` declares the
top-level context with `[JsonSerializable(typeof(SaveData))]` (used by
`SaveSerializer` and `SaveStore`'s SavedAt-header read) and
`[JsonSerializable(typeof(CampaignData))]` (used by `CampaignSerializer`);
`scripts/UserSettings.cs` nests its own `JsonContext` to reach its
`private sealed class SettingsDto`. `[JsonSourceGenerationOptions]` carries
`WriteIndented` / `WhenWritingNull`. **Adding a new top-level serialized type
means adding it to the context's `[JsonSerializable]` list.** The
discriminator-string-plus-hand-switch shape (`SerializeOccupant` /
`SerializeReplayBeats`) keeps the surface tiny. Both accept an optional
`Tutorial` POCO round-tripping as the top-level `"Tutorial"` block carrying
`{ Title }`; gameplay lives in the sibling `"Replay"` block. `Tutorial` and
`Replay` must both be present on a tutorial save (Deserialize throws otherwise);
absent on regular saves and starting maps. `SaveSlotInfo` is the slot listing
record.

**Replay block.** `Serialize` and `WriteSlot` / `WriteAutosave` accept an
optional `Replay` POCO round-tripping as the top-level `"Replay"` block:

- `InitialState` — per-game-start `GameStateSnapshot` (tiles + occupants +
  capital gold + territories) plus starting `TurnNumber` / `CurrentPlayerIndex`.
  Captured by `GameController.StartGame` after `SeedStartingGold` and before
  `Resume` — the anchor `BeginReplay` rewinds to.
- `Beats` — ordered list of `ReplayBeat`s. Same kind-discriminated DTO pattern
  as tutorial beats; switches in `SerializeReplayBeats` /
  `DeserializeReplayBeats` handle each kind (Move / BuyUnit / BuildTower /
  EndTurn / LongPressRally / ClaimVictory / DismissClaim / DismissDefeat).

Absent from `Map` and `Tutorial` flavors. A save without a complete replay log
loads with the controller capturing a `_initialSnapshot` at load time (so future
autosaves can carry replay data) and setting
`_replayDataIsCompleteFromStart = false` so the victory-overlay Replay button
stays disabled — the log starts after load, not at game start.

## Campaign mode

256 levels (`00`–`FF`) from the menu's **Campaign** button, persistent per-level win/loss tracking. Four tiers of 64 map to the high hex digit and `Difficulty`: Recruit `00–3F`, Soldier `40–7F`, Captain `80–BF`, Commander `C0–FF`. Each level: one Human + 1–5 Computer on a procedural map with a deterministic per-level roster. Human's handicap = tier (AIs stay Soldier); level→seed identity (`MasterSeed = level`).

Spans all four layers, one-way:

- **Model (Godot-free, unit-tested):**
  - `CampaignProgress` (`src/FourExHex.Model/CampaignProgress.cs`) — 256 `CampaignLevelStatus` (`Untried`/`Lost`/`Won`, member order load-bearing — persisted numerically). Exposes `StatusOf`, `MarkAttempted` (Untried→Lost, Won terminal), `MarkWon` (terminal), `WonCount`, `TierWonCount`, `NextUp` (lowest non-won, null when all won); statics `DifficultyForLevel` (`(Difficulty)(level / 64)`), `LabelFor`, `SeedForLevel` (reads the baked winnable-seed table `CampaignSeeds.ByLevel`), `HumanSlotForLevel(level, playerCount)` (stable integer hash mod `playerCount`); roster `PlayerCountForLevel` (2–6, weighted high), `ActiveColorSlotsForLevel` (sorted distinct subset), `HumanColorSlotForLevel` (`= active[HumanSlotForLevel(level, count)]`). All draw from one seeded `DeterministicRng` per level (offset decorrelated from seed/terrain), fixing players and terrain forever. `ModeForLevel` derives `GameMode`: `Freeform` below Soldier tier; Soldier+ tiers each hold an exact per-mode quota of Rising Tides / Fog Of War / Viking Raiders levels via a tier-seeded shuffle sliced per mode (see the Campaign paragraph under Rising Tides). **Mark-at-launch:** starting marks Lost; winning flips to Won, which a later loss can't revert.
  - `CampaignSerializer` + `CampaignData` — JSON `{ FormatVersion, Statuses[] }`, registered on `FourExHexJsonContext` for iOS AOT. Tolerant read: short arrays pad with Untried, extras past 256 ignored, out-of-range → Untried, unknown versions throw (store catches → fresh progress).
- **ViewMath (floats OK, unit-tested):** `CampaignGridMath` (`src/FourExHex.ViewMath/CampaignGridMath.cs`) — pointy-top honeycomb geometry: `CellCenter` (odd rows shift half a step, 0.75×height pitch), `BlockSize`, `HitTest` (exact point-in-hexagon). Drives both draw and tap.
- **Scripts (Godot view layer, test-excluded):**
  - `CampaignStore` (`scripts/CampaignStore.cs`) — static persistence to the `user://campaign.json` **sidecar** (independent of game saves). Mirrors `UserSettings`: lazy load, atomic tmp+rename write per status transition, `GD.PushWarning` + fresh fallback on corruption. `PrepareLaunch(level)` sets `GameSettings.CampaignLevel` + `MasterSeed` and marks-attempted. Does **not** write the roster: `Main` builds it via `Player.BuildCampaignRoster(level)` — active color slots, human at `HumanColorSlotForLevel(level)` with tier difficulty, rest Computer/Soldier. Keeping the roster out of `GameSettings.PlayerKinds` avoids clobbering the freeform default.
  - `CampaignPanel` (`scripts/CampaignPanel.cs`) — fixed header (back, `won / 256`, progress bar) over a `ScrollContainer` of four tier sections. Each tier is **one** custom-drawn `TierGrid` (64 hexes in `_Draw` via `CampaignGridMath`, taps in `_GuiInput`); 8↔16 column reflow is a rebuild. Styling: green fill = won, red outline = lost, gray outline = untried.
  - `MainMenuScene` — campaign panel is the third toggled panel, rebuilt on orientation flip. Tapping a hex opens the shared `MapInfoSheet` (via `CampaignConfirmSheet.Create`) whose thumbnail previews the roster and "playing as &lt;Color&gt;" line is tinted via `HumanColorSlotForLevel`. Play calls `CampaignStore.PrepareLaunch`, changes to `main.tscn`. One-shot static `MainMenuScene.OpenCampaignOnArrival` opens straight to the campaign screen on return.

**Win-flow call path.** `Main._Ready` reads `GameSettings.CampaignLevel` into `_campaignLevel`, wires the `HudView` campaign events. On `GameController.GameEnded`, `Main.OnGameEndedRecordCampaignResult` marks Won iff the winner is the human (else launch-time Lost stands) — **before** the controller's trailing `RefreshViews`, so the overlay reads updated totals. `HudView.Refresh` shows the **campaign victory overlay** with **Next unbeaten level** (`Main.LaunchNextUnbeatenCampaignLevel` → `PrepareLaunch(NextUp)`) and **Back to campaign** (`OpenCampaignOnArrival`, then `AbandonAndReturnToMenu`). AI win shows the standard overlay. The campaign overlay is a Main-facing extension of `HudView`, **not** part of the `IHudView` contract.

## Pause / Options menu

A single **Options** button on each scene's HUD (and Escape when no Buy/Build/Move is pending) opens that scene's `EscMenu` with the scene's own option list. Three scenes: gameplay (`Main`), map editor (`MapEditorScene`), tutorial builder (`TutorialBuilderScene`).

### Gameplay pause coordinator (`Main`)

`Main` owns `_isPaused` plus `EnterPause`, `ExitPause`, `ShowPauseMenu`. Entering pause sets `GetTree().Paused = true`, halting every `SceneTreeTimer` (the heartbeat of `GodotAiPacer`) so the AI loop freezes mid-step. Menu:

- **Resume** — `ExitPause`.
- **Save Game** — `OpenSaveDialogFromPause`: opens the autosave path's `AcceptDialog`; on Confirmed/Canceled re-calls `ShowPauseMenu`. Pause stays on.
- **Load Game** — `OpenLoadDialogFromPause`: opens `SlotPickerDialog`. Cancel re-shows the menu; picking a slot sets `LoadRequest.Pending`, `_controller.AbandonGame`s the in-flight step, `ExitPause`s (`GetTree().Paused` persists across scenes), then `ChangeSceneToFile("res://scenes/main.tscn")`.
- **Settings** — opens the shared `SettingsPanel`; on `Closed` re-shows the menu.
- **Exit Game** — `ExitPause` then `AbandonAndReturnToMenu`.

`EscMenu.EscapeClosed` is a sibling event to `Closed`, firing just before `Hide` when Escape closes an open menu. `Main` hooks it to `ExitPause` — the button-click path already manages pause inside each callback, so `EscapeClosed` is the only path needing the unpause hook. `Closed` still fires on every close; nothing else listens for the pause flow.

### Reusable `SettingsPanel`

`SettingsPanel` (CanvasLayer modal — backdrop + centered panel + SFX/VFX `CheckBox` rows + AI Turn Speed and Replay Speed radio rows + Back) is the single Settings UI for menu and in-game pause. SFX/VFX toggles bind to `UserSettings.SfxEnabled` / `VfxEnabled` via `Toggled`. Both speed rows are four `Button`s over the shared `PlaybackSpeed` enum (`Slow`/`Normal`/`Fast`/`Instant`, one `SpeedOrder` + one `SpeedLabel`) in `ToggleMode` sharing a `ButtonGroup` (radio). AI Turn Speed's `Pressed` writes `UserSettings.AiSpeed`; Replay Speed's writes `ReplaySpeed`. `ApplySpeedButtonStyle` paints white/dark-text on the pressed button, dim/light-text on others; `Toggled` fires on both just-pressed and just-unpressed siblings, so one handler keeps them synced. `Open()` re-syncs controls from `UserSettings`. Back/Escape calls `Close`, firing `Closed`.

A **Credits** button above Back opens `CreditsPanel` (`scripts/CreditsPanel.cs`) — sibling CanvasLayer modal at `Layer = 101`, above `SettingsPanel`'s `100`, drawing on top. `SettingsPanel` owns the instance (`_Ready`), reachable from both hosts with no per-scene wiring. Mirrors the modal shell (backdrop + `PanelContainer` + serif title + gold rule + `ScrollContainer` body + Back); vbox uses the same `(420, 570)` min size, scroll area `ExpandFill`s. Body is a BBCode `RichTextLabel` so "FooBarzalot" is a gold `[url]` link; `MetaClicked` → `OS.ShellOpen`. `SettingsPanel.Close` also calls `_creditsPanel.Close()`, and `SettingsPanel._UnhandledInput` early-returns while `_creditsPanel.IsOpen` so Escape closes only Credits.

### Quitting from the main menu (`ConfirmModal`)

The landing page has an **Exit** button (desktop only). Exit and Escape route to `OnExitPressed`, which opens a quit-confirmation modal rather than `GetTree().Quit()` outright; the quit lives in `OnQuitConfirmed`, wired to the modal's `Confirmed`.

`ConfirmModal` (`scripts/ConfirmModal.cs`) — a reusable yes/no dialog in the `ModalChrome` family (dim backdrop + centered slate panel + serif title + gold rule + message + Cancel/confirm). Title, message, confirm-label are constructor args. Cancel/Escape raises `Canceled`; confirm **or Enter** raises `Confirmed`. `MainMenuScene._UnhandledInput` early-returns while `_quitConfirmModal.IsOpen` so the dialog owns its own Escape/Enter.

### ProcessMode rules

Modals must stay interactive while `GetTree().Paused == true`, so each opts out of the freeze: `EscMenu`, `SettingsPanel`, `CreditsPanel`, `SlotPickerDialog` (and its sibling error dialog), `Main`'s `_saveDialog` / `_saveErrorDialog` all set `ProcessMode = ProcessModeEnum.Always`. `Always` is a superset of the unpaused-host scenes' needs (map editor / tutorial builder / main menu), so it works in every host; `WhenPaused` only processes while paused.

Conversely, `SceneTreeTimerFactory.After` passes `processAlways: false` to `SceneTree.CreateTimer` so the timer halts during pause, freezing the AI loop.

### Map editor / Tutorial builder

Map editor's `EscMenu`: **Resume / Save Map / Load Map / Exit** — Save/Load invoke `OpenSaveDialog` / `OpenLoadDialog` in `MapEditorScene`. Tutorial builder's: mode-switch buttons + Save Tutorial / Load Tutorial / Exit; the target mode's button is `Disabled = true`. Neither calls `GetTree().Paused` — no AI loop runs, so cosmetic-only "pause" is fine.

`MapEditorHudView.ShowSceneRootChrome` gates one button: when `true` (default, used by `MapEditorScene` and `TutorialBuilderScene`'s Map Edit mode), the HUD's right strip ends with an **Options** button raising `EscRequested`; the host's `OpenEscMenu` decides contents. Record and Preview submodes hide the `MapEditorHudView` and rely on the nested `HudView`'s own Options button (raises `EscRequested` too, forwarded to the same `OpenEscMenu`).

### Debug cheat menu (`CheatMenu`)

`scripts/CheatMenu.cs` is a Debug-only modal summonable over any screen: backquote on desktop, 3-finger tap on touch (via `MultiTouchTapDetector` in ViewMath). The whole file is `#if DEBUG`; every scene root (`MainMenuScene`, `Main`, `MapEditorScene`, `TutorialBuilderScene`, `PlayTutorialScene`) calls `CheatMenu.Attach(this)` from `_Ready` inside its own `#if DEBUG` block — **no autoload registration**, so Release has no listener, menu, or call sites. `Attach` also runtime-guards on `OS.IsDebugBuild()`.

A thin input listener (`_Input`, not `_UnhandledInput`, so the summon gesture wins over focused Controls) owning a private `EscMenu`. Entries: **Tutorial Builder** (`ChangeSceneToFile`, no in-progress guard), **Toggle Recording Mode On/Off** (label names the action for the current state), **Determinism Check** (runs `DeterminismProbe.Run` inline with the Headless views and shows the digest triple in a `ModalChrome` overlay, plus one grep-able `[determinism-probe]` `GD.Print` line — the on-device equivalent of the `FOUREXHEX_6AI_QUICK` digest on targets where env vars can't be set), **Close**. Adding a cheat = adding an `EscMenu.Option` in `Toggle`. Instrumented under `Log.LogCategory.Cheat`.

**Recording mode** (`scripts/RecordingMode.cs`): a session-scoped static flag (`Active` + `Changed` event, never persisted) that hides promo-noisy chrome for clean promotional captures while the game stays fully playable. Only the cheat menu flips it, so Release builds carry the flag permanently false. Subscribers react immediately on `Changed` and re-assert per-`Refresh`: `HudView` hides the action-hint/tutorial panel, gold chip, turn + swatch chip, map/level label, bankruptcy toast, and transient banner (show-sites `ShowTransientBanner` / `SummonCapitalAlertNotice` early-return while active; `ShowTutorialMessage` / `ShowTappableTutorialMessage` keep all state — `_externalMessageActive`, text, tap catcher — but leave the panel invisible, so toggling off restores an active message); `HexMapView.RedrawWarningBadges` leaves the capital warning badges cleared. Buttons and endgame overlays are untouched. View-layer only — no controller/model involvement.

## Map editor

`MapEditorScene` (root of `res://scenes/map_editor.tscn`, from the menu's "Map Editor" button) paints a starting map by hand and saves to `user://maps/`. No `GameController`, but reuses the view layer (`HexMapView` + `MapEditorHudView`) so edits match in-game terrain.

- **Up-front roster + bake-on-save.** Entered via `MapEditorRequest.Pending`: **New Map** carries per-color kinds + difficulties; **Load Map** carries a slot name (roster derived from the file). `_Ready` resolves it into `_rosterKinds` / `_rosterDifficulties`. The preview roster (`_panel.Players`) is the active (non-`None`) colors, all Human. `MapEditorHudView.ApplyRosterKinds` hides `None` swatches and draws a white pip on Human ones (`HexPaletteButton.IsHuman`). **Save** runs `MapRosterRules.ValidateForSave` (block + inline error on mismatch), then serializes a 6-slot roster with the chosen kinds/difficulties.
- **Scene/panel split.** `MapEditorScene` is a thin chrome host: owns `MapEditorHudView`, `SaveStore`, Save/Load dialogs, the `EscMenu` modal, the Escape→hand→modal ladder, `ReturnToMainMenu`. The body is `MapEditorPanel : Node2D` — a reusable Node owning the `HexMapView` instance, draft grid/water/territory state, paint-stroke state machine, undo stack, hover tooltip. The scene wires HUD events (`PaletteSelectionChanged`, `GenerateRequested`, `UndoLast/All`, `RedoLast/All`, `EscRequested`) to panel methods (`SetSelectedPalette`, `GenerateMap`, `UndoLast/All`) and to `OpenEscMenu` (Resume / Save / Load / Exit → `OpenSaveDialog` / `OpenLoadDialog`), and listens to `panel.UndoStateChanged`. The split lets `tutorial_builder.tscn` host the same panel under different chrome. The panel exposes `PaintingEnabled` (gates all paint events; off in Build/Preview hosts), `SnapshotDraft` / `RestoreDraft` (Preview cloning), `BuildLiveState` / `BuildSaveState` (host serializes without poking internals).
- **HUD configurability.** `MapEditorHudView` exposes one knob hosts set before `AddChild`:
  - `ShowSceneRootChrome` (default `true`) — whether the HUD's right strip ends with an **Options** button raising `EscRequested`. Both scenes set `true`; each scene's `OpenEscMenu` decides the modal contents.
- **Draft state.** Panel owns a mutable `HexGrid`, water set, territory list, plus `UndoStack<EditorSnapshot>`. `EditorSnapshot.Capture` deep-copies all three; `ApplyTo` rebuilds the grid from scratch (paints add and remove tiles, so `GameStateSnapshot`'s in-place updates aren't enough).
- **Push cycle.** Every paint/generate calls `PushState`: rebuilds a fresh `GameState`, hands it to `HexMapView.ReloadState` (preserving zoom/pan), reapplies occupant visuals, fires `UndoStateChanged`. Hence `HexMapView` exposes both `Init` and `ReloadState`.
- **Input model.** Each palette swatch flips `HexMapView.DragMode` to one of two channels:
  - **Pan mode** (hand, capital): drag pans; release without drag fires `CoordClicked`. Hand ignores the click; capital handles it via `MapEditPaint.PaintCapital`.
  - **Paint mode** (colors, water, tree, tower): drag paints a stroke. View fires `PaintCellEntered` on press and per new cell crossed while held, `PaintStrokeEnded` on release. A sub-threshold press-release still produces a one-cell stroke.

  A stroke wraps in one undo entry: first `PaintCellEntered` captures `EditorSnapshot.Capture`, per-cell paints reuse it, `PaintStrokeEnded` pushes once iff any cell mutated.
- **Hand swatch.** Palette index 0, default selection. Pan-mode, no paint. Escape ladder: first press with a non-hand swatch active reselects hand; second press with hand active opens `EscMenu`.
- **Toggle stroke locking.** Tree and tower drag-paints lock an "Add"/"Erase" mode at the first cell. First cell already carries the occupant → Erase (later cells only matching removals); else → Add (later cells skip cells that already have it). Keeps long strokes consistent over varied terrain.
- **Hover tooltip.** `HexMapView.CoordHovered` fires on motion with the hex under the cursor (null off the `Cols × Rows` rect or over the HUD). Wired to `HexHoverTooltip`, a floating `CanvasLayer + Label` appearing after ~500ms dwell, hiding on motion. Label shows the row-major lex index (`row * Cols + col`) plus `(col, row)` — the lex index is the single-int handle for tutorial scripting. `MapEditorPanel` always subscribes, but `OnCoordHovered` feeds `null` when `PaintingEnabled` is false or `DisplayServer.IsTouchscreenAvailable()` is true. So it shows in the standalone editor and tutorial-builder Map Edit mode on a pointer device, not in Record/Preview/Play Tutorial or on touchscreen.
- **Palette.** `MapEditorHudView` builds `HexPaletteButton` swatches: one per player color, a **neutral (unowned land)** swatch, plus water, tree, capital, tower toggles. The selected index is read by `OnCoordClicked` and dispatched to one of `MapEditPaint`'s pure functions (`PaintLand`, `PaintNeutral`, `PaintCapital`, `PaintTowerToggle`, `PaintTreeToggle`, `PaintWater`). Each mutates the grid in place, then re-runs `TerritoryFinder` + `CapitalReconciler` (except `PaintCapital`, which honors the user's exact pick).
- **Neutral hexes.** A neutral hex is land owned by `PlayerId.None` — capturable by any adjacent player like enemy territory (`tile.Owner != attackerTerritory.Owner` is the predicate); once captured it's ordinary owned land. Editor-only — `MapGenerator` never produces a `None`-owned tile. `PaintNeutral` sets owner to `None` and clears only player-bound occupants (`Capital`, `Unit`); owner-agnostic ones (`Tower`, `Tree`, `Grave`) survive. Neutral tiles flood-fill into a `None`-owned `Territory` that generates no income (`Treasury.CollectIncomeFor` skips non-owned capital-less territories) and never gets a capital — `CapitalReconciler.Reconcile` short-circuits a `None`-owned territory to capital-less and throws if a `Capital` is found there. Neutral capture logs under `Log.LogCategory.Capture` (`[capture] neutral hex {coord} -> {player}` from `GameOperations.HandleCapture`). Save/load round-trips neutral tiles (`None` encodes as wire index `-1`).
- **Responsive land swatches.** The land-color swatches plus the neutral swatch (the "owner" group) collapse to a single cycling `HexPaletteButton` when the viewport is narrow. The full `_landRow` and the lone `_landCycleButton` live side-by-side; `OnViewportMetricsChanged` (from `OrientationHud`) toggles visibility by width threshold (`FullLandRowWidth{Portrait,Landscape}`). The collapsed button is select-first-then-cycle: when land isn't active a press selects it at `_lastLandPaletteIndex`; once active each press advances through the player colors then the neutral slot, wrapping. Its `FillColor` (neutral shows `PlayerPalette.Neutral` gray) and selection outline track state via `RefreshLandCycleVisual`. Only the owner group collapses.
- **Save format.** Editor maps written with `SaveSerializer.SerializeMap` (no per-player `Kind`, `TurnNumber == 0`). At play time `Main` detects `TurnNumber == 0` to branch into the "starting map" flow: fresh players from `GameSettings`, fresh `TurnState`, empty `Treasury`, but saved grid + territories + pre-placed trees/towers/capitals stick.

## Tutorial builder

`TutorialBuilderScene` (root of `res://scenes/tutorial_builder.tscn`, from the menu's debug-only "Tutorial Builder" button — gated on `OS.IsDebugBuild()`) is a 3-mode authoring tool. Tutorials are v4 save files in `user://tutorials/` carrying both a `Tutorial { Title }` block and a `Replay { InitialState, Beats }` block — the same Replay format every in-progress save ships. The scene reuses the Map Editor body: a single `MapEditorPanel` built in `_Ready`, never torn down. Mode switching only flips `panel.PaintingEnabled` and per-mode chrome `Visible`, so the painted draft survives transitions.

### Playing a tutorial (end-user `play_tutorial.tscn`)

`PlayTutorialScene` (root of `res://scenes/play_tutorial.tscn`, from the menu's always-visible "Play Tutorial" button) plays a tutorial without the authoring tool. Chrome-free host: `_Ready` builds a `MapEditorPanel` (roster set to `Player.BuildAllHumanRoster()` BEFORE `AddChild`, as the panel asserts) + a `PreviewPane` + a shared `EscMenu`, loads via `SaveStore.LoadBundledTutorial("full_tutorial")`, then `panel.LoadFromMap` → `panel.ResetToTutorialStart(InitialSnapshot)` → `preview.Start(tutorial)` — the same sequence as `TutorialBuilderScene.OnLoadSlotPressed`, ending in `Start` instead of `SetMode(Record)`. ESC raises `PreviewPane.EscRequested` → minimal `EscMenu` (Resume / Main Menu). The victory overlay's Replay / Play Again / Main Menu buttons are handled inside `PreviewPane`. The tutorial ships at `tutorials/full_tutorial.json` (= `res://tutorials/`, same `SaveStore.BundledMapsDirectory` as bundled maps). Since `.json` isn't a Godot resource, `export_presets.cfg` carries `include_filter="tutorials/*.json"` on every preset.

### Modes

`TutorialMode { MapEdit, Record, Preview }`. Mode switching, Save/Load Tutorial, and Exit all flow through the shared `EscMenu` modal — no top strip, no 1/2/3 hotkeys. The modal's button for the current mode is `Disabled = true`.

- **Map Edit** — `panel.PaintingEnabled = true`; chrome-trimmed `MapEditorHudView` (palette + seed + Generate + undo bar) at y=0..60.
- **Record** — `panel.PaintingEnabled = false`; `RecordPane` builds a transient `GameController` over the draft with all six players forced `PlayerKind.Human`. Its `HudView` occupies y=0..60. Dev plays hot-seat; the recording pipeline (`_replayBeats` via `TrackHandler` / `AiTurnDriver.ApplyAiActionCore`) captures game-action beats automatically — including rejected unit-move attempts, auto-captured as `ReplayRejectedMoveBeat`s (recording mode only). Three HUD authoring buttons (hidden outside Record; revealed via `SetAdd*ButtonVisible`) author the other tutorial-only beats inline: **`+ Text`** (narration dialog → `ReplayDisplayTextBeat`), **`◎`** (current selection → `ReplaySelectTerritoryBeat` anchored at its capital), **`»`** (`ReplayDemoStartBeat` fast-forward marker).
- **Preview** — `panel.PaintingEnabled = false`; `PreviewPane` builds a transient `GameController` where player 0 is Human (dev plays Red) and 1-5 are AI driven by a `ReplayDrivenAi` chooser replaying the recorded non-player-0 beats.

ESC opens the shared `EscMenu` in every submode. In Map Edit ESC first drops a non-hand palette to hand; second press with hand opens the modal. `RecordPane` / `PreviewPane` forward their inner `HudView`'s `EscRequested` up to the scene.

**Draft preservation across mode switches.** The panel's `_grid` is shared with the play state Record / Preview build atop, so recruits/towers placed during recording mutate the same tile occupants the panel later reads. `TutorialBuilderScene` captures an `EditorSnapshot` on every exit from Map Edit and restores it (`MapEditorPanel.RestoreDraft`) on every return. Switching to Map Edit while a non-empty recording exists pops a "Discard recording?" confirm; on confirm the scene calls `RecordPane.DiscardRecording` (→ `RecordingCapture.Reset`) first.

**Tutorial-load realignment.** A saved tutorial's `LoadedSave.State.Grid` reflects whatever frame the dev was on at save (post-replay if saved mid-Record/Preview). `OnLoadSlotPressed` calls `MapEditorPanel.ResetToTutorialStart(Replay.InitialSnapshot)` right after `LoadFromMap` so `_grid` matches the recording's initial frame. The subsequent MapEdit→Record `SnapshotDraft` captures the painted starting map, which a later Discard restores.

### Record-mode flow

`SetMode(Record)` dispatches to one of two `RecordPane` entry points:

- **Fresh entry** (`StartRecording`) — when the previous mode was Map Edit (or recording was empty). Builds a controller from `panel.BuildLiveStateWith(roster)`, calls `StartGame` to capture `_initialSnapshot` post-`SeedStartingGold`, starts from beat 0.
- **Resume from Preview** (`ContinueRecording(previous)`) — on `Preview → Record` when a recording exists. Builds a controller with `loadedReplay: previous.Replay` (seeds `_initialSnapshot` and `_replayBeats` from the existing Tutorial) and calls `BeginReplay`. Under `SynchronousAiPacer`'s trampoline the replay drains inline, leaving state at the recorded end with `_replayMode = false` and beats intact. Subsequent inputs append new beats.

Both paths share the rest:

1. All-Human roster from the panel's colors/names.
2. `state = panel.BuildLiveStateWith(roster)`.
3. Real `HudView` + `GameController` with `aiChooser: null`, `aiPacer: new SynchronousAiPacer()` (no AI runs; unused outside the resume replay), `recordingMode: true`. The latter gates `HandleCapture`'s `PendingDefeatScreen` to player 0 only (else every defeat in the all-Human roster pops the overlay); also suppresses the End-Turn claim-victory prompt and hides the full-win overlay.
4. `panel.Map.DragMode = HexDragMode.Pan` so tile clicks fire.
5. Dev plays normally; every action goes through `TrackHandler` / `AiTurnDriver.ApplyAiActionCore` recording `ReplayBeat`s into `_replayBeats`.

`RecordPane.HasRecording` returns true iff a non-empty tutorial was captured — gates the discard-confirm and the `StartRecording` / `ContinueRecording` pick.

`RecordPane.PrimeForContinue(Tutorial)` pre-populates the capture from a loaded Tutorial without starting a session. Used by `OnLoadSlotPressed`: after Load Tutorial the scene calls `PrimeForContinue` (if the file has beats) then `SetMode(TutorialMode.Record)`. `ApplyModeSwitch`'s Record branch inspects `CurrentTutorial`; non-empty triggers `ContinueRecording`, else `StartRecording`.

**Authoring tutorial-only beats.** While recording, three HUD buttons author them via `controller.RecordTutorialOnlyBeat(...)` (stamps `Index` + `Turn`, forces `Actor = -1`): `+ Text` opens a modal (`LineEdit` + Insert / Cancel) → `ReplayDisplayTextBeat`; `◎` captures the currently selected territory as a `ReplaySelectTerritoryBeat` (no-op with a log when nothing is selected); `»` drops a `ReplayDemoStartBeat` marker (playback honors the first one). Rejected unit-move attempts need no button — the move-rejection branch in `OnTileClickedBody` auto-records a `ReplayRejectedMoveBeat` when `_recordingMode` is set. Beats append at the current end — no in-line insertion; to add narration before turn N, author it before pressing End Turn into N+1. Buttons + dialog torn down in `StopRecording`.

`RecordPane.StopRecording` (on `SetMode(out of Record)`):

- Snapshots the captured tutorial into a `RecordingCapture` BEFORE nulling the controller, so `Save Tutorial` / `Preview` read it post-switch. Tuple: `(InitialSnapshot, InitialTurn, InitialPlayer, Beats[])`.
- `controller.AbandonGame()` unsubscribes from `panel.Map`'s `TileClicked` and every `_hud` event — else the abandoned record controller routes shared `panel.Map` clicks into itself during Preview, hitting `ObjectDisposedException` on the freed record `HudView`.
- Drag mode restored; panel re-`Init`s its draft view.

### Preview-mode flow

`PreviewPane.Start(tutorial)` (on `SetMode(Preview)`):

1. Roster: player 0 Human (dev), players 1-5 Heuristic (any AI kind — the chooser is overridden).
2. `state = panel.BuildLiveStateWith(roster)`.
3. `PreviewSetup.Apply(panel.Map, state, tutorial)` — pure-C# helper that:
   - Applies `tutorial.Replay.InitialSnapshot` back to grid + treasury.
   - `state.Turns.Reset(initialPlayer, initialTurn)`.
   - `map.RebuildAfterTerritoryChange()` — refreshes border/capital/tree/grave layers.
   - Clears highlight + every overlay (`ShowMoveTargets`, `ShowTowerTargets`, …) so leftovers don't bleed in.
4. A single shared `ScriptCursor` is passed to BOTH `ReplayDrivenAi` (AI side) and `TutorialPreview` (human side). Beats consumed by either advance the other.
5. `GameController` built with:
   - `aiChooser: replayAi.ChooseNextAction`
   - `humanActionValidator: tutorialPreview.TryAccept`
   - `previewMode: true` (suppresses every `RecordBeat`; skips the End-Turn claim-victory prompt; hides the full-win overlay; does NOT block input handlers — Preview wants player-0 clicks through)
   - `aiPacer: new GodotAiPacer(new SceneTreeTimerFactory(GetTree()))`
   - `onAfterRefresh: () => { narration.Tick(); cues.Apply(); }` (driver runs first to set its `IsPresenting` flag before cues check it)
6. `TutorialPreviewCues` + `TutorialNarrationDriver` built and wired via `onAfterRefresh`. Forward-reference dance: the controller takes the callback at construction, but cues + driver need a controller reference (`SelectTerritoryForTutorial` / `CancelActionForTutorial` / `RefreshViewsForTutorial`); PreviewPane captures `cuesRef` / `narrationRef` in the closure and assigns post-construction. The driver shares the same `ScriptCursor` as AI / human sides.
7. `hud.SetUndoRedoLocked(true)` — undo/redo aren't recorded as beats and would desync the cursor, so the four buttons stay disabled all session.
8. Drag mode = Pan; `controller.StartGame()`.

While the dev plays:

- Player-0 clicks hit `ExecuteMove` / `ExecuteBuyAndPlace` / `ExecuteBuildTower` / End-Turn / etc. Each builds the prospective `ReplayBeat` and calls `humanActionValidator` BEFORE mutating. On mismatch the action aborts, `TutorialPreview.PlayerActionRejected` fires, `PreviewPane` surfaces the reason via `hud.ShowTutorialMessage(...)`.
- AI turns: the `StepAi*` loop asks `ReplayDrivenAi.ChooseNextAction`, yielding the next beat for the current actor or null (player done); the shared cursor advances.

When the final player-0 beat is consumed, `TutorialPreview` fires `TutorialFinished` and the HUD shows "Tutorial complete."

### Preview cues

`TutorialPreviewCues` is a pure-C# helper painting visual hints for the one-and-only-legal-move on player 0's turn. Wired via `onAfterRefresh` so `Apply()` runs at the tail of every `RefreshViews()` and every human `TrackHandler` (handler bodies sometimes paint `ShowMoveTargets` after their mid-body refresh; the tail invocation ensures the cue paints last).

`Apply()` first checks `narration.IsPresenting`: while a tutorial-only beat shows, cues early-return. Otherwise it reads `TutorialPreview.NextPlayer0Beat` (returns `null` while a `TutorialOnlyBeat` sits between cursor and next player-0 beat) and dispatches:

- **`ReplayEndTurnBeat`** → `SetCta(EndTurn, true, pulse: true)`.
- **`ReplayBuyBeat`** → auto-select capital's territory (`SelectTerritoryForTutorial`). Buy CTA on iff not yet in the matching Buying mode (`BuyModeLevel(Mode) != bu.Level`): cycling presses pulse the button; once matched, CTA drops and `ShowMoveTargets([To], level)` highlights the target.
- **`ReplayBuildTowerBeat`** → analogous; CTA pulses on Build Tower while `Mode != BuildingTower`, then drops for `ShowTowerTargets([To])`.
- **`ReplayMoveBeat`** → auto-select source territory; if `Mode == MovingUnit && MoveSource == mv.From`, overwrite `ShowMoveTargets([To], level)`; else overwrite with `[From]` (ring on source).
- **`ReplayLongPressRallyBeat`** → auto-select containing territory; `ShowMoveTargets([Target], Recruit)`.
- **`ReplayClaimVictoryBeat` / `ReplayDismissClaimBeat` / `ReplayDismissDefeatBeat`** → CTA on the matching overlay button.

Before dispatching, `Apply` checks mode compatibility; if the player is in a mode the beat can't execute from, it calls `GameController.CancelActionForTutorial()` to clear `Mode` / `MoveSource` and overlays. A `_applying` re-entrancy guard short-circuits the recursive `Apply` from `CancelActionForTutorial`'s `RefreshViews`.

Both `SelectTerritoryForTutorial` and `CancelActionForTutorial` bypass `TrackHandler` — Tutorial Preview isn't undoable.

After per-beat side effects, the tail of `ApplyCore` pushes the step prompt:

```csharp
_hud.ShowTutorialMessage(TutorialInstructionText.For(next, _state, _session));
```

`TutorialInstructionText.For(ReplayBeat, GameState, SessionState)` is a pure helper switching on the next beat kind and current `SessionState.Mode` / destination occupant for sub-step-aware strings:

- **Buy beat** — escalates: Mode=None → "Press the Buy Recruit button."; Mode=BuyingX below target → upgrade prompt; matching mode → a destination-aware placement prompt ("Place the {level} at the highlighted hex …" — the `tutorial.place.*` key picked by To-tile occupant and same/enemy color: combine / tree-clear / grave-remove / capture variants).
- **Move beat** — pickup vs placement; placement varies by destination occupant (friendly combine, same-color tree/grave clearance, enemy capture incl. capture-with-clear / capture-with-destroy).
- **BuildTower / EndTurn / Rally / Claim / Dismiss** — fixed text per kind.

When `Apply` returns early (opponent turn), cues call `HideTutorialMessage`; once the script ends (`NextPlayer0Beat == null`) the panel is left alone so "Tutorial complete." survives.

### Tutorial-only beats

A second `ReplayBeat` sub-hierarchy under `TutorialOnlyBeat` carries beats NOT captured from normal gameplay — authored during Record mode, presentation-only (no state mutation, no player ownership). Four kinds:

- **`ReplayDisplayTextBeat { Text }`** — narration, presented by `TutorialNarrationDriver` during interactive Preview; skipped by hands-free replay.
- **`ReplaySelectTerritoryBeat { Anchor }`** — hands-free replay resolves the territory containing `Anchor` (any tile of it — territory objects aren't snapshot-stable), sets `session.SelectedTerritory`, and shows the live selection highlight, like a player's click. Paced on the EndTurn delay. Authored via the `◎` button.
- **`ReplayRejectedMoveBeat { From, To }`** — a rules-rejected unit-move attempt; hands-free replay shows the pickup pulse on `From` then the live rejection flash at `To` (shape from the unit's level, defenders re-derived via `DefenseRules.BlockingDefenders`), mutating nothing. Auto-recorded by the move-rejection branch when `_recordingMode` — the only tutorial-only beat not button-authored.
- **`ReplayDemoStartBeat`** — fast-forward marker: paced `BeginReplay` executes everything up to and including the first marker instantly and silently, then paces from there; every loop restart re-runs the skip. Authored via the `»` button.

The dispatcher accepts future kinds (camera pan/zoom, HUD callout) without rework — unhandled tutorial-only beats fall through the skip arms.

**Identity.** `TutorialOnlyBeat` carries `Actor = -1` (sentinel — no owner). The abstract `TutorialOnlyBeat` record is the discriminator dispatch and the cursor skip-scan key off.

**Cursor semantics.** The shared `ScriptCursor` is the single source of truth. Three consumers:

- **`TutorialPreview.NextPlayer0Beat`** skip-scans for the next player-0 beat AND gates on tutorial-only beats: if any `TutorialOnlyBeat` sits between cursor and next player-0 beat, returns `null`.
- **`TutorialPreview.TryAccept`** unaffected — by the time the player can click, the driver has advanced past pending tutorial-only beats during the prior `onAfterRefresh` tick.
- **`ReplayDrivenAi.ChooseNextAction`** returns null (does NOT advance) when the cursor points at a `TutorialOnlyBeat`. Only the driver advances past these.

**`TutorialNarrationDriver`.** Pure-C# helper wired into `onAfterRefresh` ahead of `TutorialPreviewCues.Apply()`. Per tick:

- `IsPresenting` true → no-op (re-entrancy guard).
- Cursor at end-of-script → no-op.
- Beat at cursor is `ReplayDisplayTextBeat dt`: call `hud.ShowTappableTutorialMessage(dt.Text)`, set `IsPresenting = true`, arm a one-shot `hud.TutorialMessageTapped` subscription. On tap: detach the handler, advance the cursor, clear `IsPresenting`, call `HideTutorialMessage`, fire `controller.RefreshViewsForTutorial`.
- Unknown future `TutorialOnlyBeat`s fall through a `default:` arm silently advancing the cursor.

**Cues gating.** `TutorialPreviewCues.ApplyCore` early-returns if `narration.IsPresenting`. Ordering in `onAfterRefresh` matters: driver ticks first to set the flag, cues then check it.

**Tap-anywhere dismissal.** `HudView`'s `ShowTappableTutorialMessage` builds a single full-viewport invisible `Control` (lazy, retained), moves it topmost via `MoveChild`, sets `MouseFilter = Stop`. Clicks anywhere route to `TutorialMessageTapped`, so the player can't hit Buy Recruit / End Turn while a narration beat is gated. `HideTutorialMessage` hides the catcher and sets `MouseFilter = Ignore`.

**In-game Replay.** The victory overlay's "Replay" button runs `GameController.BeginReplay` → `StepReplayExecute`, whose switch honors the select / rejected-move / demo-start kinds (see the paced-replay flow) and silently skips display-text. Normal recordings never contain tutorial-only beats, so live-game replays are unaffected.

**Recording.** `GameController.RecordTutorialOnlyBeat(TutorialOnlyBeat)` is the public entry point. Stamps `Index` + `Turn` like the private `RecordBeat`, forces `Actor = -1`. Gated on `!_replayMode && !_previewMode` so playback / Preview can't inject authored beats.

**Serialization.** Round-trips through the same v4 `BeatDto` pipeline: discriminators `"DisplayText"` (`Text` field), `"SelectTerritory"` (anchor in `ToQ`/`ToR`), `"RejectedMove"` (`FromQ`/`FromR`/`ToQ`/`ToR`), `"DemoStart"` (no payload). Actor stored literally (-1) — no color-by-index lookup.

### Gating lives in GameController

Preview gating lives in `GameController` via the single `humanActionValidator` hook and reuses `_replayBeats` for the script — one source of truth for recording and validation. No parallel view-wrapping / state-machine layer mirrors the controller's click/buy/end-turn logic.

### Tutorial file format

Same v4 schema as in-progress saves. A tutorial file is a v4 save with BOTH a `Tutorial { Title }` block AND a `Replay { ... }` block. Deserialize throws if the Tutorial block is present without a Replay block. The `Tutorial` class is `{ Title, Replay }` — no `StartTurn` / `StartPlayer` / `Beats` (the Replay carries those).

## Instructions viewer

The Help menu's Instructions option opens `InstructionsPanel` (`scripts/InstructionsPanel.cs`) — a paged, near-fullscreen rules explainer where every page pairs explanatory text with a looping mini-board animation demonstrating the rule.

**`InstructionsPanel`** — `CanvasLayer` (Layer 100, `ProcessMode = Always`), owned by `HudView` (`OpenInstructions` / `ExitInstructions`, freed on `Closed`). `ModalChrome.BuildBackdrop` + an edge-anchored slate panel (~12 px margins). The static `Pages` table maps each page to string-store title/body keys plus the bundled tutorial name driving its demo (null = text-only page). The two sub-panels live in one `BoxContainer` whose `Vertical` flag follows `ScreenLayout.Resolve` on viewport resize: portrait stacks the demo above the text, landscape puts the demo left. Back/Next wrap; Close/Escape exit; Left/Right page; an `_Input` override swallows every key while up (same contract as `HudTour`). Page changes `Stop()` the current demo and `Play()` the next via `SaveStore.LoadBundledTutorial`; a missing/corrupt bundle logs a warning and shows the page text-only. Logs `[instr] open / page -> n (key) / close` (Hud).

**`InstructionDemoView`** (`scripts/InstructionDemoView.cs`) — the graphics sub-panel: a complete second (model, controller, view) stack playing a recorded tutorial hands-free, on a loop.

- **Second stack.** `Play(LoadedSave)` builds its own `GameState` (the bundle's map + roster) + fresh `SessionState`, a new `HexMapView`, a `HeadlessHudView` (that's the "no HUD"), and a new `GameController` constructed with `loadedReplay: tutorial.Replay`, `previewMode: true` (no divergence checksum, no beat recording), its own `GodotAiPacer`, `replayIsInstantMode: () => false` (pinned paced — the demo always animates regardless of the user's Replay Speed), and `autoSelectFirstTerritory: false`. No input events are wired and no `StartGame` is called — the controller is purely a replay-playback engine driven by `BeginReplay()`. Same multi-instance pattern as `PreviewPane`; nothing is shared with the live game behind the modal.
- **Display.** The map renders inside a `SubViewport` (`RenderTargetUpdateMode.Always`, `GuiDisableInput`, `HandleInputLocally = false`, transparent) shown live through a `TextureRect` bound to its `ViewportTexture` — the `MapThumbnailView` hosting pattern minus the snapshot. The viewport is sized by `ThumbnailLayout.OrientedFit` (grid box, aspect swapped tall in portrait so the `HexMapView` inside rotates −90° exactly like the in-game map; shared with the thumbnail) and re-shaped on window resize.
- **Audio.** `HexMapView.SetMutePinned(true)` — a hard audio-off flag gating the view's `AudioBus` dispatch points (`PlaySound`, `FlashRejection`), independent of the controller-driven `_silentMode`, which paced replay actively lifts. Animations/VFX stay on.
- **Loop.** `GameController.ReplayEnded` → a ~1.5 s still-frame `SceneTreeTimer` → `BeginReplay()` again (a generation counter drops stale timers after `Stop`). A `ReplayDemoStartBeat` in the recording makes every iteration fast-forward the author's staging. Logs `[instr] demo start / loop restart / stop` (Tutorial).

**Content pipeline.** Each page's demo is authored in the tutorial builder (small painted map + recorded beats, incl. the tutorial-only kinds above), saved to `user://tutorials/`, and bundled by copying the JSON into `res://tutorials/` as `instr_<page>.json` (`instr_territories`, `instr_recruit`, `instr_defense`, `instr_towers`, `instr_commander`, `instr_income`, `instr_trees`, `instr_winning`). Page copy lives in the string store (`hud.instructions.page.*`). The `export_presets.cfg` `tutorials/*.json` include filter ships them.

## Renderer

Pinned to **GL Compatibility** (`project.godot`: `config/features` has `"GL Compatibility"`, `rendering/renderer/rendering_method="gl_compatibility"`). 2D-only — `Polygon2D` fills + batched immediate-mode primitives, no shaders/3D. Portable; required for web export.

2D MSAA on at 2× (`rendering/anti_aliasing/quality/msaa_2d=1`) smooths the batched non-AA lines; per-primitive AA off (defeats batching). One renderer everywhere, no per-platform override. Web export blocked: Godot 4.6.1 .NET (mono) ships no Web templates.

### Draw-call batching (Android performance)

In GL Compatibility every `CanvasItem` issues its own draw call per frame; neither `Polygon2D` nor antialiased lines batch. Naïve one-node-per-shape hit **~6,500 draws/frame** (cost is draw-call count, not C# churn — rebuild ~1 ms). Two pieces in `HexMapView` collapse this to **~180–256 draws/frame**:

- **`PolylineBatch`** (one per layer, territory borders + per-tile outlines): all edge segments in one `DrawMultiline` (borders, uniform color) or `DrawMultilineColors` (outlines, player-dark per tile). Non-antialiased so batches to ~1 call; 2D MSAA smooths it. `DrawTerritoryBorders` / `PopulateOutlinesLayer` build segment arrays + `QueueRedraw()` on territory change.
- **`TriangleSoup` + `TriangleSoupBuilder`**: water cells, rim water, shoreline foam are **static**, baked once into one vertex-colored indexed triangle array (`TriangleSoupBuilder` triangulates via `Geometry2D.TriangulatePolygon`, preserving `Color × VertexColors`), drawn in one `RenderingServer.CanvasItemAddTriangleArray` call.

Tile fills stay one `Polygon2D` each (recolored, not recreated, on capture). Remaining per-capture cost: `RefreshOccupantVisuals` recreating occupant nodes each refresh. Diagnostics behind the `[hitch]` prefix (`Log.Since`, `LogLongFrame` CPU/draw split in `_Process`, one-shot `DumpSceneComposition`), all `[Conditional("DEBUG")]`.

## Visual / UI theme

Owned by three view-layer pieces (Model/Controller stay color-free):

- **`theme/fourexhex_theme.tres`** — project-default `Theme`, set as `gui/theme/custom`. Defines slate `Panel`/`PanelContainer`/`PopupPanel`/`PopupMenu` styleboxes, `Button`/`OptionButton` normal/hover/pressed/disabled/focus, `LineEdit` normal+focus, `CheckBox`+`Label` font colors, `TooltipLabel` font (Geist) + size (28). `Window`/`AcceptDialog` have no entries — Godot 4 ignores `embedded_border` there, so modals use the `CanvasLayer` + `PanelContainer` shell.
- **`scripts/UiPalette.cs`** — static class exposing design tokens as `oklch`-style constants for direct-paint view code. Groups: surfaces (`BgDeep`, `BgPanel`, `BgElev`, `BgRow`, `BgRowH`, `HudBar`), lines (`Line`, `LineSoft`, `LineHard`), ink (`Ink`, `InkSoft`, `InkMute`, `InkFaint`), brass (`Gold`, `GoldDeep`, `GoldDim`), water (`Water`, `WaterDeep`), plus `ModalBackdrop` dim-scrim.
- **`fonts/`** — three OFL `FontFile` resources, loaded via `GD.Load<FontFile>`, applied via `AddThemeFontOverride`: DM Serif Display (titles), Geist (UI body), JetBrains Mono (numerics).

**Player palette** — `scripts/PlayerPalette.cs`, separate because roster-dependent: `ColorFor(PlayerId)` reads `GameSettings.PlayerConfig` for fill; `DarkColorFor(PlayerId)` returns a per-slot darker companion (~ fill × 0.45) for the 1.5-px per-tile hex border stroke in `HexMapView.PopulateOutlinesLayer`, keeping per-tile borders visible within a single-owner territory.

**Board palette** — `scripts/BoardPalette.cs`, a third fixed-color class distinct from `UiPalette`/`PlayerPalette`. Holds board colors: `RejectRed` (illegal-action ghost), `ForestCanopy`/`ForestTrunk` (conifer, shared by HexMapView tree + `HudIcons.DrawTree`), `CastleFill`, `GraveCross`, economy hues `WarnRed`/`WarnYellow`. Single source keeps on-tile rendering and HUD swatches in sync.

### Modal-dialog shell pattern

Every modal (Settings, EscMenu/pause, SlotPickerDialog) uses the same three-piece shell:

1. **`CanvasLayer`** — `Layer = 100`, `Visible = false`, `ProcessMode = Always` so it stays interactive whether or not the tree is paused.
2. **`ColorRect`** backdrop sized to viewport, painted `UiPalette.ModalBackdrop`, `MouseFilter = Stop`. (`SlotPickerDialog` wires backdrop `GuiInput` to close on click; `SettingsPanel`/`CreditsPanel`/`EscMenu` don't.)
3. **`PanelContainer`** centered via `AnchorLeft/Right/Top/Bottom = 0.5` + `GrowDirection.Both`, picking up the theme's `Panel/styles/panel` stylebox. Content in a `VBoxContainer` child.

Shared builders in **`scripts/ModalChrome.cs`** (static): `BuildBackdrop(viewport)`, `BuildCenteredPanel(panelW, panelH)` (fixed pixel — slot picker), parameterless overload `BuildCenteredPanel()` (content-sized — Settings/Credits/EscMenu), `BuildPanelHead` (uppercase title + close × + 1-px line-soft divider). `ModalChrome` also exposes `PalettePanelStyle()`, the rounded slate `StyleBoxFlat` shared by HudView's and MapEditorHudView's palette-group panels.

### HUD shape

The play HUD (`HudView`) is widget *clusters* parented into floating zones (no opaque bar — design D1 "Roles Split (floating)"). Map fills the viewport; chips/buttons overlay in fixed zones, only they block clicks. Clusters:

- **Status chip** — `_statusChip` `PanelContainer` (75% slate, line-soft border, 8-px radius) wrapping `_statusCluster` HBox: `TURN` gold eyebrow + turn number (JetBrains Mono 36) and **player-swatch bar** (`scripts/PlayerSwatchBar.cs`) — custom-drawn `Control`, one swatch per player in movement order, current enlarged + white-outlined, eliminated (via `WinConditionRules.IsEliminated`) dimmed. Collapses to single active-swatch + bare turn number in compact. `MouseFilter = Ignore` cascaded.
- **Gold chip** — same styling, gold total + income breakdown (JetBrains Mono 36), hidden when no capital territory selected. Click-through.
- **Action cluster** — `_actionCluster` `BoxContainer` (Vertical flipped per orientation by `SetClusterVertical`) holding the four buy buttons (Recruit/Soldier/Captain/Commander) as flippable `_paletteRow` AND a collapsed cycle button (`_collapsedBuyButton`); exactly one visible, driven by `Compact` in `OnViewportMetricsChanged`. Cycle button fires the same `BuyRecruitClicked` event as the `U` hotkey (`GameController.OnBuyPressed`). `_buildTowerButton` sits in the cluster.
- **Controls cluster** — `_controlsCluster` `BoxContainer` (Vertical flips per orientation) holding `_nextUnitButton` + `_nextTerritoryButton`. `_endTurnButton` is NOT here — placed at row/rail level to anchor independently (bottom-right in landscape, end of bottom-bar row 2 in portrait).
- **Undo cluster** — `_undoCluster` HBox, Undo/Redo ghost icon buttons. Long-press fires Undo All / Redo All.
- **Options** — gear cog (raises `EscRequested`).

Every action/chrome button is a `HudIconButton` at **68×68 logical px**, 2-px black border, 10-px rounded corners, dark-slate fill. A white CTA stylebox layers on via `HudView.ApplyCtaStyle`, which restores the base stylebox on CTA-off via `RestoreBaseStylebox`. Selected state draws a `UiPalette.SelectionRing` outline.

`HudView.HudHeight = 96f` is a layout token for tutorial-builder/record-pane chrome above the editor HUD. Portrait bottom-bar height is `HudBars.PortraitBottomBarHeight = 200f` (two 68-px rows + 8-px separation + 10-px padding).

The editor HUD (`MapEditorHudView`) follows the same shell/clusters: `_landCluster` (rounded slate `PanelContainer` wrapping six land swatches as `BoxContainer`), `_landCycleButton` (standalone squared swatch for compact — sibling, not nested), `_paintCluster` (water/tree/capital/tower as **squared** `HexPaletteButton`s, 68×68 chrome), `_toolsCluster` (hand/pan + die/random), plus undo/redo and Options gear. The die is the lone randomize trigger — fresh seed, regenerate, drop back to hand.

### Responsive layout (landscape / portrait, compact / expanded)

Gameplay and editor reflow between landscape ↔ portrait **and** compact (phone) ↔ expanded (tablet/desktop). Two pure, Godot-free, unit-tested decisions:

- **`ScreenLayout.Resolve(width, height)`** → `Landscape` when `width >= height`, else `Portrait` (square ties to landscape).
- **`ScreenLayout.IsCompact(width, height, prevWasCompact, deadBand)`** → true when the shorter viewport edge is below `ScreenLayout.CompactBreakpointPx = 700` logical px (±32 px dead-band hysteresis to avoid thrash). Every test phone lands compact, every tablet expanded.
- **`ComputeInsets()`** returns `(0, 0)` for the gameplay and editor HUDs — D1 is a floating overlay: the map fills the viewport and the bars float on top.

**Orientation + compact lifecycle** lives in **`OrientationHud : CanvasLayer`** (Template Method). The base owns five **zone** containers, recreated on every layout flip:

| Zone | Type | Present | Role |
|---|---|---|---|
| `TopLeftZone` | content-sized HBox anchored top-left | both | Read-only chips (status, gold) |
| `TopRightZone` | content-sized HBox anchored top-right | both | undo / redo / options |
| `BottomBar` | full-width Panel anchored bottom | portrait only | Action button rows |
| `LeftRail` (+ `LeftRailGroup` inner VBox) | 78-px Panel anchored left, full height | landscape only | Create / paint cluster |
| `RightRail` (+ `RightRailGroup` inner VBox) | mirror of LeftRail anchored right | landscape only | Command / tools cluster |

`Compact` is a public `bool` on `OrientationHud`; subclasses read it in `OnViewportMetricsChanged` to swap collapsed↔expanded palette/roster variants. Rails are `Center`-aligned on compact, `End`-aligned on expanded. Subclasses (`HudView`, `MapEditorHudView`) override `DetachClusters`, `BuildLandscapeBars`, `BuildPortraitBars`, `ComputeInsets`, plus virtual `OnLayoutApplied` (post-flip) and `OnViewportMetricsChanged` (every resize); they never `AddChild` a fresh zone, only parent persistent clusters into the base-prepared zone. `ApplyLayout` rebuilds zones whenever `Orientation` OR `Compact` flips.

**Z-order matters.** `ApplyLayout` adds rails/bottom bar FIRST, then corner zones — corner buttons (Options, undo/redo) must intercept clicks before the rail's full-height Panel. Corner zones are `MouseFilter.Pass`; only chips/buttons inside block clicks. Portrait `BottomBar` is also `MouseFilter.Pass`, so the gap between left action cluster and End Turn falls through.

**Safe-area policy** — split between critical buttons and corner chrome:
- *Rails* (buy, build, nav, end turn) use `max(safe.Left, safe.Right) + edgePad` on BOTH sides so they never overlap the notch in any orientation.
- *Corner zones* (status/gold chips, Options, undo/redo) + bottom-right pinned End Turn use no horizontal safe inset — claiming the corner real estate rails leave. On iPhone landscape corner chrome may visually overlap the notch, but iOS routes taps through.
- `OrientationHud` subscribes to `SafeArea.Changed` so status-bar show/hide or rotation across the notch axis triggers relayout.

**Cluster placement (orientation × variant) — gameplay:**

| | Compact (phone) | Expanded (tablet / desktop) |
|---|---|---|
| Portrait TopLeft | `_statusChip` (1-swatch active) over `_goldChip` | Same, 6-roster swatch bar |
| Portrait TopRight | `_undoCluster` + `_optionsButton` | Same |
| Portrait BottomBar | Row 1: nav cluster (left). Row 2 (space-between): `_actionCluster` (buy cycle + Build Tower) left, `_endTurnButton` right | Row 1 same; Row 2 buy palette → 1×4 radio |
| Landscape TopLeft | `_statusChip` (1-swatch) + `_goldChip` inline | Same, expanded swatches |
| Landscape TopRight | undo + options | Same |
| Landscape LeftRail | `_actionCluster` (buy cycle + Build Tower) vertically centered | Buy palette → 1×4 vertical |
| Landscape RightRail | `_controlsCluster` (nav) vertically centered | Vertically end-anchored (End Turn clearance up) |
| Landscape End Turn | Pinned bottom-right corner (anchored directly to `HudView`, outside rails) | Same; right rail group pushed up by `endTurnClearance = 88px` |

**Cluster placement — editor:**

| | Compact | Expanded |
|---|---|---|
| Portrait TopLeft | *(empty)* | *(empty)* |
| Portrait TopRight | undo + options | Same |
| Portrait BottomBar | Row 1: tools (hand + die). Row 2: `_landCycleButton` + paint tools (water/tree/capital/tower) | Row 2: 1×6 land panel + paint tools |
| Landscape LeftRail | `_landCycleButton` + paint tools, vertically stacked | `_landCluster` (1×6 vertical inside slate panel) + paint tools |
| Landscape RightRail | hand + die | Same |

The `_landCluster` PanelContainer (slate frame around the 1×6 land row) is fully hidden in compact — the bare `_landCycleButton` stands alone as sibling.

**Map reserves nothing in D1** (`HexMapView`). `MapInsetsChanged` fires from `OrientationHud`, but both subclasses' `ComputeInsets` return `(0, 0)` — map fills the viewport edge to edge. Portrait board rotation (−90°) runs via `ScreenLayout.Resolve`; pan/center/zoom math shared (below).

- **Map reserves the bars + rotates in portrait** (`HexMapView`), a pure layout consumer: `SetMapInsets(top, bottom)` (pushed by HUD via `MapInsetsChanged`, relayed by `Main`/`MapEditorScene`) gives the bars' vertical space; it re-centers within that. Rotation resolved from viewport aspect (`ScreenLayout.Resolve`): **portrait ⇒ board node rotates −90° (CCW)**. Up-glyphs (units, capitals, towers, trees, graves, warning badges, tower-placement previews) counter-rotated by `ApplyGlyphUpright()`; the capital warning badge also counter-rotates its upper-left corner offset (`-_mapAngleRad`). Hex-cell-aligned overlays (fills, outlines, borders, water, foam, tower coverage, selection highlight, move-target rings) + rejection arrows rotate with the board. Pan/center/zoom-fit/zoom-anchor math runs through the pure, unit-tested **`MapPlacement.RotatedBoardBox(w, h, zoom, angleRad)`** (on-screen AABB of the scaled+rotated board), with **`MapPlacement.BoxCenter`** (content/grid-box midpoint) and **`MapPlacement.ToWorldOffset(offset, zoom, angleRad)`** (local→world offset, Godot `Rotated` convention) supplying the framing terms. The tree glyph splits into a center-pivot placement node (counter-rotated) + inner trunk-base anchor (grow animation).

- **Content-aware centering (centering only, not clamping).** *Centering* frames the *playable content* (land tiles `_state.Grid.Tiles` — water off-grid), not the nominal `Cols×Rows` grid: `HexMapView` caches the content's unscaled pixel box (`MapPlacement.ContentPixelBounds(landCoords, hexSize)`, recomputed on `Init`/`ReloadState`); `RecenterMap` centers on it via **`MapPlacement.RotatedRectBox(left, top, right, bottom, zoom, angleRad)`** — the offset-rect generalization of `RotatedBoardBox` (which delegates to it). **Pan-clamping frames the full nominal grid** (`ClampPan` → `RotatedBoardBox(PixelSize…)`, then the pure `PanMath.Clamp`); clamping to content would lock panning when content is smaller than the viewport. Discrete zoom stepping (wheel/key) snaps via `ZoomMath.ClosestLevelIndex` + `StepLevel` (nearest stop + clamped step); the view keeps only the `IsEqualApprox` no-op guard. **Edge-scroll pad:** clamp box widened by `ScrollPaddingPx` (300 board-local px pre-zoom, symmetric) applied *after* `RotatedBoardBox` (viewport space, by `PanMath`), letting edge hexes pan out from under floating chips/buttons. Water rim depth = `ceil(ScrollPaddingPx / (1.5·HexSize)) + 1`. Zoom-fit (`ZoomMath.ComputeZoomMin`) uses the full grid; the zoom-out floor `_zoomMin` is that fit divided by `ZoomOutGrace` (1.2, capped at 1×) so max zoom-out leaves margin around the board, while the exact fit is kept as `_zoomFit` for `FrameWholeGrid` (the menu thumbnail frames at fit, not the graced floor). **Insets must reach the map:** `MapInsetsChanged` is relayed to `HexMapView.SetMapInsets` by *both* `Main` (play) and `PreviewPane` (tutorial). (`ComputeInsets()` returns `(0, 0)`; the map fills the viewport.) `RecenterMap` logs inputs + rect at `Render:Debug`. **Hand-tuned opening framing:** `HexMapView.SetCamera(zoom, contentCenterOffset)` is the public alternative to the `RecenterMap` fit default — clamps zoom, re-syncs the discrete level index, centers `contentCenterOffset` from the content-box center. `PreviewPane.Start` uses it (deferred, after the `ReloadState`-queued recenter) for landscape tutorial playback; portrait keeps the fit default. Every user pan/zoom (and `SetCamera`) logs a `Render:Debug` `camera pan/zoom/set` line with zoom + content point under viewport center.

`project.godot` uses default stretch, resizable; responsive behavior is all view-layer. Verify by launching `--resolution 720x1280` (portrait) vs `1280x720` (landscape) and resizing across the square boundary. **Do not switch `window/stretch/mode` to `canvas_items`/`expand`** — view layout already scales from real viewport size, so a stretch mode double-applies scaling.

**Touch input.** Additive — mouse/trackpad stay functional. Single-finger needs no special code: Godot's default `emulate_mouse_from_touch` synthesizes mouse events from finger 0, so **tap = left-click, drag = pan, press-and-hold = long-press (rally)** flow through the existing `HexMapView` mouse path. The new path is **two-finger pinch-to-zoom**: touchscreens don't emit the macOS-trackpad `InputEventMagnifyGesture`/`PanGesture` (those keep their own handlers), so `HexMapView._UnhandledInput` also handles `InputEventScreenTouch`/`InputEventScreenDrag`, tracking fingers in `_touchPoints` and feeding the pure, unit-tested `ZoomMath.PinchZoom` (zoom × new-sep/prev-sep) into `ApplyZoom(newZoom, midpoint)`. A second finger cancels the in-flight finger-0 drag; a `_gestureWasPinch` flag swallows the trailing emulated finger-0 release so ending a pinch never registers a spurious tap/rally. Pinch begin/update/end log under `Log.LogCategory.Input`. The gesture state machine is view-layer (test-excluded); only `PinchZoom` is unit-tested.

## Platform builds & orientation

Build/export mechanics for all four targets live in `RELEASE.md`: `export_presets.cfg`, `tools/build_{macos,windows,android,ios}.sh`, the `dotnet build -c Debug` + `-c ExportDebug`/`ExportRelease` + headless-export shape, the net8-vs-net9 gradle workaround on Android (iOS runs `dotnet publish` against net8 from the Xcode build phases), APK signing, the iOS chain (xcodebuild archive → exportArchive → altool for TestFlight or devicectl for tethered USB, Team ID sed-injected into the empty preset slot and restored on EXIT), plus the on-device install / log-reading / scale-reproduction workflow. This section keeps only the architectural pieces those docs reference.

### Orientation

`project.godot` sets `display/window/handheld/orientation=6` (Godot "Sensor" → Android `screenOrientation="13"` / `fullUser`), so the app follows the device through all four orientations when auto-rotate is on. The `OrientationHud` layer (see *Responsive layout*) resolves orientation from the live viewport size and relayouts on every `SizeChanged`. **Gotcha:** the key is `handheld`, not `handle` — Godot silently ignores an unknown key and keeps default landscape (0).

### Rotation transition (`RotationFix` Android plugin)

A rotation triggers an Android display freeze: `startFreezingDisplayLocked` snapshots the old frame and stretches it into the new bounds until redraw — one distorted frame per rotation. The snapshot is taken *before* the app is notified, so `OrientationHud` / `HexMapView` can't pre-empt it (their relayout settles in ~6ms — see `resize@frame` / `settled` `Render` logs in each `OnViewportResized`). No `android:windowRotationAnimation` attribute exists (aapt rejects it), and the only mode that skips the snapshot (`SEAMLESS`) requires an opaque fullscreen window, which Godot's translucent GL `SurfaceView` prevents.

Workaround: a Godot v2 Android plugin, `RotationFix`:

- **Source:** `android_plugin/rotationfix/` — Kotlin `RotationFixPlugin : GodotPlugin`, built to an AAR by `tools/build_android_plugin.sh` (own gradle project, compiles against `org.godotengine:godot:4.6.1.stable`).
- **Wiring:** `addons/rotationfix/` — `plugin.cfg` + an `EditorExportPlugin` (`rotation_fix_export.gd`) whose `_get_android_libraries` links the AAR; enabled in `project.godot` `[editor_plugins]`. `tools/build_android.sh` auto-builds the AAR on first run if missing (gitignored `bin/` artifact). Discovered via the AAR manifest's `org.godotengine.plugin.v2.RotationFix` meta-data.
- **Behavior:** watches the physical orientation sensor (`OrientationEventListener`) — the only signal arriving before the freeze — and on crossing a band drops an opaque black `TYPE_APPLICATION_PANEL` window over the surface, so the OS snapshots black. Removed `DISPLAY_SETTLE_MS` (600ms) after the rotation lands (`DisplayManager.DisplayListener.onDisplayChanged`), with a `FALLBACK_MS` (1000ms) safety net. Self-skips when auto-rotate is off.

This is a heuristic (hand-tuned hold, can blank on an incomplete tilt).

## User-facing strings (`Strings` / `en.json`)

Every player-visible English string lives in **`assets/strings/en.json`** — a flat JSON object of `key → template`. Code references strings only by key, through three Godot-free pieces in `FourExHex.Controller`:

- **`StringTable`** — immutable-after-parse table. `Parse(json, isMobile)` deserializes via the source-generated `FourExHexJsonContext` (iOS AOT forbids reflection JSON). `Get(key, params (name, value)[] tokens)` resolves the template and substitutes named `{token}`s (values are strings; call sites `.ToString()` ints — the no-floats rule holds). Built-in platform tokens `{Verb}` / `{verb}` resolve from the table's own `verb.capitalized.*` / `verb.lowercase.*` entries picked by `isMobile` — the Tap/Click split is data, not a code branch. **Missing key** → the key itself renders and `[strings] missing key` warns once per key (`Hud` category, deduped so per-frame Refresh can't spam); **missing token** → the `{token}` stays literal and warns once per (key, token). No escape syntax; malformed JSON throws to the caller.
- **`Strings`** — the process-wide static facade (`Configure` / `Get` / `Count`), plus `Strings.UnitName(UnitLevel)` for the shared `unit.*` display names. Unconfigured, every lookup renders its key.
- **`StringKeys`** — one `public const string` per `en.json` entry (dotted lowercase, area-prefixed: `hud.tooltip.*`, `menu.*`, `endgame.*`, `tutorial.*`, `editor.*`, …) so a typo is a compile error, plus key-picking helpers `ForUnit(UnitLevel)`, `ForBuyAction(UnitLevel)`, `ForMode(GameMode)`, `ForDifficulty(Difficulty)`. `tests/StringKeysParityTests.cs` pins exact two-way agreement between the consts and `en.json` — an entry added on one side only fails `dotnet test`.

**Boot wiring**: `LogBootstrap._EnterTree` reads `res://assets/strings/en.json` via `FileAccess` (resolves into the PCK in exports, the project tree in the editor) and calls `Strings.Configure(text, OS.HasFeature("mobile"))` before any scene builds text; it logs `[strings] loaded N keys` (`Info(Hud)`), or `Error(Hud)` on a missing/unparseable file (store stays empty, game runs with keys rendering). Plain `.json` is not a Godot-imported resource, so `export_presets.cfg` lists `assets/strings/*.json` in every preset's `include_filter` — without it an export ships keyless.

**Tests** assert the real shipped English: the tests csproj copies `en.json` beside the binaries and `tests/TestStrings.cs` (`[ModuleInitializer]`) configures the store (desktop verbs) before any test; `TestStrings.ConfigureFromFixture(isMobile: true)` + a `Dispose` restore is the pattern for mobile-verb tests. Copy helpers (`EndgameOverlayContent`, `VikingWaveBannerContent`, `TutorialInstructionText`) own only which-message logic; their tests double as pins on the `en.json` wording.

**Deliberately not in the store**: `Log.*` diagnostics and `GD.PushWarning`/`GD.PushError` developer messages, player/roster names and `CampaignProgress.LabelFor` hex labels (data, not prose), tutorial narration text (author data in `tutorials/*.json`), and default save-slot names. Localization-ready: a second `<lang>.json` drops in by handing `Configure` different text.

## Logging (`Log`)

`src/FourExHex.Model/Log.cs` is the master logging system — one Godot-free static class shared by Model, Controller, and `scripts/` (no namespace, so call sites need no `using`).

- **Two gates.** (1) Compile-time: `Log.Trace` / `Debug` / `Info` are `[Conditional("DEBUG")]`, so the compiler removes the call and its argument evaluation from Release builds; `Log.Warn` / `Error` always compile. (2) Runtime: each `Log.LogCategory` (`Ai`, `Turn`, `Capture`, `Tutorial`, `Render`, `Input`, `Display`, `Hud`, `Undo`, `Cheat`, `Campaign`, `MapGen`, `Replay`, `Tide`, `Fog`, `Tree`, `Automate`, `Viking`, `Determinism`) has an independent minimum `Log.LogLevel`; a message emits only if its level ≥ the category threshold.
- **Default is silent** — every category defaults to `Off`.
- **Configuration.** `LogBootstrap` (autoload) calls `Log.Configure(OS.GetEnvironment("FOUREXHEX_LOG"))`, parsing a spec like `"Ai:Debug,Turn:Info,*:Warn"` (comma-separated `category:level`, `*` = all; case-insensitive; unknown tokens ignored; never throws).
- **Pre-computing helpers** (`GameController.LogTurnStart`, `LogAction`, `LogGameEndDiagnostics`, `LogCaptureDiff`) are themselves `[Conditional("DEBUG")]` so the body strips in Release. `Warn`/`Error` sites keep their precompute.
- `GD.PushWarning` / `GD.PushError` (developer-facing failure diagnostics) are deliberately not routed through `Log`.

## Diagnostic mode (`FOUREXHEX_6AI`)

Setting the env var before launch reconfigures the session for a fully headless regression run:

- All six slots forced to `PlayerKind.Computer` (the menu also detects the var and skips itself, jumping straight into `Main`).
- After parsing `FOUREXHEX_LOG`, `Main` pins `Log` to `Ai:Debug`, `Turn:Info`, `Capture:Debug` — set *after* `Configure` so a stray `FOUREXHEX_LOG=*:Off` can't silence the harness.
- `SynchronousAiPacer` replaces `GodotAiPacer` — turns execute inline.
- `HeadlessHexMapView` / `HeadlessHudView` replace the real views.
- `GameController` constructed with `maxTurnNumber: 500` so stasis runs terminate.
- The scene subscribes to `GameController.GameEnded` and defers `SceneTree.Quit()` so the process exits on game-over.

Typical invocation:
```
FOUREXHEX_6AI=1 /Applications/Godot_mono.app/Contents/MacOS/Godot \
  --headless --path . 2>&1 | tee /tmp/ai-run.log
```

## File layout

Files grouped by responsibility; the **project** a file belongs to follows "Project structure & the Godot-free model" above. Three source trees:

- `scripts/` (the `FourExHex` Godot project) — `Node`/scene/view/filesystem code plus the `PlayerPalette` / `HexPixel` view adapters.
- `src/FourExHex.Model/` — pure model, rules, AI (incl. `AiDispatcher`), `UndoStack<T>` + `GameStateSnapshot`, save serialization (`SaveSerializer`, `Replay`, `ReplayBeat`, the `Tutorial` POCO), `MapGenerator` / `MapEditPaint` / `EditorSnapshot`, `PlayerId`.
- `src/FourExHex.Controller/` (references Model one-way) — `GameController`, `SessionState` / `SessionStateSnapshot` / `UndoEntry`, the `IHexMapView` / `IHudView` / `IAiPacer` interfaces, `AiPacer` / `GodotAiPacer`, and the `Tutorial/` Record/Preview scripting helpers (everything in `Tutorial/` except the `Tutorial` POCO).

The tree below keeps the `scripts/` prefix only as a grouping label; per-file project is per the lists above.

```
scripts/  (split: see the three source trees listed just above)
├─ Main.cs                ─ play scene root; wires model + views + controller
├─ MainMenuScene.cs       ─ landing (Resume / Play / Play Tutorial / Load /
│                           Map Editor / Settings + desktop Exit) + paged New
│                           Game flow; Load Game modal; SettingsPanel overlay;
│                           Exit/Escape→ConfirmModal; writes GameSettings +
│                           LoadRequest
├─ MapThumbnailView.cs    ─ New Game preview: renders HexMapView into a hidden
│                           SubViewport, snapshots to a TextureRect
├─ MapInfoSheet.cs        ─ reusable "play this board?" sheet; CampaignConfirm-
│                           Sheet is a factory over it
├─ MapEditorRequest.cs    ─ static menu→editor handoff (NewMap kinds / LoadMap
│                           slot), like LoadRequest
├─ PlayTutorialScene.cs   ─ "Play Tutorial" scene root; hosts MapEditorPanel +
│                           PreviewPane + EscMenu, loads + plays bundled
│                           full_tutorial
├─ MapEditorScene.cs      ─ editor scene root; chrome host (HUD, Save/Load
│                           dialogs, EscMenu with Resume / Save Map / Load Map
│                           / Exit)
├─ MapEditorPanel.cs      ─ reusable editor body; owns HexMapView + draft
│                           grid/water/territories + UndoStack<EditorSnapshot>
│                           + paint stroke state + hover tooltip
├─ MapEditorHudView.cs    ─ editor HUD (seed + palette + undo/redo + Options).
│                           Configurable via ShowSceneRootChrome.
│                           Save/Load Map live in the EscMenu
├─ TutorialBuilderScene.cs─ tutorial builder scene root; TutorialMode
│                           { MapEdit, Record, Preview } state machine; hosts
│                           MapEditorPanel + MapEditorHudView + RecordPane +
│                           PreviewPane + EscMenu; captures/restores draft
│                           EditorSnapshot around play sessions
├─ EscMenu.cs             ─ shared pause/exit modal (CanvasLayer; ProcessMode =
│                           Always). Show takes a mode-aware option list. ESC
│                           fires EscapeClosed (vs Closed) so the pause
│                           coordinator distinguishes back-out from clicks.
│                           Used by Main, MapEditorScene,
│                           TutorialBuilderScene, CheatMenu
├─ CheatMenu.cs           ─ Debug-only cheat menu (#if DEBUG; owns an EscMenu);
│                           backquote / 3-finger tap toggles over any screen;
│                           scene roots opt in via CheatMenu.Attach(this)
├─ SettingsPanel.cs       ─ shared Settings modal (SFX/VFX checkboxes + speed
│                           rows + Credits + Back); Open/Close/Closed; owns
│                           CreditsPanel. Used by MainMenuScene + Main pause
├─ CreditsPanel.cs        ─ Credits modal (CanvasLayer Layer 101; scrollable
│                           BBCode credits, author name → repo via MetaClicked
│                           → OS.ShellOpen). Owned by SettingsPanel
├─ ConfirmModal.cs        ─ reusable yes/no confirm modal (ModalChrome family);
│                           ctor takes title/message/confirm-label;
│                           Confirmed/Canceled; Escape cancels, Enter confirms.
│                           Used by MainMenuScene's Exit flow
├─ SlotPickerDialog.cs    ─ reusable load-slot picker on the shared modal
│                           shell; ShowSlots(slots, emptyMsg, labelFor,
│                           onPicked) + ShowError; ProcessMode = Always. Built
│                           from ModalChrome. Used by MainMenuScene,
│                           MapEditorScene, TutorialBuilderScene, Main
├─ RecordPane.cs          ─ Record-mode chrome: real GameController over the
│                           draft, all six players Human; captures via
│                           RecordingCapture. ContinueRecording resumes a
│                           Preview→Record handoff via BeginReplay
├─ PreviewPane.cs         ─ Preview-mode chrome: real GameController with
│                           ReplayDrivenAi + TutorialPreview +
│                           humanActionValidator; PreviewSetup resets state
├─ InstructionsPanel.cs   ─ paged near-fullscreen rules explainer (Help menu →
│                           Instructions); per-page text + demo, orientation-
│                           aware split, tour-style keys
├─ InstructionDemoView.cs ─ Instructions graphics panel: second game stack
│                           (own GameState/Controller/HexMapView + headless
│                           HUD) looping a bundled tutorial in an input-
│                           isolated SubViewport, pinned mute
├─ MapEditPaint.cs        ─ pure paint helpers (Land / Neutral / Capital /
│                           Tower / Tree / Water)
├─ EditorSnapshot.cs      ─ deep copy of editor draft (grid + water + terr.)
├─ HexPaletteButton.cs    ─ hex-shaped palette swatch; delegates glyphs to
│                           HudIcons helpers (shared with HudView)
├─ HexHoverTooltip.cs     ─ editor-only tooltip: hovered hex's lex index +
│                           (col, row)
├─ HexDragMode.cs         ─ Pan | Paint enum gating HexMapView's left-button
│                           gesture interpretation
├─ GameSettings.cs        ─ global player config (PlayerConfig, PlayerKinds,
│                           optional MasterSeed)
├─ LoadRequest.cs         ─ static one-shot handoff: menu Load → Main
├─ GameController.cs      ─ pure C# orchestration: input event handlers,
│                           human execute paths, undo/redo application,
│                           turn rotation, AI/replay entry points
├─ GameOperations.cs      ─ mutation/orchestration core shared by live AI and
│                           replay: ExecuteAi*, HandleCapture, DeclareWinner,
│                           DispatchActionSound, ApplyLongPressRally,
│                           EndOfTurnProcessing, AdvanceToNextActivePlayer,
│                           StartPlayerTurn, RefreshViews, RunInstantTick,
│                           CheckGameEndConditions, RefreshSilentMode. See
│                           "GameController ↔ GameOperations split"
├─ AiTurnDriver.cs        ─ live-AI turn driver: paced preview/execute step
│                           machine, chunked instant driver, per-turn scratch
│                           state, single game-over/human gate. Calls
│                           GameOperations + ReplayRecorder one-way. See
│                           "GameController ↔ AiTurnDriver split"
├─ StepPacing.cs          ─ shared step-beat cadence: the four delay constants
│                           + the Redispatch instant↔paced transition/dispatch
│                           skeleton both step machines delegate to
├─ ReplayRecorder.cs      ─ replay subsystem: beat log, initial snapshot,
│                           undo/redo beat-stack, paced + instant playback step
│                           machines. RecordBeat, BeginReplay/EndReplay/
│                           StepReplay*, ExecuteReplayBeat, ReplayApplyEndTurn,
│                           ReplayInstantStep. Calls GameOperations one-way.
│                           Hosts InstantStep enum. See "GameController ↔
│                           ReplayRecorder split"
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
├─ HexMapView.cs          ─ concrete map: rendering + input + camera pan +
│                           audio forwarding
├─ HudView.cs             ─ concrete HUD: 96-px slate bar (bottom landscape;
│                           split display-top / controls-bottom portrait) +
│                           defeat / claim-victory / victory overlays +
│                           tutorial-message popup + bankruptcy toast. Buy/
│                           Build always visible; tooltips name disabled reason
├─ HudIconButton.cs       ─ Button painting a programmatic glyph via _Draw;
│                           carries Selected, CtaActive, BuyLevel.
│                           DefaultTooltip(HudIcon) is the single source for
│                           "<label> — <hotkey>" strings (HudView +
│                           MapEditorHudView)
├─ HudIcons.cs            ─ static glyph helpers shared by HudIconButton +
│                           HexPaletteButton (tree, capital, tower, hand, unit
│                           rings, curved arrow, end-turn triangle, gear, d6)
├─ UiPalette.cs           ─ static design-token constants (surfaces incl.
│                           HudBar, lines, ink, brass, water, ModalBackdrop
│                           scrim) for view code that paints directly.
│                           Heraldic board-game palette
├─ BoardPalette.cs        ─ static fixed board colors (RejectRed, ForestCanopy/
│                           Trunk, CastleFill, GraveCross, WarnRed/Yellow);
│                           shared by HexMapView on-tile art + HudIcons.
│                           Distinct from UiPalette + PlayerPalette
├─ ModalChrome.cs         ─ static builders for the CanvasLayer modal shell
│                           (BuildBackdrop, BuildCenteredPanel, BuildPanelHead)
│                           + PalettePanelStyle(); shared by SlotPickerDialog,
│                           SettingsPanel, CreditsPanel, ConfirmModal, EscMenu,
│                           HUD palette-group panels
├─ HeadlessViews.cs       ─ no-op view stubs for diagnostic mode
├─ AudioBus.cs            ─ autoload Node singleton: shared SFX players
│                           surviving scene changes; each Play* gates on
│                           UserSettings.SfxEnabled
├─ UserSettings.cs        ─ static; SfxEnabled / VfxEnabled / AiSpeed /
│                           ReplaySpeed persisted to user://settings.json
│                           (lazy load, atomic tmp+rename save). AiSpeed/
│                           ReplaySpeed share one PlaybackSpeed enum.
│                           SpeedMultiplier maps Slow/Normal/Fast → 2/1/0.5;
│                           Instant has no arm (chunked ScheduleUnscaled driver)
│
├─ AiPacer.cs             ─ IAiPacer (Schedule + ScheduleUnscaled + Cancel) +
│                           SynchronousAiPacer (drains inline) + ITimerFactory
├─ GodotAiPacer.cs        ─ production pacer; ITimerFactory + generation
│                           counter for Cancel-then-reuse (testable via
│                           ManualTimerFactory). Schedule scales by optional
│                           Func<float> delayMultiplier; ScheduleUnscaled
│                           passes through. Always frame-yields
├─ SceneTreeTimerFactory.cs ─ production ITimerFactory wrapping
│                           SceneTree.CreateTimer (test-excluded). processAlways:
│                           false so AI pacing halts on GetTree().Paused
├─ AiAction.cs            ─ AiMoveAction / AiBuyUnitAction / …
├─ AiCommon.cs            ─ shared candidate-action enumeration
├─ AiDispatcher.cs        ─ routes by Player.Kind
├─ AiActionCore.cs        ─ bare AI-action mutation core (flags, gold
│                           deduction, placement) shared by AiSimulator
│                           and GameOperations.ExecuteAi*
├─ AiSimulator.cs         ─ Clone + apply for 1-ply lookahead; throws on
│                           unsupported AiAction kinds
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
│                           OnTileLongClickedBody and replay's ApplyLongPressRally
├─ PurchaseRules.cs       ─
├─ TreeRules.cs           ─
├─ UpkeepRules.cs         ─
├─ WinConditionRules.cs   ─
│
├─ SaveStore.cs           ─ user://saves/ + user://maps/ + user://tutorials/
│                           slot CRUD; res://tutorials/ read-only bundled maps
├─ SaveSerializer.cs      ─ JSON (de)serializer for game state + maps +
│                           optional Tutorial + optional Replay block (v4;
│                           still reads v2/v3)
├─ SaveSlotInfo.cs        ─ slot listing metadata
├─ Replay.cs              ─ POCO bundling InitialSnapshot + beat list (v4 block)
├─ ReplayBeat.cs          ─ discriminated record family: ReplayMoveBeat /
│                           ReplayBuyBeat / ReplayBuildTowerBeat /
│                           ReplayEndTurnBeat / ReplayLongPressRallyBeat /
│                           ReplayClaimVictoryBeat / ReplayDismissClaim /
│                           ReplayDismissDefeat. Plus a TutorialOnlyBeat
│                           sub-hierarchy (Actor=-1, authored) — first kind
│                           ReplayDisplayTextBeat. See Tutorial-only beats
├─ Tutorial/Tutorial.cs   ─ tutorial POCO { Title, Replay }
├─ Tutorial/ReplayDrivenAi.cs ─ AI chooser replaying recorded non-player-0
│                           beats through the AI step machine; shares a
│                           ScriptCursor with TutorialPreview
├─ Tutorial/TutorialPreview.cs ─ player-0 input validator; matches attempts
│                           against next expected beat; fires
│                           PlayerActionRejected / TutorialFinished
├─ Tutorial/RecordingCapture.cs ─ pure-C# captor letting the recorded tutorial
│                           survive the record controller's teardown
│                           (RecordPane)
├─ Tutorial/PreviewSetup.cs ─ pure-C# helper applying the InitialSnapshot back
│                           to live state + clears overlays + rebuilds
│                           border/capital layers (PreviewPane)
├─ Tutorial/TutorialPreviewCues.cs ─ pure-C# helper painting the visual cue for
│                           the next beat (CTA button + auto-selected territory
│                           + single-tile highlight) and pushing step text via
│                           ShowTutorialMessage; wired via onAfterRefresh
├─ Tutorial/TutorialInstructionText.cs ─ pure-C# lookup mapping next ReplayBeat
│                           + GameState + SessionState to a sub-step-aware
│                           instruction string
├─ Tutorial/TutorialNarrationDriver.cs ─ pure-C# helper consuming
│                           TutorialOnlyBeats from the shared ScriptCursor
│                           during Preview. Presents via ShowTappableTutorial-
│                           Message, gates cues via IsPresenting, advances on
│                           TutorialMessageTapped. Wired into PreviewPane's
│                           onAfterRefresh ahead of TutorialPreviewCues.Apply
│
├─ HexCoord.cs            ─ model primitives
├─ HexGrid.cs             ─
├─ HexTile.cs             ─ pure model: Coord, Owner, Occupant (no Godot/view
│                           ref — fills owned by HexMapView)
├─ HexOccupant.cs         ─
├─ Unit.cs                ─ + UnitLevel + UnitLevelExtensions
├─ Capital.cs             ─
├─ Tower.cs               ─
├─ Tree.cs                ─
├─ Grave.cs               ─
├─ Territory.cs           ─ + TerritoryExtensions
├─ Player.cs              ─ + PlayerKind {Human,Computer,None}; BuildRoster
│                           (skips None), BuildCampaignRoster
├─ MapRosterRules.cs      ─ pure editor-save validation (active⇔owns-land, ≥1
│                           capital per active color, ≥2 players) for baked
│                           map rosters
├─ TurnState.cs           ─
├─ Treasury.cs            ─
├─ ZoomMath.cs            ─ pixel↔hex helpers used by HexMapView
├─ GameStateSnapshot.cs   ─
├─ GameStateChecksum.cs   ─ SHA-256 digest over tiles/gold/territories/turn
│                           state; used by replay-fidelity tests + live
│                           divergence check
└─ UndoStack.cs           ─ generic two-sided history (play + editor)

scenes/
├─ main_menu.tscn         ─ initial scene (pinned in project.godot)
├─ main.tscn              ─ play scene
├─ map_editor.tscn        ─ editor scene
└─ tutorial_builder.tscn  ─ tutorial builder scene (debug-only entry)

tests/
├─ TestHelpers.cs         ─ shared fixtures
├─ MockHexMapView.cs      ─ IHexMapView in-memory impl
├─ MockHudView.cs         ─ IHudView in-memory impl
├─ QueuedAiPacer.cs       ─ IAiPacer queuing callbacks for explicit Drain() —
│                           for tests inspecting intermediate AI step state
└─ *Tests.cs              ─ xUnit tests: controller flows, rules, AI, snapshot/
                            undo, primitives, save/load round-trip, autosave,
                            abandon, RNG determinism, editor paint + snapshot/
                            undo, replay recording / playback / fidelity
```

`Main.cs`, `MainMenuScene.cs`, `MapEditorScene.cs`, `MapEditorPanel.cs`, `MapEditorHudView.cs`, `TutorialBuilderScene.cs`, `EscMenu.cs`, `CheatMenu.cs`, `SettingsPanel.cs`, `CreditsPanel.cs`, `ConfirmModal.cs`, `SlotPickerDialog.cs`, `RecordPane.cs`, `PreviewPane.cs`, `HexPaletteButton.cs`, `HexHoverTooltip.cs`, `HexMapView.cs`, `HudView.cs`, `SceneTreeTimerFactory.cs`, `HeadlessViews.cs`, `SaveStore.cs`, `AudioBus.cs`, and `UserSettings.cs` are NOT compiled into the test assembly — they derive from Godot nodes or depend on `SceneTree` / Godot `FileAccess` / autoload lifecycle, so they stay in the `FourExHex` project. The test project `<ProjectReference>`s both `src/FourExHex.Model` and `src/FourExHex.Controller` with NO per-file `<Compile Include>` list and NO GodotSharp reference: a testable source file under either library is picked up automatically. If it needs Godot it belongs in `scripts/`.

## Tests

Run with `dotnet test`. Covers every static rule class, the `GameController` click + turn state machine (mock views + synchronous pacer), `Treasury`, `UndoStack`, `GameStateSnapshot`, both AI flavors, the editor's paint helpers + `EditorSnapshot` round-trip, save/serialize/deserialize equivalence, RNG determinism across save/load, replay recording + playback contracts, and a 6-heuristic-AI replay-fidelity test that hashes the live final state, round-trips through SaveSerializer, and asserts digest-for-digest match. Also `PlayerId` semantics, the `Log` category/level gate, `HexCoord.Round`, and v2→v7 save migration (`SaveMigrationTests`). The view layer is deliberately uncovered (Godot `Node` lifecycle); pin behavior in the controller and rules instead.

That `dotnet test` builds and passes against `FourExHex.Model` + `FourExHex.Controller` with zero GodotSharp on the reference graph is itself the purity test: if either library takes a Godot dependency — or model code names a controller-layer type — the build stops compiling and the suite goes red.

For coverage:

```
dotnet test --collect:"XPlat Code Coverage" --settings tests/coverlet.runsettings
```

## Rebuild-before-launch rule

Godot does not always rebuild the C# assembly when launching. After editing any `.cs` file, run:

```
dotnet build FourExHex.csproj
```

before relaunching or you'll run stale code.

