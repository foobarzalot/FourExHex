# Tech Debt

Running list of known issues, flaky tests, and shortcuts that should eventually be cleaned up. Add new entries at the top.

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
