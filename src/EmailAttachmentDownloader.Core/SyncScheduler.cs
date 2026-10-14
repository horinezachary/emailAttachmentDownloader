using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EmailAttachmentDownloader.Core;

/// <summary>
/// Runs accounts one at a time, on startup, on demand and on their schedule. Between runs nothing
/// executes except a 30-second timer tick, so an idle app uses effectively no CPU (the old Java
/// version polled in a tight loop).
/// </summary>
public sealed class SyncScheduler : IAsyncDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);

    private readonly Func<IReadOnlyList<AccountProfile>> _profiles;
    private readonly MailSyncService _sync;
    private readonly RunHistory _history;
    private readonly SyncStateStore _state;
    private readonly ILogger _log;

    private readonly Channel<string> _queue = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true });

    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
    private readonly List<string> _waiting = new();
    private readonly object _gate = new();

    // What the worker is doing right now (guarded by _gate).
    private string? _runningId;
    private DateTimeOffset _runningSince;
    private SyncProgress? _runningProgress;

    private CancellationTokenSource? _cts;
    private Task? _worker;
    private Task? _ticker;

    public SyncScheduler(
        Func<IReadOnlyList<AccountProfile>> profiles,
        MailSyncService sync,
        RunHistory history,
        SyncStateStore state,
        ILogger<SyncScheduler>? logger = null)
    {
        _profiles = profiles;
        _sync = sync;
        _history = history;
        _state = state;
        _log = logger ?? (ILogger)NullLogger.Instance;
        _sync.Progress += OnProgress;
    }

    /// <summary>
    /// Raised (on whichever thread caused it) when something is queued, starts, advances or finishes. UI code must marshal to
    /// its own thread, then call <see cref="CurrentActivity"/>.
    /// </summary>
    public event EventHandler? ActivityChanged;

    /// <summary>The account being checked (first) followed by those waiting, in order.</summary>
    public IReadOnlyList<SyncActivity> CurrentActivity()
    {
        IReadOnlyList<AccountProfile> profiles = _profiles();
        string NameOf(string id) => profiles.FirstOrDefault(p => p.Id == id)?.Name ?? id;

        lock (_gate)
        {
            var list = new List<SyncActivity>();
            if (_runningId is not null)
            {
                list.Add(new SyncActivity(_runningId, NameOf(_runningId), true, _runningProgress, _runningSince));
            }

            foreach (string id in _waiting)
            {
                list.Add(new SyncActivity(id, NameOf(id), false, null, null));
            }

            return list;
        }
    }

    private void OnProgress(object? sender, SyncProgress progress)
    {
        lock (_gate)
        {
            if (_runningId != progress.AccountId)
            {
                return; // a run started outside the scheduler
            }

            _runningProgress = progress;
        }

        ActivityChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised on a worker thread after every run. UI code must marshal to its own thread.</summary>
    public event EventHandler<RunResult>? RunCompleted;

    public void Start()
    {
        if (_cts is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;
        _worker = Task.Run(() => WorkerAsync(token));
        _ticker = Task.Run(() => TickAsync(token));

        foreach (AccountProfile profile in _profiles().Where(p => p.Enabled && p.RunOnStartup))
        {
            RequestRun(profile.Id);
        }
    }

    /// <summary>Queues a run now. Returns false if that account is already queued or running.</summary>
    public bool RequestRun(string accountId)
    {
        lock (_gate)
        {
            if (!_pending.Add(accountId))
            {
                return false;
            }

            _waiting.Add(accountId);
        }

        _queue.Writer.TryWrite(accountId);
        ActivityChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void RequestRunAll()
    {
        foreach (AccountProfile profile in _profiles().Where(p => p.Enabled))
        {
            RequestRun(profile.Id);
        }
    }

    /// <summary>
    /// True when a scheduled, enabled account has not run for its interval. The interval is never
    /// shorter than <see cref="AccountProfile.MinIntervalMinutes"/>, to be kind to mail servers.
    /// </summary>
    internal static bool IsDue(AccountProfile profile, DateTimeOffset? lastRun, DateTimeOffset now)
    {
        if (!profile.Enabled || !profile.Scheduled)
        {
            return false;
        }

        if (lastRun is null)
        {
            return true;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(AccountProfile.MinIntervalMinutes, profile.IntervalMinutes));
        return now - lastRun.Value >= interval;
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TickInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                DateTimeOffset now = DateTimeOffset.Now;
                foreach (AccountProfile profile in _profiles())
                {
                    if (IsDue(profile, _state.LastRunAt(profile.Id), now))
                    {
                        RequestRun(profile.Id);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    private async Task WorkerAsync(CancellationToken ct)
    {
        try
        {
            await foreach (string accountId in _queue.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                lock (_gate)
                {
                    _waiting.Remove(accountId);
                    _runningId = accountId;
                    _runningSince = DateTimeOffset.Now;
                    _runningProgress = null;
                }

                ActivityChanged?.Invoke(this, EventArgs.Empty);

                try
                {
                    AccountProfile? profile = _profiles().FirstOrDefault(p => p.Id == accountId);
                    if (profile is not null)
                    {
                        RunResult result = await _sync.RunAsync(profile, ct).ConfigureAwait(false);
                        _history.Append(result);
                        RunCompleted?.Invoke(this, result);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // One bad run must not stop the loop.
                    _log.LogError(ex, "Unexpected error while running account {AccountId}", accountId);
                }
                finally
                {
                    lock (_gate)
                    {
                        _pending.Remove(accountId);
                        _runningId = null;
                        _runningProgress = null;
                    }

                    ActivityChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    public async ValueTask DisposeAsync()
    {
        CancellationTokenSource? cts = _cts;
        if (cts is null)
        {
            return;
        }

        _cts = null;
        await cts.CancelAsync().ConfigureAwait(false);
        _queue.Writer.TryComplete();

        foreach (Task? task in new[] { _worker, _ticker })
        {
            if (task is null)
            {
                continue;
            }

            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _sync.Progress -= OnProgress;
        cts.Dispose();
    }
}
