# Level-design harness

Headless authoring + playtesting of **starting maps** — the tool loop that lets an
AI agent (or a human at a terminal) design levels without Godot. The CLI in
`tools/FourExHex.LevelDesigner/` is a thin shell over the same Model/Controller
code the game runs: edits route through `MapEditPaint` (via `LevelWorkspace`),
validation is `MapRosterRules.ValidateForSave`, saves are `SaveSerializer`
starting-map JSON, and playtests run real `GameController` games in-process
(`LevelPlaytest`, the DeterminismProbe pattern).

Output maps are ordinary files in the game's `user://maps/` directory
(`~/Library/Application Support/Godot/app_userdata/FourExHex/maps/` on macOS) —
they appear in **Play Game → Load Starting Map** and open in the in-game map
editor immediately.

## Commands

```
dotnet run --project tools/FourExHex.LevelDesigner -- <command> <map-name> [options]
```

| Command | Purpose |
|---|---|
| `new NAME --cols 30 --rows 20 [--mode M]` | blank all-water canvas |
| `new NAME --gen SEED [--players N --trees N --mountains N --gold N --neutral N --clump N]` | procedural starting point |
| `show NAME` | board + roster + validation status |
| `edit NAME <ops...>` or `--script FILE` | apply paint ops, save, reprint |
| `roster NAME --slot I=KIND[:DIFFICULTY] ...` | set slot kinds/difficulties |
| `validate NAME` | problems or `OK` (exit 2 if invalid) |
| `playtest NAME --games N --seed S [--max-turns M]` | headless AI-vs-AI metrics |

`--dir PATH` on any command overrides the maps directory (useful for scratch
work). `FOUREXHEX_LOG="LevelDesign:Debug"` traces every op; `LevelDesign:Info`
logs one line per playtest game.

### Edit ops

Coordinates are `COL,ROW` offsets exactly as labeled on the rendered board.

```
land SLOT C,R ...      paint land for slot 0-5 (accepts: rect C,R C,R)
neutral C,R ...        unowned land            (accepts: rect)
water C,R ...          back to water           (accepts: rect)
capital C,R            move the territory's capital to this tile
tree|tower|gold|mountain C,R ...   toggle that feature/occupant
```

Painting land for a dormant slot auto-activates it as `Computer:Soldier`; an
explicit `roster` choice is never overridden. Capitals are placed automatically
whenever a 2+ tile region forms — use `capital` only to move one deliberately.

### Board text

Each cell is two characters, owner then mark: owner `0-5` = slot, `.` = neutral
land, `~` = water; mark `*` capital, `t` tower, `T` tree, `x` grave, `1-4` unit
level, `$` gold, `^` mountain, `.` plain. Odd rows are indented half a cell
(odd-r hex layout: odd rows sit half a hex right; a cell's neighbors on the
rows above/below are the two cells its position straddles).

## The design loop

1. **Ideate.** State a design intent in the map vocabulary: board shape and
   water, per-slot land regions and capital positions, neutral buffer zones,
   terrain (trees delay income, gold pays 5×, mountains defend), roster
   (2-6 slots, difficulties), and game mode (`Freeform`, `RisingTides`,
   `FogOfWar`, `VikingRaiders`). Anything outside that vocabulary (new rules,
   scripted events) is a game feature, not a level.
2. **Express.** `new`, then `edit` with rect blocks first and detail passes
   after. Re-`show` between passes — always look at the board you actually
   produced, not the one you intended.
3. **Validate.** Must be `OK` before anything else matters: every active slot
   owns land and a capital, every landowner is active, ≥2 slots active.
4. **Playtest.** `playtest NAME --games 10 --seed 42`. Read the metrics against
   the design intent (targets below).
5. **Iterate** edits until the metrics match the intent, then hand off for
   **human review**: the map plays via Load Starting Map (set the human's slot
   with `roster NAME --slot 0=Human` first) and opens in the map editor. The
   metrics measure balance and pacing proxies — whether the shape creates
   interesting decisions is judged by a person.

## Playtest metrics

Every rostered slot plays as Computer (baked difficulty kept — a Human slot is
substituted, same as the campaign winnable sweep). Per game and aggregated:

- **winners** — who wins across the batch, plus `unresolved` (turn cap hit).
- **length** — final turn number.
- **decided turn** — first turn of the winner's unbroken final land lead;
  later = the game stayed contested longer.
- **lead changes** — strict land-leader handoffs across turns.
- **closeness** — runner-up land as % of winner land at game end.
- **eliminated slot N** — games and median turn of each elimination.

Suggested targets for a "fair fight" level: no slot winning ~100% of games,
decided turn well past the midpoint, no eliminations in the opening turns,
closeness comfortably above zero. Deliberately asymmetric levels (a doomed
underdog scenario, a king-of-the-hill gold rush) should instead *confirm* their
intended asymmetry — the metrics verify intent, they don't impose one shape.

**Seed caveat:** on a fixed map, `ComputerAi` currently consumes no randomness
when choosing actions, so games often play out identically across seeds
(seed-driven variance enters only through in-game capital-merge tie-breaks and
mode randomness like Rising Tides). Identical per-seed rows are normal — treat
a multi-seed batch as a cheap invariance check, not a distribution, and prefer
varying the *map* (or difficulty mix) to probe robustness.

Determinism: same map + same seeds → byte-identical report.

## Shipping a map in the build

Maps committed to the repo's `maps/` directory (= `res://maps/`) ship inside
the game. To promote a finished map:

1. Copy its JSON into `maps/<name>.json` (final roster baked — usually
   `roster NAME --slot 0=Human` first) and verify:
   `dotnet run --project tools/FourExHex.LevelDesigner -- validate <name> --dir maps`.
2. Add `<name>` to `StartingMapCatalog.Names`
   (`src/FourExHex.Model/StartingMapCatalog.cs`). The catalog, not a directory
   scan, defines the shipped set — res:// listing is unreliable in exported
   builds.

Bundled maps appear in Play Game → Load Starting Map tagged "built-in"; a
user-saved map with the same name shadows the bundled one everywhere
(listing, loading, thumbnail). The export presets already ship `maps/*.json`
on all platforms. The map editor's Load Map lists user maps only — bundled
maps are read-only.

## Layer map

- `src/FourExHex.Model/LevelWorkspace.cs` — authoring session (blank/procedural
  init, paint ops threading `MapEditPaint`'s territory list, roster, validate,
  starting-map JSON round-trip).
- `src/FourExHex.Model/MapTextRenderer.cs` — the ASCII board.
- `src/FourExHex.Controller/LevelPlaytest.cs` — playtest runner, integer-only
  fun-proxy metrics (`PlaytestMetrics`), recording HUD + null map view.
- `tools/FourExHex.LevelDesigner/Program.cs` — argument parsing and file I/O
  only; no game logic.
