using EmailAttachmentDownloader.Core;

namespace EmailAttachmentDownloader.Tray;

/// <summary>
/// What is happening and what has happened: the accounts being checked right now with live progress, above the list of
/// recent runs (the replacement for the old scrolling message page).
/// </summary>
internal sealed class HistoryForm : Form
{
    private readonly RunHistory _history;
    private readonly SyncScheduler _scheduler;
    private readonly ActivityPanel _running = new();
    private readonly ListView _list = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        GridLines = true,
        HideSelection = false,
    };

    public HistoryForm(RunHistory history, SyncScheduler scheduler)
    {
        _history = history;
        _scheduler = scheduler;

        Text = "Activity";
        Icon = AppIcon.ForWindow();
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(820, 520);
        MinimumSize = new Size(520, 320);

        _list.Columns.Add("When", 150);
        _list.Columns.Add("Account", 140);
        _list.Columns.Add("Result", 500);

        // Docking is applied from the last control added to the first: the running list takes the top, the heading goes
        // under it, and the history fills the rest.
        Controls.Add(_list);
        Controls.Add(new Label { Text = "Recent runs", Dock = DockStyle.Top, Height = 24, Padding = new Padding(10, 6, 0, 0) });
        Controls.Add(_running);

        _scheduler.ActivityChanged += OnActivityChanged;
        FormClosed += (_, _) => _scheduler.ActivityChanged -= OnActivityChanged;

        RefreshList();
        RefreshActivity();
    }

    // Raised on worker threads, so hop to this window's thread (it may already be closing).
    private void OnActivityChanged(object? sender, EventArgs e)
    {
        try
        {
            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke(RefreshActivity);
            }
        }
        catch (InvalidOperationException)
        {
            // Closed in the meantime.
        }
    }

    private void RefreshActivity() => _running.ShowActivity(_scheduler.CurrentActivity());

    public void RefreshList()
    {
        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            foreach (RunResult run in _history.Recent(200))
            {
                string outcome = run.Succeeded
                    ? $"{run.MessagesMatched} matching e-mail(s), {run.NewFiles} new, {run.Duplicates} already saved"
                      + (run.Failures > 0 ? $", {run.Failures} error(s)" : "")
                    : run.Error ?? "Failed";

                var item = new ListViewItem(run.StartedAt.LocalDateTime.ToString("g"));
                item.SubItems.Add(run.AccountName);
                item.SubItems.Add(outcome);
                if (!run.Succeeded)
                {
                    item.ForeColor = Color.Firebrick;
                }

                _list.Items.Add(item);
            }
        }
        finally
        {
            _list.EndUpdate();
        }
    }
}
