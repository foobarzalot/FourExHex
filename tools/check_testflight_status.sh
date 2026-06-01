#!/usr/bin/env bash
# Poll App Store Connect for the most recent build's processing state.
# Uses the same creds + .p8 API key that build_ios.sh's TestFlight upload
# path uses. Prints just the build version + processing state, never the
# credentials. The bundle ID is hardcoded to avoid passing it on the CLI.
#
# Usage:
#   tools/check_testflight_status.sh
#
# Exits 0 with the latest build state on stdout. Exit >0 on auth or API
# error so a polling loop can break cleanly.

set -euo pipefail

APP_ID="6774765597"   # App Store Connect app resource ID for FourExHex
CREDS="$HOME/Library/Application Support/Godot/keystores/fourexhex-ios-creds.sh"

[[ -f "$CREDS" ]] || { echo "ERROR: creds missing at $CREDS" >&2; exit 1; }
# shellcheck source=/dev/null
source "$CREDS"
for v in ASC_API_KEY_ID ASC_API_ISSUER_ID; do
  [[ -n "${!v:-}" ]] || { echo "ERROR: $v not set" >&2; exit 1; }
done

KEY_FILE="$HOME/.appstoreconnect/private_keys/AuthKey_${ASC_API_KEY_ID}.p8"
[[ -f "$KEY_FILE" ]] || { echo "ERROR: private key missing at expected path" >&2; exit 1; }

# --- JWT for App Store Connect API ----------------------------------------
# ES256 (ECDSA P-256 + SHA-256). 20-minute expiry per Apple's max window.
b64url() { openssl base64 -e -A | tr '+/' '-_' | tr -d '='; }

NOW=$(date +%s)
EXP=$((NOW + 1200))
HEADER_JSON='{"alg":"ES256","kid":"'"$ASC_API_KEY_ID"'","typ":"JWT"}'
PAYLOAD_JSON='{"iss":"'"$ASC_API_ISSUER_ID"'","iat":'"$NOW"',"exp":'"$EXP"',"aud":"appstoreconnect-v1"}'
HEADER_B64=$(printf '%s' "$HEADER_JSON" | b64url)
PAYLOAD_B64=$(printf '%s' "$PAYLOAD_JSON" | b64url)
SIGNING_INPUT="$HEADER_B64.$PAYLOAD_B64"
# openssl produces DER-encoded ECDSA signature. JWT wants raw R||S (64 bytes
# for P-256). Convert via asn1parse: find the two INTEGER fields, strip any
# leading 0x00 padding, left-pad each to 32 bytes, concat.
DER_SIG=$(mktemp)
trap 'rm -f "$DER_SIG"' EXIT
printf '%s' "$SIGNING_INPUT" | openssl dgst -sha256 -sign "$KEY_FILE" -binary > "$DER_SIG"
# Use Python (always present on macOS) for the DER→raw conversion — keeps
# the shell pipeline simple and avoids brittle asn1parse text munging.
RAW_SIG_B64=$(python3 - "$DER_SIG" <<'PY'
import sys, base64
data = open(sys.argv[1], "rb").read()
# Minimal DER ECDSA-Sig: 30 LEN 02 LEN R 02 LEN S
assert data[0] == 0x30
# Skip SEQUENCE header
body = data[2:] if data[1] < 0x80 else data[3:]
assert body[0] == 0x02
rlen = body[1]
r = body[2:2+rlen]
body = body[2+rlen:]
assert body[0] == 0x02
slen = body[1]
s = body[2:2+slen]
# Strip leading zero padding, then left-pad to 32 bytes each.
r = r.lstrip(b"\x00").rjust(32, b"\x00")
s = s.lstrip(b"\x00").rjust(32, b"\x00")
raw = r + s
print(base64.urlsafe_b64encode(raw).rstrip(b"=").decode())
PY
)
JWT="$SIGNING_INPUT.$RAW_SIG_B64"

# --- Fetch builds ---------------------------------------------------------
# Filter by bundle ID, sort by uploadedDate desc, limit 1. Builds API:
# https://developer.apple.com/documentation/appstoreconnectapi/list_builds
RESP=$(curl -sS -H "Authorization: Bearer $JWT" \
  "https://api.appstoreconnect.apple.com/v1/builds?filter%5Bapp%5D=$APP_ID&sort=-uploadedDate&limit=1&fields%5Bbuilds%5D=version,processingState,uploadedDate,expired")

# Surface API errors verbatim (no creds in response body).
if printf '%s' "$RESP" | python3 -c "import sys,json; d=json.load(sys.stdin); sys.exit(0 if 'data' in d else 1)" 2>/dev/null; then
  python3 - <<PY
import json, sys
resp = json.loads('''$RESP''')
items = resp.get("data", [])
if not items:
    print("no builds found for app id $APP_ID")
    sys.exit(2)
b = items[0]
attrs = b.get("attributes", {})
print(f"build={attrs.get('version')} state={attrs.get('processingState')} uploaded={attrs.get('uploadedDate')} expired={attrs.get('expired')}")
PY
else
  echo "API error response:" >&2
  printf '%s\n' "$RESP" >&2
  exit 3
fi
