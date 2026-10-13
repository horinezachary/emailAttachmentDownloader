using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EmailAttachmentDownloader.Core;

/// <summary>Shared JSON settings for every file the app writes.</summary>
internal static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Compact single-line options for JSON-lines files.</summary>
    public static readonly JsonSerializerOptions Compact = new(Options) { WriteIndented = false };
}

/// <summary>Writes a file so readers never see a half-written version.</summary>
internal static class AtomicFile
{
    public static void WriteAllText(string path, string contents)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string temp = path + ".tmp";
        File.WriteAllText(temp, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Moves an unreadable file aside so it is not silently overwritten.</summary>
    public static string? QuarantineCorrupt(string path)
    {
        try
        {
            string target = $"{path}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}";
            File.Move(path, target);
            return target;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
