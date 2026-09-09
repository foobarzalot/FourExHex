#!/usr/bin/env bash
# Upload an AAB to the Google Play testing tracks — the Android sibling of
# build_ios.sh's altool upload step.
#
# Flow (androidpublisher v3 "edits" API):
#   1. insert an edit (a transaction)
#   2. upload the bundle into the edit
#   3. point every track in PLAY_TRACKS at the uploaded versionCode
#   4. commit the edit (one atomic publish for all of them)
#
# Tracks: `internal` rolls out instantly, no review — that's the fast loop for
# your own devices. `alpha` is the closed track wired to the tester Google
# Group; its releases go through Google review before testers see them.
# On any failure the dangling edit is deleted so retries start clean.
#
# Usage:  tools/upload_play.sh [path/to/app.aab]
#         (default: build/android/FourExHex-release.aab — from `tools/build_android.sh aab`)
#
# Prereqs: docs/android-play-console-setup.md (app record, first manual upload,
# service-account JSON). Auth is delegated to tools/play_api.sh.

set -euo pipefail

source "$(dirname "${BASH_SOURCE[0]}")/_build_common.sh"
BASE="https://androidpublisher.googleapis.com/androidpublisher/v3/applications/$BUNDLE_ID"
UPLOAD_BASE="https://androidpublisher.googleapis.com/upload/androidpublisher/v3/applications/$BUNDLE_ID"
AAB="${1:-$PROJECT_DIR/build/android/FourExHex-release.aab}"

# Comma-separated track ids, in order. Override to publish to a subset, e.g.
# PLAY_TRACKS=internal tools/upload_play.sh
PLAY_TRACKS="${PLAY_TRACKS:-internal,alpha}"

[[ -f "$AAB" ]] || fail "AAB not found at $AAB (build one with: tools/build_android.sh aab)"

# One token for the whole flow (valid 1 h; the upload of a ~40 MB bundle is
# nowhere near that).
TOKEN="$("$PROJECT_DIR/tools/play_api.sh" --token)"
auth_curl() { curl -sS -H "Authorization: Bearer $TOKEN" "$@"; }

# json_field <field> — extract a top-level field from JSON on stdin; dies
# loudly (dumping the API's error body) if it's absent.
json_field() {
  python3 -c 'import json,sys
d = json.load(sys.stdin)
v = d.get(sys.argv[1])
if v is None:
    sys.exit("ERROR: expected field %r in API response: %s" % (sys.argv[1], json.dumps(d)))
print(v)' "$1"
}

EDIT_ID=""
COMMITTED=0
cleanup_edit() {
  if [[ -n "$EDIT_ID" && "$COMMITTED" == 0 ]]; then
    auth_curl -X DELETE "$BASE/edits/$EDIT_ID" >/dev/null || true
  fi
}
trap cleanup_edit EXIT

echo "==> Opening edit for $BUNDLE_ID"
EDIT_ID=$(auth_curl -X POST "$BASE/edits" | json_field id)

echo "==> Uploading $(basename "$AAB") ($(du -h "$AAB" | cut -f1 | tr -d ' '))"
VERSION_CODE=$(auth_curl -X POST \
  -H "Content-Type: application/octet-stream" \
  --data-binary @"$AAB" \
  "$UPLOAD_BASE/edits/$EDIT_ID/bundles?uploadType=media" | json_field versionCode)
echo "==> Uploaded versionCode $VERSION_CODE"

IFS=',' read -r -a TRACKS <<< "$PLAY_TRACKS"
for track in "${TRACKS[@]}"; do
  echo "==> Assigning versionCode $VERSION_CODE to the $track track"
  auth_curl -X PUT \
    -H "Content-Type: application/json" \
    -d '{"track":"'"$track"'","releases":[{"versionCodes":["'"$VERSION_CODE"'"],"status":"completed"}]}' \
    "$BASE/edits/$EDIT_ID/tracks/$track" | json_field track >/dev/null
done

echo "==> Committing edit"
auth_curl -X POST "$BASE/edits/$EDIT_ID:commit" | json_field id >/dev/null
COMMITTED=1

echo "==> Done. versionCode $VERSION_CODE is on: $PLAY_TRACKS"
echo "    internal: live now; opt-in link under Play Console -> Testing -> Internal testing."
echo "    alpha:    queued for Google review; testers on the Google Group get it once approved."
echo "    Check anytime with: tools/check_play_status.sh"
