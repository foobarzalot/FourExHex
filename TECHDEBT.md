# Tech Debt

Running list of known issues, flaky tests, and shortcuts that should eventually be cleaned up. Add new entries at the top.

## iPhone HUD undersized: DisplayScale floors `factor` to 1.0 below 160 baseline

**Where:** discovered 2026-05-30 reading the first device-capture of
`DisplayScale:` from an iPhone 13 mini (iPhone14,4, iOS 26.5; numbers
in `RELEASE.md` §5).

**Symptom:** the iPhone reports `dpi=476` / `osScale=3`, so
`DisplayScaleMath` computes `logicalDpi = 476 / 3 ≈ 158.67` — *just* under
the 160 mdpi baseline. `clamp(logicalDpi / 160, 1.0, 3.0)` therefore floors
the factor to **1.0**, meaning no UI upscale. Bar height stays at the
authored 96 logical px = 96 *physical* px ≈ 5 mm tall on the device — under
Apple's HIG 44 pt (~7 mm) touch-target minimum. The Android baseline (which
160 was chosen for) is mdpi; iPhones at 158.67 logical DPI fall on the
"barely below baseline" side of the cliff by coincidence.

**Why this is debt:** the safe-area work made the bars *placed correctly*
on iPhone — slate fills the notch / home-indicator zones, content sits in
the safe area. But "correctly placed and undersized" still reads as
"unusably small" to a player.

**Candidate fixes:**
- (a) Lower `DisplayScaleMath.ReferenceDpi` from 160 to ~140 so 158.67
  scales to ≈ 1.13. Risk: changes the desktop floor — would a 144-dpi
  desktop monitor start upscaling? Verify with a `dotnet test` sweep of
  the existing `DisplayScaleMath` tests + a check on a hi-DPI Mac.
- (b) Add a per-platform multiplier in `DisplayScale.Apply`: on iOS,
  multiply the computed factor by ~1.3 to lift typical iPhones into the
  Android-comparable range. Localizes the change to mobile.
- (c) Raise `MinFactor` only when `OS.HasFeature("ios")`, so iPhones
  always scale up at least 1.2× regardless of logical DPI. Simplest gate.

Defer-pending-decision: which lever to pull is a design call — the user
will see whichever choice picks the on-screen size they want. Capture an
intentional on-device screenshot at each candidate factor before deciding.

**Severity:** functional but cramped — the app is playable on iPhone, but
HUD interactions are awkward at design-size on a 5.4" screen.

## iOS landscape: side-notch L/R safe insets not consumed by HUD bars

**Where:** discovered 2026-05-30 reading the first device-capture of
`SafeArea:` from an iPhone 13 mini in landscape (`RELEASE.md` §5).

**Symptom:** iPhone landscape reports `insets=(t=0, b=60, l=150, r=150)`
— the notch sits on the rotated screen edge. The current safe-area
plumbing only feeds top/bottom into `MakeBarPanel`/`MakeBarFrame`'s
`safeAreaTop`/`safeAreaBottom` params. The landscape bottom bar covers
the home indicator correctly (b=60 → 60 logical px slate at the bottom),
but the bar itself extends edge-to-edge horizontally, so its left and
right ~150 px sit under the notch's curved cutout. The clusters anchored
to the left (`_actionCluster` + gold chip) and right (`_undoCluster` +
controls + options) of the landscape bar can therefore be visually
clipped or thumb-unreachable on a notched iPhone in landscape.

**Why this is debt:** the safe-area architecture covers the obvious
orientation pair (notch on top in portrait, home indicator on bottom in
either orientation), but side-notch landscape was deliberately deferred
when the work landed — see commit `4cc09a6` description. The exact
landscape sub-orientation that puts the notch on a side
(`UIInterfaceOrientationLandscapeLeft` vs `LandscapeRight`) determines
which side is non-zero, but in our case the symptom is symmetric (both
L and R were 150 in the capture).

**Candidate fix:** plumb `safeAreaLeft` / `safeAreaRight` through
`MakeBarPanel` (shifts the bar's `AnchorLeft`/`AnchorRight` inward) and
`MakeBarFrame` (offsets the content inset by the side amount). Map
insets in `ComputeInsets` would also need a `(Left, Right)` extension —
currently `MapInsets` is `(Top, Bottom)` only, so this involves widening
the model record and the consumer (HexMapView's pan-clamp). Bigger
change than the top/bottom work; worth doing once we decide it's a
priority.

**Severity:** cosmetic-plus — buttons on the notched-side edge of a
landscape phone may be partly obscured or hard to reach. Portrait
unaffected.

## iPhone first on-device install: visual screenshot comparison still open

**Where:** original entry added 2026-05-29 after the first tethered iOS
install. Partial closeout 2026-05-30: the `RELEASE.md` §5 device-data row
for iPhone 13 mini is filled in (Console-app/idevicesyslog capture via the
new IosLog mirror), and the `factor=1` + landscape-side-notch findings have
their own dedicated TECHDEBT entries above. What's still open:

- A side-by-side portrait + landscape screenshot of the HUD on iPhone
  alongside the desktop reference, just to eyeball that seed-label
  position / cluster spacing / bar borders all read correctly. The
  user reports "playable" + the one mid-game screenshot we have (the
  one that revealed the Save failed dialog) showed the safe-area wrap
  working, but a side-by-side hasn't been done.
- The original portrait-overlap entry below (narrow logical width on
  high-DPI phones) — not actually a portrait-overlap on iPhone *because*
  factor floored to 1 (logical viewport = full 1125 wide). Confirmed
  not-an-issue on iPhone 13 mini specifically. Other iPhones with
  higher logical DPI (Pro Max models near 460 dpi÷3 ≈ 153) would also
  floor to factor 1 and not overlap.

**Severity:** mostly cosmetic — the install pipeline works, the app runs
saves work, and the unsurprising on-device numbers landed in RELEASE.md.

## iOS SDK build vs device OS build mismatch breaks `xcodebuild archive`

**Where:** discovered 2026-05-29 during the first tethered install. `xcodebuild
archive -destination "generic/platform=iOS"` refused with "iOS 26.5 is not
installed. Please download and install the platform from Xcode > Settings >
Components." even though `xcodebuild -showsdks` listed `iOS 26.5`.

**Root cause:** Xcode's bundled iOS SDK is for build `23F73`; the iPhone is
running iOS 26.5 build `23F77` (a later point-release with the same minor
version number). xcodebuild's strict-match logic refused the destination on
the build-number mismatch, and `xcodebuild -downloadPlatform iOS` downloads the
**Simulator** runtime (8.5 GB), not the **device** platform / DDI we actually
needed.

**Workaround in use:** open `build/ios/FourExHex.xcodeproj` in Xcode UI, pick
the device from the destination dropdown, hit Cmd+R. Xcode then offers to
download the matching device-support files interactively (smaller download,
~500 MB) and proceeds. `tools/build_ios.sh --tethered` works once the support
files are in place; this Xcode-UI step is a one-time-per-iOS-build fixup.

**Candidate proper fixes:** (1) check Mac App Store / Xcode for a point-release
that bundles support for the device's build; (2) extend `build_ios.sh` to
detect the "iOS X is not installed" error and call `xcodebuild
-downloadPlatform iOS-device` (if Apple ships such an option in a future Xcode)
or print a clear "open in Xcode to fetch DDI" hint; (3) explore whether
`xcrun xcdevice` or the CoreDevice service can pre-fetch the DDI from CLI.

**Severity:** blocks `tools/build_ios.sh --tethered` whenever the device gets
an iOS point-update ahead of the installed Xcode. Recoverable via Xcode UI in
a few minutes.

## Per-capture frame hitch on Android: scene renders ~6,500 draw calls/frame [RESOLVED 2026-05-27]

**RESOLUTION:** fixed by cutting per-frame draw calls **~6,550 → ~180–256** on
the S9. Three changes in `HexMapView`: (1) borders + outlines now draw via a
single batched `DrawMultiline`/`DrawMultilineColors` per layer (`PolylineBatch`)
instead of one antialiased `DrawPolyline` per segment; (2) project 2D MSAA enabled
(`rendering/anti_aliasing/quality/msaa_2d=1`, 2×) to keep the now-non-AA lines
smooth; (3) the ~1,870 static water + shoreline-foam `Polygon2D` baked into one
vertex-colored `TriangleSoup` (via `RenderingServer.CanvasItemAddTriangleArray`) =
one draw call. Device-confirmed: the per-capture stall went from a cluster of 2–3
frames (~300 ms total) to a single ~60–75 ms frame; user reports captures now feel
instant. **Residual (optional future work):** that remaining ~60–75 ms single
frame is now CPU-bound (`cpuProc` ~20–48 ms), largely `RefreshOccupantVisuals`
recreating all occupant nodes on every refresh — make it incremental if it ever
becomes perceptible again. Tile fills (~344 `Polygon2D`) could also become a
`MultiMesh` but weren't the bottleneck.

The original problem and (now-validated) diagnosis are kept below for context.

**Where:** whole-map rendering in `HexMapView` (`scripts/HexMapView.cs`). Reported
on the S9 as a hard hitch when placing a unit to capture a hex (even an empty hex,
even with SFX off). Diagnosed 2026-05-27. **The first hypothesis (Line2D node
churn) was wrong** — see below; keeping the corrected diagnosis here.

**Device measurement (debug APK, logcat `[hitch]`):** every capture produces a
cluster of **2–3 long frames, ~55 + ~130 + ~110 ms ≈ 300 ms total**, with
`draws=~6550 objs=~6800` per frame (`Performance` monitors via the `_Process`
delta>50ms probe). The S9 runs continuously (no `low_processor_mode`), so these
are real stalls. The synchronous C# cost is ~1 ms — **not** the bottleneck.

**Root cause:** the scene contains ~2,230 static-ish `Polygon2D` plus ~2,060
antialiased border/outline line draws, and in the `gl_compatibility` 2D renderer
**every visible canvas item issues its own draw call every frame** — `Polygon2D`
and antialiased lines do **not** batch (confirmed: `draws ≈ objs`). Composition
dump (`[hitch] composition …`, `DumpSceneComposition`): `Polygon2D=2216,
PolylineBatch=2`. Breakdown of the ~6,500 draws:
- ~1,870 `Polygon2D` — water-rim hexes + per-edge shore foam + corner foam disks
  (`AddShoreFoamStrips`/`AddCornerFoamDisk`/`CreateWaterHexVisual`). **Static —
  never change after init**, yet redrawn every frame.
- ~344 `Polygon2D` — tile fills (recolored on capture).
- ~2,060 antialiased line draws — `DrawTerritoryBorders` (~1,720) +
  `PopulateOutlinesLayer` (~344), now in two `PolylineBatch` `_Draw` nodes but
  still one `DrawPolyline(antialiased:true)` per segment ⇒ one draw call each.
- remainder — occupants (capitals/trees/units), recreated wholesale every
  `RefreshOccupantVisuals` (i.e. every click), which dirties the canvas.

**Already done (2026-05-27):** the border/outline `Line2D`-per-edge swarm was
collapsed into two `PolylineBatch` `_Draw` nodes. This cut the C# rebuild cost
~10× (`HandleCapture` ~8 ms → ~1 ms) and removed ~4,400 scene nodes — **worth
keeping** — but did **not** reduce draw calls (AA `DrawPolyline` is still 1 draw
per segment), so the device hitch was unchanged.

**Candidate fixes (deferred — measured, not yet fixed; all reduce draw calls):**
- **Lines → `DrawMultiline`:** borders are all one color/width, outlines are
  per-tile colored — `DrawMultiline`/`DrawMultilineColors` collapses ~2,060 draws
  to ~2. Trade-off: loses per-line AA; pair with project 2D MSAA
  (`rendering/anti_aliasing/quality/msaa_2d`) to keep edges smooth.
- **Bake static water + foam:** ~1,870 `Polygon2D` never change — bake to a single
  sprite/texture (or merge into one mesh / `MultiMesh`) ⇒ ~1,870 draws → 1.
- **Tile fills → `MultiMesh`** (~344 → 1); recolor via per-instance color.
- **Incremental occupants:** stop recreating all occupant nodes on every refresh
  (only touch what changed) so a click/capture doesn't dirty the whole canvas.

**Build-config note:** the Android `ExportDebug` build compiles
`FourExHex.Model` **without** the `DEBUG` symbol, so `Log.Trace/Debug/Info` (and
the body of `Log.Since`) are stripped *inside the Model assembly* — `Log.Since`
timing lines are silently no-ops on device, while direct `Log.Debug` calls made
from the game assembly (`scripts/`) still print. Worth aligning the export configs
to define `DEBUG` for all libs if on-device sub-step timing is needed.

**Instrumentation in place:** `[hitch]`-prefixed `Log.Capture` timing lines
(`Log.Since`), the `Log.Render` long-frame probe with CPU/draw-call split in
`HexMapView._Process`, and `DumpSceneComposition`. Free in Release
(`[Conditional("DEBUG")]`); on a debug APK every category is on, so
`adb logcat -d -s godot | grep '\[hitch\]'` reproduces these numbers on-device.
The `Log.Stamp()`/`Log.Since()` helper lives in `src/FourExHex.Model/Log.cs`.

## Rotation-stretch worked around with a heuristic blank overlay (RotationFix plugin)

**Where:** `android_plugin/rotationfix/` (Kotlin `RotationFixPlugin`, a Godot v2
Android plugin), wired in via `addons/rotationfix/` (`plugin.cfg` +
`rotation_fix_export.gd` `EditorExportPlugin._get_android_libraries`), enabled in
`project.godot` `[editor_plugins]`. AAR built by `tools/build_android_plugin.sh`
and auto-built by `tools/build_android.sh` if missing. Added 2026-05-27.

**Root cause (confirmed via logcat):** on portrait↔landscape rotation Android
calls `startFreezingDisplayLocked` and shows a snapshot of the OLD-orientation
frame **stretched** into the NEW screen bounds until the app redraws — the single
distorted frame. The snapshot is taken BEFORE the app is notified (config change /
surface resize), so nothing on the Godot side can pre-empt it. Our own resize
handling already settles in one frame (~6ms — see the `resize@frame`/`settled`
Render logs in `OrientationHud.OnViewportResized` / `HexMapView.OnViewportResized`),
so the stretch is purely this OS freeze.

**Why the clean fixes don't work:** there is no `android:windowRotationAnimation`
theme/manifest attribute (aapt rejects it). Setting it programmatically to
`JUMPCUT` does nothing (the stretch is a freeze, not an animation). The only mode
that skips the snapshot, `SEAMLESS`, requires an OPAQUE fullscreen window —
Godot's Android GL `SurfaceView` forces the window `fmt=TRANSLUCENT` and a plugin
cannot override it (tried `setFormat(OPAQUE)` in both `onMainCreate` and
`onGodotMainLoopStarted`; dumpsys still reports TRANSLUCENT). Properly removing the
snapshot would need an engine patch to make the render surface opaque.

**The workaround (heuristic):** the plugin watches the physical orientation
sensor (`OrientationEventListener`) — the only signal that arrives before the
freeze — and on a band crossing drops an opaque black `TYPE_APPLICATION_PANEL`
window over the surface, so the snapshot is of black (stretched black = invisible).
Removed `DISPLAY_SETTLE_MS` (600ms, tuned by hand on an S9) after the display
actually rotates, with a `FALLBACK_MS` (1000ms) safety net for tilts that never
become a rotation. Skips when auto-rotate is off.

**Known limitations / debt:**
- `DISPLAY_SETTLE_MS=600` is a magic number tuned on one device (Galaxy S9); the
  freeze duration is device-dependent, so other devices may show a tail of the
  stretch (too short) or a longer black (too long). **A Godot-frame-driven
  removal was tried (a `@UsedByGodot dismissBlank` called from
  `OrientationHud.OnViewportResized`) and reintroduced the stretch** — the
  disappearance of the stretch is gated by the OS display-freeze *thaw*, which
  happens well after Godot's resize callback and is NOT observable from the app's
  render loop (Godot's own redraw is already fast, ~6ms, and doesn't drive the
  thaw). So a fixed hold that outlasts the freeze is the appropriate tool here;
  the device-dependent constant is the residual debt. A truly device-independent
  removal would need an OS-side "freeze thawed" signal (none is exposed to apps;
  best lead is detecting the overlay window's own post-rotation relayout).
- Blanks on any tilt past the ~45° band boundary even if no rotation completes
  (brief black flash during normal handling). Threshold/hysteresis is untuned.
- There is a ~300ms lead (sensor predicts well before the OS commits), so total
  black ≈ lead + 600ms — longer than strictly necessary.
- Only manually verified on the S9; not validated across devices/orientations
  (incl. reverse-portrait) or with auto-rotate edge cases.

## Capital "actionable" star reported dark on Android (not reproduced)

**Where:** `scripts/HexMapView.cs` — `RefreshOccupantVisuals` computes
`actionableCapitals` (recruit-affordable, current player); `CreateCapitalVisual`
fills the star Gold iff actionable, else BgDeep; the pulse is a Scale animation
in `_Process` keyed off the same `_pulsingCapitals` set. Star-color-by-
actionability landed in commit 8c8e7d1.

**Symptom (2026-05-25, Samsung Galaxy S9, debug APK):** capital stars for
territories that *had* an action showed **dark (BgDeep) and did not pulse**,
where they should be bright (Gold) + pulsing. Worked correctly in desktop
testing. After a rebuild/reinstall it **stopped reproducing**, and logcat showed
the actionable set computed correctly (9 actionable, each `fill=Gold`). No code
change explains a fix — root cause unknown.

**Hypotheses:** (1) transient/timing — a stale render between refreshes (same
family as the portrait first-show mis-position, since fixed); (2) state-
dependent — those capitals genuinely had no affordable action at that instant
(gold not yet collected) and were correctly dark.

**Catch-net in place:** `HexMapView.LogActionableCapitalsIfChanged` (Render
category, logs only when the actionable coord set changes). If it recurs: a dark
star whose coord **is** in the logged set ⇒ render bug (the device runs Vulkan
Forward Mobile vs desktop GL Compatibility — suspect color/pulse rendering); a
coord **absent** from the set ⇒ data/timing. On mobile builds every Log category
is on, so the line lands in `adb logcat`.

**Severity:** cosmetic; not currently reproducible.

## Portrait HUD overlaps at high DPI scale factors (logical viewport too narrow)

**Where:** added 2026-05-24, surfaced by the new DPI UI-scaling feature
(`scripts/DisplayScale.cs` + `DisplayScaleMath`). The portrait bars are built
in `HudView.BuildPortraitBars` / the cluster layout in `OrientationHud`;
width-responsive tweaks live in `HudView.OnViewportMetricsChanged`
(`CompactLandscapeWidth`, `FullSwatchRowWidthPortrait`, eyebrow/​swatch
compaction).

**Symptom:** `Window.ContentScaleFactor` scales the UI up for legibility/touch,
which *divides* the logical viewport (`window / factor`). On a real portrait
phone (e.g. 1080×2340 physical at ~440 dpi → factor ≈ 2.75) the logical
viewport is only ~**400 px wide**. The portrait HUD was laid out for a much
wider logical space, so at ~400 wide the bottom-bar clusters (buy buttons +
controls) **overlap**. Reproduce on desktop by forcing a high factor
(temporarily set `factor` in `DisplayScale.Apply`) or launching small +
portrait: `--resolution 800x1280`.

**Also:** the bankruptcy toast text now autowraps and the box width is capped
(`PositionBankruptToast`), but the toast height is still fixed at
`BankruptToastH = 96`, so at very narrow widths the wrapped text can clip
*vertically*. Same root cause (portrait layout not designed for a narrow
logical canvas).

**Root tension (not a scaling bug):** bigger physical touch targets + a
fixed-width phone screen ⇒ fewer elements fit per row. There's no free lunch.

**Candidate fixes:** (1) make the portrait bars responsive at narrow logical
widths — tighter cluster separation, smaller fonts, and/or wrap the buy/control
clusters to multiple rows; (2) let the bankruptcy toast grow vertically with its
content instead of a fixed height; (3) as a stopgap, lower
`DisplayScaleMath.MaxFactor` (currently 3.0) toward ~1.8 to keep more logical
width, trading away some touch-target size on the densest phones. Decision
(2026-05-24): ship the scaling + overlay/tutorial/toast-width fixes, defer the
portrait responsive pass to its own effort. Closely tied to the Android entry
below — this is exactly what a device test would expose.

**Confirmed on device (2026-05-25, Samsung Galaxy S9 / SM-G960U, debug APK):**
- Android `DisplayServer.ScreenGetScale` does **NOT** return 1.0 (my original
  assumption) — it returned **1.35 in portrait and 1.8 in landscape** on the
  same device. So the divide-by-`osScale` (added for macOS retina) *does* fire
  on Android, and the resulting factor **varies by orientation**: ~**2.22**
  portrait (480 dpi / 1.35 / 160) vs ~**1.67** landscape (480 / 1.8 / 160).
- This means the scale factor is **device- and orientation-dependent** on
  Android, not a clean function of density. On the S9 it happened to land at a
  comfortable, usable size (the user confirmed buttons tappable + readable), so
  the divide-by-scale is being **kept** for now rather than gated to macOS/iOS.
- Open risk: other Android devices may report different `osScale`, so the
  on-screen size won't be consistent across the fleet. Revisit if a future
  device looks too small/large; the fix would be per-platform gating of the
  divide (raw `dpi/160` on Android) and/or tuning `MaxFactor`.
- The original portrait *overlap* (bottom-bar clusters colliding) and the
  first-show top-bar mis-position were fixed (landscape buy palette anchored
  right; portrait top row made full-width/centered). What remains deferred is
  the general portrait responsiveness at the narrowest widths and the toast's
  fixed vertical height.

**Severity:** portrait layout on high-density phones is cramped/overlapping;
landscape and desktop are unaffected.

## Android APK pipeline added but never built/verified (no SDK, no device yet)

**Where:** added 2026-05-23. New "Android" preset in `export_presets.cfg`
(`[preset.2]`) + `tools/build_android.sh`. Keystores generated under
`~/Library/Application Support/Godot/keystores/` (debug + release;
credentials in the non-committed `fourexhex-android-creds.sh`).

**Symptom / shortcut taken:** the build pipeline is written but has **never
produced an APK** and has **never been smoke-tested on a device**. The Android
SDK is not installed (editor settings point at `~/Library/Android/sdk`, which
doesn't exist yet) and no device has been attached. `tools/build_android.sh`
currently stops at its fail-fast SDK check. Task #4 of the original plan
(build + `adb install` + logcat verification + user confirmation of
tap-select / pinch-zoom) is outstanding.

**The net8-vs-net9 constraint (the important gotcha):** Godot 4.6.1's
**prebuilt** Android template hardcodes **net9.0** as the only supported C#
TFM (string `net9.0` is baked into the engine binary; the editor's own
`GodotPlugins`/`GodotTools` runtimeconfigs are `net8.0`). This project pins
`net8.0` across all four csprojs, and a net9 game assembly would no longer
load in the net8 editor / desktop builds (no major-version roll-forward) —
which is almost certainly the wall hit when the project briefly tried a higher
TFM before settling on net8.0. **Resolution:** use a custom **Gradle build**
(`gradle_build/use_gradle_build=true`), which runs `dotnet publish` against
the project's own net8.0 and bundles that runtime into the APK, bypassing the
net9 check. This is the engine's own suggested workaround. So: do NOT "fix"
this by bumping the TFM — that re-breaks desktop. Keep net8.0 + gradle build.

**SDK components the gradle build requires** (Godot 4.6.1 build template:
Gradle 8.11.1, AGP 8.6.1): `platforms;android-35`, `build-tools;35.x`,
`ndk;28.1.13356709` (exact), `platform-tools`, `cmdline-tools` (licenses
accepted), and JDK ≥ 17 (the installed JDK 21 is fine). The Gradle wrapper
downloads itself + AGP deps on the first build (one-time network).

**Candidate next steps:** (1) install the SDK components above; (2) run
`tools/build_android.sh debug` (it auto-installs the `res://android/build/`
template via `--install-android-build-template`); (3) `adb install -r` +
read logcat for a clean boot; (4) confirm tap-select + pinch-zoom on device;
(5) then `tools/build_android.sh release`. **Also unverified:** that
`dotnet publish` for the android RID succeeds under the gradle build with only
the .NET 8 SDK installed (no android workload) — if it complains, that's the
first thing to chase.

**Severity:** the Android target is non-functional until built+verified. Not
blocking any desktop path.

## Custom-draw rendering in HexMapView/HudView is full of unnamed magic numbers + long methods

**Where:** discovered 2026-05-21 during a view-styling code-debt audit
(`scripts/HexMapView.cs`, `scripts/HudView.cs`).

**Symptom / shortcut taken:** the visual-redesign rewrite of the hex tile
rendering and HUD left ~40 hand-tuned geometry literals inline (bevel/border
widths, halo + unit-ring radii, symbol scale factors, alpha values, circle
segment counts that vary 8/16/28/36, line widths 1.2–6) and several long
mixed-responsibility methods (`RefreshOccupantVisuals` ~190 lines,
`BuildStateVisuals` ~150, `AddShoreFoamStrips`, `DrawWarningBadgeAt`,
`RedrawHighlight`). The audit's color-token and structural-dedup wins were
done; this geometry/method-length tier was deliberately deferred.

**Why this is debt:** the literals are undocumented (hard to retune the look
coherently) and the long methods mix layout, classification, and drawing.

**Why deferred (the blocker):** these files are view-layer and excluded from
the unit-test suite, so there is no automated net to catch a visual
regression from an aggressive refactor — each change needs a manual launch +
eyeball. High churn × no test net = real regression risk for low functional
payoff, so it wasn't worth bundling into the token cleanup.

**Candidate next steps:** (1) extract a `RenderMetrics` constants block (or
per-shape `static readonly` groups) for the recurring radii/widths/segment
counts, with comments tying them to the design spec; (2) split the two long
`Refresh`/`Build` methods into named sub-builders (water layer, foam layer,
occupant diff) one at a time, manual-testing after each; (3) consider a
small stroke/segment style sheet so circle quality is a named choice. Do
these incrementally, never as one big-bang refactor.

**Severity:** maintainability only; no functional impact. Low priority.

---

## macOS export ships ad-hoc (no hardened runtime) — fine locally, blocks distribution

**Where:** discovered 2026-05-21 wiring up the macOS desktop build
(`tools/build_macos.sh`, `export_presets.cfg`).

**Symptom / shortcut taken:** Godot signs the exported bundle ad-hoc *with
hardened runtime* (codesign flags `0x10002`). On this macOS (26.3) the
kernel SIGKILLs an ad-hoc + hardened binary at the exec gate — `Killed: 9`,
zero stdout, no crash report. `build_macos.sh` works around it by re-signing
the bundle **plain ad-hoc** (`codesign --force --deep --sign -`, flags
`0x2`), which strips hardened runtime and runs locally.

**Why this is debt:** plain ad-hoc only runs on *this* Mac (and any Mac with
Gatekeeper bypassed). Shipping FourExHex.app to another Mac or to itch.io
needs the proper chain: a Developer ID Application cert, hardened runtime
*kept on*, the JIT entitlements we already set in the preset
(`allow-jit` / `allow-unsigned-executable-memory` /
`disable-library-validation`), then `notarytool` submission + `stapler`.
Until then the build is self-test only.

**Candidate next steps:** (1) get an Apple Developer ID; (2) set
`codesign/identity` + `notarization/notarization` in `export_presets.cfg`;
(3) drop the plain-ad-hoc re-sign from `build_macos.sh` and add notarize +
staple steps instead. Related: the system-wide .NET 8 SDK requirement
(Godot's exporter uses `/usr/local/share/dotnet`, not `~/.dotnet`) is
documented in the `build_macos.sh` header, not here, since it's a one-time
machine setup rather than recurring debt.

**Severity:** scope blocker for distributing the macOS build to other
machines. Not blocking local testing.

---

## Godot 4.6.1 C# has no Web/HTML5 export — itch.io browser hosting is blocked

**Where:** discovered 2026-05-21 while scoping a web-export spike. The user
wants to eventually host FourExHex on itch.io as a browser-playable HTML5
build.

**Symptom:** the official Godot 4.6.1 .NET export-templates archive
(`Godot_v4.6.1-stable_mono_export_templates.tpz`, ~1.1 GB, downloaded from
the `godot-builds` GitHub release) contains 27 files for Android / iOS /
Linux / macOS / Windows but **zero `web_*.zip` templates**. Confirmed by
listing `/tmp/godot-templates/templates/` after `unzip`. The non-mono
build of Godot 4.6 does ship web templates, but they target the
GDScript-only runtime and cannot run a C# project.

**Suspected cause:** not a bug — Godot's official position as of 4.6 is
that .NET web export is not yet supported. The .NET runtime needs to be
compiled to WebAssembly via NativeAOT-LLVM, and this toolchain has been a
multi-release in-progress effort. Expected in a future Godot release but
no shipping version supports it today.

**What we already did toward this spike** (saved separately so we don't
re-spend the time):
- Switched the project renderer from Forward Plus to GL Compatibility
  (`project.godot` lines 16 & 38). Asset reimport clean; build clean;
  `dotnet test` 985/985 passing. Independent decision from web export —
  Compatibility is the more portable choice for a 2D game.
- Surveyed the codebase for web-export risk surfaces: no threading, no
  DllImport, no native deps, no runtime NuGet packages, no custom
  shaders. The codebase is unusually clean and would web-export readily
  if the engine supported it.
- Saved the export-templates archive to `~/Library/Application
  Support/Godot/export_templates/4.6.1.stable.mono/`. Useful for
  desktop exports without re-downloading.

**Candidate fixes / next steps:**
1. **Desktop builds to itch.io.** Native binaries (macOS / Windows /
   Linux) ship to itch.io via the `butler` CLI; the desktop templates
   we now have installed support this immediately. No engine wait.
2. **Watch Godot release notes.** When .NET web export ships
   (potentially 4.7+), retry this spike — the saved code-surface
   survey above is still valid, and the renderer is already
   web-friendly.
3. **Reconfirm before retry.** Future-you: download the new
   templates archive and re-check whether `web_*.zip` files appear in
   the listing before committing time to the export-preset / browser
   stack.

**Severity:** scope blocker for browser hosting. Not blocking any
desktop-shipping path. Pinned to: future Godot release.

---

## Godot crashes on exit with `mutex lock failed: Invalid argument`

**Where:** seen on the macOS Godot 4.6.1 mono build closing the
`FourExHex (DEBUG)` window after a normal play / map-editor session.
Tail of the launch log:

```
libc++abi: terminating due to uncaught exception of type
std::__1::system_error: mutex lock failed: Invalid argument
```

**Symptom:** the game runs fine, the user closes the window, and the
process aborts (exit code 0 from the parent shell because the shell
wrapper still reports clean exit, but the engine itself is killed by
the libc++ uncaught-exception path).

**Suspected cause:** a Godot-internal thread (likely the audio or
resource loader subsystem) trying to lock a mutex on a destroyed
object during the engine shutdown ordering. The C# stack traces
right before the abort are benign — `SaveStore.TryReadHeader` warnings
for old v1-format saves on disk that have nothing to do with the
crash. No script of ours appears on the abort's call path.

**Severity:** cosmetic for now — the game completes its session and
the user doesn't lose state. No data corruption observed.

**Candidate fixes / next steps:** reproduce under a `--verbose`
Godot launch to capture the offending thread; check if it's gated on
specific subsystems we use (HexHoverTooltip's `_Process` GuiGetHoveredControl
polling, the `FlashPress` tween, or the AudioBus autoload's tear-down
order); search Godot 4.6.1 mono issues for matching reports — this may
already be a known engine bug with an upgrade path.
