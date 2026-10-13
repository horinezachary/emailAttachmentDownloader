using System.Diagnostics;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Pop3;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;

namespace EmailAttachmentDownloader.Core;

/// <summary>Stops two processes (for example the tray app and a scheduled <c>ead run</c>) syncing at once.</summary>
internal sealed class ProcessLock : IDisposable
{
    private readonly FileStream _stream;

    private ProcessLock(FileStream stream)
    {
        _stream = stream;
    }

    public static ProcessLock? TryAcquire(string path)
    {
        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            return new ProcessLock(new FileStream(
                path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, bufferSize: 1, FileOptions.DeleteOnClose));
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Dispose() => _stream.Dispose();
}

internal sealed class RunCounts
{
    public int Checked;
    public int Matched;
    public int NewFiles;
    public int Duplicates;
    public int Failures;
}

/// <summary>
/// Runs one sync of one account: connect, find new mail whose subject matches, save the attachments,
/// remember where it got to. Only mail that arrived since the last successful run is examined.
/// </summary>
public sealed class MailSyncService
{
    private const int TimeoutMilliseconds = 60_000;

    private readonly ImageLibrary _library;
    private readonly SyncStateStore _state;
    private readonly ICredentialStore _credentials;
    private readonly string _lockPath;
    private readonly ILogger _log;
    private readonly AttachmentSaver _saver;

    public MailSyncService(
        ImageLibrary library,
        SyncStateStore state,
        ICredentialStore credentials,
        string lockFilePath,
        ILogger<MailSyncService>? logger = null)
    {
        _library = library;
        _state = state;
        _credentials = credentials;
        _lockPath = lockFilePath;
        _log = logger ?? (ILogger)NullLogger.Instance;
        _saver = new AttachmentSaver(library, _log);
    }

    /// <summary>Raised (on the worker thread) for every new file saved.</summary>
    public event EventHandler<AttachmentSavedEventArgs>? AttachmentSaved;

    public async Task<RunResult> RunAsync(AccountProfile profile, CancellationToken cancellationToken = default)
    {
        DateTimeOffset started = DateTimeOffset.Now;
        var timer = Stopwatch.StartNew();
        var counts = new RunCounts();

        RunResult Finish(string? failure) => new(
            profile.Id, profile.Name, started, timer.Elapsed,
            counts.Checked, counts.Matched, counts.NewFiles, counts.Duplicates, counts.Failures,
            failure, profile.SaveFolder);

        IReadOnlyList<string> problems = profile.Validate();
        if (problems.Count > 0)
        {
            return Finish(string.Join(" ", problems));
        }

        using ProcessLock? processLock = ProcessLock.TryAcquire(_lockPath);
        if (processLock is null)
        {
            return Finish("Skipped: another sync is already running.");
        }

        string? error = null;
        bool cancelled = false;
        AccountSyncState? state = null;

        try
        {
            string? password = _credentials.Get(profile.Id);
            if (string.IsNullOrEmpty(password))
            {
                throw new InvalidOperationException("No password is stored for this account. Open Settings and enter it.");
            }

            // Another process may have saved files since we last looked.
            _library.Reload();
            _state.Reload();

            state = _state.Snapshot(profile.Id);
            string signature = FilterSignature.Compute(profile);
            if (state.FilterSignature != signature)
            {
                _log.LogInformation("Settings for {Account} changed; rescanning the mailbox.", profile.Name);
                state = new AccountSyncState { FilterSignature = signature };
            }

            Directory.CreateDirectory(profile.SaveFolder);

            if (profile.Protocol == MailProtocol.Imap)
            {
                await RunImapAsync(profile, password, state, counts, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await RunPop3Async(profile, password, state, counts, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            throw;
        }
        catch (Exception ex)
        {
            error = Describe(ex);
            _log.LogError(ex, "Sync failed for {Account}", profile.Name);
        }
        finally
        {
            _library.Flush();
            if (!cancelled && state is not null)
            {
                state.LastRunAt = started;
                _state.Put(profile.Id, state);
            }
        }

        RunResult result = Finish(error);
        _log.LogInformation("{Summary}", result.Summary);
        return result;
    }

    /// <summary>Connects and signs in without downloading anything. Throws with a readable message on failure.</summary>
    public async Task<string> TestConnectionAsync(AccountProfile profile, string password, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> problems = profile.ValidateConnection();
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", problems));
        }

        try
        {
            if (profile.Protocol == MailProtocol.Imap)
            {
                using var client = new ImapClient { Timeout = TimeoutMilliseconds };
                await ConnectAsync(client, profile, password, cancellationToken).ConfigureAwait(false);
                IMailFolder inbox = client.Inbox;
                await inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken).ConfigureAwait(false);
                int count = inbox.Count;
                await client.DisconnectAsync(true, cancellationToken).ConfigureAwait(false);
                return $"Connected to {profile.Host}. The inbox has {count} message(s).";
            }
            else
            {
                using var client = new Pop3Client { Timeout = TimeoutMilliseconds };
                await ConnectAsync(client, profile, password, cancellationToken).ConfigureAwait(false);
                int count = await client.GetMessageCountAsync(cancellationToken).ConfigureAwait(false);
                await client.DisconnectAsync(true, cancellationToken).ConfigureAwait(false);
                return $"Connected to {profile.Host}. The mailbox has {count} message(s).";
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(Describe(ex), ex);
        }
    }

    private async Task RunImapAsync(
        AccountProfile profile, string password, AccountSyncState state, RunCounts counts, CancellationToken ct)
    {
        using var client = new ImapClient { Timeout = TimeoutMilliseconds };
        await ConnectAsync(client, profile, password, ct).ConfigureAwait(false);

        IMailFolder inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadOnly, ct).ConfigureAwait(false);

        // UIDs are only meaningful within one UIDVALIDITY epoch; a new epoch means start over.
        if (state.ImapUidValidity != inbox.UidValidity)
        {
            state.ImapUidValidity = inbox.UidValidity;
            state.ImapLastUid = 0;
        }

        uint lastUid = state.ImapLastUid;
        uint maxSeen = lastUid;

        // Fetch only subjects of mail newer than the cursor. Matching is done here, not with a server
        // SEARCH, so every provider behaves the same (Gmail's SUBJECT search matches whole words only).
        var range = new UniqueIdRange(new UniqueId(lastUid + 1), UniqueId.MaxValue);
        IList<IMessageSummary> summaries = await inbox
            .FetchAsync(range, MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope, ct)
            .ConfigureAwait(false);

        // "N:*" always returns the newest message even when it is older than N, so filter explicitly.
        foreach (IMessageSummary summary in summaries.Where(s => s.UniqueId.Id > lastUid).OrderBy(s => s.UniqueId.Id))
        {
            ct.ThrowIfCancellationRequested();
            counts.Checked++;
            maxSeen = Math.Max(maxSeen, summary.UniqueId.Id);

            if (!Keywords.IsMatch(summary.Envelope?.Subject, profile.Keywords, profile.KeywordMode))
            {
                continue;
            }

            // The envelope already carries To and Cc, so this needs no extra download.
            if (!Recipients.Accepts(profile, Recipients.From(summary.Envelope?.To, summary.Envelope?.Cc)))
            {
                continue;
            }

            counts.Matched++;
            try
            {
                MimeMessage message = await inbox.GetMessageAsync(summary.UniqueId, ct).ConfigureAwait(false);
                Record(profile, _saver.SaveFrom(profile, message), counts);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                counts.Failures++;
                _log.LogError(ex, "Could not download message {Uid} for {Account}", summary.UniqueId, profile.Name);
            }
        }

        if (inbox.UidNext is { } next && next.Id > 0)
        {
            maxSeen = Math.Max(maxSeen, next.Id - 1);
        }

        // Only move the cursor when everything succeeded; hashes make a retry harmless.
        if (counts.Failures == 0)
        {
            state.ImapLastUid = maxSeen;
        }

        await client.DisconnectAsync(true, ct).ConfigureAwait(false);
    }

    private async Task RunPop3Async(
        AccountProfile profile, string password, AccountSyncState state, RunCounts counts, CancellationToken ct)
    {
        using var client = new Pop3Client { Timeout = TimeoutMilliseconds };
        await ConnectAsync(client, profile, password, ct).ConfigureAwait(false);

        IList<string> uids = await client.GetMessageUidsAsync(ct).ConfigureAwait(false);
        bool canReadHeadersOnly = client.Capabilities.HasFlag(Pop3Capabilities.Top);

        var seen = new HashSet<string>(state.Pop3SeenUids, StringComparer.Ordinal);
        var onServer = new HashSet<string>(uids, StringComparer.Ordinal);

        for (int index = 0; index < uids.Count; index++)
        {
            ct.ThrowIfCancellationRequested();

            string uid = uids[index];
            if (seen.Contains(uid))
            {
                continue;
            }

            counts.Checked++;
            bool handled = true;
            try
            {
                MimeMessage? message = null;
                string? subject;
                List<string> recipients;
                if (canReadHeadersOnly)
                {
                    HeaderList headers = await client.GetMessageHeadersAsync(index, ct).ConfigureAwait(false);
                    subject = headers[HeaderId.Subject];
                    recipients = Recipients.FromHeaders(headers[HeaderId.To], headers[HeaderId.Cc]);
                }
                else
                {
                    message = await client.GetMessageAsync(index, ct).ConfigureAwait(false);
                    subject = message.Subject;
                    recipients = Recipients.From(message.To, message.Cc);
                }

                if (Keywords.IsMatch(subject, profile.Keywords, profile.KeywordMode)
                    && Recipients.Accepts(profile, recipients))
                {
                    counts.Matched++;
                    message ??= await client.GetMessageAsync(index, ct).ConfigureAwait(false);
                    Record(profile, _saver.SaveFrom(profile, message), counts);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                handled = false;
                counts.Failures++;
                _log.LogError(ex, "Could not process POP3 message {Uid} for {Account}", uid, profile.Name);
            }

            if (handled)
            {
                seen.Add(uid);
            }
        }

        // Forget UIDLs that have left the server so the list cannot grow forever.
        state.Pop3SeenUids = seen.Where(onServer.Contains).ToList();

        await client.DisconnectAsync(true, ct).ConfigureAwait(false);
    }

    private void Record(AccountProfile profile, SaveOutcome outcome, RunCounts counts)
    {
        counts.NewFiles += outcome.Saved.Count;
        counts.Duplicates += outcome.Duplicates;
        counts.Failures += outcome.Failures;
        _library.Flush();

        foreach (LibraryImage image in outcome.Saved)
        {
            AttachmentSaved?.Invoke(this, new AttachmentSavedEventArgs(profile, image));
        }
    }

    private static async Task ConnectAsync(MailService client, AccountProfile profile, string password, CancellationToken ct)
    {
        await client.ConnectAsync(profile.Host.Trim(), profile.Port, ToSocketOptions(profile.Tls), ct).ConfigureAwait(false);
        await client.AuthenticateAsync(profile.UserName, password, ct).ConfigureAwait(false);
    }

    private static SecureSocketOptions ToSocketOptions(TlsMode mode) => mode switch
    {
        TlsMode.Implicit => SecureSocketOptions.SslOnConnect,
        TlsMode.StartTls => SecureSocketOptions.StartTls,
        _ => SecureSocketOptions.Auto,
    };

    private static string Describe(Exception ex) => ex switch
    {
        MailKit.Security.AuthenticationException =>
            "Sign-in was rejected. Check the user name and password (Gmail needs an app password).",
        SslHandshakeException => "Could not establish a secure connection: " + ex.Message,
        _ => ex.Message,
    };
}
