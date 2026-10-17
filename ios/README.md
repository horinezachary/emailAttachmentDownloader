# Photo Frame for iPhone

A small iOS app that e-mails photos to the mailbox Email Attachment Downloader watches, so you can
send a picture to your frame straight from the share sheet. It pairs with a **Phone share**
account in the Windows app. The wire format is described in [`docs/PHONE-SHARE.md`](../docs/PHONE-SHARE.md).

> **Status: written but never built.** This code was produced without Xcode or a Swift toolchain,
> so nobody has compiled it yet. The first real build is the macOS job in
> `.github/workflows/ios.yml` or your own Mac. Expect to fix a few compile errors and some
> layout rough edges. The unit tests (pairing, SMTP replies, MIME, image preparation, storage) are
> there to make that first run informative. Nothing has been tried against a real SMTP server or on
> a real phone.

## What it does

- **Share sheet:** pick photos in Photos (or any app), tap Share, then *Photo Frame*. A small sheet
  confirms the frame and sends. Handled by the `ShareExtension` target.
- **Shortcuts, Siri, Spotlight, Action Button:** the *Send Photos to a Frame* App Intent
  (`App/Intents/SendPhotosIntent.swift`) takes photos and an optional frame.
- **In the app:** pick photos with the system picker and send.
- **Pairing:** scan the QR code from the PC's Settings, or paste the `ead://pair?...` link. Opening
  the link on the phone works too.
- **Outbox:** photos are copied to disk before sending. If the phone is offline or the server says
  no, they wait there and are retried when the app opens and, when iOS allows, in the background.
- HEIC is sent as JPEG and very large photos are scaled down (both adjustable in Settings). Re-encoded
  photos carry no location data.

## Build it

You need a Mac with Xcode 16 or later and [XcodeGen](https://github.com/yonaskolb/XcodeGen).

```sh
brew install xcodegen
cd ios
xcodegen generate
open EADCompanion.xcodeproj
```

The Xcode project and the `Info.plist` and entitlements files are generated from `project.yml` and
are not committed. To run on a device, pick your team under *Signing & Capabilities* for **both** the
app and the share extension (or put it in `DEVELOPMENT_TEAM` in `project.yml`).

To run the tests without signing:

```sh
xcodebuild test -project EADCompanion.xcodeproj -scheme EADCompanion \
  -destination 'platform=iOS Simulator,name=iPhone 16' CODE_SIGNING_ALLOWED=NO
```

(Use any iPhone simulator you have; the CI job picks one automatically.)

### Using your own bundle id

The defaults are `dev.horine.ead` (app), `dev.horine.ead.share` (extension), App Group
`group.dev.horine.ead` and keychain group `dev.horine.ead.shared`. An App Group has to be unique to
you in practice, so change them if signing complains. They appear in `project.yml` **and** in
`Shared/AppConfig.swift` (`appGroupID`, `keychainService`, `backgroundTaskID`); keep both in step.

## Set it up

1. **On the PC:** Settings, add an account, Account type *Phone share*. Fill in the mailbox the PC reads
   (the same IMAP/POP3 details as any account) and the address photos should go to. Press
   *Show pairing code*.
2. **On the phone:** Frames tab, *Scan code*. Confirm the name.
3. **Mail account:** Settings tab. For Gmail, use `smtp.gmail.com`, port 465, your address, and an
   [app password](https://myaccount.google.com/apppasswords) (needs 2-step verification). Tap
   *Test sign-in*.
4. Share a photo.

## Layout

```
project.yml          XcodeGen spec: three targets, entitlements and plists are generated from it
Shared/              Used by the app, the share extension and the tests (compiled into each)
  Pairing.swift        parse ead://pair links
  SmtpClient.swift     small SMTP client over CFStream (TLS or STARTTLS, AUTH PLAIN/LOGIN)
  MimeBuilder.swift    streams a multipart message to disk; splits big sets into messages
  ImagePreparer.swift  HEIC to JPEG, downscaling, via ImageIO thumbnails (low memory)
  Storage.swift        settings file and the on-disk outbox, in the App Group folder
  OutboxSender.swift   sends a batch, updates its state, retries
  Keychain.swift       mail password, shared by the app and the extension
App/                 SwiftUI app, App Intents, QR scanner
ShareExtension/      The share sheet UI and its photo loading
Tests/               XCTest, hostless
```

## Limits and choices

- **Password sign-in only.** The phone sends through SMTP with a password (an app password for Gmail).
  There is no Google or Microsoft OAuth on the phone.
- **The tag is not a secret.** It only tells the PC which messages are for it. Anyone with the address
  and tag can send pictures. *New tag* on the PC revokes old pairings.
- **Share extensions are memory-limited** (roughly 120 MB), so photos are processed one at a time and
  streamed to disk. A very large selection can still be better sent from the app.
- **Background retries are best effort.** iOS decides when a background refresh runs. Opening the app
  always retries.
- **No app icon yet.** iOS icons must be a square, fully opaque PNG, which the Windows icon is not.
  Add one in an asset catalog before distributing.
- No iPad-specific layout, no localisation, English only.
