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
- **Run a single test file / class**: `dotnet test --filter FullyQualifiedName~HeuristicAiTests` (substring match on the fully qualified test name)
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

**Manual-test-after-every-change rule**: The user is not running the Godot editor. After any change whose unit tests pass, rebuild and launch the game yourself, then wait for the user to confirm the feature works before moving on or pushing. Unit tests don't cover the view layer (`Main`, `HexMapView`, `HudView`, `MainMenuScene` are excluded), so passing tests aren't enough evidence that a visual/interaction change actually works.

## Diagnostic AI-stress mode (`FOUREXHEX_6AI`)

Setting the env var `FOUREXHEX_6AI` before launching Godot reconfigures the session for a fully headless regression run:

- All six player slots are forced to `AiKind.Heuristic` (bypassing the main menu).
- `AiLog.Enabled = true` so every AI decision prints to stdout (routed via `GD.Print`).
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
- **Snapshots capture `GameState` plus the player-intent slice of `SessionState`** (`SelectedTerritory` anchor, `Mode`, `MoveSource`). `Winner` and the `Undo` stack itself stay out. Top-level human event handlers (`OnTileClicked`, `OnBuyPressed`, `OnBuildTowerPressed`, `OnCancelActionPressed`, `OnNextTerritoryPressed`) are wrapped in `TrackHandler` which captures pre-state, runs the body, and pushes a single `UndoEntry` iff state actually changed — de-dups no-op clicks (e.g. Buy Peasant when already in BuyingPeasant and only peasant is affordable). Exceptions inside a handler propagate; no push happens (a half-mutated state means the controller's invariants are broken — crash, don't paper over).
- Pure rules (`MovementRules`, `PurchaseRules`, `TerritoryFinder`, `CapitalPlacer`, `CapitalReconciler`, `DefenseRules`, `TreeRules`, `UpkeepRules`, `WinConditionRules`) are static and Godot-free. Keep them that way.

## AI subsystem

- `GameController` takes an injected `aiChooser: Func<GameState, Color, HashSet<HexCoord>, Random, AiAction?>` and an `IAiPacer`. `Main` wires the chooser to `AiDispatcher.ChooseForCurrentPlayer`, which routes to `RandomAi` or `HeuristicAi` based on `Player.Kind`.
- `AiCommon.Enumerate` is the single source of legal candidate actions; both AIs consume it. Only this helper knows about rule legality — the AIs own the "which candidate?" decision.
- `AiSimulator.Clone` + `AiStateScorer.Score` back `HeuristicAi`'s 1-ply lookahead. `AiSimulator` mirrors the mutation logic in `GameController`'s `ExecuteAi*` paths; if you add a new AI-capable action, update both in lockstep or simulated scoring will drift from real play.
- AI turn pacing is split into preview/execute beats (see the `AiPreviewDelayMs` / `AiActionDelayMs` / `AiBetweenPlayersDelayMs` constants in `GameController`) so humans can see which territory is acting. Tests use `SynchronousAiPacer` and observe all effects inline.
- `AiLog.Print` is off by default. Enable it (via `FOUREXHEX_6AI` or by setting `AiLog.Enabled` in a scratch test) when debugging AI choices.

## Project Structure

- `scenes/` — `.tscn` scene files. `main_menu.tscn` is the launched scene (see `project.godot`); `main.tscn` is the in-game scene the menu swaps to.
- `scripts/` — C# sources. Godot `Node` subclasses (`Main`, `HexMapView`, `HudView`, `MainMenuScene`) are `partial` so the Godot source generator can extend them; everything else is plain C# (structs, static rule classes, POCOs, `GameController`, AI helpers, headless view stubs).
- `tests/` — xUnit test project (`FourExHex.Tests.csproj`), standalone `Microsoft.NET.Sdk`, excluded from the main assembly via `DefaultItemExcludes` and from Godot's resource scanner via a `.gdignore`. Test files pull in production sources with `<Compile Include="..\scripts\*.cs" />` — **the test csproj explicitly lists each production file it includes**, so when you add a new testable source file you must add a matching `<Compile Include>` entry or tests won't see it. `Main.cs`, `HexMapView.cs`, `HudView.cs`, `MainMenuScene.cs`, `GodotAiPacer.cs`, and `HeadlessViews.cs` are NOT compiled into the test assembly — they derive from Godot nodes or depend on `SceneTree`. Use `MockHexMapView` / `MockHudView` + `SynchronousAiPacer` to test controller flows.
- `.godot/` — engine cache and generated build artifacts (gitignored)
- `bin/`, `obj/` — .NET build artifacts (gitignored)

## Notes

- `project.godot` pins the main scene to `res://scenes/main_menu.tscn`. If that scene is deleted or renamed, update `application/run/main_scene` or Godot will fail to launch. The in-game scene (`main.tscn`) is reached via `GetTree().ChangeSceneToFile` from the menu — `Main.cs` is its root.
- The Godot editor may rewrite `FourExHex.csproj` and `FourExHex.sln` on first open to match its exact SDK version — this is expected; commit the changes.
- `.cs.uid` files (sibling to each `.cs` script) are Godot's stable resource IDs — they ARE tracked in git and must be staged/committed alongside new or moved C# scripts. They are not build artifacts; deleting them breaks scene/script references.
