using System.Text.Json;

namespace EmailAttachmentDownloader.Core;

/// <summary>One saved attachment, as recorded in the library index.</summary>
public sealed record LibraryImage(
    string Sha256,
    string FilePath,
    string FileName,
    string AccountId,
    string? Subject,
    string? From,
    DateTimeOffset? ReceivedAt,
    DateTimeOffset SavedAt,
    long SizeBytes);

internal sealed class LibraryFile
{
    public int Version { get; set; } = 1;
    public List<LibraryImage> Images { get; set; } = new();
}

/// <summary>
/// Index of everything the app has saved: content hash, file path and where it came from.
/// It is the source of truth for de-duplication, and it is the read-side hook for anything that
/// wants to display the pictures later (a custom slideshow, a .scr screensaver, a curated
/// lock-screen folder) without touching mail code. Readers in other processes can open the same
/// file; writes are atomic.
/// </summary>
public sealed class ImageLibrary
{
    private readonly string _path;
    private readonly object _gate = new();
    private Dictionary<string, LibraryImage> _images = new(StringComparer.Ordinal);
    private bool _dirty;

    public ImageLibrary(string path)
    {
        _path = path;
        Reload();
    }

    /// <summary>Re-reads the index from disk, picking up changes made by another process.</summary>
    public void Reload()
    {
        lock (_gate)
        {
            _images = Read(_path);
            _dirty = false;
        }
    }

    public bool Contains(string sha256)
    {
        lock (_gate)
        {
            return _images.ContainsKey(sha256);
        }
    }

    public void Add(LibraryImage image)
    {
        lock (_gate)
        {
            _images[image.Sha256] = image;
            _dirty = true;
        }
    }

    /// <summary>Writes pending additions to disk. Cheap when nothing changed.</summary>
    public void Flush()
    {
        lock (_gate)
        {
            if (!_dirty)
            {
                return;
            }

            var file = new LibraryFile { Images = _images.Values.OrderBy(i => i.SavedAt).ToList() };
            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(file, Json.Options));
            _dirty = false;
        }
    }

    /// <summary>Every recorded image, newest first (received date when known, otherwise saved date).</summary>
    public IReadOnlyList<LibraryImage> All()
    {
        lock (_gate)
        {
            return _images.Values.OrderByDescending(i => i.ReceivedAt ?? i.SavedAt).ToList();
        }
    }

    /// <summary>Like <see cref="All"/>, but only images whose file is still on disk.</summary>
    public IReadOnlyList<LibraryImage> AllExisting() => All().Where(i => File.Exists(i.FilePath)).ToList();

    private static Dictionary<string, LibraryImage> Read(string path)
    {
        var result = new Dictionary<string, LibraryImage>(StringComparer.Ordinal);
        if (!File.Exists(path))
        {
            return result;
        }

        try
        {
            var file = JsonSerializer.Deserialize<LibraryFile>(File.ReadAllText(path), Json.Options);
            foreach (LibraryImage image in file?.Images ?? new List<LibraryImage>())
            {
                result[image.Sha256] = image;
            }
        }
        catch (JsonException)
        {
            // Keep the unreadable file for inspection and start a fresh index. Images on disk are untouched.
            AtomicFile.QuarantineCorrupt(path);
            result.Clear();
        }

        return result;
    }
}
