#!/usr/bin/env python3
"""Renumber the `Index` field of every beat in a tutorial JSON so it equals the
beat's zero-based position (the invariant ReplayBeat documents).

Use after inserting/removing/reordering DisplayText beats by hand: edit the text
freely (the inserted beat's Index value doesn't matter), then run this to fix all
indices in one pass.

Surgical by design: it rewrites ONLY the `"Index": N` lines that appear after the
`"Beats": [` marker (beat indices), leaving every other line — and all
formatting — byte-for-byte unchanged. The Beats array is the last array in the
file and its beats are the only `Index` keys past that marker.

Usage:  tools/renumber_tutorial_beats.py [path]   (default: tutorials/full_tutorial.json)
"""
import re
import sys

INDEX_LINE = re.compile(r'^(\s*)"Index":\s*\d+(\s*,?)\s*$')
BEATS_MARKER = re.compile(r'^\s*"Beats":\s*\[\s*$')


def main() -> int:
    path = sys.argv[1] if len(sys.argv) > 1 else "tutorials/full_tutorial.json"
    with open(path, "r", encoding="utf-8") as f:
        lines = f.readlines()

    start = next((i for i, ln in enumerate(lines) if BEATS_MARKER.match(ln)), None)
    if start is None:
        print(f"ERROR: no '\"Beats\": [' marker found in {path}", file=sys.stderr)
        return 1

    counter = 0
    for i in range(start + 1, len(lines)):
        m = INDEX_LINE.match(lines[i])
        if not m:
            continue
        indent, comma = m.group(1), m.group(2).strip()
        lines[i] = f'{indent}"Index": {counter}{comma}\n'
        counter += 1

    with open(path, "w", encoding="utf-8") as f:
        f.writelines(lines)

    print(f"Renumbered {counter} beats in {path} (0..{counter - 1}).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
