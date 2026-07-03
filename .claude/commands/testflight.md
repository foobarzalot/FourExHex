---
description: Cut a new iOS TestFlight release — bump build number, commit+push, build & upload
---

# /testflight — cut an iOS TestFlight release

Ship the current `main` to TestFlight. Follow **`RELEASE.md` §1 (Versioning)** and
**§2 (iOS — TestFlight upload)** — read them if anything below is ambiguous. Work
the steps in order and report the outcome of each.

## 0. Preconditions
- Confirm the working tree is clean (`git status`). If there are uncommitted
  changes, stop and ask the user whether to include them, stash them, or abort —
  a TestFlight build should ship a known state, not a dirty tree.
- Confirm we're on `main` (or ask if not).

## 1. Determine the build number (increment *as needed*)
- Read the current `Build` const in `scripts/AppVersion.cs` (canonical source).
- Find the latest build number already on App Store Connect:
  `tools/check_testflight_status.sh` (reports the most recent build's version +
  processingState).
- Decide the target build number:
  - If the current `AppVersion.Build` is **already greater** than the latest
    uploaded build (i.e. it was bumped but never shipped), keep it as-is — do
    **not** double-bump.
  - Otherwise set it to `max(current, latestUploaded) + 1` (normally just
    `current + 1`). It must be **strictly greater** than any build already on ASC
    for this `Marketing` version, or Apple rejects the upload as a duplicate
    `CFBundleVersion`.
- If a bump is needed, edit **only** the `Build` const in `scripts/AppVersion.cs`.
  Leave `Marketing` alone unless the user asked for a marketing-version change.

## 2. Sync the export presets
- Run `tools/sync_version.sh` (idempotent) so `export_presets.cfg` picks up the
  new version fields from `AppVersion`.

## 3. Commit and push — BEFORE launching the build
This ordering matters (RELEASE.md §1.5 gotcha): `build_ios.sh` injects the real
Team ID into `export_presets.cfg` for the duration of the build and only restores
it on exit. **Never `git add export_presets.cfg` while a build is running.** So
commit and push the version bump *now*, before step 4:

- `git add scripts/AppVersion.cs export_presets.cfg`
- Commit with message: `Bump build number to <N> for TestFlight`
  (end the message with the standard `Co-Authored-By:` trailer).
- `git push`

If no bump was needed in step 1 (build already ahead of ASC and already
committed), skip the commit/push and note that in the report.

## 4. Build and upload to TestFlight
- Run `tools/build_ios.sh release`. This syncs the version again, builds the C#
  assemblies + Godot iOS export, archives, re-signs for distribution, and runs
  `xcrun altool --upload-app` against the App Store Connect API key.
- This is long-running. Warn the user before starting, then stream/summarize
  progress — don't go silent. Watch for the signing/archive gotchas documented in
  RELEASE.md §2 (conflicting provisioning, "iOS X.Y is not installed", stale
  mobileprovision) and surface them if they occur.

## 5. Report
- On a successful upload, tell the user the new build number and that it will sit
  in App Store Connect → TestFlight under "Processing" for ~15–30 min before it's
  available to internal testers.
- Optionally offer to poll `tools/check_testflight_status.sh` until the build
  flips to `VALID`.
- If the build or upload failed, report the failing step and the relevant log
  output — do not claim success.
