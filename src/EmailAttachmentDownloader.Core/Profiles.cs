using System.Text.Json;

namespace EmailAttachmentDownloader.Core;

internal sealed class ProfilesFile
{
    public int Version { get; set; } = 1;
    public List<AccountProfile> Profiles { get; set; } = new();
}

/// <summary>Reads and writes <c>profiles.json</c>. Passwords are never stored here.</summary>
public sealed class ProfileStore
{
    private readonly string _path;

    public ProfileStore(string path)
    {
        _path = path;
    }

    /// <summary>Set when the file could not be read and was moved aside.</summary>
    public string? LoadWarning { get; private set; }

    public List<AccountProfile> Load()
    {
        LoadWarning = null;
        if (!File.Exists(_path))
        {
            return new List<AccountProfile>();
        }

        try
        {
            var file = JsonSerializer.Deserialize<ProfilesFile>(File.ReadAllText(_path), Json.Options);
            return file?.Profiles ?? new List<AccountProfile>();
        }
        catch (JsonException ex)
        {
            string? moved = AtomicFile.QuarantineCorrupt(_path);
            LoadWarning = $"profiles.json could not be read ({ex.Message}). "
                          + (moved is null ? "" : $"It was kept as {Path.GetFileName(moved)}.");
            return new List<AccountProfile>();
        }
    }

    public void Save(IEnumerable<AccountProfile> profiles)
    {
        var file = new ProfilesFile { Profiles = profiles.ToList() };
        AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(file, Json.Options));
    }
}

/// <summary>
/// The live, thread-safe list of profiles shared by the UI and the scheduler. Everything it hands
/// out is a copy, so the UI can edit freely and only <see cref="Replace"/> changes what runs.
/// </summary>
public sealed class ProfileCatalog
{
    private readonly ProfileStore _store;
    private readonly object _gate = new();
    private List<AccountProfile> _profiles;

    public ProfileCatalog(ProfileStore store)
    {
        _store = store;
        _profiles = store.Load();
    }

    public IReadOnlyList<AccountProfile> Snapshot()
    {
        lock (_gate)
        {
            return _profiles.Select(p => p.Clone()).ToList();
        }
    }

    public AccountProfile? Find(string id)
    {
        lock (_gate)
        {
            return _profiles.FirstOrDefault(p => p.Id == id)?.Clone();
        }
    }

    public void Replace(IEnumerable<AccountProfile> profiles)
    {
        lock (_gate)
        {
            _profiles = profiles.Select(p => p.Clone()).ToList();
            _store.Save(_profiles);
        }
    }
}
