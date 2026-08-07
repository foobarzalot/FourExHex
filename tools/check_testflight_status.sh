#!/usr/bin/env bash
# Poll App Store Connect for the most recent build's processing state.
# Auth + the ES256 JWT are delegated to tools/asc_api.sh (the one JWT
# implementation); this script only owns the builds query and its parsing.
# Prints just the build version + processing state, never the credentials.
#
# Usage:
#   tools/check_testflight_status.sh
#
# Exits 0 with the latest build state on stdout. Exit >0 on auth or API
# error so a polling loop can break cleanly.

set -euo pipefail

source "$(dirname "${BASH_SOURCE[0]}")/_build_common.sh"

# Filter by app, sort by uploadedDate desc, limit 1. Builds API:
# https://developer.apple.com/documentation/appstoreconnectapi/list_builds
RESP=$("$PROJECT_DIR/tools/asc_api.sh" \
  "/builds?filter%5Bapp%5D=$ASC_APP_ID&sort=-uploadedDate&limit=1&fields%5Bbuilds%5D=version,processingState,uploadedDate,expired")

# Surface API errors verbatim (no creds in response body). The response is fed
# to python via stdin, never interpolated into source.
if printf '%s' "$RESP" | python3 -c "import sys,json; d=json.load(sys.stdin); sys.exit(0 if 'data' in d else 1)" 2>/dev/null; then
  printf '%s' "$RESP" | python3 -c '
import json, sys
resp = json.load(sys.stdin)
items = resp.get("data", [])
if not items:
    print(f"no builds found for app id {sys.argv[1]}")
    sys.exit(2)
a = items[0].get("attributes", {})
print("build=%s state=%s uploaded=%s expired=%s"
      % (a.get("version"), a.get("processingState"), a.get("uploadedDate"), a.get("expired")))
' "$ASC_APP_ID"
else
  echo "API error response:" >&2
  printf '%s\n' "$RESP" >&2
  exit 3
fi
