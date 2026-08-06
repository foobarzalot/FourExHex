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
#   4. Interrupting the script must stop the WHOLE pipeline, or an orphaned
#      xcodebuild/altool can upload a build minutes after the "stop". Three
#      pieces cooperate: the script re-execs itself as a process-group leader
#      (macOS has no setsid, so perl's setpgrp does it), traps INT/TERM/HUP to
#      kill that group, and runs every slow step as a backgrounded child it
#      `wait`s on — bash defers a trapped signal until the running foreground
#      command finishes, so without the `wait` the handler wouldn't run until
#      the archive completed on its own.
#   5. The .ipa's CFBundleVersion is asserted against AppVersion.Build before
#      upload, so a build number that doesn't match the repo's counter fails
#      here rather than shipping (see manageAppVersionAndBuildNumber in
#      tools/ios_export_options.plist).
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
#   --dev-ipa    Sign for `development` distribution like --tethered but stop
#                after producing the .ipa (no device install, no upload). Used
#                by CI PR builds: the artifact installs tethered later via
#                `xcrun devicectl device install app`.
set -euo pipefail

# ---- Own process group, so a kill takes the whole build tree down ----
# `kill -- -$$` only reaps our descendants if $$ IS the process-group id; when
# launched from a shell we inherit the caller's group, where that kill would
# either hit the caller or nothing at all. macOS ships no setsid; stock perl
# can call setpgrp(2) and exec us back. Re-exec exactly once.
if [[ "${BUILD_IOS_PGLEADER:-}" != "1" ]]; then
  export BUILD_IOS_PGLEADER=1
  exec perl -e 'setpgrp(0,0); exec @ARGV' -- bash "$0" "$@"
fi

kill_group() {
  trap '' TERM   # our own group-kill must not re-enter this handler
  echo "" >&2
  echo "==> Interrupted — killing build process group (pgid $$)" >&2
  kill -TERM -- -$$ 2>/dev/null || true
  exit 130       # falls through to the EXIT trap, which restores the presets
}
trap kill_group INT TERM HUP

# Run a long command as a backgrounded child and wait on it. `wait` is
# interruptible, so a trapped signal reaches kill_group immediately instead of
# after the step finishes. Pipelines can't go through this helper (a pipe can't
# be passed as arguments) — those use the inline `… & wait $! || fail` form.
run_step() {
  "$@" &
  wait $!
}

MODE="${1:-release}"
UPLOAD=1
TETHERED=0
DEV_IPA=0
for arg in "$@"; do
  case "$arg" in
    --no-upload) UPLOAD=0 ;;
    --tethered)  TETHERED=1; UPLOAD=0 ;;
    --dev-ipa)   DEV_IPA=1;  UPLOAD=0 ;;
  esac
done
case "$MODE" in
  debug)   CSHARP_CONFIG="ExportDebug";   GODOT_FLAG="--export-debug";   XCODE_CONFIG="Debug" ;;
  release) CSHARP_CONFIG="ExportRelease"; GODOT_FLAG="--export-release"; XCODE_CONFIG="Release" ;;
  *) echo "ERROR: unknown mode '$MODE' (use 'debug' or 'release')" >&2; exit 2 ;;
esac

if (( TETHERED || DEV_IPA )); then
  EXPORT_METHOD="development"
else
  EXPORT_METHOD="app-store-connect"
fi

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GODOT="${GODOT:-/Applications/Godot_mono.app/Contents/MacOS/Godot}"
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

# CI runners point GODOT / DOTNET_ROOT elsewhere via env; the defaults match
# this Mac's local install. Skip the user-local .NET entirely when absent.
if [[ -n "${DOTNET_ROOT:-}" || -d "$HOME/.dotnet" ]]; then
  export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
  export PATH="$DOTNET_ROOT:$PATH"
fi

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

# On this Mac, xcodebuild's -allowProvisioningUpdates authenticates through the
# logged-in Xcode account. CI has no logged-in Xcode, so when ASC_API_KEY_PATH
# is set (pointing at the .p8) the xcodebuild calls authenticate with the App
# Store Connect API key instead. Unset locally -> no args added.
XCODEBUILD_AUTH=()
if [[ -n "${ASC_API_KEY_PATH:-}" ]]; then
  [[ -f "$ASC_API_KEY_PATH" ]] || fail "ASC_API_KEY_PATH is set but no file exists at $ASC_API_KEY_PATH"
  XCODEBUILD_AUTH=(
    -authenticationKeyPath "$ASC_API_KEY_PATH"
    -authenticationKeyID "$ASC_API_KEY_ID"
    -authenticationKeyIssuerID "$ASC_API_ISSUER_ID"
  )
fi

# ---- Team ID injection / restore trap ----
restore_presets() {
  if [[ -f "$PRESETS_BAK" ]]; then
    mv "$PRESETS_BAK" "$PRESETS_CFG"
  fi
}
trap restore_presets EXIT

# Sync version from the canonical AppVersion.cs BEFORE taking the backup, so the
# synced values are captured in PRESETS_BAK and persist through the trap restore
# (the restore only exists to scrub the transient team-ID edit below, which must
# NOT be committed — the version sync is meant to stick).
echo "==> Syncing export_presets.cfg version from scripts/AppVersion.cs"
"$PROJECT_DIR/tools/sync_version.sh"

cp "$PRESETS_CFG" "$PRESETS_BAK"
# In-place edit: empty app_store_team_id → real Team ID. -i.sedbak is the
# in-place form both BSD and GNU sed accept (CI runners include Linux).
sed -i.sedbak "s|^application/app_store_team_id=\"\"\$|application/app_store_team_id=\"${IOS_TEAM_ID}\"|" "$PRESETS_CFG"
rm -f "$PRESETS_CFG.sedbak"
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
run_step dotnet build "$PROJECT_DIR/FourExHex.csproj" -c Debug            >/dev/null \
  || fail "dotnet build -c Debug failed"
run_step dotnet build "$PROJECT_DIR/FourExHex.csproj" -c "$CSHARP_CONFIG" >/dev/null \
  || fail "dotnet build -c $CSHARP_CONFIG failed"

echo "==> Exporting iOS Xcode project ($MODE, headless)"
rm -rf "$BUILD_DIR/FourExHex.xcodeproj" "$BUILD_DIR/FourExHex" "$BUILD_DIR/FourExHex.pck" \
       "$BUILD_DIR/FourExHex.xcframework" "$XCARCHIVE" "$IPA"
mkdir -p "$BUILD_DIR"
run_step "$GODOT" --headless --path "$PROJECT_DIR" "$GODOT_FLAG" "$PRESET" "$XCODEPROJ" \
  || fail "Godot iOS export failed"
[[ -d "$XCODEPROJ" ]] || fail "Godot export did not produce $XCODEPROJ"

# Godot's iOS exporter hardcodes CODE_SIGN_IDENTITY = "Apple Distribution"
# in the Release config alongside CODE_SIGN_STYLE = "Automatic". xcodebuild
# archive's automatic signing picks "Apple Development" by default, so the
# hardcoded "Apple Distribution" fires the "conflicting provisioning
# settings" error. Strip the hardcoded identity so auto-signing handles
# archive cleanly; the exportArchive step (method=app-store-connect) re-
# signs with Apple Distribution as part of producing the .ipa, which is
# the canonical Apple distribution workflow. No-op for Debug (which has
# "Apple Development" — matches auto-signing's archive pick).
sed -i.sedbak 's|CODE_SIGN_IDENTITY = "Apple Distribution";|CODE_SIGN_IDENTITY = "";|g' \
  "$XCODEPROJ/project.pbxproj"
rm -f "$XCODEPROJ/project.pbxproj.sedbak"
grep -q 'CODE_SIGN_IDENTITY = "";' "$XCODEPROJ/project.pbxproj" \
  || fail "CODE_SIGN_IDENTITY blanking in project.pbxproj failed — the exporter may no longer hardcode 'Apple Distribution'; re-check whether this sed is still needed"

echo "==> Archiving with xcodebuild (this is the slow step, several minutes)"
# Archive uses auto-signing (Apple Development for both Debug and Release —
# the Release distribution-signing happens at exportArchive time per the
# ExportOptions.plist method).
xcodebuild \
  -project "$XCODEPROJ" \
  -scheme FourExHex \
  -configuration "$XCODE_CONFIG" \
  -destination "generic/platform=iOS" \
  -archivePath "$XCARCHIVE" \
  -allowProvisioningUpdates \
  ${XCODEBUILD_AUTH[@]+"${XCODEBUILD_AUTH[@]}"} \
  DEVELOPMENT_TEAM="$IOS_TEAM_ID" \
  archive \
  | sed -E 's/^/    /' &
wait $! || fail "xcodebuild archive failed"
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
  ${XCODEBUILD_AUTH[@]+"${XCODEBUILD_AUTH[@]}"} \
  | sed -E 's/^/    /' &
wait $! || fail "xcodebuild -exportArchive failed"
[[ -f "$IPA" ]] || fail "xcodebuild -exportArchive did not produce $IPA"

echo "==> Built: $(file -b "$IPA")"

# The .ipa's build number must be exactly AppVersion.Build — that const is the
# single monotonic counter both platforms ship under. Anything else means the
# number was rewritten between the preset sync and the export (see
# manageAppVersionAndBuildNumber in tools/ios_export_options.plist), and
# uploading it would put a build on App Store Connect that no commit records.
# Same parse as tools/sync_version.sh, against the same canonical file.
EXPECTED_BUILD="$(grep -oE 'Build[[:space:]]*=[[:space:]]*[0-9]+' \
  "$PROJECT_DIR/scripts/AppVersion.cs" | grep -oE '[0-9]+$')"
[[ -n "$EXPECTED_BUILD" ]] || fail "could not parse 'Build = <int>' from $PROJECT_DIR/scripts/AppVersion.cs"
IPA_BUILD="$(unzip -p "$IPA" 'Payload/*.app/Info.plist' \
  | plutil -extract CFBundleVersion raw -o - -)"
[[ "$IPA_BUILD" == "$EXPECTED_BUILD" ]] \
  || fail "IPA is stamped CFBundleVersion $IPA_BUILD but AppVersion.Build is $EXPECTED_BUILD — the build number was rewritten during export; check manageAppVersionAndBuildNumber in tools/ios_export_options.plist"
echo "==> Verified CFBundleVersion: $IPA_BUILD (matches AppVersion.Build)"

if (( TETHERED )); then
  echo "==> Installing onto tethered iOS device via xcrun devicectl"
  # Pick the first connected device that's connected via USB. `devicectl list
  # devices` JSON output keys are stable across Xcode 15/16/26.
  DEVICE_JSON="$(xcrun devicectl list devices --json-output - 2>/dev/null || true)"
  if [[ -z "$DEVICE_JSON" ]]; then
    fail "xcrun devicectl list devices failed — is Xcode 15+ installed? (current: $(xcodebuild -version | head -1))"
  fi
  # First paired, USB-attached iOS device wins. devicectl's
  # deviceProperties.platformIdentifier was unreliable in our testing
  # (came back null for a real paired iPhone), so we lean on the more
  # robust pairingState + transportType checks.
  DEVICE_UDID="$(printf '%s' "$DEVICE_JSON" | python3 -c '
import json, sys
data = json.load(sys.stdin)
for d in data.get("result", {}).get("devices", []):
    conn = d.get("connectionProperties", {})
    if conn.get("pairingState") == "paired" \
       and "wired" in str(conn.get("transportType", "")).lower():
        print(d.get("identifier", ""))
        break
' )"
  if [[ -z "$DEVICE_UDID" ]]; then
    fail "No paired USB-attached iOS device found. Plug in, unlock, Trust This Computer, and enable Developer Mode (Settings → Privacy & Security → Developer Mode)."
  fi
  echo "    Device: $DEVICE_UDID"
  xcrun devicectl device install app --device "$DEVICE_UDID" "$IPA" \
    | sed -E 's/^/    /' &
  wait $! || fail "devicectl install failed"
  echo "==> Done. App is installed; launch it from the home screen."
  echo "    Read live device logs with:"
  echo "      xcrun devicectl device process launch --console --device $DEVICE_UDID com.foobarzalot.fourexhex"
  echo "    Or open Console.app → filter by process 'FourExHex' for the SafeArea/DisplayScale lines."
  exit 0
fi

if (( DEV_IPA )); then
  echo "==> --dev-ipa set; development-signed .ipa ready (no install, no upload)."
  echo "    Install onto a tethered device with:"
  echo "      xcrun devicectl device install app --device <udid> \"$IPA\""
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
  | sed -E 's/^/    /' &
wait $! || fail "altool upload failed"

echo "==> Done. Watch App Store Connect → My Apps → FourExHex → TestFlight for the build to appear."
