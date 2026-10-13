using System.Text;

namespace EmailAttachmentDownloader.Core;

/// <summary>A profile read from an old Java-era <c>.cfg</c> file, plus the password it contained.</summary>
public sealed record ImportedProfile(AccountProfile Profile, string Password);

/// <summary>
/// Reads the old app's settings files so nobody has to retype them. The old format was a base64
/// string (which is encoding, not encryption) holding ten semicolon-separated fields:
/// folder; account; password; host; store type; keywords; on startup; scheduled; schedule; multiplier-in-ms.
/// </summary>
public static class LegacyCfgImporter
{
    private const int FieldCount = 10;

    public static ImportedProfile Import(string path)
    {
        string encoded = File.ReadAllText(path, Encoding.ASCII);
        string data;
        try
        {
            data = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        }
        catch (FormatException ex)
        {
            throw new FormatException($"{Path.GetFileName(path)} is not a valid old-format settings file.", ex);
        }

        return Parse(data, Path.GetFileNameWithoutExtension(path));
    }

    public static ImportedProfile Parse(string data, string name)
    {
        string[] fields = data.Split(';');
        if (fields.Length < FieldCount)
        {
            throw new FormatException(
                $"Expected {FieldCount} fields but found {fields.Length}; this does not look like an old settings file.");
        }

        string saveFolder = fields[0] == "null" ? "" : fields[0];

        // The old app only spoke POP3, almost always over implicit TLS on 995.
        bool isPop3 = !fields[4].Equals("imap", StringComparison.OrdinalIgnoreCase);

        long.TryParse(fields[8], out long amount);
        long.TryParse(fields[9], out long unitMilliseconds);
        long minutes = amount * unitMilliseconds / 60_000;
        int interval = (int)Math.Clamp(minutes, AccountProfile.MinIntervalMinutes, 7 * 24 * 60);

        var profile = new AccountProfile
        {
            Name = name,
            Protocol = isPop3 ? MailProtocol.Pop3 : MailProtocol.Imap,
            Host = fields[3],
            Port = isPop3 ? 995 : 993,
            Tls = TlsMode.Implicit,
            UserName = fields[1],
            SaveFolder = saveFolder,
            Keywords = Keywords.Parse(fields[5]),
            KeywordMode = KeywordMode.Any,
            RunOnStartup = fields[6] == "true",
            Scheduled = fields[7] == "true",
            IntervalMinutes = interval,
        };

        return new ImportedProfile(profile, fields[2]);
    }
}
