# Google Play Console Setup — Hand-off Runbook

**This is a runbook for Claude-in-Chrome (or Cowork) to execute with the user.** Claude Code (the local CLI) is handling all in-repo work: the AAB build (`tools/build_android.sh aab`) and the eventual API-driven uploads (`tools/upload_play.sh`). Your job is everything that requires a web browser at play.google.com/console or console.cloud.google.com — flows that need the user's Google account password, 2FA, a payment, and reading text from web pages.

Walk the user through this one step at a time. At each step: state the goal, open the URL, confirm field values with the user before they enter them, and confirm the goal is met before moving on. Don't batch.

When all six steps are done, the user has: an active Google Play developer account, an app record for `com.foobarzalot.fourexhex`, a first internal-testing release live behind a tester opt-in link, and a service-account JSON key at a known path on disk. Claude Code can then upload every subsequent build headlessly.

---

## Project facts (use these verbatim — do not improvise)

| Field | Value |
| --- | --- |
| App name | `FourExHex` |
| Package name | `com.foobarzalot.fourexhex` |
| Google account | the user's personal Google account (their choice) |
| Category | Game → Strategy |
| Price | Free |
| Ads | No |
| Local credentials dir | `~/Library/Application Support/Godot/keystores/` |
| Service-account JSON path | `~/Library/Application Support/Godot/keystores/fourexhex-play-service-account.json` |
| First AAB to upload | `build/android/FourExHex-release.aab` (Claude Code builds this) |

---

## Step 1 — Register the Google Play developer account

**Goal:** an active (identity-verified) Play Console developer account on the user's Google account.

**URL:** https://play.google.com/console/signup

**Cost:** $25 USD, one-time (not recurring).

**Timeline:** payment is instant; **identity verification can take from hours to several days**. Steps 3–6 are blocked until verification completes; Step 2 (deciding the developer name) happens during signup itself.

### Field values / decisions

- Account type: **Personal** (an Organization account requires a D-U-N-S number and a registered legal entity; not needed here).
- **Developer name**: this is the publisher name shown publicly on Google Play. Unlike Apple, Google lets a personal account pick a display name freely — the user can enter `foobarzalot` directly. Confirm the choice with them before submitting.
- Contact details: Google requires a contact email and phone number and **verifies both** (email link + SMS code) during signup.

### Personal-account testing requirements (tell the user up front)

Personal developer accounts created after November 13, 2023 must run a closed test with **at least 12 testers for 14 days before they can publish to production**. This does **NOT** affect internal testing — the internal track (what this pipeline targets, up to 100 testers, no review delay) works immediately. It only matters if/when the user later wants a public Play Store release.

### What the user needs at hand

- Google account password + 2FA device
- A payment method for the $25 fee
- A government ID for identity verification (Google usually asks for ID upload for new personal accounts)
- A phone that can receive SMS

### Verification — do not move on until all of these are true

- play.google.com/console shows the Console dashboard (not the signup flow)
- Account status shows verified (no "verify your identity" banner blocking app creation)
- The "Create app" button is available

If verification stalls past a week, the user should check the email Google sent for rejected-document reasons and resubmit.

---

## Step 2 — Create the app record

**Goal:** an app entry for FourExHex exists in the Play Console. (The Play Developer API cannot create app records — this is Console-UI-only.)

**URL:** https://play.google.com/console → All apps → **Create app**

### Field values

| Field | Value |
| --- | --- |
| App name | `FourExHex` |
| Default language | English (United States) |
| App or game | **Game** |
| Free or paid | **Free** (irreversible once published — confirm with the user, but Free is the plan) |
| Declarations | tick the developer-policies and US-export-laws checkboxes after the user reads them |

**Note on the package name:** the package name (`com.foobarzalot.fourexhex`) is NOT entered on this form — Play locks it in from the **first uploaded AAB** (Step 4). Do not look for a package-name field here.

### Verification

- The app appears under All apps with status "Draft"

---

## Step 3 — Build the first AAB (hand back to Claude Code briefly)

**Goal:** `build/android/FourExHex-release.aab` exists locally, signed with the release keystore.

The user runs (or asks Claude Code to run) in the repo:

```sh
tools/build_android.sh aab
```

This produces `build/android/FourExHex-release.aab`, signed with the existing release keystore (`GODOT_ANDROID_KEYSTORE_RELEASE_*` from `fourexhex-android-creds.sh`). **That signature becomes the Play upload key in Step 4 — there is no separate keystore to create.**

---

## Step 4 — First internal-testing release (manual upload) + Play App Signing

**Goal:** the first AAB is uploaded through the Console UI, Play App Signing is enrolled, and an internal-testing release is live.

**Why manual:** the very first bundle upload establishes the package name and the App Signing enrollment, and API uploads only work reliably once the app has an initial upload. Every later build goes through `tools/upload_play.sh` instead.

**URL:** Play Console → FourExHex → **Testing → Internal testing** → Create new release

### Steps

1. Click **Create new release**.
2. **Play App Signing**: when prompted, accept the default — **Google generates and holds the app signing key**. The key inside the uploaded AAB (our release keystore) is automatically registered as the **upload key**. Do not choose "export and upload a key" options; the default is what we want.
3. Upload `build/android/FourExHex-release.aab` (the user drags the file in, or picks it from `<repo>/build/android/`).
4. Release name: leave the default (Play derives it from versionCode/versionName).
5. Release notes: something minimal like `First internal build.`
6. Click through review → **Start rollout to Internal testing**. Internal-track rollouts are immediate (no Google review).

If the Console complains about missing app-content declarations before allowing rollout (privacy policy, ads declaration, content rating, data safety): fill in the **minimum the internal track demands** — Ads: "No", Data safety: no data collected/shared, content rating questionnaire: complete it truthfully for a strategy game (no violence/gambling/user content). Internal testing requires far less than production; only complete sections the rollout button actually blocks on.

### Verification

- Internal testing shows a release with status **Available to internal testers**
- The release's versionCode matches `Build` in `scripts/AppVersion.cs`
- Play Console → Setup → App signing shows an **App signing key** (Google's) and an **Upload key** (ours) — two distinct SHA-1 fingerprints

---

## Step 5 — Internal testers + opt-in link

**Goal:** the user (and any friends) can install FourExHex from Play on a real device.

**URL:** Play Console → FourExHex → Testing → Internal testing → **Testers** tab

### Steps

1. Create an email list (name: `FourExHex internal`) and add the tester Gmail addresses (the user's own Google account at minimum). Up to 100 testers.
2. Save, then copy the **opt-in URL** ("Copy link" under "How testers join your test").
3. The user opens the opt-in link on their Android device (or any browser logged into a tester account), accepts the invitation, and installs from the Play Store link it gives.

### Verification

- FourExHex installs from Play on a real Android device and launches
- Record the opt-in URL somewhere the user can share it (it's stable; new builds reuse it)

---

## Step 6 — Service account for API uploads

**Goal:** a Google Cloud service account with release-manager access to the app, its JSON key saved at the standard path, so `tools/upload_play.sh` / `tools/check_play_status.sh` can authenticate headlessly.

### 6a. Create the service account (Google Cloud console)

**URL:** https://console.cloud.google.com

1. Create a new project (name: `fourexhex-play`) — or reuse an existing personal project if the user prefers.
2. **Enable the API**: APIs & Services → Library → search **"Google Play Android Developer API"** → Enable.
3. IAM & Admin → **Service Accounts** → Create service account:
   - Name: `fourexhex-play-upload`
   - Grant no project-level roles (access is granted in Play Console, not GCP IAM).
4. Open the created service account → **Keys** tab → Add key → Create new key → **JSON** → download.
5. Note the service account's email address (`fourexhex-play-upload@<project>.iam.gserviceaccount.com`).

### 6b. Save the key where the scripts expect it

```sh
mv ~/Downloads/<downloaded-key>.json "$HOME/Library/Application Support/Godot/keystores/fourexhex-play-service-account.json"
chmod 600 "$HOME/Library/Application Support/Godot/keystores/fourexhex-play-service-account.json"
```

> Sensitive-values warning to give the user: "This JSON file lets anyone holding it upload and roll out builds of your app. Keep it out of the repo, out of synced folders, and never paste its contents into a chat."

### 6c. Invite the service account in Play Console

**URL:** Play Console → (left nav, account level) **Users and permissions** → Invite new users

1. Email: the service-account address from 6a.
2. Permissions: **App permissions** tab → add FourExHex → grant **"Release to testing tracks"** (plus "View app information (read-only)"). This is the least-privilege set for internal-track uploads; do NOT grant admin or production-release rights.
3. Send invite — service accounts auto-accept.

**Propagation gotcha:** the very first API call after granting access can fail with a 401/403 for up to ~24 h (usually minutes) while Google propagates the permission. If the smoke test below fails right after setup, wait and retry before debugging.

### Verification (hand back to Claude Code)

The user switches back to their terminal Claude Code session and says something like:

> "Play Console setup is done. Account verified, app record exists, first internal release is live, testers added, and the service-account JSON is at `~/Library/Application Support/Godot/keystores/fourexhex-play-service-account.json`."

Claude Code then runs the smoke test:

```sh
tools/check_play_status.sh    # should print the internal track's release (versionCode from Step 4)
```

From then on, every release is: bump `Build` in `scripts/AppVersion.cs` → `tools/build_android.sh aab` → `tools/upload_play.sh`.

---

## Common gotchas to watch for

- **Identity verification is the long pole.** Payment is instant but the account can sit in "verifying" for days. Everything after Step 1 is blocked on it; suggest the user starts Step 1 immediately and comes back.
- **Package name is set by the first upload, forever.** Double-check the AAB really is `com.foobarzalot.fourexhex` (it is, from `export_presets.cfg`) before Step 4 — a wrong package name means deleting the app record and starting over.
- **"Free" is irreversible.** Once published, a free app can never become paid (a paid version would need a new package name). FourExHex is planned free; just make sure the user knows.
- **Don't opt out of Play App Signing.** The default (Google holds the signing key, our keystore is the upload key) is what the scripts assume, and it makes a lost/leaked upload key recoverable (Console → App signing → request upload-key reset).
- **The 12-tester/14-day rule is production-only.** If the user reads scary banners about closed-testing requirements, reassure them: internal testing is exempt and is all this pipeline needs.
- **Service-account 401/403 right after setup** is almost always permission propagation, not a broken key. Wait, retry, then check that the Android Developer API is enabled on the right GCP project and the invite in Play Console targeted the exact service-account email.
