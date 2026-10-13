using EmailAttachmentDownloader.Core;

namespace EmailAttachmentDownloader.Tray;

/// <summary>
/// Offers a newer release: shows what is new and lets the user install it now, be reminded later, or skip that version.
/// While installing it turns into a progress display.
/// </summary>
internal sealed class UpdateAvailableForm : Form
{
    private const int MaxNotesLength = 2000;

    private readonly Label _headline = new() { Dock = DockStyle.Top, AutoSize = false, Height = 48 };
    private readonly TextBox _notes = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        BackColor = SystemColors.Window,
    };
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Bottom, Height = 18, Maximum = 1000, Visible = false };
    private readonly Label _status = new() { Dock = DockStyle.Bottom, AutoSize = false, Height = 22 };
    private readonly Button _install = new() { Text = "Download and install", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
    private readonly Button _later = new() { Text = "Remind me later", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
    private readonly Button _skip = new() { Text = "Skip this version", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };

    public UpdateAvailableForm(ReleaseInfo release, Version current)
    {
        Release = release;

        Text = "Email Attachment Downloader - update available";
        Icon = AppIcon.ForWindow();
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        TopMost = true;
        ClientSize = new Size(500, 360);
        Padding = new Padding(14);

        _headline.Text = $"Version {release.Version.ToString(3)} is available (you have {current.ToString(3)})."
                         + (release.IsPrerelease ? " This is a pre-release." : "")
                         + "\nInstalling closes the app and starts it again afterwards. Your settings and saved pictures are kept.";
        _notes.Text = FormatNotes(release.Notes);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(0, 10, 0, 0),
        };
        buttons.Controls.Add(_later);
        buttons.Controls.Add(_install);
        buttons.Controls.Add(_skip);

        Controls.Add(_notes);
        Controls.Add(_headline);
        Controls.Add(_progress);
        Controls.Add(_status);
        Controls.Add(buttons);

        AcceptButton = _install;
        CancelButton = _later;

        _install.Click += (_, _) => InstallRequested?.Invoke();
        _skip.Click += (_, _) => SkipRequested?.Invoke();
        _later.Click += (_, _) => Close();
    }

    public ReleaseInfo Release { get; }

    public event Action? InstallRequested;

    public event Action? SkipRequested;

    /// <summary>Disables the buttons and shows progress while the download runs; false re-enables them.</summary>
    public void SetBusy(bool busy)
    {
        _install.Enabled = !busy;
        _skip.Enabled = !busy;
        _progress.Visible = busy;
        if (busy)
        {
            _progress.Value = 0;
        }

        _later.Text = busy ? "Cancel" : "Remind me later";
    }

    public void SetStatus(string text) => _status.Text = text;

    public void SetProgress(double fraction)
    {
        _progress.Value = (int)Math.Clamp(fraction * _progress.Maximum, 0, _progress.Maximum);
        SetStatus($"Downloading... {(int)(fraction * 100)}%");
    }

    private static string FormatNotes(string notes)
    {
        string text = notes.Replace("\r\n", "\n").Trim();
        if (text.Length == 0)
        {
            return "No release notes were published.";
        }

        if (text.Length > MaxNotesLength)
        {
            text = text[..MaxNotesLength] + "...";
        }

        return text.Replace("\n", Environment.NewLine);
    }
}
