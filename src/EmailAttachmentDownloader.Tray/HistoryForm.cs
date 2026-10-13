using EmailAttachmentDownloader.Core;

namespace EmailAttachmentDownloader.Tray;

/// <summary>Read-only list of recent runs (the replacement for the old scrolling message page).</summary>
internal sealed class HistoryForm : Form
{
    private readonly RunHistory _history;
    private readonly ListView _list = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        GridLines = true,
        HideSelection = false,
    };

    public HistoryForm(RunHistory history)
    {
        _history = history;

        Text = "Recent activity";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(820, 420);
        MinimumSize = new Size(520, 240);

        _list.Columns.Add("When", 150);
        _list.Columns.Add("Account", 140);
        _list.Columns.Add("Result", 500);
        Controls.Add(_list);

        RefreshList();
    }

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
