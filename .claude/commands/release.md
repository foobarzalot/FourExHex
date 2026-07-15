---
description: Cut a full release — bump the build number ONCE, then upload to TestFlight, upload to Play internal testing, and build the Android APK off that same number
---

# /release — cut a full iOS + Android release

Ship the current `main` to **both** stores off a **single** build-number bump:
upload to TestFlight (iOS), upload an AAB to the Play internal-testing track
(Android OTA), and produce a signed release APK (Android sideload). This is
`/testflight`, `/play`, and `/android` combined, sharing one version bump —
`Build` is a single monotonic counter shared by both platforms
(`scripts/AppVersion.cs`), so it must be incremented exactly **once** here, not
once per platform.

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
- Find the latest build already on each store:
  `tools/check_testflight_status.sh` (ASC) and `tools/check_play_status.sh`
  (Play internal track; skip if the Play service-account JSON isn't set up yet).
- Target build number:
  - If the current `AppVersion.Build` is **already greater** than the latest
    uploaded build on both stores (bumped but never shipped), keep it as-is —
    do **not** double-bump.
  - Otherwise set it to `max(current, latestUploadedAsc, latestUploadedPlay) + 1`.
    It must be strictly greater than any build on ASC for this `Marketing`
    version (Apple rejects duplicate `CFBundleVersion`s) and any versionCode
    Play has seen (Play rejects reused versionCodes). Sideload installs likewise
    need a strictly-higher `versionCode` to install over the last release APK.
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

## 6. Android — build the AAB and upload to Play internal testing
- If the Play service-account JSON is missing
  (`~/Library/Application Support/Godot/keystores/fourexhex-play-service-account.json`
  — one-time setup in `docs/android-play-console-setup.md` not done), skip this
  step and note it in the report.
- Run `tools/build_android.sh aab` (second gradle pass, faster than the first —
  deps are warm). Output: `build/android/FourExHex-release.aab`.
- Run `tools/upload_play.sh` — uploads the bundle and rolls it out to the
  internal track (live immediately, no processing delay). A 401/403 right after
  first-time setup is usually permission propagation — wait and retry.

## 7. Publish the APK as a GitHub Release — ALWAYS
The release APK is `.gitignore`d (`build/`) and must never be committed into the
repo tree — it's 80+ MB, git history is forever, and GitHub hard-rejects single
files over 100 MB on push. The correct home for it is a **GitHub Release asset**
(stored outside the git object database, up to 2 GB each). Always publish it here
so testers have a stable download link:

- Run `gh release create build-<N> build/android/FourExHex-release.apk --title "Build <N> (v<Marketing>)" --notes "Android release APK, build <N> (v<Marketing>). iOS build <N> is on TestFlight. Install: adb install -r FourExHex-release.apk"`
  — where `<N>` is the build number from step 1 and `<Marketing>` is the
  `AppVersion.Marketing` string.
- If the `build-<N>` tag/release already exists (e.g. a re-run), upload the APK to
  it instead of failing: `gh release upload build-<N> build/android/FourExHex-release.apk --clobber`.
- Skip this step only if the Android build in step 5 failed (there's no APK to
  publish) — note that in the report.
- Confirm the asset attached (`gh release view build-<N> --json assets`) and
  capture the release URL for the report.

## 8. Report
- State the single new build number both artifacts carry.
- **iOS**: the TestFlight upload succeeded and sits in App Store Connect →
  TestFlight under "Processing" ~15–30 min before internal testers see it.
  Optionally offer to poll `tools/check_testflight_status.sh` until it's `VALID`.
- **Android (Play)**: the new versionCode is live on the internal track (no
  processing delay); testers get it via the existing opt-in link. Confirm with
  `tools/check_play_status.sh`. If step 6 was skipped (no service account),
  say so.
- **Android (sideload)**: the absolute APK path (`build/android/FourExHex-release.apk`) plus
  its `file -b` type line, and the install one-liner (uninstall first when
  switching from a debug-signed build):
  `"$HOME/Library/Android/sdk/platform-tools/adb" install -r build/android/FourExHex-release.apk`
- **GitHub Release**: the `build-<N>` release URL with the APK attached as a
  downloadable asset (from step 7).
- If either platform's build/upload failed, report which step failed and the
  relevant log output — do not claim success. Note that a partial result is
  possible (e.g. iOS uploaded, Android failed): report each platform's outcome
  independently.
