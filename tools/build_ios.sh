#!/usr/bin/env bash
#
# Build an iOS .ipa of FourExHex from this Mac and upload it to TestFlight.
#
# Why this script exists / the non-obvious bits it papers over:
#   1. Team ID is required AT GODOT EXPORT TIME but is per-developer-account
#      private info, so it can't live in the committed export_presets.cfg.
#      Godot rejects the export with "App Store Team ID not specified" if the
#      field is empty. The script sed-injects the real team ID into the preset
#      before calling Godot and restores the empty value on exit (trap), so the
#      committed file stays clean even if the build crashes.
#   2. Godot's iOS export produces an Xcode project, NOT a .ipa directly. The
#      script runs Godot to get build/ios/FourExHex.xcodeproj, then xcodebuild
#      archive + xcodebuild -exportArchive to produce build/ios/FourExHex.ipa.
#   3. TestFlight upload uses xcrun altool with an App Store Connect API key.
#      altool finds the .p8 in ~/.appstoreconnect/private_keys/AuthKey_<KeyID>.p8
#      (a standard search path); the creds file just provides the Key ID and
#      Issuer ID env vars.
#   4. The Godot 4.6.1 .NET-on-iOS export prints "Exporting to an Apple
#      Embedded platform when using C#/.NET is experimental" as a WARNING but
#      not a hard error (verified during the Phase 1 spike). The export still
#      succeeds and the Xcode project compiles cleanly.
#
# Toolchain prerequisites (the script does NOT install these — it checks):
#   - Full Xcode at /Applications/Xcode.app (xcodebuild -version succeeds)
#   - .NET 8 SDK at $HOME/.dotnet
#   - The signed-in iOS-creds file at:
#       ~/Library/Application Support/Godot/keystores/fourexhex-ios-creds.sh
#     exporting ASC_API_KEY_ID, ASC_API_ISSUER_ID, IOS_TEAM_ID
#   - The .p8 key file at:
#       ~/.appstoreconnect/private_keys/AuthKey_<ASC_API_KEY_ID>.p8
#
# Result:
#   build/ios/FourExHex.xcodeproj  (Godot output, sub-dir layout)
#   build/ios/FourExHex.xcarchive  (xcodebuild archive output)
#   build/ios/FourExHex.ipa        (uploaded to TestFlight)
#
# Usage:  tools/build_ios.sh [debug|release] [--no-upload]
#   debug    -> ExportDebug C# config, --export-debug    (DEBUG defined, logs/asserts on)
#   release  -> ExportRelease C# config, --export-release (optimized; default)
#   --no-upload  Skip the xcrun altool upload step (for dry-run / inspection).
#   --tethered   Sign the .ipa for `development` distribution (not
#                `app-store-connect`), skip the App Store Connect upload, and
#                install onto the connected USB device via `xcrun devicectl`.
#                Device must be in Developer Mode and trusted on this Mac.
set -euo pipefail

MODE="${1:-release}"
UPLOAD=1
TETHERED=0
for arg in "$@"; do
  case "$arg" in
    --no-upload) UPLOAD=0 ;;
    --tethered)  TETHERED=1; UPLOAD=0 ;;
  esac
done
case "$MODE" in
  debug)   CSHARP_CONFIG="ExportDebug";   GODOT_FLAG="--export-debug" ;;
  release) CSHARP_CONFIG="ExportRelease"; GODOT_FLAG="--export-release" ;;
  *) echo "ERROR: unknown mode '$MODE' (use 'debug' or 'release')" >&2; exit 2 ;;
esac

if (( TETHERED )); then
  EXPORT_METHOD="development"
else
  EXPORT_METHOD="app-store-connect"
fi

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GODOT="/Applications/Godot_mono.app/Contents/MacOS/Godot"
PRESET="iOS"
BUILD_DIR="$PROJECT_DIR/build/ios"
XCODEPROJ="$BUILD_DIR/FourExHex.xcodeproj"
XCARCHIVE="$BUILD_DIR/FourExHex.xcarchive"
IPA="$BUILD_DIR/FourExHex.ipa"
PRESETS_CFG="$PROJECT_DIR/export_presets.cfg"
PRESETS_BAK="$PRESETS_CFG.bak.$$"
EXPORT_OPTIONS_TEMPLATE="$PROJECT_DIR/tools/ios_export_options.plist"
EXPORT_OPTIONS_LIVE="$BUILD_DIR/ExportOptions.plist"

CREDS="$HOME/Library/Application Support/Godot/keystores/fourexhex-ios-creds.sh"

export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$PATH"

# ---- Fail-fast prerequisite checks ----
fail() { echo "ERROR: $1" >&2; exit 1; }

[[ -x "$GODOT" ]] || fail "Godot not found at $GODOT"
xcodebuild -version >/dev/null 2>&1 \
  || fail "xcodebuild not working — install full Xcode (not just CLT), accept license: sudo xcodebuild -license"
[[ -f "$EXPORT_OPTIONS_TEMPLATE" ]] || fail "ExportOptions template missing: $EXPORT_OPTIONS_TEMPLATE"
[[ -f "$CREDS" ]] || fail "iOS creds file missing at $CREDS (see docs/ios-apple-developer-setup.md)"

# shellcheck source=/dev/null
source "$CREDS"
for v in ASC_API_KEY_ID ASC_API_ISSUER_ID IOS_TEAM_ID; do
  [[ -n "${!v:-}" ]] || fail "$v not set by $CREDS"
done

# Validate Team ID shape — Apple Team IDs are 10 alphanumeric chars; a typo
# wastes a 30-second Godot export before xcodebuild fails further downstream.
[[ "$IOS_TEAM_ID" =~ ^[A-Z0-9]{10}$ ]] \
  || fail "IOS_TEAM_ID '$IOS_TEAM_ID' doesn't look like a 10-char Apple Team ID"

# altool searches a fixed set of paths for the .p8 key file. We standardize
# on ~/.appstoreconnect/private_keys/AuthKey_<KeyID>.p8 (one of altool's
# documented search paths). The hand-off runbook tells the user to save there.
ASC_KEY_FILE="$HOME/.appstoreconnect/private_keys/AuthKey_${ASC_API_KEY_ID}.p8"
if (( UPLOAD )) && [[ ! -f "$ASC_KEY_FILE" ]]; then
  fail "App Store Connect API key file missing at $ASC_KEY_FILE — move the .p8 there or run with --no-upload"
fi

# ---- Team ID injection / restore trap ----
restore_presets() {
  if [[ -f "$PRESETS_BAK" ]]; then
    mv "$PRESETS_BAK" "$PRESETS_CFG"
  fi
}
trap restore_presets EXIT

cp "$PRESETS_CFG" "$PRESETS_BAK"
# In-place edit: empty app_store_team_id → real Team ID. Bracket regex
# protects against macOS sed eating the trailing newline.
sed -i '' "s|^application/app_store_team_id=\"\"\$|application/app_store_team_id=\"${IOS_TEAM_ID}\"|" "$PRESETS_CFG"
grep -q "^application/app_store_team_id=\"${IOS_TEAM_ID}\"\$" "$PRESETS_CFG" \
  || fail "Team ID substitution into $PRESETS_CFG failed — preset may have moved"

echo "==> Xcode:    $(xcodebuild -version | head -1)"
echo "==> Mode:     $MODE  ($CSHARP_CONFIG, $GODOT_FLAG)"
if (( TETHERED )); then
  echo "==> Method:   $EXPORT_METHOD  (tethered USB install)"
else
  echo "==> Method:   $EXPORT_METHOD"
fi
echo "==> Team ID:  $IOS_TEAM_ID"
echo "==> Output:   $IPA"

echo "==> Building C# assemblies (Debug for editor load + $CSHARP_CONFIG for the export)"
dotnet build "$PROJECT_DIR/FourExHex.csproj" -c Debug            >/dev/null
dotnet build "$PROJECT_DIR/FourExHex.csproj" -c "$CSHARP_CONFIG" >/dev/null

echo "==> Exporting iOS Xcode project ($MODE, headless)"
rm -rf "$BUILD_DIR/FourExHex.xcodeproj" "$BUILD_DIR/FourExHex" "$BUILD_DIR/FourExHex.pck" \
       "$BUILD_DIR/FourExHex.xcframework" "$XCARCHIVE" "$IPA"
mkdir -p "$BUILD_DIR"
"$GODOT" --headless --path "$PROJECT_DIR" "$GODOT_FLAG" "$PRESET" "$XCODEPROJ"
[[ -d "$XCODEPROJ" ]] || fail "Godot export did not produce $XCODEPROJ"

echo "==> Archiving with xcodebuild (this is the slow step, several minutes)"
xcodebuild \
  -project "$XCODEPROJ" \
  -scheme FourExHex \
  -configuration Release \
  -destination "generic/platform=iOS" \
  -archivePath "$XCARCHIVE" \
  -allowProvisioningUpdates \
  DEVELOPMENT_TEAM="$IOS_TEAM_ID" \
  archive \
  | sed -E 's/^/    /'
[[ -d "$XCARCHIVE" ]] || fail "xcodebuild archive did not produce $XCARCHIVE"

# Materialize ExportOptions.plist with the real Team ID + method substituted in.
sed -e "s|@TEAM_ID@|${IOS_TEAM_ID}|g" -e "s|@METHOD@|${EXPORT_METHOD}|g" \
  "$EXPORT_OPTIONS_TEMPLATE" > "$EXPORT_OPTIONS_LIVE"

echo "==> Exporting .ipa for $EXPORT_METHOD distribution"
xcodebuild \
  -exportArchive \
  -archivePath "$XCARCHIVE" \
  -exportPath "$BUILD_DIR" \
  -exportOptionsPlist "$EXPORT_OPTIONS_LIVE" \
  -allowProvisioningUpdates \
  | sed -E 's/^/    /'
[[ -f "$IPA" ]] || fail "xcodebuild -exportArchive did not produce $IPA"

echo "==> Built: $(file -b "$IPA")"

if (( TETHERED )); then
  echo "==> Installing onto tethered iOS device via xcrun devicectl"
  # Pick the first connected device that's connected via USB. `devicectl list
  # devices` JSON output keys are stable across Xcode 15/16/26.
  DEVICE_JSON="$(xcrun devicectl list devices --json-output - 2>/dev/null || true)"
  if [[ -z "$DEVICE_JSON" ]]; then
    fail "xcrun devicectl list devices failed — is Xcode 15+ installed? (current: $(xcodebuild -version | head -1))"
  fi
  # Filter for paired, USB-attached iPhone/iPad. Stop early if none.
  DEVICE_UDID="$(printf '%s' "$DEVICE_JSON" | python3 -c '
import json, sys
data = json.load(sys.stdin)
for d in data.get("result", {}).get("devices", []):
    props = d.get("deviceProperties", {})
    conn  = d.get("connectionProperties", {})
    if props.get("platformIdentifier", "").startswith("com.apple.platform.iphoneos") \
       and conn.get("pairingState") == "paired" \
       and "wired" in str(conn.get("transportType", "")).lower():
        print(d.get("identifier", ""))
        break
' )"
  if [[ -z "$DEVICE_UDID" ]]; then
    fail "No paired USB-attached iOS device found. Plug in, unlock, Trust This Computer, and enable Developer Mode (Settings → Privacy & Security → Developer Mode)."
  fi
  echo "    Device: $DEVICE_UDID"
  xcrun devicectl device install app --device "$DEVICE_UDID" "$IPA" \
    | sed -E 's/^/    /'
  echo "==> Done. App is installed; launch it from the home screen."
  echo "    Read live device logs with:"
  echo "      xcrun devicectl device process launch --console --device $DEVICE_UDID com.foobarzalot.fourexhex"
  echo "    Or open Console.app → filter by process 'FourExHex' for the SafeArea/DisplayScale lines."
  exit 0
fi

if (( ! UPLOAD )); then
  echo "==> --no-upload set; skipping TestFlight upload."
  echo "    To upload manually:"
  echo "      xcrun altool --upload-app --type ios -f \"$IPA\" \\"
  echo "        --apiKey \"\$ASC_API_KEY_ID\" --apiIssuer \"\$ASC_API_ISSUER_ID\""
  exit 0
fi

echo "==> Uploading to App Store Connect / TestFlight"
echo "    (build will be in 'Processing' for ~15-30 minutes before appearing in TestFlight)"
xcrun altool --upload-app --type ios -f "$IPA" \
  --apiKey "$ASC_API_KEY_ID" \
  --apiIssuer "$ASC_API_ISSUER_ID" \
  | sed -E 's/^/    /'

echo "==> Done. Watch App Store Connect → My Apps → FourExHex → TestFlight for the build to appear."
