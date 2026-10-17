using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace EmailAttachmentDownloader.Core;

/// <summary>What an account is for. Old <c>profiles.json</c> files have no kind and load as <see cref="Mail"/>.</summary>
public enum AccountKind
{
    /// <summary>An ordinary mailbox: mail is picked by subject keywords and the optional recipient filter.</summary>
    Mail = 0,

    /// <summary>
    /// A mailbox that the iPhone companion app sends photos to. Only mail whose subject carries this account's
    /// share tag is taken, so nothing else in the mailbox is touched.
    /// </summary>
    PhoneShare = 1,
}

/// <summary>The random tag that ties an iPhone to one Phone share account.</summary>
/// <remarks>
/// The phone puts <c>[ead:TAG]</c> in the subject of every message it sends. The tag is not a password (anyone who knows
/// it and the address can mail pictures in), so it is long enough not to be guessed and can be replaced at any time,
/// which stops every phone that was paired with the old one.
/// </remarks>
public static class ShareTags
{
    public const int Length = 10;

    private static readonly Regex Pattern = new("^[a-z0-9]{6,32}$", RegexOptions.CultureInvariant);

    /// <summary>Ten lowercase hex characters from a cryptographic random source.</summary>
    public static string NewTag()
    {
        Span<byte> bytes = stackalloc byte[Length / 2];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>Lowercase letters and digits, 6 to 32 of them. The phone app applies the same rule.</summary>
    public static bool IsValid(string? tag) => tag is not null && Pattern.IsMatch(tag);

    /// <summary>The text the phone puts in the subject, for example <c>[ead:a1b2c3d4e5]</c>.</summary>
    public static string MarkerFor(string tag) => $"[ead:{tag}]";
}

/// <summary>Decides whether a message subject qualifies for an account, whatever its kind.</summary>
public static class SubjectMatcher
{
    /// <summary>
    /// Mail accounts use the subject keywords. Phone share accounts need the account's marker in the subject and
    /// ignore keywords, so a hidden keyword list left over from a mail account cannot hide a phone's photos.
    /// </summary>
    public static bool IsMatch(AccountProfile profile, string? subject)
    {
        if (!profile.IsPhoneShare)
        {
            return Keywords.IsMatch(subject, profile.Keywords, profile.KeywordMode);
        }

        return !string.IsNullOrEmpty(subject)
               && ShareTags.IsValid(profile.ShareTag)
               && subject.Contains(profile.SubjectMarker, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>What the phone needs to know about a Phone share account.</summary>
/// <param name="Name">The account's name, shown in the phone app.</param>
/// <param name="ToAddress">The address the phone mails photos to.</param>
/// <param name="Tag">The share tag.</param>
public sealed record PairingInfo(string Name, string ToAddress, string Tag);

/// <summary>
/// The link (also shown as a QR code) that pairs a phone with an account: <c>ead://pair?v=1&amp;name=...&amp;to=...&amp;tag=...</c>.
/// It holds no passwords. The phone sends through its own mail account, which the user enters in the phone app.
/// </summary>
public static class PairingLink
{
    public const int Version = 1;
    public const string Scheme = "ead";

    public static string Build(PairingInfo info) => Build(info.Name, info.ToAddress, info.Tag);

    public static string Build(string name, string toAddress, string tag) =>
        $"{Scheme}://pair?v={Version}"
        + $"&name={Uri.EscapeDataString(name)}"
        + $"&to={Uri.EscapeDataString(toAddress)}"
        + $"&tag={Uri.EscapeDataString(tag)}";

    /// <summary>Null when the text is not a usable pairing link (wrong scheme, newer version, missing or invalid fields).</summary>
    public static PairingInfo? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)
            || !Uri.TryCreate(text.Trim(), UriKind.Absolute, out Uri? uri)
            || !uri.Scheme.Equals(Scheme, StringComparison.OrdinalIgnoreCase)
            || !uri.Host.Equals("pair", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = part.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            fields[part[..equals]] = Uri.UnescapeDataString(part[(equals + 1)..]);
        }

        if (!fields.TryGetValue("v", out string? version) || !int.TryParse(version, out int v) || v < 1 || v > Version)
        {
            return null;
        }

        fields.TryGetValue("name", out string? name);
        fields.TryGetValue("to", out string? to);
        fields.TryGetValue("tag", out string? tag);

        if (string.IsNullOrWhiteSpace(name)
            || Recipients.Problem(to) is not null
            || !ShareTags.IsValid(tag))
        {
            return null;
        }

        return new PairingInfo(name.Trim(), to!.Trim(), tag!);
    }
}
