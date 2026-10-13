using System.Text.Json;

namespace EmailAttachmentDownloader.Core;

/// <summary>
/// Rolling record of recent runs (replaces the old base64 <c>.messages</c> file). One JSON object
/// per line, capped at <see cref="MaxEntries"/> so it never grows without bound.
/// </summary>
public sealed class RunHistory
{
    public const int MaxEntries = 500;

    private readonly string _path;
    private readonly object _gate = new();

    public RunHistory(string path)
    {
        _path = path;
    }

    public void Append(RunResult result)
    {
        lock (_gate)
        {
            var lines = ReadLines();
            lines.Add(JsonSerializer.Serialize(result, Json.Compact));
            if (lines.Count > MaxEntries)
            {
                lines = lines.GetRange(lines.Count - MaxEntries, MaxEntries);
            }

            AtomicFile.WriteAllText(_path, string.Join(Environment.NewLine, lines) + Environment.NewLine);
        }
    }

    /// <summary>Most recent first.</summary>
    public IReadOnlyList<RunResult> Recent(int count)
    {
        lock (_gate)
        {
            var results = new List<RunResult>();
            foreach (string line in Enumerable.Reverse(ReadLines()))
            {
                if (results.Count >= count)
                {
                    break;
                }

                try
                {
                    RunResult? result = JsonSerializer.Deserialize<RunResult>(line, Json.Compact);
                    if (result is not null)
                    {
                        results.Add(result);
                    }
                }
                catch (JsonException)
                {
                    // Skip a damaged line rather than hiding the rest of the history.
                }
            }

            return results;
        }
    }

    private List<string> ReadLines()
    {
        if (!File.Exists(_path))
        {
            return new List<string>();
        }

        return File.ReadAllLines(_path).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
    }
}
