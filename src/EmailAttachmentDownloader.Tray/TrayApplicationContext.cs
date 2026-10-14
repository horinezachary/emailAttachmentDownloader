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
    private readonly ToolStripMenuItem _signInNeeded = new("Sign-in needed...") { Visible = false };
    private readonly ToolStripMenuItem _activityItem = new("") { Visible = false, Enabled = false };
    private readonly ToolStripMenuItem _folderMenu = new("Open picture folder");
    private readonly ToolStripSeparator _activitySeparator = new() { Visible = false };

    // Accounts whose Google sign-in must be redone, and when each was last put in front of the user.
    private static readonly TimeSpan ReminderInterval = TimeSpan.FromHours(4);
    private readonly Dictionary<string, string> _needSignIn = new();
    private readonly Dictionary<string, DateTime> _lastPrompted = new();
    private SignInNeededForm? _signInForm;

    // Updates: checked shortly after startup and then daily, and on demand from the tray menu.
    private static readonly TimeSpan UpdateStartupDelay = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromHours(24);
    private readonly UpdateSettingsStore _updateSettings;
    private readonly UpdateChecker _updateChecker = new();
    private readonly UpdateDownloader _updateDownloader = new();
    private readonly System.Windows.Forms.Timer _updateTimer = new();
    private readonly ToolStripMenuItem _autoUpdate = new("Check for updates automatically") { CheckOnClick = false };
    private UpdateAvailableForm? _updateForm;
    private bool _updateCheckRunning;

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
        _updateSettings = new UpdateSettingsStore(_paths.UpdateFile);
        DeleteOldUpdateDownloads();

        _sync = new MailSyncService(
            _library, _state, _credentials, _paths.LockFile, _loggerFactory.CreateLogger<MailSyncService>());
        _sync.AttachmentSaved += OnAttachmentSaved;

        _scheduler = new SyncScheduler(
            _catalog.Snapshot, _sync, _history, _state, _loggerFactory.CreateLogger<SyncScheduler>());
        _scheduler.RunCompleted += OnRunCompleted;
        _scheduler.ActivityChanged += (_, _) => RunOnUiThread(UpdateTrayText);

        _icon = new NotifyIcon
        {
            Icon = AppIcon.ForTray(),
            Text = "Email Attachment Downloader",
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };
        _icon.DoubleClick += (_, _) => ShowSettings();

        _scheduler.Start();

        _updateTimer.Tick += async (_, _) =>
        {
            _updateTimer.Interval = (int)UpdateInterval.TotalMilliseconds;
            if (_updateSettings.Current.CheckAutomatically)
            {
                await CheckForUpdatesAsync(manual: false);
            }
        };
        _updateTimer.Interval = (int)UpdateStartupDelay.TotalMilliseconds;
        _updateTimer.Start();

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
        _signInNeeded.Font = new Font(_signInNeeded.Font, FontStyle.Bold);
        _signInNeeded.Click += (_, _) => ShowSignInPrompt();
        menu.Items.Add(_signInNeeded);
        menu.Items.Add(_activityItem);
        menu.Items.Add(_activitySeparator);
        menu.Items.Add("Check mail now", null, (_, _) => _scheduler.RequestRunAll());

        // One entry per account, filled in when the menu opens so it always matches the current accounts.
        _folderMenu.DropDownItems.Add(new ToolStripMenuItem("..."));
        _folderMenu.DropDownOpening += (_, _) => FillFolderMenu();
        menu.Items.Add(_folderMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings...", null, (_, _) => ShowSettings());
        menu.Items.Add("Activity...", null, (_, _) => ShowHistory());

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

        menu.Items.Add("Check for updates now...", null, async (_, _) => await CheckForUpdatesAsync(manual: true));
        _autoUpdate.Checked = _updateSettings.Current.CheckAutomatically;
        _autoUpdate.Click += (_, _) =>
        {
            bool enable = !_updateSettings.Current.CheckAutomatically;
            try
            {
                _updateSettings.Update(u => u.CheckAutomatically = enable);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show("Could not save the setting: " + ex.Message, "Email Attachment Downloader");
            }

            _autoUpdate.Checked = _updateSettings.Current.CheckAutomatically;
        };
        menu.Items.Add(_autoUpdate);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Quit());
        return menu;
    }

    private void FillFolderMenu()
    {
        IReadOnlyList<AccountProfile> accounts = _catalog.Snapshot();
        _folderMenu.DropDownItems.Clear();

        if (accounts.Count == 0)
        {
            _folderMenu.DropDownItems.Add("Add an account...", null, (_, _) => ShowSettings());
            return;
        }

        foreach (AccountProfile account in accounts)
        {
            string folder = account.SaveFolder;
            string label = account.Name.Replace("&", "&&") + (account.Enabled ? "" : " (paused)");
            var item = new ToolStripMenuItem(label, null, (_, _) => OpenFolder(folder)) { ToolTipText = folder };
            _folderMenu.DropDownItems.Add(item);
        }

        var distinct = accounts
            .Select(a => a.SaveFolder)
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (distinct.Count > 1)
        {
            _folderMenu.DropDownItems.Add(new ToolStripSeparator());
            _folderMenu.DropDownItems.Add("All folders", null, (_, _) => distinct.ForEach(OpenFolder));
        }
    }

    private void OpenFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            ShowSettings();
            return;
        }

        if (!Directory.Exists(folder))
        {
            _icon.ShowBalloonTip(
                5000,
                "Email Attachment Downloader",
                $"Nothing has been saved to {folder} yet. The folder is created when the first picture arrives.",
                ToolTipIcon.Info);
            return;
        }

        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
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

        _historyForm = new HistoryForm(_history, _scheduler);
        _historyForm.Show();
    }

    // These two fire on the scheduler's worker thread, so hop to the UI thread before touching controls.
    private void OnRunCompleted(object? sender, RunResult result)
    {
        RunOnUiThread(() =>
        {
            bool prompting = false;
            if (result.NeedsSignIn)
            {
                prompting = TrackSignInNeeded(result);
            }
            else if (result.Succeeded)
            {
                ClearSignInNeeded(result.AccountId);
            }

            // The pop-up already says what is wrong, so skip the balloon for the same failure.
            if (!prompting && (result.NewFiles > 0 || !result.Succeeded))
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

    /// <summary>Remembers that an account needs a new sign-in and shows the pop-up unless it was shown recently. True if shown.</summary>
    private bool TrackSignInNeeded(RunResult result)
    {
        _needSignIn[result.AccountId] = result.AccountName;
        UpdateSignInIndicators();

        bool open = _signInForm is { IsDisposed: false };
        bool due = !_lastPrompted.TryGetValue(result.AccountId, out DateTime last) || DateTime.UtcNow - last >= ReminderInterval;
        if (!open && !due)
        {
            return false;
        }

        ShowSignInPrompt();
        return true;
    }

    private void ClearSignInNeeded(string accountId)
    {
        _lastPrompted.Remove(accountId);
        if (_needSignIn.Remove(accountId))
        {
            _signInForm?.Remove(accountId);
            UpdateSignInIndicators();
        }
    }

    private void UpdateSignInIndicators()
    {
        _signInNeeded.Visible = _needSignIn.Count > 0;
        UpdateTrayText();
    }

    /// <summary>Keeps the tray tooltip and the menu's status line in step with what is running and what needs attention.</summary>
    private void UpdateTrayText()
    {
        IReadOnlyList<SyncActivity> activity = _scheduler.CurrentActivity();
        string tip = "Email Attachment Downloader";

        if (activity.Count == 0)
        {
            _activityItem.Visible = false;
            _activitySeparator.Visible = _signInNeeded.Visible;
        }
        else
        {
            SyncActivity first = activity[0];
            string waiting = activity.Count > 1 ? $" (+{activity.Count - 1} waiting)" : "";
            _activityItem.Text = (first.IsRunning ? $"{first.AccountName}: {first.Describe()}" : $"{first.AccountName}: waiting") + waiting;
            _activityItem.Visible = true;
            _activitySeparator.Visible = true;

            SyncProgress? p = first.Progress;
            string detail = p is { Total: > 0 } ? $"{p.Done:N0}/{p.Total:N0}" : first.IsRunning ? "checking" : "waiting";
            tip += $" - {first.AccountName}: {detail}{waiting}";
        }

        if (_needSignIn.Count > 0)
        {
            tip += " - sign-in needed";
        }

        // The tooltip is limited to 127 characters.
        _icon.Text = tip.Length <= 127 ? tip : tip[..124] + "...";
    }

    private void ShowSignInPrompt()
    {
        if (_needSignIn.Count == 0)
        {
            return;
        }

        SignInNeededForm form;
        if (_signInForm is { IsDisposed: false })
        {
            form = _signInForm;
        }
        else
        {
            form = new SignInNeededForm();
            form.SignInRequested += id => _ = SignInFromPromptAsync(id);
            form.Show();
            _signInForm = form;
        }

        foreach ((string id, string name) in _needSignIn)
        {
            form.Add(id, name);
            _lastPrompted[id] = DateTime.UtcNow;
        }

        form.Activate();
    }

    private async Task SignInFromPromptAsync(string accountId)
    {
        ShowSettings();
        if (_settingsForm is null || await _settingsForm.SignInAccountAsync(accountId))
        {
            ClearSignInNeeded(accountId);
        }
    }

    // ----- updates -----------------------------------------------------------------------------

    /// <summary>Installers from earlier updates are no longer needed once the new version is running.</summary>
    private static void DeleteOldUpdateDownloads()
    {
        try
        {
            string folder = Path.Combine(Path.GetTempPath(), "EmailAttachmentDownloader-update");
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Still in use or not ours to delete; try again next start.
        }
    }

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (_updateCheckRunning)
        {
            return;
        }

        Version? current = AppVersion.Current;
        if (current is null)
        {
            if (manual)
            {
                MessageBox.Show("This build does not have a version number, so it cannot check for updates.", "Email Attachment Downloader");
            }

            return;
        }

        _updateCheckRunning = true;
        try
        {
            UpdateCheckResult result = await _updateChecker.CheckAsync(current);
            if (!result.Succeeded)
            {
                _loggerFactory.CreateLogger("Updates").LogWarning("Update check failed: {Error}", result.Error);
                if (manual)
                {
                    MessageBox.Show("Could not check for updates. " + result.Error, "Email Attachment Downloader");
                }

                return;
            }

            if (result.Update is null)
            {
                if (manual)
                {
                    MessageBox.Show(
                        $"You have the latest version ({current.ToString(3)}).",
                        "Email Attachment Downloader",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }

                return;
            }

            // Automatic checks respect "Skip this version"; asking by hand always shows it.
            if (!manual && _updateSettings.Current.SkippedVersion == result.Update.Version.ToString(3))
            {
                return;
            }

            ShowUpdatePrompt(result.Update, current);
        }
        finally
        {
            _updateCheckRunning = false;
        }
    }

    private void ShowUpdatePrompt(ReleaseInfo release, Version current)
    {
        if (_updateForm is { IsDisposed: false })
        {
            _updateForm.Activate();
            return;
        }

        var form = new UpdateAvailableForm(release, current);
        _updateForm = form;
        var cancel = new CancellationTokenSource();
        form.FormClosed += (_, _) => cancel.Cancel();
        form.SkipRequested += () =>
        {
            try
            {
                _updateSettings.Update(u => u.SkippedVersion = release.Version.ToString(3));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _loggerFactory.CreateLogger("Updates").LogWarning(ex, "Could not remember the skipped version");
            }

            form.Close();
        };
        form.InstallRequested += () => _ = InstallUpdateAsync(form, release, cancel.Token);
        form.Show();
        form.Activate();
    }

    private async Task InstallUpdateAsync(UpdateAvailableForm form, ReleaseInfo release, CancellationToken ct)
    {
        ILogger log = _loggerFactory.CreateLogger("Updates");
        form.SetBusy(true);
        form.SetStatus("Starting download...");
        try
        {
            string folder = Path.Combine(Path.GetTempPath(), "EmailAttachmentDownloader-update", release.Version.ToString(3));
            var progress = new Progress<double>(form.SetProgress);
            string installer = await _updateDownloader.DownloadAsync(release, folder, progress, ct);

            form.SetStatus("Starting the installer...");

            // /SILENT shows only a progress window. The installer closes this app, replaces it and starts it again.
            Process.Start(new ProcessStartInfo(installer, "/SILENT /CLOSEAPPLICATIONS /RELAUNCH=1") { UseShellExecute = true });
            log.LogInformation("Started the installer for {Version}; exiting so it can replace the app.", release.Version);
            Quit();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The window was closed or "Cancel" pressed.
        }
        catch (Exception ex) when (ex is UpdateException or OperationCanceledException or HttpRequestException or IOException
                                       or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            log.LogWarning(ex, "Update to {Version} failed", release.Version);
            if (!form.IsDisposed)
            {
                form.SetBusy(false);
                form.SetStatus("Update failed: " + ex.Message);
            }
        }
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
        _signInForm?.Close();
        _updateTimer.Stop();
        _updateForm?.Close();

        try
        {
            _scheduler.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // Shutting down anyway.
        }

        _icon.Dispose();
        _updateTimer.Dispose();
        _marshal.Dispose();
        _loggerFactory.Dispose();
        ExitThread();
    }
}
