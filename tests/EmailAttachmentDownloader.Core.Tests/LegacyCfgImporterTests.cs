using System.Text;

namespace EmailAttachmentDownloader.Core.Tests;

public class LegacyCfgImporterTests
{
    private static string Encode(string data, bool lineBreaks = false) =>
        Convert.ToBase64String(
            Encoding.UTF8.GetBytes(data),
            lineBreaks ? Base64FormattingOptions.InsertLineBreaks : Base64FormattingOptions.None);

    [Fact]
    public void Parse_maps_every_field()
    {
        ImportedProfile result = LegacyCfgImporter.Parse(
            "C:\\Pics;me@example.com;secret;pop.example.com;pop3;sunset,trail;true;true;2;3600000;", "old");

        AccountProfile p = result.Profile;
        Assert.Equal("old", p.Name);
        Assert.Equal("C:\\Pics", p.SaveFolder);
        Assert.Equal("me@example.com", p.UserName);
        Assert.Equal("secret", result.Password);
        Assert.Equal("pop.example.com", p.Host);
        Assert.Equal(MailProtocol.Pop3, p.Protocol);
        Assert.Equal(995, p.Port);
        Assert.Equal(TlsMode.Implicit, p.Tls);
        Assert.Equal(new[] { "sunset", "trail" }, p.Keywords);
        Assert.Equal(KeywordMode.Any, p.KeywordMode);
        Assert.True(p.RunOnStartup);
        Assert.True(p.Scheduled);
        Assert.Equal(120, p.IntervalMinutes); // 2 x 3,600,000 ms
    }

    [Fact]
    public void A_null_folder_and_empty_keywords_import_cleanly_and_short_intervals_are_clamped()
    {
        ImportedProfile result = LegacyCfgImporter.Parse("null;u;p;h;pop3;;false;false;1;1000;", "x");

        Assert.Equal("", result.Profile.SaveFolder);
        Assert.Empty(result.Profile.Keywords);
        Assert.False(result.Profile.RunOnStartup);
        Assert.False(result.Profile.Scheduled);
        Assert.Equal(AccountProfile.MinIntervalMinutes, result.Profile.IntervalMinutes);
    }

    [Fact]
    public void Too_few_fields_is_a_format_error()
    {
        Assert.Throws<FormatException>(() => LegacyCfgImporter.Parse("a;b", "x"));
    }

    [Fact]
    public void Import_reads_a_base64_file_with_line_breaks()
    {
        using var dir = new TempDir();
        string path = dir.File("old.cfg");
        File.WriteAllText(
            path,
            Encode("C:\\Pics;me@example.com;secret;pop.example.com;pop3;sunset,trail;true;true;2;3600000;", lineBreaks: true));

        ImportedProfile result = LegacyCfgImporter.Import(path);

        Assert.Equal("old", result.Profile.Name);
        Assert.Equal("secret", result.Password);
        Assert.Equal("pop.example.com", result.Profile.Host);
    }

    [Fact]
    public void Import_rejects_files_that_are_not_base64()
    {
        using var dir = new TempDir();
        string path = dir.File("bad.cfg");
        File.WriteAllText(path, "this is not base64!!");

        Assert.Throws<FormatException>(() => LegacyCfgImporter.Import(path));
    }

    private static string WriteCfg(TempDir dir, string fileName, string data)
    {
        string path = dir.File(fileName);
        File.WriteAllText(path, Encode(data), Encoding.ASCII);
        return path;
    }

    [Fact]
    public void ImportFiles_reads_several_files_and_defaults_a_blank_folder()
    {
        using var dir = new TempDir();
        string a = WriteCfg(dir, "frames.cfg", "C:\\Pics;a@example.com;pw1;pop.example.com;pop3;;false;false;1;60000;");
        string b = WriteCfg(dir, "garden.cfg", "null;b@example.com;pw2;imap.example.com;imap;rose;true;true;1;3600000;");

        IReadOnlyList<LegacyImportOutcome> outcomes = LegacyCfgImporter.ImportFiles(new[] { a, b }, Array.Empty<AccountProfile>());

        Assert.Equal(2, outcomes.Count);
        Assert.All(outcomes, o => Assert.Null(o.SkippedBecause));
        Assert.Equal("frames", outcomes[0].Item!.Profile.Name);
        Assert.Equal("pw1", outcomes[0].Item!.Password);
        Assert.Equal("garden", outcomes[1].Item!.Profile.Name);
        Assert.Equal(AccountProfile.DefaultSaveFolder(), outcomes[1].Item!.Profile.SaveFolder);
    }

    [Fact]
    public void ImportFiles_skips_names_that_already_exist_including_earlier_files_in_the_batch()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(dir.File("one"));
        Directory.CreateDirectory(dir.File("two"));
        string first = WriteCfg(dir, "Frames.cfg", "C:\\P;a@example.com;pw;h;pop3;;false;false;1;60000;");
        string sameNameInBatch = WriteCfg(dir, Path.Combine("two", "frames.cfg"), "C:\\P;a@example.com;pw;h;pop3;;false;false;1;60000;");
        string alreadyThere = WriteCfg(dir, "garden.cfg", "C:\\P;a@example.com;pw;h;pop3;;false;false;1;60000;");

        IReadOnlyList<LegacyImportOutcome> outcomes = LegacyCfgImporter.ImportFiles(
            new[] { first, sameNameInBatch, alreadyThere }, new[] { new AccountProfile { Name = "GARDEN" } });

        Assert.NotNull(outcomes[0].Item);
        Assert.Null(outcomes[1].Item);
        Assert.Contains("already exists", outcomes[1].SkippedBecause, StringComparison.Ordinal);
        Assert.Null(outcomes[2].Item);
        Assert.Contains("already exists", outcomes[2].SkippedBecause, StringComparison.Ordinal);
    }

    [Fact]
    public void ImportFiles_reports_unreadable_files_and_carries_on()
    {
        using var dir = new TempDir();
        string junk = dir.File("junk.cfg");
        File.WriteAllText(junk, "this is not base64 !!!");
        string good = WriteCfg(dir, "good.cfg", "C:\\P;a@example.com;pw;h;pop3;;false;false;1;60000;");
        string missing = dir.File("missing.cfg");

        IReadOnlyList<LegacyImportOutcome> outcomes = LegacyCfgImporter.ImportFiles(new[] { junk, missing, good }, Array.Empty<AccountProfile>());

        Assert.Null(outcomes[0].Item);
        Assert.Contains("not a valid old-format", outcomes[0].SkippedBecause, StringComparison.Ordinal);
        Assert.Null(outcomes[1].Item);
        Assert.NotNull(outcomes[1].SkippedBecause);
        Assert.Equal("good", outcomes[2].Item!.Profile.Name);
    }
}
