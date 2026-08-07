#!/usr/bin/env bash
#
# view_matrix.sh — view-layer integration sweep: scene navigator × resolution /
# orientation / safe-inset matrix (issue #63).
#
# Launches the real game ONCE per pass — not once per cell. The in-process
# ViewHarness autoload resizes the live window per cell, which fires the real
# SizeChanged reflow; that is the only way to exercise ScreenLayout.IsCompact's
# path-dependent hysteresis in both directions, since its dead-band hold states
# only exist when the same size is reached from two different prior states.
#
# NEVER runs --headless. Godot's headless display server is a stub: the window
# is pinned to 64x64, --resolution is ignored, dpi is hardcoded 96 and the safe
# rect is empty — every layout branch would be measured against fiction. Local
# runs are windowed and visible; CI runs windowed under xvfb.
#
# Two passes, because Strings.Configure(json, isMobile) runs once at boot and
# every built panel holds the resulting text (and the landing panel bakes a
# 7-vs-8 button count): the mobile flag cannot flip mid-process.
#
# Usage:
#   tools/view_matrix.sh                    # both passes, full matrix
#   tools/view_matrix.sh --self-test        # inject a clip; run MUST detect it
#   VIEW_MATRIX_CELLS="square-tie" \
#     VIEW_MATRIX_SCENES=main_menu tools/view_matrix.sh    # ~20s smoke
#
# Env knobs (all optional):
#   VIEW_MATRIX_OUT        output dir           (default /tmp/view_matrix_<ts>)
#   VIEW_MATRIX_CELLS      space-separated cell names      (default: all)
#   VIEW_MATRIX_SCENES     space-separated scene ids       (default: all)
#   VIEW_MATRIX_SKIP_MOBILE_PASS  1 = desktop pass only
#   GODOT                  Godot binary path    (default the mono app bundle)
#
set -euo pipefail

source "$(dirname "${BASH_SOURCE[0]}")/_build_common.sh"

SELF_TEST=0
[[ "${1:-}" == "--self-test" ]] && SELF_TEST=1

OUT="${VIEW_MATRIX_OUT:-/tmp/view_matrix_$(date +%Y%m%d_%H%M%S)}"
mkdir -p "$OUT"

echo "[view-matrix] out=$OUT"
echo "[view-matrix] rebuilding game assembly (stale C# would run silently otherwise)..."
dotnet build "$PROJECT_DIR/FourExHex.csproj" -v quiet >/dev/null

# Targeted categories only. Never *:Trace — LayoutDump.Dump emits a full subtree
# per layout event and would bury the signal.
LOG_SPEC="Layout:Debug,Render:Info,Display:Info"

# Run one pass. $1 is the pass name (its log is $OUT/pass-$1.log); the rest are
# KEY=VALUE overrides exported only for that pass, so the desktop pass can never
# inherit the mobile pass's platform flag.
run_pass() {
  local name="$1"; shift
  local logfile="$OUT/pass-$name.log"
  echo "[view-matrix] pass '$name' -> $logfile"

  (
    export FOUREXHEX_VIEW_MATRIX=1
    export FOUREXHEX_LOG="$LOG_SPEC"
    [[ -n "${VIEW_MATRIX_CELLS:-}"  ]] && export FOUREXHEX_VIEW_CELLS="$VIEW_MATRIX_CELLS"
    [[ -n "${VIEW_MATRIX_SCENES:-}" ]] && export FOUREXHEX_VIEW_SCENES="$VIEW_MATRIX_SCENES"
    ((SELF_TEST)) && export FOUREXHEX_LAYOUT_INJECT_CLIP=1
    local kv
    for kv in "$@"; do export "${kv?}"; done

    # xvfb on Linux/CI (no display); windowed and visible on macOS. The virtual
    # screen is sized for the largest PHYSICAL cell — the mobile-scale cell
    # drives that well past its logical size.
    if [[ -z "${DISPLAY:-}" ]] && command -v xvfb-run >/dev/null 2>&1; then
      LIBGL_ALWAYS_SOFTWARE=1 xvfb-run -a -s "-screen 0 2560x2048x24" \
        "$GODOT" --path "$PROJECT_DIR" --audio-driver Dummy
    else
      "$GODOT" --path "$PROJECT_DIR" --audio-driver Dummy
    fi
  ) > "$logfile" 2>&1 || true

  LOGS+=("$logfile")
}

LOGS=()
run_pass desktop FOUREXHEX_UI_SCALE=1
if [[ "${VIEW_MATRIX_SKIP_MOBILE_PASS:-0}" != "1" ]]; then
  run_pass mobile FOUREXHEX_FAKE_MOBILE=1 FOUREXHEX_UI_SCALE=1
fi

echo
echo "[view-matrix] ---- summary ----"
grep -hE '\[view-matrix\] (SUMMARY|DONE|  skip:)' "${LOGS[@]}" || true

# Two independent verdicts, belt and braces: the harness sets its own exit code,
# and the logs are graded here too, so a crash that killed the process before
# the summary still fails the run.
VIOLATIONS=$(grep -hcE '^(WARN|ERROR) ' "${LOGS[@]}" | paste -sd+ - | bc)
COMPLETED=$(grep -hlE '\[view-matrix\] DONE' "${LOGS[@]}" | wc -l | tr -d ' ')

echo "[view-matrix] warn/error lines: $VIOLATIONS   completed passes: $COMPLETED/${#LOGS[@]}"

if ((SELF_TEST)); then
  # Inverted verdict: the injected clip MUST be reported, or detection is broken
  # and a green matrix run would mean nothing.
  #
  # Correlated against the specific node the injector moved, not just "some
  # overflow exists" — the codebase currently has real violations, so a bare
  # "any WARN" check would pass even with detection completely broken.
  INJECTED=$(grep -hoE 'clip injected into [^(]+' "${LOGS[@]}" \
             | sed -E 's/clip injected into //' | sort -u)
  if [[ -z "$INJECTED" ]]; then
    echo "[view-matrix] SELF-TEST FAIL — injector never fired (no eligible node?)" >&2
    exit 1
  fi

  MATCHED=0
  while IFS= read -r node; do
    [[ -z "$node" ]] && continue
    if grep -hF "$node" "${LOGS[@]}" | grep -qE '^WARN .*OverflowsViewport'; then
      echo "[view-matrix]   detected injected clip in $node"
      MATCHED=1
    fi
  done <<< "$INJECTED"

  if ((MATCHED)); then
    echo "[view-matrix] SELF-TEST PASS — the injected node was reported as overflowing"
    exit 0
  fi
  echo "[view-matrix] SELF-TEST FAIL — injected node(s) were NOT reported:" >&2
  echo "$INJECTED" >&2
  exit 1
fi

if ((COMPLETED < ${#LOGS[@]})); then
  echo "[view-matrix] FAIL — a pass did not reach '[view-matrix] DONE' (crash?)" >&2
  exit 1
fi

if ((VIOLATIONS > 0)); then
  echo "[view-matrix] FAIL — $VIOLATIONS warn/error line(s), distinct:" >&2
  grep -hE '^(WARN|ERROR) ' "${LOGS[@]}" | sort -u | head -40 >&2
  exit 1
fi

echo "[view-matrix] PASS"
