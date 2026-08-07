# Sourced library of the definitions shared across the tools/ scripts —
# NOT executable. Each script starts (after its own `set -euo pipefail`) with:
#
#   source "$(dirname "${BASH_SOURCE[0]}")/_build_common.sh"
#
# This file defines variables and functions only; its single side effect is
# the DOTNET_ROOT/PATH export below. It must stay safe under the callers'
# `set -euo pipefail` and macOS's bash 3.2.

# Repo root, resolved from this file's own location so it is correct no
# matter which script sources it or where that script was invoked from.
PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# CI runners point GODOT / DOTNET_ROOT elsewhere via env; the defaults match
# this Mac's local install. Skip the user-local .NET entirely when absent.
GODOT="${GODOT:-/Applications/Godot_mono.app/Contents/MacOS/Godot}"
if [[ -n "${DOTNET_ROOT:-}" || -d "$HOME/.dotnet" ]]; then
  export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
  export PATH="$DOTNET_ROOT:$PATH"
fi

# App identity: the store bundle/package id (also baked into the presets) and
# the App Store Connect numeric app resource id.
BUNDLE_ID="com.foobarzalot.fourexhex"
ASC_APP_ID="6774765597"

# Android toolchain defaults. Consumers export JAVA_HOME themselves (only the
# Android scripts want it exported).
ANDROID_SDK="${ANDROID_SDK_ROOT:-${ANDROID_HOME:-$HOME/Library/Android/sdk}}"
JAVA_HOME_DEFAULT="/opt/homebrew/opt/openjdk@21/libexec/openjdk.jdk/Contents/Home"

# Credential locations (the files themselves are never committed).
KEYSTORES_DIR="$HOME/Library/Application Support/Godot/keystores"
IOS_CREDS_FILE="$KEYSTORES_DIR/fourexhex-ios-creds.sh"
ANDROID_CREDS_FILE="$KEYSTORES_DIR/fourexhex-android-creds.sh"

fail() { echo "ERROR: $1" >&2; exit 1; }

# URL-safe base64 without padding — the JWT alphabet (asc_api.sh, play_api.sh).
b64url() { openssl base64 -e -A | tr '+/' '-_' | tr -d '='; }

# parse_mode <mode> <allowed-mode>... — validate <mode> against the allowed
# list and set MODE, CSHARP_CONFIG, GODOT_FLAG, XCODE_CONFIG. `aab` builds
# like release (the AAB-vs-APK switch is a preset flip in build_android.sh).
parse_mode() {
  MODE="$1"
  shift
  local allowed ok=0
  for allowed in "$@"; do
    [[ "$MODE" == "$allowed" ]] && ok=1
  done
  if (( ! ok )); then
    echo "ERROR: unknown mode '$MODE' (use $(printf "'%s' " "$@" | sed "s/ $//" | sed "s/ /, /g"))" >&2
    exit 2
  fi
  case "$MODE" in
    debug) CSHARP_CONFIG="ExportDebug";   GODOT_FLAG="--export-debug";   XCODE_CONFIG="Debug" ;;
    *)     CSHARP_CONFIG="ExportRelease"; GODOT_FLAG="--export-release"; XCODE_CONFIG="Release" ;;
  esac
}

# Build the C# assemblies: Debug so the editor can still load the assembly,
# plus the export config the Godot export will publish against.
build_assemblies() {
  echo "==> Building C# assemblies (Debug for editor load + $CSHARP_CONFIG for the export)"
  dotnet build "$PROJECT_DIR/FourExHex.csproj" -c Debug            >/dev/null
  dotnet build "$PROJECT_DIR/FourExHex.csproj" -c "$CSHARP_CONFIG" >/dev/null
}

# Sync export_presets.cfg version fields from scripts/AppVersion.cs before an
# export, so a build can never ship a stale preset version.
sync_presets_version() {
  echo "==> Syncing export_presets.cfg version from scripts/AppVersion.cs"
  "$PROJECT_DIR/tools/sync_version.sh"
}
