namespace EmailAttachmentDownloader.Core;

/// <summary>Where the app keeps its own data (settings, index, state, logs). Not where images go.</summary>
public sealed class AppPaths
{
    public AppPaths(string? dataDirectory = null)
    {
        DataDirectory = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EmailAttachmentDownloader");
        Directory.CreateDirectory(DataDirectory);
    }

    public string DataDirectory { get; }

    public string ProfilesFile => Path.Combine(DataDirectory, "profiles.json");
    public string LibraryFile => Path.Combine(DataDirectory, "library.json");
    public string SyncStateFile => Path.Combine(DataDirectory, "sync-state.json");
    public string HistoryFile => Path.Combine(DataDirectory, "history.jsonl");
    public string CredentialsFile => Path.Combine(DataDirectory, "credentials.json");
    public string LockFile => Path.Combine(DataDirectory, "sync.lock");
    public string UpdateFile => Path.Combine(DataDirectory, "update.json");
    public string LogFile => Path.Combine(DataDirectory, "logs", "app.log");
}
