#!/usr/bin/env bash
#
# Build the RotationFix / FileOpen / MailCompose Android plugin AARs and stage
# them for the app export.
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

source "$(dirname "${BASH_SOURCE[0]}")/_build_common.sh"
PLUGIN_DIR="$PROJECT_DIR/android_plugin"

export ANDROID_SDK_ROOT="$ANDROID_SDK"
export ANDROID_HOME="$ANDROID_SDK"
export JAVA_HOME="${JAVA_HOME:-$JAVA_HOME_DEFAULT}"

[[ -d "$ANDROID_SDK" ]] || fail "Android SDK not found at $ANDROID_SDK"
[[ -x "$JAVA_HOME/bin/java" ]] || fail "JDK not found at JAVA_HOME=$JAVA_HOME"

echo "==> Building RotationFix + FileOpen + MailCompose AARs (gradle assembleRelease)"
( cd "$PLUGIN_DIR" && ./gradlew :rotationfix:assembleRelease :fileopen:assembleRelease :mailcompose:assembleRelease )

# module-dir:addon-dir:AAR-name — plugin code is build-type independent, so
# one release AAR fills both the debug and release slots.
for spec in "rotationfix:rotationfix:RotationFix" "fileopen:fileopen:FileOpen" \
            "mailcompose:mailcompose:MailCompose"; do
  MODULE="${spec%%:*}"; rest="${spec#*:}"; ADDON="${rest%%:*}"; NAME="${rest#*:}"
  AAR="$PLUGIN_DIR/$MODULE/build/outputs/aar/$MODULE-release.aar"
  [[ -f "$AAR" ]] || fail "gradle did not produce $AAR"
  ADDON_BIN="$PROJECT_DIR/addons/$ADDON/bin"
  echo "==> Staging $NAME.aar into addons/$ADDON/bin/{debug,release}"
  mkdir -p "$ADDON_BIN/debug" "$ADDON_BIN/release"
  cp "$AAR" "$ADDON_BIN/debug/$NAME.aar"
  cp "$AAR" "$ADDON_BIN/release/$NAME.aar"
  ls -la "$ADDON_BIN/debug/$NAME.aar" "$ADDON_BIN/release/$NAME.aar"
done

echo "==> Done."
