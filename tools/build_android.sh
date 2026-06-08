#!/usr/bin/env bash
#
# Build an Android APK of FourExHex from this Mac.
#
# Why this script exists / the non-obvious bits it papers over:
#   1. .NET TFM trap (the big one). Godot 4.6.1's PREBUILT Android template
#      hardcodes net9.0 as the only supported C# target framework, but this
#      project pins net8.0 (and the Godot editor's own runtime is net8.0, so a
#      net9 game assembly would no longer load in the editor / desktop builds).
#      The engine's own advice is "use gradle builds instead": a custom Gradle
#      build runs `dotnet publish` against the project's net8.0 and bundles that
#      runtime into the APK, bypassing the net9 check. Hence
#      gradle_build/use_gradle_build=true in the "Android" preset, and the
#      --install-android-build-template flag below (idempotent; installs the
#      Gradle project under res://android/build/ the first time).
#   2. .NET on Android is 64-bit only. The preset enables arm64-v8a ONLY
#      (armeabi-v7a / x86 / x86_64 off). Re-enabling a 32-bit ABI breaks the
#      C# publish step.
#   3. Signing secrets stay OUT of git. Godot reads keystore credentials from
#      GODOT_ANDROID_KEYSTORE_{DEBUG,RELEASE}_{PATH,USER,PASSWORD} at export
#      time; we source them from a creds file next to the keystores (outside
#      the repo). The export_presets.cfg keystore fields stay empty.
#   4. Godot reads the Android SDK / JDK locations from the editor settings
#      (~/Library/Application Support/Godot/editor_settings-4.6.tres), and the
#      Gradle wrapper (gradle-8.11.1) downloads itself + AGP 8.6.1 deps on the
#      first build (network required once). This script does NOT install the
#      SDK/NDK — it only checks they're present.
#
# Toolchain the SDK must provide (Godot 4.6.1 android build template):
#   - platforms;android-35   (compileSdk/targetSdk 35)
#   - build-tools;35.x        (apksigner + zipalign)
#   - ndk;28.1.13356709       (exact version pinned by config.gradle)
#   - platform-tools          (adb)
#   - cmdline-tools           (sdkmanager + accepted licenses)
#   - JDK >= 17               (JDK 21 satisfies the gradle validateJavaVersion task)
#
# Result: build/android/FourExHex.apk (debug- or release-signed).
#
# Usage:  tools/build_android.sh [debug|release]   (default: debug)
#   debug   -> ExportDebug config, --export-debug   (DEBUG defined, logs/asserts on)
#   release -> ExportRelease config, --export-release (optimized, Conditional logs stripped)
set -euo pipefail

MODE="${1:-debug}"
case "$MODE" in
  debug)   CSHARP_CONFIG="ExportDebug";   GODOT_FLAG="--export-debug" ;;
  release) CSHARP_CONFIG="ExportRelease"; GODOT_FLAG="--export-release" ;;
  *) echo "ERROR: unknown mode '$MODE' (use 'debug' or 'release')" >&2; exit 2 ;;
esac

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GODOT="/Applications/Godot_mono.app/Contents/MacOS/Godot"
PRESET="Android"
OUT="$PROJECT_DIR/build/android/FourExHex-$MODE.apk"
NDK_VERSION="28.1.13356709"
COMPILE_SDK="android-35"

# Default SDK location matches the editor-settings android_sdk_path; override
# with ANDROID_SDK_ROOT / ANDROID_HOME if you installed it elsewhere.
ANDROID_SDK="${ANDROID_SDK_ROOT:-${ANDROID_HOME:-$HOME/Library/Android/sdk}}"
KSDIR="$HOME/Library/Application Support/Godot/keystores"
CREDS="$KSDIR/fourexhex-android-creds.sh"
JAVA_HOME_DEFAULT="/opt/homebrew/opt/openjdk@21/libexec/openjdk.jdk/Contents/Home"

export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$PATH"
export JAVA_HOME="${JAVA_HOME:-$JAVA_HOME_DEFAULT}"

# ---- Fail-fast prerequisite checks (this script does NOT install anything) ----
fail() { echo "ERROR: $1" >&2; exit 1; }

[[ -x "$GODOT" ]] || fail "Godot not found at $GODOT"
[[ -d "$ANDROID_SDK" ]] || fail "Android SDK not found at $ANDROID_SDK (set ANDROID_SDK_ROOT or install it there)"
[[ -x "$ANDROID_SDK/platform-tools/adb" ]] || fail "adb missing — install platform-tools into $ANDROID_SDK"
[[ -d "$ANDROID_SDK/platforms/$COMPILE_SDK" ]] || fail "platform $COMPILE_SDK missing — install platforms;$COMPILE_SDK (compileSdk 35)"
[[ -d "$ANDROID_SDK/ndk/$NDK_VERSION" ]] || fail "NDK $NDK_VERSION missing — install ndk;$NDK_VERSION (exact version pinned by the build template)"
BTDIR="$(ls -d "$ANDROID_SDK"/build-tools/* 2>/dev/null | sort -V | tail -1 || true)"
[[ -n "$BTDIR" && -x "$BTDIR/apksigner" && -x "$BTDIR/zipalign" ]] \
  || fail "build-tools with apksigner+zipalign missing under $ANDROID_SDK/build-tools"
[[ -x "$JAVA_HOME/bin/java" ]] || fail "JDK not found at JAVA_HOME=$JAVA_HOME (need JDK >= 17)"
[[ -f "$CREDS" ]] || fail "keystore creds file missing at $CREDS (run the keystore setup first)"

# ---- Signing credentials (sourced, never committed) ----
# shellcheck source=/dev/null
source "$CREDS"
for v in GODOT_ANDROID_KEYSTORE_DEBUG_PATH GODOT_ANDROID_KEYSTORE_DEBUG_USER GODOT_ANDROID_KEYSTORE_DEBUG_PASSWORD; do
  [[ -n "${!v:-}" ]] || fail "$v not set by $CREDS"
done
if [[ "$MODE" == "release" ]]; then
  for v in GODOT_ANDROID_KEYSTORE_RELEASE_PATH GODOT_ANDROID_KEYSTORE_RELEASE_USER GODOT_ANDROID_KEYSTORE_RELEASE_PASSWORD; do
    [[ -n "${!v:-}" ]] || fail "$v not set by $CREDS (needed for a release build)"
  done
  [[ -f "$GODOT_ANDROID_KEYSTORE_RELEASE_PATH" ]] || fail "release keystore missing at $GODOT_ANDROID_KEYSTORE_RELEASE_PATH"
fi

echo "==> SDK:  $ANDROID_SDK"
echo "==> JDK:  $JAVA_HOME"
echo "==> NDK:  $NDK_VERSION   build-tools: $(basename "$BTDIR")   platform: $COMPILE_SDK"

# The Android export links the RotationFix plugin AAR (via the addons/rotationfix
# EditorExportPlugin); without it the gradle build fails to resolve the
# dependency. The AAR is a build artifact (gitignored under bin/), so build it on
# first run. When the plugin SOURCE changes, rerun tools/build_android_plugin.sh
# by hand to regenerate it.
PLUGIN_AAR="$PROJECT_DIR/addons/rotationfix/bin/release/RotationFix.aar"
if [[ ! -f "$PLUGIN_AAR" ]]; then
  echo "==> RotationFix plugin AAR missing; building it first"
  "$PROJECT_DIR/tools/build_android_plugin.sh"
fi

echo "==> Syncing export_presets.cfg version from scripts/AppVersion.cs"
"$PROJECT_DIR/tools/sync_version.sh"

echo "==> Building C# assemblies (Debug for editor load + $CSHARP_CONFIG for the export)"
dotnet build "$PROJECT_DIR/FourExHex.csproj" -c Debug            >/dev/null
dotnet build "$PROJECT_DIR/FourExHex.csproj" -c "$CSHARP_CONFIG" >/dev/null

echo "==> Exporting Android APK ($MODE, headless, gradle build)"
echo "    (first run downloads gradle 8.11.1 + AGP deps — this can take several minutes)"
mkdir -p "$PROJECT_DIR/build/android"
rm -f "$OUT"
# --install-android-build-template is idempotent: it drops the Gradle project
# into res://android/build/ on first run and is a no-op once present.
"$GODOT" --headless --path "$PROJECT_DIR" --install-android-build-template "$GODOT_FLAG" "$PRESET" "$OUT"

[[ -f "$OUT" ]] || fail "export did not produce $OUT"

echo "==> Built: $(file -b "$OUT")"
echo "==> Done. Install on a connected device with:"
echo "    \"$ANDROID_SDK/platform-tools/adb\" install -r \"$OUT\""
