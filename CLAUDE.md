# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

FourExHex is a hex-based 4X strategy game built with Godot 4.6 and C#.

## Tech Stack

- **Engine**: Godot 4.6.1 (.NET / mono build) — installed at `/Applications/Godot_mono.app`
- **Language**: C# (not GDScript) — target framework `net8.0`
- **SDK pin**: `Godot.NET.Sdk/4.6.1` (see `FourExHex.csproj`)
- **.NET SDK**: 8.0.x, installed to `$HOME/.dotnet` (not system-wide). Ensure `$HOME/.dotnet` is on `PATH` and `DOTNET_ROOT=$HOME/.dotnet`.

## Commands

- **Headless import / refresh asset cache**:
  `/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path . --import`
- **Build C# assembly**: `dotnet build`
- **Run the game headless**: `/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path .`
- **Open editor**: `open /Applications/Godot_mono.app --args --path $(pwd)`

## Code Style

- `PascalCase` for C# classes, methods, properties, and Godot node names
- `camelCase` for local variables and parameters
- `_camelCase` for private fields
- Nullable reference types enabled — annotate intent explicitly
- Prefer signals (`[Signal]` delegates) over direct cross-node references
- Type everything; avoid `var` when the type isn't obvious from the right-hand side

## Project Structure

- `scenes/` — `.tscn` scene files; `main.tscn` is the entry point
- `scripts/` — C# scripts (one `partial class` per file, matching filename)
- `.godot/` — engine cache and generated build artifacts (gitignored)
- `bin/`, `obj/` — .NET build artifacts (gitignored)

## Notes

- `project.godot` pins the main scene to `res://scenes/main.tscn`. If that scene is deleted or renamed, update `application/run/main_scene` or Godot will fail to launch.
- The Godot editor may rewrite `FourExHex.csproj` and `FourExHex.sln` on first open to match its exact SDK version — this is expected; commit the changes.
