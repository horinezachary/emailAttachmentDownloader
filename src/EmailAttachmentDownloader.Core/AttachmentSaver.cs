using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace EmailAttachmentDownloader.Core;

internal sealed class SaveOutcome
{
    public List<LibraryImage> Saved { get; } = new();
    public int Duplicates { get; set; }
    public int Failures { get; set; }
}

/// <summary>
/// Walks a message's MIME tree (including forwarded messages) and writes matching attachments to
/// the account's folder. Duplicates are detected by SHA-256 of the content, never by file name.
/// </summary>
internal sealed class AttachmentSaver
{
    private const int MaxNesting = 20;

    private readonly ImageLibrary _library;
    private readonly ILogger _log;

    public AttachmentSaver(ImageLibrary library, ILogger log)
    {
        _library = library;
        _log = log;
    }

    public SaveOutcome SaveFrom(AccountProfile profile, MimeMessage message)
    {
        var outcome = new SaveOutcome();
        Walk(profile, message, message.Body, outcome, depth: 0);
        return outcome;
    }

    private void Walk(AccountProfile profile, MimeMessage context, MimeEntity? entity, SaveOutcome outcome, int depth)
    {
        if (entity is null || depth > MaxNesting)
        {
            return;
        }

        switch (entity)
        {
            case Multipart multipart:
                foreach (MimeEntity child in multipart)
                {
                    Walk(profile, context, child, outcome, depth + 1);
                }

                break;

            case MessagePart messagePart:
                if (messagePart.Message is { } inner)
                {
                    Walk(profile, inner, inner.Body, outcome, depth + 1);
                }

                break;

            case MimePart part:
                try
                {
                    SavePart(profile, context, part, outcome);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    outcome.Failures++;
                    _log.LogError(ex, "Could not save an attachment from \"{Subject}\"", context.Subject);
                }

                break;
        }
    }

    private void SavePart(AccountProfile profile, MimeMessage context, MimePart part, SaveOutcome outcome)
    {
        ContentType type = part.ContentType;
        if (!AttachmentFilter.Matches(type.MediaType, type.MediaSubtype, profile.AttachmentTypes) || part.Content is null)
        {
            return;
        }

        using var buffer = new MemoryStream();
        part.Content.DecodeTo(buffer);
        byte[] data = buffer.ToArray();

        if (data.Length == 0 || data.Length < (long)profile.MinSizeKb * 1024)
        {
            return;
        }

        string sha256 = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        if (_library.Contains(sha256))
        {
            outcome.Duplicates++;
            return;
        }

        string fileName = FileNames.Sanitize(
            part.FileName,
            "attachment-" + sha256[..8],
            AttachmentFilter.ExtensionFor(type.MediaType, type.MediaSubtype));

        string path = WriteUnique(profile.SaveFolder, fileName, data);

        DateTimeOffset? received = context.Date == DateTimeOffset.MinValue ? null : context.Date;
        if (received is { } receivedAt)
        {
            try
            {
                // Explorer sorts by date, so make the file's timestamp the e-mail's date.
                File.SetLastWriteTimeUtc(path, receivedAt.UtcDateTime);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Cosmetic only.
            }
        }

        var image = new LibraryImage(
            sha256,
            path,
            Path.GetFileName(path),
            profile.Id,
            context.Subject,
            context.From?.ToString(),
            received,
            DateTimeOffset.Now,
            data.Length);

        _library.Add(image);
        outcome.Saved.Add(image);
    }

    /// <summary>
    /// Creates the file with <see cref="FileMode.CreateNew"/> so an existing file is never
    /// overwritten; a name clash with different content becomes "name (1).ext", "name (2).ext", ...
    /// </summary>
    internal static string WriteUnique(string folder, string fileName, byte[] data)
    {
        string stem = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);

        for (int attempt = 0; attempt < 10_000; attempt++)
        {
            string name = attempt == 0 ? fileName : $"{stem} ({attempt}){extension}";
            string candidate = Path.Combine(folder, name);
            try
            {
                using var stream = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                stream.Write(data, 0, data.Length);
                return candidate;
            }
            catch (IOException) when (File.Exists(candidate))
            {
                // Taken (possibly a moment ago by another process): try the next number.
            }
        }

        throw new IOException($"Could not find a free file name for \"{fileName}\" in {folder}.");
    }
}
