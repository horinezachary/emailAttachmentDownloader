# Email Attachment Downloader

Watches one or more mailboxes, finds e-mails whose subject matches your keywords, and saves their
picture attachments into a folder. Point the Windows lock screen slideshow (or anything else) at
that folder and new pictures show up on their own.

This branch is a ground-up rewrite of the original Java/Swing app in **C# on .NET 10**. The
behaviour you relied on is kept; the parts that were unstable or heavy are replaced. The Java
version is still on `master`.

> **Status: first pass.** It builds, and all unit tests pass on a Windows runner
> (`.github/workflows/build.yml`, which also publishes ready-to-run `win-x64` builds as an
> artifact). It was written without a local .NET SDK, so CI was its first real compile. What has
> **not** been exercised: the IMAP/POP3 code against a real mail server, and the tray UI on screen.
> See [Known gaps](#known-gaps).

## What it does

- Connects over **IMAP or POP3** (TLS) with MailKit.
- Matches on the **subject**, case-insensitively, using your keywords (any or all).
- Signs in with a password (for Gmail, an app password) or **Sign in with Google (OAuth)**.
- Optionally also requires the e-mail to be **sent to** one of your addresses (To or Cc). Useful when
  one mailbox receives mail for several addresses, such as `you+frames@gmail.com`.
- Saves attachments whose type matches (default `image/*`; add `application/pdf` etc. if you like).
- **Never saves the same picture twice**, even if it is renamed or re-sent, by comparing content hashes.
- Runs **on startup, on demand, and on a schedule**, one account at a time.
- Lives in the **system tray**; there is also a command-line tool for Task Scheduler.

## Layout

```
src/EmailAttachmentDownloader.Core    Mail, saving, de-dup, scheduling, storage. No UI. Cross-platform.
src/EmailAttachmentDownloader.Tray    Windows tray app (WinForms): settings, history, notifications.
src/EmailAttachmentDownloader.Cli     `ead`: list / run / test / set-password / import-legacy.
tests/EmailAttachmentDownloader.Core.Tests
docs/DISPLAY.md                       Plan for custom display and a .scr screensaver.
assets/                               App icon: app-icon.svg (master), app-icon.ico (embedded), app-icon.png.
installer/                            Inno Setup script for the Windows installer.
```

The core has no UI dependencies, so the tray app, the CLI, a Windows service, or a screensaver can
all sit on top of it.

## Install

Download `EmailAttachmentDownloader-Setup.exe` from the latest
[GitHub Release](https://github.com/horinezachary/emailAttachmentDownloader/releases) (or from the
`EmailAttachmentDownloader-Setup` artifact of any CI run) and run it.

- **No admin rights needed.** It installs for your Windows user only (under `%LOCALAPPDATA%\Programs`).
- **.NET is handled for you.** The app needs the .NET 10 *Desktop Runtime*. If it is missing, Setup
  asks, downloads it from Microsoft, checks that the download is signed by Microsoft, and runs it.
  That one step asks for administrator approval. If anything goes wrong you get a link to install it by hand.
- **Options:** start with Windows, and add the `ead` command-line tool to your PATH.
- **Uninstall** from Windows Settings, Apps. It removes the program but **keeps your settings, saved
  pictures and stored passwords** (they live in `%APPDATA%\EmailAttachmentDownloader` and your picture folders).
- Windows SmartScreen may warn about an unrecognised publisher, because the installer is not code-signed yet.
  Choose "More info", then "Run anyway".
- Silent install for scripts: `EmailAttachmentDownloader-Setup.exe /VERYSILENT /SUPPRESSMSGBOXES`.
  Add `/SKIPRUNTIMECHECK=1` if the runtime is managed some other way.

### Updates

The tray app checks GitHub for a newer release about 20 seconds after it starts and then once a day. If one
exists, a window shows what is new and offers **Download and install**, **Remind me later** or **Skip this
version** (skipped versions are not offered again by the automatic check). Installing downloads the installer,
checks it against the SHA-256 file published with the release, then runs it silently: the app closes, is
replaced, and starts again. Settings, passwords and saved pictures are untouched.

- The tray menu has **Check for updates now...** (always shows the newest release, even a skipped one) and
  **Check for updates automatically** to turn the automatic check off.
- Only releases that carry both `EmailAttachmentDownloader-Setup.exe` and its `.sha256` file are offered, and only
  `https://github.com` downloads are accepted. The checksum comes from the same release, so it catches a damaged
  or swapped download but is not a substitute for code signing.
- While the app's own version starts with `0.`, pre-releases are offered too (all current releases are
  pre-releases). A stable `1.x` or newer build is only offered stable releases.
- Builds are stamped with their version by CI (`v1.2.3` tag gives 1.2.3, other builds 0.0.<run number>); a plain
  local build is 1.0.0.
- If the check fails (offline, GitHub rate limit) the automatic check just logs it; the manual one tells you.

### Making a release

Push a tag like `v0.1.0`. CI builds and tests, builds the installer, and attaches it (with a SHA-256
file) to a new GitHub Release. Versions starting with `0.` are marked as pre-releases. The installer
script is `installer/EmailAttachmentDownloader.iss` (Inno Setup 6.1 or newer).

## Build and run

Requires the .NET 10 SDK.

```powershell
dotnet build
dotnet test
dotnet run --project src/EmailAttachmentDownloader.Tray
```

Or use the CLI:

```powershell
dotnet run --project src/EmailAttachmentDownloader.Cli -- list
dotnet run --project src/EmailAttachmentDownloader.Cli -- run
```

Local builds have no built-in Google client (see [Sign in with Google](#sign-in-with-google-oauth)); use an app
password, or the "use my own Google client" option.

CI also uploads ready-to-run `win-x64` builds (framework-dependent, so they need the .NET 10 Desktop Runtime) as a workflow artifact.

### Moving over from the Java version

```powershell
ead import-legacy C:\path\to\old\cfg
```

This reads the old `.cfg` files, creates matching accounts, and moves each password into protected
storage. You can then delete the `.cfg` files and the old `.messages` file.

### Gmail

Gmail does not accept your normal password over IMAP/POP3. There are two ways in; pick one per account
under **Sign-in method** in Settings.

**1. App password (simplest).** Turn on 2-Step Verification and create an **app password**, then use that as
the password. Test with the "Test connection" button.

**2. Sign in with Google (OAuth).** You approve the app in your browser and Google hands it a token, so no
password is stored at all. Release builds carry a Google client, so there is nothing to set up: choose the
method, click **Sign in with Google**, approve. Builds made without one (your own local or fork builds), or
accounts that opt to use their own client, need the one-time Google Cloud setup in
[Sign in with Google](#sign-in-with-google-oauth).

### Sign in with Google (OAuth)

**With the built-in client** (release builds): in Settings set **Sign-in method** to "Sign in with Google
(OAuth)", server `imap.gmail.com`, your Gmail address as the user name, click **Sign in with Google**, approve
in the browser, and press **Save**. **Test connection** checks it.

**Where the built-in client comes from.** CI reads the repository secrets `CLIENT_ID` and `CLIENT_SECRET`
(Settings, Secrets and variables, Actions) and compiles them into `EmailAttachmentDownloader.Core.dll` as
assembly metadata when it publishes. They are not in the source tree, and a local `dotnet build` has none.
To build with them locally:

```powershell
dotnet publish src/EmailAttachmentDownloader.Tray -c Release -p:GoogleClientId=... -p:GoogleClientSecret=...
```

(The same names work as environment variables.) Things worth knowing:

- A desktop app cannot keep a secret. Google's own guidance accepts that for installed apps, but it means
  anyone can pull the client ID and secret out of the installer. They identify the *app*, not any user's mail;
  a user's mail is protected by their own approval and sign-in token. Treat the secret as replaceable: if it is
  abused, rotate it in Google Cloud and rebuild.
- An app whose Google verification is incomplete is limited to about 100 users and shows the "Google hasn't
  verified this app" screen. Reading mail over IMAP needs the restricted scope `https://mail.google.com/`, so
  public use means going through Google's verification (and, for restricted scopes, a security assessment).
- A sign-in token only works with the client that issued it. Switching an account between the built-in client and
  its own client therefore means signing in again.

**Advanced: use your own Google client.** Tick **Use my own Google OAuth client** under the sign-in method to
supply your own client ID and secret instead. Builds without a built-in client always work this way. The setup
in [Google Cloud Console](https://console.cloud.google.com/):

1. Select or create a **project**.
2. **APIs & Services, Library:** enable **Gmail API** (this makes the Gmail permission available in step 4).
3. **Google Auth Platform** (older name: OAuth consent screen): set it up with an app name, your e-mail as
   the support contact, and audience **External**.
4. **Data Access:** add the scope `https://mail.google.com/` (listed as the Gmail API's "read, compose, send,
   and permanently delete all your email" permission). The app only reads mail; Google does not offer a
   narrower scope for IMAP. `openid` and `email` are also requested, only to show which account signed in.
5. **Audience:** add your Gmail address as a **test user**, then choose **Publish app** so it is "In production".
   Left in "Testing", Google expires the sign-in after **7 days**. Unverified apps show a "Google hasn't verified
   this app" screen; for your own project choose Advanced, then "Go to ... (unsafe)".
6. **Clients, Create client:** application type **Desktop app** (it must be this type), any name. Copy the
   **client ID** and **client secret** into Settings, then click **Sign in with Google**.

**There is no redirect URI to register.** The "Desktop app" type accepts a loopback address, and the app uses
`http://127.0.0.1:<a free port>/` for the few seconds the browser needs to hand back the result. (A "Web
application" client will not work, because it needs exact redirect addresses.)

- The client secret (if you use your own) and the sign-in token are stored with Windows DPAPI like passwords,
  never in `profiles.json`. A client ID is not secret and is kept in `profiles.json`.
- If Google stops accepting the saved sign-in (you revoked it at <https://myaccount.google.com/permissions>, or
  changed your Google password), the app does not fail quietly: a **sign-in needed** pop-up appears on top of your
  other windows, the tray icon's tooltip says so, and the tray menu gets a bold "Sign-in needed..." entry. Choose
  **Sign in with Google**, approve in the browser, and the account is saved and checked straight away. "Remind me
  later" closes the pop-up; it comes back at the next failed check after 4 hours (or from the tray menu). Being
  offline or a Google outage does not trigger it, only a sign-in that has to be redone.
- Set it up in Settings first; `ead run` and `ead test` then use the saved sign-in. There is no command-line sign-in yet.
- Gmail only. Microsoft (Outlook.com, Microsoft 365) needs a similar flow that is not built yet.

### Recipient filter

In Settings, tick **Only mail sent to** and list full addresses separated by commas. An e-mail must
match the subject keywords **and** be addressed (To or Cc) to one of the listed addresses.

- Matching is on the whole address, ignoring case. `you+frames@gmail.com` and `you@gmail.com` are
  different addresses; list each one you want.
- For `gmail.com` / `googlemail.com`, dots in the name part are ignored (`y.o.u@gmail.com` is `you@gmail.com`).
- Domain-only entries (`@example.com`) are not supported.
- Every address is checked when you press Save; the message names the bad entry.
- Unticking the box turns the filter off but keeps the list. Turning it on with an empty list is rejected.
- Bcc'd mail cannot be matched (the address is not in the message). Mail sent through an alias or a
  mailing list only matches if the address you listed appears in To or Cc.
- Changing the list makes the next run re-examine the mailbox, because mail skipped earlier may now
  qualify. Pictures already saved are never duplicated.

### Pausing an account

Untick **Enabled** to pause an account without deleting it. It shows as `Name (disabled)` in grey in
the account list. A paused account is skipped by the schedule, by "check on start", by the tray's
"Check mail now" and by `ead run`; **Save and check now** in Settings and `ead run <account>` still
check it, so you can test without re-enabling.

### Running without a resident process

If you would rather not keep the tray app running, schedule `ead run` with Windows Task Scheduler
instead. A lock file stops the two from syncing at the same time.

## Where things live

Settings and state are in `%APPDATA%\EmailAttachmentDownloader`:

| File | Contents |
|---|---|
| `profiles.json` | Accounts and rules. **No passwords.** |
| `update.json` | Update preferences: automatic checks on/off and a skipped version. |
| `credentials.json` | Passwords, Google client secrets and sign-in tokens, encrypted with Windows DPAPI (current user). |
| `library.json` | Index of every saved picture (hash, path, subject, sender, date). |
| `sync-state.json` | Per-account progress so only new mail is examined. |
| `history.jsonl` | Last 500 runs, shown under "Recent activity". |
| `logs\app.log` | Diagnostic log, rolled at 1 MB. |

Pictures go wherever you set "Save pictures to" for each account (default `Pictures\EmailPictures`).

## What changed from the Java version, and why

Bugs and weak spots found in a review of the original, and what replaced them:

| Original | Now |
|---|---|
| Exactly one keyword was treated as "no keywords" and downloaded everything. | One keyword filters like any other. Keywords can be phrases, and match mode is Any/All. |
| Uppercase keywords never matched (subject lowercased, keyword not). | Case-insensitive on both sides. |
| Worker thread busy-looped with no sleep and threw on an empty queue. | A 30-second timer tick and a work queue. Idle cost is effectively zero. |
| Re-downloaded the entire inbox on every run. | IMAP: remembers the last UID and only looks at newer mail. POP3: remembers seen UIDLs. |
| Duplicate check compared one repeated buffer, so different files could look identical. | SHA-256 of the content. |
| `\` vs `/` path handling that only worked on one OS. | `Path.Combine` throughout. |
| Attachment file names were used as-is (path traversal, missing names, reserved names). | Names are sanitised; files are created with `CreateNew` so nothing is overwritten. |
| A non-base64-encoded image part threw and abandoned the whole mailbox. | Each message and attachment is isolated; failures are counted and logged. |
| Passwords in `.cfg` files as base64 ("encoding, not encryption"). | Windows DPAPI, per user. `profiles.json` never holds a password. |
| `;`-delimited config broke on a `;` in a password or keyword. | JSON. |
| Run log was base64 and committed with a local path in it. | Rolling JSON-lines history in `%APPDATA%`; not in the repo. |

Behaviour to be aware of: the index remembers every picture it has saved. **If you delete a picture
from the folder, it will not be downloaded again** (that is how you reject one). To start over,
delete `library.json` and `sync-state.json`.

Note on credential storage: this uses DPAPI-encrypted values in a file rather than the Windows
Credential Manager UI. It is the same underlying protection (only your Windows user on this
machine can decrypt it) with much less interop code. Credential Manager can be swapped in behind
`ICredentialStore` later.

## Known gaps

- The **tray UI has never been looked at on screen**; it compiles, but expect layout rough edges.
- The mail code is **untested against a real server**. There is no mail-server test double, so the
  IMAP/POP3 paths are covered only by reading the code. Everything around them (matching, saving,
  de-dup, scheduling rules, storage, import) has unit tests.
- `MailSyncService`, `SyncScheduler`'s run loop and the WinForms code have no automated tests.
- Packages use floating versions (`MailKit 4.*` and so on) so the first restore cannot fail on a
  version that does not exist. Pin exact versions after the first successful restore.
- Whole messages are downloaded for matches. Fetching only the attachment parts over IMAP would
  save bandwidth.
- Settings are discarded if you close the window without pressing Save (no "unsaved changes" prompt yet).
- The installer's .NET download-and-install path has not been run on a machine without .NET 10 (CI covers install, the CLI, the PATH option and uninstall, but its runner already has .NET). Upgrading over a running copy relies on Windows asking the app to close.
- Google sign-in has only been tested with stubbed Google responses, not against Google itself, and the built-in client is only in CI-built releases. Microsoft OAuth is not built. Sign-in must be done in the tray app's Settings (no CLI sign-in).

## Roadmap

See [docs/DISPLAY.md](docs/DISPLAY.md) for how richer display control and a `.scr` screensaver are
meant to plug in.
