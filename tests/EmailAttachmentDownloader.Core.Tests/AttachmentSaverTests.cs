using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;

namespace EmailAttachmentDownloader.Core.Tests;

public sealed class AttachmentSaverTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly ImageLibrary _library;
    private readonly AttachmentSaver _saver;
    private readonly AccountProfile _profile;

    public AttachmentSaverTests()
    {
        Directory.CreateDirectory(Folder);
        _library = new ImageLibrary(_dir.File("library.json"));
        _saver = new AttachmentSaver(_library, NullLogger.Instance);
        _profile = new AccountProfile { Id = "acct", SaveFolder = Folder };
    }

    private string Folder => _dir.File("pictures");

    public void Dispose() => _dir.Dispose();

    private static byte[] Bytes(int seed, int length = 4096)
    {
        var data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }

    /// <summary>A normal message with attachments added the way a mail client would.</summary>
    private static MimeMessage Message(string subject, params (string Name, byte[] Data, string Type)[] attachments)
    {
        var builder = new BodyBuilder { TextBody = "hello" };
        foreach ((string name, byte[] data, string type) in attachments)
        {
            builder.Attachments.Add(name, data, ContentType.Parse(type));
        }

        return new MimeMessage { Subject = subject, Body = builder.ToMessageBody() };
    }

    /// <summary>A single raw part, so hostile or missing file names reach the saver unchanged.</summary>
    private static MimeMessage RawMessage(string mediaType, string subtype, byte[] data, string? fileName)
    {
        var part = new MimePart(mediaType, subtype)
        {
            Content = new MimeContent(new MemoryStream(data)),
            ContentTransferEncoding = ContentEncoding.Base64,
        };

        if (fileName is not null)
        {
            part.FileName = fileName;
        }

        return new MimeMessage { Subject = "raw", Body = part };
    }

    [Fact]
    public void An_image_attachment_is_saved_and_indexed()
    {
        byte[] data = Bytes(1);

        SaveOutcome outcome = _saver.SaveFrom(_profile, Message("Photos", ("pic.png", data, "image/png")));

        LibraryImage saved = Assert.Single(outcome.Saved);
        Assert.Equal(Path.Combine(Folder, "pic.png"), saved.FilePath);
        Assert.Equal(data, File.ReadAllBytes(saved.FilePath));
        Assert.Equal("Photos", saved.Subject);
        Assert.Equal("acct", saved.AccountId);
        Assert.True(_library.Contains(saved.Sha256));
        Assert.Equal(0, outcome.Duplicates);
    }

    [Fact]
    public void The_same_content_in_another_message_is_a_duplicate_even_with_a_different_name()
    {
        byte[] data = Bytes(2);
        _saver.SaveFrom(_profile, Message("first", ("pic.png", data, "image/png")));

        SaveOutcome second = _saver.SaveFrom(_profile, Message("second", ("renamed.png", data, "image/png")));

        Assert.Empty(second.Saved);
        Assert.Equal(1, second.Duplicates);
        Assert.Single(Directory.GetFiles(Folder));
    }

    [Fact]
    public void The_same_name_with_different_content_gets_a_numbered_name()
    {
        _saver.SaveFrom(_profile, Message("one", ("pic.png", Bytes(3), "image/png")));
        _saver.SaveFrom(_profile, Message("two", ("pic.png", Bytes(4), "image/png")));
        _saver.SaveFrom(_profile, Message("three", ("pic.png", Bytes(5), "image/png")));

        var names = Directory.GetFiles(Folder)
            .Select(file => Path.GetFileName(file))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(new[] { "pic (1).png", "pic (2).png", "pic.png" }, names);
    }

    [Fact]
    public void A_hostile_file_name_cannot_escape_the_save_folder()
    {
        SaveOutcome outcome = _saver.SaveFrom(_profile, RawMessage("image", "png", Bytes(6), "..\\..\\evil.png"));

        LibraryImage saved = Assert.Single(outcome.Saved);
        Assert.Equal(Path.Combine(Folder, "evil.png"), saved.FilePath);
        Assert.False(File.Exists(_dir.File("evil.png")));
        Assert.Single(Directory.GetFiles(Folder));
    }

    [Fact]
    public void A_missing_file_name_falls_back_to_a_name_built_from_the_content()
    {
        SaveOutcome outcome = _saver.SaveFrom(_profile, RawMessage("image", "png", Bytes(7), fileName: null));

        string name = Path.GetFileName(Assert.Single(outcome.Saved).FilePath);
        Assert.StartsWith("attachment-", name);
        Assert.EndsWith(".png", name);
    }

    [Fact]
    public void Only_the_configured_types_are_saved()
    {
        byte[] pdf = Bytes(8);

        SaveOutcome imagesOnly = _saver.SaveFrom(_profile, RawMessage("application", "pdf", pdf, "doc.pdf"));
        Assert.Empty(imagesOnly.Saved);

        _profile.AttachmentTypes = new List<string> { "image/*", "application/pdf" };
        SaveOutcome withPdf = _saver.SaveFrom(_profile, RawMessage("application", "pdf", pdf, "doc.pdf"));
        Assert.Equal("doc.pdf", Path.GetFileName(Assert.Single(withPdf.Saved).FilePath));
    }

    [Fact]
    public void Tiny_attachments_can_be_skipped_with_a_minimum_size()
    {
        _profile.MinSizeKb = 2;

        SaveOutcome tiny = _saver.SaveFrom(_profile, RawMessage("image", "png", Bytes(9, length: 1024), "dot.png"));
        SaveOutcome big = _saver.SaveFrom(_profile, RawMessage("image", "png", Bytes(10, length: 4096), "photo.png"));

        Assert.Empty(tiny.Saved);
        Assert.Single(big.Saved);
    }

    [Fact]
    public void Empty_attachments_are_ignored()
    {
        SaveOutcome outcome = _saver.SaveFrom(_profile, RawMessage("image", "png", Array.Empty<byte>(), "empty.png"));

        Assert.Empty(outcome.Saved);
        Assert.Empty(Directory.GetFiles(Folder));
    }

    [Fact]
    public void Attachments_inside_a_forwarded_message_are_found()
    {
        MimeMessage inner = Message("inner subject", ("fwd.png", Bytes(11), "image/png"));
        var outer = new MimeMessage
        {
            Subject = "FW: pictures",
            Body = new Multipart("mixed")
            {
                new TextPart("plain") { Text = "see below" },
                new MessagePart { Message = inner },
            },
        };

        SaveOutcome outcome = _saver.SaveFrom(_profile, outer);

        LibraryImage saved = Assert.Single(outcome.Saved);
        Assert.Equal("fwd.png", saved.FileName);
        Assert.Equal("inner subject", saved.Subject);
    }

    [Fact]
    public void The_file_timestamp_and_index_use_the_date_of_the_e_mail()
    {
        var sent = new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.Zero);
        MimeMessage message = Message("dated", ("dated.png", Bytes(12), "image/png"));
        message.Date = sent;

        LibraryImage saved = Assert.Single(_saver.SaveFrom(_profile, message).Saved);

        Assert.Equal(sent, saved.ReceivedAt);
        Assert.Equal(sent.UtcDateTime, File.GetLastWriteTimeUtc(saved.FilePath));
    }

    [Fact]
    public void WriteUnique_never_overwrites_an_existing_file()
    {
        string first = AttachmentSaver.WriteUnique(Folder, "a.txt", new byte[] { 1 });
        string second = AttachmentSaver.WriteUnique(Folder, "a.txt", new byte[] { 2 });

        Assert.Equal(Path.Combine(Folder, "a.txt"), first);
        Assert.Equal(Path.Combine(Folder, "a (1).txt"), second);
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(first));
        Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(second));
    }
}
