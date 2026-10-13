using System.Collections.Concurrent;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EmailAttachmentDownloader.Core;

/// <summary>Holds mail passwords, keyed by <see cref="AccountProfile.Id"/>. Never written to profiles.json.</summary>
public interface ICredentialStore
{
    string? Get(string accountId);
    void Set(string accountId, string password);
    void Remove(string accountId);
}

public static class CredentialStores
{
    public static ICredentialStore CreateDefault(AppPaths paths)
    {
        return OperatingSystem.IsWindows()
            ? new DpapiCredentialStore(paths.CredentialsFile)
            : new EnvironmentCredentialStore();
    }
}

/// <summary>Test double and fallback that keeps passwords in memory only.</summary>
public sealed class MemoryCredentialStore : ICredentialStore
{
    private readonly ConcurrentDictionary<string, string> _passwords = new();

    public string? Get(string accountId) => _passwords.TryGetValue(accountId, out string? value) ? value : null;

    public void Set(string accountId, string password) => _passwords[accountId] = password;

    public void Remove(string accountId) => _passwords.TryRemove(accountId, out _);
}

/// <summary>
/// Non-Windows fallback: read-only, taking each secret from <c>EAD_PASSWORD_&lt;KEY&gt;</c> (key upper-cased, other characters as underscores).
/// Intended for headless testing, not for everyday use.
/// </summary>
public sealed class EnvironmentCredentialStore : ICredentialStore
{
    public static string VariableName(string accountId) =>
        "EAD_PASSWORD_" + new string(accountId.ToUpperInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray());

    public string? Get(string accountId) => Environment.GetEnvironmentVariable(VariableName(accountId));

    public void Set(string accountId, string password) =>
        throw new NotSupportedException(
            $"Passwords cannot be stored on this operating system. Set the environment variable {VariableName(accountId)} instead.");

    public void Remove(string accountId)
    {
    }
}

internal sealed class CredentialsFile
{
    public int Version { get; set; } = 1;

    /// <summary>Account id to base64 of the DPAPI-protected UTF-8 password.</summary>
    public Dictionary<string, string> Secrets { get; set; } = new();
}

/// <summary>
/// Windows DPAPI (current-user scope). The file holds only ciphertext that can be decrypted by the
/// same Windows user on the same machine, which is a real improvement on the old base64 "encoding".
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiCredentialStore : ICredentialStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("EmailAttachmentDownloader/v1");

    private readonly string _path;
    private readonly object _gate = new();
    private Dictionary<string, string> _secrets;

    public DpapiCredentialStore(string path)
    {
        _path = path;
        _secrets = Read();
    }

    public string? Get(string accountId)
    {
        lock (_gate)
        {
            if (!_secrets.TryGetValue(accountId, out string? encoded))
            {
                return null;
            }

            try
            {
                byte[] protectedBytes = Convert.FromBase64String(encoded);
                byte[] plain = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException)
            {
                // Copied from another user or machine: unreadable here, so ask for the password again.
                return null;
            }
        }
    }

    public void Set(string accountId, string password)
    {
        lock (_gate)
        {
            byte[] protectedBytes = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(password), Entropy, DataProtectionScope.CurrentUser);
            _secrets[accountId] = Convert.ToBase64String(protectedBytes);
            Write();
        }
    }

    public void Remove(string accountId)
    {
        lock (_gate)
        {
            if (_secrets.Remove(accountId))
            {
                Write();
            }
        }
    }

    private Dictionary<string, string> Read()
    {
        if (!File.Exists(_path))
        {
            return new Dictionary<string, string>();
        }

        try
        {
            var file = JsonSerializer.Deserialize<CredentialsFile>(File.ReadAllText(_path), Json.Options);
            return file?.Secrets ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            AtomicFile.QuarantineCorrupt(_path);
            return new Dictionary<string, string>();
        }
    }

    private void Write()
    {
        var file = new CredentialsFile { Secrets = _secrets };
        AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(file, Json.Options));
    }
}
