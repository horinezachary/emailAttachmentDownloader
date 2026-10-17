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

/// <summary>Turns "where is this run now" into throttled <see cref="SyncProgress"/> reports.</summary>
internal sealed class ProgressTracker
{
    private readonly Action<SyncProgress> _publish;
    private readonly AccountProfile _profile;
    private readonly RunCounts _counts;
    private readonly ProgressThrottle _throttle = new();
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

    public ProgressTracker(Action<SyncProgress> publish, AccountProfile profile, RunCounts counts)
    {
        _publish = publish;
        _profile = profile;
        _counts = counts;
    }

    /// <summary>Reports a step the user should see straight away.</summary>
    public void Phase(SyncPhase phase, int done = 0, int total = 0) => Publish(phase, done, total);

    /// <summary>Reports progress through a long loop, at most a few times a second.</summary>
    public void Tick(SyncPhase phase, int done, int total)
    {
        if (_throttle.ShouldReport(phase, _clock.ElapsedMilliseconds))
        {
            Publish(phase, done, total);
        }
    }

    private void Publish(SyncPhase phase, int done, int total) =>
        _publish(new SyncProgress(_profile.Id, _profile.Name, phase, done, total, _counts.Matched, _counts.NewFiles));
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
    private readonly GoogleOAuth _oauth;
    private readonly GoogleClientCredentials? _builtInClient;

    public MailSyncService(
        ImageLibrary library,
        SyncStateStore state,
        ICredentialStore credentials,
        string lockFilePath,
        ILogger<MailSyncService>? logger = null,
        GoogleOAuth? oauth = null,
        GoogleClientCredentials? builtInClient = null)
    {
        _oauth = oauth ?? new GoogleOAuth();
        _builtInClient = builtInClient ?? GoogleClient.BuiltIn;
        _library = library;
        _state = state;
        _credentials = credentials;
        _lockPath = lockFilePath;
        _log = logger ?? (ILogger)NullLogger.Instance;
        _saver = new AttachmentSaver(library, _log);
    }

    /// <summary>Raised (on the worker thread) for every new file saved.</summary>
    public event EventHandler<AttachmentSavedEventArgs>? AttachmentSaved;

    /// <summary>Raised (on the worker thread, a few times a second at most) while an account is being checked.</summary>
    public event EventHandler<SyncProgress>? Progress;

    private void Publish(SyncProgress progress) => Progress?.Invoke(this, progress);

    public async Task<RunResult> RunAsync(AccountProfile profile, CancellationToken cancellationToken = default)
    {
        DateTimeOffset started = DateTimeOffset.Now;
        var timer = Stopwatch.StartNew();
        var counts = new RunCounts();

        bool needsSignIn = false;

        RunResult Finish(string? failure) => new(
            profile.Id, profile.Name, started, timer.Elapsed,
            counts.Checked, counts.Matched, counts.NewFiles, counts.Duplicates, counts.Failures,
            failure, profile.SaveFolder, needsSignIn);

        IReadOnlyList<string> problems = profile.Validate(_builtInClient);
        if (problems.Count > 0)
        {
            // Count it as an attempt, or a scheduled account with a bad setting would be re-run every 30 seconds.
            _state.RecordRun(profile.Id, started);
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

        var tracker = new ProgressTracker(Publish, profile, counts);

        try
        {
            tracker.Phase(profile.Authentication == AuthMethod.GoogleOAuth ? SyncPhase.SigningIn : SyncPhase.Connecting);
            MailAuth auth = await ResolveAuthAsync(profile, null, null, cancellationToken).ConfigureAwait(false);

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
                await RunImapAsync(profile, auth, state, counts, tracker, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await RunPop3Async(profile, auth, state, counts, tracker, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            throw;
        }
        catch (Exception ex)
        {
            error = Describe(ex, profile);
            needsSignIn = NeedsGoogleSignIn(ex, profile);
            _log.LogError(ex, "Sync failed for {Account}", profile.Name);
        }
        finally
        {
            _library.Flush();
            if (!cancelled)
            {
                if (state is not null)
                {
                    state.LastRunAt = started;
                    _state.Put(profile.Id, state);
                }
                else
                {
                    // Failed before the mailbox was reached (sign-in, password): still wait the interval before trying again.
                    _state.RecordRun(profile.Id, started);
                }
            }
        }

        RunResult result = Finish(error);
        _log.LogInformation("{Summary}", result.Summary);
        return result;
    }

    /// <summary>Connects and signs in without downloading anything. Throws with a readable message on failure.</summary>
    /// <param name="password">Used for <see cref="AuthMethod.Password"/> accounts.</param>
    /// <param name="oauth">Secrets not saved yet; when null, the stored ones are used (Google sign-in accounts).</param>
    public async Task<string> TestConnectionAsync(
        AccountProfile profile, string? password, CancellationToken cancellationToken = default, OAuthSecrets? oauth = null)
    {
        IReadOnlyList<string> problems = profile.ValidateConnection(_builtInClient);
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", problems));
        }

        try
        {
            MailAuth auth = await ResolveAuthAsync(profile, password, oauth, cancellationToken).ConfigureAwait(false);
            if (profile.Protocol == MailProtocol.Imap)
            {
                using var client = new ImapClient { Timeout = TimeoutMilliseconds };
                await ConnectAsync(client, profile, auth, cancellationToken).ConfigureAwait(false);
                IMailFolder inbox = client.Inbox;
                await inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken).ConfigureAwait(false);
                int count = inbox.Count;
                await client.DisconnectAsync(true, cancellationToken).ConfigureAwait(false);
                return $"Connected to {profile.Host}. The inbox has {count} message(s).";
            }
            else
            {
                using var client = new Pop3Client { Timeout = TimeoutMilliseconds };
                await ConnectAsync(client, profile, auth, cancellationToken).ConfigureAwait(false);
                int count = await client.GetMessageCountAsync(cancellationToken).ConfigureAwait(false);
                await client.DisconnectAsync(true, cancellationToken).ConfigureAwait(false);
                return $"Connected to {profile.Host}. The mailbox has {count} message(s).";
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(Describe(ex, profile), ex);
        }
    }

    /// <summary>How many times one run may reconnect after the server drops the connection.</summary>
    private const int MaxReconnects = 3;

    private async Task RunImapAsync(
        AccountProfile profile, MailAuth auth, AccountSyncState state, RunCounts counts, ProgressTracker tracker, CancellationToken ct)
    {
        var clients = new List<ImapClient>();
        ImapClient client = null!;
        IMailFolder inbox = null!;
        int reconnects = 0;
        uint maxSeen = state.ImapLastUid;

        async Task OpenAsync()
        {
            client = new ImapClient { Timeout = TimeoutMilliseconds };
            clients.Add(client);
            await ConnectAsync(client, profile, auth, ct).ConfigureAwait(false);
            inbox = client.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadOnly, ct).ConfigureAwait(false);
        }

        try
        {
            tracker.Phase(SyncPhase.Connecting);
            await OpenAsync().ConfigureAwait(false);

            // UIDs are only meaningful within one UIDVALIDITY epoch; a new epoch means start over.
            if (state.ImapUidValidity != inbox.UidValidity)
            {
                state.ImapUidValidity = inbox.UidValidity;
                state.ImapLastUid = 0;
                state.ImapRetry.Clear();
            }

            uint lastUid = state.ImapLastUid;
            maxSeen = lastUid;
            const MessageSummaryItems Items = MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope;

            // Fetch only subjects of mail newer than the cursor. Matching is done here, not with a server
            // SEARCH, so every provider behaves the same (Gmail's SUBJECT search matches whole words only).
            // On a first run the whole mailbox is read, so its size is the total; later runs only read what is new.
            int listingTotal = lastUid == 0 ? inbox.Count : 0;
            int read = 0;
            tracker.Phase(SyncPhase.Listing, 0, listingTotal);

            void OnSummaryFetched(object? sender, MessageSummaryFetchedEventArgs e) =>
                tracker.Tick(SyncPhase.Listing, Interlocked.Increment(ref read), listingTotal);

            var range = new UniqueIdRange(new UniqueId(lastUid + 1), UniqueId.MaxValue);
            IList<IMessageSummary> summaries;
            List<IMessageSummary> retries = new();
            inbox.MessageSummaryFetched += OnSummaryFetched;
            try
            {
                summaries = await inbox.FetchAsync(range, Items, ct).ConfigureAwait(false);

                // Messages that failed on an earlier run sit below the cursor, so nothing else would look at them again.
                if (state.ImapRetry.Count > 0)
                {
                    var ids = state.ImapRetry.Keys.OrderBy(id => id).Select(id => new UniqueId(id)).ToList();
                    IList<IMessageSummary> found = await inbox.FetchAsync(ids, Items, ct).ConfigureAwait(false);
                    retries = found.OrderBy(s => s.UniqueId.Id).ToList();

                    // Gone from the server (deleted), so there is nothing left to retry.
                    foreach (uint gone in state.ImapRetry.Keys.Where(id => retries.All(s => s.UniqueId.Id != id)).ToList())
                    {
                        state.ImapRetry.Remove(gone);
                    }
                }
            }
            finally
            {
                inbox.MessageSummaryFetched -= OnSummaryFetched;
            }

            // "N:*" always returns the newest message even when it is older than N, so filter explicitly.
            List<IMessageSummary> fresh = summaries.Where(s => s.UniqueId.Id > lastUid).OrderBy(s => s.UniqueId.Id).ToList();
            int total = retries.Count + fresh.Count;
            _log.LogInformation(
                "{Account}: {New} new message(s) after UID {Cursor}, {Retry} earlier failure(s) to retry.",
                profile.Name, fresh.Count, lastUid, retries.Count);

            // The server dropped us (a timeout, a reset, or a message it chokes on). Reconnect and carry on with the
            // next message rather than failing every message that is left or starting the whole check over.
            async Task ReconnectAsync(Exception cause)
            {
                if (++reconnects > MaxReconnects)
                {
                    throw new InvalidOperationException("The connection to the server keeps dropping.", cause);
                }

                _log.LogWarning(cause, "{Account}: connection lost; reconnecting ({Attempt} of {Max})", profile.Name, reconnects, MaxReconnects);
                await OpenAsync().ConfigureAwait(false);
                if (inbox.UidValidity != state.ImapUidValidity)
                {
                    throw new InvalidOperationException("The mailbox was reset while it was being checked; it will be rescanned on the next check.", cause);
                }
            }

            async Task HandleAsync(IMessageSummary summary)
            {
                uint uid = summary.UniqueId.Id;
                counts.Checked++;
                tracker.Tick(SyncPhase.Checking, counts.Checked, total);

                // The envelope already carries To and Cc, so recipients need no extra download.
                if (!SubjectMatcher.IsMatch(profile, summary.Envelope?.Subject)
                    || !Recipients.Accepts(profile, Recipients.From(summary.Envelope?.To, summary.Envelope?.Cc)))
                {
                    state.ImapRetry.Remove(uid);
                    return;
                }

                counts.Matched++;
                tracker.Tick(SyncPhase.Downloading, counts.Checked, total);
                try
                {
                    MimeMessage message = await inbox.GetMessageAsync(summary.UniqueId, ct).ConfigureAwait(false);
                    SaveOutcome outcome = _saver.SaveFrom(profile, message);
                    Record(profile, outcome, counts);

                    if (outcome.Failures > 0)
                    {
                        // Could not write to the picture folder: keep it for next time, without using up an attempt.
                        RetryPolicy.NoteFailure(state.ImapRetry, uid, countsAsAttempt: false);
                    }
                    else
                    {
                        state.ImapRetry.Remove(uid);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A message that drops the connection counts as an attempt too, or it would be retried for ever.
                    bool dropped = !client.IsConnected;
                    counts.Failures++;
                    if (RetryPolicy.NoteFailure(state.ImapRetry, uid, countsAsAttempt: true))
                    {
                        _log.LogError(ex, "Giving up on message {Uid} for {Account} after repeated failures", uid, profile.Name);
                    }
                    else
                    {
                        _log.LogError(ex, "Could not download message {Uid} for {Account}; it will be tried again", uid, profile.Name);
                    }

                    if (dropped)
                    {
                        await ReconnectAsync(ex).ConfigureAwait(false);
                    }
                }
            }

            foreach (IMessageSummary summary in retries)
            {
                ct.ThrowIfCancellationRequested();
                await HandleAsync(summary).ConfigureAwait(false);
            }

            // Save the position now and then, so a big first run that is interrupted resumes instead of starting over.
            var sinceCheckpoint = Stopwatch.StartNew();
            int handledSinceCheckpoint = 0;
            foreach (IMessageSummary summary in fresh)
            {
                ct.ThrowIfCancellationRequested();
                await HandleAsync(summary).ConfigureAwait(false);
                maxSeen = Math.Max(maxSeen, summary.UniqueId.Id);

                if (++handledSinceCheckpoint >= 100 || sinceCheckpoint.Elapsed >= TimeSpan.FromSeconds(5))
                {
                    state.ImapLastUid = maxSeen;
                    _state.Put(profile.Id, state);
                    handledSinceCheckpoint = 0;
                    sinceCheckpoint.Restart();
                }
            }

            if (inbox.UidNext is { } next && next.Id > 0)
            {
                maxSeen = Math.Max(maxSeen, next.Id - 1);
            }

            if (client.IsConnected)
            {
                await client.DisconnectAsync(true, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            // Always move the cursor, even when the run ends early: a message that failed is in the retry list,
            // not a reason to read everything after it again.
            if (maxSeen > state.ImapLastUid)
            {
                state.ImapLastUid = maxSeen;
            }

            foreach (ImapClient c in clients)
            {
                c.Dispose();
            }
        }
    }

    private async Task RunPop3Async(
        AccountProfile profile, MailAuth auth, AccountSyncState state, RunCounts counts, ProgressTracker tracker, CancellationToken ct)
    {
        var clients = new List<Pop3Client>();
        Pop3Client client = null!;
        int reconnects = 0;
        var seen = new HashSet<string>(state.Pop3SeenUids, StringComparer.Ordinal);
        bool completed = false;

        async Task OpenAsync()
        {
            client = new Pop3Client { Timeout = TimeoutMilliseconds };
            clients.Add(client);
            await ConnectAsync(client, profile, auth, ct).ConfigureAwait(false);
        }

        try
        {
            tracker.Phase(SyncPhase.Connecting);
            await OpenAsync().ConfigureAwait(false);

            tracker.Phase(SyncPhase.Listing);
            IList<string> uids = await client.GetMessageUidsAsync(ct).ConfigureAwait(false);
            bool canReadHeadersOnly = client.Capabilities.HasFlag(Pop3Capabilities.Top);

            var onServer = new HashSet<string>(uids, StringComparer.Ordinal);
            int toCheck = uids.Count(u => !seen.Contains(u));
            _log.LogInformation(
                "{Account}: {New} message(s) not seen before, {Retry} earlier failure(s) among them.",
                profile.Name, toCheck, state.Pop3Retry.Count(r => onServer.Contains(r.Key)));

            // The server dropped us. Reconnect and carry on with the next message; message numbers must still line up.
            async Task ReconnectAsync(Exception cause)
            {
                if (++reconnects > MaxReconnects)
                {
                    throw new InvalidOperationException("The connection to the server keeps dropping.", cause);
                }

                _log.LogWarning(cause, "{Account}: connection lost; reconnecting ({Attempt} of {Max})", profile.Name, reconnects, MaxReconnects);
                await OpenAsync().ConfigureAwait(false);
                IList<string> again = await client.GetMessageUidsAsync(ct).ConfigureAwait(false);
                if (again.Count != uids.Count || !again.SequenceEqual(uids, StringComparer.Ordinal))
                {
                    throw new InvalidOperationException("The mailbox changed while it was being checked; it will be checked again next time.", cause);
                }
            }

            var sinceCheckpoint = Stopwatch.StartNew();
            int handledSinceCheckpoint = 0;

            for (int index = 0; index < uids.Count; index++)
            {
                ct.ThrowIfCancellationRequested();

                string uid = uids[index];
                if (seen.Contains(uid))
                {
                    continue;
                }

                counts.Checked++;
                tracker.Tick(SyncPhase.Checking, counts.Checked, toCheck);
                bool handled = true;
                bool countsAsAttempt = false;
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

                    if (SubjectMatcher.IsMatch(profile, subject)
                        && Recipients.Accepts(profile, recipients))
                    {
                        counts.Matched++;
                        tracker.Tick(SyncPhase.Downloading, counts.Checked, toCheck);
                        message ??= await client.GetMessageAsync(index, ct).ConfigureAwait(false);
                        SaveOutcome outcome = _saver.SaveFrom(profile, message);
                        Record(profile, outcome, counts);

                        // Could not write to the picture folder: try this one again next time, without using up an attempt.
                        handled = outcome.Failures == 0;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    bool dropped = !client.IsConnected;
                    handled = false;
                    countsAsAttempt = true;
                    counts.Failures++;
                    _log.LogError(ex, "Could not process POP3 message {Uid} for {Account}", uid, profile.Name);

                    if (dropped)
                    {
                        // Note the attempt first: a message that drops the connection must not be retried for ever.
                        if (RetryPolicy.NoteFailure(state.Pop3Retry, uid, countsAsAttempt))
                        {
                            _log.LogError("Giving up on POP3 message {Uid} for {Account} after repeated failures", uid, profile.Name);
                            seen.Add(uid);
                            state.Pop3Retry.Remove(uid);
                        }

                        await ReconnectAsync(ex).ConfigureAwait(false);
                        continue;
                    }
                }

                if (!handled && RetryPolicy.NoteFailure(state.Pop3Retry, uid, countsAsAttempt))
                {
                    _log.LogError("Giving up on POP3 message {Uid} for {Account} after repeated failures", uid, profile.Name);
                    handled = true;
                }

                if (handled)
                {
                    seen.Add(uid);
                    state.Pop3Retry.Remove(uid);
                }

                if (++handledSinceCheckpoint >= 100 || sinceCheckpoint.Elapsed >= TimeSpan.FromSeconds(5))
                {
                    state.Pop3SeenUids = seen.ToList();
                    _state.Put(profile.Id, state);
                    handledSinceCheckpoint = 0;
                    sinceCheckpoint.Restart();
                }
            }

            // Forget UIDLs that have left the server so the lists cannot grow forever.
            state.Pop3SeenUids = seen.Where(onServer.Contains).ToList();
            foreach (string gone in state.Pop3Retry.Keys.Where(k => !onServer.Contains(k)).ToList())
            {
                state.Pop3Retry.Remove(gone);
            }

            completed = true;
            if (client.IsConnected)
            {
                await client.DisconnectAsync(true, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            if (!completed)
            {
                // The run ended early: keep what was already dealt with.
                state.Pop3SeenUids = seen.ToList();
            }

            foreach (Pop3Client c in clients)
            {
                c.Dispose();
            }
        }
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

    /// <summary>Either a password or a Google access token, whichever the account signs in with.</summary>
    private readonly record struct MailAuth(string? Password, string? AccessToken);

    /// <summary>
    /// Works out how to sign in. Password accounts use the stored (or supplied) password. Google accounts swap the stored
    /// refresh token for a fresh access token; no password is involved.
    /// </summary>
    private async Task<MailAuth> ResolveAuthAsync(
        AccountProfile profile, string? passwordOverride, OAuthSecrets? secretsOverride, CancellationToken ct)
    {
        if (profile.Authentication != AuthMethod.GoogleOAuth)
        {
            string? password = string.IsNullOrEmpty(passwordOverride) ? _credentials.Get(profile.Id) : passwordOverride;
            if (string.IsNullOrEmpty(password))
            {
                throw new InvalidOperationException("No password is stored for this account. Open Settings and enter it.");
            }

            return new MailAuth(password, null);
        }

        string? refreshToken = secretsOverride?.RefreshToken ?? _credentials.Get(OAuthKeys.RefreshToken(profile.Id));
        if (string.IsNullOrEmpty(refreshToken))
        {
            throw new SignInRequiredException(
                "This account uses Google sign-in but is not signed in yet. Open Settings and choose \"Sign in with Google\".");
        }

        // The built-in client needs no stored secret; an account's own client uses the secret saved with the account.
        string? ownSecret = secretsOverride?.ClientSecret ?? _credentials.Get(OAuthKeys.ClientSecret(profile.Id));
        GoogleClientCredentials? client = GoogleClient.Resolve(profile, ownSecret, _builtInClient);
        if (client is null)
        {
            throw new InvalidOperationException(
                "This account uses its own Google OAuth client, but the client ID or secret is missing. Open Settings and enter them.");
        }

        OAuthTokens tokens = await _oauth.RefreshAsync(client.ClientId, client.ClientSecret, refreshToken, ct).ConfigureAwait(false);

        // Google normally keeps the same refresh token; if it ever sends a new one, keep that instead.
        if (secretsOverride is null && !string.IsNullOrEmpty(tokens.RefreshToken) && tokens.RefreshToken != refreshToken)
        {
            _credentials.Set(OAuthKeys.RefreshToken(profile.Id), tokens.RefreshToken);
        }

        return new MailAuth(null, tokens.AccessToken);
    }

    private static async Task ConnectAsync(MailService client, AccountProfile profile, MailAuth auth, CancellationToken ct)
    {
        await client.ConnectAsync(profile.Host.Trim(), profile.Port, ToSocketOptions(profile.Tls), ct).ConfigureAwait(false);

        if (auth.AccessToken is not null)
        {
            await client.AuthenticateAsync(new SaslMechanismOAuth2(profile.UserName, auth.AccessToken), ct).ConfigureAwait(false);
        }
        else
        {
            await client.AuthenticateAsync(profile.UserName, auth.Password!, ct).ConfigureAwait(false);
        }
    }

    private static SecureSocketOptions ToSocketOptions(TlsMode mode) => mode switch
    {
        TlsMode.Implicit => SecureSocketOptions.SslOnConnect,
        TlsMode.StartTls => SecureSocketOptions.StartTls,
        _ => SecureSocketOptions.Auto,
    };

    /// <summary>True when only signing in with Google again can fix the failure (as opposed to a network blip or a bad setting).</summary>
    private static bool NeedsGoogleSignIn(Exception ex, AccountProfile profile) =>
        profile.Authentication == AuthMethod.GoogleOAuth
        && (ex is SignInRequiredException
            or OAuthException { NeedsSignIn: true }
            or MailKit.Security.AuthenticationException);

    private static string Describe(Exception ex, AccountProfile profile) => ex switch
    {
        MailKit.Security.AuthenticationException when profile.Authentication == AuthMethod.GoogleOAuth =>
            "Gmail rejected the Google sign-in. Check that the user name is the Google account you signed in with, "
            + "then sign in with Google again in Settings.",
        MailKit.Security.AuthenticationException =>
            "Sign-in was rejected. Check the user name and password (Gmail needs an app password).",
        SslHandshakeException => "Could not establish a secure connection: " + ex.Message,
        _ => ex.Message,
    };
}

/// <summary>The account has no saved Google sign-in at all.</summary>
internal sealed class SignInRequiredException : InvalidOperationException
{
    public SignInRequiredException(string message) : base(message)
    {
    }
}
