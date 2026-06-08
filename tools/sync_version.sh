#!/usr/bin/env bash
#
# Sync the per-platform version fields in export_presets.cfg from the single
# canonical source, scripts/AppVersion.cs (Marketing string + Build int).
#
# Why this exists: export_presets.cfg holds the version twice per platform
# (iOS short/bundle, Android name/code, macOS short/version), with no source of
# truth, so they drifted (issue #32). AppVersion.cs is now canonical; this
# rewrites the preset fields to match so a single bump there updates every
# target. Idempotent — re-running with no AppVersion change is a no-op diff.
#
# The build_*.sh scripts call this before exporting, so a build can never ship
# a stale preset version. Run it standalone after bumping AppVersion.cs to keep
# the committed export_presets.cfg in sync.
#
# Schema mapping (see also AppVersion.cs / RELEASE.md §Versioning):
#   Marketing -> iOS application/short_version (CFBundleShortVersionString)
#             -> Android version/name          (versionName)
#             -> macOS application/short_version
#   Build     -> iOS application/version       (CFBundleVersion)
#             -> Android version/code          (versionCode)
#             -> macOS application/version
# (Windows uses a different dot-quad file_version/product_version format and is
#  left untouched — currently unset; wire it here if Windows shipping starts.)
set -euo pipefail

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APPVER="$PROJECT_DIR/scripts/AppVersion.cs"
PRESETS="$PROJECT_DIR/export_presets.cfg"

[[ -f "$APPVER" ]]  || { echo "ERROR: $APPVER not found" >&2; exit 1; }
[[ -f "$PRESETS" ]] || { echo "ERROR: $PRESETS not found" >&2; exit 1; }

MARKETING="$(grep -oE 'Marketing[[:space:]]*=[[:space:]]*"[^"]*"' "$APPVER" | sed -E 's/.*"([^"]*)".*/\1/')"
BUILD="$(grep -oE 'Build[[:space:]]*=[[:space:]]*[0-9]+' "$APPVER" | grep -oE '[0-9]+$')"

[[ -n "$MARKETING" ]] || { echo "ERROR: could not parse 'Marketing = \"...\"' from $APPVER" >&2; exit 1; }
[[ -n "$BUILD" ]]     || { echo "ERROR: could not parse 'Build = <int>' from $APPVER" >&2; exit 1; }

# Anchored at line start so application/short_version is never matched by the
# application/version rule, and Windows' application/{file,product}_version are
# left alone. macOS and iOS both carry application/short_version + /version and
# both want the same value, so the global substitution is correct for both.
sed -i '' -E \
  -e "s|^application/short_version=\".*\"|application/short_version=\"${MARKETING}\"|" \
  -e "s|^application/version=\".*\"|application/version=\"${BUILD}\"|" \
  -e "s|^version/name=\".*\"|version/name=\"${MARKETING}\"|" \
  -e "s|^version/code=.*|version/code=${BUILD}|" \
  "$PRESETS"

echo "==> Synced export_presets.cfg to AppVersion: marketing=${MARKETING} build=${BUILD}"
