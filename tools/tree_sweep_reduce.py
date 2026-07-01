#!/usr/bin/env python3
"""Reduce tree_sweep run logs into census.csv + summary.csv.

Usage: tree_sweep_reduce.py <out_dir>
Reads <out_dir>/runs/*.log (the grepped [tree-census] + GAME OVER lines from
each game) and writes <out_dir>/census.csv and <out_dir>/summary.csv. Safe to
re-run standalone over an existing sweep dir (e.g. to add a derived column).
"""
import sys, os, re, glob, csv

out = sys.argv[1]
cen_re = re.compile(
    r'\[tree-census\] T(\d+) land=(\d+) trees=(\d+) graves=(\d+) owned=(\d+) neutral=(\d+)')
tag_re = re.compile(
    r'(?P<mode>\w+)_clump(?P<clump>\d+)_tree(?P<density>\d+)'
    r'_mtn(?P<mtn>\d+)_gold(?P<gold>\d+)_seed(?P<seed>\d+)')
go_re = re.compile(r'\[T(\d+)\] GAME OVER')
win_re = re.compile(r'winner:\s*(.+)$')

census_rows, summary_rows = [], []
for path in sorted(glob.glob(os.path.join(out, "runs", "*.log"))):
    tag = os.path.basename(path)[:-4]
    m = tag_re.match(tag)
    if not m:
        continue
    mode, clump, density = m['mode'], m['clump'], m['density']
    mtn, gold, seed = m['mtn'], m['gold'], m['seed']
    step = peak = auc = 0
    last = None
    outcome, winner, last_turn = "incomplete", "-", ""
    with open(path, encoding='utf-8', errors='replace') as f:
        for line in f:
            cm = cen_re.search(line)
            if cm:
                step += 1
                turn, land, trees, graves, owned, neutral = map(int, cm.groups())
                census_rows.append(
                    [mode, clump, density, mtn, gold, seed, step, turn,
                     land, trees, graves, owned, neutral])
                last = (turn, land, trees, graves, owned, neutral)
                peak = max(peak, trees)
                auc += trees
                continue
            gm = go_re.search(line)
            if gm:
                last_turn = gm.group(1)
                if 'stasis' in line:
                    outcome, winner = "stasis", "-"
                else:
                    outcome = "win"
                    wm = win_re.search(line)
                    winner = wm.group(1).strip() if wm else "?"
    if last:
        turn, land, trees, graves, owned, neutral = last
        cov = (trees * 1000) // land if land else 0   # coverage in permille
        turns = int(last_turn) if last_turn else 0
        # Length-normalized tree load: AUC (sum of trees over census samples)
        # divided by turns played. Strips out the game-length inflation that
        # makes a 500-turn stalemate's raw AUC dwarf a short game's.
        per_turn = round(auc / turns, 1) if turns else 0.0
        summary_rows.append(
            [mode, clump, density, mtn, gold, seed, outcome, winner, last_turn,
             land, trees, owned, neutral, cov, peak, auc, per_turn, step])

with open(os.path.join(out, "census.csv"), "w", newline="") as f:
    w = csv.writer(f)
    w.writerow(["mode", "clump", "density", "mtn", "gold", "seed", "step", "turn",
                "land", "trees", "graves", "owned", "neutral"])
    w.writerows(census_rows)

with open(os.path.join(out, "summary.csv"), "w", newline="") as f:
    w = csv.writer(f)
    w.writerow(["mode", "clump", "density", "mtn", "gold", "seed",
                "outcome", "winner", "last_turn",
                "term_land", "term_trees", "term_owned", "term_neutral",
                "coverage_permille", "peak_trees", "auc_trees", "auc_per_turn",
                "census_lines"])
    w.writerows(summary_rows)

print(f"[sweep] wrote {len(census_rows)} census rows, {len(summary_rows)} game summaries")
if summary_rows:
    hdr = ("mode", "clump", "tree", "mtn", "gold", "seed", "outcome", "winner",
           "lastT", "land", "trees", "own", "neut", "cov‰", "peak", "auc", "auc/T")
    print("  " + " ".join(f"{h:>8}" for h in hdr))
    for r in summary_rows:
        cells = [r[0][:8], r[1], r[2], r[3], r[4], r[5], r[6], str(r[7])[:8],
                 r[8], r[9], r[10], r[11], r[12], r[13], r[14], r[15], r[16]]
        print("  " + " ".join(f"{str(c):>8}" for c in cells))
