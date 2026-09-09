#!/usr/bin/env bash
# Print the current state of the Google Play testing tracks — the Android
# sibling of check_testflight_status.sh.
#
# A track can only be read inside an edit transaction, so this opens one
# throwaway edit, GETs every track in PLAY_TRACKS, and deletes the edit again.
#
# Output on success (exit 0), one line per release on each track:
#   internal: versionCodes=[28] status=completed name=28 (1.0)
#   alpha:    versionCodes=[28] status=inProgress name=28 (1.0)
# Exit >0 on auth/API errors (usable in poll loops, like the TestFlight one).
#
# Usage:  tools/check_play_status.sh          (reads PLAY_TRACKS, default internal,alpha)
#         PLAY_TRACKS=alpha tools/check_play_status.sh
# Prereqs: service-account JSON (docs/android-play-console-setup.md, step 6).

set -euo pipefail

source "$(dirname "${BASH_SOURCE[0]}")/_build_common.sh"
BASE="https://androidpublisher.googleapis.com/androidpublisher/v3/applications/$BUNDLE_ID"

# Comma-separated track ids to report, in order.
PLAY_TRACKS="${PLAY_TRACKS:-internal,alpha}"

TOKEN="$("$PROJECT_DIR/tools/play_api.sh" --token)"
auth_curl() { curl -sS -H "Authorization: Bearer $TOKEN" "$@"; }

EDIT_ID=$(auth_curl -X POST "$BASE/edits" \
  | python3 -c 'import json,sys
d = json.load(sys.stdin)
v = d.get("id") or sys.exit("ERROR: could not open edit: " + json.dumps(d))
print(v)')
trap 'auth_curl -X DELETE "$BASE/edits/$EDIT_ID" >/dev/null || true' EXIT

IFS=',' read -r -a TRACKS <<< "$PLAY_TRACKS"
for track in "${TRACKS[@]}"; do
  auth_curl "$BASE/edits/$EDIT_ID/tracks/$track" | python3 -c '
import json, sys
track = sys.argv[1]
d = json.load(sys.stdin)
if "error" in d:
    sys.exit("ERROR: " + json.dumps(d["error"]))
releases = d.get("releases") or []
if not releases:
    print("%s: no releases on the track yet" % track)
for r in releases:
    codes = ",".join(str(c) for c in r.get("versionCodes", []))
    print("%s: versionCodes=[%s] status=%s name=%s"
          % (track, codes, r.get("status", "?"), r.get("name", "?")))
' "$track"
done
