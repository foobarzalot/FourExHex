---
description: Cut a new Google Play testing release — bump build number, commit+push, build AAB & upload to the internal and closed tracks
---

# /play — cut a Google Play testing release

Ship the current `main` to the Play testing tracks — `internal` (instant, your
own devices) and `alpha` (the closed track wired to the tester Google Group).
The Android counterpart of `/testflight`. Follow **`RELEASE.md` §1 (Versioning)**
and **§2 (Android — Google Play testing tracks)** — read them if anything below
is ambiguous. Work the steps in order and report the outcome of each.

## 0. Preconditions
- Confirm the working tree is clean (`git status`). If there are uncommitted
  changes, stop and ask the user whether to include them, stash them, or abort —
  a Play build should ship a known state, not a dirty tree.
- Confirm we're on `main` (or ask if not).
- Confirm the service-account JSON exists at
  `~/Library/Application Support/Godot/keystores/fourexhex-play-service-account.json`.
  If it's missing, the one-time Play Console setup
  (`docs/android-play-console-setup.md`) hasn't been completed — stop and tell
  the user.

## 1. Determine the build number (increment *as needed*)
- Read the current `Build` const in `scripts/AppVersion.cs` (canonical source).
- Find the latest versionCode already on the internal track:
  `tools/check_play_status.sh`.
- Decide the target build number:
  - If the current `AppVersion.Build` is **already greater** than the latest
    uploaded versionCode (bumped but never shipped), keep it as-is — do **not**
    double-bump.
  - Otherwise set it to `max(current, latestUploaded) + 1`. Play rejects a
    versionCode it has already seen. Remember `Build` is a single monotonic
    counter shared with iOS/TestFlight — always move it forward, never reuse.
- If a bump is needed, edit **only** the `Build` const in `scripts/AppVersion.cs`.
  Leave `Marketing` alone unless the user asked for a marketing-version change.

## 2. Sync the export presets
- Run `tools/sync_version.sh` (idempotent) so `export_presets.cfg` picks up the
  new `version/code` + `version/name` from `AppVersion`.

## 3. Commit and push — BEFORE launching the build
`build_android.sh aab` flips `gradle_build/export_format` in
`export_presets.cfg` for the duration of the export and only restores it on
exit. **Never `git add export_presets.cfg` while a build is running.** So commit
and push the version bump *now*, before step 4:

- `git add scripts/AppVersion.cs export_presets.cfg`
- Commit with message: `Bump build number to <N> for Play testing`
  (end the message with the standard `Co-Authored-By:` trailer).
- `git push`

If no bump was needed in step 1 (build already ahead of Play and already
committed), skip the commit/push and note that in the report.

## 4. Build the AAB and upload to the testing tracks
- Run `tools/build_android.sh aab`. Long-running (gradle); warn the user before
  starting, then stream/summarize progress — don't go silent. Watch for the
  fail-fast prerequisite errors in the script header (SDK / NDK / JDK / signing
  creds) and surface them if they occur. Output:
  `build/android/FourExHex-release.aab`.
- Run `tools/upload_play.sh`. It opens an edit, uploads the bundle, points every
  track in `PLAY_TRACKS` (default `internal,alpha`) at the new versionCode, and
  commits — one edit, so both tracks land together. A 401/403 right after
  first-time setup is usually permission propagation — wait and retry before
  debugging (see the runbook's gotchas).

## 5. Report
- On success, tell the user the new build number is on both tracks, and be
  precise about what that means: live immediately on `internal` (no processing
  delay, unlike TestFlight), and queued for Google review on `alpha` — Google
  Group testers only see it once that review clears.
- Optionally confirm with `tools/check_play_status.sh`, which reports both.
- If the build or upload failed, report the failing step and the relevant log
  output — do not claim success.
