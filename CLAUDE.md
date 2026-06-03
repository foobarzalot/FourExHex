# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Session start

At the very start of a new session — before exploring code, running commands, or making assumptions about what to do — ask the user what they'd like to work on. Keep the prompt to a single line (e.g. "What would you like to work on?"). Skip this only if the user's first message already contains a concrete task.

## Project Overview

FourExHex is a hex-based 4X strategy game built with Godot 4.6 and C#.

**Start here**: `ARCHITECTURE.md` has the current layered view (Main → GameController → views/model/rules), key contracts (`IHexMapView`, `IHudView`), invariants, and call flows (click→select, click→capture, undo, end turn). Read it before making non-trivial changes.

## Tech Stack

- **Engine**: Godot 4.6.1 (.NET / mono build) — installed at `/Applications/Godot_mono.app`
- **Language**: C# (not GDScript) — target framework `net8.0`
- **SDK pin**: `Godot.NET.Sdk/4.6.1` (see `FourExHex.csproj`)
- **.NET SDK**: 8.0.x, installed to `$HOME/.dotnet` (not system-wide). Ensure `$HOME/.dotnet` is on `PATH` and `DOTNET_ROOT=$HOME/.dotnet`.

## Commands

- **Headless import / refresh asset cache**:
  `/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path . --import`
- **Build C# assembly**: `dotnet build FourExHex.csproj` (or `dotnet build` to build the whole solution including tests)
- **Run all unit tests**: `dotnet test` — runs the xUnit tests in `tests/FourExHex.Tests.csproj`
- **Run a single test file / class**: `dotnet test --filter FullyQualifiedName~ComputerAiTests` (substring match on the fully qualified test name)
- **Run the game** (launches the real menu scene pinned in `project.godot`): `/Applications/Godot_mono.app/Contents/MacOS/Godot --path .`
- **Run the game headless**: add `--headless` to the above.
- **Open editor**: `open /Applications/Godot_mono.app --args --path $(pwd)`

**Test-first rule (strict TDD)**: For changes to pure-logic classes (rules, `GameController`, `Treasury`, AI, snapshot/undo), follow the full red-green loop:

1. **Write the test(s) first** — expressing the new intended behavior.
2. **Run them and see them fail** — `dotnet test --filter ...` and confirm the failure is for the expected reason (not a compile error, not a typo). A test that was never seen red proves nothing.
3. **Then change the implementation** to make them pass.
4. **Re-run the full suite** to confirm no regressions.

Show the diff and get buy-in on the plan when rule changes have test fallout (e.g., existing assertions need to flip). Don't silently rewrite tests to match new behavior without flagging what's changing and why. The view-layer exclusions mean tests are the main safety net for logic changes; strict TDD keeps that net honest.

**Rebuild-before-launch rule**: Godot does NOT always rebuild the C# assembly when launching the game. After editing any `.cs` file, run `dotnet build FourExHex.csproj` before relaunching or you'll be running stale code.

**Manual-test-after-every-change rule**: The user is not running the Godot editor. After any change whose unit tests pass, rebuild and launch the game yourself, then wait for the user to confirm the feature works before moving on or pushing. Unit tests don't cover the view layer (`Main`, `HexMapView`, `HudView`, `MainMenuScene` are excluded), so passing tests aren't enough evidence that a visual/interaction change actually works. Every manual test run is followed by a **log-verification step** (see the Instrumentation rule) — launch with the relevant `FOUREXHEX_LOG` categories enabled and read the captured stdout to confirm the code path actually executed and did what you expected. "The window opened and the user didn't complain" is not verification; the logs are.

**Instrumentation rule**: Instrumentation is a permanent, first-class part of this codebase, not scaffolding to rip out once a bug is fixed. It is the primary way the agent inspects the results of its own work in a view layer it cannot otherwise observe — leave it in, and add more.

- **Every plan includes instrumentation.** When planning a change, decide up front what `Log` calls (which `Log.LogCategory`, which level) prove the new code path ran and behaved correctly, and include adding them as an explicit step — same standing as writing tests. A plan with no instrumentation step is incomplete.
- **Use the `Log` system** (`src/FourExHex.Model/Log.cs`): per-category × level, off by default, `Trace`/`Debug`/`Info` are `[Conditional("DEBUG")]` (compile-stripped from Release, so it is free to leave in permanently — see the Logging section in `ARCHITECTURE.md`), `Warn`/`Error` always compile. Never reach for `Console.WriteLine` or `GD.Print` for diagnostics; route through `Log` with the right category so it can be enabled/disabled per subsystem. `GD.PushWarning`/`GD.PushError` remain only for user-facing failure dialogs, not instrumentation.
- **Verify via the logs after manual testing.** The verification step of the manual-test rule is: relaunch with `FOUREXHEX_LOG="<Category>:Debug,..."` (or the headless `FOUREXHEX_6AI` run for AI/turn flows), then read the captured stdout and assert the expected lines are present (the path ran) *and* unrelated categories stayed silent (no leakage / correct gating). Treat this log check as a required deliverable of the change, reported alongside the test results.

**Architecture-doc-before-push rule**: When the user asks to push code changes to the remote, ask first whether they want `ARCHITECTURE.md` updated to reflect those changes — and wait for an answer before pushing. Triggers on any push whose diff (committed or about-to-commit) touches code: `.cs`, `.tscn`, `.csproj`, `project.godot`, etc. Skip the question if the push is documentation-only (`*.md` and similar) — there's no code drift to capture. Don't ask on plain commits, only when the push itself is requested.

**Tech-debt-capture rule**: When you discover a problem in your own work — a flaky test, a known-broken edge case, a shortcut taken to unblock something, a TODO you'd otherwise drop into a comment — and you are not fixing it right now in this change, add an entry to `TECHDEBT.md` instead of leaving it un-tracked. Include enough context that future-you can act on it: file/line, symptom, suspected cause, and candidate fixes. The bar is "did I notice it and choose to defer it" — if yes, it goes in `TECHDEBT.md`. Don't silently move on.

**Issue-tracking rule**: Bugs and features are tracked as GitHub issues on `foobarzalot/FourExHex` via the `gh` CLI. Before planning or implementing any non-trivial fix or feature, prefer to have an associated issue — check `gh issue list` first; if none exists, propose filing one (or ask the user to point at the right ticket) before changing code. The user can override on any given task ("just fix it", "no ticket needed") — when they do, proceed without one and don't keep asking for that task. Available labels: `bug`, `enhancement`, `tech-debt`, `ai`, `ui`, `rules`. Issue body format — bugs: **Context / Repro / Expected / Notes**; features: **Context / Proposed behavior / Acceptance / Notes**; goals-only design tickets (when the solution is still TBD): **Context / Goals / Notes**.

## Diagnostic AI-stress mode (`FOUREXHEX_6AI`)

Setting the env var `FOUREXHEX_6AI` before launching Godot reconfigures the session for a fully headless regression run:

- All six player slots are forced to `PlayerKind.Computer` (bypassing the main menu).
- `Log` is pinned to verbose AI/turn output (`Ai:Debug`, `Turn:Info`, `Capture:Debug`) so every AI decision prints to stdout (routed via `GD.Print`). This is set *after* `Log.Configure(FOUREXHEX_LOG)`, so it can't be silenced by a stray `FOUREXHEX_LOG`.
- `SynchronousAiPacer` replaces `GodotAiPacer` — turns execute inline with no delays.
- `HeadlessHexMapView` / `HeadlessHudView` replace the real views so layout and rendering are skipped.
- `GameController` is constructed with `maxTurnNumber: 500` so stasis runs terminate.
- The scene subscribes to `GameController.GameEnded` and defers `SceneTree.Quit()` so the process exits on game-over.

Typical invocation: `FOUREXHEX_6AI=1 /Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path . 2>&1 | tee /tmp/ai-run.log`. Use this for AI-behavior debugging — do **not** use it as a substitute for the manual-test rule above when the change affects anything a human would see.

## Code Style

- `PascalCase` for C# classes, methods, properties, and Godot node names
- `camelCase` for local variables and parameters
- `_camelCase` for private fields
- Nullable reference types enabled — annotate intent explicitly
- Views expose plain C# `event Action<...>` (see `IHexMapView` / `IHudView`), NOT Godot `[Signal]` delegates. This keeps `GameController` pure C# and unit-testable with mocks.
- Type everything; avoid `var` when the type isn't obvious from the right-hand side
- Prefer DRY: if you find yourself writing the same logic twice, extract a helper (static method on the relevant rules class, or a private method on the controller). The codebase has plenty of small reusable helpers already — `AiCommon.IsBorderTile`, `TerritoryLookup.FindOwnedContaining`, `TestHelpers.BuildRectGrid`, `MockHudView.LastSeenWinner`. Reuse before re-deriving. Watch for it especially when adding new tests, AI scoring terms, or rule predicates — those are where copy-paste creeps in fastest.

## Architecture rules (do not violate)

- **Views never mutate the model.** `HexMapView` / `HudView` only read `GameState` and render; they don't write to it.
- **`GameController` never touches Godot `Node`s directly.** It talks to views through `IHexMapView` / `IHudView`, and to the event loop through `IAiPacer`. This is what makes `GameControllerTests` possible.
- **Every state change funnels through `RefreshViews()`** at the end of the handler — one UI update path, no drift.
- **Snapshots capture `GameState` plus the player-intent slice of `SessionState`** (`SelectedTerritory` anchor, `Mode`, `MoveSource`). `Winner` and the `Undo` stack itself stay out. Top-level human event handlers (`OnTileClicked`, `OnBuyPressed`, `OnBuildTowerPressed`, `OnCancelActionPressed`, `OnNextTerritoryPressed`) are wrapped in `TrackHandler` which captures pre-state, runs the body, and pushes a single `UndoEntry` iff state actually changed — de-dups no-op clicks (e.g. Buy Recruit when already in BuyingRecruit and only recruit is affordable). Exceptions inside a handler propagate; no push happens (a half-mutated state means the controller's invariants are broken — crash, don't paper over).
- Pure rules (`MovementRules`, `PurchaseRules`, `TerritoryFinder`, `CapitalPlacer`, `CapitalReconciler`, `DefenseRules`, `TreeRules`, `UpkeepRules`, `WinConditionRules`) are static and Godot-free. Keep them that way.
- **No circular dependencies.** Across projects the layering is one-way and compiler-enforced: `FourExHex.Model` ← `FourExHex.Controller` ← (`FourExHex` game + `tests`). Model must never gain a reference back to Controller; neither library may reference GodotSharp. The same one-way discipline applies *within* a project: no class A → B → A cycles between rules classes, between AI helpers, between view-side scripts, etc. When two pieces want to call each other, the fix is one of: extract the shared logic into a lower-level helper that both call, invert the dependency with an interface or delegate the upper layer injects down (as `GameController` already does for `aiChooser` / `IAiPacer` / `IHexMapView` / `IHudView`), or merge them if they were never really separable. A new cycle is a design smell — stop and resolve it before continuing.

## AI subsystem

- `GameController` takes an injected `aiChooser: Func<GameState, Color, HashSet<HexCoord>, Random, AiAction?>` and an `IAiPacer`. `Main` wires the chooser to `AiDispatcher.ChooseForCurrentPlayer`, which delegates to `ComputerAi` for a `PlayerKind.Computer` slot and returns null for a `Human` one (based on `Player.Kind`).
- `AiCommon.Enumerate` is the single source of legal candidate actions; `ComputerAi` consumes it. Only this helper knows about rule legality — the AI owns the "which candidate?" decision.
- `AiSimulator.Clone` + `AiStateScorer.Score` back `ComputerAi`'s 1-ply lookahead. `AiSimulator` mirrors the mutation logic in `GameController`'s `ExecuteAi*` paths; if you add a new AI-capable action, update both in lockstep or simulated scoring will drift from real play.
- AI turn pacing is split into preview/execute beats (see the `AiPreviewDelayMs` / `AiActionDelayMs` / `AiBetweenPlayersDelayMs` constants in `GameController`) so humans can see which territory is acting. Tests use `SynchronousAiPacer` and observe all effects inline.
- Logging goes through `Log` (`src/FourExHex.Model/Log.cs`): per-category (`Ai`/`Turn`/`Capture`/…) × level, off by default. `Trace`/`Debug`/`Info` are `[Conditional("DEBUG")]` (stripped from Release); `Warn`/`Error` always compile. Enable via `FOUREXHEX_LOG="Ai:Debug,Turn:Info"`, `FOUREXHEX_6AI`, or `Log.SetLevel(...)` in a scratch test when debugging AI choices.

## Project Structure

- `scenes/` — `.tscn` scene files. `main_menu.tscn` is the launched scene (see `project.godot`); `main.tscn` is the in-game scene the menu swaps to.
- `src/FourExHex.Model/` — the **Godot-free model library** (`FourExHex.Model.csproj`, plain `Microsoft.NET.Sdk`, **no GodotSharp**, **no reference to the controller layer**). Holds the pure model, static rule classes, AI subsystem (incl. `AiDispatcher`), the generic `UndoStack<T>` + `GameStateSnapshot`, save serialization (`SaveSerializer`, `Replay`, `ReplayBeat`, the `Tutorial` POCO), `MapGenerator`/`MapEditPaint`/`EditorSnapshot`, `PlayerId`. It is *physically incapable* of depending on Godot — `using Godot;` will not compile — and cannot name `GameController`/`SessionState`/the view interfaces (a stray reference fails with `CS0246`). New testable model logic goes HERE. `src/` has a `.gdignore` so the editor never scans it.
- `src/FourExHex.Controller/` — the **Godot-free controller library** (`FourExHex.Controller.csproj`, plain `Microsoft.NET.Sdk`, **no GodotSharp**), which `<ProjectReference>`s **only** `FourExHex.Model` (one-way). Holds `GameController` (input handling + AI scheduling), `GameOperations` (the mutation/orchestration helpers shared between live AI and replay; see "GameController ↔ GameOperations split" in `ARCHITECTURE.md`), `ReplayRecorder` (the recording log + paced/instant playback step machines; see "GameController ↔ ReplayRecorder split" in `ARCHITECTURE.md`), the top-level `InstantStep` enum, the UI-scoped `SessionState`/`SessionStateSnapshot`/`UndoEntry`, the `IHexMapView`/`IHudView`/`IAiPacer` view-boundary interfaces, the AI pacers (`AiPacer`/`GodotAiPacer`), and the `Tutorial/` Record/Preview scripting helpers (everything in `Tutorial/` except the `Tutorial` POCO). New testable orchestration logic goes HERE; the test project picks both libraries up automatically.
- `scripts/` — Godot-side C# only: scene roots (`Main`, `MainMenuScene`, `MapEditorScene`, `TutorialBuilderScene`), views (`HexMapView`, `HudView`, editor/tutorial panels), `SaveStore`, `AudioBus`, `SceneTreeTimerFactory`, `HeadlessViews`, and the view-boundary adapters `PlayerPalette` (PlayerId↔Godot.Color) and `HexPixel` (axial↔pixel). `Node` subclasses are `partial` for the Godot source generator.
- `tests/` — xUnit project (`FourExHex.Tests.csproj`, `Microsoft.NET.Sdk`). It `<ProjectReference>`s **both** `src/FourExHex.Model` and `src/FourExHex.Controller` and has **no GodotSharp reference and no `<Compile Include>` list** — a new source file in either library is seen automatically. Godot-side `scripts/` files are NOT testable (they need `Node`/`SceneTree`); test the model logic they call instead. Use `MockHexMapView`/`MockHudView` + `SynchronousAiPacer` for controller flows. That the suite compiles+passes with zero Godot on its graph is the compile-time purity proof.
- `.godot/` — engine cache and generated build artifacts (gitignored)
- `bin/`, `obj/` — .NET build artifacts (gitignored)

## Notes

- `project.godot` pins the main scene to `res://scenes/main_menu.tscn`. If that scene is deleted or renamed, update `application/run/main_scene` or Godot will fail to launch. The in-game scene (`main.tscn`) is reached via `GetTree().ChangeSceneToFile` from the menu — `Main.cs` is its root.
- The Godot editor may rewrite `FourExHex.csproj` and `FourExHex.sln` on first open to match its exact SDK version — this is expected; commit the changes.
- `.cs.uid` files (sibling to each `.cs` script in `scripts/`) are Godot's stable resource IDs — they ARE tracked in git and must be staged/committed alongside new or moved `scripts/` C# files. Files under `src/FourExHex.Model/` and `src/FourExHex.Controller/` are NOT Godot resources and have NO `.cs.uid`. They are not build artifacts; deleting a `scripts/` one breaks scene/script references.
- `FourExHex.Controller` references `FourExHex.Model` one-way; the Godot game (`FourExHex.csproj`) and the test project both `<ProjectReference>` **both** libraries. `FourExHex.csproj` MUST keep `src/**/*` in `DefaultItemExcludes` — the single `src/**` exclude already covers `src/FourExHex.Controller/`; without it the Godot glob also compiles the moved sources and every type is duplicated (CS0436). Model must never gain a reference back to Controller — that one-way edge is the compiler-enforced layering invariant (a violation fails with `CS0246`).
