# Tech Debt

Running list of known issues, flaky tests, and shortcuts that should eventually be cleaned up. Add new entries at the top.

## `Log` is silent in menu/editor scenes — only `Main` wires `Log.Sink`/`Configure`

**Where:** discovered 2026-05-21 instrumenting `CreditsPanel`/`SettingsPanel`.
`Log.Sink ??= GD.Print` and `Log.Configure(OS.GetEnvironment("FOUREXHEX_LOG"))`
are set only in `scripts/Main.cs:54,58` (the in-game scene root).

**Symptom:** any `Log.*` call made from a scene that isn't `Main` —
`MainMenuScene`, `MapEditorScene`, `TutorialBuilderScene`, and the modals
they own (`SettingsPanel`, `CreditsPanel`, `EscMenu`, `SlotPickerDialog`) —
is dropped because `Log.Sink` is null and no category is configured. So the
CLAUDE.md "verify via the logs after manual testing" step is impossible for
menu-side code paths: e.g. `CreditsPanel.Open`/`Close` only print when
Credits is reached from the **in-game pause** Settings, never from the main
menu. The view-layer log net has a hole exactly where the menu UI lives.

**Suspected cause:** sink/config wiring was added to `Main` when logging was
an AI/turn-debugging tool (gameplay-only). The menu scenes predate the need
to instrument them and were never given the same bootstrap.

**Candidate fixes:** (1) hoist the `Log.Sink ??= GD.Print` + `Log.Configure`
pair into an autoload (e.g. `AudioBus` already autoloads, or a tiny new
`LogBootstrap` autoload) so every scene gets it once at startup; (2) or
duplicate the two lines into each scene root's `_Ready` (cheap, but drift-
prone). Option 1 is the clean fix — one bootstrap, all scenes covered.

**Severity:** blocks log-based verification of menu/editor view code; no
runtime impact (logs are diagnostic only).

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
