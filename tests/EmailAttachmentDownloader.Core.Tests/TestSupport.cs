namespace EmailAttachmentDownloader.Core.Tests;

/// <summary>A scratch folder that deletes itself.</summary>
internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ead-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string File(string name) => System.IO.Path.Combine(Root, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
