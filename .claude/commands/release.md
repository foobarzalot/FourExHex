---
description: Cut a full release — bump the build number ONCE, then upload to TestFlight and build the Android APK off that same number
---

# /release — cut a full iOS + Android release

Ship the current `main` to **both** platforms off a **single** build-number bump:
upload to TestFlight (iOS) and produce a signed release APK (Android). This is
`/testflight` and `/android` combined, sharing one version bump — `Build` is a
single monotonic counter shared by both platforms (`scripts/AppVersion.cs`), so
it must be incremented exactly **once** here, not once per platform.

Follow **`RELEASE.md`** — §1 (Versioning/Building), §2 (iOS TestFlight + Android
install). Read it if anything below is ambiguous. Work the steps in order and
report the outcome of each.

## 0. Preconditions
- Confirm the working tree is clean (`git status`). If there are uncommitted
  changes, stop and ask the user whether to include them, stash them, or abort —
  a release should ship a known state, not a dirty tree.
- Confirm we're on `main` (or ask if not).

## 1. Determine the build number — bump ONCE
- Read the current `Build` const in `scripts/AppVersion.cs` (canonical source;
  feeds iOS `CFBundleVersion` and Android `versionCode`).
- Find the latest build already on App Store Connect:
  `tools/check_testflight_status.sh`.
- Target build number:
  - If the current `AppVersion.Build` is **already greater** than the latest
    uploaded ASC build (bumped but never shipped), keep it as-is — do **not**
    double-bump.
  - Otherwise set it to `max(current, latestUploaded) + 1`. It must be strictly
    greater than any build on ASC for this `Marketing` version, or Apple rejects
    the iOS upload as a duplicate `CFBundleVersion`. Android likewise needs a
    strictly-higher `versionCode` to install over the last release APK.
- If a bump is needed, edit **only** the `Build` const. Leave `Marketing` alone
  unless the user asked for a marketing-version change. **This is the only bump
  in the whole flow** — both platform builds read the same number.

## 2. Sync the export presets
- Run `tools/sync_version.sh` (idempotent) so `export_presets.cfg` picks up the
  new version fields for every platform.

## 3. Commit and push — BEFORE launching the builds
This ordering matters (RELEASE.md §1.5 gotcha): `build_ios.sh` injects the real
Team ID into `export_presets.cfg` for the duration of the build and only restores
it on exit. **Never `git add export_presets.cfg` while a build is running.**
Commit and push the version bump now:

- `git add scripts/AppVersion.cs export_presets.cfg`
- Commit: `Bump build number to <N> for release (TestFlight + Android)`
  (end with the standard `Co-Authored-By:` trailer).
- `git push`

If no bump was needed in step 1 (build already ahead of ASC and committed), skip
the commit/push and note that in the report.

## 4. iOS — build and upload to TestFlight
- Run `tools/build_ios.sh release`. Long-running; warn the user, then
  stream/summarize progress — don't go silent. Watch for the signing/archive
  gotchas in RELEASE.md §2 (conflicting provisioning, "iOS X.Y is not
  installed", stale mobileprovision) and surface them if they occur.

## 5. Android — build the release APK
- Run `tools/build_android.sh release`. It re-syncs the version (same `Build`
  from step 1, no second bump), builds the C# assemblies + RotationFix AAR (first
  run) + headless gradle export, signing with the release keystore. First run
  downloads Gradle + AGP deps (a few minutes). Watch for the fail-fast
  prerequisite errors in the script header and surface them if they occur.
- Output: `build/android/FourExHex-release.apk`.

## 6. Report
- State the single new build number both artifacts carry.
- **iOS**: the TestFlight upload succeeded and sits in App Store Connect →
  TestFlight under "Processing" ~15–30 min before internal testers see it.
  Optionally offer to poll `tools/check_testflight_status.sh` until it's `VALID`.
- **Android**: the absolute APK path (`build/android/FourExHex-release.apk`) plus
  its `file -b` type line, and the install one-liner (uninstall first when
  switching from a debug-signed build):
  `"$HOME/Library/Android/sdk/platform-tools/adb" install -r build/android/FourExHex-release.apk`
- If either platform's build/upload failed, report which step failed and the
  relevant log output — do not claim success. Note that a partial result is
  possible (e.g. iOS uploaded, Android failed): report each platform's outcome
  independently.
