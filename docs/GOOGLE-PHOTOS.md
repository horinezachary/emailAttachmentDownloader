# Plan: a "Google Photos" account type

Status: **plan only, nothing built.** Researched 2026-10-15 against Google's current documentation.

## The short version

"Connect a Google account, then choose albums to keep in sync" is **no longer possible with Google's official
APIs.** Since 31 March 2025 an app can only read photos it uploaded itself, and the one way to reach a user's own
library is the **Picker API**, where the user selects items in a Google-run screen **every time**. There is no
background sync and no way to list a user's albums.

What we *can* build is a Google Photos account type where the user signs in with Google, clicks **Choose photos...**,
picks an album's photos (or any photos) in Google's own picker in their browser, and the app downloads them into
the account's folder like mail attachments. Re-running it later only adds what is new, and the app can remind the
user to refresh. It is a manual refresh, not a sync, and that difference should be visible in the UI and README.

## What Google allows today

| Fact | Source |
|---|---|
| `photoslibrary.readonly`, `photoslibrary.sharing`, `photoslibrary` scopes were removed (31 Mar 2025, banner says 1 Apr). Calls that rely only on them return 403. | [Updates to the Google Photos APIs](https://developers.google.com/photos/support/updates) |
| Library API list/get/search (`albums.list`, `mediaItems.list`, `mediaItems.search`...) now only return content **the app itself uploaded**. Shared-album methods were removed. | same |
| The Picker API replaces it for user-owned content. Scope: `https://www.googleapis.com/auth/photospicker.mediaitems.readonly`. | [Authorization](https://developers.google.com/photos/overview/authorization) |
| Flow: create a session, open its `pickerUri` for the user, poll until `mediaItemsSet` is true, list the picked items, download each `baseUrl` with a bearer token, delete the session. | [Get started](https://developers.google.com/photos/picker/guides/get-started-picker), [Sessions](https://developers.google.com/photos/picker/guides/sessions) |
| The picker shows recent photos; "albums, favorites and other categories are not shown directly". Users **search** (keywords, dates, places, **album titles**) and select. The launch post says users can "search for and select photos and albums". | [What users see](https://developers.google.com/photos/picker/guides/picking-experience), [launch post](https://developers.googleblog.com/en/google-photos-picker-api-launch-and-library-api-updates/) |
| A session returns at most **2,000** items (`pickingConfig.maxItemCount`, default 2,000). | [Sessions reference](https://developers.google.com/photos/picker/reference/rest/v1/sessions) |
| `baseUrl`s last **60 minutes** and need an `Authorization: Bearer` header. Images: `=d` gives the file with Exif **minus location**. Videos: `=dv` gives a **transcoded** copy, not the original. | [Media items](https://developers.google.com/photos/picker/guides/media-items) |
| Apps using the Photos APIs must pass OAuth verification. Whether the Picker scope counts as sensitive is **not stated** in the docs. | [Authorization](https://developers.google.com/photos/overview/authorization) |
| No release notes after April 2025 mention new Picker features (page last updated 2025-08-28). | [Release notes](https://developers.google.com/photos/library/support/release-notes) |

Maintainers of sync tools reached the same conclusion: the Picker API gives "no background syncing capability" and
`gphotos-sync` was archived over it ([discussion](https://github.com/gilesknap/gphotos-sync/issues/511)).

## Options considered

| Option | Verdict |
|---|---|
| **A. Picker API account type** (this plan) | Supported and policy-safe. Manual refresh only. Recommended. |
| B. Library API `mediaItems.list` / `albums.list` | Dead for this purpose: returns only app-uploaded items. |
| C. Google Takeout | Takeout can export chosen albums on a schedule to Drive (from memory, unverified: every 2 months for a year). Would need a "Google Drive folder" account type, restricted Drive scopes, and unzipping Takeout's folder layout and JSON sidecars. Heavy and slow-moving. Possible later, separate idea. |
| D. Scraping shared-album links | Unsupported, breaks without notice, likely against Google's terms. Not recommended. |
| E. The existing e-mail route | Unchanged. Google Photos cannot e-mail albums, but people can still e-mail themselves pictures. |

## Proposed design (option A)

### What the user sees

1. **Add** an account, set **Account type** to "Google Photos (pick photos)".
2. Fields: name, save folder, picture types (default `image/*`), minimum size, "remind me to refresh every N days"
   (0 = never), **Sign in with Google**. No server, keywords or recipient settings.
3. **Choose photos from Google Photos...** (in Settings, and in the tray menu under a per-account sub-menu):
   the browser opens Google's picker; the app says "Search for your album's name, select the photos, press Done".
   The app waits (cancellable), then downloads the picks with progress in the **Activity** window, and finishes with a
   balloon: "Family: 212 new, 1,788 already saved".
4. Repeat any time. Photos already saved (same content hash) are skipped, so re-picking a whole album only adds new
   pictures.
5. Optional reminder balloon every N days: "It has been 30 days since you refreshed Family. Choose photos now?"

### Code changes

- **Model:** `AccountProfile.Kind` (`Mail` default, `GooglePhotos`), so existing `profiles.json` files load unchanged.
  Validation branches by kind. `Scheduled`, `RunOnStartup`, filters and server fields are ignored for Photos accounts
  (`IsDue` returns false; "Check mail now" skips them). Add `LastPickedAt` to sync state for reminders.
- **Auth:** generalise `GoogleOAuth` to take a scope list. Photos accounts request only
  `openid email photospicker.mediaitems.readonly` and get their own refresh token (same DPAPI key scheme), so a
  Photos token never carries mail access and the reverse. Reuse the built-in client and the "own client" override.
  The Google Cloud project needs the **Photos Picker API** enabled and the scope added to the consent screen.
- **Picker client** (`PhotosPickerClient`, Core, `HttpClient`-based so tests can stub it): `CreateSession`,
  `GetSession` (honours `pollingConfig`), `ListPickedItems` (paged), `Download` (`=d`, re-list on 401/403 when a
  `baseUrl` has expired), `DeleteSession`.
- **Saving:** pull the "hash, de-duplicate, write unique file name, record in `ImageLibrary`, raise `AttachmentSaved`"
  steps out of `AttachmentSaver` into a bytes-based method both sources use. `LibraryImage.Subject/From` get a
  Photos-flavoured value (album search text the user typed is not available, so use the item's filename and "Google
  Photos").
- **Orchestration:** `PhotosPickService.RunAsync(profile, ct)` returns the usual `RunResult`, so history, the
  sign-in-needed pop-up and notifications work unchanged. Progress reuses `SyncProgress` with two new phases
  (waiting for the picker, downloading N of M). The `SyncScheduler` stays mail-only; the tray context calls the
  Photos service directly and registers it in `CurrentActivity()` so it appears under **Running now**.
- **UI:** `SettingsForm` shows the fields that apply to the chosen kind. Tray: "Choose photos from Google Photos"
  sub-menu (one entry per Photos account). Reminder timer next to the update timer.
- **CLI:** none at first (sign-in is Settings-only today). A later `ead photos` command could download a pick that
  was started in the tray.

### Tests (all stubbed, CI-runnable)

Picker client: session create/poll/list/delete request shapes, `pollingConfig` backoff, timeout, user never finishing,
multi-page listing, expired `baseUrl` re-list, 403 mapped to "sign in again". Service: duplicates skipped,
type/size filters, files written with unique names, library entries, progress events, cancellation. Model:
old profile JSON loads as `Mail`, Photos validation, round trip.

## Risks and open questions

1. **Does picking an album yield its photos?** The docs say users search album titles and "select photos and
   albums" but never show an album selection returning its contents. A real-account test settles this on day one. If
   an album is not selectable as a unit, the UX becomes "search the album, select all" (still fine for up to 2,000).
2. **Not byte-identical originals:** images lose location Exif; videos are transcoded. Fine for a photo frame, worth
   stating in the README. Default filter stays `image/*`.
3. **OAuth verification:** the scope's sensitivity class is not documented; the Cloud console's Data Access page
   shows it. Unverified apps are capped (about 100 users) and show the warning screen. Same posture as the mail
   sign-in.
4. **Policy:** the Photos APIs have their own developer policy. Saving a user's own picks to their own disk for
   personal display looks in scope, but read the "Photos API User Data and Developer Policy" before any wider
   release.
5. **Item ids across sessions:** unknown whether a picked item's `id` is stable. If it is, we can skip re-downloading
   known items; if not, we download then hash (correct, just slower). Measure during the spike.
6. **Quotas and rate limits** for `baseUrl` downloads are not documented on the Picker pages; download sequentially
   with small delays and log failures.
7. **Session timeout length** is not documented; rely on `pollingConfig.timeoutIn` from the response.
8. **Docs may move:** nothing newer than Aug 2025 was found, but re-check the release notes before building.

## Suggested order of work

1. **Spike (small, needs you):** enable the Photos Picker API in your Cloud project, add the scope to the consent
   screen, and test picking an album through a minimal build to answer risks 1, 5 and 7.
2. **PR 1, Core:** `Kind`, scoped OAuth, picker client, bytes-based saving, service, tests. No UI.
3. **PR 2, Tray:** Settings fields, "Choose photos..." flow and menu, Activity integration.
4. **PR 3, Polish:** reminders, sign-in-needed pop-up for Photos accounts, README and installer notes.

## Decisions needed

- Is a manual "choose photos" refresh acceptable, or is automatic background sync the point? If it is, option C
  (Takeout to Drive) is the only automatable path and is a different, larger project.
- Reminders on by default, or opt-in?
- Should Photos accounts also accept videos, or stay images-only?
