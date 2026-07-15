#!/usr/bin/env bash
# Shared Google Play Developer API helper — the Android sibling of asc_api.sh.
# Auth: service-account JSON -> RS256 JWT (openssl) -> OAuth access token.
#
# Usage:
#   tools/play_api.sh --token                    # print a bare access token
#                                                # (for callers hitting the
#                                                # upload/… endpoint, which has
#                                                # a different base URL)
#   tools/play_api.sh /edits/<id>/tracks/internal          # GET
#   tools/play_api.sh -X POST -d '' /edits                 # POST
#
# Last positional arg is the API path; everything before is curl flags. Paths
# are relative to
#   https://androidpublisher.googleapis.com/androidpublisher/v3/applications/com.foobarzalot.fourexhex
#
# The service-account key comes from
#   ~/Library/Application Support/Godot/keystores/fourexhex-play-service-account.json
# (override with PLAY_SERVICE_ACCOUNT_JSON). Setup: docs/android-play-console-setup.md
# step 6. Never prints credentials.

set -euo pipefail

PACKAGE="com.foobarzalot.fourexhex"
SA_JSON="${PLAY_SERVICE_ACCOUNT_JSON:-$HOME/Library/Application Support/Godot/keystores/fourexhex-play-service-account.json}"
[[ -f "$SA_JSON" ]] || { echo "ERROR: service-account JSON missing at $SA_JSON (see docs/android-play-console-setup.md, step 6)" >&2; exit 1; }

b64url() { openssl base64 -e -A | tr '+/' '-_' | tr -d '='; }

CLIENT_EMAIL=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["client_email"])' "$SA_JSON")
# The PEM private key never touches the command line — extracted to a 0600 temp file.
KEY_FILE=$(mktemp)
trap 'rm -f "$KEY_FILE"' EXIT
python3 -c 'import json,sys; open(sys.argv[2],"w").write(json.load(open(sys.argv[1]))["private_key"])' "$SA_JSON" "$KEY_FILE"

NOW=$(date +%s)
EXP=$((NOW + 3600))
HEADER_B64=$(printf '%s' '{"alg":"RS256","typ":"JWT"}' | b64url)
PAYLOAD_JSON='{"iss":"'"$CLIENT_EMAIL"'","scope":"https://www.googleapis.com/auth/androidpublisher","aud":"https://oauth2.googleapis.com/token","iat":'"$NOW"',"exp":'"$EXP"'}'
PAYLOAD_B64=$(printf '%s' "$PAYLOAD_JSON" | b64url)
SIGNING_INPUT="$HEADER_B64.$PAYLOAD_B64"
# RS256 = plain PKCS#1 signature — no DER->raw conversion needed (unlike ES256
# in asc_api.sh).
SIG_B64=$(printf '%s' "$SIGNING_INPUT" | openssl dgst -sha256 -sign "$KEY_FILE" -binary | b64url)
JWT="$SIGNING_INPUT.$SIG_B64"

TOKEN=$(curl -sS -X POST https://oauth2.googleapis.com/token \
  -d "grant_type=urn:ietf:params:oauth:grant-type:jwt-bearer" \
  --data-urlencode "assertion=$JWT" \
  | python3 -c 'import json,sys; d=json.load(sys.stdin); t=d.get("access_token") or sys.exit("ERROR: token exchange failed: "+json.dumps(d)); print(t)')

if [[ "${1:-}" == "--token" ]]; then
  printf '%s\n' "$TOKEN"
  exit 0
fi

# Last positional arg is the API path; everything before is curl flags.
# (Bash 3.2 on macOS has no negative array subscripts, so build it long-hand.)
N=$#
if (( N < 1 )); then
  echo "ERROR: at least one arg required (API path, or --token)" >&2
  exit 1
fi
PATH_ARG="${!N}"
EXTRA=()
i=1
while (( i < N )); do
  EXTRA+=("${!i}")
  i=$((i + 1))
done
curl -sS -H "Authorization: Bearer $TOKEN" \
  ${EXTRA[@]+"${EXTRA[@]}"} \
  "https://androidpublisher.googleapis.com/androidpublisher/v3/applications/${PACKAGE}${PATH_ARG}"
