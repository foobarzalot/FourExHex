#!/usr/bin/env bash
#
# Build a runnable macOS .app of FourExHex for LOCAL testing on this Mac.
#
# Why this script exists (two non-obvious gotchas it papers over):
#   1. Godot's macOS exporter spawns `dotnet publish` from the system dotnet
#      (/usr/local/share/dotnet). That dotnet must have the .NET 8 SDK +
#      runtime installed system-wide, or GodotTools.BuildLogger (net8.0)
#      fails to load under a newer SDK and the publish dies with exit 1 and
#      an empty MSBuild log. Install once with the official .NET 8 SDK .pkg.
#   2. Godot signs the bundle ad-hoc *with hardened runtime* (flags
#      0x10002). On recent macOS the kernel SIGKILLs an ad-hoc + hardened
#      binary at the exec gate (Killed: 9, no output, no crash report).
#      Hardened runtime is only needed for notarized distribution, so we
#      re-sign plain ad-hoc (flags 0x2) afterward, which runs locally.
#
# Result: build/FourExHex.app, launchable via `open build/FourExHex.app`.
#
# Usage:  tools/build_macos.sh [debug|release]   (default: debug)
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
PRESET="macOS"
OUT="$PROJECT_DIR/build/FourExHex.app"

export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$PATH"

echo "==> Building C# assemblies (Debug for editor load + $CSHARP_CONFIG for the export)"
dotnet build "$PROJECT_DIR/FourExHex.csproj" -c Debug            >/dev/null
dotnet build "$PROJECT_DIR/FourExHex.csproj" -c "$CSHARP_CONFIG" >/dev/null

echo "==> Exporting macOS bundle ($MODE, headless)"
rm -rf "$PROJECT_DIR/build"
mkdir -p "$PROJECT_DIR/build"
"$GODOT" --headless --path "$PROJECT_DIR" "$GODOT_FLAG" "$PRESET" "$OUT"

if [[ ! -x "$OUT/Contents/MacOS/FourExHex" ]]; then
  echo "ERROR: export did not produce $OUT/Contents/MacOS/FourExHex" >&2
  exit 1
fi

echo "==> Re-signing plain ad-hoc (stripping hardened runtime so it runs locally)"
find "$OUT" -type f \( -name "*.dylib" -o -name "*.so" \) -exec codesign --force --sign - {} \; 2>/dev/null
codesign --force --deep --sign - "$OUT" 2>/dev/null
xattr -cr "$OUT"

FLAGS="$(codesign -dv --verbose=2 "$OUT" 2>&1 | grep -i flags || true)"
echo "==> Signature: $FLAGS"
echo "==> Done. Launch with:  open \"$OUT\""
