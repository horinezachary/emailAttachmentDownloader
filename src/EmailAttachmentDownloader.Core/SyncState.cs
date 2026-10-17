using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EmailAttachmentDownloader.Core;

/// <summary>What has already been looked at for one account, so each run only touches new mail.</summary>
public sealed class AccountSyncState
{
    /// <summary>Hash of the settings that decide which mail qualifies; a change resets the cursor.</summary>
    public string FilterSignature { get; set; } = "";

    public uint ImapUidValidity { get; set; }

    /// <summary>Every IMAP message up to this UID has been dealt with (or is in <see cref="ImapRetry"/>).</summary>
    public uint ImapLastUid { get; set; }

    /// <summary>
    /// IMAP messages at or below the cursor that could not be downloaded or saved, with how many times they have failed.
    /// They are tried again on later runs without re-reading the rest of the mailbox.
    /// </summary>
    public Dictionary<uint, int> ImapRetry { get; set; } = new();

    /// <summary>POP3 UIDLs already handled and still on the server.</summary>
    public List<string> Pop3SeenUids { get; set; } = new();

    /// <summary>POP3 messages that failed, with how many times (they are not in <see cref="Pop3SeenUids"/> until they succeed).</summary>
    public Dictionary<string, int> Pop3Retry { get; set; } = new();

    public DateTimeOffset? LastRunAt { get; set; }

    public AccountSyncState Clone()
    {
        var copy = (AccountSyncState)MemberwiseClone();
        copy.Pop3SeenUids = new List<string>(Pop3SeenUids);
        copy.ImapRetry = new Dictionary<uint, int>(ImapRetry);
        copy.Pop3Retry = new Dictionary<string, int>(Pop3Retry);
        return copy;
    }
}

/// <summary>
/// Decides when to stop retrying one message. Without a limit, a single message that can never be downloaded would be
/// retried forever; with the per-message list it costs one download attempt per run instead of a rescan of the mailbox.
/// </summary>
internal static class RetryPolicy
{
    public const int MaxAttempts = 5;

    /// <summary>
    /// Notes that <paramref name="key"/> failed. A failure that is the machine's fault (the picture folder is not writable, the
    /// disk is full) does not use up one of the <see cref="MaxAttempts"/>, so nothing is lost while that is being fixed.
    /// </summary>
    /// <returns>True when the message has now failed too often and should be given up on (it is removed from the list).</returns>
    public static bool NoteFailure<TKey>(Dictionary<TKey, int> retry, TKey key, bool countsAsAttempt) where TKey : notnull
    {
        retry.TryGetValue(key, out int attempts);
        if (countsAsAttempt)
        {
            attempts++;
        }

        if (attempts >= MaxAttempts)
        {
            retry.Remove(key);
            return true;
        }

        retry[key] = attempts;
        return false;
    }
}

internal sealed class SyncStateFile
{
    public int Version { get; set; } = 1;
    public Dictionary<string, AccountSyncState> Accounts { get; set; } = new();
}

/// <summary>
/// Settings that change which messages qualify. If any of them change, mail that was skipped
/// earlier may now match, so the saved cursor is thrown away. Duplicate saves are prevented by
/// content hashes, so re-scanning is safe.
/// </summary>
internal static class FilterSignature
{
    public static string Compute(AccountProfile profile)
    {
        var lines = new List<string>
        {
            profile.Protocol.ToString(),
            profile.Host.Trim().ToLowerInvariant(),
            profile.UserName.Trim().ToLowerInvariant(),
            profile.KeywordMode.ToString(),
            "min:" + profile.MinSizeKb,
        };

        lines.AddRange(profile.Keywords
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => "k:" + k.Trim().ToLowerInvariant())
            .OrderBy(k => k, StringComparer.Ordinal));

        // Only present while the filter is in use, so accounts that never use it keep their old signature
        // (and do not rescan after upgrading).
        if (profile.UsesRecipientFilter)
        {
            lines.AddRange(profile.ToAddresses
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Select(a => "to:" + Recipients.Normalize(a))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(a => a, StringComparer.Ordinal));
        }

        // Only present for phone share accounts, so ordinary mail accounts keep their old signature.
        if (profile.IsPhoneShare)
        {
            lines.Add("kind:phoneshare");
            lines.Add("tag:" + profile.ShareTag.Trim().ToLowerInvariant());
        }

        lines.AddRange(profile.AttachmentTypes
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => "t:" + t.Trim().ToLowerInvariant())
            .OrderBy(t => t, StringComparer.Ordinal));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines))));
    }
}

/// <summary>Persists <see cref="AccountSyncState"/> per account in a small JSON file.</summary>
public sealed class SyncStateStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private Dictionary<string, AccountSyncState> _accounts = new();

    public SyncStateStore(string path)
    {
        _path = path;
        Reload();
    }

    public void Reload()
    {
        lock (_gate)
        {
            _accounts = Read();
        }
    }

    /// <summary>A private copy; changes only take effect through <see cref="Put"/>.</summary>
    public AccountSyncState Snapshot(string accountId)
    {
        lock (_gate)
        {
            return _accounts.TryGetValue(accountId, out AccountSyncState? state) ? state.Clone() : new AccountSyncState();
        }
    }

    public DateTimeOffset? LastRunAt(string accountId)
    {
        lock (_gate)
        {
            return _accounts.TryGetValue(accountId, out AccountSyncState? state) ? state.LastRunAt : null;
        }
    }

    public void Put(string accountId, AccountSyncState state)
    {
        lock (_gate)
        {
            _accounts[accountId] = state.Clone();
            Write();
        }
    }

    /// <summary>
    /// Notes that a run was attempted, keeping everything else. Used when a run fails before it can touch the mailbox, so the
    /// schedule waits its interval instead of trying again at the next 30-second tick.
    /// </summary>
    public void RecordRun(string accountId, DateTimeOffset startedAt)
    {
        lock (_gate)
        {
            if (!_accounts.TryGetValue(accountId, out AccountSyncState? state))
            {
                state = new AccountSyncState();
                _accounts[accountId] = state;
            }

            state.LastRunAt = startedAt;
            Write();
        }
    }

    public void Remove(string accountId)
    {
        lock (_gate)
        {
            if (_accounts.Remove(accountId))
            {
                Write();
            }
        }
    }

    private Dictionary<string, AccountSyncState> Read()
    {
        if (!File.Exists(_path))
        {
            return new Dictionary<string, AccountSyncState>();
        }

        try
        {
            var file = JsonSerializer.Deserialize<SyncStateFile>(File.ReadAllText(_path), Json.Options);
            return file?.Accounts ?? new Dictionary<string, AccountSyncState>();
        }
        catch (JsonException)
        {
            // Losing the cursor only means one extra scan; hashes prevent duplicates.
            AtomicFile.QuarantineCorrupt(_path);
            return new Dictionary<string, AccountSyncState>();
        }
    }

    private void Write()
    {
        var file = new SyncStateFile { Accounts = _accounts };
        AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(file, Json.Options));
    }
}
