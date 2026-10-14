using EmailAttachmentDownloader.Core;

namespace EmailAttachmentDownloader.Tray;

/// <summary>
/// The "running now" list: one row per account being checked or waiting, each with a status line and a progress bar
/// that fills as messages are examined (or just animates while the total is not known yet).
/// </summary>
internal sealed class ActivityPanel : GroupBox
{
    private const int BarMaximum = 1000;

    private readonly TableLayoutPanel _table = new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        ColumnCount = 1,
    };

    private readonly Label _idle = new()
    {
        Text = "Nothing is running right now.",
        AutoSize = true,
        ForeColor = SystemColors.GrayText,
        Margin = new Padding(0, 4, 0, 4),
    };

    private readonly Dictionary<string, Row> _rows = new();
    private List<string> _order = new();

    public ActivityPanel()
    {
        Text = "Running now";
        Dock = DockStyle.Top;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(10, 6, 10, 8);

        _table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(_table);
        _table.Controls.Add(_idle);
    }

    public void ShowActivity(IReadOnlyList<SyncActivity> activity)
    {
        List<string> order = activity.Select(a => a.AccountId).ToList();
        bool changedSet = !order.SequenceEqual(_order);

        if (changedSet)
        {
            _table.SuspendLayout();
            _table.Controls.Clear();
            _table.RowStyles.Clear();
            _table.RowCount = Math.Max(1, activity.Count);

            foreach (string gone in _rows.Keys.Except(order).ToList())
            {
                _rows[gone].Dispose();
                _rows.Remove(gone);
            }

            if (activity.Count == 0)
            {
                _table.Controls.Add(_idle);
            }

            foreach (SyncActivity a in activity)
            {
                if (!_rows.TryGetValue(a.AccountId, out Row? row))
                {
                    row = new Row();
                    _rows[a.AccountId] = row;
                }

                _table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _table.Controls.Add(row.Container);
            }

            _order = order;
            _table.ResumeLayout(performLayout: true);
        }

        foreach (SyncActivity a in activity)
        {
            _rows[a.AccountId].Show(a);
        }
    }

    private sealed class Row : IDisposable
    {
        private readonly Label _label = new() { Dock = DockStyle.Top, AutoSize = false, Height = 20, AutoEllipsis = true };
        private readonly ProgressBar _bar = new() { Dock = DockStyle.Top, Height = 14, Maximum = BarMaximum };

        public Row()
        {
            Container = new Panel { Dock = DockStyle.Fill, Height = 44, Margin = new Padding(0, 0, 0, 6) };
            Container.Controls.Add(_bar);
            Container.Controls.Add(_label);
        }

        public Panel Container { get; }

        public void Show(SyncActivity activity)
        {
            _label.Text = $"{activity.AccountName}: {activity.Describe()}";

            double? fraction = activity.Progress?.Fraction;
            if (!activity.IsRunning)
            {
                SetStyle(ProgressBarStyle.Continuous);
                _bar.Value = 0;
            }
            else if (fraction is { } f)
            {
                SetStyle(ProgressBarStyle.Continuous);
                _bar.Value = (int)Math.Clamp(f * BarMaximum, 0, BarMaximum);
            }
            else
            {
                SetStyle(ProgressBarStyle.Marquee);
            }
        }

        public void Dispose() => Container.Dispose();

        private void SetStyle(ProgressBarStyle style)
        {
            if (_bar.Style != style)
            {
                _bar.Style = style;
                if (style == ProgressBarStyle.Marquee)
                {
                    _bar.MarqueeAnimationSpeed = 30;
                }
            }
        }
    }
}
