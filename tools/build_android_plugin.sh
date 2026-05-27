#!/usr/bin/env bash
#
# Build the RotationFix Android plugin AAR and stage it for the app export.
#
# Why this exists: Android's window rotationAnimation can only be set
# programmatically (it has no theme/manifest attribute — aapt rejects
# android:windowRotationAnimation). So a tiny Godot v2 Android plugin
# (android_plugin/rotationfix, a Kotlin GodotPlugin) sets it to JUMPCUT in
# onMainCreate, killing the stretched frame on portrait/landscape rotation.
#
# This compiles that Kotlin into an AAR and copies it into
# addons/rotationfix/bin/{debug,release}/, where the addon's EditorExportPlugin
# (_get_android_libraries) picks it up and links it into the gradle app build.
# The plugin code is build-type independent, so one release AAR fills both slots.
#
# Run this BEFORE tools/build_android.sh whenever the plugin source changes.
# Toolchain: same SDK/JDK as tools/build_android.sh; gradle 8.11.1 via the
# wrapper copied alongside this project.
set -euo pipefail

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PLUGIN_DIR="$PROJECT_DIR/android_plugin"
ADDON_BIN="$PROJECT_DIR/addons/rotationfix/bin"
ANDROID_SDK="${ANDROID_SDK_ROOT:-${ANDROID_HOME:-$HOME/Library/Android/sdk}}"
JAVA_HOME_DEFAULT="/opt/homebrew/opt/openjdk@21/libexec/openjdk.jdk/Contents/Home"

export ANDROID_SDK_ROOT="$ANDROID_SDK"
export ANDROID_HOME="$ANDROID_SDK"
export JAVA_HOME="${JAVA_HOME:-$JAVA_HOME_DEFAULT}"

fail() { echo "ERROR: $1" >&2; exit 1; }
[[ -d "$ANDROID_SDK" ]] || fail "Android SDK not found at $ANDROID_SDK"
[[ -x "$JAVA_HOME/bin/java" ]] || fail "JDK not found at JAVA_HOME=$JAVA_HOME"

echo "==> Building RotationFix AAR (gradle assembleRelease)"
( cd "$PLUGIN_DIR" && ./gradlew :rotationfix:assembleRelease )

AAR="$PLUGIN_DIR/rotationfix/build/outputs/aar/rotationfix-release.aar"
[[ -f "$AAR" ]] || fail "gradle did not produce $AAR"

echo "==> Staging AAR into addons/rotationfix/bin/{debug,release}"
mkdir -p "$ADDON_BIN/debug" "$ADDON_BIN/release"
cp "$AAR" "$ADDON_BIN/debug/RotationFix.aar"
cp "$AAR" "$ADDON_BIN/release/RotationFix.aar"

echo "==> Done:"
ls -la "$ADDON_BIN/debug/RotationFix.aar" "$ADDON_BIN/release/RotationFix.aar"
