using System.Diagnostics;
using EmailAttachmentDownloader.Core;
using Microsoft.Extensions.Logging;

namespace EmailAttachmentDownloader.Tray;

/// <summary>
/// The whole app: a tray icon that owns the scheduler. There is no main window, so when nothing is
/// due the process just sleeps.
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private readonly ILoggerFactory _loggerFactory;
    private readonly AppPaths _paths = new();
    private readonly ProfileStore _profileStore;
    private readonly ProfileCatalog _catalog;
    private readonly ICredentialStore _credentials;
    private readonly ImageLibrary _library;
    private readonly SyncStateStore _state;
    private readonly RunHistory _history;
    private readonly MailSyncService _sync;
    private readonly SyncScheduler _scheduler;

    // Never shown. Its only job is to give worker threads a safe way to run code on the UI thread.
    private readonly Control _marshal = new();

    private readonly ToolStripMenuItem _startWithWindows = new("Start with Windows") { CheckOnClick = false };

    private SettingsForm? _settingsForm;
    private HistoryForm? _historyForm;

    public TrayApplicationContext()
    {
        _ = _marshal.Handle; // force handle creation on the UI thread

        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddProvider(new FileLoggerProvider(_paths.LogFile));
        });

        _profileStore = new ProfileStore(_paths.ProfilesFile);
        _catalog = new ProfileCatalog(_profileStore);
        _credentials = CredentialStores.CreateDefault(_paths);
        _library = new ImageLibrary(_paths.LibraryFile);
        _state = new SyncStateStore(_paths.SyncStateFile);
        _history = new RunHistory(_paths.HistoryFile);

        _sync = new MailSyncService(
            _library, _state, _credentials, _paths.LockFile, _loggerFactory.CreateLogger<MailSyncService>());
        _sync.AttachmentSaved += OnAttachmentSaved;

        _scheduler = new SyncScheduler(
            _catalog.Snapshot, _sync, _history, _state, _loggerFactory.CreateLogger<SyncScheduler>());
        _scheduler.RunCompleted += OnRunCompleted;

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Email Attachment Downloader",
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };
        _icon.DoubleClick += (_, _) => ShowSettings();

        _scheduler.Start();

        if (_profileStore.LoadWarning is not null)
        {
            _icon.ShowBalloonTip(8000, "Email Attachment Downloader", _profileStore.LoadWarning, ToolTipIcon.Warning);
        }

        if (_catalog.Snapshot().Count == 0)
        {
            // First launch: go straight to adding an account (queued until the message loop is running).
            RunOnUiThread(ShowSettings);
        }
    }

    private void RunOnUiThread(Action action)
    {
        try
        {
            if (!_marshal.IsDisposed && _marshal.IsHandleCreated)
            {
                _marshal.BeginInvoke(action);
            }
        }
        catch (InvalidOperationException)
        {
            // The app is shutting down.
        }
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Check mail now", null, (_, _) => _scheduler.RequestRunAll());
        menu.Items.Add("Open picture folder", null, (_, _) => OpenPictureFolder());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings...", null, (_, _) => ShowSettings());
        menu.Items.Add("Recent activity...", null, (_, _) => ShowHistory());

        _startWithWindows.Checked = StartupRegistration.IsEnabled();
        _startWithWindows.Click += (_, _) =>
        {
            bool enable = !StartupRegistration.IsEnabled();
            try
            {
                StartupRegistration.SetEnabled(enable);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
            {
                MessageBox.Show("Could not change the startup setting: " + ex.Message, "Email Attachment Downloader");
            }

            _startWithWindows.Checked = StartupRegistration.IsEnabled();
        };
        menu.Items.Add(_startWithWindows);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Quit());
        return menu;
    }

    private void OpenPictureFolder()
    {
        var folders = _catalog.Snapshot()
            .Select(p => p.SaveFolder)
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (folders.Count == 0)
        {
            ShowSettings();
            return;
        }

        foreach (string folder in folders)
        {
            if (Directory.Exists(folder))
            {
                Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
            }
        }
    }

    private void ShowSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.Activate();
            return;
        }

        _settingsForm = new SettingsForm(_catalog, _credentials, _sync, _scheduler, _state);
        _settingsForm.Show();
    }

    private void ShowHistory()
    {
        if (_historyForm is { IsDisposed: false })
        {
            _historyForm.Activate();
            return;
        }

        _historyForm = new HistoryForm(_history);
        _historyForm.Show();
    }

    // These two fire on the scheduler's worker thread, so hop to the UI thread before touching controls.
    private void OnRunCompleted(object? sender, RunResult result)
    {
        RunOnUiThread(() =>
        {
            if (result.NewFiles > 0 || !result.Succeeded)
            {
                _icon.ShowBalloonTip(
                    5000,
                    "Email Attachment Downloader",
                    result.Summary,
                    result.Succeeded ? ToolTipIcon.Info : ToolTipIcon.Warning);
            }

            if (_historyForm is { IsDisposed: false })
            {
                _historyForm.RefreshList();
            }
        });
    }

    private void OnAttachmentSaved(object? sender, AttachmentSavedEventArgs e)
    {
        // Hook point: a future slideshow or screensaver can be told about new pictures here.
    }

    private void Quit()
    {
        _icon.Visible = false;
        _settingsForm?.Close();
        _historyForm?.Close();

        try
        {
            _scheduler.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // Shutting down anyway.
        }

        _icon.Dispose();
        _marshal.Dispose();
        _loggerFactory.Dispose();
        ExitThread();
    }
}
