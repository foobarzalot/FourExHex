#!/usr/bin/env bash
#
# tree_sweep.sh — treepocalypse × map-settings correlation sweep (issue #100).
#
# Launches the headless 6AI diagnostic harness once per (mode, clump, density,
# seed) cell, capturing only the [tree-census] and GAME OVER lines from each
# game, then reduces them to two CSVs:
#   census.csv  — one row per emitted census line (the growth curve)
#   summary.csv — one row per game (terminal incidence + outcome)
#
# The 6AI harness pins Ai/Turn/Capture/Tree verbose and can't be silenced, so
# we grep at the pipe and discard the rest to keep disk sane over a long matrix.
# One game per process (no in-process batch) — this script is the batch runner.
#
# Usage:
#   tools/tree_sweep.sh                       # full-mode grid from the plan
#   TREE_SWEEP_MODE=quick \                   # fast proof-of-concept
#     TREE_SWEEP_CLUMPS="0 100" \
#     TREE_SWEEP_MODES="Freeform RisingTides" \
#     TREE_SWEEP_DENSITIES=10 TREE_SWEEP_SEEDS=42 \
#     tools/tree_sweep.sh
#
# Env knobs (all optional):
#   TREE_SWEEP_MODE       quick|full            (default full — 30x20, 500 turns)
#   TREE_SWEEP_DENSITIES  space-separated ints  (default "5 15")   tree density
#   TREE_SWEEP_CLUMPS     space-separated ints  (default "0 50 100")
#   TREE_SWEEP_MODES      Freeform|RisingTides  (default "Freeform RisingTides")
#   TREE_SWEEP_MTNS       space-separated ints  (default "0")      mountain density
#   TREE_SWEEP_GOLDS      space-separated ints  (default "0")      gold density
#   TREE_SWEEP_SEEDS      space-separated ints  (default "42 101 777")
#
# Mountains and gold are the only neutral (PlayerId.None) tiles; trees spread
# onto both at runtime, so a non-zero MTN/GOLD density is what exercises the
# `neutral` census column (via the None phantom growth turn). The default 0/0
# keeps the primary tree×clump×mode grid unchanged.
#   TREE_SWEEP_OUT        output dir            (default /tmp/tree_sweep_<ts>)
#   GODOT                 Godot binary path     (default the mono app bundle)
#
set -euo pipefail

GODOT="${GODOT:-/Applications/Godot_mono.app/Contents/MacOS/Godot}"
PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SWEEP_MODE="${TREE_SWEEP_MODE:-full}"

IFS=' ' read -r -a DENSITIES <<< "${TREE_SWEEP_DENSITIES:-5 15}"
IFS=' ' read -r -a CLUMPS    <<< "${TREE_SWEEP_CLUMPS:-0 50 100}"
IFS=' ' read -r -a MODES     <<< "${TREE_SWEEP_MODES:-Freeform RisingTides}"
IFS=' ' read -r -a MTNS      <<< "${TREE_SWEEP_MTNS:-0}"
IFS=' ' read -r -a GOLDS     <<< "${TREE_SWEEP_GOLDS:-0}"
IFS=' ' read -r -a SEEDS     <<< "${TREE_SWEEP_SEEDS:-42 101 777}"

OUT="${TREE_SWEEP_OUT:-/tmp/tree_sweep_$(date +%Y%m%d_%H%M%S)}"
mkdir -p "$OUT/runs"

case "$SWEEP_MODE" in
  quick) DIAG_VAR=FOUREXHEX_6AI_QUICK ;;
  full)  DIAG_VAR=FOUREXHEX_6AI ;;
  *) echo "tree_sweep: bad TREE_SWEEP_MODE='$SWEEP_MODE' (want quick|full)" >&2; exit 1 ;;
esac

echo "[sweep] mode=$SWEEP_MODE out=$OUT"
echo "[sweep] rebuilding game assembly (stale C# would run silently otherwise)..."
dotnet build "$PROJECT_DIR/FourExHex.csproj" -v quiet >/dev/null

total=$(( ${#DENSITIES[@]} * ${#CLUMPS[@]} * ${#MODES[@]} * ${#MTNS[@]} * ${#GOLDS[@]} * ${#SEEDS[@]} ))
i=0
for gm in "${MODES[@]}"; do
  for cl in "${CLUMPS[@]}"; do
    for de in "${DENSITIES[@]}"; do
      for mt in "${MTNS[@]}"; do
        for go in "${GOLDS[@]}"; do
          for sd in "${SEEDS[@]}"; do
            i=$((i + 1))
            tag="${gm}_clump${cl}_tree${de}_mtn${mt}_gold${go}_seed${sd}"
            runlog="$OUT/runs/${tag}.log"
            printf '[sweep] (%d/%d) %s ... ' "$i" "$total" "$tag"
            env "$DIAG_VAR=1" \
                FOUREXHEX_SEED="$sd" \
                FOUREXHEX_TREE_DENSITY="$de" \
                FOUREXHEX_CLUMP_FACTOR="$cl" \
                FOUREXHEX_MODE="$gm" \
                FOUREXHEX_MTN_DENSITY="$mt" \
                FOUREXHEX_GOLD_DENSITY="$go" \
                "$GODOT" --headless --path "$PROJECT_DIR" 2>&1 \
              | grep -E '\[tree-census\]|GAME OVER' > "$runlog" || true
            printf '%s census lines\n' "$(grep -c '\[tree-census\]' "$runlog" || echo 0)"
          done
        done
      done
    done
  done
done

echo "[sweep] reducing $total run logs -> CSVs ..."
python3 "$PROJECT_DIR/tools/tree_sweep_reduce.py" "$OUT"

echo "[sweep] done -> $OUT/summary.csv  $OUT/census.csv"
