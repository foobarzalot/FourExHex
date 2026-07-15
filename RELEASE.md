# RELEASE.md

Release / on-device (Android + iOS) testing playbook for FourExHex. Desktop dev
is covered by `CLAUDE.md`; this is the device side — building, installing,
reading logs, and reproducing a device's UI scale locally.

## 1. Building

Four export presets live in `export_presets.cfg`, each with a matching `tools/`
script that builds the C# assemblies and runs a headless Godot export:

| Target  | Command                              | Output                                       |
|---------|--------------------------------------|----------------------------------------------|
| macOS   | `tools/build_macos.sh`               | `build/macos/FourExHex.app`                  |
| Windows | `tools/build_windows.sh`             | `build/windows/FourExHex.exe`                |
| Android | `tools/build_android.sh [debug\|release\|aab]` | `build/android/FourExHex-{debug,release}.apk`, or `build/android/FourExHex-release.aab` (`aab` mode, for Play upload) |
| iOS     | `tools/build_ios.sh [debug\|release] [--tethered\|--no-upload]` | `build/ios/FourExHex.ipa` (+ `.xcodeproj`, `.xcarchive`) |

(Android defaults to `debug`; iOS defaults to `release`.) All follow the same
shape: `dotnet build -c Debug` (so the editor can load the assembly) **plus**
`-c ExportDebug`/`-c ExportRelease` for the export, then `godot --headless
--export-debug|--export-release <preset> <out>`. Each script's header documents
the platform-specific gotchas it papers over. iOS additionally runs `xcodebuild
archive` → `xcodebuild -exportArchive` → either `xcrun devicectl device install`
(tethered) or `xcrun altool --upload-app` (TestFlight); see §1.5 below.

### Versioning

The canonical version lives in **`scripts/AppVersion.cs`** — `Marketing` (a
semver string, e.g. `"1.0"`) and `Build` (a monotonic integer, e.g. `6`). The
Settings panel reads it directly (`AppVersion.Display` → `v1.0 (6)`), so the
displayed value can never be a hand-copied second copy. Bump those two consts to
cut a release.

The two-part scheme maps onto each platform's two version fields in
`export_presets.cfg`:

| Canonical | iOS | Android | Desktop (macOS/Windows) |
|-----------|-----|---------|-------------------------|
| `AppVersion.Marketing` | `application/short_version` (CFBundleShortVersionString) | `version/name` (versionName) | macOS `application/short_version` |
| `AppVersion.Build` | `application/version` (CFBundleVersion) | `version/code` (versionCode) | macOS `application/version` |

**The export presets are auto-synced from `AppVersion`** by
`tools/sync_version.sh`, which every `tools/build_*.sh` runs before exporting —
so a build can never ship a stale preset version. To bump, edit **only**
`scripts/AppVersion.cs`; run `tools/sync_version.sh` (idempotent) to update the
committed `export_presets.cfg` immediately, or just let the next build do it.
Windows needs a 4-part numeric version, so the sync derives a dot-quad
`<major>.<minor>.<patch>.<build>` from Marketing (padded to three parts) plus
Build — e.g. `1.0` + `6` → `1.0.0.6` — and writes it to both
`application/file_version` and `application/product_version`.

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

### App icon assets

The app icon is baked by `tools/build_icon.py` from the in-game palette
(`UiPalette.Gold`, `GameSettings` player-0 red) and the DMSerifDisplay-Regular
font, with the "4X" glyphs converted to SVG paths via `fontTools` (so the
committed SVG has no system-font dependency). Re-run with
`python3 tools/build_icon.py` after editing the script; needs `fonttools` +
`Pillow` in a venv (see the module docstring). After regenerating, run
`godot --headless --path . --import` so the next `build_*.sh` picks up the
new rasters.

Four files are emitted:

| Path | Used by |
|------|---------|
| `icon.svg` (1024×1024, transparent) | macOS, iOS, Windows — via `project.godot`'s `config/icon` and the per-platform presets |
| `assets/icon/android_fg_432.png` | Android — wired in `export_presets.cfg` as `launcher_icons/adaptive_foreground_432x432` |
| `assets/icon/android_bg_432.png` | Android — wired as `launcher_icons/adaptive_background_432x432` |
| `assets/icon/splash_1024.png` (1024×1024, slate) | Godot runtime boot splash on all platforms (`project.godot` `boot_splash/image`) **and** iOS pre-engine launch storyboard (`export_presets.cfg` `storyboard/custom_image@2x` + `@3x`) |

Android gets its own pair because Godot's auto-rasterized adaptive layers
would scale `icon.svg` 1:1 into the 108 dp canvas, pushing the full-bleed
hex outside the 72 dp safe square — every launcher mask then clips the hex
points and heraldic border, leaving only the red interior and "4X" visible
(reproduced on a Samsung S9). The dedicated foreground PNG renders the hex
inside the safe square; the background PNG fills the masked area with the
slate frame.

The splash PNG is *not* the same as `icon.svg` because (a) iOS launch
storyboards take PNG only, no SVG, and (b) the splash needs an opaque
slate background and a smaller hex (~70% of canvas) so the launch screen
has breathing room on portrait phones once iOS Scale-to-Fit stretches the
square image to the short axis. The `boot_splash/bg_color` in
`project.godot` and the storyboard's `custom_bg_color` both match the PNG
bg (`UiPalette.BgDeep` = `#23211d` = `Color(0.137, 0.129, 0.114, 1)`), so
the area iOS or Godot fills around the image is indistinguishable from
the image itself.

### Signing

Debug and release keystores live **outside the repo** under `~/Library/Application
Support/Godot/keystores/`. Credentials are sourced from a non-committed
`fourexhex-android-creds.sh` into the
`GODOT_ANDROID_KEYSTORE_{DEBUG,RELEASE}_{PATH,USER,PASSWORD}` env vars Godot reads
at export time, so the `export_presets.cfg` keystore fields stay empty and no
secret is committed. Debug and release are signed with **different keys** — see
the signature-mismatch note in §2.

The `aab` mode signs with the **release** keystore too; under Play App Signing
that signature is the **upload key** (Google holds the actual app-signing key
and re-signs what devices install). `aab` mode flips the preset's
`gradle_build/export_format` 0→1 for the duration of the export (backup +
EXIT-trap restore, same pattern as the iOS Team-ID injection), so the committed
preset stays APK-format.

## 1.5. iOS prerequisites

`build_ios.sh` fail-fast checks require:

- **Full Xcode at `/Applications/Xcode.app`** — Command Line Tools alone are
  not enough; `xcodebuild` will refuse to archive without the full app. Accept
  the license once: `sudo xcodebuild -license`.
- **Apple Developer Program membership** (paid, $99/yr) — Individual enrollment
  is fine for TestFlight; the seller name shown on the public App Store would
  be the enrolling person's legal name unless DBA / Organization paperwork
  follows later. See `docs/ios-apple-developer-setup.md` for the one-time
  setup runbook (App ID registration, App Store Connect record, API key).
- **App Store Connect API key file** at `~/.appstoreconnect/private_keys/AuthKey_<KeyID>.p8`
  — one of `xcrun altool`'s standard search paths. Generated once via App
  Store Connect → Users and Access → Integrations.
- **Creds file** at `~/Library/Application Support/Godot/keystores/fourexhex-ios-creds.sh`
  (same dir as Android keystores), exporting `ASC_API_KEY_ID`,
  `ASC_API_ISSUER_ID`, `IOS_TEAM_ID`. NOT committed. `chmod 600`.

### iOS Team ID injection (why the committed preset has it empty)

Godot's iOS export REQUIRES a non-empty `application/app_store_team_id` or it
refuses with "App Store Team ID not specified". But the team ID is per-developer
private data that shouldn't ship in `export_presets.cfg`. `build_ios.sh` `cp`s
`export_presets.cfg` to a `.bak.$$` file, `sed`s the real `IOS_TEAM_ID` from the
creds file into the empty placeholder, and runs Godot — then unconditionally
restores the backup on EXIT (trap), so a crashed build never leaves the team ID
checked in. The committed file always has `application/app_store_team_id=""`.

> **Don't `git add` `export_presets.cfg` while a build is running.** During the
> build the file holds the real team ID (and a synced version bump); the EXIT
> trap only restores the team ID *after* the build finishes. If you stage/commit
> a version bump mid-build you'll capture the injected `app_store_team_id` by
> accident. Either commit the version bump **before** launching the build, or
> wait for the build to finish (and the trap to restore the file) before
> `git add`. Also expect a stray `export_presets.cfg.bak.<pid>` if a build is
> killed before its trap runs — it's a temp artifact, safe to delete, and not
> committed.

### iOS targeted_device_family enum gotcha

Godot 4.6.1's iOS preset writes `TARGETED_DEVICE_FAMILY = ""` (empty string) if
the enum value is out of range — which produces an Xcode project that matches
no device family, so Xcode silently refuses to install with "doesn't match any
of FourExHex.app's targeted device families". The right values are
**0=iPhone, 1=iPad, 2=Universal** (writes `"1,2"`). Anything else writes "".
Our preset is `application/targeted_device_family=2`.

### iOS net8 (no net9 issue)

Unlike the Android prebuilt template (which forces net9.0), Godot 4.6.1's iOS
export generates an Xcode project whose own build phases run `dotnet publish`
for the iOS RID against the project's own `net8.0`, so no Gradle-style workaround
is needed. The export DOES print a "Exporting to an Apple Embedded platform when
using C#/.NET is experimental" warning — it's just a warning; the export
succeeds. No `dotnet workload install ios` was needed; Godot's iOS export uses
the system .NET (10.x at `/usr/local/share/dotnet`) for the publish step, and
the project's own `$HOME/.dotnet` (8.x) for the editor / desktop builds. Both
pipelines coexist.

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
"$ADB" uninstall com.foobarzalot.fourexhex
"$ADB" install build/android/FourExHex-release.apk
```

Launch without needing the activity name:
```
"$ADB" shell monkey -p com.foobarzalot.fourexhex -c android.intent.category.LAUNCHER 1
```

### iOS — tethered USB install

```
tools/build_ios.sh debug --tethered
```

Prereqs once per device: plug in via USB, accept "Trust This Computer" on the
device, enable **Settings → Privacy & Security → Developer Mode** (only appears
after the first `xcrun devicectl list devices` from this Mac), restart, confirm
Developer Mode again. `xcrun devicectl device info details --device <UDID>`
should show `developerModeStatus: enabled`.

The script auto-detects the first paired USB device. If `xcodebuild archive`
errors with **"iOS X.Y is not installed"** even though `xcodebuild -showsdks`
lists the SDK: the iPhone is on a newer point-release of the same iOS minor
than Xcode's bundled SDK (build number mismatch, e.g. device on `23F77`, Xcode
SDK on `23F73`). Workaround: open `build/ios/FourExHex.xcodeproj` in Xcode UI,
pick the device, hit Cmd+R — Xcode auto-downloads the matching device-support
files. Real fix: update Xcode to a matching point-release.

If Xcode complains **"Build input file cannot be found: ... .mobileprovision"**
on a fresh sign-in, it's a stale provisioning-profile UUID cached in
DerivedData. Wipe and retry: `rm -rf ~/Library/Developer/Xcode/DerivedData/FourExHex-*`.

### iOS — TestFlight upload (App Store Connect)

```
tools/build_ios.sh release
```

Same build, but the script swaps `method=app-store-connect` into the
`tools/ios_export_options.plist`, skips the `devicectl install` step, and runs
`xcrun altool --upload-app` against the API key found at
`~/.appstoreconnect/private_keys/AuthKey_<KeyID>.p8`. The build then sits in
App Store Connect → My Apps → FourExHex → TestFlight under "Processing" for
~15–30 min before becoming available to internal testers. External testers
require a one-time Beta App Review (~24h) on the first build.

**Signing fix in the archive step** — Godot's iOS exporter hardcodes
`CODE_SIGN_IDENTITY = "Apple Distribution"` into the Release config of
the generated `.xcodeproj`, alongside `CODE_SIGN_STYLE = "Automatic"`.
xcodebuild's automatic archive signing picks "Apple Development" by
default and treats the explicit "Apple Distribution" as a conflicting
manual override, failing with *"FourExHex has conflicting provisioning
settings."* `build_ios.sh` works around this by `sed`-stripping the
hardcoded identity right after Godot exports, so archive auto-signs
with Development and `xcodebuild -exportArchive` re-signs with
Distribution per the export options plist (the canonical Apple
distribution workflow).

**Bump the build number for every TestFlight upload.** App Store
Connect rejects duplicate `CFBundleVersion` values within the same
`CFBundleShortVersionString`. Before each `tools/build_ios.sh release`,
increment `AppVersion.Build` in `scripts/AppVersion.cs` (the canonical
source — `build_ios.sh` syncs it into the iOS preset's
`application/version` automatically; see §1 Versioning). Apple Apple ID
`6774765597`, bundle `com.foobarzalot.fourexhex`.

`tools/build_ios.sh debug --no-upload` does the local build through .ipa and
stops — useful for verifying signing locally without burning an upload slot.

**Internal-group setup is web-UI-only (one-time).** Apple's API
doesn't expose `isInternalGroup` as a writable attribute on
`POST /v1/betaGroups`, so the first internal beta group must be
created via App Store Connect → FourExHex → TestFlight → Internal
Testing → "+". Add team users from the dropdown. Once the group
exists, every subsequent operation (add/remove builds, add/remove
testers, query state) is fully API-driven via `tools/asc_api.sh`.

**App Store Connect helpers:**
- `tools/asc_api.sh` — mints a JWT from the same creds `build_ios.sh`
  uses and runs `curl` against `api.appstoreconnect.apple.com/v1$1`.
  Never prints credentials. Usage: `tools/asc_api.sh /apps`, or
  `tools/asc_api.sh -X POST -H 'Content-Type: application/json' -d '{...}' /betaGroups`.
- `tools/check_testflight_status.sh` — one-shot read of the most
  recent build's `processingState` (PROCESSING / VALID / INVALID /
  EXPIRED). Useful for poll loops between upload and tester
  visibility.

### Android — Google Play internal testing

The Android counterpart of TestFlight. One-time setup (Play Console account,
app record, first manual upload, service account) is the runbook in
**`docs/android-play-console-setup.md`**; after that, every release is:

```
tools/build_android.sh aab      # release-signed App Bundle (Play takes .aab, not .apk)
tools/upload_play.sh            # upload + roll out to the internal track
```

`upload_play.sh` drives the Play Developer API's edits flow (open edit →
upload bundle → point the `internal` track at the new versionCode → commit) and
deletes the edit on failure so retries start clean. Internal-track rollouts are
live immediately — no review, no processing delay. Testers install via the
opt-in link (Play Console → Testing → Internal testing → Testers); the link is
stable across builds. Bump `AppVersion.Build` before each upload — Play rejects
a versionCode it has already seen.

**Google Play helpers:**
- `tools/play_api.sh` — mints an OAuth token from the service-account
  JSON at `~/Library/Application Support/Godot/keystores/fourexhex-play-service-account.json`
  (override with `PLAY_SERVICE_ACCOUNT_JSON`) and runs `curl` against
  `androidpublisher.googleapis.com/androidpublisher/v3/applications/com.foobarzalot.fourexhex$1`.
  `tools/play_api.sh --token` prints a bare token for callers hitting
  other endpoints (the bundle-upload URL lives under `/upload/…`).
  Never prints credentials.
- `tools/check_play_status.sh` — one-shot read of the internal track's
  releases (`versionCodes` + `status`), via a throwaway edit. Exit ≠ 0
  on auth/API errors, so it works in poll loops like the TestFlight one.

## 3. Reading logs on the device

`Log` (`src/FourExHex.Model/Log.cs`) routes through `GD.Print`, which Android
sends to logcat under the `godot` tag:
```
"$ADB" logcat -c                                 # clear buffer first
# …launch + reproduce…
"$ADB" logcat -d -s godot | grep DisplayScale    # one-shot dump + filter
```

iOS routes `GD.Print` to the device's unified logging system. Easiest read is
**Console.app** on the Mac with the device connected: filter the `FourExHex`
process and search for `DisplayScale:` / `SafeArea:` / `[hitch]` etc. The CLI
equivalent is `idevicesyslog` (Homebrew: `brew install libimobiledevice`) or
`xcrun devicectl device process launch --console --device <UDID> com.foobarzalot.fourexhex`
which attaches stdout to the foreground terminal.

**Gotcha:** `devicectl ... --console` ties the on-device app's lifecycle to the
host terminal session — killing the local `devicectl` process (Ctrl-C, pkill,
or closing the terminal) **also terminates the app on the phone**. For a
hands-off log capture that survives, prefer `idevicesyslog | grep ...` (or tap
the app from the home screen and read Console.app separately) so the host- and
device-side processes are independent.

**Gotcha:** `FOUREXHEX_LOG` (the desktop log-config env var) can't be set on
Android or iOS. So `LogBootstrap` force-enables **every** `Log` category on
mobile builds. In a **release** build `Trace`/`Debug`/`Info` are compiled out,
so only `Warn`/`Error` reach the device log; use a **debug** build to see
`Debug`-level diagnostics (e.g. the `DisplayScale:` / `SafeArea:` lines).

## 4. DPI / UI scaling (`DisplayScale`)

`scripts/DisplayScale.cs` (autoload) drives the root `Window.ContentScaleFactor`
to keep UI at a roughly constant physical size across densities. Pure math is in
`src/FourExHex.ViewMath/DisplayScaleMath.cs`:

```
# Desktop (FactorForDpi):
logicalDpi = ScreenGetDpi / max(ScreenGetScale, 1)
factor     = clamp(logicalDpi / 160, 1.0, 3.0)     # 160 = Android mdpi baseline

# Mobile (FactorForRawMobileDpi — OS.HasFeature("mobile")):
factor     = clamp(ScreenGetDpi / 180, 2.2222, 3.0)  # 180 = S9-parity reference
```

Dividing by the OS display scale recovers logical DPI so retina desktops floor
to 1.0 (unchanged) while phones scale up. `ContentScaleFactor` also sets the GUI
layout size to `window / factor`, so a higher factor means a **smaller logical
viewport** — which is why high-density phones get a cramped portrait layout.

The `DisplayScale:` logcat line (Info on change, Debug otherwise) reports:
`dpi`, `osScale`, `logicalDpi`, `screen`, `window` (physical px), `factor`,
`changed`, `logicalViewport` (= window ÷ factor; what the HUD lays out against).

## 5. Known device data

On mobile, the effective `factor` is `max(naturalFactor, MobileMinFactor)`
where `naturalFactor = rawDpi / 180` (`FactorForRawMobileDpi`) and
`MobileMinFactor` is currently
**2.2222** (S9-portrait parity — see `DisplayScaleMath.cs`). So natural values
below the floor are lifted; values above are unaffected. The tables below
show the **post-floor** factor that actually drives `ContentScaleFactor`, with
the natural value in parentheses where the floor clamps.

**Samsung Galaxy S9 (SM-G960U)** — FHD+ mode, debug APK, captured 2026-05-25;
factor recomputed under current floor 2.2222:

| Orientation | dpi | osScale | factor (natural) | window (phys) | logical viewport |
|-------------|-----|---------|-------------------|---------------|------------------|
| Portrait    | 480 | 1.35    | 2.22              | 1080 × 2220   | 486 × 999        |
| Landscape   | 480 | 1.80    | 2.22 (1.67)       | 2220 × 1080   | 1000 × 486       |

Notes: Android's `ScreenGetScale` is **not** 1.0 (my original assumption) and
**varies by orientation**, so the natural factor is device- and orientation-
dependent, not a clean function of density. S9-portrait sits exactly on the
floor; S9-landscape was lifted from 1.67 → 2.22 when the floor moved to
S9-portrait parity (this collapsed the landscape buy palette via the
`FullBuyRowWidthLandscape=1300px` threshold — see `HudView`).

**iPhone 13 mini (iPhone14,4)** — iOS 26.5 (build 23F77), debug build,
captured 2026-06-11 via `idevicesyslog | grep -E "DisplayScale:|SafeArea:"`
under the `FactorForRawMobileDpi` formula (raw 476 dpi / 180 = 2.64, above
the 2.2222 floor so the floor doesn't clamp):

| Orientation | dpi | osScale | factor (natural) | window (phys) | logical viewport | safe insets (t,b,l,r) |
|-------------|-----|---------|-------------------|---------------|------------------|------------------------|
| Portrait    | 476 | 3       | 2.64              | 1125 × 2436   | 425 × 921        | 150, 102, 0, 0          |
| Landscape   | 476 | 3       | 2.64              | 2436 × 1125   | 921 × 425        | 0, 60, 150, 150         |

Notes: the mobile factor formula is `rawDpi / MobileReferenceDpi (180)`
(`DisplayScaleMath.FactorForRawMobileDpi`) — NOT the desktop
`logicalDpi / 160` path, which divides by iOS's `ScreenGetScale` of 3 and
would floor the iPhone to the 2.2222 clamp (the pre-2026-06 behavior that
produced the old 507×1097 figure). At factor 2.64 the 13 mini's logical
viewport is **425×921** — noticeably narrower than the S9's 486×999, which
is what makes long tutorial strings wrap to 4 lines there (#18). The safe
insets column is physical px. The landscape `(L, R) = (150, 150)` insets are
the iPhone notch sitting on the rotated edge; the HUD bars currently only
consume top/bottom safe insets, so in landscape the bars draw under the notch
on the left/right edges — tracked as part of GitHub issue #3.

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

# iPhone 13 mini PORTRAIT  (device logical 425×921)
"$GODOT" --path . --resolution 425x921

# iPhone 13 mini LANDSCAPE (device logical 921×425)
"$GODOT" --path . --resolution 921x425
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

**Option B — reproduce the magnification too.** `DisplayScale.Apply()` honors a
`FOUREXHEX_UI_SCALE` env var that bypasses the DPI computation and forces a
specific factor. Combined with `--resolution` (physical window size), this
reproduces a device's pixel-for-pixel layout on the dev Mac:

```
GODOT="/Applications/Godot_mono.app/Contents/MacOS/Godot"

# iPhone 13 mini PORTRAIT  (physical 1125×2436 at factor 2.644 → logical 425×921)
FOUREXHEX_UI_SCALE=2.644 "$GODOT" --path . --resolution 1125x2436

# iPhone 13 mini LANDSCAPE (physical 2436×1125 at factor 2.644)
FOUREXHEX_UI_SCALE=2.644 "$GODOT" --path . --resolution 2436x1125

# Galaxy S9 PORTRAIT  (physical 1080×2220 at factor 2.222)
FOUREXHEX_UI_SCALE=2.222 "$GODOT" --path . --resolution 1080x2220

# Galaxy S9 LANDSCAPE (physical 2220×1080 at factor 1.667)
FOUREXHEX_UI_SCALE=1.667 "$GODOT" --path . --resolution 2220x1080
```

Prefix `FOUREXHEX_LOG="Display:Debug"` to see the `DisplayScale:` line confirm
the override fired (`override=<value>` is set instead of `<none>`). The
override takes precedence over both the DPI path and the mobile floor, on any
platform.
