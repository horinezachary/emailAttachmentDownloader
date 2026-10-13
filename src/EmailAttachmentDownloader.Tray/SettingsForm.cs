using EmailAttachmentDownloader.Core;

namespace EmailAttachmentDownloader.Tray;

/// <summary>
/// Edit accounts. Works on private copies and only changes what the scheduler sees when Save is
/// pressed. Passwords are typed here but go straight into protected storage, never into profiles.json.
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly ProfileCatalog _catalog;
    private readonly ICredentialStore _credentials;
    private readonly MailSyncService _sync;
    private readonly SyncScheduler _scheduler;
    private readonly SyncStateStore _state;

    private readonly List<AccountProfile> _working;
    private readonly HashSet<string> _originalIds;
    private readonly Dictionary<string, string> _newPasswords = new();
    private int _current = -1;
    private bool _loading;

    private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly Button _add = new() { Text = "Add", AutoSize = true };
    private readonly Button _remove = new() { Text = "Remove", AutoSize = true };

    private readonly TextBox _name = new();
    // CheckBox does not size itself to its text unless AutoSize is set; without it the label is clipped.
    private readonly CheckBox _enabled = new() { Text = "Enabled", AutoSize = true };
    private readonly ComboBox _protocol = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _host = new();
    private readonly NumericUpDown _port = new() { Minimum = 1, Maximum = 65535, Value = 993 };
    private readonly ComboBox _tls = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _user = new();
    private readonly TextBox _password = new()
    {
        UseSystemPasswordChar = true,
        PlaceholderText = "Leave blank to keep the stored password",
    };

    private readonly TextBox _folder = new();
    private readonly Button _browse = new() { Text = "Browse...", AutoSize = true };
    private readonly TextBox _keywords = new();
    private readonly ComboBox _keywordMode = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _types = new();
    private readonly NumericUpDown _minSize = new() { Minimum = 0, Maximum = 100000, Value = 0 };
    private readonly CheckBox _runOnStartup = new() { Text = "Check mail when the app starts", AutoSize = true };
    private readonly CheckBox _scheduled = new() { Text = "Check mail on a schedule, every", AutoSize = true };
    private readonly NumericUpDown _interval = new()
    {
        Minimum = AccountProfile.MinIntervalMinutes,
        Maximum = 10080,
        Value = 60,
        Width = 80,
    };

    private readonly Button _test = new() { Text = "Test connection", AutoSize = true };
    private readonly Button _runNow = new() { Text = "Save and check now", AutoSize = true };
    private readonly Button _save = new() { Text = "Save", AutoSize = true };
    private readonly Button _close = new() { Text = "Close", AutoSize = true };
    private readonly Label _status = new() { AutoSize = true, Anchor = AnchorStyles.Left };

    private readonly ToolTip _tips = new();

    private TableLayoutPanel _grid = null!;

    public SettingsForm(
        ProfileCatalog catalog,
        ICredentialStore credentials,
        MailSyncService sync,
        SyncScheduler scheduler,
        SyncStateStore state)
    {
        _catalog = catalog;
        _credentials = credentials;
        _sync = sync;
        _scheduler = scheduler;
        _state = state;

        _working = catalog.Snapshot().ToList();
        _originalIds = _working.Select(p => p.Id).ToHashSet();

        Text = "Email Attachment Downloader - Settings";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(880, 700);
        MinimumSize = new Size(760, 560);

        _protocol.Items.AddRange(new object[] { "IMAP", "POP3" });
        _tls.Items.AddRange(new object[] { "Automatic", "SSL/TLS (implicit)", "STARTTLS" });
        _keywordMode.Items.AddRange(new object[] { "Any keyword (OR)", "Every keyword (AND)" });

        BuildLayout();
        WireEvents();

        foreach (AccountProfile profile in _working)
        {
            _list.Items.Add(profile.Name);
        }

        if (_working.Count == 0)
        {
            AddProfile();
        }
        else
        {
            _list.SelectedIndex = 0;
        }
    }

    private void BuildLayout()
    {
        // Left: account list with Add/Remove.
        var leftButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(4) };
        leftButtons.Controls.Add(_add);
        leftButtons.Controls.Add(_remove);

        var left = new Panel { Dock = DockStyle.Left, Width = 210, Padding = new Padding(8, 8, 0, 8) };
        left.Controls.Add(_list);
        left.Controls.Add(leftButtons);

        // Right: one row per setting.
        _grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Padding = new Padding(8),
        };
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow("Name", _name);
        AddRow("", _enabled);
        AddRow("Protocol", _protocol);
        AddRow("Server", _host);
        AddRow("Port", _port);
        AddRow("Security", _tls);
        AddRow("User name", _user);
        AddRow("Password", _password);

        var folderRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _folder.Dock = DockStyle.Fill;
        folderRow.Controls.Add(_folder, 0, 0);
        folderRow.Controls.Add(_browse, 1, 0);
        AddRow("Save pictures to", folderRow);

        AddRow("Subject keywords", _keywords);
        AddRow("Keyword match", _keywordMode);
        AddRow("Attachment types", _types);
        AddRow("Skip smaller than (KB)", _minSize);
        AddRow("", _runOnStartup);
        _tips.SetToolTip(_runOnStartup, "Takes effect the next time the app starts. Use \"Save and check now\" to check immediately.");
        _tips.SetToolTip(_enabled, "Turn off to pause this account without deleting it.");

        var scheduleRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        scheduleRow.Controls.Add(_scheduled);
        scheduleRow.Controls.Add(_interval);
        scheduleRow.Controls.Add(new Label { Text = "minutes", AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 0, 0) });
        AddRow("", scheduleRow);

        var hint = new Label
        {
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            MaximumSize = new Size(520, 0),
            Text = "Keywords are separated by commas and matched anywhere in the subject, ignoring case. "
                   + "Leave blank to take every e-mail. Attachment types look like image/*, application/pdf.",
        };
        AddRow("", hint);

        var rightScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        rightScroll.Controls.Add(_grid);

        // Bottom: actions.
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8),
        };
        buttons.Controls.Add(_close);
        buttons.Controls.Add(_save);
        buttons.Controls.Add(_runNow);
        buttons.Controls.Add(_test);

        var statusBar = new Panel { Dock = DockStyle.Bottom, Height = 26, Padding = new Padding(10, 4, 0, 0) };
        statusBar.Controls.Add(_status);

        // The fill-docked control goes in first so the edge-docked ones are laid out around it.
        Controls.Add(rightScroll);
        Controls.Add(left);
        Controls.Add(statusBar);
        Controls.Add(buttons);
    }

    private void AddRow(string label, Control control)
    {
        int row = _grid.RowCount++;
        _grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _grid.Controls.Add(
            new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 0, 0) }, 0, row);

        if (control is TextBox or ComboBox or TableLayoutPanel or FlowLayoutPanel)
        {
            control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        }
        else
        {
            control.Anchor = AnchorStyles.Left;
        }

        _grid.Controls.Add(control, 1, row);
    }

    private void WireEvents()
    {
        _list.SelectedIndexChanged += (_, _) => OnSelectionChanged();
        _add.Click += (_, _) => AddProfile();
        _remove.Click += (_, _) => RemoveProfile();
        _browse.Click += (_, _) => BrowseForFolder();
        _protocol.SelectedIndexChanged += (_, _) => OnProtocolChanged();
        _save.Click += (_, _) => SaveAll();
        _close.Click += (_, _) => Close();
        _runNow.Click += (_, _) => SaveAndRun();
        _test.Click += async (_, _) => await TestConnectionAsync();
        _scheduled.CheckedChanged += (_, _) => _interval.Enabled = _scheduled.Checked;
    }

    // ----- selection and editor plumbing -------------------------------------------------------

    private void OnSelectionChanged()
    {
        if (_loading)
        {
            return;
        }

        CommitEditor();
        _current = _list.SelectedIndex;
        LoadEditor();
    }

    private void AddProfile()
    {
        CommitEditor();

        var profile = new AccountProfile { SaveFolder = AccountProfile.DefaultSaveFolder() };
        _working.Add(profile);
        _list.Items.Add(profile.Name);

        _current = -1;
        _list.SelectedIndex = _working.Count - 1; // fires OnSelectionChanged, which loads the editor
    }

    private void RemoveProfile()
    {
        if (_current < 0)
        {
            return;
        }

        AccountProfile profile = _working[_current];
        if (MessageBox.Show(
                this,
                $"Remove \"{profile.Name}\"? Pictures already saved are not deleted.",
                "Remove account",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question) != DialogResult.OK)
        {
            return;
        }

        _loading = true;
        try
        {
            _newPasswords.Remove(profile.Id);
            _working.RemoveAt(_current);
            _list.Items.RemoveAt(_current);
            _current = -1;
        }
        finally
        {
            _loading = false;
        }

        if (_working.Count > 0)
        {
            _list.SelectedIndex = 0;
            if (_current < 0)
            {
                // The selection event did not fire (the index was already 0), so load the editor ourselves.
                _current = _list.SelectedIndex;
                LoadEditor();
            }
        }
        else
        {
            LoadEditor();
        }
    }

    private void LoadEditor()
    {
        bool has = _current >= 0 && _current < _working.Count;
        _grid.Enabled = has;
        _remove.Enabled = has;
        _test.Enabled = has;
        _runNow.Enabled = has;

        _loading = true;
        try
        {
            if (!has)
            {
                _name.Text = _host.Text = _user.Text = _password.Text = _folder.Text = _keywords.Text = _types.Text = "";
                return;
            }

            AccountProfile p = _working[_current];
            _name.Text = p.Name;
            _enabled.Checked = p.Enabled;
            _protocol.SelectedIndex = (int)p.Protocol;
            _host.Text = p.Host;
            _port.Value = Math.Clamp(p.Port, 1, 65535);
            _tls.SelectedIndex = (int)p.Tls;
            _user.Text = p.UserName;
            _password.Text = _newPasswords.TryGetValue(p.Id, out string? pending) ? pending : "";
            _folder.Text = p.SaveFolder;
            _keywords.Text = Keywords.Format(p.Keywords);
            _keywordMode.SelectedIndex = (int)p.KeywordMode;
            _types.Text = string.Join(", ", p.AttachmentTypes);
            _minSize.Value = Math.Clamp(p.MinSizeKb, 0, 100000);
            _runOnStartup.Checked = p.RunOnStartup;
            _scheduled.Checked = p.Scheduled;
            _interval.Value = Math.Clamp(p.IntervalMinutes, AccountProfile.MinIntervalMinutes, 10080);
            _interval.Enabled = p.Scheduled;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Copies the editor controls into the profile that is currently selected.</summary>
    private void CommitEditor()
    {
        if (_current < 0 || _current >= _working.Count)
        {
            return;
        }

        AccountProfile p = _working[_current];
        p.Name = _name.Text.Trim();
        p.Enabled = _enabled.Checked;
        p.Protocol = (MailProtocol)Math.Max(0, _protocol.SelectedIndex);
        p.Host = _host.Text.Trim();
        p.Port = (int)_port.Value;
        p.Tls = (TlsMode)Math.Max(0, _tls.SelectedIndex);
        p.UserName = _user.Text.Trim();
        p.SaveFolder = _folder.Text.Trim();
        p.Keywords = Keywords.Parse(_keywords.Text);
        p.KeywordMode = (KeywordMode)Math.Max(0, _keywordMode.SelectedIndex);
        p.AttachmentTypes = AttachmentFilter.ParsePatterns(_types.Text);
        p.MinSizeKb = (int)_minSize.Value;
        p.RunOnStartup = _runOnStartup.Checked;
        p.Scheduled = _scheduled.Checked;
        p.IntervalMinutes = (int)_interval.Value;

        if (_password.Text.Length > 0)
        {
            _newPasswords[p.Id] = _password.Text;
        }

        _loading = true;
        try
        {
            if (_current < _list.Items.Count && !Equals(_list.Items[_current], p.Name))
            {
                _list.Items[_current] = p.Name;
            }
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnProtocolChanged()
    {
        if (_loading)
        {
            return;
        }

        // Nudge the server and port to the usual values if they still hold the other protocol's defaults.
        bool imap = _protocol.SelectedIndex == (int)MailProtocol.Imap;
        int oldDefaultPort = imap ? 995 : 993;
        if ((int)_port.Value == oldDefaultPort)
        {
            _port.Value = imap ? 993 : 995;
        }

        if (_host.Text.StartsWith(imap ? "pop." : "imap.", StringComparison.OrdinalIgnoreCase))
        {
            _host.Text = (imap ? "imap." : "pop.") + _host.Text[(imap ? 4 : 5)..];
        }
    }

    private void BrowseForFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose where saved pictures should go",
            UseDescriptionForTitle = true,
            InitialDirectory = Directory.Exists(_folder.Text) ? _folder.Text : "",
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _folder.Text = dialog.SelectedPath;
        }
    }

    // ----- actions -----------------------------------------------------------------------------

    private bool SaveAll()
    {
        CommitEditor();

        for (int i = 0; i < _working.Count; i++)
        {
            IReadOnlyList<string> problems = _working[i].Validate();
            bool hasPassword = _newPasswords.ContainsKey(_working[i].Id) || _credentials.Get(_working[i].Id) is not null;
            if (!hasPassword)
            {
                problems = problems.Append("Password is required.").ToList();
            }

            if (problems.Count > 0)
            {
                _list.SelectedIndex = i;
                MessageBox.Show(
                    this,
                    $"\"{_working[i].Name}\" needs attention:\n\n- " + string.Join("\n- ", problems),
                    "Cannot save",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return false;
            }
        }

        try
        {
            foreach ((string id, string password) in _newPasswords)
            {
                _credentials.Set(id, password);
            }

            _newPasswords.Clear();
            _password.Text = "";

            // Anything that was in the saved list but is gone now has been removed.
            var currentIds = _working.Select(p => p.Id).ToHashSet();
            foreach (string removed in _originalIds.Where(id => !currentIds.Contains(id)))
            {
                _credentials.Remove(removed);
                _state.Remove(removed);
            }

            _originalIds.Clear();
            _originalIds.UnionWith(currentIds);

            _catalog.Replace(_working);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            MessageBox.Show(this, "Could not save settings: " + ex.Message, "Cannot save", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        _status.Text = "Saved.";
        return true;
    }

    private void SaveAndRun()
    {
        if (_current < 0 || !SaveAll())
        {
            return;
        }

        AccountProfile profile = _working[_current];
        _status.Text = _scheduler.RequestRun(profile.Id)
            ? $"Checking \"{profile.Name}\"... results appear as a notification and under Recent activity."
            : $"\"{profile.Name}\" is already being checked.";
    }

    private async Task TestConnectionAsync()
    {
        if (_current < 0)
        {
            return;
        }

        CommitEditor();
        AccountProfile profile = _working[_current];
        string? password = _newPasswords.TryGetValue(profile.Id, out string? typed) ? typed : _credentials.Get(profile.Id);
        if (string.IsNullOrEmpty(password))
        {
            MessageBox.Show(this, "Type the password first.", "Test connection", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SetBusy(true, "Connecting...");
        try
        {
            string message = await _sync.TestConnectionAsync(profile, password);
            MessageBox.Show(this, message, "Test connection", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Test connection failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            SetBusy(false, "");
        }
    }

    private void SetBusy(bool busy, string status)
    {
        _test.Enabled = !busy;
        _runNow.Enabled = !busy;
        _save.Enabled = !busy;
        _status.Text = status;
        UseWaitCursor = busy;
    }
}
