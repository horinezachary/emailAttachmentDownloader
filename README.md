# Email Attachment Downloader

Watches one or more mailboxes, finds e-mails whose subject matches your keywords, and saves their
picture attachments into a folder. Point the Windows lock screen slideshow (or anything else) at
that folder and new pictures show up on their own.

This branch is a ground-up rewrite of the original Java/Swing app in **C# on .NET 10**. The
behaviour you relied on is kept; the parts that were unstable or heavy are replaced. The Java
version is still on `master`.

> **Status: first pass.** It builds, and all 64 unit tests pass on a Windows runner
> (`.github/workflows/build.yml`, which also publishes ready-to-run `win-x64` builds as an
> artifact). It was written without a local .NET SDK, so CI was its first real compile. What has
> **not** been exercised: the IMAP/POP3 code against a real mail server, and the tray UI on screen.
> See [Known gaps](#known-gaps).

## What it does

- Connects over **IMAP or POP3** (TLS) with MailKit.
- Matches on the **subject**, case-insensitively, using your keywords (any or all).
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
```

The core has no UI dependencies, so the tray app, the CLI, a Windows service, or a screensaver can
all sit on top of it.

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

CI uploads ready-to-run `win-x64` builds (framework-dependent) as a workflow artifact.

### Moving over from the Java version

```powershell
ead import-legacy C:\path\to\old\cfg
```

This reads the old `.cfg` files, creates matching accounts, and moves each password into protected
storage. You can then delete the `.cfg` files and the old `.messages` file.

### Gmail

Gmail does not accept your normal password over IMAP/POP3. Turn on 2-Step Verification and create an
**app password**, then use that in Settings. Test with the "Test connection" button.

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
| `credentials.json` | Passwords, encrypted with Windows DPAPI (current user). |
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
- The tray icon is a stock Windows icon.
- Sign-in is username and password only. OAuth2 (Gmail/Outlook) is possible with MailKit later.

## Roadmap

See [docs/DISPLAY.md](docs/DISPLAY.md) for how richer display control and a `.scr` screensaver are
meant to plug in.
