# RELEASE.md

Release / on-device (Android) testing playbook for FourExHex. Desktop dev is
covered by `CLAUDE.md`; this is the device side — building APKs, installing,
reading logs, and reproducing a device's UI scale locally.

## 1. Building

Three export presets live in `export_presets.cfg`, each with a matching `tools/`
script that builds the C# assemblies and runs a headless Godot export:

| Target  | Command                              | Output                                       |
|---------|--------------------------------------|----------------------------------------------|
| macOS   | `tools/build_macos.sh`               | `build/macos/FourExHex.app`                  |
| Windows | `tools/build_windows.sh`             | `build/windows/FourExHex.exe`                |
| Android | `tools/build_android.sh [debug\|release]` | `build/android/FourExHex-{debug,release}.apk` |

(Android defaults to `debug`.) All three follow the same shape: `dotnet build -c
Debug` (so the editor can load the assembly) **plus** `-c ExportDebug`/`-c
ExportRelease` for the export, then `godot --headless
--export-debug|--export-release <preset> <out>`. Each script's header documents
the platform-specific gotchas it papers over.

### Android prerequisites

The `build_android.sh` fail-fast checks require (it installs nothing): Android
SDK at `~/Library/Android/sdk` with `platforms;android-35`, `build-tools;35.x`,
`ndk;28.1.13356709` (exact), `platform-tools`, `cmdline-tools`; JDK ≥ 17 (the
machine's JDK 21 is fine); and the signing creds file (see Signing below). The
first gradle run downloads Gradle 8.11.1 + AGP 8.6.1 deps (one-time, network;
takes a few minutes).

### The net8-vs-net9 constraint (why Android uses a gradle build)

Godot 4.6.1's **prebuilt** Android template hardcodes **net9.0** as the only
supported C# target framework (the string is baked into the engine binary), but
this project pins **net8.0** across all four csprojs — and the editor's own
runtime (`GodotPlugins`/`GodotTools`) is net8.0, so a net9 game assembly would no
longer load in the editor / desktop builds. Retargeting up is therefore **not**
an option: it re-breaks every desktop path. The engine's own advice is "use
gradle builds instead", so the Android preset sets
**`gradle_build/use_gradle_build=true`** — a custom Gradle build runs `dotnet
publish` against the project's net8.0 and bundles that runtime into the APK,
bypassing the net9 check. `build_android.sh` passes
`--install-android-build-template` (idempotent) so the Gradle project is dropped
into `res://android/build/` on first run (`/android/` is gitignored). .NET on
Android is 64-bit only, so the preset enables **arm64-v8a only** — re-enabling a
32-bit ABI breaks the publish step.

### Signing

Debug and release keystores live **outside the repo** under `~/Library/Application
Support/Godot/keystores/`. Credentials are sourced from a non-committed
`fourexhex-android-creds.sh` into the
`GODOT_ANDROID_KEYSTORE_{DEBUG,RELEASE}_{PATH,USER,PASSWORD}` env vars Godot reads
at export time, so the `export_presets.cfg` keystore fields stay empty and no
secret is committed. Debug and release are signed with **different keys** — see
the signature-mismatch note in §2.

## 2. Installing to a device

`adb` is at `~/Library/Android/sdk/platform-tools/adb` (not on `PATH` — use the
full path). Enable Developer options → USB debugging on the device and accept
the RSA prompt, then:

```
ADB="$HOME/Library/Android/sdk/platform-tools/adb"
"$ADB" devices                                   # confirm it's attached
"$ADB" install -r build/android/FourExHex-debug.apk
```

**Signature-mismatch fallback.** Debug and release are signed with different
keys, so switching between them fails with
`INSTALL_FAILED_UPDATE_INCOMPATIBLE`. Uninstall first (this clears that app's
save data), then install:
```
"$ADB" uninstall net.sitkoff.fourexhex
"$ADB" install build/android/FourExHex-release.apk
```

Launch without needing the activity name:
```
"$ADB" shell monkey -p net.sitkoff.fourexhex -c android.intent.category.LAUNCHER 1
```

## 3. Reading logs on the device

`Log` (`src/FourExHex.Model/Log.cs`) routes through `GD.Print`, which Android
sends to logcat under the `godot` tag:
```
"$ADB" logcat -c                                 # clear buffer first
# …launch + reproduce…
"$ADB" logcat -d -s godot | grep DisplayScale    # one-shot dump + filter
```

**Gotcha:** `FOUREXHEX_LOG` (the desktop log-config env var) can't be set on
Android. So `LogBootstrap` force-enables **every** `Log` category on mobile
builds. In a **release** build `Trace`/`Debug`/`Info` are compiled out, so only
`Warn`/`Error` reach logcat; use a **debug** build to see `Debug`-level
diagnostics (e.g. the capital-actionability line, the `DisplayScale:` line).

## 4. DPI / UI scaling (`DisplayScale`)

`scripts/DisplayScale.cs` (autoload) drives the root `Window.ContentScaleFactor`
to keep UI at a roughly constant physical size across densities. Pure math is in
`src/FourExHex.Model/DisplayScaleMath.cs`:

```
logicalDpi = ScreenGetDpi / max(ScreenGetScale, 1)
factor     = clamp(logicalDpi / 160, 1.0, 3.0)     # 160 = Android mdpi baseline
```

Dividing by the OS display scale recovers logical DPI so retina desktops floor
to 1.0 (unchanged) while phones scale up. `ContentScaleFactor` also sets the GUI
layout size to `window / factor`, so a higher factor means a **smaller logical
viewport** — which is why high-density phones get a cramped portrait layout.

The `DisplayScale:` logcat line (Info on change, Debug otherwise) reports:
`dpi`, `osScale`, `logicalDpi`, `screen`, `window` (physical px), `factor`,
`changed`, `logicalViewport` (= window ÷ factor; what the HUD lays out against).

## 5. Known device data

**Samsung Galaxy S9 (SM-G960U)** — FHD+ mode, debug APK, 2026-05-25:

| Orientation | dpi | osScale | factor | window (phys) | logical viewport |
|-------------|-----|---------|--------|---------------|------------------|
| Portrait    | 480 | 1.35    | 2.22   | 1080 × 2220   | 486 × 999        |
| Landscape   | 480 | 1.80    | 1.67   | 2220 × 1080   | 1332 × 648       |

Notes: Android's `ScreenGetScale` is **not** 1.0 (my original assumption) and
**varies by orientation**, so the factor is device- and orientation-dependent,
not a clean function of density. On the S9 this lands at a usable size; see the
portrait-overlap entry in `TECHDEBT.md` for the open risk on other devices.

## 6. Reproducing a device's scale locally

```
GODOT="/Applications/Godot_mono.app/Contents/MacOS/Godot"
```

**Option A — reproduce the layout (no code, recommended).** On the dev Mac the
factor floors to 1.0, so the window size *is* the logical viewport. Launch at the
device's logical size to get the identical layout (crowding/overlap), just at 1×
physical size.

**S9 quick-launch** (logical viewports from §5; factor 1.0 locally → window ==
logical viewport):
```
GODOT="/Applications/Godot_mono.app/Contents/MacOS/Godot"

# Galaxy S9 PORTRAIT  (device logical 486×999)
"$GODOT" --path . --resolution 486x999

# Galaxy S9 LANDSCAPE (device logical 1332×648)
"$GODOT" --path . --resolution 1332x648
```

Append a scene path to load straight into a screen (omit it for the main menu),
and prefix `FOUREXHEX_LOG` to see the centering / scale logs:
```
# scenes/play_tutorial.tscn (tutorial) · scenes/map_editor.tscn (editor) · scenes/main.tscn (in-game)

# S9 portrait, tutorial, with map-centering logs (RecenterMap: / content box):
FOUREXHEX_LOG="Render:Debug" "$GODOT" --path . scenes/play_tutorial.tscn --resolution 486x999

# S9 landscape, map editor:
"$GODOT" --path . scenes/map_editor.tscn --resolution 1332x648
```
(Use `Display:Debug` instead for the `DisplayScale:` line.) This reproduces the
S9 *logical layout* at 1× — not the physical magnification; for that see Option B
and the real device factors in §5.

**Option B — reproduce the magnification too (not yet implemented).** Godot has
no CLI flag or project-setting override for `ContentScaleFactor`, and the
`DisplayScale` autoload recomputes the factor from DPI on launch anyway. To force
a specific factor locally you'd add an `FOUREXHEX_UI_SCALE` env override in
`DisplayScale.Apply()` (matching the existing `FOUREXHEX_LOG` / `FOUREXHEX_6AI`
convention), then e.g. `FOUREXHEX_UI_SCALE=2.222 "$GODOT" --path . --resolution 1080x2220`
would match the S9 pixel-for-pixel. This knob does not exist yet.
