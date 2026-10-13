using System.Diagnostics;
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
    private readonly Dictionary<string, string> _newClientSecrets = new();
    private readonly Dictionary<string, string> _newRefreshTokens = new();
    private readonly Dictionary<string, string> _signedInAs = new();
    private readonly GoogleOAuth _googleOAuth = new();
    private CancellationTokenSource? _signInCts;
    private Control[] _passwordControls = Array.Empty<Control>();
    private Control[] _oauthControls = Array.Empty<Control>();
    private int _current = -1;
    private bool _loading;

    // Owner-drawn so a paused account can be shown in grey.
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed };
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
    private readonly ComboBox _auth = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _clientId = new();
    private readonly TextBox _clientSecret = new()
    {
        UseSystemPasswordChar = true,
        PlaceholderText = "Leave blank to keep the stored secret",
    };

    private readonly Button _signIn = new() { Text = "Sign in with Google", AutoSize = true };
    private readonly Label _signInStatus = new() { AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = SystemColors.GrayText };
    private readonly TextBox _password = new()
    {
        UseSystemPasswordChar = true,
        PlaceholderText = "Leave blank to keep the stored password",
    };

    private readonly TextBox _folder = new();
    private readonly Button _browse = new() { Text = "Browse...", AutoSize = true };
    private readonly TextBox _keywords = new();
    private readonly ComboBox _keywordMode = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _recipientFilter = new() { Text = "Only mail sent to", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly TextBox _toAddresses = new() { PlaceholderText = "you@gmail.com, you+frames@gmail.com" };
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
        Icon = AppIcon.ForWindow();
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(880, 700);
        MinimumSize = new Size(760, 560);

        _protocol.Items.AddRange(new object[] { "IMAP", "POP3" });
        _tls.Items.AddRange(new object[] { "Automatic", "SSL/TLS (implicit)", "STARTTLS" });
        _keywordMode.Items.AddRange(new object[] { "Any keyword (OR)", "Every keyword (AND)" });
        _auth.Items.AddRange(new object[] { "Password (Gmail: an app password)", "Sign in with Google (OAuth)" });

        BuildLayout();
        WireEvents();

        foreach (AccountProfile profile in _working)
        {
            _list.Items.Add(ListText(profile));
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
        AddRow("Sign-in method", _auth);
        _passwordControls = AddRow("Password", _password);
        _clientId.PlaceholderText = "From Google Cloud: a \"Desktop app\" OAuth client";
        _tips.SetToolTip(
            _clientId,
            "Create an OAuth client of type \"Desktop app\" in Google Cloud and paste its client ID here. "
            + "No redirect address needs registering. See the README, \"Sign in with Google\".");

        var signInRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        signInRow.Controls.Add(_signIn);
        signInRow.Controls.Add(_signInStatus);
        _signInStatus.Margin = new Padding(8, 8, 0, 0);

        Control[] clientIdRow = AddRow("OAuth client ID", _clientId);
        Control[] clientSecretRow = AddRow("OAuth client secret", _clientSecret);
        Control[] signInRowControls = AddRow("", signInRow);
        _oauthControls = clientIdRow.Concat(clientSecretRow).Concat(signInRowControls).ToArray();

        var folderRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _folder.Dock = DockStyle.Fill;
        folderRow.Controls.Add(_folder, 0, 0);
        folderRow.Controls.Add(_browse, 1, 0);
        AddRow("Save pictures to", folderRow);

        AddRow("Subject keywords", _keywords);
        AddRow("Keyword match", _keywordMode);

        // Checkbox in front of the address list; unticking greys the list out but keeps what was typed.
        var recipientRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        recipientRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        recipientRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _toAddresses.Dock = DockStyle.Fill;
        recipientRow.Controls.Add(_recipientFilter, 0, 0);
        recipientRow.Controls.Add(_toAddresses, 1, 0);
        AddRow("Recipient filter", recipientRow);
        _tips.SetToolTip(
            _recipientFilter,
            "Only take mail addressed (To or Cc) to one of these full addresses, separated by commas.\n"
            + "Plus-addresses such as you+frames@gmail.com must match exactly. For Gmail, dots in the name are ignored.\n"
            + "Untick to ignore the list without deleting it.");

        AddRow("Attachment types", _types);
        AddRow("Skip smaller than (KB)", _minSize);
        AddRow("", _runOnStartup);
        _tips.SetToolTip(_runOnStartup, "Takes effect the next time the app starts. Use \"Save and check now\" to check immediately.");
        _tips.SetToolTip(
            _enabled,
            "Turn off to pause this account without deleting it. A paused account is skipped by the schedule and by \"Check mail now\"; "
            + "\"Save and check now\" here still checks it.");

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
                   + "Leave blank to take every e-mail. If the recipient filter is on, mail must match the keywords "
                   + "and be sent to one of the listed addresses. Attachment types look like image/*, application/pdf.",
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

    /// <summary>Adds a labelled row and returns its two controls (label first), so a row can be hidden as a unit.</summary>
    private Control[] AddRow(string label, Control control)
    {
        int row = _grid.RowCount++;
        _grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var labelControl = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 0, 0) };
        _grid.Controls.Add(labelControl, 0, row);

        if (control is TextBox or ComboBox or TableLayoutPanel or FlowLayoutPanel)
        {
            control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        }
        else
        {
            control.Anchor = AnchorStyles.Left;
        }

        _grid.Controls.Add(control, 1, row);
        return new Control[] { labelControl, control };
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
        _auth.SelectedIndexChanged += (_, _) => ApplyAuthVisibility();
        _signIn.Click += async (_, _) => await SignInWithGoogleAsync();
        FormClosing += (_, _) => _signInCts?.Cancel();
        _recipientFilter.CheckedChanged += (_, _) => _toAddresses.Enabled = _recipientFilter.Checked;
        _list.DrawItem += DrawListItem;
        _enabled.CheckedChanged += (_, _) => OnEnabledToggled();
    }

    // ----- account list ------------------------------------------------------------------------

    private static string ListText(AccountProfile profile) => profile.Enabled ? profile.Name : profile.Name + " (disabled)";

    private void DrawListItem(object? sender, DrawItemEventArgs e)
    {
        e.DrawBackground();
        if (e.Index >= 0 && e.Index < _working.Count)
        {
            AccountProfile profile = _working[e.Index];
            bool selected = (e.State & DrawItemState.Selected) != 0;

            // Grey for a paused account, except when selected (grey on the highlight colour is unreadable).
            Color color = selected || profile.Enabled ? e.ForeColor : SystemColors.GrayText;
            TextRenderer.DrawText(
                e.Graphics,
                ListText(profile),
                e.Font ?? Font,
                e.Bounds,
                color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        e.DrawFocusRectangle();
    }

    private void OnEnabledToggled()
    {
        if (_loading || _current < 0 || _current >= _working.Count)
        {
            return;
        }

        _working[_current].Enabled = _enabled.Checked;
        RefreshListItem(_current);
    }

    private void RefreshListItem(int index)
    {
        if (index < 0 || index >= _working.Count || index >= _list.Items.Count)
        {
            return;
        }

        string text = ListText(_working[index]);
        if (Equals(_list.Items[index], text))
        {
            return;
        }

        // Replacing an item can drop the selection, so put back whatever was selected before.
        int selected = _list.SelectedIndex;
        _loading = true;
        try
        {
            _list.Items[index] = text;
            if (_list.SelectedIndex != selected)
            {
                _list.SelectedIndex = selected;
            }
        }
        finally
        {
            _loading = false;
        }
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
        _list.Items.Add(ListText(profile));

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
            _newClientSecrets.Remove(profile.Id);
            _newRefreshTokens.Remove(profile.Id);
            _signedInAs.Remove(profile.Id);
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
                _toAddresses.Text = "";
                _clientId.Text = _clientSecret.Text = "";
                UpdateSignInStatus();
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
            _auth.SelectedIndex = (int)p.Authentication;
            _clientId.Text = p.OAuthClientId;
            _clientSecret.Text = _newClientSecrets.TryGetValue(p.Id, out string? pendingSecret) ? pendingSecret : "";
            ApplyAuthVisibility();
            UpdateSignInStatus();
            _folder.Text = p.SaveFolder;
            _keywords.Text = Keywords.Format(p.Keywords);
            _keywordMode.SelectedIndex = (int)p.KeywordMode;
            _recipientFilter.Checked = p.RecipientFilterEnabled;
            _toAddresses.Text = Recipients.Format(p.ToAddresses);
            _toAddresses.Enabled = p.RecipientFilterEnabled;
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
        p.Authentication = (AuthMethod)Math.Max(0, _auth.SelectedIndex);
        p.OAuthClientId = _clientId.Text.Trim();
        p.SaveFolder = _folder.Text.Trim();
        p.Keywords = Keywords.Parse(_keywords.Text);
        p.KeywordMode = (KeywordMode)Math.Max(0, _keywordMode.SelectedIndex);
        p.RecipientFilterEnabled = _recipientFilter.Checked;
        p.ToAddresses = Recipients.Parse(_toAddresses.Text);
        p.AttachmentTypes = AttachmentFilter.ParsePatterns(_types.Text);
        p.MinSizeKb = (int)_minSize.Value;
        p.RunOnStartup = _runOnStartup.Checked;
        p.Scheduled = _scheduled.Checked;
        p.IntervalMinutes = (int)_interval.Value;

        if (_password.Text.Length > 0)
        {
            _newPasswords[p.Id] = _password.Text;
        }

        if (_clientSecret.Text.Trim().Length > 0)
        {
            _newClientSecrets[p.Id] = _clientSecret.Text.Trim();
        }

        RefreshListItem(_current);
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

    // ----- Google sign-in ----------------------------------------------------------------------

    private void ApplyAuthVisibility()
    {
        bool google = _auth.SelectedIndex == (int)AuthMethod.GoogleOAuth;
        foreach (Control control in _passwordControls)
        {
            control.Visible = !google;
        }

        foreach (Control control in _oauthControls)
        {
            control.Visible = google;
        }
    }

    private void UpdateSignInStatus()
    {
        if (_current < 0 || _current >= _working.Count)
        {
            _signInStatus.Text = "";
            return;
        }

        string id = _working[_current].Id;
        _signedInAs.TryGetValue(id, out string? email);

        if (_newRefreshTokens.ContainsKey(id))
        {
            _signInStatus.Text = "Signed in" + (email is null ? "" : " as " + email) + " (press Save to keep it)";
        }
        else if (!string.IsNullOrEmpty(_credentials.Get(OAuthKeys.RefreshToken(id))))
        {
            _signInStatus.Text = "Signed in (saved)";
        }
        else
        {
            _signInStatus.Text = "Not signed in";
        }
    }

    /// <summary>Opens Google's page in the default browser; the button turns into "Cancel sign-in" while waiting.</summary>
    private async Task SignInWithGoogleAsync()
    {
        if (_signInCts is not null)
        {
            _signInCts.Cancel();
            return;
        }

        if (_current < 0)
        {
            return;
        }

        CommitEditor();
        AccountProfile profile = _working[_current];
        string? clientSecret = _newClientSecrets.TryGetValue(profile.Id, out string? typed)
            ? typed
            : _credentials.Get(OAuthKeys.ClientSecret(profile.Id));
        if (string.IsNullOrWhiteSpace(profile.OAuthClientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            MessageBox.Show(
                this,
                "Enter the OAuth client ID and client secret first. The README explains how to create them in Google Cloud.",
                "Sign in with Google",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        string accountId = profile.Id;
        _signInCts = new CancellationTokenSource();
        _signIn.Text = "Cancel sign-in";
        _status.Text = "Finish signing in in your browser. This waits up to 5 minutes.";
        try
        {
            OAuthTokens tokens = await _googleOAuth.SignInAsync(
                profile.OAuthClientId, clientSecret, profile.UserName, OpenInBrowser, _signInCts.Token);

            _newRefreshTokens[accountId] = tokens.RefreshToken!;
            if (tokens.Email is not null)
            {
                _signedInAs[accountId] = tokens.Email;
            }

            _status.Text = "Signed in with Google. Press Save to keep it.";

            // Compare the way Gmail does (dots and case do not matter in Gmail addresses).
            if (tokens.Email is not null && Recipients.Normalize(tokens.Email) != Recipients.Normalize(profile.UserName))
            {
                MessageBox.Show(
                    this,
                    $"You signed in as {tokens.Email}, but this account's user name is {profile.UserName}. "
                    + "Gmail only accepts the sign-in if they match, so change the user name or sign in again with the right account.",
                    "Different account",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Sign-in cancelled.";
        }
        catch (OAuthException ex)
        {
            _status.Text = "";
            MessageBox.Show(this, ex.Message, "Sign in with Google", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _signInCts?.Dispose();
            _signInCts = null;
            _signIn.Text = "Sign in with Google";
            UpdateSignInStatus();
        }
    }

    private static void OpenInBrowser(string url)
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    // ----- actions -----------------------------------------------------------------------------

    private bool SaveAll()
    {
        CommitEditor();

        for (int i = 0; i < _working.Count; i++)
        {
            AccountProfile account = _working[i];
            IReadOnlyList<string> problems = account.Validate();
            if (account.Authentication == AuthMethod.GoogleOAuth)
            {
                bool hasSecret = _newClientSecrets.ContainsKey(account.Id)
                                 || !string.IsNullOrEmpty(_credentials.Get(OAuthKeys.ClientSecret(account.Id)));
                bool hasToken = _newRefreshTokens.ContainsKey(account.Id)
                                || !string.IsNullOrEmpty(_credentials.Get(OAuthKeys.RefreshToken(account.Id)));
                if (!hasSecret)
                {
                    problems = problems.Append("OAuth client secret is required.").ToList();
                }

                if (!hasToken)
                {
                    problems = problems.Append("Click \"Sign in with Google\" to authorise this account.").ToList();
                }
            }
            else
            {
                bool hasPassword = _newPasswords.ContainsKey(account.Id) || _credentials.Get(account.Id) is not null;
                if (!hasPassword)
                {
                    problems = problems.Append("Password is required.").ToList();
                }
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

            foreach ((string id, string secret) in _newClientSecrets)
            {
                _credentials.Set(OAuthKeys.ClientSecret(id), secret);
            }

            foreach ((string id, string token) in _newRefreshTokens)
            {
                _credentials.Set(OAuthKeys.RefreshToken(id), token);
            }

            _newPasswords.Clear();
            _newClientSecrets.Clear();
            _newRefreshTokens.Clear();
            _password.Text = "";
            _clientSecret.Text = "";

            // Anything that was in the saved list but is gone now has been removed.
            var currentIds = _working.Select(p => p.Id).ToHashSet();
            foreach (string removed in _originalIds.Where(id => !currentIds.Contains(id)))
            {
                _credentials.Remove(removed);
                _credentials.Remove(OAuthKeys.ClientSecret(removed));
                _credentials.Remove(OAuthKeys.RefreshToken(removed));
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

        UpdateSignInStatus();
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
        string? password = null;
        OAuthSecrets? oauth = null;

        if (profile.Authentication == AuthMethod.GoogleOAuth)
        {
            string? clientSecret = _newClientSecrets.TryGetValue(profile.Id, out string? typedSecret)
                ? typedSecret
                : _credentials.Get(OAuthKeys.ClientSecret(profile.Id));
            string? refreshToken = _newRefreshTokens.TryGetValue(profile.Id, out string? newToken)
                ? newToken
                : _credentials.Get(OAuthKeys.RefreshToken(profile.Id));
            if (string.IsNullOrEmpty(clientSecret) || string.IsNullOrEmpty(refreshToken))
            {
                MessageBox.Show(this, "Enter the client secret and click \"Sign in with Google\" first.", "Test connection", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            oauth = new OAuthSecrets(clientSecret, refreshToken);
        }
        else
        {
            password = _newPasswords.TryGetValue(profile.Id, out string? typed) ? typed : _credentials.Get(profile.Id);
            if (string.IsNullOrEmpty(password))
            {
                MessageBox.Show(this, "Type the password first.", "Test connection", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
        }

        SetBusy(true, "Connecting...");
        try
        {
            string message = await _sync.TestConnectionAsync(profile, password, oauth: oauth);
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
