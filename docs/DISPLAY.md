# Display control and the screensaver

Today the app only fills a folder, and the Windows lock screen slideshow does the displaying. This
note records what is already in place for taking over display later, so it stays an add-on rather
than a rewrite.

## What the lock screen can and cannot do

The lock screen slideshow is built into Windows: you pick folders, and Windows decides order,
timing and cropping. It cannot run custom code, so it cannot, for example, favour newer pictures,
group by sender, or caption a picture with its subject. Anything that needs that kind of control
has to be a program that draws the pictures itself.

Two ways to get there, and they can coexist:

1. **A curated folder for the lock screen.** The app keeps the lock-screen folder to a chosen subset
   (newest N, one sender, pictures from the last month) by copying or removing files according to a
   rule. Windows still does the display, but you control what is eligible.
2. **A screensaver (`.scr`).** A real program that draws the pictures. Full control over order,
   timing, transitions, fit/crop and captions. Note that it runs when the PC is idle, not on the
   lock screen itself.

## Hooks that already exist

- **`ImageLibrary`** (`library.json`) records every saved picture: file path, content hash, account,
  subject, sender, date received and date saved. `ImageLibrary.AllExisting()` returns the pictures
  that are still on disk, newest first. A slideshow or screensaver reads this instead of scanning
  folders, so it knows where each picture came from. It can run in a separate process: writes are
  atomic, and `Reload()` picks up new pictures while it is running.
- **`MailSyncService.AttachmentSaved`** fires for every new file. Today the tray app ignores it
  (`TrayApplicationContext.OnAttachmentSaved`); this is the place to react immediately, for example
  to refresh a curated folder or tell a running slideshow.
- **Core has no UI dependencies.** A new project can reference it directly.

## Adding the screensaver

A `.scr` is just an executable with a different extension. Windows starts it with:

| Argument | Meaning |
|---|---|
| `/s` | Run full screen |
| `/c` | Show the settings dialog |
| `/p <hwnd>` | Draw a preview inside the small window in the Screen Saver dialog |

Plan:

1. Add `src/EmailAttachmentDownloader.Screensaver` (WinForms, `net10.0-windows`, `WinExe`) that
   references Core.
2. Parse those arguments in `Main`; `/s` opens a borderless full-screen form per monitor, exits on
   any key, mouse click or significant mouse movement.
3. Read `ImageLibrary.AllExisting()` and show pictures according to a small `DisplayOptions` model
   (order, seconds per picture, fit/fill, show caption). Store it as JSON next to `profiles.json`.
4. A post-build step copies the output to `*.scr`. Installing is right-click, then "Install".

Open questions for when we get there: what ordering rules you want, whether to show the e-mail
subject or sender as a caption, and whether to also maintain a curated lock-screen folder.
