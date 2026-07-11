# Campaign winnable-seed pipeline

Every campaign level must be provably winnable on basic (Soldier) difficulty.
The proof: an all-AI game of the level — every active slot a Soldier
`Computer`, identical map/roster/mode to a real launch — in which the level's
hash-assigned human slot (`CampaignProgress.HumanColorSlotForLevel`) wins.
`CampaignProgress.SeedForLevel(level)` returns a per-level seed from the baked
table in `src/FourExHex.Model/CampaignSeeds.cs`; every entry carries such a
proof. Where possible the seed is the level number itself (the level's
original map); levels whose original map loses or stalls carry a searched
replacement seed.

Both halves of the pipeline are env-gated xUnit facts in
`tests/CampaignWinnerSweepTests.cs` (plain `dotnet test` skips them, <1ms):

- **Verify** (`CampaignWinnerSweep_ReportDistribution`) — plays each level on
  its current `SeedForLevel` seed and reports who won.
- **Regenerate** (`CampaignSeedSearch_GenerateWinnableSeedTable`) — walks each
  level's deterministic candidate-seed sequence (attempt 0 = the level number,
  so an already-winnable level keeps its map byte-identically; later attempts
  avalanche-hash across the full 32-bit range) and records the first seed
  whose game the human slot wins.

## When to re-run

- **Any change that can shift game outcomes** — rules, AI scoring/behavior,
  map generation, economy/upkeep, win conditions — silently invalidates the
  proofs. Run **verify**; re-run **search** for whatever fails.
- **A change to a specific level's identity** — e.g. assigning a game mode to
  a level, changing its densities/roster derivation — invalidates exactly
  those levels. Re-run **search** for that range, then **verify** it.

## Environment variables

| Var | Meaning |
|---|---|
| `FOUREXHEX_CAMPAIGN_SWEEP=1` | run the verify sweep |
| `FOUREXHEX_CAMPAIGN_SEED_SEARCH=1` | run the seed search |
| `FOUREXHEX_SWEEP_LEVELS=a-b` (or `n`) | level range, default `0-255` |
| `FOUREXHEX_SWEEP_OUT=path` | CSV output path (default under `$TMPDIR`); summary at `path.summary.txt`, search table at `path.table.cs.txt`, live progress at `path.progress` |
| `FOUREXHEX_SEARCH_MAX_ATTEMPTS=n` | per-level search cutoff, default 128 |

## Recipes

Verify all levels (~20 min on 8 cores):

```
FOUREXHEX_CAMPAIGN_SWEEP=1 FOUREXHEX_SWEEP_OUT=/tmp/sweep.csv \
  dotnet test tests/FourExHex.Tests.csproj --filter FullyQualifiedName~CampaignWinnerSweep
```

Acceptance in `/tmp/sweep.csv.summary.txt`: `Hash-assigned human slot won:
256/256 (100%)` and `DidNotResolve … : 0`. Anything else lists the failing
levels — re-search those.

Regenerate seeds for a range (a single level: `FOUREXHEX_SWEEP_LEVELS=57`):

```
FOUREXHEX_CAMPAIGN_SEED_SEARCH=1 FOUREXHEX_SWEEP_LEVELS=0-255 FOUREXHEX_SWEEP_OUT=/tmp/search.csv \
  dotnet test tests/FourExHex.Tests.csproj --filter FullyQualifiedName~CampaignSeedSearch
```

The full 256-level search is **hours** (roughly 4–6 games per level; losing
candidates that stall cost 500 turns each). It is kill-safe: each level's row
appends to the CSV as it concludes, and rerunning with the **same**
`FOUREXHEX_SWEEP_OUT` resumes past levels already on disk. Corollary: when
the goal is *re*-seeding after a change, point `FOUREXHEX_SWEEP_OUT` at a
**fresh path** — resuming from a stale CSV would skip the levels you meant to
redo. `Exhausted N attempts` in the summary flags levels that found no
winnable seed within the cutoff (raise `FOUREXHEX_SEARCH_MAX_ATTEMPTS` or
investigate the level's config).

Bake the results:

1. Take `<out>.table.cs.txt` (a 256-entry C# initializer; for a partial-range
   search, splice just the changed levels' entries) and update the array in
   `src/FourExHex.Model/CampaignSeeds.cs`.
2. `dotnet build && dotnet test` — unit tests pin table shape and known
   identity-kept/re-seeded levels.
3. Run **verify** and confirm the acceptance line.

## Fidelity and caveats

- The runner is faithful to a real campaign launch: same seed →
  `ProceduralGame.Build` on the 30×20 grid, level-derived
  `MapGenOptionsForLevel` / `ModeForLevel` / sorted active-slot roster,
  randomized capital selection, and the seed also drives the in-game turn
  RNG. Map generation and capital placement never read `Player.Kind`, so the
  human→Computer substitution cannot perturb the board.
- Everything is deterministic: same level + seed → same outcome, so the
  proofs are reproducible and the search/sweep runs are restartable.
- The winner is an **AI proxy at Soldier**: proof means "a baseline AI wins
  from the human's slot," a strong indicator — not a guarantee — that a human
  can. Tier handicaps (Captain/Commander purchase costs) are deliberately
  absent: the table proves map fairness, not the difficulty curve.
- **Fog Of War levels**: AIs plan with full vision (fog blinds only the human
  view), so a fog level's proof is "winnable with full vision" — weaker than
  the freeform/tides proof.
- **Viking Raiders levels**: no win can fire while any viking threat remains
  (`VikingRaidersRules.ThreatRemains`), so a proof game must survive all six
  waves, clear the board of raiders, and then win the ordinary endgame —
  expect longer games and higher attempt counts. Viking levels clamp their
  clumping draw to ≥90 (`MapGenOptionsForLevel`); fragmented starts make the
  mode near-unwinnable for the AI at any seed.
- Games are capped at 500 turns (`DidNotResolve` / stasis). A capped game is
  treated as a loss by the search.
