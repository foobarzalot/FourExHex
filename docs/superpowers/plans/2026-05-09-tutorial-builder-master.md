# TutorialBuilder Implementation Plan — Master

> **For agentic workers:** This is the master index. To execute a phase, read its per-phase plan file (linked below). For phases marked ⏳, first run `superpowers:writing-plans` against the design spec to expand the phase into a step-by-step plan, then execute it.

**Design spec:** [`docs/superpowers/specs/2026-05-09-tutorial-builder-design.md`](../specs/2026-05-09-tutorial-builder-design.md)

**Goal:** Ship a developer-only TutorialBuilder authoring tool for FourExHex via 14 sequential, manually-testable phases.

**Architecture (summary):** A new `tutorial_builder.tscn` scene composes the existing Map Editor (refactored into a reusable `MapEditorPanel`) plus two new modes — Build (author beats) and Preview (live-play with input gating). A pure-C# `TutorialPlayer` wraps `GameController` via injected `aiChooser` + wrapper views; no changes to `GameController` or the rules layer. JSON v3 adds an optional `Tutorial` block. Dev-mode gating via `OS.IsDebugBuild()`.

**Tech stack:** Godot 4.6.1 (.NET) + C# (net8.0) + xUnit.

---

## How to execute this plan

1. **Phases run strictly in order.** Each builds on previous artifacts. No parallel execution.
2. **Each phase is its own plan file** under `docs/superpowers/plans/`. The next agent picks up the next phase whose status is 📝 (ready to execute).
3. **For phases still marked ⏳** (not yet expanded): the agent who picks up that phase first runs `superpowers:writing-plans` against the design spec, scoped to that phase's section, to produce the per-phase plan file. Then executes it.
4. **Every phase ends with a manual test** in the running game. Per `CLAUDE.md`'s rebuild-before-launch rule, run `dotnet build FourExHex.csproj` before relaunching Godot.
5. **Per `CLAUDE.md`'s strict-TDD rule**, behavior changes are test-first; refactors verify against existing green tests. Each phase's plan flags which tasks are TDD vs refactor.
6. **Strictly-sequential**: do not start phase N+1 before phase N's manual test passes and the user signs off.

## Status legend

- ✅ Complete (merged to main)
- 🟡 In progress
- 📝 Plan written, ready to execute
- ⏳ Not yet expanded — run `superpowers:writing-plans` to produce the per-phase plan first

---

## Phase index

### Phase 1 — Refactor `MapEditorScene` → `MapEditorPanel`

- **Status:** ✅ Complete
- **Plan file:** [`2026-05-09-tutorial-builder-phase-01-mapeditorpanel-refactor.md`](2026-05-09-tutorial-builder-phase-01-mapeditorpanel-refactor.md)
- **Goal:** Pure refactor. Extract draft state, paint logic, view ownership, undo stack, and hover tooltip into a reusable `MapEditorPanel : Node2D`. `MapEditorScene` becomes a thin host that wires its existing HUD to the panel. No behavior change.
- **Key files:** `scripts/MapEditorScene.cs` (slim down), `scripts/MapEditorPanel.cs` (new).
- **Manual test:** Launch Map Editor, exercise paint/generate/save/load/undo (all four buttons + Z/Y kbd)/escape-ladder/exit — identical to today.
- **Depends on:** None.

### Phase 2 — `tutorial_builder.tscn` scene + 3-mode topbar

- **Status:** ✅ Complete
- **Plan file:** [`2026-05-09-tutorial-builder-phase-02-scene-and-topbar.md`](2026-05-09-tutorial-builder-phase-02-scene-and-topbar.md)
- **Goal:** New scene with `TutorialBuilderScene : Node2D` root, 3-mode topbar (Map Edit / Build / Preview, kbd 1/2/3), Save Tutorial / Load Tutorial / Exit chrome (Save/Load disabled until Phase 3). Map Edit mode hosts `MapEditorPanel` (from Phase 1). Build/Preview show "Coming soon" placeholders. `MainMenuScene` adds a debug-only "Tutorial Builder" button.
- **Key files:** `scenes/tutorial_builder.tscn` (new), `scripts/TutorialBuilderScene.cs` (new), `scripts/TutorialBuilderTopBar.cs` (new), `scripts/MainMenuScene.cs` (add button).
- **Manual test:** From main menu in a debug build, click "Tutorial Builder" → land in scene; switch modes via buttons + kbd 1/2/3; in Map Edit, paint a hex; in Build/Preview, see placeholders; switch back to Map Edit, painted hex still there.
- **Depends on:** Phase 1.
- **Spec reference:** Spec §"TutorialBuilder scene" + §"Dev-mode gating".

### Phase 3 — Beat schema v3 + `EndTurn` beat (author + preview)

> Phase 3 was originally a single load-bearing phase. It's now split into 3a / 3b / 3c so each sub-phase ends with a real, manually-testable user-facing change. `AnchorRef` / `DismissOn` POCOs are deferred to **Phase 7** (their first consumer — `PromptBeat`).

#### Phase 3a — Empty-tutorial save / load

- **Status:** ✅ Complete
- **Plan file:** [`2026-05-09-tutorial-builder-phase-03a-empty-tutorial-save-load.md`](2026-05-09-tutorial-builder-phase-03a-empty-tutorial-save-load.md)
- **Goal:** Stand up the tutorial save format and the topbar Save / Load buttons end-to-end, with an *empty* tutorial (no beats yet). Lays the v3 format compatibility foundation everything else relies on.
  - New `Tutorial` POCO under `scripts/Tutorial/` (`Title`, `StartTurn`, `StartPlayer`, empty `Beats` list — no `Beat` type yet).
  - `SaveSerializer` v2 → v3 bump with **optional** top-level `Tutorial` block. Critically, the deserializer must **accept both v2 and v3** (the current code rejects any mismatch — that has to soften).
  - `LoadedSave.Tutorial` nullable field.
  - `SaveStore.WriteTutorial` / `LoadTutorial` / `ListTutorials` against `user://tutorials/`.
  - `TutorialBuilderScene` enables `_topBar.SaveEnabled = _topBar.LoadEnabled = true` and wires `SaveTutorialPressed` / `LoadTutorialPressed` to in-scene save/load dialogs (reusing the `MapEditorScene` pattern). Saves the panel's current map draft + an empty `Tutorial`; load restores the draft into the panel.
- **Key files:** `scripts/Tutorial/Tutorial.cs` (new), `scripts/SaveSerializer.cs` (v3 bump + dual-version read), `scripts/SaveStore.cs` (tutorial CRUD), `scripts/LoadedSave.cs` (Tutorial field), `scripts/TutorialBuilderScene.cs` (enable + wire buttons), `tests/TutorialSerializerTests.cs` (new).
- **Manual test:** Open Tutorial Builder → paint a few hexes → Save Tutorial (name "test1") → Load Tutorial → see "test1" in list → pick it → painted hexes restored.
- **Depends on:** Phase 2.
- **Spec reference:** Spec §"Data model" (`Tutorial` only), §"Save/load", §"JSON v3 schema".

#### Phase 3b — Author an `EndTurn` beat in Build

- **Status:** ✅ Complete
- **Plan file:** [`2026-05-09-tutorial-builder-phase-03b-endturn-beat-author.md`](2026-05-09-tutorial-builder-phase-03b-endturn-beat-author.md)
- **Goal:** The author side of the EndTurn loop. Introduces `Beat` discriminator + `EndTurnBeat` POCO, grows the BuildPane into a minimal timeline + "Add EndTurn" + inspector, and round-trips the beat through 3a's serializer.
  - `Beat` abstract record + `BeatKind` enum + `EndTurnBeat` (no other concrete beats yet).
  - `SaveSerializer` learns to (de)serialize `Beats` with `Kind` discriminator (extension of 3a's Tutorial block).
  - `BuildPane` real chrome: empty timeline, "Add EndTurn" button (creates beat for actor 0 turn 1), inspector showing `(Turn, Actor)`.
  - Tests extended for beat round-trip.
- **Key files:** `scripts/Tutorial/Beat.cs` (new), `scripts/Tutorial/Tutorial.cs` (extend), `scripts/SaveSerializer.cs` (extend Tutorial DTOs), `scripts/BuildPane.cs` (real chrome), `tests/TutorialSerializerTests.cs` (extend).
- **Manual test:** In TutorialBuilder Build mode, click "Add EndTurn" → beat appears in timeline; Save → reload → beat is back; click the beat → inspector shows `Turn 1, Actor 0`.
- **Depends on:** Phase 3a.
- **Spec reference:** Spec §"Data model" (`Beat`, `EndTurnBeat`), §"Build mode" (EndTurn paragraph).

#### Phase 3c — Preview the `EndTurn` beat

- **Status:** ⏳ Not yet expanded
- **Goal:** The preview side. Brings up `TutorialPlayer` + gated view wrappers + `TutorialValidator.MatchesEndTurn` + `PreviewPane` real chrome, reaching the original Phase 3 end-to-end manual test.
  - `TutorialPlayer` (no scripted-AI logic — `AiChooser` falls through to `AiDispatcher` always).
  - `TutorialGatedHexMapView` + `TutorialGatedHudView` wrapper views.
  - `TutorialValidator.MatchesEndTurn`.
  - `PreviewPane` real chrome: instantiates a transient `GameController` with the gated views + the panel's current draft as starting state.
- **Key files:** `scripts/Tutorial/TutorialPlayer.cs` (new), `scripts/Tutorial/TutorialValidator.cs` (new), `scripts/Tutorial/TutorialGatedHexMapView.cs` (new), `scripts/Tutorial/TutorialGatedHudView.cs` (new), `scripts/PreviewPane.cs` (real chrome), `tests/TutorialPlayerTests.cs` (new).
- **Manual test:** Author an EndTurn beat (3b), Save, switch to Preview → click End Turn → toast "tutorial complete" / auto-advance; click a tile → soft-reject toast.
- **Depends on:** Phase 3b.
- **Spec reference:** Spec §"Runtime: TutorialPlayer", §"Preview mode".

### Phase 4 — `BuyPeasant` beat (author + preview)

- **Status:** ⏳ Not yet expanded
- **Goal:** Add `BuyPeasantBeat` POCO + serializer support; BuildPane "Add BuyPeasant" enters "pick At" tile mode; gated views accept matching BuyPeasant clicks; `AiSimulator.Apply` already handles BuyPeasant (verify), reuse for the BuildPane state-after-beat-N cache; `TutorialValidator.MatchesBuyPeasant`.
- **Key files:** `scripts/Tutorial/Beat.cs` (extend), `scripts/BuildPane.cs` (extend), `scripts/Tutorial/TutorialPlayer.cs` (extend), `scripts/Tutorial/TutorialGatedHudView.cs` (extend `BuyPeasantClicked` gating), `tests/TutorialPlayerTests.cs` (extend), `tests/TutorialBeatSimulatorTests.cs` (new).
- **Manual test:** In TutorialBuilder, paint a 5-tile territory with a capital in Map Edit; switch to Build, click "Add BuyPeasant", click a friendly tile → beat created; save, switch to Preview, click designated tile → peasant placed; click a wrong tile → soft-reject toast.
- **Depends on:** Phase 3.
- **Spec reference:** Spec §"Data model" (BuyPeasantBeat), §"Beat pointer advancement" (Human player-action beats).

### Phase 5 — `Move` beat (author + preview)

- **Status:** ⏳ Not yet expanded
- **Goal:** Add `MoveBeat` POCO + serializer support; BuildPane "Add Move" enters "pick Src" then "pick Dst" mode; gated views accept matching `(Src, Dst)` Move clicks per `MovementRules`; `TutorialValidator.MatchesMove`.
- **Key files:** `scripts/Tutorial/Beat.cs` (extend), `scripts/BuildPane.cs` (extend), `scripts/Tutorial/TutorialPlayer.cs` (extend), `scripts/Tutorial/TutorialGatedHexMapView.cs` (extend `TileClicked` gating with two-click sequence), `tests/TutorialPlayerTests.cs` (extend), `tests/TutorialBeatSimulatorTests.cs` (extend).
- **Manual test:** Continue Phase 4's setup. In Build, after the BuyPeasant beat, "Add Move", click peasant tile then destination → beat created. Preview through.
- **Depends on:** Phase 4.
- **Spec reference:** Spec §"Data model" (MoveBeat).

### Phase 6 — `BuildTower` beat (author + preview)

- **Status:** ⏳ Not yet expanded
- **Goal:** Mirror of Phase 4 for `BuildTowerBeat`.
- **Key files:** Same shape as Phase 4, swapping `BuyPeasant` → `BuildTower`.
- **Manual test:** Author + preview a BuildTower beat.
- **Depends on:** Phase 5.
- **Spec reference:** Spec §"Data model" (BuildTowerBeat).

### Phase 7 — `Prompt` beat + bubble UI (Tile anchor only)

- **Status:** ⏳ Not yet expanded
- **Goal:** Add `PromptBeat` POCO; new `TutorialPromptBubble : Control` (CanvasLayer overlay, positioned around the anchor's screen rect); BuildPane "Add Prompt" → inline editor (body, dismiss-mode dropdown, tile-anchor pick); PreviewPane mounts the bubble at beat onset; auto-dismisses per `DismissOn` (Click / NextBeat / Delay).
- **Key files:** `scripts/Tutorial/Beat.cs` (extend), `scripts/TutorialPromptBubble.cs` (new), `scripts/BuildPane.cs` (extend with prompt editor), `scripts/PreviewPane.cs` (extend), `scripts/Tutorial/TutorialPlayer.cs` (extend overlay-beat path), `tests/TutorialPlayerTests.cs` (extend with prompt show/dismiss timing).
- **Manual test:** In Build, add a Prompt beat anchored to a tile, body "Hello", dismiss `Click`. Preview: bubble appears at the tile; click anywhere → dismisses; tutorial advances. Repeat with `NextBeat` and `Delay:1500`.
- **Depends on:** Phase 6.
- **Spec reference:** Spec §"Data model" (PromptBeat, AnchorRef.TileAnchor, DismissOn), §"Beat pointer advancement" (Overlay beats).

### Phase 8 — `Highlight` beat (author + preview)

- **Status:** ⏳ Not yet expanded
- **Goal:** Add `HighlightBeat` POCO; BuildPane "Add Highlight" → tile pick + Style toggle (Ring/Spot); PreviewPane uses existing `_map.ShowHighlight` (or a new Highlight overlay if Style:Spot needs different rendering); auto-clears on next beat.
- **Key files:** `scripts/Tutorial/Beat.cs` (extend), `scripts/BuildPane.cs` (extend), `scripts/PreviewPane.cs` (extend), `scripts/Tutorial/TutorialPlayer.cs` (extend), possibly `scripts/HexMapView.cs` (add Highlight overlay if needed for Style:Spot — judged in-phase), `tests/TutorialPlayerTests.cs` (extend).
- **Manual test:** Author Highlight, Preview: tile pulses (Ring) or solid spotlight (Spot); auto-clears when next beat starts.
- **Depends on:** Phase 7.
- **Spec reference:** Spec §"Data model" (HighlightBeat).

### Phase 9 — `CameraFocus` + multi-anchor (Hud / Region / None)

- **Status:** ⏳ Not yet expanded
- **Goal:** Add `CameraFocusBeat` POCO; add `HudView.GetAnchorRect(string id) → Rect2?` for `HudAnchor` resolution (HUD ids: `hud.gold`, `hud.endTurn`, `hud.buyPeasant`, `hud.buildTower`, `hud.undoLast`, `hud.nextTerritory`); BuildPane anchor picker upgraded to support HUD dropdown, Region multi-click + commit, None; PreviewPane CameraFocus pans `HexMapView` camera (preserving the dev's manual zoom per scope decision #5).
- **Key files:** `scripts/Tutorial/Beat.cs` (extend), `scripts/Tutorial/AnchorRef.cs` (extend if needed), `scripts/HudView.cs` (add `GetAnchorRect`), `scripts/IHudView.cs` (add `GetAnchorRect`), `scripts/MockHudView.cs` (add stub), `scripts/HeadlessViews.cs` (add stub), `scripts/HexMapView.cs` (add `PanCameraTo(Vector2 worldPos)` if no equivalent exists), `scripts/IHexMapView.cs` (add), `scripts/BuildPane.cs` (extend anchor picker), `scripts/PreviewPane.cs` (extend), `scripts/Tutorial/TutorialPlayer.cs` (extend).
- **Manual test:** Author HUD-anchored Prompt (e.g., on End Turn), Region Highlight (3 tiles), centered Prompt (None anchor), CameraFocus pan; Preview each.
- **Depends on:** Phase 8.
- **Spec reference:** Spec §"Data model" (CameraFocusBeat, AnchorRef variants).

### Phase 10 — Multi-turn timeline + AI ghost beats

- **Status:** ⏳ Not yet expanded
- **Goal:** Upgrade BuildPane timeline to turn-lane layout grouped by `(Turn, Actor)`; ghost beats appear in AI lanes (computed at preview time, not authored — they're whatever `AiSimulator.ChooseForCurrentPlayer` would return given the current state-at-beat-N); `TutorialPlayer.AiChooser` activates the scripted-beat-for-AI-actor path (returns next scripted beat as `AiAction`, falls back to `AiDispatcher` when no scripted beat for the current AI player); `TutorialAi.cs` left as the passive stub for the un-bound case (Play Tutorial without TutorialPlayer wired in).
- **Key files:** `scripts/BuildPane.cs` (timeline layout), `scripts/Tutorial/TutorialPlayer.cs` (extend `AiChooser`), `tests/TutorialPlayerTests.cs` (extend with scripted-AI dispatch + fallthrough tests).
- **Manual test:** Author a tutorial with actor 0 (human) + actor 1 (AI) alternating turns. In Build, ghost beats appear in actor 1's lane. In Preview, AI takes its turn auto.
- **Depends on:** Phase 9.
- **Spec reference:** Spec §"Beat pointer advancement" (AI player-action beats), §"AI auto-resolution" notes.

### Phase 11 — Beat editing in Build (inspector / reorder / delete / right-click)

- **Status:** ⏳ Not yet expanded
- **Goal:** Click beat → inspector populates with kind-specific fields; edits commit on blur or ENTER; reorder via `[`/`]` keys or drag within lane; delete via `⌫` or right-click → "Delete beat" (confirms); right-click context menu (Edit, Re-anchor, Duplicate `⌘D`, Move ↑/↓ `[/]`, Delete `⌫`); after every mutation, the BuildPane state-after-beat-N cache is invalidated from that index onward.
- **Key files:** `scripts/BuildPane.cs` (inspector + context menu + reorder), `scripts/Tutorial/Tutorial.cs` (mutation helpers if needed), `tests/TutorialMutationTests.cs` (new — beat insert/edit/reorder/delete update beat indices correctly).
- **Manual test:** Author a 5-beat tutorial; click each beat → inspector reflects fields; edit a body, see Preview reflect; reorder via `[`; delete via `⌫`.
- **Depends on:** Phase 10.
- **Spec reference:** Spec §"Build mode" (Authoring paragraphs), §"Beat editing" (S6 from wireframes).

### Phase 12 — Validation banners + orphan handling

- **Status:** ⏳ Not yet expanded
- **Goal:** `TutorialValidator.Validate(Tutorial, GameState startingState) → IReadOnlyList<ValidationIssue>` runs after every Build mutation and after every Map Edit commit. Errors → red banner + block save; warnings → yellow flag on offending beat in timeline. Rules: see spec §"Validation".
- **Key files:** `scripts/Tutorial/TutorialValidator.cs` (extend), `scripts/Tutorial/ValidationIssue.cs` (new), `scripts/BuildPane.cs` (banner + yellow beat flag), `scripts/MapEditorPanel.cs` (fire `DraftChanged` so BuildPane revalidates), `tests/TutorialValidatorTests.cs` (new — every rule).
- **Manual test:** Author a tutorial; switch to Map Edit; remove a tile referenced by a Prompt anchor; switch back to Build → yellow flag on that beat. Author a turn lane with no player-action → red banner blocks save.
- **Depends on:** Phase 11.
- **Spec reference:** Spec §"Validation".

### Phase 13 — Scrubber + per-beat snapshots in Preview

- **Status:** ⏳ Not yet expanded
- **Goal:** New `TutorialScrubber : Control` (CanvasLayer, bottom-anchored, mounted only when `OS.IsDebugBuild()`); tick rail with one tick per beat (current beat = "major"); drag head + ghost head; Play/Pause/Step (`SPACE`/`←`/`→`)/Restart (`HOME`); speed selector (0.5×/1×/2×, applies only to AI-actor and overlay beats). `TutorialPlayer.Snapshots` populated per-beat (from Phase 3, becomes load-bearing here). PreviewPane wires scrubber events; `OnSteppedTo(int n)` reconstructs from clone + applies snapshot N + resets `CurrentBeatIndex`.
- **Key files:** `scripts/TutorialScrubber.cs` (new, partial Control), `scripts/PreviewPane.cs` (extend with scrubber wiring), `scripts/Tutorial/TutorialPlayer.cs` (extend rewind API if needed), `tests/TutorialPlayerTests.cs` (extend with snapshot push timing + rewind tests).
- **Manual test:** Author a 4-beat tutorial; in Preview, scrub head backward to beat 1 → state rewinds; press → → step forward; SPACE → resume play.
- **Depends on:** Phase 12.
- **Spec reference:** Spec §"Scrubber".

### Phase 14 — Polish

- **Status:** ⏳ Not yet expanded
- **Goal:** All keyboard shortcuts (`A`/`T`/`H`/`F`/`⌘D`/`[`/`]`/`⌫`/`HOME`/`SHIFT+drag`/`SPACE`); breadcrumb (S8: ±5 beats around playhead, undone beats at 0.45 opacity); auto-pause at last beat in Preview; ESC ladders out (1st cancels current placement, 2nd drops to Map Edit, 3rd exits scene); cross-cutting edge cases from S1-S9 spec panels (cross-lane reorder rejected, identical Src/Dst rejected). `ARCHITECTURE.md` gets the full Tutorial system section.
- **Key files:** `scripts/TutorialBuilderScene.cs` (kbd shortcuts + ESC ladder), `scripts/BuildPane.cs` (kbd + edge cases), `scripts/PreviewPane.cs` (auto-pause + breadcrumb), `scripts/TutorialScrubber.cs` (HOME, SPACE), `ARCHITECTURE.md` (Tutorial section).
- **Manual test:** Run through every keyboard shortcut; verify auto-pause; ESC ladder works.
- **Depends on:** Phase 13.
- **Spec reference:** Spec §"Phases" → Phase 14 entry.

---

## Cross-cutting reminders for every phase

- **Rebuild before launch:** After any `.cs` change, `dotnet build FourExHex.csproj` before `/Applications/Godot_mono.app/Contents/MacOS/Godot --path .`.
- **Strict TDD on logic changes:** Write tests first, RUN them, SEE them fail for the right reason, THEN implement. Refactors don't need new tests.
- **Manual test before push:** Per `CLAUDE.md`, the user must confirm the manual test passes before the phase is considered done.
- **Architecture-doc-before-push rule:** When pushing, ask whether `ARCHITECTURE.md` should be updated. Phase 14 batches this for all new architecture; earlier phases may incrementally update.
- **Tutorial-related tests must be added to `tests/FourExHex.Tests.csproj`'s `<Compile Include>` list** — the test csproj explicitly enumerates each production file it pulls in.
- **`.cs.uid` files**: Godot generates these for each new `.cs` script under `scripts/`. They must be staged and committed alongside the script. Don't delete them.
