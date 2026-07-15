#!/usr/bin/env bash
# Print the current state of the Google Play internal-testing track — the
# Android sibling of check_testflight_status.sh.
#
# The track can only be read inside an edit transaction, so this opens a
# throwaway edit, GETs the internal track, and deletes the edit again.
#
# Output on success (exit 0), one line per release on the track:
#   internal: versionCodes=[28] status=completed name=28 (1.0)
# Exit >0 on auth/API errors (usable in poll loops, like the TestFlight one).
#
# Usage:  tools/check_play_status.sh
# Prereqs: service-account JSON (docs/android-play-console-setup.md, step 6).

set -euo pipefail

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PACKAGE="com.foobarzalot.fourexhex"
BASE="https://androidpublisher.googleapis.com/androidpublisher/v3/applications/$PACKAGE"

TOKEN="$("$PROJECT_DIR/tools/play_api.sh" --token)"
auth_curl() { curl -sS -H "Authorization: Bearer $TOKEN" "$@"; }

EDIT_ID=$(auth_curl -X POST "$BASE/edits" \
  | python3 -c 'import json,sys
d = json.load(sys.stdin)
v = d.get("id") or sys.exit("ERROR: could not open edit: " + json.dumps(d))
print(v)')
trap 'auth_curl -X DELETE "$BASE/edits/$EDIT_ID" >/dev/null || true' EXIT

auth_curl "$BASE/edits/$EDIT_ID/tracks/internal" | python3 -c '
import json, sys
d = json.load(sys.stdin)
if "error" in d:
    sys.exit("ERROR: " + json.dumps(d["error"]))
releases = d.get("releases") or []
if not releases:
    print("internal: no releases on the track yet")
for r in releases:
    codes = ",".join(str(c) for c in r.get("versionCodes", []))
    print("internal: versionCodes=[%s] status=%s name=%s"
          % (codes, r.get("status", "?"), r.get("name", "?")))
'
