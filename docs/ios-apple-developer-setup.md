# iOS Apple Developer Setup — Hand-off Runbook

**This is a runbook for Claude-in-Chrome (or Cowork) to execute with the user.** Claude Code (the local CLI) is handling all in-repo work, Xcode install, and the eventual TestFlight upload. Your job is everything that requires a web browser at developer.apple.com or appstoreconnect.apple.com — flows that need the user's Apple ID password, 2FA on their phone, and reading text from web pages.

Walk the user through this one step at a time. At each step: state the goal, open the URL, confirm field values with the user before they enter them, and confirm the goal is met before moving on. Don't batch.

When all five steps are done, the user has: a paid Apple Developer Program membership, a registered App ID, an App Store Connect record, and an App Store Connect API key file at a known path on disk. Claude Code can then run the local build and upload to TestFlight.

---

## Project facts (use these verbatim — do not improvise)

| Field | Value |
| --- | --- |
| App name | `FourExHex` |
| Bundle identifier | `com.foobarzalot.fourexhex` |
| Publisher identity | `foobarzalot` (chosen publisher name — see Step 1 caveat) |
| Apple ID for enrollment | `nathan@sitkoff.net` |
| Legal name on Apple ID | Nathan Sitkoff |
| Primary language | English (U.S.) |
| Platform | iOS (Universal — iPhone + iPad) |
| Local credentials path | `~/Library/Application Support/Godot/keystores/` |

---

## Step 1 — Enroll in the Apple Developer Program

**Goal:** paid membership active on the user's Apple ID `nathan@sitkoff.net`.

**URL:** https://developer.apple.com/programs/enroll

**Cost:** $99 USD/year (or local equivalent), recurring annually.

**Timeline:** Apple's approval is usually 24–48 hours, occasionally same-day. The user will receive an email when approval is granted.

### Before paying — important decision point (raise this with the user explicitly)

The user has chosen the publisher name `foobarzalot` for their game, but Apple's enrollment options handle "publisher name" differently:

- **Individual enrollment** (recommended for this immediate effort): uses the enrolling person's *legal name* — "Nathan Sitkoff" — as the App Store seller name shown to customers. To later display "foobarzalot" instead, the user submits documentation to Apple (typically a business-registration certificate proving they do business as "foobarzalot"). Until that paperwork clears, the public seller name will be "Nathan Sitkoff".
- **Organization enrollment**: uses the legal entity name (e.g. "foobarzalot LLC") and requires a D-U-N-S number for the entity. More paperwork, higher barrier, but the App Store seller name is "foobarzalot" with no follow-up needed. Requires foobarzalot to already be a registered legal entity.

**For this first TestFlight build, the seller name does NOT appear to TestFlight testers.** It only matters for public App Store release. So Individual enrollment is fine for now and the DBA/Organization decision can be deferred.

**Recommendation to give the user:** Individual enrollment, decide on the DBA/Organization path later when ready for public App Store release. Make sure they consciously accept this before paying.

### Field values to enter

- Entity type: **Individual** (per recommendation above; if they choose otherwise, follow Apple's Organization flow which differs)
- Legal name: matches their Apple ID — "Nathan Sitkoff"
- Address: matches their Apple ID and a valid form of ID
- Payment method: their card

### What the user needs at hand

- Apple ID password
- The 2FA device for that Apple ID (phone, trusted device)
- A payment method
- A government-ID-matching legal name and address

### Verification — do not move on until all of these are true

- developer.apple.com → "Account" shows the user as a member
- "Certificates, Identifiers & Profiles" page is accessible (was inaccessible before enrollment)
- The user has received the membership-activation email

If Apple's review takes more than 48 hours, tell the user to check the activation email and to call Apple Developer Support if it's stuck. Steps 2–5 are blocked until Step 1 is verified.

---

## Step 2 — Register the App ID

**Goal:** bundle identifier `com.foobarzalot.fourexhex` registered as an App ID with no extra capabilities.

**URL:** https://developer.apple.com/account/resources/identifiers/list

### Field values

| Field | Value |
| --- | --- |
| Type | App IDs → App |
| Description | `FourExHex` |
| Bundle ID type | Explicit (not Wildcard) |
| Bundle ID | `com.foobarzalot.fourexhex` |
| Capabilities | **Leave ALL unchecked** — FourExHex needs no push, no iCloud, no GameKit, no in-app purchases at this stage |

### Verification

- The new App ID appears in the Identifiers list with description "FourExHex" and bundle ID `com.foobarzalot.fourexhex`

If the bundle ID is already taken (extremely unlikely with that reverse-DNS), tell the user — they'll need to pick a different one and the rest of this runbook needs the new value substituted in.

---

## Step 3 — Create the App Store Connect record

**Goal:** an app entry exists at appstoreconnect.apple.com so TestFlight has somewhere to receive uploaded builds.

**URL:** https://appstoreconnect.apple.com → My Apps → click the **+** button → "New App"

### Field values

| Field | Value |
| --- | --- |
| Platforms | iOS (just iOS — not macOS) |
| Name | `FourExHex` |
| Primary Language | English (U.S.) |
| Bundle ID | Select `com.foobarzalot.fourexhex` from the dropdown (it appears after Step 2 is done; if it's missing, Step 2 hasn't propagated — wait a few minutes and refresh) |
| SKU | `fourexhex-ios` (free-form internal identifier — Apple never shows this to anyone) |
| User Access | Full Access |

### Verification

- The app appears at App Store Connect → My Apps
- Clicking into the app shows a left-nav with **TestFlight** as one of the tabs (this is what we need for the upload destination)

---

## Step 4 — Generate the App Store Connect API key

**Goal:** a `.p8` private key file the local build script can use to upload builds to TestFlight without an Xcode UI session.

**URL:** https://appstoreconnect.apple.com → Users and Access → Integrations → App Store Connect API → (Team Keys tab)

### Steps

1. Click **+** (or "Generate API Key") to create a new key.
2. **Name**: `FourExHex CI`
3. **Access**: select the role **App Manager** — this is sufficient for TestFlight upload. Do **not** pick Admin (least-privilege principle; the .p8 file is sensitive).
4. Click Generate.
5. **Download the `.p8` file IMMEDIATELY** — Apple displays the download link exactly once. If the user closes the page without downloading, they'll have to revoke the key and start this step over.
6. Note two values from the page after creation:
   - **Key ID** — the 10-character string in the "Key ID" column of the newly created key's row
   - **Issuer ID** — a UUID-style string shown near the top of the page; this is the team's Issuer ID and is the same for every key in the team

### Where the user saves the file

Tell the user to save the downloaded `.p8` file at the path Apple's `xcrun altool` searches for App Store Connect API keys:

```
~/.appstoreconnect/private_keys/AuthKey_<KeyID>.p8
```

Replacing `<KeyID>` with the actual 10-character Key ID. Have the user create the directory first if needed:

```sh
mkdir -p ~/.appstoreconnect/private_keys
```

This is one of `altool`'s documented search paths, so the local build script `tools/build_ios.sh` will find the key automatically — no extra wiring needed.

### Sensitive-values warning to give the user

> "These three values together — the .p8 file, the Key ID, and the Issuer ID — give anyone with them the ability to upload builds to your TestFlight, manage testers, and (with elevated roles) edit your apps. Keep the .p8 file out of any directory that gets synced or shared. Never paste them into a public chat."

---

## Step 5 — Find the Team ID

**Goal:** capture the 10-character Team ID that Xcode signing needs.

**URL:** https://developer.apple.com/account → Membership Details

### Steps

1. Open the URL.
2. Locate the **Team ID** field — a 10-character alphanumeric string.
3. Note it down (it goes into the same credentials file as the API key values).

---

## Hand-off back to Claude Code

When all five steps are verified, tell the user to do this final setup so Claude Code can pick up the credentials:

**Create the credentials file** at `~/Library/Application Support/Godot/keystores/fourexhex-ios-creds.sh` (this matches the existing Android keystore convention used by `tools/build_android.sh`) with the following contents (substituting the real values for `<KeyID>`, `<IssuerID>`, `<TeamID>`):

```sh
export ASC_API_KEY_ID="<KeyID>"
export ASC_API_ISSUER_ID="<IssuerID>"
export IOS_TEAM_ID="<TeamID>"
```

(The `.p8` file itself lives at `~/.appstoreconnect/private_keys/AuthKey_<KeyID>.p8` — see Step 4 — and is found by `xcrun altool` automatically; no need to repeat the path here.)

This file is NOT committed to the repo. The local build script will `source` it at run time. Claude Code already knows it should expect this file at that path.

### Report back to Claude Code

Tell the user to switch back to their terminal Claude Code session and say something like:

> "Apple Developer setup is done. Membership is active, App ID `com.foobarzalot.fourexhex` is registered, App Store Connect record exists, API key file is saved, and the credentials file at `~/Library/Application Support/Godot/keystores/fourexhex-ios-creds.sh` is populated."

Claude Code will then verify the credentials file is present and proceed with the headless iOS build + TestFlight upload (Phase 4 of the iOS TestFlight plan). The user does not need to manually do anything more on Apple's side until adding external testers in App Store Connect after the first build appears.

---

## Common gotchas to watch for

- **Apple's enrollment review can stall.** If the user doesn't see an activation email within 48 hours, they should check spam, then contact Apple Developer Support via developer.apple.com/contact.
- **Two-factor auth is mandatory.** The Apple ID must have 2FA enabled before enrollment is possible. If the user's `nathan@sitkoff.net` Apple ID doesn't have 2FA yet, walk them through enabling it at appleid.apple.com first.
- **Bundle ID rejection.** If `com.foobarzalot.fourexhex` is somehow already taken, stop and report to the user; they'll need to choose a new bundle ID and Claude Code will need to update it in the repo.
- **Tax & banking forms.** App Store Connect will eventually prompt the user to fill out "Agreements, Tax, and Banking" forms before *paid* apps can ship. This is NOT required for free apps or for TestFlight. Skip it for now; the user can fill it out later if they decide to charge for the app.
- **Sandbox testers / GameKit / iCloud.** Not needed for this game in its current scope. Don't enable any of these even if Apple suggests them.
