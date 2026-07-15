---
description: Build a signed release Android APK — bump build number, sync presets, build, report location
---

# /android — build a release Android APK

Produce a signed **release** APK of the current tree (for adb/sideload
install). For an over-the-air Google Play internal-testing release, use
`/play` instead — it builds an AAB (`tools/build_android.sh aab`) and uploads
it. Follow **`RELEASE.md` §1 (Building)** and its **Versioning** / **Android
prerequisites** / **Signing** subsections — read them if anything below is
ambiguous. Work the steps in order and report the outcome of each.

## 0. Preconditions
- Confirm the working tree is clean (`git status`). If there are uncommitted
  changes, stop and ask the user whether to include them, stash them, or abort —
  a release APK should ship a known state, not a dirty tree.
- Confirm we're on `main` (or ask if not).

## 1. Bump the build number
- Read the current `Build` const in `scripts/AppVersion.cs` (canonical source —
  it feeds Android `version/code` / versionCode via `tools/sync_version.sh`).
- Increment it by 1 and edit **only** the `Build` const in
  `scripts/AppVersion.cs`. Leave `Marketing` alone unless the user asked for a
  marketing-version change.
- Why bump: `Build` is the Android `versionCode`. Android refuses to install a
  release APK over an installed one with an equal-or-lower versionCode, so each
  release gets a strictly higher number. `Build` is a single monotonic counter
  shared with iOS/TestFlight — always move it forward, never reuse a value.

## 2. Sync the export presets
- Run `tools/sync_version.sh` (idempotent) so `export_presets.cfg` picks up the
  new `version/code` + `version/name` from `AppVersion`. (`build_android.sh`
  runs this itself, but syncing now keeps the committed preset in step 4 honest.)

## 3. Build the release APK
- Run `tools/build_android.sh release`. This syncs the version, builds the C#
  assemblies (`Debug` for editor load + `ExportRelease` for the export), builds
  the RotationFix plugin AAR on first run, and runs the headless Godot gradle
  export, signing with the **release** keystore.
- This is long-running — the first run downloads Gradle 8.11.1 + AGP deps (a few
  minutes, network). **Warn the user before starting, then stream/summarize
  progress — don't go silent.** Watch for the fail-fast prerequisite errors in
  the script header (missing SDK platform / NDK `28.1.13356709` / build-tools /
  JDK / the `fourexhex-android-creds.sh` signing creds) and surface them if they
  occur.
- The output APK is `build/android/FourExHex-release.apk`.

## 4. Commit and push the version bump
- `git add scripts/AppVersion.cs export_presets.cfg`
- Commit with message: `Bump build number to <N> for Android release`
  (end the message with the standard `Co-Authored-By:` trailer).
- `git push`

## 5. Report
- On a successful build, report the **new build number** and the **absolute path
  to the APK** (`build/android/FourExHex-release.apk`), plus its `file -b` type
  line as proof it was produced.
- Offer the install one-liner from RELEASE.md §2 (note the signature-mismatch
  fallback: switching between debug and release keys needs an uninstall first):
  `"$HOME/Library/Android/sdk/platform-tools/adb" install -r build/android/FourExHex-release.apk`
- If the build failed, report the failing step and the relevant log output — do
  not claim success.
