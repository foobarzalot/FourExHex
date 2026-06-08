#!/usr/bin/env bash
#
# Cross-export a Windows .exe of FourExHex FROM this Mac (x86_64).
#
# This is a PRODUCE-only build: there is no Wine/VM here, so it cannot be
# run or smoke-tested locally — copy build/windows/ to a Windows PC to test.
#
# Notes / gotchas:
#   - Godot's exporter spawns `dotnet publish -r win-x64` from the system
#     dotnet (/usr/local/share/dotnet), which must have the .NET 8 SDK +
#     runtime installed system-wide (same requirement as the macOS build;
#     install once via the official .NET 8 SDK .pkg). The first run also
#     pulls the win-x64 runtime pack from NuGet.
#   - The .pck is embedded in the .exe (binary_format/embed_pck=true), but
#     the .NET runtime + game DLLs still ship as sidecar files next to it —
#     distribute the whole build/windows/ directory, not just the .exe.
#   - No code signing and no custom icon: rcedit isn't installed
#     (application/modify_resources=false), so the .exe uses the default
#     Godot icon. Windows SmartScreen will warn on an unsigned exe; that's
#     expected for a test build.
#
# Usage:  tools/build_windows.sh [debug|release]   (default: debug)
#   debug   -> ExportDebug config, --export-debug
#   release -> ExportRelease config, --export-release
set -euo pipefail

MODE="${1:-debug}"
case "$MODE" in
  debug)   CSHARP_CONFIG="ExportDebug";   GODOT_FLAG="--export-debug" ;;
  release) CSHARP_CONFIG="ExportRelease"; GODOT_FLAG="--export-release" ;;
  *) echo "ERROR: unknown mode '$MODE' (use 'debug' or 'release')" >&2; exit 2 ;;
esac

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GODOT="/Applications/Godot_mono.app/Contents/MacOS/Godot"
PRESET="Windows Desktop"
OUT="$PROJECT_DIR/build/windows/FourExHex.exe"

export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$PATH"

echo "==> Syncing export_presets.cfg version from scripts/AppVersion.cs"
"$PROJECT_DIR/tools/sync_version.sh"

echo "==> Building C# assemblies (Debug for editor load + $CSHARP_CONFIG for the export)"
dotnet build "$PROJECT_DIR/FourExHex.csproj" -c Debug            >/dev/null
dotnet build "$PROJECT_DIR/FourExHex.csproj" -c "$CSHARP_CONFIG" >/dev/null

echo "==> Exporting Windows bundle ($MODE, headless)"
rm -rf "$PROJECT_DIR/build/windows"
mkdir -p "$PROJECT_DIR/build/windows"
"$GODOT" --headless --path "$PROJECT_DIR" "$GODOT_FLAG" "$PRESET" "$OUT"

if [[ ! -f "$OUT" ]]; then
  echo "ERROR: export did not produce $OUT" >&2
  exit 1
fi

echo "==> Built: $(file -b "$OUT")"
echo "==> Done. Copy the whole directory to a Windows PC to run:"
echo "    $PROJECT_DIR/build/windows/"
