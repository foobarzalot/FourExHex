#!/usr/bin/env bash
# Shared App Store Connect API helper. Mints a JWT from the same creds
# build_ios.sh uses, then runs `curl $@` against
# https://api.appstoreconnect.apple.com/v1$1.
#
# Usage:
#   tools/asc_api.sh /apps                                # GET
#   tools/asc_api.sh /betaGroups?filter%5Bapp%5D=$APP_ID  # GET with query
#   tools/asc_api.sh -X POST -H 'Content-Type: application/json' \
#     -d '{...}' /betaGroups                              # POST
#
# The JWT is materialized into the env var ASC_JWT_TOKEN so callers can
# pass extra headers without re-minting. Never prints credentials.

set -euo pipefail

CREDS="$HOME/Library/Application Support/Godot/keystores/fourexhex-ios-creds.sh"
[[ -f "$CREDS" ]] || { echo "ERROR: creds missing at $CREDS" >&2; exit 1; }
# shellcheck source=/dev/null
source "$CREDS"
for v in ASC_API_KEY_ID ASC_API_ISSUER_ID; do
  [[ -n "${!v:-}" ]] || { echo "ERROR: $v not set" >&2; exit 1; }
done

KEY_FILE="$HOME/.appstoreconnect/private_keys/AuthKey_${ASC_API_KEY_ID}.p8"
[[ -f "$KEY_FILE" ]] || { echo "ERROR: private key missing" >&2; exit 1; }

b64url() { openssl base64 -e -A | tr '+/' '-_' | tr -d '='; }
NOW=$(date +%s)
EXP=$((NOW + 1200))
HEADER_JSON='{"alg":"ES256","kid":"'"$ASC_API_KEY_ID"'","typ":"JWT"}'
PAYLOAD_JSON='{"iss":"'"$ASC_API_ISSUER_ID"'","iat":'"$NOW"',"exp":'"$EXP"',"aud":"appstoreconnect-v1"}'
HEADER_B64=$(printf '%s' "$HEADER_JSON" | b64url)
PAYLOAD_B64=$(printf '%s' "$PAYLOAD_JSON" | b64url)
SIGNING_INPUT="$HEADER_B64.$PAYLOAD_B64"
DER_SIG=$(mktemp)
trap 'rm -f "$DER_SIG"' EXIT
printf '%s' "$SIGNING_INPUT" | openssl dgst -sha256 -sign "$KEY_FILE" -binary > "$DER_SIG"
RAW_SIG_B64=$(python3 - "$DER_SIG" <<'PY'
import sys, base64
data = open(sys.argv[1], "rb").read()
assert data[0] == 0x30
body = data[2:] if data[1] < 0x80 else data[3:]
assert body[0] == 0x02
rlen = body[1]; r = body[2:2+rlen]; body = body[2+rlen:]
assert body[0] == 0x02
slen = body[1]; s = body[2:2+slen]
r = r.lstrip(b"\x00").rjust(32, b"\x00")
s = s.lstrip(b"\x00").rjust(32, b"\x00")
print(base64.urlsafe_b64encode(r + s).rstrip(b"=").decode())
PY
)
JWT="$SIGNING_INPUT.$RAW_SIG_B64"

# Last positional arg is the API path; everything before is curl flags.
# (Bash 3.2 on macOS has no negative array subscripts, so build it long-hand.)
N=$#
if (( N < 1 )); then
  echo "ERROR: at least one arg required (API path)" >&2
  exit 1
fi
PATH_ARG="${!N}"
EXTRA=()
i=1
while (( i < N )); do
  EXTRA+=("${!i}")
  i=$((i + 1))
done
curl -sS -H "Authorization: Bearer $JWT" \
  ${EXTRA[@]+"${EXTRA[@]}"} \
  "https://api.appstoreconnect.apple.com/v1${PATH_ARG}"
