using System.Text;

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
