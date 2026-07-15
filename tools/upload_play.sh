#!/usr/bin/env bash
# Upload an AAB to the Google Play internal-testing track — the Android
# sibling of build_ios.sh's altool upload step.
#
# Flow (androidpublisher v3 "edits" API):
#   1. insert an edit (a transaction)
#   2. upload the bundle into the edit
#   3. point the internal track at the uploaded versionCode
#   4. commit the edit (atomic publish; internal track = no review delay)
# On any failure the dangling edit is deleted so retries start clean.
#
# Usage:  tools/upload_play.sh [path/to/app.aab]
#         (default: build/android/FourExHex-release.aab — from `tools/build_android.sh aab`)
#
# Prereqs: docs/android-play-console-setup.md (app record, first manual upload,
# service-account JSON). Auth is delegated to tools/play_api.sh.

set -euo pipefail

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PACKAGE="com.foobarzalot.fourexhex"
BASE="https://androidpublisher.googleapis.com/androidpublisher/v3/applications/$PACKAGE"
UPLOAD_BASE="https://androidpublisher.googleapis.com/upload/androidpublisher/v3/applications/$PACKAGE"
AAB="${1:-$PROJECT_DIR/build/android/FourExHex-release.aab}"

fail() { echo "ERROR: $1" >&2; exit 1; }
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

echo "==> Opening edit for $PACKAGE"
EDIT_ID=$(auth_curl -X POST "$BASE/edits" | json_field id)

echo "==> Uploading $(basename "$AAB") ($(du -h "$AAB" | cut -f1 | tr -d ' '))"
VERSION_CODE=$(auth_curl -X POST \
  -H "Content-Type: application/octet-stream" \
  --data-binary @"$AAB" \
  "$UPLOAD_BASE/edits/$EDIT_ID/bundles?uploadType=media" | json_field versionCode)
echo "==> Uploaded versionCode $VERSION_CODE"

echo "==> Assigning versionCode $VERSION_CODE to the internal track"
auth_curl -X PUT \
  -H "Content-Type: application/json" \
  -d '{"track":"internal","releases":[{"versionCodes":["'"$VERSION_CODE"'"],"status":"completed"}]}' \
  "$BASE/edits/$EDIT_ID/tracks/internal" | json_field track >/dev/null

echo "==> Committing edit"
auth_curl -X POST "$BASE/edits/$EDIT_ID:commit" | json_field id >/dev/null
COMMITTED=1

echo "==> Done. versionCode $VERSION_CODE is live on the internal track."
echo "    Testers get it via the opt-in link (Play Console -> Testing -> Internal testing)."
echo "    Check anytime with: tools/check_play_status.sh"
