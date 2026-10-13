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
    public uint ImapLastUid { get; set; }

    /// <summary>POP3 UIDLs already handled and still on the server.</summary>
    public List<string> Pop3SeenUids { get; set; } = new();

    public DateTimeOffset? LastRunAt { get; set; }

    public AccountSyncState Clone()
    {
        var copy = (AccountSyncState)MemberwiseClone();
        copy.Pop3SeenUids = new List<string>(Pop3SeenUids);
        return copy;
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
