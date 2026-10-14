namespace EmailAttachmentDownloader.Core;

public enum SyncPhase
{
    SigningIn,
    Connecting,

    /// <summary>Reading the list of messages (the slow part of a first run on a big mailbox).</summary>
    Listing,

    /// <summary>Looking at each new message's subject and recipients.</summary>
    Checking,

    /// <summary>Downloading a message that matched, to save its attachments.</summary>
    Downloading,
}

/// <summary>Where one running account's check has got to.</summary>
/// <param name="Done">Messages looked at so far (while Listing: messages read so far).</param>
/// <param name="Total">How many there will be, or 0 when that is not known yet.</param>
public sealed record SyncProgress(
    string AccountId, string AccountName, SyncPhase Phase, int Done, int Total, int Matched, int NewFiles)
{
    /// <summary>0 to 1, or null when the total is not known (show a busy indicator instead).</summary>
    public double? Fraction => Total > 0 ? Math.Clamp((double)Done / Total, 0, 1) : null;

    public string Describe()
    {
        string counts = Matched > 0 || NewFiles > 0 ? $" ({Matched:N0} matching, {NewFiles:N0} new picture(s))" : "";
        return Phase switch
        {
            SyncPhase.SigningIn => "Signing in...",
            SyncPhase.Connecting => "Connecting to the mail server...",
            SyncPhase.Listing when Total > 0 => $"Reading the mailbox: {Done:N0} of {Total:N0} e-mails",
            SyncPhase.Listing when Done > 0 => $"Reading the mailbox: {Done:N0} e-mails so far",
            SyncPhase.Listing => "Reading the mailbox...",
            SyncPhase.Downloading => $"Downloading a matching e-mail: {Done:N0} of {Total:N0} checked" + counts,
            _ => $"Checking e-mails: {Done:N0} of {Total:N0}" + counts,
        };
    }
}

/// <summary>One entry in the "what is happening now" list: the account being checked, or one waiting its turn.</summary>
public sealed record SyncActivity(string AccountId, string AccountName, bool IsRunning, SyncProgress? Progress, DateTimeOffset? StartedAt)
{
    public string Describe() => IsRunning
        ? Progress?.Describe() ?? "Starting..."
        : "Waiting for its turn";
}

/// <summary>
/// Lets a progress report through when the phase changes or enough time has passed, so a mailbox of 50,000 messages
/// produces a few reports a second instead of 50,000.
/// </summary>
internal sealed class ProgressThrottle
{
    private readonly long _minIntervalMs;
    private SyncPhase? _lastPhase;
    private long _lastReportMs = long.MinValue;

    public ProgressThrottle(long minIntervalMs = 250)
    {
        _minIntervalMs = minIntervalMs;
    }

    public bool ShouldReport(SyncPhase phase, long nowMs)
    {
        if (_lastPhase == phase && nowMs - _lastReportMs < _minIntervalMs)
        {
            return false;
        }

        _lastPhase = phase;
        _lastReportMs = nowMs;
        return true;
    }
}
