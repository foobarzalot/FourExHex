# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

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
- **Run unit tests**: `dotnet test` — runs the xUnit tests in `tests/FourExHex.Tests.csproj`
- **Run the game headless**: `/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path .`
- **Open editor**: `open /Applications/Godot_mono.app --args --path $(pwd)`

**Rebuild-before-launch rule**: Godot does NOT always rebuild the C# assembly when launching the game. After editing any `.cs` file, run `dotnet build FourExHex.csproj` before relaunching or you'll be running stale code.

**Manual-test-after-every-change rule**: The user is not running the Godot editor. After any change whose unit tests pass, rebuild and launch the game yourself (`/Applications/Godot_mono.app/Contents/MacOS/Godot --path . res://scenes/main.tscn` in the background), then wait for the user to confirm the feature works before moving on or pushing. Unit tests don't cover the view layer (`Main`, `HexMapView`, `HudView` are excluded), so passing tests aren't enough evidence that a visual/interaction change actually works.

## Code Style

- `PascalCase` for C# classes, methods, properties, and Godot node names
- `camelCase` for local variables and parameters
- `_camelCase` for private fields
- Nullable reference types enabled — annotate intent explicitly
- Views expose plain C# `event Action<...>` (see `IHexMapView` / `IHudView`), NOT Godot `[Signal]` delegates. This keeps `GameController` pure C# and unit-testable with mocks.
- Type everything; avoid `var` when the type isn't obvious from the right-hand side

## Architecture rules (do not violate)

- **Views never mutate the model.** `HexMapView` / `HudView` only read `GameState` and render; they don't write to it.
- **`GameController` never touches Godot `Node`s directly.** It talks to views through `IHexMapView` / `IHudView` only. This is what makes `GameControllerTests` possible.
- **Every state change funnels through `RefreshViews()`** at the end of the handler — one UI update path, no drift.
- **`SessionState` never enters a snapshot.** Only `GameState` does, so undo/redo can't resurrect UI artifacts.
- Pure rules (`MovementRules`, `PurchaseRules`, `TerritoryFinder`, `CapitalReconciler`, `DefenseRules`, `TreeRules`, `UpkeepRules`, `WinConditionRules`) are static and Godot-free. Keep them that way.

## Project Structure

- `scenes/` — `.tscn` scene files; `main.tscn` is the entry point
- `scripts/` — C# sources. Godot `Node` subclasses (`Main`, `HexMapView`, `HudView`) are `partial` so the Godot source generator can extend them; everything else is plain C# (structs, static rule classes, POCOs, `GameController`).
- `tests/` — xUnit test project (`FourExHex.Tests.csproj`), standalone `Microsoft.NET.Sdk`, excluded from the main assembly via `DefaultItemExcludes` and from Godot's resource scanner via a `.gdignore`. Test files pull in production sources with `<Compile Include="..\scripts\*.cs">`. `Main.cs`, `HexMapView.cs`, and `HudView.cs` are NOT compiled into the test assembly — they derive from Godot nodes and need the Godot source generator. Use `MockHexMapView` / `MockHudView` to test controller flows.
- `.godot/` — engine cache and generated build artifacts (gitignored)
- `bin/`, `obj/` — .NET build artifacts (gitignored)

## Notes

- `project.godot` pins the main scene to `res://scenes/main.tscn`. If that scene is deleted or renamed, update `application/run/main_scene` or Godot will fail to launch.
- The Godot editor may rewrite `FourExHex.csproj` and `FourExHex.sln` on first open to match its exact SDK version — this is expected; commit the changes.
