# Phone share: how the iPhone app and the PC talk

The iPhone companion app (`ios/`) and the Windows app's **Phone share** account type meet in the
middle through an ordinary mailbox. There is no server and no special protocol; the phone sends
e-mail, the PC reads it the way it reads any other account.

```
iPhone                         your mailbox                    PC
share sheet ──SMTP──►  me+frames@gmail.com  ◄──IMAP/POP3──  Phone share account
 (app password)         subject: [ead:a1b2c3d4e5] ...         matches the tag, saves pictures
```

## Pairing

Pairing tells the phone **where to send** and **which tag to put in the subject**. On the PC, open
Settings, choose Account type *Phone share*, and press *Show pairing code*. The code is a QR
and a link:

```
ead://pair?v=1&name=<name>&to=<address>&tag=<tag>
```

| Part | Meaning |
|---|---|
| `v` | Format version. Currently `1`. A phone that sees a higher number refuses the code and asks you to update the app. |
| `name` | Label shown in the app, such as `Family frames`. |
| `to` | Address the photos are mailed to. Usually the mailbox the PC reads, or a `+tag` alias of it. |
| `tag` | Random lowercase `[a-z0-9]`, 6 to 32 characters. The PC generates 10 hex characters. |

Values are percent-encoded (`Uri.EscapeDataString` on the PC). The code **contains no password**.

The tag is a filter, **not a secret**. Anyone who knows the address and the tag can put pictures in
your frame folder. Press *New tag* on the PC to cut off old pairings; phones then need to scan again.

## The message

- **Subject:** `[ead:<tag>] Photos from iPhone (1/2)`. The part after the marker is free text.
- **Attachments:** the pictures, as `multipart/mixed`. HEIC is converted to JPEG and very large
  photos are scaled down (default longest side 3840 px) before sending. Re-encoded photos carry no
  Exif block, so location data is not sent.
- **Size:** about 15 MB of pictures per message, so mail servers do not refuse them. A bigger set is
  split over several messages, which is why the subject has `(i/n)`.
- **From:** whatever account the phone is signed into. The PC does not check the sender.

## What the PC does

A Phone share account differs from an e-mail account in one way: **a message matches when its
subject contains `[ead:<tag>]`** (case-insensitive) instead of matching the keywords. Everything
else is the normal pipeline: attachment types, content-hash de-duplication, the saving folder, the
schedule. Because of the hashing, a photo sent twice (for example after a retry) is saved once.

Changing the tag resets the account's mailbox cursor, the same as changing keywords does, so
old messages are looked at again under the new rule.

## Sending from the phone

The phone mails through its own SMTP account (Gmail: `smtp.gmail.com`, port 465, an
[app password](https://myaccount.google.com/apppasswords)). The password is kept in the iOS
keychain, shared between the app and its share extension, and is never written to a file.

Photos are copied into an on-disk outbox first. If the phone is offline or the server refuses, they
stay there and are retried when the app is opened and, when iOS allows, in the background.
