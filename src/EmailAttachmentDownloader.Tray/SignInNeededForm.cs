namespace EmailAttachmentDownloader.Tray;

/// <summary>
/// The pop-up shown when a Google sign-in has stopped working and only the user can fix it. It stays on top
/// of other windows so it is not missed, lists every account that needs attention, and can be dismissed
/// ("Remind me later") without losing track of them.
/// </summary>
internal sealed class SignInNeededForm : Form
{
    private readonly ListBox _accounts = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly Label _message = new()
    {
        Dock = DockStyle.Top,
        AutoSize = false,
        Height = 72,
        Padding = new Padding(0, 0, 0, 8),
    };
    private readonly Button _signIn = new() { Text = "Sign in with Google", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
    private readonly Button _later = new() { Text = "Remind me later", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };

    private readonly List<(string Id, string Name)> _items = new();

    public SignInNeededForm()
    {
        Text = "Email Attachment Downloader - sign-in needed";
        Icon = AppIcon.ForWindow();
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        TopMost = true;
        ShowInTaskbar = true;
        ClientSize = new Size(440, 230);
        Padding = new Padding(14);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(0, 10, 0, 0),
        };
        buttons.Controls.Add(_later);
        buttons.Controls.Add(_signIn);

        Controls.Add(_accounts);
        Controls.Add(buttons);
        Controls.Add(_message);

        AcceptButton = _signIn;
        CancelButton = _later;

        _signIn.Click += (_, _) =>
        {
            if (_accounts.SelectedIndex >= 0)
            {
                SignInRequested?.Invoke(_items[_accounts.SelectedIndex].Id);
            }
        };
        _later.Click += (_, _) => Close();
    }

    /// <summary>Raised with the account id when the user chooses to sign in.</summary>
    public event Action<string>? SignInRequested;

    public int Count => _items.Count;

    public void Add(string id, string name)
    {
        if (_items.Any(i => i.Id == id))
        {
            return;
        }

        _items.Add((id, name));
        _accounts.Items.Add(name);
        if (_accounts.SelectedIndex < 0)
        {
            _accounts.SelectedIndex = 0;
        }

        RefreshMessage();
    }

    public void Remove(string id)
    {
        int index = _items.FindIndex(i => i.Id == id);
        if (index < 0)
        {
            return;
        }

        _items.RemoveAt(index);
        _accounts.Items.RemoveAt(index);
        if (_items.Count == 0)
        {
            Close();
            return;
        }

        if (_accounts.SelectedIndex < 0)
        {
            _accounts.SelectedIndex = 0;
        }

        RefreshMessage();
    }

    private void RefreshMessage()
    {
        _message.Text = _items.Count == 1
            ? "The saved Google sign-in for this account is missing or no longer accepted, so no new mail is being downloaded. "
              + "Sign in with Google again to start downloading again."
            : "The saved Google sign-in for these accounts is missing or no longer accepted, so no new mail is being downloaded. "
              + "Choose an account and sign in with Google again.";
    }
}
