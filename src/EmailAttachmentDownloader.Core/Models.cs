using System.Text.Json.Serialization;

namespace EmailAttachmentDownloader.Core;

public enum MailProtocol
{
    Imap = 0,
    Pop3 = 1,
}

public enum TlsMode
{
    /// <summary>Let MailKit choose (implicit TLS on the usual ports, STARTTLS otherwise).</summary>
    Auto = 0,

    /// <summary>TLS from the first byte (IMAPS 993 / POP3S 995).</summary>
    Implicit = 1,

    /// <summary>Plain connection upgraded with STARTTLS.</summary>
    StartTls = 2,
}

public enum AuthMethod
{
    /// <summary>A password. For Gmail this must be an app password.</summary>
    Password = 0,

    /// <summary>Sign in with Google (OAuth 2.0). Needs a client ID/secret from Google Cloud; see the README.</summary>
    GoogleOAuth = 1,
}

public enum KeywordMode
{
    /// <summary>The subject must contain at least one keyword.</summary>
    Any = 0,

    /// <summary>The subject must contain every keyword.</summary>
    All = 1,
}

/// <summary>One mailbox plus the rules for what to pull from it and where to put it.</summary>
/// <remarks>The password is deliberately not part of this type; see <see cref="ICredentialStore"/>.</remarks>
public sealed class AccountProfile
{
    public const int MinIntervalMinutes = 5;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New account";
    public bool Enabled { get; set; } = true;

    public MailProtocol Protocol { get; set; } = MailProtocol.Imap;
    public string Host { get; set; } = "imap.gmail.com";
    public int Port { get; set; } = 993;
    public TlsMode Tls { get; set; } = TlsMode.Implicit;
    public string UserName { get; set; } = "";

    /// <summary>How to sign in. The password, client secret and refresh token themselves live in <see cref="ICredentialStore"/>.</summary>
    public AuthMethod Authentication { get; set; } = AuthMethod.Password;

    /// <summary>The Google Cloud "Desktop app" OAuth client ID. Not secret (the client secret and refresh token are).</summary>
    public string OAuthClientId { get; set; } = "";

    /// <summary>
    /// Sign in with <see cref="OAuthClientId"/> and the stored client secret instead of the client built into the app.
    /// Builds without a built-in client always behave as if this were on.
    /// </summary>
    public bool UseOwnOAuthClient { get; set; }

    public string SaveFolder { get; set; } = "";

    /// <summary>Subject keywords. Empty means "every e-mail".</summary>
    public List<string> Keywords { get; set; } = new();
    public KeywordMode KeywordMode { get; set; } = KeywordMode.Any;

    /// <summary>
    /// Whether <see cref="ToAddresses"/> is applied. The list is kept when this is off, so the
    /// user can switch the filter on and off without retyping it.
    /// </summary>
    public bool RecipientFilterEnabled { get; set; }

    /// <summary>Only mail sent (To or Cc) to one of these exact addresses qualifies, when the filter is on.</summary>
    public List<string> ToAddresses { get; set; } = new();

    /// <summary>True when mail is actually being filtered by recipient.</summary>
    [JsonIgnore]
    public bool UsesRecipientFilter => RecipientFilterEnabled && ToAddresses.Any(a => !string.IsNullOrWhiteSpace(a));

    /// <summary>MIME patterns such as <c>image/*</c> or <c>application/pdf</c>.</summary>
    public List<string> AttachmentTypes { get; set; } = new() { "image/*" };

    /// <summary>Skip attachments smaller than this many KB (filters out tracking pixels and icons). 0 = keep all.</summary>
    public int MinSizeKb { get; set; }

    public bool RunOnStartup { get; set; }
    public bool Scheduled { get; set; }
    public int IntervalMinutes { get; set; } = 60;

    public AccountProfile Clone()
    {
        var copy = (AccountProfile)MemberwiseClone();
        copy.Keywords = new List<string>(Keywords);
        copy.ToAddresses = new List<string>(ToAddresses);
        copy.AttachmentTypes = new List<string>(AttachmentTypes);
        return copy;
    }

    public static string DefaultSaveFolder()
    {
        string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrEmpty(pictures))
        {
            pictures = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        return Path.Combine(pictures, "EmailPictures");
    }

    public static bool IsGoogleHost(string? host)
    {
        string h = (host ?? "").Trim().ToLowerInvariant();
        return h == "gmail.com" || h == "googlemail.com" || h.EndsWith(".gmail.com", StringComparison.Ordinal) || h.EndsWith(".googlemail.com", StringComparison.Ordinal);
    }

    /// <summary>Problems that prevent connecting to the server.</summary>
    public IReadOnlyList<string> ValidateConnection() => ValidateConnection(GoogleClient.BuiltIn);

    internal IReadOnlyList<string> ValidateConnection(GoogleClientCredentials? builtInClient)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(Host)) problems.Add("Server host is required.");
        if (Port is < 1 or > 65535) problems.Add("Port must be between 1 and 65535.");
        if (string.IsNullOrWhiteSpace(UserName)) problems.Add("User name is required.");

        if (Authentication == AuthMethod.GoogleOAuth)
        {
            if (GoogleClient.UsesOwnClient(this, builtInClient) && string.IsNullOrWhiteSpace(OAuthClientId))
            {
                problems.Add("OAuth client ID is required when using your own Google client.");
            }

            if (!IsGoogleHost(Host)) problems.Add("Google sign-in only works with Gmail servers (imap.gmail.com or pop.gmail.com).");
        }

        return problems;
    }

    /// <summary>Problems that prevent a full run (connection settings plus the destination folder).</summary>
    public IReadOnlyList<string> Validate() => Validate(GoogleClient.BuiltIn);

    internal IReadOnlyList<string> Validate(GoogleClientCredentials? builtInClient)
    {
        var problems = new List<string>(ValidateConnection(builtInClient));
        if (string.IsNullOrWhiteSpace(Name)) problems.Add("Name is required.");
        if (string.IsNullOrWhiteSpace(SaveFolder))
        {
            problems.Add("Save folder is required.");
        }
        else if (!Path.IsPathRooted(SaveFolder))
        {
            problems.Add("Save folder must be an absolute path.");
        }

        if (AttachmentTypes.Count == 0 || AttachmentTypes.All(string.IsNullOrWhiteSpace))
        {
            problems.Add("At least one attachment type is required (for example image/*).");
        }

        // Checked even while the filter is off, so a saved list is always clean.
        problems.AddRange(Recipients.Problems(ToAddresses));
        if (RecipientFilterEnabled && !ToAddresses.Any(a => !string.IsNullOrWhiteSpace(a)))
        {
            problems.Add("The recipient filter is on but the address list is empty. Add an address or turn the filter off.");
        }

        return problems;
    }
}

/// <summary>The outcome of one sync of one account.</summary>
public sealed record RunResult(
    string AccountId,
    string AccountName,
    DateTimeOffset StartedAt,
    TimeSpan Duration,
    int MessagesChecked,
    int MessagesMatched,
    int NewFiles,
    int Duplicates,
    int Failures,
    string? Error,
    string SaveFolder)
{
    [JsonIgnore]
    public bool Succeeded => Error is null;

    [JsonIgnore]
    public string Summary
    {
        get
        {
            if (Error is not null)
            {
                return $"{AccountName}: {Error}";
            }

            string text = $"{AccountName}: {MessagesMatched} matching e-mail(s), {NewFiles} new file(s)";
            if (Duplicates > 0) text += $", {Duplicates} already saved";
            if (Failures > 0) text += $", {Failures} error(s)";
            return text;
        }
    }
}

public sealed class AttachmentSavedEventArgs : EventArgs
{
    public AttachmentSavedEventArgs(AccountProfile account, LibraryImage image)
    {
        Account = account;
        Image = image;
    }

    public AccountProfile Account { get; }
    public LibraryImage Image { get; }
}
