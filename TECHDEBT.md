# Tech Debt

Running list of known issues, flaky tests, and shortcuts that should eventually be cleaned up. Add new entries at the top.

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
