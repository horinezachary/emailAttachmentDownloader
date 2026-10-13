using System.Text;
using MimeKit;

namespace EmailAttachmentDownloader.Core;

/// <summary>Subject keyword parsing and matching.</summary>
public static class Keywords
{
    private static readonly char[] Separators = { ',', ';', '\r', '\n' };

    /// <summary>
    /// Splits user input on commas, semicolons and line breaks. Spaces are kept, so
    /// <c>order of the arrow</c> is one phrase rather than four words.
    /// </summary>
    public static List<string> Parse(string? text)
    {
        return (text ?? "")
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string Format(IEnumerable<string> keywords) => string.Join(", ", keywords);

    /// <summary>
    /// Case-insensitive substring match. No keywords means everything matches; a missing
    /// subject never matches when keywords exist. One keyword is treated like any other.
    /// </summary>
    public static bool IsMatch(string? subject, IReadOnlyCollection<string> keywords, KeywordMode mode)
    {
        var active = keywords.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).ToList();
        if (active.Count == 0)
        {
            return true;
        }

        if (string.IsNullOrEmpty(subject))
        {
            return false;
        }

        string text = subject;
        bool Has(string keyword) => text.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        return mode == KeywordMode.All ? active.All(Has) : active.Any(Has);
    }
}

/// <summary>Recipient (To/Cc) parsing, validation and matching.</summary>
/// <remarks>
/// Matching is on the whole address, ignoring case. Plus-addresses are exact:
/// <c>you+frames@gmail.com</c> does not match <c>you@gmail.com</c> or the other way round.
/// Gmail ignores dots in the name part and treats googlemail.com as gmail.com, so for those
/// two domains <c>y.o.u@gmail.com</c> matches <c>you@gmail.com</c>. Domain-only entries are not supported.
/// </remarks>
public static class Recipients
{
    private static readonly char[] Separators = { ',', ';', '\r', '\n' };

    /// <summary>Splits user input on commas, semicolons and line breaks, dropping repeats.</summary>
    public static List<string> Parse(string? text)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string entry in (text ?? "").Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (seen.Add(Normalize(entry)))
            {
                result.Add(entry);
            }
        }

        return result;
    }

    public static string Format(IEnumerable<string> addresses) => string.Join(", ", addresses);

    /// <summary>Null when the entry is a plain, valid e-mail address; otherwise a message saying what is wrong.</summary>
    public static string? Problem(string? entry)
    {
        string text = (entry ?? "").Trim();
        if (text.Length == 0)
        {
            return "An address is empty.";
        }

        if (text.StartsWith('@'))
        {
            return $"\"{text}\": whole domains are not supported; enter full addresses such as name@example.com.";
        }

        // Reject "Name <a@b.com>" and similar: only a bare address is allowed.
        if (!MailboxAddress.TryParse(text, out MailboxAddress? mailbox)
            || !mailbox.Address.Equals(text, StringComparison.OrdinalIgnoreCase))
        {
            return $"\"{text}\" is not a valid e-mail address.";
        }

        int at = text.LastIndexOf('@');
        if (at <= 0 || at == text.Length - 1 || !text[(at + 1)..].Contains('.'))
        {
            return $"\"{text}\" is not a valid e-mail address (expected name@domain.com).";
        }

        return null;
    }

    /// <summary>One message per invalid entry; empty when the whole list is fine.</summary>
    public static IReadOnlyList<string> Problems(IEnumerable<string> entries)
    {
        return entries
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(Problem)
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList();
    }

    /// <summary>The canonical form used for comparing addresses.</summary>
    public static string Normalize(string address)
    {
        string text = address.Trim().ToLowerInvariant();
        int at = text.LastIndexOf('@');
        if (at <= 0)
        {
            return text;
        }

        string local = text[..at];
        string domain = text[(at + 1)..];

        if (domain == "googlemail.com")
        {
            domain = "gmail.com";
        }

        if (domain == "gmail.com")
        {
            // Dots only count before the "+tag"; the tag itself stays exact.
            int plus = local.IndexOf('+');
            string name = plus >= 0 ? local[..plus] : local;
            string tag = plus >= 0 ? local[plus..] : "";
            local = name.Replace(".", "") + tag;
        }

        return local + "@" + domain;
    }

    /// <summary>True when any of the message's addresses equals any wanted address.</summary>
    public static bool IsMatch(IEnumerable<string> messageAddresses, IEnumerable<string> wanted)
    {
        var wantedSet = new HashSet<string>(
            wanted.Where(a => !string.IsNullOrWhiteSpace(a)).Select(Normalize),
            StringComparer.Ordinal);
        if (wantedSet.Count == 0)
        {
            return false;
        }

        return messageAddresses
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Any(a => wantedSet.Contains(Normalize(a)));
    }

    /// <summary>True when the profile has no active recipient filter, or the message passes it.</summary>
    public static bool Accepts(AccountProfile profile, IEnumerable<string> messageAddresses)
    {
        return !profile.UsesRecipientFilter || IsMatch(messageAddresses, profile.ToAddresses);
    }

    /// <summary>The To and Cc addresses of a message. Groups are flattened.</summary>
    public static List<string> From(InternetAddressList? to, InternetAddressList? cc)
    {
        var result = new List<string>();
        if (to is not null) result.AddRange(to.Mailboxes.Select(m => m.Address));
        if (cc is not null) result.AddRange(cc.Mailboxes.Select(m => m.Address));
        return result;
    }

    /// <summary>Same as <see cref="From"/>, for raw header text (POP3 TOP). Unparseable headers give no addresses.</summary>
    public static List<string> FromHeaders(string? toHeader, string? ccHeader)
    {
        InternetAddressList? to = null;
        InternetAddressList? cc = null;
        if (!string.IsNullOrWhiteSpace(toHeader)) InternetAddressList.TryParse(toHeader, out to);
        if (!string.IsNullOrWhiteSpace(ccHeader)) InternetAddressList.TryParse(ccHeader, out cc);
        return From(to, cc);
    }
}

/// <summary>Decides which MIME parts count as attachments worth saving.</summary>
public static class AttachmentFilter
{
    public static List<string> ParsePatterns(string? text) => Keywords.Parse(text);

    /// <summary>Patterns: <c>*</c>, <c>*/*</c>, <c>image/*</c>, or an exact type such as <c>application/pdf</c>.</summary>
    public static bool Matches(string mediaType, string mediaSubtype, IEnumerable<string> patterns)
    {
        string full = $"{mediaType}/{mediaSubtype}";
        foreach (string raw in patterns)
        {
            string pattern = raw.Trim();
            if (pattern.Length == 0) continue;
            if (pattern is "*" or "*/*") return true;
            if (pattern.EndsWith("/*", StringComparison.Ordinal)
                && mediaType.Equals(pattern[..^2], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (pattern.Equals(full, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    public static string ExtensionFor(string mediaType, string mediaSubtype)
    {
        string full = $"{mediaType}/{mediaSubtype}".ToLowerInvariant();
        return full switch
        {
            "image/jpeg" or "image/jpg" or "image/pjpeg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/bmp" => ".bmp",
            "image/webp" => ".webp",
            "image/tiff" => ".tif",
            "image/heic" => ".heic",
            "image/heif" => ".heif",
            "image/svg+xml" => ".svg",
            "application/pdf" => ".pdf",
            _ when mediaType.Equals("image", StringComparison.OrdinalIgnoreCase)
                   && mediaSubtype.Length is > 0 and <= 5
                   && mediaSubtype.All(char.IsLetterOrDigit) => "." + mediaSubtype.ToLowerInvariant(),
            _ => ".bin",
        };
    }
}

/// <summary>Turns attacker-controlled attachment names into safe file names.</summary>
public static class FileNames
{
    private const int MaxStemLength = 120;
    private const int MaxExtensionLength = 16;

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    // Windows-invalid characters are replaced on every OS so a folder synced from Linux stays valid on Windows.
    private static readonly char[] InvalidChars = { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };

    /// <summary>
    /// Returns a bare file name (no directories) that is safe on Windows. Directory parts such as
    /// <c>..\..\x.png</c> are dropped, invalid characters become underscores, reserved device names
    /// are prefixed, and a missing stem or extension is filled from the fallbacks.
    /// </summary>
    public static string Sanitize(string? name, string fallbackStem, string fallbackExtension)
    {
        string candidate = name ?? "";

        int slash = candidate.LastIndexOfAny(new[] { '/', '\\' });
        if (slash >= 0)
        {
            candidate = candidate[(slash + 1)..];
        }

        var builder = new StringBuilder(candidate.Length);
        foreach (char ch in candidate)
        {
            builder.Append(ch < 32 || Array.IndexOf(InvalidChars, ch) >= 0 ? '_' : ch);
        }

        string cleaned = builder.ToString().Trim().TrimEnd('.', ' ');

        string extension = Path.GetExtension(cleaned);
        string stem = Path.GetFileNameWithoutExtension(cleaned);

        if (string.IsNullOrWhiteSpace(stem))
        {
            stem = fallbackStem;
        }

        if (extension.Length == 0)
        {
            extension = fallbackExtension.Length == 0 || fallbackExtension.StartsWith('.')
                ? fallbackExtension
                : "." + fallbackExtension;
        }

        if (extension.Length > MaxExtensionLength)
        {
            extension = extension[..MaxExtensionLength];
        }

        if (stem.Length > MaxStemLength)
        {
            stem = stem[..MaxStemLength];
        }

        stem = stem.TrimEnd('.', ' ');
        if (stem.Length == 0)
        {
            stem = fallbackStem;
        }

        // Windows reserves "CON", "CON.txt", "con.tar.gz", ... so look at the part before the first dot.
        int firstDot = stem.IndexOf('.');
        string head = firstDot >= 0 ? stem[..firstDot] : stem;
        if (ReservedDeviceNames.Contains(head))
        {
            stem = "_" + stem;
        }

        return stem + extension;
    }
}
